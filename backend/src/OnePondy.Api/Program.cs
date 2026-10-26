using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using OnePondy.Api.Http;
using OnePondy.Application;
using OnePondy.Infrastructure;
using OnePondy.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));

builder.Services.AddApplication(builder.Configuration);
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);
builder.Services.AddOnePondyAuthentication();
builder.Services.AddOnePondyRateLimiting(builder.Configuration);
builder.Services.AddHealthChecks()
    .AddDbContextCheck<OnePondyDbContext>("database", tags: ["ready"]);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Serves the public account deletion page at /account/delete/.
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// Liveness: the process is running. Readiness: dependencies such as PostgreSQL are reachable.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

app.MapAuthEndpoints();
app.MapAccountEndpoints();

app.Run();
