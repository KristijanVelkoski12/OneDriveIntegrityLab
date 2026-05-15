using System.Security.Cryptography;
using System.Text;
using OneDriveIntegrityLab.Integrity;

namespace OneDriveIntegrityLab.Tests.Integrity;

public sealed class Sha256FileHasherTests
{
    private readonly Sha256FileHasher _hasher = new();

    [Fact]
    public void AlgorithmName_IsSha256() =>
        Assert.Equal("SHA-256", _hasher.AlgorithmName);

    [Fact]
    public async Task HashStreamAsync_EmptyStream_ReturnsKnownDigest()
    {
        // NIST published SHA-256 digest of an empty input.
        const string expected = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
        using var stream = new MemoryStream(Array.Empty<byte>());

        var actual = await _hasher.HashStreamAsync(stream);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task HashStreamAsync_AbcVector_MatchesNistTestVector()
    {
        // NIST FIPS 180-4 test vector for "abc".
        const string expected = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes("abc"));

        var actual = await _hasher.HashStreamAsync(stream);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task HashFileAsync_AgreesWithHashStreamAsync()
    {
        var bytes = RandomNumberGenerator.GetBytes(1_500_000); // > buffer size, exercises multiple reads
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(path, bytes);

            var fileDigest = await _hasher.HashFileAsync(path);
            using var memory = new MemoryStream(bytes);
            var streamDigest = await _hasher.HashStreamAsync(memory);

            Assert.Equal(streamDigest, fileDigest);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task HashFileAsync_AgreesWithBclSha256()
    {
        var bytes = RandomNumberGenerator.GetBytes(64 * 1024);
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(path, bytes);
            var expected = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

            var actual = await _hasher.HashFileAsync(path);

            Assert.Equal(expected, actual);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task HashFileAsync_CancelledToken_Throws()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(path, RandomNumberGenerator.GetBytes(1024));
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => _hasher.HashFileAsync(path, cts.Token));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
