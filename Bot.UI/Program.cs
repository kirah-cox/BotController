using Bot.UI.Api;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthEndpoints();
builder.Services.AddBotEndpoints(builder.Configuration);

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

var api = app.MapGroup("/api");
api.MapHealthEndpoints();
api.MapBotEndpoints();

app.Run();
