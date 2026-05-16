using System.Text.Json;

namespace OneDriveIntegrityLab.Experiment;

public sealed class ResultsWriter
{
    private static readonly JsonSerializerOptions PrettyOptions = new()
    {
        WriteIndented = true,
    };
    private static readonly JsonSerializerOptions CompactOptions = new()
    {
        WriteIndented = false,
    };
    public async Task<string> WriteAsync(
        ExperimentResult result,
        string directory,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(directory);
        var stamp = result.StartedAtUtc.ToString("yyyyMMdd'T'HHmmss'Z'");
        var fileName = $"run-{stamp}-{Path.GetFileNameWithoutExtension(result.RemoteFileName)}.json";
        var path = Path.Combine(directory, fileName);
        await File.WriteAllTextAsync(
            path,
            JsonSerializer.Serialize(result, PrettyOptions),
            cancellationToken).ConfigureAwait(false);
        var indexPath = Path.Combine(directory, "index.jsonl");
        await File.AppendAllTextAsync(
            indexPath,
            JsonSerializer.Serialize(result, CompactOptions) + Environment.NewLine,
            cancellationToken).ConfigureAwait(false);
        return path;
    }
}
