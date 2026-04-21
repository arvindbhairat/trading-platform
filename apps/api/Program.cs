using Microsoft.AspNetCore.Http.HttpResults;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();

var app = builder.Build();

app.MapGet("/api/v1", () => Results.Ok(new { name = "SignalStack.Api", version = "v0.2" }));
app.MapHealthChecks("/healthz");
app.MapHealthChecks("/readyz");

app.Run();

