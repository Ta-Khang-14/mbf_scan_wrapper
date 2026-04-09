namespace mbf_scan_service.Services;

using mbf_scan_service.Models;
using Serilog;
using System.Collections.Concurrent;

public class FileService
{
    private readonly string _outputFolder;
    private readonly string _downloadBasePath;
    private readonly ConcurrentDictionary<string, FileMetadata> _files = new();

    public FileService() : this("output", "/api/files")
    {
    }

    public FileService(string outputFolder, string downloadBasePath)
    {
        _outputFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, outputFolder);
        _downloadBasePath = downloadBasePath;

        EnsureOutputFolder();

        Log.Information("FileService initialized. Output: {Folder}, Download base: {Base}",
            _outputFolder, _downloadBasePath);
    }

    private void EnsureOutputFolder()
    {
        if (!Directory.Exists(_outputFolder))
        {
            Directory.CreateDirectory(_outputFolder);
            Log.Information("Created output folder: {Folder}", _outputFolder);
        }
    }

    public FileMetadata SavePdf(string pdfPath, string? fileName = null)
    {
        if (!File.Exists(pdfPath))
        {
            Log.Warning("PDF file not found: {Path}", pdfPath);
            return new FileMetadata { Error = "File not found" };
        }

        try
        {
            var id = Guid.NewGuid().ToString("N")[..12];
            var finalFileName = fileName ?? $"scan_{id}.pdf";
            var destinationPath = Path.Combine(_outputFolder, finalFileName);

            File.Copy(pdfPath, destinationPath, overwrite: true);

            var metadata = new FileMetadata
            {
                Id = id,
                FileName = finalFileName,
                FilePath = destinationPath,
                DownloadUrl = $"{_downloadBasePath}/{id}",
                FileSize = new FileInfo(destinationPath).Length,
                CreatedAt = DateTime.Now
            };

            _files[id] = metadata;

            Log.Information("File saved: {FileName}, Size: {Size}, URL: {Url}",
                finalFileName, metadata.FormattedFileSize, metadata.DownloadUrl);

            return metadata;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error saving file: {Path}", pdfPath);
            return new FileMetadata { Error = ex.Message };
        }
    }

    public FileMetadata? SavePdfFromBytes(byte[] pdfBytes, string fileName)
    {
        try
        {
            var id = Guid.NewGuid().ToString("N")[..12];
            var extension = Path.GetExtension(fileName);
            var finalFileName = string.IsNullOrEmpty(extension) ? $"{id}.pdf" : fileName;
            var destinationPath = Path.Combine(_outputFolder, finalFileName);

            File.WriteAllBytes(destinationPath, pdfBytes);

            var metadata = new FileMetadata
            {
                Id = id,
                FileName = finalFileName,
                FilePath = destinationPath,
                DownloadUrl = $"{_downloadBasePath}/{id}",
                FileSize = pdfBytes.Length,
                CreatedAt = DateTime.Now
            };

            _files[id] = metadata;

            Log.Information("File saved from bytes: {FileName}, Size: {Size}",
                finalFileName, metadata.FormattedFileSize);

            return metadata;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error saving file from bytes: {FileName}", fileName);
            return null;
        }
    }

    public FileMetadata? GetFileById(string id)
    {
        if (_files.TryGetValue(id, out var metadata))
        {
            return metadata;
        }

        var files = Directory.GetFiles(_outputFolder, "*.pdf");
        foreach (var file in files)
        {
            if (Path.GetFileNameWithoutExtension(file).Contains(id) ||
                file.Contains(id))
            {
                var info = new FileInfo(file);
                metadata = new FileMetadata
                {
                    Id = id,
                    FileName = info.Name,
                    FilePath = file,
                    DownloadUrl = $"{_downloadBasePath}/{id}",
                    FileSize = info.Length,
                    CreatedAt = info.CreationTime
                };
                return metadata;
            }
        }

        Log.Debug("File not found by ID: {Id}", id);
        return null;
    }

    public string? GetFilePath(string id)
    {
        var metadata = GetFileById(id);
        return metadata?.FilePath;
    }

    public bool FileExists(string id)
    {
        var path = GetFilePath(id);
        return !string.IsNullOrEmpty(path) && File.Exists(path);
    }

    public List<FileMetadata> GetAllFiles()
    {
        return _files.Values.OrderByDescending(f => f.CreatedAt).ToList();
    }

    public List<FileMetadata> GetFilesByDate(DateTime date)
    {
        return _files.Values
            .Where(f => f.CreatedAt.Date == date.Date)
            .OrderByDescending(f => f.CreatedAt)
            .ToList();
    }

    public List<FileMetadata> GetFilesInRange(DateTime from, DateTime to)
    {
        return _files.Values
            .Where(f => f.CreatedAt >= from && f.CreatedAt <= to)
            .OrderByDescending(f => f.CreatedAt)
            .ToList();
    }

    public int GetFileCount()
    {
        return _files.Count;
    }

    public long GetTotalSize()
    {
        return _files.Values.Sum(f => f.FileSize);
    }

    public string GetOutputFolder()
    {
        return _outputFolder;
    }

    public string GetDownloadBasePath()
    {
        return _downloadBasePath;
    }

    public void DeleteFile(string id)
    {
        var metadata = GetFileById(id);
        if (metadata == null)
        {
            Log.Warning("Cannot delete - file not found: {Id}", id);
            return;
        }

        try
        {
            if (File.Exists(metadata.FilePath))
            {
                File.Delete(metadata.FilePath);
            }

            _files.TryRemove(id, out _);

            Log.Information("File deleted: {Id}, {FileName}", id, metadata.FileName);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error deleting file: {Id}", id);
        }
    }

    public void ClearAllFiles()
    {
        try
        {
            var files = Directory.GetFiles(_outputFolder, "*.pdf");
            foreach (var file in files)
            {
                File.Delete(file);
            }

            _files.Clear();

            Log.Information("Cleared all {Count} files from output folder", files.Length);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error clearing files");
        }
    }
}

public class FileMetadata
{
    public string? Id { get; set; }
    public string? FileName { get; set; }
    public string? FilePath { get; set; }
    public string? DownloadUrl { get; set; }
    public long FileSize { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? Error { get; set; }

    public string FormattedFileSize
    {
        get
        {
            if (FileSize < 1024)
                return $"{FileSize} B";
            if (FileSize < 1024 * 1024)
                return $"{FileSize / 1024.0:F1} KB";
            if (FileSize < 1024 * 1024 * 1024)
                return $"{FileSize / (1024.0 * 1024.0):F1} MB";
            return $"{FileSize / (1024.0 * 1024.0 * 1024.0):F1} GB";
        }
    }

    public bool IsSuccess => string.IsNullOrEmpty(Error);
}
