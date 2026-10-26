namespace OnePondy.Domain.Places;

/// <summary>Image sizes produced for every upload, widest first. Files are WebP.</summary>
public static class MediaVariants
{
    public static readonly IReadOnlyList<(string Name, int Width)> All = [("large", 1600), ("medium", 800), ("thumb", 400)];

    public static string FileName(string storageKey, string variant) => $"{storageKey}-{variant}.webp";
}

/// <summary>A photo of a place with the credit and licence needed to show it (spec §10, Phase 3).</summary>
public sealed class PlaceMedia
{
    public const int MaxAltTextLength = 250;
    public const int MaxCaptionLength = 300;
    public const int MaxAttributionLength = 300;
    public const int MaxSourceUrlLength = 500;

    private PlaceMedia()
    {
    }

    public Guid Id { get; private set; }

    public Guid PlaceId { get; private set; }

    /// <summary>Path prefix in media storage; each variant adds "-large.webp" and so on.</summary>
    public string StorageKey { get; private set; } = string.Empty;

    /// <summary>Size of the largest stored variant, after applying the camera orientation.</summary>
    public int Width { get; private set; }

    public int Height { get; private set; }

    /// <summary>Describes the image for screen readers.</summary>
    public string AltText { get; private set; } = string.Empty;

    public string? Caption { get; private set; }

    public MediaLicence Licence { get; private set; }

    /// <summary>Credit shown with the image, e.g. "Photo: A. Kumar, CC BY 4.0".</summary>
    public string? Attribution { get; private set; }

    public string? SourceUrl { get; private set; }

    public int SortOrder { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public Guid CreatedByUserId { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static PlaceMedia Create(
        Guid id,
        Guid placeId,
        string storageKey,
        int width,
        int height,
        MediaDetails details,
        int sortOrder,
        Guid userId,
        DateTimeOffset now)
    {
        var media = new PlaceMedia
        {
            Id = id,
            PlaceId = placeId,
            StorageKey = storageKey,
            Width = width,
            Height = height,
            SortOrder = sortOrder,
            CreatedAt = now,
            CreatedByUserId = userId,
        };
        media.Describe(details, now);
        return media;
    }

    public void Describe(MediaDetails details, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(details.AltText);
        if (details.Licence.RequiresAttribution() && string.IsNullOrWhiteSpace(details.Attribution))
        {
            throw new ArgumentException("This licence requires an attribution.", nameof(details));
        }

        AltText = details.AltText;
        Caption = details.Caption;
        Licence = details.Licence;
        Attribution = details.Attribution;
        SourceUrl = details.SourceUrl;
        UpdatedAt = now;
    }

    public void MoveTo(int sortOrder, DateTimeOffset now)
    {
        SortOrder = sortOrder;
        UpdatedAt = now;
    }
}

public sealed record MediaDetails(
    string AltText,
    string? Caption,
    MediaLicence Licence,
    string? Attribution,
    string? SourceUrl);
