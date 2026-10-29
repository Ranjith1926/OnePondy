namespace OnePondy.Domain.Audit;

/// <summary>
/// One recorded change to content or configuration (spec §10, Phase 3). Append-only: the database
/// rejects updates and deletes on this table.
/// </summary>
public sealed class AuditEntry
{
    private AuditEntry()
    {
    }

    public Guid Id { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    /// <summary>The staff member who made the change; null for system changes.</summary>
    public Guid? ActorUserId { get; private set; }

    /// <summary>What happened, e.g. "place.verified". See <see cref="AuditActions"/>.</summary>
    public string Action { get; private set; } = string.Empty;

    public string EntityType { get; private set; } = string.Empty;

    public string EntityId { get; private set; } = string.Empty;

    /// <summary>JSON with the relevant before/after values or notes; never personal contact data.</summary>
    public string? Details { get; private set; }

    public static AuditEntry Record(
        Guid? actorUserId,
        string action,
        string entityType,
        string entityId,
        string? details,
        DateTimeOffset now) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            OccurredAt = now,
            ActorUserId = actorUserId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Details = details,
        };
}

public static class AuditEntityTypes
{
    public const string Place = "place";
    public const string Media = "media";
    public const string ModuleFlag = "module_flag";
    public const string AppSetting = "app_setting";
    public const string Staff = "staff";
    public const string Business = "business";
    public const string Listing = "listing";
    public const string ReconciliationException = "reconciliation_exception";
    public const string Settlement = "settlement";
    public const string Restaurant = "restaurant";
    public const string MenuCategory = "menu_category";
    public const string MenuItem = "menu_item";
}

public static class AuditActions
{
    public const string PlaceCreated = "place.created";
    public const string PlaceUpdated = "place.updated";
    public const string PlaceSubmitted = "place.submitted";
    public const string PlaceVerified = "place.verified";
    public const string PlaceRejected = "place.rejected";
    public const string PlacePublished = "place.published";
    public const string PlaceUnpublished = "place.unpublished";
    public const string PlaceReviewConfirmed = "place.review_confirmed";
    public const string ReconciliationExceptionResolved = "reconciliation_exception.resolved";
    public const string SettlementDrafted = "settlement.drafted";
    public const string SettlementApproved = "settlement.approved";
    public const string SettlementPaid = "settlement.paid";
    public const string SettlementDiscarded = "settlement.discarded";
    public const string RestaurantUpdated = "restaurant.updated";
    public const string MenuCategoryCreated = "menu_category.created";
    public const string MenuCategoryUpdated = "menu_category.updated";
    public const string MenuCategoryDeleted = "menu_category.deleted";
    public const string MenuItemCreated = "menu_item.created";
    public const string MenuItemUpdated = "menu_item.updated";
    public const string MenuItemArchived = "menu_item.archived";
    public const string MediaAdded = "media.added";
    public const string MediaUpdated = "media.updated";
    public const string MediaRemoved = "media.removed";
    public const string ModuleFlagChanged = "config.module_flag_changed";
    public const string MinAppVersionChanged = "config.min_app_version_changed";
    public const string StaffRoleGranted = "staff.role_granted";
    public const string StaffRoleRevoked = "staff.role_revoked";
    public const string BusinessApplied = "business.applied";
    public const string BusinessUpdated = "business.updated";
    public const string BusinessApproved = "business.approved";
    public const string BusinessRejected = "business.rejected";
    public const string BusinessSuspended = "business.suspended";
    public const string BusinessReinstated = "business.reinstated";
    public const string BusinessDocumentAdded = "business.document_added";
    public const string BusinessDocumentRemoved = "business.document_removed";
    public const string ListingCreated = "listing.created";
    public const string ListingUpdated = "listing.updated";
    public const string ListingSubmitted = "listing.submitted";
    public const string ListingPublished = "listing.published";
    public const string ListingSentBack = "listing.sent_back";
    public const string ListingSuspended = "listing.suspended";
    public const string ListingReinstated = "listing.reinstated";
}
