using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using OnePondy.Application.Common;
using OnePondy.Application.Identity;
using OnePondy.Domain.Audit;
using OnePondy.Domain.Bookings;
using OnePondy.Domain.Businesses;
using OnePondy.Domain.Configuration;
using OnePondy.Domain.Identity;
using OnePondy.Domain.Itineraries;
using OnePondy.Domain.Listings;
using OnePondy.Domain.Places;
using OnePondy.Domain.TripPlanner;
using OnePondy.Infrastructure.Persistence;
using OnePondy.Infrastructure.Security;

namespace OnePondy.UnitTests.Identity;

public static class TestKeys
{
    public static readonly string Signing = Convert.ToBase64String(Encoding.UTF8.GetBytes(new string('s', 32)));
    public static readonly string Hashing = Convert.ToBase64String(Encoding.UTF8.GetBytes(new string('h', 32)));
}

/// <summary>In-memory stand-in for the application's persistence interface.</summary>
public sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options), IOnePondyDbContext
{
    public DbSet<OtpChallenge> OtpChallenges => Set<OtpChallenge>();

    public DbSet<UserSession> UserSessions => Set<UserSession>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<Place> Places => Set<Place>();

    public DbSet<PlaceCategory> PlaceCategories => Set<PlaceCategory>();

    public DbSet<PlaceMedia> PlaceMedia => Set<PlaceMedia>();

    public DbSet<ModuleFlag> ModuleFlags => Set<ModuleFlag>();

    public DbSet<AppSetting> AppSettings => Set<AppSetting>();

    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    public DbSet<SavedPlace> SavedPlaces => Set<SavedPlace>();

    public DbSet<Itinerary> Itineraries => Set<Itinerary>();

    public DbSet<TripPlannerRequestLog> TripPlannerRequestLogs => Set<TripPlannerRequestLog>();

    public DbSet<Business> Businesses => Set<Business>();

    public DbSet<Listing> Listings => Set<Listing>();

    public DbSet<AvailabilitySlot> AvailabilitySlots => Set<AvailabilitySlot>();

    public DbSet<SavedListing> SavedListings => Set<SavedListing>();

    public DbSet<Booking> Bookings => Set<Booking>();

    public DbSet<BookingSlot> BookingSlots => Set<BookingSlot>();

    public static TestDbContext Create() =>
        new(new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    // The in-memory provider has no xmin, so version checks are covered by the PostgreSQL integration tests.
    public void ExpectVersion<TEntity>(TEntity entity, uint version)
        where TEntity : class
    {
    }

    public void MarkAdded<TEntity>(TEntity entity)
        where TEntity : class =>
        Entry(entity).State = EntityState.Added;

    public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken) =>
        Database.BeginTransactionAsync(cancellationToken);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OtpChallenge>().HasKey(c => c.Id);
        modelBuilder.Entity<UserSession>().HasKey(s => s.Id);
        modelBuilder.Entity<RefreshToken>().HasKey(t => t.Id);
        modelBuilder.Entity<PlaceCategory>().HasKey(c => c.Code);
        modelBuilder.Entity<PlaceMedia>().HasKey(m => m.Id);
        modelBuilder.Entity<ModuleFlag>().HasKey(f => f.Key);
        modelBuilder.Entity<AppSetting>().HasKey(s => s.Key);
        modelBuilder.Entity<AuditEntry>().HasKey(e => e.Id);
        modelBuilder.Entity<Place>(place =>
        {
            place.HasKey(p => p.Id);
            place.Ignore(p => p.Location);
            place.Property(p => p.OpeningHours).HasConversion(OpeningHoursJson.Converter, OpeningHoursJson.Comparer);
            place.OwnsMany(p => p.Translations, translation =>
            {
                translation.WithOwner().HasForeignKey("PlaceId");
                translation.HasKey("PlaceId", nameof(PlaceTranslation.Language));
            });
            place.OwnsMany(p => p.EntryFees, fee =>
            {
                fee.WithOwner().HasForeignKey("PlaceId");
                fee.HasKey("PlaceId", nameof(PlaceEntryFee.Category));
            });
            place.Navigation(p => p.Translations).UsePropertyAccessMode(PropertyAccessMode.Field);
            place.Navigation(p => p.EntryFees).UsePropertyAccessMode(PropertyAccessMode.Field);
        });
        modelBuilder.Entity<SavedPlace>().HasKey(s => new { s.UserId, s.PlaceId });
        modelBuilder.Entity<Itinerary>(itinerary =>
        {
            itinerary.HasKey(i => i.Id);
            itinerary.OwnsMany(i => i.Stops, stop =>
            {
                stop.WithOwner().HasForeignKey("ItineraryId");
                stop.HasKey(s => s.Id);
            });
            itinerary.Navigation(i => i.Stops).UsePropertyAccessMode(PropertyAccessMode.Field);
        });
        modelBuilder.Entity<Business>(business =>
        {
            business.HasKey(b => b.Id);
            business.OwnsMany(b => b.Documents, document =>
            {
                document.WithOwner().HasForeignKey("BusinessId");
                document.HasKey(d => d.Id);
            });
            business.Navigation(b => b.Documents).UsePropertyAccessMode(PropertyAccessMode.Field);
        });
        modelBuilder.Entity<Listing>(listing =>
        {
            listing.HasKey(l => l.Id);
            listing.HasAlternateKey(l => new { l.Id, l.BusinessId });
            listing.Property(l => l.CancellationPolicy).HasConversion(CancellationPolicyJson.Converter, CancellationPolicyJson.Comparer);
        });
        modelBuilder.Entity<AvailabilitySlot>().HasKey(s => s.Id);
        modelBuilder.Entity<SavedListing>().HasKey(s => new { s.UserId, s.ListingId });
        modelBuilder.Entity<Booking>(booking =>
        {
            booking.HasKey(b => b.Id);
            booking.HasOne<Listing>().WithMany().HasForeignKey(b => new { b.ListingId, b.BusinessId }).HasPrincipalKey(l => new { l.Id, l.BusinessId });
            booking.Property(b => b.PriceSnapshot).HasConversion(PriceSnapshotJson.Converter, PriceSnapshotJson.Comparer);
            booking.Property(b => b.CancellationPolicySnapshot).HasConversion(CancellationPolicyJson.RequiredConverter, CancellationPolicyJson.RequiredComparer);
            booking.OwnsMany(b => b.History, history =>
            {
                history.WithOwner().HasForeignKey("BookingId");
                history.HasKey(h => h.Id);
            });
            booking.Navigation(b => b.History).UsePropertyAccessMode(PropertyAccessMode.Field);
        });
        modelBuilder.Entity<BookingSlot>().HasKey(bs => new { bs.BookingId, bs.SlotId });
    }
}

public sealed class CapturingOtpSender : IOtpSender
{
    public Dictionary<string, string> LastCodes { get; } = [];

    public List<string> DeletionConfirmations { get; } = [];

    public bool Fail { get; set; }

    public Task SendCodeAsync(
        OtpChannel channel,
        string destination,
        string code,
        OtpPurpose purpose,
        CancellationToken cancellationToken)
    {
        if (Fail)
        {
            throw new OtpDeliveryException("Provider down");
        }

        LastCodes[destination] = code;
        return Task.CompletedTask;
    }

    public Task SendAccountDeletedAsync(OtpChannel channel, string destination, CancellationToken cancellationToken)
    {
        DeletionConfirmations.Add(destination);
        return Task.CompletedTask;
    }
}

public sealed class FakeUserAccountStore(TimeProvider time) : IUserAccountStore
{
    private sealed class StoredAccount
    {
        public required Guid Id { get; init; }
        public required AccountContact Contact { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public string Language { get; set; } = "en";
        public string? StaffRole { get; set; }
        public DateTimeOffset? DeletedAt { get; set; }
        public DateTimeOffset CreatedAt { get; init; }
    }

    private readonly List<StoredAccount> _accounts = [];

    public Task<UserAccount> FindOrCreateAsync(OtpChannel channel, string destination, CancellationToken cancellationToken)
    {
        if (Find(channel, destination) is { } existing)
        {
            return Task.FromResult(ToAccount(existing));
        }

        var created = new StoredAccount
        {
            Id = Guid.NewGuid(),
            Contact = new AccountContact(channel, destination),
            CreatedAt = time.GetUtcNow(),
        };
        _accounts.Add(created);
        return Task.FromResult(ToAccount(created) with { IsNew = true });
    }

    public Task<UserAccount?> FindByContactAsync(OtpChannel channel, string destination, CancellationToken cancellationToken) =>
        Task.FromResult(Find(channel, destination) is { } account ? ToAccount(account) : null);

    public Task<UserAccount?> FindActiveAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(Active(userId) is { } account ? ToAccount(account) : null);

    public Task<AccountProfile?> GetProfileAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(Active(userId) is { } account ? ToProfile(account) : null);

    public Task<AccountProfile?> UpdateProfileAsync(
        Guid userId,
        string? displayName,
        string? preferredLanguage,
        CancellationToken cancellationToken)
    {
        if (Active(userId) is not { } account)
        {
            return Task.FromResult<AccountProfile?>(null);
        }

        account.DisplayName = displayName ?? account.DisplayName;
        account.Language = preferredLanguage ?? account.Language;
        return Task.FromResult<AccountProfile?>(ToProfile(account));
    }

    public Task<AccountContact?> GetContactAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(Active(userId)?.Contact);

    public Task AnonymizeAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var account = _accounts.Single(a => a.Id == userId);
        account.DeletedAt = now;
        account.DisplayName = "Deleted user";
        return Task.CompletedTask;
    }

    public bool IsDeleted(Guid userId) => _accounts.Single(a => a.Id == userId).DeletedAt is not null;

    public void SetStaffRole(Guid userId, string? role) => _accounts.Single(a => a.Id == userId).StaffRole = role;

    public IEnumerable<OnePondy.Application.Staff.StaffMember> StaffMembers() =>
        _accounts
            .Where(a => a.DeletedAt is null && a.StaffRole is not null)
            .Select(a => new OnePondy.Application.Staff.StaffMember(
                a.Id,
                NameOf(a.Id)!,
                a.Contact.Channel == OtpChannel.Email ? a.Contact.Destination : null,
                a.StaffRole!));

    public string? NameOf(Guid userId) => _accounts.SingleOrDefault(a => a.Id == userId) is { } account
        ? (account.DisplayName.Length > 0 ? account.DisplayName : account.Contact.Destination)
        : null;

    private StoredAccount? Find(OtpChannel channel, string destination) =>
        _accounts.SingleOrDefault(a => a.DeletedAt is null && a.Contact == new AccountContact(channel, destination));

    private StoredAccount? Active(Guid userId) => _accounts.SingleOrDefault(a => a.Id == userId && a.DeletedAt is null);

    private static UserAccount ToAccount(StoredAccount account) =>
        new(account.Id, account.DisplayName, account.StaffRole is { } role ? ["customer", role] : ["customer"]);

    private static AccountProfile ToProfile(StoredAccount account) => new(
        account.Id,
        account.DisplayName,
        account.Contact.Channel == OtpChannel.Sms ? account.Contact.Destination : null,
        account.Contact.Channel == OtpChannel.Email ? account.Contact.Destination : null,
        account.Language,
        account.CreatedAt);
}

public sealed class FakeAccessTokenIssuer(TimeProvider time) : IAccessTokenIssuer
{
    public AccessToken Issue(Guid userId, IReadOnlyCollection<string> roles, Guid sessionId) =>
        new($"access:{userId}:{sessionId}", time.GetUtcNow().AddMinutes(15));
}

/// <summary>A guard whose blockers can change mid-test, like a booking made after a code was sent.</summary>
public sealed class ToggleDeletionGuard : IAccountDeletionGuard
{
    public IReadOnlyList<string> Reasons { get; set; } = [];

    public Task<IReadOnlyList<string>> GetBlockersAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(Reasons);
}

public sealed class BlockingDeletionGuard(params string[] reasons) : IAccountDeletionGuard
{
    public Task<IReadOnlyList<string>> GetBlockersAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<string>>(reasons);
}

/// <summary>Wires the real application services to in-memory doubles and a controllable clock.</summary>
public sealed class AuthHarness
{
    public AuthHarness(OtpOptions? otpOptions = null, IAccountDeletionGuard? deletionGuard = null)
    {
        var otp = Options.Create(otpOptions ?? new OtpOptions());
        var sessions = Options.Create(new SessionOptions());
        Hasher = new HmacSecretHasher(Options.Create(new SecretHashingOptions { HashingKey = TestKeys.Hashing }));
        Accounts = new FakeUserAccountStore(Time);

        Challenges = new OtpChallengeService(Db, Sender, Hasher, otp, Time, NullLogger<OtpChallengeService>.Instance);
        Auth = new AuthService(
            Db,
            Challenges,
            Accounts,
            new FakeAccessTokenIssuer(Time),
            Hasher,
            sessions,
            Time,
            NullLogger<AuthService>.Instance);
        AccountService = new AccountService(
            Db,
            Challenges,
            Accounts,
            deletionGuard ?? new NoCommitmentsDeletionGuard(),
            Sender,
            Hasher,
            Time,
            NullLogger<AccountService>.Instance);
    }

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 10, 26, 4, 0, 0, TimeSpan.Zero));

    public TestDbContext Db { get; } = TestDbContext.Create();

    public CapturingOtpSender Sender { get; } = new();

    public HmacSecretHasher Hasher { get; }

    public FakeUserAccountStore Accounts { get; }

    public OtpChallengeService Challenges { get; }

    public AuthService Auth { get; }

    public AccountService AccountService { get; }

    /// <summary>Requests and verifies a sign-in code, returning the issued session tokens.</summary>
    public async Task<SessionTokens> SignInAsync(string phone = "9876543210")
    {
        var requested = await Auth.RequestSignInCodeAsync(OtpChannel.Sms, phone, CancellationToken.None);
        Assert.True(requested.IsSuccess, requested.Error?.Code);
        var code = Sender.LastCodes[ContactNormalizer.NormalizePhone(phone)!];
        var signedIn = await Auth.VerifySignInCodeAsync(requested.Value.ChallengeId, code, "Test device", CancellationToken.None);
        Assert.True(signedIn.IsSuccess, signedIn.Error?.Code);
        return signedIn.Value;
    }

    public static Guid SessionIdOf(SessionTokens tokens) => Guid.Parse(tokens.AccessToken.Value.Split(':')[2]);
}
