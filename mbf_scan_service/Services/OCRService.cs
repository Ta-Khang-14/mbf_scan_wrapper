namespace mbf_scan_service.Services;

using Serilog;
using Tesseract;

public class OCRService : IDisposable
{
    private readonly string _tessdataPath;
    private readonly string[] _languages;
    private TesseractEngine? _engine;

    private static readonly string[] DefaultLanguages = { "eng", "vie" };

    public OCRService() : this(DefaultLanguages)
    {
    }

    public OCRService(string[] languages)
    {
        _languages = languages;
        _tessdataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tessdata");

        EnsureTessdataExists();
        InitializeEngine();

        Log.Information("OCRService initialized with languages: {Languages}", string.Join("+", _languages));
    }

    private void EnsureTessdataExists()
    {
        if (!Directory.Exists(_tessdataPath))
        {
            Directory.CreateDirectory(_tessdataPath);
            Log.Warning("Created tessdata directory: {Path}", _tessdataPath);
        }

        var missingFiles = new List<string>();
        foreach (var lang in _languages)
        {
            var trainedDataPath = Path.Combine(_tessdataPath, $"{lang}.traineddata");
            if (!File.Exists(trainedDataPath))
            {
                missingFiles.Add(lang);
            }
        }

        if (missingFiles.Count > 0)
        {
            Log.Warning("Missing tessdata files for languages: {Missing}", string.Join(", ", missingFiles));
            Log.Warning("Please download traineddata files from: https://github.com/tesseract-ocr/tessdata");
        }
    }

    private void InitializeEngine()
    {
        try
        {
            var langConfig = string.Join("+", _languages);
            _engine = new TesseractEngine(_tessdataPath, langConfig, EngineMode.Default);
            Log.Information("Tesseract engine initialized successfully");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to initialize Tesseract engine");
            _engine = null;
        }
    }

    public string? PerformOCR(string imagePath)
    {
        return PerformOCRAsync(imagePath).GetAwaiter().GetResult();
    }

    public async Task<string?> PerformOCRAsync(string imagePath)
    {
        if (!File.Exists(imagePath))
        {
            Log.Warning("Image file not found: {Path}", imagePath);
            return null;
        }

        if (_engine == null)
        {
            Log.Warning("OCR engine not initialized");
            return null;
        }

        return await Task.Run(() =>
        {
            try
            {
                using var img = Pix.LoadFromFile(imagePath);
                using var page = _engine.Process(img);

                var text = page.GetText();
                var confidence = page.GetMeanConfidence();

                Log.Information("OCR completed for: {Path}, Confidence: {Confidence:P2}, Length: {Length}",
                    Path.GetFileName(imagePath), confidence, text.Length);

                if (string.IsNullOrWhiteSpace(text))
                {
                    Log.Debug("No text found in image: {Path}", imagePath);
                    return null;
                }

                return text.Trim();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "OCR failed for image: {Path}", imagePath);
                return null;
            }
        });
    }

    public OCRResult PerformOCRWithDetails(string imagePath)
    {
        var result = new OCRResult();

        if (!File.Exists(imagePath))
        {
            result.Error = $"Image file not found: {imagePath}";
            return result;
        }

        if (_engine == null)
        {
            result.Error = "OCR engine not initialized";
            return result;
        }

        try
        {
            using var img = Pix.LoadFromFile(imagePath);
            using var page = _engine.Process(img);

            result.Text = page.GetText();
            result.Confidence = page.GetMeanConfidence();

            Log.Information("OCR completed for: {Path}, Confidence: {Confidence:P2}",
                Path.GetFileName(imagePath), result.Confidence);

            return result;
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
            Log.Error(ex, "OCR failed for image: {Path}", imagePath);
            return result;
        }
    }

    public bool IsEngineReady()
    {
        return _engine != null;
    }

    public string GetTessdataPath()
    {
        return _tessdataPath;
    }

    public void Dispose()
    {
        if (_engine != null)
        {
            _engine.Dispose();
            _engine = null;
            Log.Information("OCR engine disposed");
        }
    }
}

public class OCRResult
{
    public string? Text { get; set; }
    public double Confidence { get; set; }
    public string? Error { get; set; }
    public bool IsSuccess => string.IsNullOrEmpty(Error) && !string.IsNullOrEmpty(Text);
}
