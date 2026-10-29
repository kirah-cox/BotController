namespace Bot.UI.Game;

// The shapes of the JSON the NetRTS server sends back. Only the fields this bot uses are listed;
// docs/bot-guide.md in the NetRTS repo (or /scalar on the server) lists them all.

public record RegisteredPlayer(string PlayerId, string Name, string ApiKey);

public record MatchInfo(Guid MatchId, string? Name);

public record GameState(
    int Tick,
    string Status,
    int MaxTicks,
    int MapWidth,
    int MapHeight,
    Me You,
    List<PlayerInfo> Players,
    List<Unit> Units,
    List<Building> Buildings,
    List<Ore> Resources);

public record Me(int Slot, int Resources, List<string>? Upgrades, List<ResearchItem>? Researching);

public record ResearchItem(string Upgrade, int Percent, int BuildingId);

public record PlayerInfo(int Slot, string Name, bool Eliminated, Spot StartPosition, Score? Score);

public record Unit(int Id, int Owner, string Type, int X, int Y, string Activity);

public record Building(int Id, int Owner, string Type, int X, int Y, bool Completed, List<ProductionItem>? Production);

public record ProductionItem(string UnitType, int Percent);

public record Ore(int Id, int X, int Y);

public record Spot(int X, int Y);

public record Score(int Total);

public record MatchResult(List<ResultPlayer> Players);

public record ResultPlayer(string Name, bool Winner, Score Score);

public record ApiError(string Code, string Message);
