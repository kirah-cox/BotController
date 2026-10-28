using Bot.UI.Game;

namespace Bot.UI.Api;

public record RegisterRequest(string Name);

public static class BotEndpoints
{
    public static IServiceCollection AddBotEndpoints(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<NetRtsOptions>(config.GetSection("NetRts"));
        services.AddSingleton<NetRtsClient>();
        services.AddSingleton<BotController>();
        return services;
    }

    public static IEndpointRouteBuilder MapBotEndpoints(this IEndpointRouteBuilder app)
    {
        var bot = app.MapGroup("/bot");

        bot.MapGet("/status", (BotController controller) => controller.GetStatus());

        bot.MapPost("/register", (RegisterRequest request, BotController controller) =>
            Run(() => controller.RegisterAsync(request.Name), controller));

        bot.MapPost("/start", (StartRequest request, BotController controller) =>
            Run(() => controller.StartAsync(request), controller));

        bot.MapPost("/strategy", (StrategyRequest request, BotController controller) =>
            Run(() =>
            {
                controller.UpdateStrategy(request);
                return Task.CompletedTask;
            }, controller));

        bot.MapPost("/stop", (BotController controller) =>
            Run(controller.StopAsync, controller));

        return app;
    }

    // Runs an action and answers with the new status, or a 400 with { message } the page can show.
    private static async Task<IResult> Run(Func<Task> action, BotController controller)
    {
        try
        {
            await action();
            return Results.Ok(controller.GetStatus());
        }
        catch (Exception e) when (e is NetRtsException or InvalidOperationException)
        {
            return Results.BadRequest(new { message = e.Message });
        }
        catch (HttpRequestException e)
        {
            return Results.Json(new { message = $"Could not reach the game server: {e.Message}" }, statusCode: 502);
        }
    }
}
