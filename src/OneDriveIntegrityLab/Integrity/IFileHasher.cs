namespace OneDriveIntegrityLab.Integrity;

public interface IFileHasher
{
    string AlgorithmName { get; }

    Task<string> HashFileAsync(string path, CancellationToken cancellationToken = default);

    Task<string> HashStreamAsync(Stream stream, CancellationToken cancellationToken = default);
}
