namespace OneDriveIntegrityLab.Experiment;

public sealed record ExperimentOptions
{
    public required string LocalFilePath { get; init; }

    public string RemoteFolderPath { get; init; } = "/IntegrityLab";

    public required string DownloadDirectory { get; init; }

    public required string ResultsDirectory { get; init; }

    public bool CleanupAfter { get; init; } = true;

    public string FileKind { get; init; } = "binary";
}
