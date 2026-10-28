using Microsoft.Extensions.Options;
using OnePondy.Application.Businesses;

namespace OnePondy.Infrastructure.Businesses;

public sealed class BusinessDocumentStorageOptions
{
    public const string SectionName = "BusinessDocuments";

    /// <summary>Folder for uploaded documents. Defaults to App_Data/business-documents under the API's content root.</summary>
    public string? RootPath { get; set; }
}

/// <summary>Stores business documents on the API server's disk, in a folder never exposed by the static file middleware.</summary>
public sealed class LocalBusinessDocumentStorage(IOptions<BusinessDocumentStorageOptions> options) : IBusinessDocumentStorage
{
    private readonly string _root = Path.GetFullPath(options.Value.RootPath
        ?? throw new InvalidOperationException("BusinessDocuments:RootPath is not set."));

    public async Task SaveAsync(string path, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        var target = Resolve(path);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);

        // Write to a temporary file first so a request never sees a half-written document.
        var temporary = target + ".tmp";
        await File.WriteAllBytesAsync(temporary, content.ToArray(), cancellationToken);
        File.Move(temporary, target, overwrite: true);
    }

    public async Task<byte[]?> ReadAsync(string path, CancellationToken cancellationToken)
    {
        var target = Resolve(path);
        return File.Exists(target) ? await File.ReadAllBytesAsync(target, cancellationToken) : null;
    }

    public Task DeleteAsync(string path, CancellationToken cancellationToken)
    {
        File.Delete(Resolve(path));
        return Task.CompletedTask;
    }

    private string Resolve(string path)
    {
        var full = Path.GetFullPath(Path.Combine(_root, path));
        if (!full.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Document path is outside the document folder.", nameof(path));
        }

        return full;
    }
}
