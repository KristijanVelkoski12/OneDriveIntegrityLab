using System.Diagnostics;
using Microsoft.Extensions.Logging;
using OneDriveIntegrityLab.Integrity;
using OneDriveIntegrityLab.OneDrive;

namespace OneDriveIntegrityLab.Experiment;

public sealed class ExperimentRunner
{
    private readonly IOneDriveService _oneDrive;
    private readonly IFileHasher _hasher;
    private readonly ResultsWriter _resultsWriter;
    private readonly ILogger<ExperimentRunner> _logger;
    public ExperimentRunner(
        IOneDriveService oneDrive,
        IFileHasher hasher,
        ResultsWriter resultsWriter,
        ILogger<ExperimentRunner> logger)
    {
        _oneDrive = oneDrive;
        _hasher = hasher;
        _resultsWriter = resultsWriter;
        _logger = logger;
    }
    public async Task<ExperimentResult> RunAsync(
        ExperimentOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!File.Exists(options.LocalFilePath))
        {
            throw new FileNotFoundException($"Input file not found: {options.LocalFilePath}");
        }
        var startedAt = DateTimeOffset.UtcNow;
        var totalSw = Stopwatch.StartNew();
        var size = new FileInfo(options.LocalFilePath).Length;
        var remoteFileName = Path.GetFileName(options.LocalFilePath);
        _logger.LogInformation(
            "Starting experiment: file='{File}' size={Size:N0}B kind={Kind} remote='{Remote}/{Name}'",
            options.LocalFilePath, size, options.FileKind, options.RemoteFolderPath, remoteFileName);
        var hashOriginalSw = Stopwatch.StartNew();
        var originalHash = await _hasher.HashFileAsync(options.LocalFilePath, cancellationToken)
            .ConfigureAwait(false);
        hashOriginalSw.Stop();
        _logger.LogInformation("Original SHA-256: {Hash}", originalHash);
        var ensureSw = Stopwatch.StartNew();
        var folderId = await _oneDrive.EnsureFolderAsync(options.RemoteFolderPath, cancellationToken)
            .ConfigureAwait(false);
        ensureSw.Stop();
        var uploadSw = Stopwatch.StartNew();
        var upload = await _oneDrive.UploadFileAsync(
            folderId, remoteFileName, options.LocalFilePath, cancellationToken)
            .ConfigureAwait(false);
        uploadSw.Stop();
        _logger.LogInformation(
            "Upload complete: id={Id} resumable={Resumable} serverSha256={ServerHash}",
            upload.DriveItemId, upload.UsedResumableSession, upload.ServerSha256Hash ?? "<none>");
        var downloadPath = Path.Combine(options.DownloadDirectory, remoteFileName);
        var downloadSw = Stopwatch.StartNew();
        await _oneDrive.DownloadFileAsync(upload.DriveItemId, downloadPath, cancellationToken)
            .ConfigureAwait(false);
        downloadSw.Stop();
        var hashDownloadedSw = Stopwatch.StartNew();
        var downloadedHash = await _hasher.HashFileAsync(downloadPath, cancellationToken)
            .ConfigureAwait(false);
        hashDownloadedSw.Stop();
        _logger.LogInformation("Downloaded SHA-256: {Hash}", downloadedHash);
        totalSw.Stop();
        var result = new ExperimentResult
        {
            StartedAtUtc = startedAt,
            LocalFilePath = options.LocalFilePath,
            RemoteFolderPath = options.RemoteFolderPath,
            RemoteFileName = remoteFileName,
            FileSizeBytes = size,
            FileKind = options.FileKind,
            OriginalSha256 = originalHash,
            DownloadedSha256 = downloadedHash,
            ServerReportedSha256 = upload.ServerSha256Hash,
            ServerReportedQuickXorHash = upload.ServerQuickXorHash,
            LocalHashesMatch = string.Equals(originalHash, downloadedHash, StringComparison.Ordinal),
            ServerSha256Matches = upload.ServerSha256Hash is not null
                && string.Equals(upload.ServerSha256Hash, originalHash, StringComparison.OrdinalIgnoreCase),
            UsedResumableUpload = upload.UsedResumableSession,
            Timings = new TimingsRecord(
                HashOriginalMs: hashOriginalSw.ElapsedMilliseconds,
                EnsureFolderMs: ensureSw.ElapsedMilliseconds,
                UploadMs: uploadSw.ElapsedMilliseconds,
                DownloadMs: downloadSw.ElapsedMilliseconds,
                HashDownloadedMs: hashDownloadedSw.ElapsedMilliseconds,
                TotalMs: totalSw.ElapsedMilliseconds),
        };
        var resultPath = await _resultsWriter.WriteAsync(result, options.ResultsDirectory, cancellationToken)
            .ConfigureAwait(false);
        _logger.LogInformation("Wrote result to {Path}", resultPath);
        if (result.LocalHashesMatch)
        {
            _logger.LogInformation(
                "INTEGRITY OK: local SHA-256 matches after round-trip ({Total} ms total).",
                result.Timings.TotalMs);
        }
        else
        {
            _logger.LogError(
                "INTEGRITY FAILURE: original={Original} downloaded={Downloaded}",
                originalHash, downloadedHash);
        }
        if (options.CleanupAfter)
        {
            try
            {
                await _oneDrive.DeleteItemAsync(upload.DriveItemId, cancellationToken).ConfigureAwait(false);
                if (File.Exists(downloadPath))
                {
                    File.Delete(downloadPath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Cleanup encountered an error; continuing.");
            }
        }
        return result;
    }
}
