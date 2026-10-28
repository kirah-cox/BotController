namespace Bot.UI.Game;

// The settings the web page can change one at a time, even in the middle of a match.
// The defaults are the starter bot's: 8 workers, soldiers, attack with 5.
public record Plan(int Workers = 8, string ArmyUnit = "Soldier", int AttackSize = 5)
{
    // What the Barracks can train and what each costs.
    public static readonly IReadOnlyDictionary<string, int> ArmyCosts =
        new Dictionary<string, int> { ["Soldier"] = 100, ["Archer"] = 125, ["Scout"] = 75 };

    public int ArmyCost => ArmyCosts[ArmyUnit];
}
