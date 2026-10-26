using Microsoft.Extensions.Options;
using OnePondy.Application.Media;

namespace OnePondy.Infrastructure.Media;

public sealed class MediaStorageOptions
{
    public const string SectionName = "Media";

    public const string RequestPath = "/media";

    /// <summary>Folder for uploaded media. Defaults to App_Data/media under the API's content root.</summary>
    public string? RootPath { get; set; }

    /// <summary>
    /// Absolute URL the files are served from, e.g. "https://api.example.in/media". When empty, URLs
    /// are paths ("/media/…") that clients resolve against the API base URL.
    /// </summary>
    public string? PublicBaseUrl { get; set; }
}

/// <summary>Stores media on the API server's disk (interim choice for spec D8; object storage comes later).</summary>
public sealed class LocalMediaStorage(IOptions<MediaStorageOptions> options) : IMediaStorage
{
    private readonly string _root = Path.GetFullPath(options.Value.RootPath
        ?? throw new InvalidOperationException("Media:RootPath is not set."));

    private readonly string? _publicBaseUrl = string.IsNullOrWhiteSpace(options.Value.PublicBaseUrl)
        ? null
        : options.Value.PublicBaseUrl.TrimEnd('/');

    public async Task SaveAsync(string path, ReadOnlyMemory<byte> content, string contentType, CancellationToken cancellationToken)
    {
        var target = Resolve(path);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);

        // Write to a temporary file first so a request never sees a half-written image.
        var temporary = target + ".tmp";
        await File.WriteAllBytesAsync(temporary, content.ToArray(), cancellationToken);
        File.Move(temporary, target, overwrite: true);
    }

    public Task DeleteAsync(string path, CancellationToken cancellationToken)
    {
        File.Delete(Resolve(path));
        return Task.CompletedTask;
    }

    public string GetPublicUrl(string path) => $"{_publicBaseUrl ?? MediaStorageOptions.RequestPath}/{path}";

    private string Resolve(string path)
    {
        var full = Path.GetFullPath(Path.Combine(_root, path));
        if (!full.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Media path is outside the media folder.", nameof(path));
        }

        return full;
    }
}
