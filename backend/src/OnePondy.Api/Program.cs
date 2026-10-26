using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using OnePondy.Infrastructure;
using OnePondy.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHealthChecks()
    .AddDbContextCheck<OnePondyDbContext>("database", tags: ["ready"]);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Liveness: the process is running. Readiness: dependencies such as PostgreSQL are reachable.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

app.Run();
