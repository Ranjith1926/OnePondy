using Microsoft.Extensions.Options;
using OnePondy.Application.Common;
using OnePondy.Application.Media;
using OnePondy.Domain.Places;
using SkiaSharp;

namespace OnePondy.Infrastructure.Media;

/// <summary>
/// Decodes JPEG, PNG and WebP uploads, applies the camera orientation and writes WebP variants.
/// Re-encoding drops all metadata, including GPS positions in phone photos. SkiaSharp is
/// MIT-licensed; ImageSharp was avoided because larger companies need a paid licence for it.
/// </summary>
public sealed class SkiaImageProcessor(IOptions<MediaOptions> options) : IImageProcessor
{
    private const int WebpQuality = 82;
    private readonly MediaOptions _options = options.Value;

    public Result<ProcessedImage> Process(ReadOnlySpan<byte> upload)
    {
        using var data = SKData.CreateCopy(upload.ToArray());
        using var codec = SKCodec.Create(data);
        if (codec is null
            || codec.EncodedFormat is not (SKEncodedImageFormat.Jpeg or SKEncodedImageFormat.Png or SKEncodedImageFormat.Webp))
        {
            return MediaErrors.UnsupportedFormat;
        }

        if ((long)codec.Info.Width * codec.Info.Height > _options.MaxPixels)
        {
            return MediaErrors.TooLarge;
        }

        using var decoded = SKBitmap.Decode(codec);
        if (decoded is null)
        {
            return MediaErrors.UnsupportedFormat;
        }

        using var upright = Orient(decoded, codec.EncodedOrigin);
        var longEdge = Math.Max(upright.Width, upright.Height);
        var shortEdge = Math.Min(upright.Width, upright.Height);
        if (longEdge < _options.MinLongEdge || shortEdge < _options.MinShortEdge)
        {
            return MediaErrors.TooSmall(_options.MinLongEdge, _options.MinShortEdge);
        }

        var variants = new List<ImageVariant>();
        foreach (var (name, maxEdge) in MediaVariants.All)
        {
            // Never upscale: a variant is at most as large as the original.
            var scale = Math.Min(1.0, (double)maxEdge / longEdge);
            var width = Math.Max(1, (int)Math.Round(upright.Width * scale));
            var height = Math.Max(1, (int)Math.Round(upright.Height * scale));
            using var resized = scale < 1.0
                ? upright.Resize(new SKImageInfo(width, height), new SKSamplingOptions(SKCubicResampler.Mitchell))
                : upright.Copy();
            using var image = SKImage.FromBitmap(resized);
            using var encoded = image.Encode(SKEncodedImageFormat.Webp, WebpQuality);
            variants.Add(new ImageVariant(name, width, height, encoded.ToArray()));
        }

        return new ProcessedImage(variants[0].Width, variants[0].Height, variants);
    }

    /// <summary>Returns a new bitmap drawn upright according to the EXIF orientation.</summary>
    internal static SKBitmap Orient(SKBitmap source, SKEncodedOrigin origin)
    {
        if (origin == SKEncodedOrigin.TopLeft)
        {
            return source.Copy();
        }

        var (w, h) = (source.Width, source.Height);
        var swapsAxes = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
            or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var result = new SKBitmap(swapsAxes ? h : w, swapsAxes ? w : h, source.ColorType, source.AlphaType);
        using var canvas = new SKCanvas(result);
        switch (origin)
        {
            case SKEncodedOrigin.TopRight: // mirrored
                canvas.Translate(w, 0);
                canvas.Scale(-1, 1);
                break;
            case SKEncodedOrigin.BottomRight: // upside down
                canvas.Translate(w, h);
                canvas.RotateDegrees(180);
                break;
            case SKEncodedOrigin.BottomLeft: // flipped
                canvas.Translate(0, h);
                canvas.Scale(1, -1);
                break;
            case SKEncodedOrigin.LeftTop: // transposed
                canvas.Scale(-1, 1);
                canvas.RotateDegrees(90);
                break;
            case SKEncodedOrigin.RightTop: // needs 90° clockwise
                canvas.Translate(h, 0);
                canvas.RotateDegrees(90);
                break;
            case SKEncodedOrigin.RightBottom: // transverse
                canvas.Translate(h, w);
                canvas.Scale(-1, 1);
                canvas.RotateDegrees(-90);
                break;
            case SKEncodedOrigin.LeftBottom: // needs 90° anticlockwise
                canvas.Translate(0, w);
                canvas.RotateDegrees(-90);
                break;
        }

        // Quarter turns and mirrors map pixels one to one, so nearest sampling is lossless.
        canvas.DrawBitmap(source, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest), null);
        return result;
    }
}
