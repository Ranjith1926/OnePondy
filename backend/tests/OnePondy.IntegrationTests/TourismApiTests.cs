using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using OnePondy.Application.Identity;
using OnePondy.Domain.Identity;
using OnePondy.Infrastructure.Persistence;

namespace OnePondy.IntegrationTests;

/// <summary>
/// Saved places and the manual itinerary builder (spec §10, Phase 4), against a real, migrated
/// PostgreSQL database. Runs only when ONEPONDY_TEST_DATABASE is set (see README).
/// </summary>
public sealed class TourismApiTests : IAsyncLifetime
{
    private ApiFactory? _factory;

    private ApiFactory Factory => _factory ?? throw new InvalidOperationException("No test database configured.");

    public async Task InitializeAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable(DatabaseFactAttribute.VariableName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        _factory = new ApiFactory(connectionString);
        await TestDatabase.MigrateOnceAsync(_factory.Services);
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }
    }

    private static object PlaceBody(string name, double latitude, double longitude) => new
    {
        name,
        categoryCode = "beach",
        description = "A quiet beach reached by boat.",
        visitorGuidance = "Carry water.",
        address = "Puducherry",
        latitude,
        longitude,
        openingHours = (object?)null,
        entryFees = Array.Empty<object>(),
        translations = Array.Empty<object>(),
    };

    /// <summary>Creates and publishes a place directly through the admin API, as a content editor.</summary>
    private async Task<(string Id, string Slug)> PublishedPlaceAsync(HttpClient client, string editorToken, string name, double lat, double lng)
    {
        var created = await ReadAsync(await SendAsync(client, HttpMethod.Post, "/api/v1/admin/places", editorToken, PlaceBody(name, lat, lng)));
        var id = created.GetProperty("id").GetString()!;
        var slug = created.GetProperty("slug").GetString()!;
        var version = created.GetProperty("version").GetUInt32();

        async Task<uint> Step(string action, object? extra = null)
        {
            object body = extra is null ? new { version } : Merge(extra, version);
            var result = await ReadAsync(await SendAsync(client, HttpMethod.Post, $"/api/v1/admin/places/{id}/{action}", editorToken, body));
            return result.GetProperty("version").GetUInt32();
        }

        version = await Step("submit");
        version = await Step("verify", new { source = "Site visit" });
        await Step("publish");
        return (id, slug);
    }

    private static Dictionary<string, object> Merge(object extra, uint version)
    {
        var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(extra))!
            .ToDictionary(e => e.Key, e => (object)e.Value);
        dict["version"] = version;
        return dict;
    }

    /// <summary>A valid content-editor session, created directly (skipping the OTP ceremony, which
    /// ContentApiTests already covers) so each test here does not depend on being the first one to
    /// claim the bootstrap administrator across the whole test run.</summary>
    private async Task<string> EditorTokenAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IUserAccountStore>();
        var staff = scope.ServiceProvider.GetRequiredService<OnePondy.Application.Staff.IStaffDirectory>();
        var auth = scope.ServiceProvider.GetRequiredService<AuthService>();

        var account = await accounts.FindOrCreateAsync(OtpChannel.Email, ApiFactory.NewEmail("editor"), default);
        await staff.SetRoleAsync(account.Id, RoleNames.ContentEditor, default);
        var withRole = await accounts.FindActiveAsync(account.Id, default) ?? account;
        var tokens = await auth.StartSessionAsync(withRole, "integration-test", default);
        return tokens.AccessToken.Value;
    }

    private async Task<string> SignInCustomerAsync(HttpClient client)
    {
        var phone = ApiFactory.NewIndianMobile();
        var requested = await client.PostAsJsonAsync("/api/v1/auth/otp/request", new { channel = "sms", destination = phone });
        var challengeId = (await ReadAsync(requested)).GetProperty("challengeId").GetString();
        var verified = await client.PostAsJsonAsync(
            "/api/v1/auth/otp/verify",
            new { challengeId, code = Factory.Codes.For("+91" + phone) });
        return (await ReadAsync(verified)).GetProperty("accessToken").GetString()!;
    }

    [DatabaseFact]
    public async Task Saving_a_place_needs_it_to_be_publicly_visible_and_is_idempotent()
    {
        using var client = Factory.CreateClient();
        var editor = await EditorTokenAsync();
        var customer = await SignInCustomerAsync(client);
        var draft = await ReadAsync(await SendAsync(client, HttpMethod.Post, "/api/v1/admin/places", editor, PlaceBody("Unpublished spot", 11.9, 79.8)));
        var (placeId, _) = await PublishedPlaceAsync(client, editor, "Paradise Beach", 11.8915, 79.8257);

        var draftSave = await SendAsync(client, HttpMethod.Post, "/api/v1/saved-places", customer, new { placeId = draft.GetProperty("id").GetString() });
        Assert.Equal(HttpStatusCode.BadRequest, draftSave.StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(client, HttpMethod.Post, "/api/v1/saved-places", customer, new { placeId })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(client, HttpMethod.Post, "/api/v1/saved-places", customer, new { placeId })).StatusCode);

        var list = await ReadAsync(await SendAsync(client, HttpMethod.Get, "/api/v1/saved-places", customer));
        Assert.Equal(placeId, Assert.Single(list.EnumerateArray()).GetProperty("place").GetProperty("id").GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(client, HttpMethod.Delete, $"/api/v1/saved-places/{placeId}", customer)).StatusCode);
        Assert.Empty((await ReadAsync(await SendAsync(client, HttpMethod.Get, "/api/v1/saved-places", customer))).EnumerateArray());
    }

    [DatabaseFact]
    public async Task Nearby_places_are_sorted_by_distance()
    {
        using var client = Factory.CreateClient();
        var editor = await EditorTokenAsync();
        var (beachId, _) = await PublishedPlaceAsync(client, editor, "Distance Test Beach", 11.8915, 79.8257);
        var (lakeId, _) = await PublishedPlaceAsync(client, editor, "Distance Test Lake", 11.9562, 79.7429);

        var result = await ReadAsync(await client.GetAsync("/api/v1/places?lat=11.9&lng=79.83"));
        var ids = result.GetProperty("items").EnumerateArray().Select(p => p.GetProperty("id").GetString()).ToList();

        Assert.True(ids.IndexOf(beachId) < ids.IndexOf(lakeId));
        Assert.True(result.GetProperty("items")[ids.IndexOf(beachId)].GetProperty("distanceKm").GetDouble() < 15);

        var mismatched = await client.GetAsync("/api/v1/places?lat=11.9");
        Assert.Equal(HttpStatusCode.BadRequest, mismatched.StatusCode);
    }

    [DatabaseFact]
    public async Task Building_and_reordering_an_itinerary_never_fails_or_loses_a_stop()
    {
        using var client = Factory.CreateClient();
        var editor = await EditorTokenAsync();
        var customer = await SignInCustomerAsync(client);
        var (beachId, _) = await PublishedPlaceAsync(client, editor, "Itinerary Beach", 11.8915, 79.8257);
        var (lakeId, _) = await PublishedPlaceAsync(client, editor, "Itinerary Lake", 11.9562, 79.7429);

        var created = await ReadAsync(await SendAsync(
            client,
            HttpMethod.Post,
            "/api/v1/itineraries",
            customer,
            new { title = "Weekend in Pondy", startDate = "2026-12-05", endDate = "2026-12-06" }));
        var itineraryId = created.GetProperty("id").GetString();

        var withFirstStop = await ReadAsync(await SendAsync(
            client, HttpMethod.Post, $"/api/v1/itineraries/{itineraryId}/stops", customer, new { placeId = beachId, dayNumber = 1, notes = "Sunrise walk" }));
        var withSecondStop = await ReadAsync(await SendAsync(
            client, HttpMethod.Post, $"/api/v1/itineraries/{itineraryId}/stops", customer, new { placeId = lakeId, dayNumber = 1 }));
        var stops = withSecondStop.GetProperty("stops").EnumerateArray().ToList();
        Assert.Equal(2, stops.Count);
        var firstStopId = stops[0].GetProperty("id").GetString();
        var secondStopId = stops[1].GetProperty("id").GetString();

        // Swap day and order together, which only works against real PostgreSQL because the
        // uniqueness check on (itinerary_id, day_number, stop_order) is deferred to commit.
        var reordered = await ReadAsync(await SendAsync(
            client,
            HttpMethod.Put,
            $"/api/v1/itineraries/{itineraryId}/stops/order",
            customer,
            new
            {
                positions = new[]
                {
                    new { stopId = firstStopId, dayNumber = 2, stopOrder = 0 },
                    new { stopId = secondStopId, dayNumber = 1, stopOrder = 0 },
                },
            }));
        var reorderedStops = reordered.GetProperty("stops").EnumerateArray().ToList();
        Assert.Equal(2, reorderedStops.Count);
        Assert.Equal(2, reorderedStops.Single(s => s.GetProperty("id").GetString() == firstStopId).GetProperty("dayNumber").GetInt32());
        Assert.Equal(1, reorderedStops.Single(s => s.GetProperty("id").GetString() == secondStopId).GetProperty("dayNumber").GetInt32());

        var afterRemove = await ReadAsync(await SendAsync(client, HttpMethod.Delete, $"/api/v1/itineraries/{itineraryId}/stops/{firstStopId}", customer));
        Assert.Equal(secondStopId, Assert.Single(afterRemove.GetProperty("stops").EnumerateArray()).GetProperty("id").GetString());

        // Only the owner can see or change it.
        var stranger = await SignInCustomerAsync(client);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Get, $"/api/v1/itineraries/{itineraryId}", stranger)).StatusCode);
    }

    [DatabaseFact]
    public async Task Sharing_an_itinerary_exposes_the_plan_but_not_the_owner_and_can_be_turned_off()
    {
        using var client = Factory.CreateClient();
        var editor = await EditorTokenAsync();
        var customer = await SignInCustomerAsync(client);
        var (placeId, _) = await PublishedPlaceAsync(client, editor, "Shared Trip Spot", 11.8915, 79.8257);

        var itinerary = await ReadAsync(await SendAsync(client, HttpMethod.Post, "/api/v1/itineraries", customer, new { title = "Honeymoon" }));
        var itineraryId = itinerary.GetProperty("id").GetString();
        await SendAsync(client, HttpMethod.Post, $"/api/v1/itineraries/{itineraryId}/stops", customer, new { placeId, dayNumber = 1, notes = "Golden hour" });

        var shareResult = await ReadAsync(await SendAsync(client, HttpMethod.Post, $"/api/v1/itineraries/{itineraryId}/share", customer));
        var token = shareResult.GetProperty("shareToken").GetString();

        var publicResponse = await client.GetAsync($"/api/v1/itineraries/shared/{token}");
        Assert.Equal(HttpStatusCode.OK, publicResponse.StatusCode);
        var shared = await ReadAsync(publicResponse);
        Assert.Equal("Honeymoon", shared.GetProperty("title").GetString());
        Assert.Equal("Golden hour", Assert.Single(shared.GetProperty("stops").EnumerateArray()).GetProperty("notes").GetString());
        Assert.False(shared.TryGetProperty("userId", out _));
        Assert.False(shared.TryGetProperty("id", out _));

        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(client, HttpMethod.Delete, $"/api/v1/itineraries/{itineraryId}/share", customer)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/itineraries/shared/{token}")).StatusCode);
    }

    [DatabaseFact]
    public async Task Archiving_hides_an_itinerary_from_the_default_list()
    {
        using var client = Factory.CreateClient();
        var customer = await SignInCustomerAsync(client);
        var kept = await ReadAsync(await SendAsync(client, HttpMethod.Post, "/api/v1/itineraries", customer, new { title = "Keep" }));
        var old = await ReadAsync(await SendAsync(client, HttpMethod.Post, "/api/v1/itineraries", customer, new { title = "Old" }));

        await SendAsync(client, HttpMethod.Post, $"/api/v1/itineraries/{old.GetProperty("id").GetString()}/archive", customer);

        var active = await ReadAsync(await client.SendAsync(Authorized(HttpMethod.Get, "/api/v1/itineraries", customer)));
        var everything = await ReadAsync(await client.SendAsync(Authorized(HttpMethod.Get, "/api/v1/itineraries?includeArchived=true", customer)));

        Assert.Equal(kept.GetProperty("id").GetString(), Assert.Single(active.EnumerateArray()).GetProperty("id").GetString());
        Assert.Equal(2, everything.GetArrayLength());
    }

    private static HttpRequestMessage Authorized(HttpMethod method, string path, string token)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, string token, object? body = null)
    {
        var request = Authorized(method, path, token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await client.SendAsync(request);
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri} -> {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}
