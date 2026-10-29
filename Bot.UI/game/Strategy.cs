namespace Bot.UI.Game;

// Your strategy goes here. Every tick the bot calls Decide with what it can see and sends back
// the commands it returns. Every command, unit and building is described in the bot guide:
// https://github.com/SnowSE/NetRTS/blob/main/docs/bot-guide.md
//
// This is the NetRTS starter bot's plan: gather, train 8 workers, build a Barracks, train
// soldiers, attack with 5. It beats "sitter" and sometimes "economist".
// The numbers come from `plan` (see Plan.cs), which the web page's buttons change.
public static class Strategy
{
    public static List<object> Decide(GameState s, Plan plan)
    {
        var commands = new List<object>();
        var ore = s.You.Resources;  // what we can still spend this tick
        if (MyBuilding(s, "CommandCenter") == null)
        {
            return commands;  // we lost our headquarters :(
        }

        var gather = GatherOre(s);
        if (gather != null)
        {
            commands.Add(gather);
        }

        var worker = TrainWorker(s, plan, ore);
        if (worker != null)
        {
            commands.Add(worker);
            ore -= 50;
        }

        var beginningBuilding = BuildBeginningBuildings(s, plan, ore);
        if (beginningBuilding != null)
        {
            commands.Add(beginningBuilding);
            ore -= plan.BuildingCost;
        }

        var advancedBuilding = BuildAdvancedBuildings(s, plan, ore);
        if (advancedBuilding != null)
        {
            commands.Add(advancedBuilding);
            ore -= plan.BuildingCost;
        }

        commands.AddRange(ResearchUpgrades(s, ref ore));

        commands.AddRange(TrainAttackers(s, plan, ref ore));

        var attack = Attack(s, plan);
        if (attack != null)
        {
            commands.Add(attack);
        }

        return commands;
    }

    // 1. Idle workers dig ore at the deposit closest to our headquarters.
    private static object? GatherOre(GameState s)
    {
        var hq = MyBuilding(s, "CommandCenter")!;
        var closest = s.Resources.OrderBy(r => Math.Abs(r.X - hq.X) + Math.Abs(r.Y - hq.Y)).FirstOrDefault();
        if (closest == null)
        {
            return null;
        }

        return new { type = "Gather", units = "idle", unitType = "Worker", targetId = closest.Id };
    }

    // 2. Train workers until we have plan.Workers (a Worker costs 50 ore).
    private static object? TrainWorker(GameState s, Plan plan, int ore)
    {
        var hq = MyBuilding(s, "CommandCenter")!;
        if (MyUnits(s, "Worker").Count >= plan.Workers || hq.Production is { Count: > 0 } || ore < 50)
        {
            return null;
        }

        return new { type = "Produce", buildingId = hq.Id, unitType = "Worker" };
    }

    // 3. Then build one Barracks (150 ore), 4 tiles from the headquarters toward the middle of the map.
    private static object? BuildBeginningBuildings(GameState s, Plan plan, int ore)
    {
        var workers = MyUnits(s, "Worker");
        if (workers.Count < plan.Workers || ore < plan.BuildingCost|| plan.BuildingUnit == "TechLab" || plan.BuildingUnit == "GuardTower" || s.Buildings.Any(b => b.X == plan.BuildX && b.Y == plan.BuildY))
        {
            return null;
        }

        var hq = MyBuilding(s, "CommandCenter")!;
        var x = plan.BuildX;
        var y = plan.BuildY;
        return new { type = "Build", unitIds = workers.Take(plan.Builders).Select(w => w.Id).ToArray(), buildingType = plan.BuildingUnit, x, y };
    }

    private static object? BuildAdvancedBuildings(GameState s, Plan plan, int ore)
    {
        var workers = MyUnits(s, "Worker");
        var barracks = MyBuilding(s, "Barracks");
        if (workers.Count < plan.Workers || ore < plan.BuildingCost || plan.BuildingUnit == "Barracks" || plan.BuildingUnit == "ResourceDepot" || barracks == null || !barracks.Completed || s.Buildings.Any(b => b.X == plan.BuildX && b.Y == plan.BuildY))
        {
            return null;
        }

        var hq = MyBuilding(s, "CommandCenter")!;
        var x = plan.BuildX;
        var y = plan.BuildY;
        return new { type = "Build", unitIds = workers.Take(plan.Builders).Select(w => w.Id).ToArray(), buildingType = plan.BuildingUnit, x, y };
    }

    // 4. Every finished Barracks trains plan.ArmyUnit non-stop, 3 at a time (a Soldier costs 100 ore),
    //    as long as there's ore for them.
    private static List<object> TrainAttackers(GameState s, Plan plan, ref int ore)
    {
        const int count = 3;
        var commands = new List<object>();
        var allBarracks = s.Buildings.Where(b => b.Owner == s.You.Slot && b.Type == "Barracks" && b.Completed);
        foreach (var barracks in allBarracks)
        {
            if (ore < plan.ArmyCost * count)
            {
                break;
            }

            commands.Add(new { type = "Produce", buildingId = barracks.Id, unitType = plan.ArmyUnit, count });
            ore -= plan.ArmyCost * count;
        }

        return commands;
    }

    // The order the TechLabs research upgrades in, and the upgrade each one needs finished first.
    private static readonly (string Name, int Cost, string? Requires)[] ResearchOrder =
    [
        ("Harvesting1", 100, null), ("Weapons1", 150, null), ("Armor1", 150, null), ("Mobility1", 100, null),
        ("Harvesting2", 200, "Harvesting1"), ("Weapons2", 250, "Weapons1"), ("Armor2", 250, "Armor1"), ("Mobility2", 200, "Mobility1"),
    ];

    // Every finished, free TechLab researches the next upgrade on the list that we don't have, isn't
    // already being researched, and whose tier 1 is finished.
    private static List<object> ResearchUpgrades(GameState s, ref int ore)
    {
        var commands = new List<object>();
        var done = s.You.Upgrades ?? [];
        var taken = (s.You.Researching ?? []).Select(r => r.Upgrade).ToHashSet();
        var busyLabs = (s.You.Researching ?? []).Select(r => r.BuildingId).ToHashSet();
        var freeLabs = s.Buildings.Where(b => b.Owner == s.You.Slot && b.Type == "TechLab" && b.Completed && !busyLabs.Contains(b.Id));
        foreach (var lab in freeLabs)
        {
            var next = ResearchOrder.FirstOrDefault(u =>
                !done.Contains(u.Name) && !taken.Contains(u.Name) && (u.Requires == null || done.Contains(u.Requires)));
            if (next.Name == null || ore < next.Cost)
            {
                break;  // nothing left we can research yet, or not enough ore for the next one
            }

            commands.Add(new { type = "Research", buildingId = lab.Id, upgrade = next.Name });
            taken.Add(next.Name);
            ore -= next.Cost;
        }

        return commands;
    }

    // 5. Once we have plan.AttackSize soldiers, send every idle soldier to attack the nearest enemy's starting corner.
    private static object? Attack(GameState s, Plan plan)
    {
        var attacker = MyUnits(s, plan.ArmyUnit);
        if (attacker.Count < plan.AttackSize)
        {
            return null;
        }

        var enemy = s.Players
            .Where(p => p.Slot != s.You.Slot && !p.Eliminated)
            .MinBy(p => Math.Max(Math.Abs(p.StartPosition.X - attacker[0].X), Math.Abs(p.StartPosition.Y - attacker[0].Y)));
        if (enemy == null)
        {
            return null;
        }

        return new { type = "Attack", units = "idle", unitType = plan.ArmyUnit, x = enemy.StartPosition.X, y = enemy.StartPosition.Y };
    }

    // Helpers: our own units (of one type, or all of them) and our first building of a type.
    public static List<Unit> MyUnits(GameState s, string? type = null) =>
        s.Units.Where(u => u.Owner == s.You.Slot && (type == null || u.Type == type)).ToList();

    public static Building? MyBuilding(GameState s, string type) =>
        s.Buildings.FirstOrDefault(b => b.Owner == s.You.Slot && b.Type == type);
}
