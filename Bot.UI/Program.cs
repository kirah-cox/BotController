using Bot.UI.Api;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthEndpoints();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

var api = app.MapGroup("/api");
api.MapHealthEndpoints();

app.Run();
