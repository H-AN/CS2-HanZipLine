using System.Text.Json;
using CS2HanZipLine.Models;
using SwiftlyS2.Shared;

namespace CS2HanZipLine.Services;

public sealed class ZiplineMapStorageService(ISwiftlyCore core)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _storageDirectory = Path.Combine(core.PluginDataDirectory, "maps");

    public Task<ZiplineMapLoadResult> LoadAsync(string mapName, CancellationToken cancellationToken) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = GetPath(mapName);
        if (!File.Exists(path))
        {
            return new ZiplineMapLoadResult(path, false, []);
        }

        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096);
            var document = JsonSerializer.Deserialize<ZiplineMapDocument>(stream, JsonOptions);
            return new ZiplineMapLoadResult(path, true, document?.Ziplines ?? []);
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return new ZiplineMapLoadResult(path, true, [], exception.Message);
        }
    }, cancellationToken);

    public Task<ZiplineMapSaveResult> SaveAsync(
        string mapName,
        IReadOnlyCollection<ZiplineMapEntry> entries,
        CancellationToken cancellationToken) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = GetPath(mapName);
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";

        try
        {
            Directory.CreateDirectory(_storageDirectory);
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 4096))
            {
                JsonSerializer.Serialize(stream, new ZiplineMapDocument { Ziplines = entries.ToList() }, JsonOptions);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, path, overwrite: true);
            return new ZiplineMapSaveResult(path, true);
        }
        catch (OperationCanceledException)
        {
            TryDeleteTemporaryFile(temporaryPath);
            throw;
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            TryDeleteTemporaryFile(temporaryPath);
            return new ZiplineMapSaveResult(path, false, exception.Message);
        }
    }, cancellationToken);

    private string GetPath(string mapName)
    {
        var safeMapName = string.Concat(mapName.Select(character =>
            Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
        safeMapName = string.IsNullOrWhiteSpace(safeMapName) ? "unknown" : safeMapName;
        return Path.Combine(_storageDirectory, $"{safeMapName}.json");
    }

    private static void TryDeleteTemporaryFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }
}

public sealed record ZiplineMapLoadResult(
    string Path,
    bool Found,
    IReadOnlyList<ZiplineMapEntry> Entries,
    string? Error = null);

public sealed record ZiplineMapSaveResult(string Path, bool Success, string? Error = null);
