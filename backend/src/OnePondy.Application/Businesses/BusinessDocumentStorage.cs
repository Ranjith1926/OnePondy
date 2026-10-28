namespace OnePondy.Application.Businesses;

/// <summary>
/// Stores GSTIN and licence proof files outside the public media folder (spec §12: protect personal
/// and business-sensitive data) — never served by the static file middleware that serves place
/// photos. Files are only ever read back through an authenticated endpoint.
/// </summary>
public interface IBusinessDocumentStorage
{
    Task SaveAsync(string path, ReadOnlyMemory<byte> content, CancellationToken cancellationToken);

    Task<byte[]?> ReadAsync(string path, CancellationToken cancellationToken);

    Task DeleteAsync(string path, CancellationToken cancellationToken);
}
