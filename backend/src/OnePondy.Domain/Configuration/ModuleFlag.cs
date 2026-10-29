namespace OnePondy.Domain.Configuration;

/// <summary>How the customer app presents a service module (spec §4: never "available" before it is).</summary>
public enum ModuleStatus
{
    Available = 1,
    ComingSoon = 2,
    Hidden = 3,
}

/// <summary>Module keys the app knows. They match the app's own keys, so the API can return them as is.</summary>
public static class ModuleKeys
{
    /// <summary>Online payment for bookings. While it is not available, a partner's acceptance confirms the booking directly.</summary>
    public const string Payments = "payments";

    /// <summary>Food ordering. Placing an order needs it to be available; browsing restaurants does not.</summary>
    public const string Food = "food";

    public static readonly IReadOnlyList<string> All =
        [Food, "rides", "stays", "activities", "tripPlanner", "notifications", "support", Payments];

    public static bool IsKnown(string key) => All.Contains(key);
}

/// <summary>A switch the console changes to show, hide or announce a module without an app release.</summary>
public sealed class ModuleFlag
{
    private ModuleFlag()
    {
    }

    public ModuleFlag(string key, ModuleStatus status)
    {
        Key = key;
        Status = status;
    }

    public string Key { get; private set; } = string.Empty;

    public ModuleStatus Status { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public Guid? UpdatedByUserId { get; private set; }

    public void Change(ModuleStatus status, Guid userId, DateTimeOffset now)
    {
        Status = status;
        UpdatedAt = now;
        UpdatedByUserId = userId;
    }
}

/// <summary>A named configuration value, such as the minimum supported app version.</summary>
public sealed class AppSetting
{
    public const string MinSupportedAppVersion = "min_supported_app_version";

    /// <summary>The last day (UTC, as <c>yyyy-MM-dd</c>) the daily payment reconciliation has completed for.</summary>
    public const string PaymentsReconciledThrough = "payments_reconciled_through";

    private AppSetting()
    {
    }

    public AppSetting(string key, string value)
    {
        Key = key;
        Value = value;
    }

    public string Key { get; private set; } = string.Empty;

    public string Value { get; private set; } = string.Empty;

    public DateTimeOffset? UpdatedAt { get; private set; }

    public Guid? UpdatedByUserId { get; private set; }

    public void Change(string value, Guid userId, DateTimeOffset now)
    {
        Value = value;
        UpdatedAt = now;
        UpdatedByUserId = userId;
    }

    /// <summary>A change made by the system itself, such as a background job recording how far it has got.</summary>
    public void ChangeBySystem(string value, DateTimeOffset now)
    {
        Value = value;
        UpdatedAt = now;
        UpdatedByUserId = null;
    }
}

/// <summary>A "major.minor.patch" app version, as in app.json.</summary>
public readonly record struct AppVersion(int Major, int Minor, int Patch) : IComparable<AppVersion>
{
    public static bool TryParse(string? value, out AppVersion version)
    {
        version = default;
        var parts = value?.Trim().Split('.');
        if (parts is not { Length: 3 })
        {
            return false;
        }

        var numbers = new int[3];
        for (var i = 0; i < 3; i++)
        {
            if (parts[i].Length is 0 or > 5 || !parts[i].All(char.IsAsciiDigit) || !int.TryParse(parts[i], out numbers[i]))
            {
                return false;
            }
        }

        version = new AppVersion(numbers[0], numbers[1], numbers[2]);
        return true;
    }

    public int CompareTo(AppVersion other) =>
        Major != other.Major ? Major.CompareTo(other.Major)
        : Minor != other.Minor ? Minor.CompareTo(other.Minor)
        : Patch.CompareTo(other.Patch);

    public override string ToString() => $"{Major}.{Minor}.{Patch}";
}
