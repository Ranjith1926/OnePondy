using OnePondy.Application.Common;
using OnePondy.Domain.Places;

namespace OnePondy.Application.Media;

/// <summary>
/// Where media files live (spec D8). A local folder for now; object storage with a CDN can replace
/// it without changing callers.
/// </summary>
public interface IMediaStorage
{
    Task SaveAsync(string path, ReadOnlyMemory<byte> content, string contentType, CancellationToken cancellationToken);

    Task DeleteAsync(string path, CancellationToken cancellationToken);

    /// <summary>An absolute URL when a public base URL is configured, otherwise a path such as "/media/…".</summary>
    string GetPublicUrl(string path);
}

public sealed record ImageVariant(string Name, int Width, int Height, ReadOnlyMemory<byte> Data);

public sealed record ProcessedImage(int Width, int Height, IReadOnlyList<ImageVariant> Variants);

/// <summary>Validates an uploaded image and produces the stored sizes, with camera metadata removed.</summary>
public interface IImageProcessor
{
    Result<ProcessedImage> Process(ReadOnlySpan<byte> upload);
}

public sealed class MediaOptions
{
    public const string SectionName = "Media";

    public long MaxUploadBytes { get; set; } = 10 * 1024 * 1024;

    /// <summary>Smaller images look poor on phones, so ask for a better photo instead. Works for portrait too.</summary>
    public int MinLongEdge { get; set; } = 800;

    public int MinShortEdge { get; set; } = 450;

    /// <summary>Guards against images that are small files but huge when decoded.</summary>
    public long MaxPixels { get; set; } = 50_000_000;

    public int MaxPerPlace { get; set; } = 20;
}

public static class MediaErrors
{
    public static readonly AppError NotFound = new("media_not_found", ErrorKind.NotFound, "The image does not exist.");

    public static readonly AppError UnsupportedFormat = new(
        "unsupported_image",
        ErrorKind.Validation,
        "Upload a JPEG, PNG or WebP image.");

    public static readonly AppError TooLarge = new("image_too_large", ErrorKind.Validation, "The file is too large.");

    public static readonly AppError TooManyImages = new(
        "too_many_images",
        ErrorKind.Conflict,
        "This place already has the maximum number of images.");

    public static AppError TooSmall(int minLongEdge, int minShortEdge) => new(
        "image_too_small",
        ErrorKind.Validation,
        $"Use an image of at least {minLongEdge}×{minShortEdge} pixels.");
}

public sealed record MediaView(
    Guid Id,
    string AltText,
    string? Caption,
    MediaLicence Licence,
    string? Attribution,
    string? SourceUrl,
    int SortOrder,
    int Width,
    int Height,
    IReadOnlyDictionary<string, string> Urls)
{
    public static MediaView From(PlaceMedia media, IMediaStorage storage) => new(
        media.Id,
        media.AltText,
        media.Caption,
        media.Licence,
        media.Attribution,
        media.SourceUrl,
        media.SortOrder,
        media.Width,
        media.Height,
        MediaVariants.All.ToDictionary(
            v => v.Name,
            v => storage.GetPublicUrl(MediaVariants.FileName(media.StorageKey, v.Name))));
}
