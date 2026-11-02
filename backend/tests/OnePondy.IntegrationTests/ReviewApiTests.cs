using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace OnePondy.IntegrationTests;

// Reviews (spec §7 and Phase 10, step 10.1), against a real PostgreSQL database: the HTTP flow from a delivered order to a published, answered
// review, who may moderate, the same order reviewed from several requests at once, and the rules only the database can enforce.
// Part of FoodOrderApiTests so these tests share its restaurant, driver and sign-in helpers and never run alongside the one that switches the
// food module off.
public sealed partial class FoodOrderApiTests
{
    private const string Reviews = "/api/v1/reviews";

    private const string AdminReviews = "/api/v1/admin/reviews";

    private async Task<string> StaffTokenAsync(string role)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<OnePondy.Application.Identity.IUserAccountStore>();
        var staff = scope.ServiceProvider.GetRequiredService<OnePondy.Application.Staff.IStaffDirectory>();
        var auth = scope.ServiceProvider.GetRequiredService<OnePondy.Application.Identity.AuthService>();

        var account = await accounts.FindOrCreateAsync(OnePondy.Domain.Identity.OtpChannel.Email, ApiFactory.NewEmail("staff"), default);
        await staff.SetRoleAsync(account.Id, role, default);
        var withRole = await accounts.FindActiveAsync(account.Id, default) ?? account;
        var tokens = await auth.StartSessionAsync(withRole, "integration-test", default);
        return tokens.AccessToken.Value;
    }

    /// <summary>Places an order, works it through the kitchen and has the courier deliver it. Returns the order id.</summary>
    private async Task<string> DeliveredOrderAsync(HttpClient client, Shop shop, string customer, Courier courier)
    {
        var orderId = await ReadyOrderAsync(client, shop, customer);
        var jobId = await JobIdOfAsync(orderId);
        foreach (var step in new[] { "accept", "pickup", "deliver" })
        {
            Assert.True((await SendAsync(client, HttpMethod.Post, JobPath(jobId, step), courier.Token)).IsSuccessStatusCode);
        }

        return orderId;
    }

    private static async Task<JsonElement> PublicReviewsAsync(HttpClient client, string restaurantId) =>
        await ReadAsync(await client.GetAsync($"{Reviews}?restaurantId={restaurantId}"));

    private static async Task<JsonElement> QueueAsync(HttpClient client, string token, string status = "pending") =>
        await ReadAsync(await SendAsync(client, HttpMethod.Get, $"{AdminReviews}?status={status}&pageSize=100", token));

    private static async Task<JsonElement> PostReviewAsync(HttpClient client, string customer, string orderId, int rating, string? text = null)
    {
        var response = await SendAsync(client, HttpMethod.Post, Reviews, customer, new { foodOrderId = orderId, rating, text });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<JsonElement> PublishReviewAsync(HttpClient client, string moderator, JsonElement review) =>
        await ReadAsync(await SendAsync(client, HttpMethod.Post, $"{AdminReviews}/{Id(review)}/publish", moderator, new { version = VersionOf(review) }));

    [DatabaseFact]
    public async Task A_delivered_order_is_reviewed_moderated_published_and_answered()
    {
        using var client = Factory.CreateClient();
        var admin = await AdministratorTokenAsync();
        await TurnFoodOnAsync(client, admin);
        var shop = await OpenShopAsync(client, admin, UniqueName("Review Roti"));
        var customer = await SignInAsync(client);
        var courier = await CourierAsync(client, admin);
        var orderId = await DeliveredOrderAsync(client, shop, customer, courier);

        // Only a signed-in customer, only for their own, only once the order is delivered.
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(Reviews, new { foodOrderId = orderId, rating = 5 })).StatusCode);
        var stranger = await SignInAsync(client);
        var notTheirs = await SendAsync(client, HttpMethod.Post, Reviews, stranger, new { foodOrderId = orderId, rating = 5 });
        Assert.Equal(HttpStatusCode.NotFound, notTheirs.StatusCode);
        Assert.Equal("review_transaction_not_found", await ErrorCodeAsync(notTheirs));
        // An order still with the restaurant (no delivery job opened, so no other test can see it) is not yet reviewable.
        var stillCooking = Id(await PlaceAsync(client, customer, shop));
        var tooEarly = await SendAsync(client, HttpMethod.Post, Reviews, customer, new { foodOrderId = stillCooking, rating = 5 });
        Assert.Equal(HttpStatusCode.Conflict, tooEarly.StatusCode);
        Assert.Equal("review_not_eligible", await ErrorCodeAsync(tooEarly));
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(client, HttpMethod.Post, Reviews, customer, new { foodOrderId = orderId, rating = 9 })).StatusCode);

        var review = await PostReviewAsync(client, customer, orderId, 5, "Hot and on time. Call 9876543210 for offers.");
        Assert.Equal("pending", review.GetProperty("status").GetString());
        var again = await SendAsync(client, HttpMethod.Post, Reviews, customer, new { foodOrderId = orderId, rating = 1 });
        Assert.Equal("review_already_exists", await ErrorCodeAsync(again));

        // Nothing is public until a moderator publishes it, and the moderator sees what the automatic check found.
        Assert.Equal(0, (await PublicReviewsAsync(client, shop.BusinessId)).GetProperty("totalCount").GetInt32());
        var queue = await QueueAsync(client, admin);
        var row = queue.GetProperty("items").EnumerateArray().Single(r => Id(r) == Id(review));
        Assert.Contains("phone_number", row.GetProperty("flags").EnumerateArray().Select(f => f.GetString()));
        Assert.Equal("food_order", row.GetProperty("kind").GetString());

        // Only moderators: customers and support agents are refused; a content editor is let in.
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(client, HttpMethod.Get, AdminReviews, customer)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(client, HttpMethod.Get, AdminReviews, await StaffTokenAsync("support_agent"))).StatusCode);
        var editor = await StaffTokenAsync("content_editor");
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, HttpMethod.Get, AdminReviews, editor)).StatusCode);

        var published = await PublishReviewAsync(client, editor, review);
        Assert.Equal("published", published.GetProperty("status").GetString());
        Assert.Equal(1, await AuditCountAsync("review.published", Id(review)));
        var republish = await SendAsync(client, HttpMethod.Post, $"{AdminReviews}/{Id(review)}/publish", editor, new { version = VersionOf(published) });
        Assert.Equal(HttpStatusCode.Conflict, republish.StatusCode);

        // Anyone can now read it, with the author's first name only, and no rating yet because one review is not an average.
        var page = await PublicReviewsAsync(client, shop.BusinessId);
        Assert.Equal(1, page.GetProperty("totalCount").GetInt32());
        Assert.Equal(JsonValueKind.Null, page.GetProperty("rating").ValueKind);
        var shown = page.GetProperty("items").EnumerateArray().Single();
        Assert.Equal(5, shown.GetProperty("rating").GetInt32());
        Assert.Equal("A customer", shown.GetProperty("authorName").GetString());

        // The business sees it and answers once; nobody else can.
        var ownerList = await ReadAsync(await SendAsync(client, HttpMethod.Get, $"/api/v1/partner/businesses/{shop.BusinessId}/reviews", shop.Owner));
        var forOwner = ownerList.GetProperty("items").EnumerateArray().Single();
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Get, $"/api/v1/partner/businesses/{shop.BusinessId}/reviews", customer)).StatusCode);
        var replyPath = $"/api/v1/partner/businesses/{shop.BusinessId}/reviews/{Id(review)}/reply";
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Post, replyPath, customer, new { version = VersionOf(forOwner), text = "Thanks to me" })).StatusCode);
        var replied = await ReadAsync(await SendAsync(client, HttpMethod.Post, replyPath, shop.Owner, new { version = VersionOf(forOwner), text = "Thank you!" }));
        Assert.Equal("Thank you!", replied.GetProperty("reply").GetProperty("text").GetString());
        var second = await SendAsync(client, HttpMethod.Post, replyPath, shop.Owner, new { version = VersionOf(replied), text = "Again" });
        Assert.Equal("review_cannot_reply", await ErrorCodeAsync(second));
        Assert.Equal("Thank you!", (await PublicReviewsAsync(client, shop.BusinessId)).GetProperty("items")[0].GetProperty("reply").GetProperty("text").GetString());

        // The author changes it once: it goes back to waiting and the old answer goes with the old words.
        var mine = (await ReadAsync(await SendAsync(client, HttpMethod.Get, $"{Reviews}/mine", customer))).EnumerateArray().Single(r => Id(r) == Id(review));
        var edited = await ReadAsync(await SendAsync(client, HttpMethod.Put, $"{Reviews}/{Id(review)}", customer, new { version = VersionOf(mine), rating = 4, text = "Still good." }));
        Assert.Equal("pending", edited.GetProperty("status").GetString());
        Assert.False(edited.GetProperty("canEdit").GetBoolean());
        Assert.Equal(0, (await PublicReviewsAsync(client, shop.BusinessId)).GetProperty("totalCount").GetInt32());
        var twice = await SendAsync(client, HttpMethod.Put, $"{Reviews}/{Id(review)}", customer, new { version = VersionOf(edited), rating = 1, text = "Worse" });
        Assert.Equal("review_already_edited", await ErrorCodeAsync(twice));
    }

    [DatabaseFact]
    public async Task Three_published_reviews_give_a_restaurant_its_rating_and_taking_one_down_removes_it()
    {
        using var client = Factory.CreateClient();
        var admin = await AdministratorTokenAsync();
        await TurnFoodOnAsync(client, admin);
        var shop = await OpenShopAsync(client, admin, UniqueName("Rated Rolls"));
        var customer = await SignInAsync(client);
        var courier = await CourierAsync(client, admin);

        var reviews = new List<JsonElement>();
        foreach (var rating in new[] { 5, 4, 3 })
        {
            var orderId = await DeliveredOrderAsync(client, shop, customer, courier);
            var review = await PostReviewAsync(client, customer, orderId, rating);
            reviews.Add(await PublishReviewAsync(client, admin, review));
        }

        var page = await PublicReviewsAsync(client, shop.BusinessId);
        Assert.Equal(4.0, page.GetProperty("rating").GetProperty("average").GetDouble());
        Assert.Equal(3, page.GetProperty("rating").GetProperty("count").GetInt32());
        var menu = await ReadAsync(await client.GetAsync($"/api/v1/restaurants/{shop.BusinessId}/menu"));
        Assert.Equal(4.0, menu.GetProperty("restaurant").GetProperty("rating").GetProperty("average").GetDouble());

        // A moderator takes one down, with a reason; it leaves the average and the author is told why.
        var down = reviews[2];
        var noReason = await SendAsync(client, HttpMethod.Post, $"{AdminReviews}/{Id(down)}/remove", admin, new { version = VersionOf(down), reason = " " });
        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);
        var removed = await ReadAsync(await SendAsync(client, HttpMethod.Post, $"{AdminReviews}/{Id(down)}/remove", admin, new { version = VersionOf(down), reason = "Reported as abusive." }));
        Assert.Equal("removed", removed.GetProperty("status").GetString());
        Assert.Equal(1, await AuditCountAsync("review.removed", Id(down)));
        Assert.Equal(JsonValueKind.Null, (await PublicReviewsAsync(client, shop.BusinessId)).GetProperty("rating").ValueKind);
        var mine = (await ReadAsync(await SendAsync(client, HttpMethod.Get, $"{Reviews}/mine", customer))).EnumerateArray().Single(r => Id(r) == Id(down));
        Assert.Equal("Reported as abusive.", mine.GetProperty("moderationReason").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await SendAsync(client, HttpMethod.Delete, $"{Reviews}/{Id(down)}", customer)).StatusCode);
    }

    [DatabaseFact]
    public async Task The_same_order_reviewed_from_several_requests_at_once_creates_one_review()
    {
        using var client = Factory.CreateClient();
        var admin = await AdministratorTokenAsync();
        await TurnFoodOnAsync(client, admin);
        var shop = await OpenShopAsync(client, admin, UniqueName("Race Rolls"));
        var customer = await SignInAsync(client);
        var courier = await CourierAsync(client, admin);
        var orderId = await DeliveredOrderAsync(client, shop, customer, courier);

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(i =>
            SendAsync(client, HttpMethod.Post, Reviews, customer, new { foodOrderId = orderId, rating = 1 + i % 5, text = $"Attempt {i}" })));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.All(responses.Where(r => r.StatusCode != HttpStatusCode.Created), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
        await using var scope = Factory.Services.CreateAsyncScope();
        Assert.Equal(1, await DbOf(scope).Reviews.AsNoTracking().CountAsync(r => r.FoodOrderId == Guid.Parse(orderId)));
    }

    [DatabaseFact]
    public async Task The_database_refuses_a_review_the_service_would_never_write()
    {
        using var client = Factory.CreateClient();
        var admin = await AdministratorTokenAsync();
        await TurnFoodOnAsync(client, admin);
        var shop = await OpenShopAsync(client, admin, UniqueName("Rules Rolls"));
        var customer = await SignInAsync(client);
        var courier = await CourierAsync(client, admin);
        var orderId = await DeliveredOrderAsync(client, shop, customer, courier);
        var review = await PostReviewAsync(client, customer, orderId, 4, "Fine.");

        await using var scope = Factory.Services.CreateAsyncScope();
        await using var sql = new NpgsqlConnection(DbOf(scope).Database.GetConnectionString());
        await sql.OpenAsync();

        async Task<string?> RefusedAsync(string text)
        {
            await using var command = new NpgsqlCommand(text, sql);
            command.Parameters.AddWithValue("id", Guid.Parse(Id(review)));
            var refused = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
            return refused.ConstraintName;
        }

        Assert.Equal("ck_review_rating", await RefusedAsync("UPDATE reviews SET rating = 6 WHERE id = @id"));
        Assert.Equal("ck_review_rating", await RefusedAsync("UPDATE reviews SET rating = 0 WHERE id = @id"));
        Assert.Equal("ck_review_text", await RefusedAsync("UPDATE reviews SET text = '' WHERE id = @id"));
        Assert.StartsWith("ck_review_", await RefusedAsync("UPDATE reviews SET status = 'bogus' WHERE id = @id"));

        // Published means a moderator and a time; a waiting review has neither; a rejected one needs its reason.
        Assert.Equal("ck_review_moderation", await RefusedAsync("UPDATE reviews SET status = 'published' WHERE id = @id"));
        Assert.Equal("ck_review_moderation", await RefusedAsync("UPDATE reviews SET published_at = now() WHERE id = @id"));
        Assert.Equal("ck_review_moderation", await RefusedAsync("UPDATE reviews SET status = 'rejected', moderated_by_user_id = customer_user_id, moderated_at = now() WHERE id = @id"));

        // A reply is all of its columns, and only on a review that was published.
        Assert.Equal("ck_review_reply", await RefusedAsync("UPDATE reviews SET reply_text = 'Thanks', replied_at = now(), replied_by_user_id = customer_user_id WHERE id = @id"));

        // A review is of exactly one booking or order, and a booking brings its listing.
        Assert.Equal("ck_review_subject", await RefusedAsync("UPDATE reviews SET food_order_id = NULL WHERE id = @id"));
        Assert.Equal("ck_review_subject", await RefusedAsync("UPDATE reviews SET listing_id = business_id WHERE id = @id"));

        // And a second review of the same order is refused by the unique index, whoever tries it.
        var duplicate = await RefusedAsync(
            "INSERT INTO reviews (id, customer_user_id, food_order_id, business_id, rating, author_name, status, transaction_completed_at, created_at, updated_at) "
            + "SELECT gen_random_uuid(), customer_user_id, food_order_id, business_id, 4, 'x', 'pending', now(), now(), now() FROM reviews WHERE id = @id");
        Assert.Equal("ux_reviews_food_order", duplicate);
    }
}
