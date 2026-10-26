using Microsoft.Extensions.Options;
using OnePondy.Application.Media;
using OnePondy.Domain.Places;
using OnePondy.Infrastructure.Media;
using SkiaSharp;

namespace OnePondy.UnitTests.Content;

public static class TestImages
{
    /// <summary>A solid-colour image in the given format, as a photo upload would arrive.</summary>
    public static byte[] Create(int width, int height, SKEncodedImageFormat format = SKEncodedImageFormat.Jpeg)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(new SKColor(14, 111, 106));
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 90);
        return data.ToArray();
    }
}

public class ImageProcessorTests
{
    private readonly SkiaImageProcessor _processor = new(Options.Create(new MediaOptions()));

    [Theory]
    [InlineData(SKEncodedImageFormat.Jpeg)]
    [InlineData(SKEncodedImageFormat.Png)]
    [InlineData(SKEncodedImageFormat.Webp)]
    public void Uploads_become_three_webp_sizes_without_upscaling(SKEncodedImageFormat format)
    {
        var result = _processor.Process(TestImages.Create(2400, 1600, format));

        Assert.True(result.IsSuccess);
        Assert.Equal([("large", 1600, 1067), ("medium", 800, 533), ("thumb", 400, 267)],
            result.Value.Variants.Select(v => (v.Name, v.Width, v.Height)));
        Assert.All(result.Value.Variants, variant =>
        {
            using var codec = SKCodec.Create(SKData.CreateCopy(variant.Data.ToArray()));
            Assert.Equal(SKEncodedImageFormat.Webp, codec.EncodedFormat);
        });

        var small = _processor.Process(TestImages.Create(1000, 800, format)).Value!;
        Assert.Equal((1000, 800), (small.Width, small.Height));
    }

    [Fact]
    public void Portrait_photos_are_measured_by_their_long_edge()
    {
        var result = _processor.Process(TestImages.Create(900, 1600));

        Assert.True(result.IsSuccess);
        Assert.Equal((900, 1600), (result.Value.Width, result.Value.Height));
    }

    [Fact]
    public void Small_and_non_image_files_are_refused()
    {
        Assert.Equal("image_too_small", _processor.Process(TestImages.Create(640, 480)).Error!.Code);
        Assert.Equal("unsupported_image", _processor.Process("not an image"u8).Error!.Code);
        Assert.Equal("unsupported_image", _processor.Process(TestImages.Create(1200, 900)[..200]).Error!.Code);
    }

    [Theory]
    [InlineData(SKEncodedOrigin.TopRight, 2, 1, 1, 0)]
    [InlineData(SKEncodedOrigin.BottomRight, 2, 1, 1, 0)]
    [InlineData(SKEncodedOrigin.RightTop, 1, 2, 0, 0)]
    [InlineData(SKEncodedOrigin.LeftBottom, 1, 2, 0, 1)]
    public void Camera_orientation_is_applied(SKEncodedOrigin origin, int width, int height, int redX, int redY)
    {
        // A 2×1 image: red on the left, blue on the right.
        using var source = new SKBitmap(2, 1);
        source.SetPixel(0, 0, SKColors.Red);
        source.SetPixel(1, 0, SKColors.Blue);

        using var upright = SkiaImageProcessor.Orient(source, origin);

        Assert.Equal((width, height), (upright.Width, upright.Height));
        Assert.Equal(SKColors.Red, upright.GetPixel(redX, redY));
    }
}

public class MediaServiceTests
{
    private readonly ContentHarness _harness = new();

    [Fact]
    public async Task Photos_are_stored_in_three_sizes_with_their_credit()
    {
        var place = await _harness.PublishedPlaceAsync();

        var photo = (await _harness.Media.UploadAsync(
            place.Id,
            TestImages.Create(2000, 1500),
            new MediaDetailsInput(" Boats at the jetty ", null, MediaLicence.CcBy, "Photo: A. Kumar, CC BY 4.0", "https://example.org/photo"),
            _harness.Editor,
            default)).Value!;

        Assert.Equal("Boats at the jetty", photo.AltText);
        Assert.Equal(3, _harness.Storage.Files.Count);
        Assert.StartsWith("https://cdn.test/places/", photo.Urls["large"]);

        var detail = (await _harness.Catalog.GetAsync(place.Slug, null, default))!;
        Assert.Equal("Photo: A. Kumar, CC BY 4.0", Assert.Single(detail.Images).Attribution);

        // Photo changes are audited but do not unpublish the place.
        Assert.True((await _harness.Places.GetAsync(place.Id, default)).Value!.IsPublished);
    }

    [Fact]
    public async Task Credit_alt_text_and_links_are_checked()
    {
        var place = (await _harness.Places.CreateAsync(ContentHarness.Input(), _harness.Editor, default)).Value!;

        var error = (await _harness.Media.UploadAsync(
            place.Id,
            TestImages.Create(1200, 900),
            new MediaDetailsInput("", null, MediaLicence.LicensedStock, null, "ftp://example.org"),
            _harness.Editor,
            default)).Error!;

        Assert.Equal(["altText", "attribution", "sourceUrl"], error.FieldErrors!.Keys.Order());
        Assert.Empty(_harness.Storage.Files);
    }

    [Fact]
    public async Task Removing_a_photo_deletes_its_files_and_reordering_sets_the_cover()
    {
        var place = (await _harness.Places.CreateAsync(ContentHarness.Input(), _harness.Editor, default)).Value!;
        var details = new MediaDetailsInput("View", null, MediaLicence.Owned, null, null);
        var first = (await _harness.Media.UploadAsync(place.Id, TestImages.Create(1200, 900), details, _harness.Editor, default)).Value!;
        var second = (await _harness.Media.UploadAsync(place.Id, TestImages.Create(1200, 900), details, _harness.Editor, default)).Value!;

        var reordered = (await _harness.Media.ReorderAsync(place.Id, [second.Id, first.Id], _harness.Editor, default)).Value!;
        Assert.Equal([second.Id, first.Id], reordered.Select(m => m.Id));

        Assert.Null(await _harness.Media.DeleteAsync(place.Id, first.Id, _harness.Editor, default));
        Assert.Equal(3, _harness.Storage.Files.Count);
        Assert.Equal("media_not_found", (await _harness.Media.DeleteAsync(place.Id, first.Id, _harness.Editor, default))!.Code);
    }
}
