using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OnePondy.Application.Audit;
using OnePondy.Application.Common;
using OnePondy.Application.Places;
using OnePondy.Domain.Audit;
using OnePondy.Domain.Places;

namespace OnePondy.Application.Media;

public sealed record MediaDetailsInput(
    string? AltText,
    string? Caption,
    MediaLicence Licence,
    string? Attribution,
    string? SourceUrl);

/// <summary>
/// Place photos: validated, resized and stored with their licence and credit (spec §10, Phase 3).
/// Photo changes do not change a place's verification; they are recorded in the audit log.
/// </summary>
public sealed class MediaService(
    IOnePondyDbContext db,
    IMediaStorage storage,
    IImageProcessor images,
    AuditTrail audit,
    IOptions<MediaOptions> options,
    TimeProvider time,
    ILogger<MediaService> logger)
{
    private readonly MediaOptions _options = options.Value;

    public async Task<Result<MediaView>> UploadAsync(
        Guid placeId,
        ReadOnlyMemory<byte> upload,
        MediaDetailsInput input,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var details = Validate(input);
        if (!details.IsSuccess)
        {
            return details.Error;
        }

        if (upload.Length > _options.MaxUploadBytes)
        {
            return MediaErrors.TooLarge;
        }

        if (!await db.Places.AnyAsync(p => p.Id == placeId, cancellationToken))
        {
            return PlaceErrors.NotFound;
        }

        var existing = await db.PlaceMedia.Where(m => m.PlaceId == placeId).Select(m => m.SortOrder).ToListAsync(cancellationToken);
        if (existing.Count >= _options.MaxPerPlace)
        {
            return MediaErrors.TooManyImages;
        }

        var processed = images.Process(upload.Span);
        if (!processed.IsSuccess)
        {
            return processed.Error;
        }

        var mediaId = Guid.CreateVersion7();
        var storageKey = $"places/{placeId:N}/{mediaId:N}";
        foreach (var variant in processed.Value.Variants)
        {
            await storage.SaveAsync(MediaVariants.FileName(storageKey, variant.Name), variant.Data, "image/webp", cancellationToken);
        }

        var media = PlaceMedia.Create(
            mediaId,
            placeId,
            storageKey,
            processed.Value.Width,
            processed.Value.Height,
            details.Value,
            existing.Count == 0 ? 0 : existing.Max() + 1,
            userId,
            time.GetUtcNow());
        db.PlaceMedia.Add(media);
        audit.Record(userId, AuditActions.MediaAdded, AuditEntityTypes.Place, placeId, new
        {
            mediaId,
            details.Value.AltText,
            details.Value.Licence,
            details.Value.Attribution,
        });

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await DeleteFilesAsync(storageKey, CancellationToken.None);
            throw;
        }

        return MediaView.From(media, storage);
    }

    public async Task<Result<MediaView>> UpdateAsync(
        Guid placeId,
        Guid mediaId,
        MediaDetailsInput input,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var details = Validate(input);
        if (!details.IsSuccess)
        {
            return details.Error;
        }

        var media = await db.PlaceMedia.SingleOrDefaultAsync(m => m.Id == mediaId && m.PlaceId == placeId, cancellationToken);
        if (media is null)
        {
            return MediaErrors.NotFound;
        }

        var before = new { media.AltText, media.Caption, media.Licence, media.Attribution, media.SourceUrl };
        media.Describe(details.Value, time.GetUtcNow());
        audit.Record(userId, AuditActions.MediaUpdated, AuditEntityTypes.Place, placeId, new
        {
            mediaId,
            before,
            after = new { media.AltText, media.Caption, media.Licence, media.Attribution, media.SourceUrl },
        });
        await db.SaveChangesAsync(cancellationToken);
        return MediaView.From(media, storage);
    }

    /// <summary>Sets the display order; the first image is the place's cover photo.</summary>
    public async Task<Result<IReadOnlyList<MediaView>>> ReorderAsync(
        Guid placeId,
        IReadOnlyList<Guid> orderedIds,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var media = await db.PlaceMedia.Where(m => m.PlaceId == placeId).ToListAsync(cancellationToken);
        if (orderedIds.Count != media.Count || orderedIds.Distinct().Count() != media.Count
            || orderedIds.Any(id => media.All(m => m.Id != id)))
        {
            return AppError.Validation(new Dictionary<string, string[]>
            {
                ["mediaIds"] = ["List every image of this place exactly once."],
            });
        }

        var now = time.GetUtcNow();
        for (var i = 0; i < orderedIds.Count; i++)
        {
            media.Single(m => m.Id == orderedIds[i]).MoveTo(i, now);
        }

        audit.Record(userId, AuditActions.MediaUpdated, AuditEntityTypes.Place, placeId, new { order = orderedIds });
        await db.SaveChangesAsync(cancellationToken);
        return Result<IReadOnlyList<MediaView>>.Success(
            [.. media.OrderBy(m => m.SortOrder).Select(m => MediaView.From(m, storage))]);
    }

    public async Task<AppError?> DeleteAsync(Guid placeId, Guid mediaId, Guid userId, CancellationToken cancellationToken)
    {
        var media = await db.PlaceMedia.SingleOrDefaultAsync(m => m.Id == mediaId && m.PlaceId == placeId, cancellationToken);
        if (media is null)
        {
            return MediaErrors.NotFound;
        }

        db.PlaceMedia.Remove(media);
        audit.Record(userId, AuditActions.MediaRemoved, AuditEntityTypes.Place, placeId, new { mediaId, media.AltText });
        await db.SaveChangesAsync(cancellationToken);

        // Files go after the row, so a failure here leaves orphaned files rather than a broken image.
        await DeleteFilesAsync(media.StorageKey, cancellationToken);
        return null;
    }

    private async Task DeleteFilesAsync(string storageKey, CancellationToken cancellationToken)
    {
        foreach (var (variant, _) in MediaVariants.All)
        {
            try
            {
                await storage.DeleteAsync(MediaVariants.FileName(storageKey, variant), cancellationToken);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(exception, "Could not delete media file {StorageKey}-{Variant}", storageKey, variant);
            }
        }
    }

    private static Result<MediaDetails> Validate(MediaDetailsInput input)
    {
        var errors = new FieldErrors();
        var altText = errors.Required("altText", input.AltText, PlaceMedia.MaxAltTextLength);
        var caption = errors.Optional("caption", input.Caption, PlaceMedia.MaxCaptionLength);
        var attribution = errors.Optional("attribution", input.Attribution, PlaceMedia.MaxAttributionLength);
        var sourceUrl = errors.Optional("sourceUrl", input.SourceUrl, PlaceMedia.MaxSourceUrlLength);

        if (!Enum.IsDefined(input.Licence))
        {
            errors.Add("licence", "Choose a licence.");
        }
        else if (input.Licence.RequiresAttribution() && attribution is null)
        {
            errors.Add("attribution", "This licence requires a credit, for example \"Photo: A. Kumar, CC BY 4.0\".");
        }

        if (sourceUrl is not null
            && !(Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"))
        {
            errors.Add("sourceUrl", "Enter a full web address starting with http:// or https://.");
        }

        return errors.Any
            ? errors.ToError()
            : new MediaDetails(altText, caption, input.Licence, attribution, sourceUrl);
    }
}
