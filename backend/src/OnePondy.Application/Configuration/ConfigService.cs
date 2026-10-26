using Microsoft.EntityFrameworkCore;
using OnePondy.Application.Audit;
using OnePondy.Application.Common;
using OnePondy.Domain.Audit;
using OnePondy.Domain.Configuration;

namespace OnePondy.Application.Configuration;

/// <summary>What <c>/api/v1/config</c> returns to the app (spec §8).</summary>
public sealed record AppConfig(IReadOnlyDictionary<string, ModuleStatus> Modules, string MinSupportedAppVersion);

public sealed record ModuleFlagView(string Key, ModuleStatus Status, DateTimeOffset? UpdatedAt);

public sealed record AdminConfig(IReadOnlyList<ModuleFlagView> Modules, string MinSupportedAppVersion);

/// <summary>
/// Module flags and the minimum app version. Only administrators change them (spec §10, Phase 3:
/// a content editor cannot change configuration); the API enforces that by role.
/// </summary>
public sealed class ConfigService(IOnePondyDbContext db, AuditTrail audit, TimeProvider time)
{
    /// <summary>Used until an administrator sets a version; matches the first app release.</summary>
    public const string DefaultMinSupportedAppVersion = "0.1.0";

    public async Task<AppConfig> GetAsync(CancellationToken cancellationToken)
    {
        var flags = await ModulesAsync(cancellationToken);
        return new AppConfig(
            flags.ToDictionary(f => f.Key, f => f.Status, StringComparer.Ordinal),
            await MinVersionAsync(cancellationToken));
    }

    public async Task<AdminConfig> GetAdminAsync(CancellationToken cancellationToken) =>
        new(await ModulesAsync(cancellationToken), await MinVersionAsync(cancellationToken));

    public async Task<Result<ModuleFlagView>> SetModuleStatusAsync(
        string key,
        ModuleStatus status,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (!ModuleKeys.IsKnown(key))
        {
            return new AppError("unknown_module", ErrorKind.NotFound, $"There is no module called '{key}'.");
        }

        if (!Enum.IsDefined(status))
        {
            return AppError.Validation(new Dictionary<string, string[]> { ["status"] = ["Choose a status."] });
        }

        var flag = await db.ModuleFlags.SingleOrDefaultAsync(f => f.Key == key, cancellationToken);
        if (flag is null)
        {
            flag = new ModuleFlag(key, ModuleStatus.ComingSoon);
            db.ModuleFlags.Add(flag);
        }

        var before = flag.Status;
        if (before != status)
        {
            flag.Change(status, userId, time.GetUtcNow());
            audit.Record(userId, AuditActions.ModuleFlagChanged, AuditEntityTypes.ModuleFlag, key, new { from = before, to = status });
            await db.SaveChangesAsync(cancellationToken);
        }

        return new ModuleFlagView(flag.Key, flag.Status, flag.UpdatedAt);
    }

    public async Task<Result<string>> SetMinSupportedAppVersionAsync(string? version, Guid userId, CancellationToken cancellationToken)
    {
        if (!AppVersion.TryParse(version, out var parsed))
        {
            return AppError.Validation(new Dictionary<string, string[]>
            {
                ["minSupportedAppVersion"] = ["Enter a version like 1.2.0."],
            });
        }

        var value = parsed.ToString();
        var setting = await db.AppSettings.SingleOrDefaultAsync(s => s.Key == AppSetting.MinSupportedAppVersion, cancellationToken);
        if (setting is null)
        {
            setting = new AppSetting(AppSetting.MinSupportedAppVersion, DefaultMinSupportedAppVersion);
            db.AppSettings.Add(setting);
        }

        var before = setting.Value;
        if (before != value)
        {
            setting.Change(value, userId, time.GetUtcNow());
            audit.Record(userId, AuditActions.MinAppVersionChanged, AuditEntityTypes.AppSetting, setting.Key, new { from = before, to = value });
            await db.SaveChangesAsync(cancellationToken);
        }

        return value;
    }

    private async Task<List<ModuleFlagView>> ModulesAsync(CancellationToken cancellationToken)
    {
        var stored = await db.ModuleFlags.AsNoTracking().ToListAsync(cancellationToken);

        // Every known module is listed; one without a stored flag stays "coming soon", never available.
        return
        [
            .. ModuleKeys.All.Select(key => stored.SingleOrDefault(f => f.Key == key) is { } flag
                ? new ModuleFlagView(key, flag.Status, flag.UpdatedAt)
                : new ModuleFlagView(key, ModuleStatus.ComingSoon, null)),
        ];
    }

    private async Task<string> MinVersionAsync(CancellationToken cancellationToken) =>
        await db.AppSettings
            .AsNoTracking()
            .Where(s => s.Key == AppSetting.MinSupportedAppVersion)
            .Select(s => s.Value)
            .SingleOrDefaultAsync(cancellationToken)
        ?? DefaultMinSupportedAppVersion;
}
