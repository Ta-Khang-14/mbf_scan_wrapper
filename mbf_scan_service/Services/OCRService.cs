namespace mbf_scan_service.Services;

using Serilog;
using Tesseract;

public class OCRService : IDisposable
{
    private readonly string _tessdataPath;
    private readonly string[] _languages;
    private readonly string[] _effectiveLanguages;
    private TesseractEngine? _engine;
    private readonly EngineMode _engineMode;

    private static readonly string[] DefaultLanguages = { "eng", "vie" };

    public OCRService() : this(DefaultLanguages)
    {
    }

    public OCRService(string[] languages)
    {
        _languages = languages;
        _tessdataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tessdata");

        EnsureTessdataExists();
        (_effectiveLanguages, _engineMode) = InitializeEngine();

        Log.Information("OCRService initialized with languages: {Languages}, mode: {Mode}",
            string.Join("+", _effectiveLanguages), _engineMode);
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
            var bestTrainedDataPath = Path.Combine(_tessdataPath, $"{lang}_best.traineddata");

            if (!File.Exists(trainedDataPath) && !File.Exists(bestTrainedDataPath))
            {
                missingFiles.Add(lang);
            }
            else if (File.Exists(bestTrainedDataPath) && !File.Exists(trainedDataPath))
            {
                Log.Information("Found best traineddata for {Lang}: {Path} (recommended for better accuracy)", lang, bestTrainedDataPath);
            }
            else if (File.Exists(bestTrainedDataPath))
            {
                Log.Information("Found best traineddata for {Lang}: {Path} (better accuracy than standard)", lang, bestTrainedDataPath);
            }
        }

        if (missingFiles.Count > 0)
        {
            Log.Warning("Missing tessdata files for languages: {Missing}", string.Join(", ", missingFiles));
            Log.Warning("Please download traineddata files from: https://github.com/tesseract-ocr/tessdata (standard) or https://github.com/tesseract-ocr/tessdata_best (best quality)");
            Log.Information("For Vietnamese documents, downloading vie_best.traineddata from tessdata_best is recommended for improved OCR accuracy");
        }
        else
        {
            var hasBest = Directory.GetFiles(_tessdataPath, "*_best.traineddata").Length > 0;
            if (hasBest)
            {
                Log.Information("OCR is configured to use LSTM mode with best traineddata for optimal Vietnamese OCR accuracy");
            }
        }
    }

    private (string[] effectiveLanguages, EngineMode mode) InitializeEngine()
    {
        var effectiveLanguages = new List<string>();
        var useLstmOnly = true;
        var hasAnyBest = false;

        foreach (var lang in _languages)
        {
            var bestTrainedDataPath = Path.Combine(_tessdataPath, $"{lang}_best.traineddata");
            var standardTrainedDataPath = Path.Combine(_tessdataPath, $"{lang}.traineddata");

            if (File.Exists(bestTrainedDataPath))
            {
                effectiveLanguages.Add($"{lang}_best");
                hasAnyBest = true;
                Log.Information("Using best traineddata for language: {Lang}", lang);
            }
            else if (File.Exists(standardTrainedDataPath))
            {
                effectiveLanguages.Add(lang);
                useLstmOnly = false;
                Log.Warning("Best traineddata not found for {Lang}, using standard (LSTM mode disabled)", lang);
            }
            else
            {
                Log.Warning("No traineddata found for language: {Lang}", lang);
            }
        }

        if (effectiveLanguages.Count == 0)
        {
            Log.Error("No traineddata files found for any language");
            return (Array.Empty<string>(), EngineMode.Default);
        }

        var langConfig = string.Join("+", effectiveLanguages);
        var engineMode = useLstmOnly ? EngineMode.LstmOnly : EngineMode.Default;

        try
        {
            _engine = new TesseractEngine(_tessdataPath, langConfig, engineMode);
            Log.Information("Tesseract engine initialized with {Mode} mode, languages: {Languages}",
                engineMode, langConfig);
            return (effectiveLanguages.ToArray(), engineMode);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to initialize Tesseract engine with {Mode} mode, falling back to Default mode", engineMode);

            try
            {
                _engine = new TesseractEngine(_tessdataPath, langConfig, EngineMode.Default);
                Log.Information("Tesseract engine initialized with Default mode (fallback), languages: {Languages}", langConfig);
                return (effectiveLanguages.ToArray(), EngineMode.Default);
            }
            catch (Exception fallbackEx)
            {
                Log.Error(fallbackEx, "Failed to initialize Tesseract engine (fallback also failed)");
                _engine = null;
                return (Array.Empty<string>(), EngineMode.Default);
            }
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
