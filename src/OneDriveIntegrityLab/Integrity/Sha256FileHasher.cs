using System.Buffers;
using System.Security.Cryptography;

namespace OneDriveIntegrityLab.Integrity;

public sealed class Sha256FileHasher : IFileHasher
{
    private const int BufferSize = 81_920;

    public string AlgorithmName => "SHA-256";

    public async Task<string> HashFileAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSize,
            useAsync: true);
        return await HashStreamAsync(stream, cancellationToken).ConfigureAwait(false);
    }
    public async Task<string> HashStreamAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var digest = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(digest).ToLowerInvariant();
    }
}
