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
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi(options => options.AddBearerScheme());
builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));

builder.Services.AddApplication(builder.Configuration);
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);
builder.Services.AddOnePondyAuthentication();
builder.Services.AddOnePondyRateLimiting(builder.Configuration);
builder.Services.AddSignalR();
builder.Services.AddSingleton<OnePondy.Application.Rides.IRideNotifier, SignalRRideNotifier>();
builder.Services.AddHealthChecks()
    .AddDbContextCheck<OnePondyDbContext>("database", tags: ["ready"]);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

// The API description and a page to try it from: always in development, on a test site only when ApiDocs:Enabled is set, and never in
// production (see ApiDocs).
if (ApiDocs.IsEnabled(app.Environment, app.Configuration))
{
    app.MapOpenApi();
    app.MapScalarApiReference("/scalar", options => options
        .WithTitle("OnePondy API")
        // The page handles access tokens, so it talks to nothing outside: no web fonts, no usage reporting, and none of
        // Scalar's cloud features (Ask AI, MCP, Share, Deploy, Generate SDK), which would send the API description to its servers.
        .DisableDefaultFonts()
        .DisableTelemetry()
        .DisableAgent()
        .DisableMcp()
        .HideDeveloperTools()
        .AddPreferredSecuritySchemes(OpenApiBearerScheme.Name));
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

// Authentication comes first so the limiter can count a signed-in person on their own, not with everyone behind the same address.
app.UseAuthentication();
app.UseRateLimitCounting();
app.UseRateLimiter();
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
app.MapRideEndpoints();
app.MapHub<RideHub>(RideHub.Path);
app.MapFoodOrderAdminEndpoints();
app.MapReviewEndpoints();
app.MapSupportEndpoints();
app.MapOperationsEndpoints();
app.MapDeviceEndpoints();

app.Run();
