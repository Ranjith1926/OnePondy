using System.Text.Json;
using OnePondy.Application.Common;
using OnePondy.Domain.Audit;

namespace OnePondy.Application.Audit;

/// <summary>
/// Adds audit entries to the current unit of work, so they are saved in the same transaction as the
/// change they describe.
/// </summary>
public sealed class AuditTrail(IOnePondyDbContext db, TimeProvider time)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public void Record(Guid? actorUserId, string action, string entityType, object entityId, object? details = null)
    {
        var json = details is null ? null : JsonSerializer.Serialize(details, JsonOptions);
        db.AuditEntries.Add(AuditEntry.Record(
            actorUserId,
            action,
            entityType,
            Convert.ToString(entityId, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            json,
            time.GetUtcNow()));
    }
}
