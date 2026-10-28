using OnePondy.Domain.Itineraries;

namespace OnePondy.UnitTests.Domain;

public class ItineraryDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 4, 0, 0, TimeSpan.Zero);
    private static readonly Guid UserId = Guid.NewGuid();

    private static Itinerary Manual(string title = "Weekend in Pondy") =>
        Itinerary.Create(UserId, title, new DateRange(null, null), ItinerarySource.Manual, Now);

    [Fact]
    public void A_manual_itinerary_is_saved_immediately_but_an_ai_assisted_one_starts_as_a_draft()
    {
        var manual = Manual();
        var aiAssisted = Itinerary.Create(UserId, "AI weekend", new DateRange(null, null), ItinerarySource.AiAssisted, Now);

        Assert.Equal(ItineraryStatus.Saved, manual.Status);
        Assert.Equal(ItineraryStatus.Draft, aiAssisted.Status);

        aiAssisted.Save(Now);
        Assert.Equal(ItineraryStatus.Saved, aiAssisted.Status);
    }

    [Fact]
    public void Saving_an_already_saved_itinerary_does_nothing()
    {
        var itinerary = Manual();

        itinerary.Save(Now.AddHours(1));

        Assert.Equal(Now, itinerary.UpdatedAt);
    }

    [Fact]
    public void Dates_must_be_in_order()
    {
        var start = new DateOnly(2026, 12, 1);
        var end = new DateOnly(2026, 11, 30);

        Assert.Throws<ArgumentException>(() =>
            Itinerary.Create(UserId, "Trip", new DateRange(start, end), ItinerarySource.Manual, Now));
    }

    [Fact]
    public void Stops_are_numbered_in_order_within_each_day()
    {
        var itinerary = Manual();

        var first = itinerary.AddStop(Guid.NewGuid(), 1, null, null, Now);
        var second = itinerary.AddStop(Guid.NewGuid(), 1, null, null, Now);
        var dayTwo = itinerary.AddStop(Guid.NewGuid(), 2, null, null, Now);

        Assert.Equal(0, first.StopOrder);
        Assert.Equal(1, second.StopOrder);
        Assert.Equal(0, dayTwo.StopOrder);
    }

    [Fact]
    public void Removing_a_stop_that_does_not_exist_is_a_no_op()
    {
        var itinerary = Manual();
        itinerary.AddStop(Guid.NewGuid(), 1, null, null, Now);

        itinerary.RemoveStop(Guid.NewGuid(), Now.AddMinutes(1));

        Assert.Single(itinerary.Stops);
        Assert.Equal(Now, itinerary.UpdatedAt);
    }

    [Fact]
    public void Reordering_applies_every_position_together_even_through_transient_collisions()
    {
        var itinerary = Manual();
        var a = itinerary.AddStop(Guid.NewGuid(), 1, null, null, Now);
        var b = itinerary.AddStop(Guid.NewGuid(), 1, null, null, Now);

        // Swap: 'a' takes what was b's position and vice versa. In a real database this only
        // succeeds because the uniqueness check is deferred to commit (spec §17.3).
        itinerary.Reorder([(a.Id, 1, 1), (b.Id, 1, 0)], Now.AddMinutes(5));

        Assert.Equal(1, a.StopOrder);
        Assert.Equal(0, b.StopOrder);
    }

    [Fact]
    public void Reordering_rejects_a_stop_from_another_itinerary_and_changes_nothing()
    {
        var itinerary = Manual();
        var stop = itinerary.AddStop(Guid.NewGuid(), 1, null, null, Now);
        var foreignStopId = Guid.NewGuid();

        Assert.Throws<ArgumentException>(() => itinerary.Reorder([(stop.Id, 1, 0), (foreignStopId, 1, 1)], Now.AddMinutes(5)));

        Assert.Equal(0, stop.StopOrder);
        Assert.Equal(Now, itinerary.UpdatedAt);
    }

    [Fact]
    public void Reordering_rejects_the_same_stop_listed_twice()
    {
        var itinerary = Manual();
        var stop = itinerary.AddStop(Guid.NewGuid(), 1, null, null, Now);

        Assert.Throws<ArgumentException>(() => itinerary.Reorder([(stop.Id, 1, 0), (stop.Id, 2, 0)], Now));
    }

    [Fact]
    public void A_shared_link_can_be_turned_off_and_re_enabling_replaces_it()
    {
        var itinerary = Manual();
        var tokens = new Queue<string>(["token-1", "token-2"]);

        var first = itinerary.EnableSharing(tokens.Dequeue, Now);
        Assert.Equal("token-1", itinerary.ShareToken);

        var second = itinerary.EnableSharing(tokens.Dequeue, Now.AddMinutes(1));
        Assert.Equal("token-2", itinerary.ShareToken);
        Assert.NotEqual(first, second);

        itinerary.DisableSharing(Now.AddMinutes(2));
        Assert.Null(itinerary.ShareToken);
    }

    [Fact]
    public void Archiving_and_unarchiving_round_trip()
    {
        var itinerary = Manual();

        itinerary.Archive(Now);
        Assert.Equal(ItineraryStatus.Archived, itinerary.Status);

        itinerary.Unarchive(Now.AddMinutes(1));
        Assert.Equal(ItineraryStatus.Saved, itinerary.Status);
    }

    [Fact]
    public void A_full_itinerary_refuses_another_stop()
    {
        var itinerary = Manual();
        for (var i = 0; i < Itinerary.MaxStops; i++)
        {
            itinerary.AddStop(Guid.NewGuid(), 1, null, null, Now);
        }

        Assert.Throws<InvalidOperationException>(() => itinerary.AddStop(Guid.NewGuid(), 1, null, null, Now));
    }
}
