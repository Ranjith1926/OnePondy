using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OnePondy.Application.Audit;
using OnePondy.Application.Common;
using OnePondy.Application.Food;
using OnePondy.Application.Identity;
using OnePondy.Application.Payments;
using OnePondy.Domain.Audit;
using OnePondy.Domain.Payments;

namespace OnePondy.Application.Delivery;

/// <summary>What a driver sees of the cash they hold for OnePondy.</summary>
public sealed record DriverCashView(decimal Held, decimal Limit, DateTimeOffset? LastCollectedAt, DateTimeOffset? LastDepositAt);

/// <summary>One driver's cash, as an administrator sees it.</summary>
public sealed record AdminDriverCashRow(
    Guid DriverUserId,
    string? DriverName,
    string? PhoneNumber,
    decimal Held,
    DateTimeOffset? LastCollectedAt,
    DateTimeOffset? LastDepositAt);

/// <summary>Cash a driver handed in. <c>ReceivedAt</c> is when, if not just now.</summary>
public sealed record DepositInput(decimal? Amount, DateTimeOffset? ReceivedAt, string? Reference, string? Note);

public static class CashErrors
{
    public static readonly AppError NoSuchDriver = new("driver_not_found", ErrorKind.NotFound, "That driver does not exist.");

    public static readonly AppError BalanceChanged = new(
        "cash_balance_changed",
        ErrorKind.Conflict,
        "This driver's cash changed while you were recording it (another deposit, or another delivery). Reload and check the amount.");
}

/// <summary>Reads what a driver holds. The ledger is the book: a driver's cash is the sum of their own account, nothing is kept twice.</summary>
public static class DriverCashBook
{
    public static async Task<decimal> HeldAsync(IOnePondyDbContext db, Guid driverUserId, CancellationToken cancellationToken)
    {
        var account = LedgerAccounts.DriverCash(driverUserId);
        return await db.LedgerEntries.AsNoTracking().Where(e => e.Account == account).SumAsync(e => e.Amount, cancellationToken);
    }
}

/// <summary>
/// Cash on delivery's cash (spec Phase 8, step 8.8). A driver who takes cash at the door holds it for OnePondy until they hand it in; an
/// administrator records each hand-in, which moves the amount from the driver's ledger account to the cash OnePondy has received. What a driver
/// owes can therefore never be edited away: it only goes down by a recorded, audited deposit, and never below nothing.
/// </summary>
public sealed class CashService(
    IOnePondyDbContext db,
    LedgerService ledger,
    AuditTrail audit,
    IUserAccountStore accounts,
    IOptions<FoodOptions> food,
    TimeProvider time)
{
    private const string AccountPrefix = "driver_cash:";

    // --- The driver ---

    public async Task<DriverCashView> GetMineAsync(Guid driverUserId, CancellationToken cancellationToken) =>
        new(
            await DriverCashBook.HeldAsync(db, driverUserId, cancellationToken),
            food.Value.CashOnDelivery.MaxDriverCashHeld,
            await LastCollectedAsync(driverUserId, cancellationToken),
            await LastDepositAsync(driverUserId, cancellationToken));

    // --- Administrators ---

    /// <summary>Every driver who has ever collected cash, largest balance first; by default only those still holding some.</summary>
    public async Task<IReadOnlyList<AdminDriverCashRow>> AdminListAsync(bool onlyHolding, CancellationToken cancellationToken)
    {
        var sums = await db.LedgerEntries.AsNoTracking()
            .Where(e => e.Account.StartsWith(AccountPrefix))
            .GroupBy(e => e.Account)
            .Select(g => new { Account = g.Key, Held = g.Sum(e => e.Amount) })
            .ToListAsync(cancellationToken);
        var held = sums
            .Select(s => (Id: Guid.Parse(s.Account[AccountPrefix.Length..]), s.Held))
            .Where(x => !onlyHolding || x.Held > 0m)
            .OrderByDescending(x => x.Held)
            .ToList();

        var rows = new List<AdminDriverCashRow>();
        foreach (var (id, amount) in held)
        {
            var profile = await db.DriverProfiles.AsNoTracking().SingleOrDefaultAsync(p => p.UserId == id, cancellationToken);
            var account = await accounts.GetProfileAsync(id, cancellationToken);
            rows.Add(new AdminDriverCashRow(
                id,
                profile?.FullName,
                account?.PhoneNumber,
                amount,
                await LastCollectedAsync(id, cancellationToken),
                await LastDepositAsync(id, cancellationToken)));
        }

        return rows;
    }

    /// <summary>Records cash a driver handed in. It cannot be more than they hold, so a balance never goes below nothing.</summary>
    public async Task<Result<AdminDriverCashRow>> RecordDepositAsync(
        Guid driverUserId, DepositInput input, Guid adminUserId, CancellationToken cancellationToken)
    {
        if (!await db.DriverProfiles.AsNoTracking().AnyAsync(p => p.UserId == driverUserId, cancellationToken))
        {
            return CashErrors.NoSuchDriver;
        }

        var now = time.GetUtcNow();
        var errors = new FieldErrors();
        if (input.Amount is not { } amount || amount <= 0m || amount > 1_000_000m || decimal.Round(amount, 2) != amount)
        {
            errors.Add("amount", "Enter the cash received, more than 0, with at most two decimal places.");
        }

        var receivedAt = input.ReceivedAt ?? now;
        if (receivedAt > now)
        {
            errors.Add("receivedAt", "The cash cannot have been received in the future.");
        }

        var reference = errors.Optional("reference", input.Reference, DriverCashDeposit.MaxReferenceLength);
        var note = errors.Optional("note", input.Note, DriverCashDeposit.MaxNoteLength);
        if (errors.Any)
        {
            return errors.ToError();
        }

        var held = await DriverCashBook.HeldAsync(db, driverUserId, cancellationToken);
        if (input.Amount!.Value > held)
        {
            errors.Add("amount", $"This driver holds only ₹{held:0.00}.");
            return errors.ToError();
        }

        var deposit = DriverCashDeposit.Create(driverUserId, input.Amount.Value, receivedAt, adminUserId, reference, note, now);
        db.DriverCashDeposits.Add(deposit);
        await ledger.PostCashDepositAsync(deposit, cancellationToken);
        audit.Record(
            adminUserId,
            AuditActions.DriverCashDepositRecorded,
            AuditEntityTypes.DriverCash,
            driverUserId,
            new { deposit.Amount, deposit.Reference, depositId = deposit.Id });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (WriteConflicts.Is(ex) || WriteConflicts.IsDatabaseRule(ex))
        {
            // The database refused: someone recorded a deposit for the same cash at the same moment, so this one would take the balance below nothing.
            db.ClearTracked();
            return CashErrors.BalanceChanged;
        }

        return (await AdminListAsync(onlyHolding: false, cancellationToken)).Single(r => r.DriverUserId == driverUserId);
    }

    // --- Shared ---

    private async Task<DateTimeOffset?> LastCollectedAsync(Guid driverUserId, CancellationToken cancellationToken) =>
        await db.FoodOrders.AsNoTracking()
            .Where(o => o.CashCollectedBy == driverUserId)
            .MaxAsync(o => o.CashCollectedAt, cancellationToken);

    private async Task<DateTimeOffset?> LastDepositAsync(Guid driverUserId, CancellationToken cancellationToken) =>
        await db.DriverCashDeposits.AsNoTracking()
            .Where(d => d.DriverUserId == driverUserId)
            .MaxAsync(d => (DateTimeOffset?)d.ReceivedAt, cancellationToken);
}
