using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using OnePondy.Api.Http;
using OnePondy.Application;
using OnePondy.Infrastructure;
using OnePondy.Infrastructure.Media;
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

// Uploaded place photos. File names include a unique id and never change, so they cache for a year.
var mediaRoot = app.Services.GetRequiredService<IOptions<MediaStorageOptions>>().Value.RootPath!;
Directory.CreateDirectory(mediaRoot);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(mediaRoot),
    RequestPath = MediaStorageOptions.RequestPath,
    OnPrepareResponse = context => context.Context.Response.Headers.CacheControl = "public, max-age=31536000, immutable",
});

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// Liveness: the process is running. Readiness: dependencies such as PostgreSQL are reachable.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

app.MapAuthEndpoints();
app.MapAccountEndpoints();
app.MapPublicContentEndpoints();
app.MapStaffAuthEndpoints();
app.MapAdminPlaceEndpoints();
app.MapAdminSettingsEndpoints();
app.MapSavedPlaceEndpoints();
app.MapItineraryEndpoints();
app.MapTripPlannerEndpoints();
app.MapBusinessEndpoints();
app.MapListingEndpoints();
app.MapSavedListingEndpoints();
app.MapBookingEndpoints();
app.MapPaymentEndpoints();
app.MapPaymentAdminEndpoints();
app.MapSettlementEndpoints();
app.MapFoodEndpoints();
app.MapFoodOrderEndpoints();
app.MapDriverEndpoints();
app.MapDeviceEndpoints();

app.Run();
