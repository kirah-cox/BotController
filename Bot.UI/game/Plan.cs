namespace Bot.UI.Game;

// The settings the web page can change one at a time, even in the middle of a match.
// The defaults are the starter bot's: 8 workers, soldiers, attack with 5.
// BuildX / BuildY are the map tile where buildings go; Builders is how many workers build each one.
public record Plan(int Workers = 8, string ArmyUnit = "Soldier", int AttackSize = 5, string BuildingUnit = "Barracks", int BuildX = 0, int BuildY = 0, int Builders = 1)
{
    // What the Barracks can train and what each costs.
    public static readonly IReadOnlyDictionary<string, int> ArmyCosts =
        new Dictionary<string, int> { ["Soldier"] = 100, ["Archer"] = 125, ["Scout"] = 75 };

    public int ArmyCost => ArmyCosts[ArmyUnit];


    public static readonly IReadOnlyDictionary<string, int> BuildingCosts =
        new Dictionary<string, int> { ["Barracks"] = 150, ["ResourceDepot"] = 100, ["TechLab"] = 150, ["GuardTower"] = 125 };

    public int BuildingCost => BuildingCosts[BuildingUnit];
}
