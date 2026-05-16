namespace OneDriveIntegrityLab.OneDrive;

public interface IOneDriveService
{
    Task<string> EnsureFolderAsync(string folderPath, CancellationToken cancellationToken = default);

    Task<UploadResult> UploadFileAsync(
        string parentFolderId,
        string remoteFileName,
        string localFilePath,
        CancellationToken cancellationToken = default);

    Task DownloadFileAsync(
        string driveItemId,
        string destinationPath,
        CancellationToken cancellationToken = default);

    Task DeleteItemAsync(string driveItemId, CancellationToken cancellationToken = default);

    public const long SimpleUploadMaxBytes = 4L * 1024 * 1024;

}
