using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OnePondy.Infrastructure.Persistence;
using SkiaSharp;

namespace OnePondy.IntegrationTests;

/// <summary>
/// The web console API against a real, migrated PostgreSQL database (spec §10, Phase 3 acceptance
/// criteria). Runs only when ONEPONDY_TEST_DATABASE is set (see README).
/// </summary>
public sealed class ContentApiTests : IAsyncLifetime
{
    // Becomes the first administrator of an empty test database; later runs find it already set up.
    private const string Founder = "founder@onepondy.test";

    private ApiFactory? _factory;

    private ApiFactory Factory => _factory ?? throw new InvalidOperationException("No test database configured.");

    public async Task InitializeAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable(DatabaseFactAttribute.VariableName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        _factory = new ApiFactory(connectionString, new Dictionary<string, string>
        {
            ["Auth:Otp:ResendInterval"] = "00:00:00",
            ["Auth:Otp:MaxPerDestination"] = "1000",
            ["Console:BootstrapAdministrators:0"] = Founder,
        });
        await TestDatabase.MigrateOnceAsync(_factory.Services);
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
            if (Directory.Exists(_factory.MediaRoot))
            {
                Directory.Delete(_factory.MediaRoot, recursive: true);
            }
        }
    }

    [DatabaseFact]
    public async Task Console_access_follows_staff_roles()
    {
        using var client = Factory.CreateClient();
        var admin = await SignInStaffAsync(client, Founder);
        var editor = await AddStaffAsync(client, admin, "content_editor");
        var support = await AddStaffAsync(client, admin, "support_agent");
        var customer = await SignInCustomerAsync(client);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/admin/places")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(client, HttpMethod.Get, "/api/v1/admin/places", customer)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, HttpMethod.Get, "/api/v1/admin/places", support)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(client, HttpMethod.Post, "/api/v1/admin/places", support, PlaceBody("Support place"))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(client, HttpMethod.Post, "/api/v1/admin/places", editor, PlaceBody(Unique("Editor place")))).StatusCode);

        // A content editor cannot change configuration; an administrator can.
        var flag = new { status = "comingSoon" };
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(client, HttpMethod.Put, "/api/v1/admin/config/modules/food", editor, flag)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, HttpMethod.Put, "/api/v1/admin/config/modules/food", admin, flag)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(client, HttpMethod.Get, "/api/v1/admin/staff", editor)).StatusCode);

        var me = await ReadAsync(await SendAsync(client, HttpMethod.Get, "/api/v1/admin/me", editor));
        Assert.Equal("content_editor", me.GetProperty("role").GetString());
    }

    [DatabaseFact]
    public async Task Codes_are_only_sent_to_staff()
    {
        using var client = Factory.CreateClient();
        var stranger = ApiFactory.NewEmail("stranger");

        var response = await client.PostAsJsonAsync("/api/v1/admin/auth/otp/request", new { email = stranger });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Throws<KeyNotFoundException>(() => Factory.Codes.For(stranger));
    }

    [DatabaseFact]
    public async Task Places_are_public_only_while_verified_and_published()
    {
        using var client = Factory.CreateClient();
        var admin = await SignInStaffAsync(client, Founder);
        var editor = await AddStaffAsync(client, admin, "content_editor");
        var name = Unique("Paradise Beach");

        var place = await ReadAsync(await SendAsync(client, HttpMethod.Post, "/api/v1/admin/places", editor, PlaceBody(name)));
        var id = place.GetProperty("id").GetString()!;
        var slug = place.GetProperty("slug").GetString()!;
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/places/{slug}")).StatusCode);

        place = await ActAsync(client, editor, id, "submit", place);
        place = await ActAsync(client, admin, id, "verify", place, new { source = "Site visit 2026-10-20" });
        Assert.Equal("Site visit 2026-10-20", place.GetProperty("verificationSource").GetString());
        Assert.False(string.IsNullOrEmpty(place.GetProperty("verifiedBy").GetProperty("name").GetString()));
        place = await ActAsync(client, admin, id, "publish", place);

        var listed = await ReadAsync(await client.GetAsync($"/api/v1/places?q={Uri.EscapeDataString(name)}"));
        Assert.Equal(id, listed.GetProperty("items")[0].GetProperty("id").GetString());
        var detail = await ReadAsync(await client.GetAsync($"/api/v1/places/{slug}"));
        Assert.Equal("standard", detail.GetProperty("entryFees")[0].GetProperty("category").GetString());

        // Any content change takes the place off the app until it is verified again.
        var stale = place.GetProperty("version").GetUInt32();
        var edited = await SendAsync(client, HttpMethod.Put, $"/api/v1/admin/places/{id}", editor, new
        {
            version = stale,
            place = PlaceBody(name, "New boat timings from November."),
        });
        Assert.Equal("draft", (await ReadAsync(edited)).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/places/{slug}")).StatusCode);

        // An edit based on an old copy is refused rather than overwriting the newer change.
        var conflict = await SendAsync(client, HttpMethod.Put, $"/api/v1/admin/places/{id}", editor, new
        {
            version = stale,
            place = PlaceBody(name, "Someone else's older edit."),
        });
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal("place_changed", (await ReadAsync(conflict)).GetProperty("code").GetString());

        var history = await ReadAsync(await SendAsync(client, HttpMethod.Get, $"/api/v1/admin/places/{id}/history", editor));
        var actions = history.GetProperty("items").EnumerateArray().Select(e => e.GetProperty("action").GetString()).ToList();
        Assert.Equal(["place.updated", "place.published", "place.verified", "place.submitted", "place.created"], actions);
    }

    [DatabaseFact]
    public async Task Photos_are_validated_resized_served_and_credited()
    {
        using var client = Factory.CreateClient();
        var admin = await SignInStaffAsync(client, Founder);
        var place = await ReadAsync(await SendAsync(client, HttpMethod.Post, "/api/v1/admin/places", admin, PlaceBody(Unique("Ousteri Lake"))));
        var id = place.GetProperty("id").GetString()!;

        var missingCredit = await UploadAsync(client, admin, id, licence: "ccBy", attribution: null);
        Assert.Equal(HttpStatusCode.BadRequest, missingCredit.StatusCode);
        Assert.True((await ReadAsync(missingCredit)).GetProperty("errors").TryGetProperty("attribution", out _));

        var uploaded = await UploadAsync(client, admin, id, licence: "ccBy", attribution: "Photo: A. Kumar, CC BY 4.0");
        Assert.Equal(HttpStatusCode.Created, uploaded.StatusCode);
        var photo = await ReadAsync(uploaded);
        Assert.Equal((1600, 1200), (photo.GetProperty("width").GetInt32(), photo.GetProperty("height").GetInt32()));

        var image = await client.GetAsync(photo.GetProperty("urls").GetProperty("medium").GetString());
        Assert.Equal(HttpStatusCode.OK, image.StatusCode);
        Assert.Equal("image/webp", image.Content.Headers.ContentType?.MediaType);
        Assert.Contains("immutable", image.Headers.CacheControl?.ToString());

        place = await ActAsync(client, admin, id, "submit", place);
        place = await ActAsync(client, admin, id, "verify", place, new { source = "Lake authority website" });
        await ActAsync(client, admin, id, "publish", place);
        var detail = await ReadAsync(await client.GetAsync($"/api/v1/places/{id}"));
        Assert.Equal("Photo: A. Kumar, CC BY 4.0", detail.GetProperty("images")[0].GetProperty("attribution").GetString());
    }

    [DatabaseFact]
    public async Task Module_flags_change_what_the_app_is_told()
    {
        using var client = Factory.CreateClient();
        var admin = await SignInStaffAsync(client, Founder);

        await SendAsync(client, HttpMethod.Put, "/api/v1/admin/config/modules/rides", admin, new { status = "hidden" });
        var config = await ReadAsync(await client.GetAsync("/api/v1/config"));
        Assert.Equal("hidden", config.GetProperty("modules").GetProperty("rides").GetString());

        await SendAsync(client, HttpMethod.Put, "/api/v1/admin/config/modules/rides", admin, new { status = "comingSoon" });
        config = await ReadAsync(await client.GetAsync("/api/v1/config"));
        Assert.Equal("comingSoon", config.GetProperty("modules").GetProperty("rides").GetString());

        var badVersion = await SendAsync(client, HttpMethod.Put, "/api/v1/admin/config/min-app-version", admin, new { minSupportedAppVersion = "latest" });
        Assert.Equal(HttpStatusCode.BadRequest, badVersion.StatusCode);
    }

    [DatabaseFact]
    public async Task The_audit_log_cannot_be_changed()
    {
        using var client = Factory.CreateClient();
        await SignInStaffAsync(client, Founder);
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OnePondyDbContext>();

        var update = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("UPDATE audit_log SET action = 'edited'"));
        var delete = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("DELETE FROM audit_log"));

        Assert.Contains("append-only", update.MessageText);
        Assert.Contains("append-only", delete.MessageText);
    }

    private static string Unique(string name) => $"{name} {Guid.NewGuid().ToString("N")[..6]}";

    private static object PlaceBody(string name, string description = "A quiet beach reached by boat.") => new
    {
        name,
        categoryCode = "beach",
        description,
        visitorGuidance = "Carry water.",
        address = "Chunnambar, Puducherry",
        latitude = 11.8915,
        longitude = 79.8257,
        openingHours = new { alwaysOpen = false, periods = new[] { new { day = "monday", opens = "08:00", closes = "17:30" } } },
        entryFees = new[] { new { category = "standard", amount = 50 } },
        translations = new[] { new { language = "ta", name = "பாரடைஸ் கடற்கரை" } },
    };

    private async Task<JsonElement> ActAsync(HttpClient client, string token, string id, string action, JsonElement place, object? extra = null)
    {
        var version = place.GetProperty("version").GetUInt32();
        object body = extra switch
        {
            null => new { version },
            _ => JsonSerializer.Deserialize<Dictionary<string, object>>(JsonSerializer.Serialize(extra))!
                .Append(new KeyValuePair<string, object>("version", version))
                .ToDictionary(),
        };
        var response = await SendAsync(client, HttpMethod.Post, $"/api/v1/admin/places/{id}/{action}", token, body);
        Assert.True(response.IsSuccessStatusCode, $"{action} failed: {await response.Content.ReadAsStringAsync()}");
        return await ReadAsync(response);
    }

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, string token, string placeId, string licence, string? attribution)
    {
        using var bitmap = new SKBitmap(2000, 1500);
        bitmap.Erase(new SKColor(232, 163, 61));
        using var jpeg = SKImage.FromBitmap(bitmap).Encode(SKEncodedImageFormat.Jpeg, 85);

        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(jpeg.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(file, "file", "lake.jpg");
        form.Add(new StringContent("Boats on Ousteri Lake at sunset"), "altText");
        form.Add(new StringContent(licence), "licence");
        if (attribution is not null)
        {
            form.Add(new StringContent(attribution), "attribution");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/places/{placeId}/media") { Content = form };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }

    private async Task<string> SignInStaffAsync(HttpClient client, string email)
    {
        var requested = await client.PostAsJsonAsync("/api/v1/admin/auth/otp/request", new { email });
        Assert.Equal(HttpStatusCode.Accepted, requested.StatusCode);
        var challengeId = (await ReadAsync(requested)).GetProperty("challengeId").GetString();
        var verified = await client.PostAsJsonAsync("/api/v1/admin/auth/otp/verify", new
        {
            challengeId,
            code = Factory.Codes.For(email),
            deviceName = "Integration test",
        });
        Assert.True(verified.IsSuccessStatusCode, await verified.Content.ReadAsStringAsync());
        return (await ReadAsync(verified)).GetProperty("accessToken").GetString()!;
    }

    private async Task<string> AddStaffAsync(HttpClient client, string adminToken, string role)
    {
        var email = ApiFactory.NewEmail(role.Replace('_', '-'));
        var added = await SendAsync(client, HttpMethod.Post, "/api/v1/admin/staff", adminToken, new { email, role });
        Assert.True(added.IsSuccessStatusCode, await added.Content.ReadAsStringAsync());
        return await SignInStaffAsync(client, email);
    }

    private async Task<string> SignInCustomerAsync(HttpClient client)
    {
        var phone = ApiFactory.NewIndianMobile();
        var requested = await client.PostAsJsonAsync("/api/v1/auth/otp/request", new { channel = "sms", destination = phone });
        var challengeId = (await ReadAsync(requested)).GetProperty("challengeId").GetString();
        var verified = await client.PostAsJsonAsync("/api/v1/auth/otp/verify", new { challengeId, code = Factory.Codes.For("+91" + phone) });
        return (await ReadAsync(verified)).GetProperty("accessToken").GetString()!;
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, string token, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await client.SendAsync(request);
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();
}
