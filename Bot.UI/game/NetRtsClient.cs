using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Options;

namespace Bot.UI.Game;

public class NetRtsOptions
{
    public string Server { get; set; } = "https://netrts.snowse.io";
}

// Thrown when the NetRTS server answers with an error, e.g. NAME_TAKEN or MATCH_FULL.
public class NetRtsException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

// A thin wrapper around the NetRTS HTTP API (see docs/bot-guide.md in the NetRTS repo).
public class NetRtsClient
{
    private readonly HttpClient http;

    public NetRtsClient(IOptions<NetRtsOptions> options)
    {
        Server = options.Value.Server.TrimEnd('/');
        // State requests long-poll for up to ~30 seconds, so the timeout has to be longer than that.
        http = new HttpClient { BaseAddress = new Uri(Server), Timeout = TimeSpan.FromMinutes(1) };
    }

    public string Server { get; }

    public void UseApiKey(string apiKey) =>
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

    public async Task<RegisteredPlayer> RegisterAsync(string name, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync("/api/v1/players", new { name }, ct);
        return await ReadAsync<RegisteredPlayer>(response, ct);
    }

    // Starts a match against house bots ("sitter", "economist", "balanced", "rusher"),
    // or an empty named match for a classmate to join when houseBots is empty.
    public async Task<MatchInfo> CreateMatchAsync(string? name, string[] houseBots, int tickIntervalMs, CancellationToken ct = default)
    {
        var body = new { name, houseBots, settings = new { tickIntervalMs } };
        var response = await http.PostAsJsonAsync("/api/v1/matches", body, ct);
        return await ReadAsync<MatchInfo>(response, ct);
    }

    // Joins a match by its id or its name (league matches like "season-week3" work the same way).
    public async Task<MatchInfo> JoinMatchAsync(string idOrName, CancellationToken ct = default)
    {
        if (!Guid.TryParse(idOrName, out var matchId))
        {
            var lookup = await http.GetAsync($"/api/v1/matches/named/{Uri.EscapeDataString(idOrName)}", ct);
            matchId = (await ReadAsync<MatchInfo>(lookup, ct)).MatchId;
        }

        var response = await http.PostAsync($"/api/v1/matches/{matchId}/join", null, ct);
        return await ReadAsync<MatchInfo>(response, ct);
    }

    // Waits until the server has played tick `waitForTick`, then returns what we can see.
    // Returns null if the match hasn't started yet (ask again to keep waiting).
    public async Task<GameState?> GetStateAsync(Guid matchId, int waitForTick, CancellationToken ct = default)
    {
        var response = await http.GetAsync($"/api/v1/matches/{matchId}/state?waitForTick={waitForTick}", ct);
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            var error = await response.Content.ReadFromJsonAsync<ApiError>(ct);
            if (error?.Code == "MATCH_NOT_STARTED")
            {
                return null;
            }

            throw new NetRtsException(error?.Code ?? "CONFLICT", error?.Message ?? "Conflict");
        }

        return await ReadAsync<GameState>(response, ct);
    }

    public async Task SendCommandsAsync(Guid matchId, List<object> commands, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync($"/api/v1/matches/{matchId}/commands", new { commands }, ct);
        await EnsureSuccessAsync(response, ct);
    }

    public async Task SurrenderAsync(Guid matchId, CancellationToken ct = default)
    {
        var response = await http.PostAsync($"/api/v1/matches/{matchId}/surrender", null, ct);
        await EnsureSuccessAsync(response, ct);
    }

    public async Task<MatchResult> GetResultAsync(Guid matchId, CancellationToken ct = default)
    {
        var response = await http.GetAsync($"/api/v1/matches/{matchId}/result", ct);
        return await ReadAsync<MatchResult>(response, ct);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        await EnsureSuccessAsync(response, ct);
        return (await response.Content.ReadFromJsonAsync<T>(ct))!;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        // Every NetRTS error has the body { "code": "...", "message": "..." }.
        ApiError? error = null;
        try
        {
            error = await response.Content.ReadFromJsonAsync<ApiError>(ct);
        }
        catch (Exception)
        {
            // Not JSON (e.g. a proxy error page); fall back to the status code below.
        }

        throw new NetRtsException(
            error?.Code ?? response.StatusCode.ToString(),
            error?.Message ?? $"The game server answered {(int)response.StatusCode} {response.ReasonPhrase}.");
    }
}
