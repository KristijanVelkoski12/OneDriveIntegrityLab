namespace OneDriveIntegrityLab.OneDrive;

public sealed record UploadResult(
    string DriveItemId,
    long SizeBytes,
    string? ServerSha256Hash,
    string? ServerQuickXorHash,
    bool UsedResumableSession);
