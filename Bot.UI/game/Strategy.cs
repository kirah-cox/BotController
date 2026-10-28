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

        var barracks = BuildBarracks(s, plan, ore);
        if (barracks != null)
        {
            commands.Add(barracks);
            ore -= 150;
        }

        var soldier = TrainSoldier(s, plan, ore);
        if (soldier != null)
        {
            commands.Add(soldier);
            ore -= plan.ArmyCost;
        }

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
    private static object? BuildBarracks(GameState s, Plan plan, int ore)
    {
        var workers = MyUnits(s, "Worker");
        if (workers.Count < plan.Workers || MyBuilding(s, "Barracks") != null || ore < 150)
        {
            return null;
        }

        var hq = MyBuilding(s, "CommandCenter")!;
        var x = hq.X < s.MapWidth / 2 ? hq.X + 4 : hq.X - 4;
        var y = hq.Y < s.MapHeight / 2 ? hq.Y + 4 : hq.Y - 4;
        return new { type = "Build", unitIds = new[] { workers[0].Id }, buildingType = "Barracks", x, y };
    }

    // 4. The Barracks trains plan.ArmyUnit non-stop (a Soldier costs 100 ore).
    private static object? TrainSoldier(GameState s, Plan plan, int ore)
    {
        var barracks = MyBuilding(s, "Barracks");
        if (barracks == null || !barracks.Completed || barracks.Production is { Count: > 0 } || ore < plan.ArmyCost)
        {
            return null;
        }

        return new { type = "Produce", buildingId = barracks.Id, unitType = plan.ArmyUnit };
    }

    // 5. Once we have plan.AttackSize soldiers, send every idle soldier to attack the nearest enemy's starting corner.
    private static object? Attack(GameState s, Plan plan)
    {
        var soldiers = MyUnits(s, plan.ArmyUnit);
        if (soldiers.Count < plan.AttackSize)
        {
            return null;
        }

        var enemy = s.Players
            .Where(p => p.Slot != s.You.Slot && !p.Eliminated)
            .MinBy(p => Math.Max(Math.Abs(p.StartPosition.X - soldiers[0].X), Math.Abs(p.StartPosition.Y - soldiers[0].Y)));
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
