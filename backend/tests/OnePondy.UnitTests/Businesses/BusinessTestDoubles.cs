using Microsoft.Extensions.Time.Testing;
using OnePondy.Application.Audit;
using OnePondy.Application.Businesses;
using OnePondy.Domain.Businesses;
using OnePondy.Domain.Identity;
using OnePondy.UnitTests.Identity;

namespace OnePondy.UnitTests.Businesses;

/// <summary>In-memory stand-in for document storage; records what was saved so tests can assert on it.</summary>
public sealed class InMemoryBusinessDocumentStorage : IBusinessDocumentStorage
{
    public Dictionary<string, byte[]> Files { get; } = [];

    public Task SaveAsync(string path, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        Files[path] = content.ToArray();
        return Task.CompletedTask;
    }

    public Task<byte[]?> ReadAsync(string path, CancellationToken cancellationToken) =>
        Task.FromResult(Files.GetValueOrDefault(path));

    public Task DeleteAsync(string path, CancellationToken cancellationToken)
    {
        Files.Remove(path);
        return Task.CompletedTask;
    }
}

/// <summary>The business service wired to in-memory doubles, with one customer and one administrator seeded.</summary>
public sealed class BusinessHarness
{
    public BusinessHarness()
    {
        Accounts = new FakeUserAccountStore(Time);
        Audit = new AuditTrail(Db, Time);
        Storage = new InMemoryBusinessDocumentStorage();
        Businesses = new BusinessService(Db, Storage, Audit, Time);

        Customer = Accounts.FindOrCreateAsync(OtpChannel.Sms, "+919876543210", CancellationToken.None).Result.Id;
        Administrator = Accounts.FindOrCreateAsync(OtpChannel.Email, "admin@onepondy.test", CancellationToken.None).Result.Id;
        Accounts.SetStaffRole(Administrator, RoleNames.Administrator);
    }

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 10, 26, 4, 0, 0, TimeSpan.Zero));

    public TestDbContext Db { get; } = TestDbContext.Create();

    public FakeUserAccountStore Accounts { get; }

    public AuditTrail Audit { get; }

    public InMemoryBusinessDocumentStorage Storage { get; }

    public BusinessService Businesses { get; }

    public Guid Customer { get; }

    public Guid Administrator { get; }

    public static BusinessApplicationInput Input(
        string name = "Pondy Paddle Tours",
        BusinessType businessType = BusinessType.Activity,
        string? fssaiLicenseNumber = null) =>
        new(name, businessType, "Kayak tours of the backwaters.", "9876543210", "owner@example.com", "Chunnambar", 11.89, 79.83, "33AAAAA0000A1Z5", fssaiLicenseNumber);
}
