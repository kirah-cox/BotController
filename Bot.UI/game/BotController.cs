using System.Text.Json;

namespace Bot.UI.Game;

public record StartRequest(string? Opponent, string? CreateMatch, string? JoinMatch, int? TickMs);

// Any field left out keeps its current value.
public record StrategyRequest(int? Workers, string? ArmyUnit, int? AttackSize);

public record BotStatus(
    string Server,
    bool Registered,
    string? PlayerName,
    string Phase,
    Guid? MatchId,
    string? WatchUrl,
    int? Tick,
    int? MaxTicks,
    int? Ore,
    int? Units,
    int? Buildings,
    Plan Strategy,
    IEnumerable<string> ArmyUnits,
    IReadOnlyList<string> Log);

// Runs one bot at a time: signs up with the NetRTS server, gets into a match and plays it in the
// background, while the web page reads its status and starts or stops it.
public class BotController(NetRtsClient client, IHostEnvironment env, ILogger<BotController> logger)
{
    private record SavedKey(string Name, string ApiKey);

    private readonly Lock gate = new();
    private readonly LinkedList<string> log = new();
    // One key per server (e.g. netrts-key.netrts.snowse.io.json, netrts-key.localhost-5080.json),
    // since a bot registered on one server doesn't exist on another.
    private readonly string keyFile = Path.Combine(env.ContentRootPath,
        $"netrts-key.{new Uri(client.Server).Authority.Replace(':', '-')}.json");

    private SavedKey? key;
    private bool keyLoaded;
    private CancellationTokenSource? playing;
    private string phase = "Idle";
    private MatchInfo? match;
    private GameState? state;
    private Plan plan = new();

    public BotStatus GetStatus()
    {
        lock (gate)
        {
            var saved = LoadKey();
            var watchName = match?.Name ?? match?.MatchId.ToString();
            return new BotStatus(
                client.Server,
                saved != null,
                saved?.Name,
                phase,
                match?.MatchId,
                watchName == null ? null : $"{client.Server}/#/match/{watchName}",
                state?.Tick,
                state?.MaxTicks,
                state?.You.Resources,
                state == null ? null : Strategy.MyUnits(state).Count,
                state?.Buildings.Count(b => b.Owner == state.You.Slot),
                plan,
                Plan.ArmyCosts.Keys,
                log.ToList());
        }
    }

    // Signs up once. The API key is shown only once, so it's saved to netrts-key.json and reused.
    public async Task RegisterAsync(string name)
    {
        if (LoadKey() != null)
        {
            throw new InvalidOperationException($"A bot is already registered. Delete {Path.GetFileName(keyFile)} to register a new one.");
        }

        var player = await client.RegisterAsync(name);
        var saved = new SavedKey(player.Name, player.ApiKey);
        await File.WriteAllTextAsync(keyFile, JsonSerializer.Serialize(saved));
        lock (gate)
        {
            key = saved;
            client.UseApiKey(saved.ApiKey);
        }

        Log($"Registered as {player.Name}.");
    }

    // Changes parts of the plan the bot follows; takes effect on the next tick, even mid-match.
    public void UpdateStrategy(StrategyRequest request)
    {
        if (request.Workers is < 1 or > 50)
        {
            throw new InvalidOperationException("Workers must be between 1 and 50.");
        }

        if (request.AttackSize is < 1 or > 50)
        {
            throw new InvalidOperationException("Attack size must be between 1 and 50.");
        }

        if (request.ArmyUnit != null && !Plan.ArmyCosts.ContainsKey(request.ArmyUnit))
        {
            throw new InvalidOperationException($"Unknown army unit '{request.ArmyUnit}'.");
        }

        Plan updated;
        lock (gate)
        {
            plan = updated = new Plan(
                request.Workers ?? plan.Workers,
                request.ArmyUnit ?? plan.ArmyUnit,
                request.AttackSize ?? plan.AttackSize);
        }

        Log($"Strategy: {updated.Workers} workers, {updated.ArmyUnit}s, attack with {updated.AttackSize}.");
    }

    // Gets into a match (vs a house bot, a new named match, or an existing one) and starts playing it.
    public async Task StartAsync(StartRequest request)
    {
        CancellationToken token;
        lock (gate)
        {
            if (LoadKey() == null)
            {
                throw new InvalidOperationException("Register a bot first.");
            }

            if (playing != null)
            {
                throw new InvalidOperationException("The bot is already in a match. Stop it first.");
            }

            playing = new CancellationTokenSource();
            token = playing.Token;
            phase = "Joining";
            match = null;
            state = null;
        }

        try
        {
            var tickMs = request.TickMs ?? 1000;
            MatchInfo joined;
            if (!string.IsNullOrWhiteSpace(request.JoinMatch))
            {
                joined = await client.JoinMatchAsync(request.JoinMatch.Trim());
            }
            else if (!string.IsNullOrWhiteSpace(request.CreateMatch))
            {
                joined = await client.CreateMatchAsync(request.CreateMatch.Trim(), [], tickMs);
            }
            else
            {
                joined = await client.CreateMatchAsync(null, [request.Opponent ?? "sitter"], tickMs);
            }

            lock (gate)
            {
                match = joined;
            }

            Log($"In match {joined.Name ?? joined.MatchId.ToString()}.");
        }
        catch (Exception)
        {
            lock (gate)
            {
                playing = null;
                phase = "Idle";
            }

            throw;
        }

        _ = Task.Run(() => PlayAsync(match!.MatchId, token));
    }

    // Surrenders the current match (if it has started) and stops the bot.
    public async Task StopAsync()
    {
        CancellationTokenSource? running;
        Guid? matchId;
        lock (gate)
        {
            running = playing;
            matchId = match?.MatchId;
        }

        if (running == null)
        {
            return;
        }

        await running.CancelAsync();
        if (matchId != null)
        {
            try
            {
                await client.SurrenderAsync(matchId.Value);
                Log("Surrendered.");
            }
            catch (NetRtsException e)
            {
                // e.g. the match already ended, or we never got past the waiting room.
                Log($"Could not surrender: {e.Message}");
            }
        }
    }

    // The bot loop: wait for the match to start, then every tick decide and send commands.
    private async Task PlayAsync(Guid matchId, CancellationToken ct)
    {
        try
        {
            SetPhase("Waiting for players");
            GameState? current = null;
            while (current == null)
            {
                current = await client.GetStateAsync(matchId, 0, ct);
            }

            SetPhase("Playing");
            while (current.Status != "Completed")
            {
                Plan currentPlan;
                lock (gate)
                {
                    state = current;
                    currentPlan = plan;
                }

                var commands = Strategy.Decide(current, currentPlan);
                if (commands.Count > 0)
                {
                    await client.SendCommandsAsync(matchId, commands, ct);
                }

                current = (await client.GetStateAsync(matchId, current.Tick + 1, ct))!;
            }

            lock (gate)
            {
                state = current;
            }

            var result = await client.GetResultAsync(matchId, ct);
            foreach (var p in result.Players)
            {
                Log($"{p.Name}: {p.Score.Total} points" + (p.Winner ? "  <-- WINNER" : ""));
            }

            SetPhase("Finished");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            SetPhase("Stopped");
        }
        catch (Exception e)
        {
            logger.LogError(e, "Bot loop failed");
            Log($"Error: {e.Message}");
            SetPhase("Error");
        }
        finally
        {
            lock (gate)
            {
                playing = null;
            }
        }
    }

    private SavedKey? LoadKey()
    {
        if (!keyLoaded)
        {
            keyLoaded = true;
            if (File.Exists(keyFile))
            {
                key = JsonSerializer.Deserialize<SavedKey>(File.ReadAllText(keyFile));
                if (key != null)
                {
                    client.UseApiKey(key.ApiKey);
                }
            }
        }

        return key;
    }

    private void SetPhase(string value)
    {
        lock (gate)
        {
            phase = value;
        }

        Log(value);
    }

    private void Log(string message)
    {
        lock (gate)
        {
            log.AddFirst($"{DateTime.Now:HH:mm:ss}  {message}");
            while (log.Count > 30)
            {
                log.RemoveLast();
            }
        }
    }
}
