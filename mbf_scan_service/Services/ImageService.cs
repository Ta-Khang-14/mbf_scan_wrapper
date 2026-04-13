namespace mbf_scan_service.Services;

using Serilog;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using System.Collections.Concurrent;

public class ImageService : IDisposable
{
    private readonly string _previewCacheFolder;
    private readonly int _maxWidth;
    private readonly int _quality;
    private readonly TimeSpan _cacheDuration;

    private readonly ConcurrentDictionary<string, CachedImageInfo> _cache = new();
    private readonly object _cacheCleanupLock = new();
    private System.Threading.Timer? _cacheCleanupTimer;

    public ImageService(int maxWidth = 200, int quality = 50, int cacheDurationMinutes = 5)
    {
        _maxWidth = maxWidth;
        _quality = quality;
        _cacheDuration = TimeSpan.FromMinutes(cacheDurationMinutes);
        _previewCacheFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "temp", "preview_cache");
        
        EnsureCacheFolder();
        StartCacheCleanup();
    }

    private void EnsureCacheFolder()
    {
        if (!Directory.Exists(_previewCacheFolder))
        {
            Directory.CreateDirectory(_previewCacheFolder);
            Log.Information("Created preview cache folder: {Folder}", _previewCacheFolder);
        }
    }

    private void StartCacheCleanup()
    {
        _cacheCleanupTimer = new System.Threading.Timer(CleanupExpiredCache, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    private void CleanupExpiredCache(object? state)
    {
        lock (_cacheCleanupLock)
        {
            var expiredKeys = _cache
                .Where(kvp => DateTime.Now - kvp.Value.CreatedAt > _cacheDuration)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in expiredKeys)
            {
                if (_cache.TryRemove(key, out var cachedInfo))
                {
                    try
                    {
                        if (File.Exists(cachedInfo.CachedPath))
                        {
                            File.Delete(cachedInfo.CachedPath);
                            Log.Debug("Deleted expired cache: {Path}", cachedInfo.CachedPath);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Debug("Error deleting cache file: {Error}", ex.Message);
                    }
                }
            }
        }
    }

    private string GetCacheKey(string originalPath)
    {
        var fileInfo = new FileInfo(originalPath);
        var lastWrite = fileInfo.Exists ? fileInfo.LastWriteTimeUtc.Ticks : 0;
        return $"{Path.GetFileNameWithoutExtension(originalPath)}_{lastWrite}_{_maxWidth}_{_quality}";
    }

    public string? GetPreviewPath(string originalPath)
    {
        if (!File.Exists(originalPath))
        {
            Log.Warning("Original image not found: {Path}", originalPath);
            return null;
        }

        var cacheKey = GetCacheKey(originalPath);
        var cachedPath = Path.Combine(_previewCacheFolder, $"{cacheKey}.jpg");

        if (_cache.TryGetValue(cacheKey, out var cachedInfo))
        {
            if (DateTime.Now - cachedInfo.CreatedAt < _cacheDuration && File.Exists(cachedInfo.CachedPath))
            {
                Log.Debug("Using cached preview: {Path}", cachedInfo.CachedPath);
                return cachedInfo.CachedPath;
            }
            _cache.TryRemove(cacheKey, out _);
        }

        try
        {
            using var image = Image.Load<Rgba32>(originalPath);
            
            int newWidth = image.Width;
            int newHeight = image.Height;
            
            if (image.Width > _maxWidth)
            {
                newWidth = _maxWidth;
                newHeight = (int)((double)image.Height / image.Width * _maxWidth);
            }

            image.Mutate(x => x.Resize(newWidth, newHeight, KnownResamplers.Lanczos3));

            var encoder = new JpegEncoder
            {
                Quality = _quality
            };

            image.Save(cachedPath, encoder);

            _cache[cacheKey] = new CachedImageInfo
            {
                CachedPath = cachedPath,
                CreatedAt = DateTime.Now
            };

            Log.Information("Created preview: {Original} -> {Preview} ({Width}x{Height}, {Quality}%)", 
                originalPath, cachedPath, newWidth, newHeight, _quality);

            return cachedPath;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error creating preview for: {Path}", originalPath);
            return null;
        }
    }

    public void InvalidateCache(string originalPath)
    {
        var cacheKey = GetCacheKey(originalPath);
        if (_cache.TryRemove(cacheKey, out var cachedInfo))
        {
            try
            {
                if (File.Exists(cachedInfo.CachedPath))
                {
                    File.Delete(cachedInfo.CachedPath);
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Error invalidating cache: {Error}", ex.Message);
            }
        }
    }

    public void ClearAllCache()
    {
        lock (_cacheCleanupLock)
        {
            foreach (var cachedInfo in _cache.Values)
            {
                try
                {
                    if (File.Exists(cachedInfo.CachedPath))
                    {
                        File.Delete(cachedInfo.CachedPath);
                    }
                }
                catch { }
            }
            _cache.Clear();
            Log.Information("Cleared all preview cache");
        }
    }

    public void Dispose()
    {
        _cacheCleanupTimer?.Dispose();
        ClearAllCache();
    }

    private class CachedImageInfo
    {
        public string CachedPath { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }
}
