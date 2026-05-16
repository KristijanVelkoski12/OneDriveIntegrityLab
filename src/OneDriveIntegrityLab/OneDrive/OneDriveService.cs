using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Drives.Item.Items.Item.CreateUploadSession;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;

namespace OneDriveIntegrityLab.OneDrive;

public sealed class OneDriveService : IOneDriveService
{
    private const int UploadChunkSize = 5 * 320 * 1024; // 1.5 MiB, multiple of 320 KiB

    private readonly GraphServiceClient _graph;
    private readonly ILogger<OneDriveService> _logger;
    private string? _cachedDriveId;

    public OneDriveService(GraphServiceClient graph, ILogger<OneDriveService> logger)
    {
        ArgumentNullException.ThrowIfNull(graph);
        _graph = graph;
        _logger = logger;
    }

    public async Task<string> EnsureFolderAsync(string folderPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);

        var driveId = await GetDriveIdAsync(cancellationToken).ConfigureAwait(false);
        var segments = folderPath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        var root = await _graph.Drives[driveId].Items["root"]
            .GetAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("Could not resolve drive root.");

        string parentId = root.Id!;
        string runningPath = string.Empty;

        foreach (var segment in segments)
        {
            runningPath = string.IsNullOrEmpty(runningPath) ? segment : $"{runningPath}/{segment}";
            try
            {
                var existing = await _graph.Drives[driveId].Items["root"].ItemWithPath(runningPath)
                    .GetAsync(cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (existing?.Folder is null)
                {
                    throw new InvalidOperationException(
                        $"Path '/{runningPath}' exists but is not a folder.");
                }

                parentId = existing.Id!;
            }
            catch (ODataError ex) when (IsNotFound(ex))
            {
                _logger.LogInformation("Creating folder /{Path}", runningPath);
                var created = await _graph.Drives[driveId].Items[parentId].Children.PostAsync(
                    new DriveItem
                    {
                        Name = segment,
                        Folder = new Folder(),
                        AdditionalData = new Dictionary<string, object>
                        {
                            ["@microsoft.graph.conflictBehavior"] = "fail",
                        },
                    },
                    cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                parentId = created?.Id
                    ?? throw new InvalidOperationException(
                        $"Folder creation for '/{runningPath}' returned no ID.");
            }
        }

        return parentId;
    }

    public async Task<UploadResult> UploadFileAsync(
        string parentFolderId,
        string remoteFileName,
        string localFilePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentFolderId);
        ArgumentException.ThrowIfNullOrWhiteSpace(remoteFileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(localFilePath);

        var driveId = await GetDriveIdAsync(cancellationToken).ConfigureAwait(false);
        var size = new FileInfo(localFilePath).Length;
        var useResumable = size > IOneDriveService.SimpleUploadMaxBytes;

        await using var content = new FileStream(
            localFilePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81_920,
            useAsync: true);

        DriveItem? uploaded;
        if (!useResumable)
        {
            _logger.LogInformation(
                "Uploading {Size:N0} bytes to '{File}' via simple PUT.", size, remoteFileName);

            uploaded = await _graph.Drives[driveId]
                .Items[parentFolderId]
                .ItemWithPath(remoteFileName)
                .Content
                .PutAsync(content, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            _logger.LogInformation(
                "Uploading {Size:N0} bytes to '{File}' via resumable upload session.",
                size, remoteFileName);

            var sessionRequest = new CreateUploadSessionPostRequestBody
            {
                Item = new DriveItemUploadableProperties
                {
                    AdditionalData = new Dictionary<string, object>
                    {
                        ["@microsoft.graph.conflictBehavior"] = "replace",
                    },
                },
            };

            var session = await _graph.Drives[driveId]
                .Items[parentFolderId]
                .ItemWithPath(remoteFileName)
                .CreateUploadSession
                .PostAsync(sessionRequest, cancellationToken: cancellationToken)
                .ConfigureAwait(false)
                ?? throw new InvalidOperationException("Server did not return an upload session.");

            var task = new LargeFileUploadTask<DriveItem>(
                session,
                content,
                UploadChunkSize,
                _graph.RequestAdapter);

            var progress = new Progress<long>(bytes =>
                _logger.LogDebug("Uploaded {Bytes:N0}/{Total:N0} bytes", bytes, size));

            var result = await task.UploadAsync(progress, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (!result.UploadSucceeded)
            {
                throw new InvalidOperationException("Resumable upload did not complete successfully.");
            }

            uploaded = result.ItemResponse;
        }

        if (uploaded?.Id is null)
        {
            throw new InvalidOperationException("Upload did not return a drive item.");
        }

        return new UploadResult(
            DriveItemId: uploaded.Id,
            SizeBytes: uploaded.Size ?? size,
            ServerSha256Hash: uploaded.File?.Hashes?.Sha256Hash?.ToLowerInvariant(),
            ServerQuickXorHash: uploaded.File?.Hashes?.QuickXorHash,
            UsedResumableSession: useResumable);
    }

    public async Task DownloadFileAsync(
        string driveItemId,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(driveItemId);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        var driveId = await GetDriveIdAsync(cancellationToken).ConfigureAwait(false);

        var remoteStream = await _graph.Drives[driveId].Items[driveItemId].Content
            .GetAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("Download returned a null content stream.");

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath) ?? ".");

        await using (remoteStream)
        await using (var local = new FileStream(
            destinationPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81_920,
            useAsync: true))
        {
            await remoteStream.CopyToAsync(local, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task DeleteItemAsync(string driveItemId, CancellationToken cancellationToken = default)
    {
        try
        {
            var driveId = await GetDriveIdAsync(cancellationToken).ConfigureAwait(false);
            await _graph.Drives[driveId].Items[driveItemId]
                .DeleteAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (ODataError ex) when (IsNotFound(ex))
        {
            _logger.LogDebug("Item {Id} was already gone; ignoring.", driveItemId);
        }
    }

    private async Task<string> GetDriveIdAsync(CancellationToken cancellationToken)
    {
        if (_cachedDriveId is not null)
        {
            return _cachedDriveId;
        }

        var drive = await _graph.Me.Drive
            .GetAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                "Could not resolve the signed-in user's default drive.");

        _cachedDriveId = drive.Id
            ?? throw new InvalidOperationException("Default drive returned no ID.");

        return _cachedDriveId;
    }

    private static bool IsNotFound(ODataError error) =>
        error.ResponseStatusCode == 404
        || string.Equals(error.Error?.Code, "itemNotFound", StringComparison.OrdinalIgnoreCase);
}
