namespace mbf_scan_service.Services;

using Serilog;

public class CleanupService : IDisposable
{
    private readonly string _tempFolder;
    private readonly string _outputFolder;
    private readonly int _tempRetentionDays;
    private readonly int _outputRetentionDays;
    private readonly System.Threading.Timer _timer;
    private bool _disposed = false;

    public CleanupService(
        string tempFolder,
        string outputFolder,
        int tempRetentionDays = 1,
        int outputRetentionDays = 7,
        int intervalHours = 6)
    {
        _tempFolder = tempFolder;
        _outputFolder = outputFolder;
        _tempRetentionDays = tempRetentionDays;
        _outputRetentionDays = outputRetentionDays;

        EnsureFolder(tempFolder);
        EnsureFolder(outputFolder);

        _timer = new System.Threading.Timer(
            RunCleanup,
            null,
            TimeSpan.FromMinutes(2),
            TimeSpan.FromHours(intervalHours));

        Log.Information(
            "CleanupService started. Temp retention: {TempDays} day(s), Output retention: {OutputDays} day(s), Interval: {Interval}h",
            _tempRetentionDays, _outputRetentionDays, intervalHours);
    }

    private void EnsureFolder(string path)
    {
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
            Log.Information("Created folder for cleanup: {Path}", path);
        }
    }

    private void RunCleanup(object? state)
    {
        if (_disposed) return;

        try
        {
            Log.Information("Running scheduled cleanup...");
            CleanupAll();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error during scheduled cleanup");
        }
    }

    public CleanupResult CleanupAll()
    {
        var result = new CleanupResult();
        var startTime = DateTime.Now;

        CleanupTempFiles(result);
        CleanupOutputFiles(result);

        result.DurationMs = (long)(DateTime.Now - startTime).TotalMilliseconds;

        if (result.TotalFilesDeleted > 0)
        {
            Log.Information(
                "Cleanup completed. Temp: deleted {TempDeleted} files ({TempSize}), Output: deleted {OutputDeleted} files ({OutputSize}), Total freed: {TotalSize}, Duration: {Duration}ms",
                result.TempFilesDeleted, result.TempSizeFreed,
                result.OutputFilesDeleted, result.OutputSizeFreed,
                result.TotalSizeFreed, result.DurationMs);
        }
        else
        {
            Log.Debug("Cleanup completed. Nothing to delete.");
        }

        return result;
    }

    private void CleanupTempFiles(CleanupResult result)
    {
        try
        {
            if (!Directory.Exists(_tempFolder))
                return;

            var cutoff = DateTime.Now.AddDays(-_tempRetentionDays);
            var files = new DirectoryInfo(_tempFolder).GetFiles("*", SearchOption.TopDirectoryOnly);

            foreach (var file in files)
            {
                if (file.CreationTime < cutoff)
                {
                    try
                    {
                        var size = file.Length;
                        file.Delete();
                        result.TempFilesDeleted++;
                        result.TempSizeFreed += size;
                        Log.Debug("Deleted old temp file: {File} (age: {Age})",
                            file.Name, DateTime.Now - file.CreationTime);
                    }
                    catch (Exception ex)
                    {
                        Log.Warning("Failed to delete temp file {File}: {Error}", file.Name, ex.Message);
                        result.Errors.Add($"temp/{file.Name}: {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error cleaning temp folder");
            result.Errors.Add($"temp: {ex.Message}");
        }
    }

    private void CleanupOutputFiles(CleanupResult result)
    {
        try
        {
            if (!Directory.Exists(_outputFolder))
                return;

            var cutoff = DateTime.Now.AddDays(-_outputRetentionDays);
            var files = new DirectoryInfo(_outputFolder).GetFiles("*.pdf", SearchOption.TopDirectoryOnly);

            foreach (var file in files)
            {
                if (file.CreationTime < cutoff)
                {
                    try
                    {
                        var size = file.Length;
                        file.Delete();
                        result.OutputFilesDeleted++;
                        result.OutputSizeFreed += size;
                        Log.Debug("Deleted old output file: {File} (age: {Age})",
                            file.Name, DateTime.Now - file.CreationTime);
                    }
                    catch (Exception ex)
                    {
                        Log.Warning("Failed to delete output file {File}: {Error}", file.Name, ex.Message);
                        result.Errors.Add($"output/{file.Name}: {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error cleaning output folder");
            result.Errors.Add($"output: {ex.Message}");
        }
    }

    public (long TempFiles, long OutputFiles) GetFileCounts()
    {
        long tempCount = 0, outputCount = 0;
        try
        {
            if (Directory.Exists(_tempFolder))
                tempCount = new DirectoryInfo(_tempFolder).GetFiles("*", SearchOption.TopDirectoryOnly).LongLength;
            if (Directory.Exists(_outputFolder))
                outputCount = new DirectoryInfo(_outputFolder).GetFiles("*.pdf", SearchOption.TopDirectoryOnly).LongLength;
        }
        catch { }
        return (tempCount, outputCount);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Dispose();
        Log.Information("CleanupService disposed");
    }
}

public class CleanupResult
{
    public int TempFilesDeleted { get; set; }
    public long TempSizeFreed { get; set; }
    public int OutputFilesDeleted { get; set; }
    public long OutputSizeFreed { get; set; }
    public long TotalSizeFreed => TempSizeFreed + OutputSizeFreed;
    public int TotalFilesDeleted => TempFilesDeleted + OutputFilesDeleted;
    public long DurationMs { get; set; }
    public List<string> Errors { get; set; } = new();

    public string FormattedTempSize => FormatSize(TempSizeFreed);
    public string FormattedOutputSize => FormatSize(OutputSizeFreed);
    public string FormattedTotalSize => FormatSize(TotalSizeFreed);

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F1} MB";
        return $"{bytes / (1024.0 * 1024.0 * 1024.0):F1} GB";
    }
}
