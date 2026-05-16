namespace OneDriveIntegrityLab.Experiment;

public sealed record ExperimentResult
{
    public required DateTimeOffset StartedAtUtc { get; init; }
    public required string LocalFilePath { get; init; }
    public required string RemoteFolderPath { get; init; }
    public required string RemoteFileName { get; init; }
    public required long FileSizeBytes { get; init; }
    public required string FileKind { get; init; } // "text" or "binary"
    public required string OriginalSha256 { get; init; }
    public required string DownloadedSha256 { get; init; }
    public string? ServerReportedSha256 { get; init; }
    public string? ServerReportedQuickXorHash { get; init; }
    public required bool LocalHashesMatch { get; init; }
    public required bool ServerSha256Matches { get; init; }
    public required bool UsedResumableUpload { get; init; }
    public required TimingsRecord Timings { get; init; }
    public bool IntegrityPreserved => LocalHashesMatch;
}

public sealed record TimingsRecord(
    long HashOriginalMs,
    long EnsureFolderMs,
    long UploadMs,
    long DownloadMs,
    long HashDownloadedMs,
    long TotalMs);
