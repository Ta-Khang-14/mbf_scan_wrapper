namespace mbf_scan_service.Services;

using System.Collections.Concurrent;
using mbf_scan_service.Models;
using Serilog;

public class DemoScanService
{
    private readonly string _tempFolder;
    private readonly string _demoSourceFolder;
    private readonly ConcurrentDictionary<string, ScanSession> _sessions = new();

    public DemoScanService()
    {
        _tempFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "temp");
        _demoSourceFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DemoSource");
        EnsureTempFolder();
    }

    private void EnsureTempFolder()
    {
        if (!Directory.Exists(_tempFolder))
        {
            Directory.CreateDirectory(_tempFolder);
            Log.Information("Created temp folder: {TempFolder}", _tempFolder);
        }
    }

    public ScanSession CreateSession(string scannerName)
    {
        var session = new ScanSession(scannerName)
        {
            Settings = new ScanSettings
            {
                DPI = 300,
                ColorMode = "Color",
                PaperSize = "A4"
            }
        };
        _sessions[session.SessionId] = session;
        Log.Information("Demo: Created session {SessionId}", session.SessionId);
        return session;
    }

    public ScanSession? ScanDemo(ScanSession session)
    {
        try
        {
            if (!Directory.Exists(_demoSourceFolder))
            {
                Log.Warning("DemoSource folder not found: {Folder}", _demoSourceFolder);
                session.Status = ScanStatus.Error;
                session.ErrorMessage = "DemoSource folder not found";
                return session;
            }

            var tiffFiles = Directory.GetFiles(_demoSourceFolder, "*.tif")
                .Concat(Directory.GetFiles(_demoSourceFolder, "*.tiff"))
                .OrderBy(f => f)
                .ToList();

            if (tiffFiles.Count == 0)
            {
                Log.Warning("No TIFF files found in DemoSource: {Folder}", _demoSourceFolder);
                session.Status = ScanStatus.Error;
                session.ErrorMessage = "No TIFF files found in DemoSource";
                return session;
            }

            int startIndex = session.Pages.Count;
            foreach (var sourceFile in tiffFiles)
            {
                var pageFileName = $"demo_{session.SessionId}_{startIndex:D4}.tif";
                var pagePath = Path.Combine(_tempFolder, pageFileName);

                File.Copy(sourceFile, pagePath, overwrite: true);

                var scanPage = new ScanPage(startIndex, pagePath, ScanSide.Front)
                {
                    ScannedAt = DateTime.Now
                };

                session.Pages.Add(scanPage);
                startIndex++;

                Log.Information("Demo: Copied page {PageIndex}: {SourceFile} -> {PagePath}",
                    startIndex - 1, Path.GetFileName(sourceFile), pagePath);
            }

            session.Status = ScanStatus.Completed;
            session.CompletedAt = DateTime.Now;
            _sessions[session.SessionId] = session;

            Log.Information("Demo: Scan completed. Total pages: {TotalPages}", session.Pages.Count);
            return session;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Demo: Error during scan");
            session.Status = ScanStatus.Error;
            session.ErrorMessage = ex.Message;
            return session;
        }
    }

    public ScanSession? GetSession(string sessionId)
    {
        return _sessions.TryGetValue(sessionId, out var session) ? session : null;
    }

    public void SaveSession(ScanSession session)
    {
        _sessions[session.SessionId] = session;
    }

    public string GetTempFolder() => _tempFolder;

    public void CleanupTempFiles(string sessionId)
    {
        try
        {
            var files = Directory.GetFiles(_tempFolder, $"*_{sessionId}_*.tif");
            foreach (var file in files)
            {
                File.Delete(file);
            }
            Log.Information("Demo: Cleaned up {Count} temp files for session {SessionId}", files.Length, sessionId);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Demo: Error cleaning up temp files for session {SessionId}", sessionId);
        }
    }
}
