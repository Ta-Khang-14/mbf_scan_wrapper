namespace mbf_scan_service.Services;

using mbf_scan_service.Models;
using Serilog;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using ZXing;
using ZXing.Common;
using static ZXing.RGBLuminanceSource;

public class BarcodeService
{
    private const string DefaultFileBarcodePattern = "File_Separate";
    private const string DefaultDocBarcodePattern = "Doc_Separate";

    public BarcodeService()
    {
        Log.Information("BarcodeService initialized with pattern: {Pattern}", DefaultFileBarcodePattern);
    }

    public BarcodeDetectionResult? DetectBarcode(string imagePath)
    {
        return DetectBarcodeAsync(imagePath).GetAwaiter().GetResult();
    }

    public async Task<BarcodeDetectionResult?> DetectBarcodeAsync(string imagePath)
    {
        if (!File.Exists(imagePath))
        {
            Log.Warning("Image file not found: {Path}", imagePath);
            return null;
        }

        try
        {
            var imageBytes = await File.ReadAllBytesAsync(imagePath);
            return DetectBarcodeFromBytes(imageBytes);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error detecting barcode from file: {Path}", imagePath);
            return null;
        }
    }

    public BarcodeDetectionResult? DetectBarcodeFromBytes(byte[] imageBytes)
    {
        try
        {
            using var image = Image.Load<Rgba32>(imageBytes);
            var width = image.Width;
            var height = image.Height;

            Log.Information("[BarcodeDebug] Image loaded: Width={Width}, Height={Height}, ByteArrayLength={ByteArrayLength}",
                width, height, imageBytes.Length);

            // Thử quét từng vùng: trên → dưới → toàn trang
            var regions = new[]
            {
                new { Name = "TOP", YStart = 0, YEnd = (int)(height * 0.35) },
                new { Name = "BOTTOM", YStart = (int)(height * 0.65), YEnd = height },
                new { Name = "FULL", YStart = 0, YEnd = height }
            };

            foreach (var region in regions)
            {
                Log.Information("[BarcodeDebug] Trying region: {Region}, Y={YStart}-{YEnd}",
                    region.Name, region.YStart, region.YEnd);

                var result = TryDecodeRegion(image, region.YStart, region.YEnd);
                if (result?.Found == true)
                {
                    result.RegionScanned = region.Name;
                    return result;
                }
            }

            Log.Warning("[BarcodeDebug] Barcode not detected in any region");
            return new BarcodeDetectionResult { Found = false };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error in barcode detection");
            return new BarcodeDetectionResult { Found = false, Error = ex.Message };
        }
    }

    private BarcodeDetectionResult? TryDecodeRegion(Image<Rgba32> image, int yStart, int yEnd)
    {
        var width = image.Width;
        var regionHeight = yEnd - yStart;
        var totalPixels = width * regionHeight;

        var source = new byte[totalPixels];
        byte minVal = 255, maxVal = 0;
        long sumVal = 0;

        for (int y = yStart; y < yEnd; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var pixel = image[x, y];
                byte gray = (byte)((pixel.R + pixel.G + pixel.B) / 3);
                source[(y - yStart) * width + x] = gray;

                if (gray < minVal) minVal = gray;
                if (gray > maxVal) maxVal = gray;
                sumVal += gray;
            }
        }

        double avgVal = (double)sumVal / totalPixels;
        int contrastRange = maxVal - minVal;

        Log.Information("[BarcodeDebug] Region stats: Height={Height}, Min={MinVal}, Max={MaxVal}, Avg={AvgVal:F2}, ContrastRange={ContrastRange}",
            regionHeight, minVal, maxVal, avgVal, contrastRange);

        var luminanceSource = new RGBLuminanceSource(source, width, regionHeight, BitmapFormat.Gray8);

        var hints = new Dictionary<DecodeHintType, object>
        {
            { DecodeHintType.POSSIBLE_FORMATS, new BarcodeFormat[] {
                BarcodeFormat.CODE_128,
                BarcodeFormat.CODE_39,
                BarcodeFormat.CODE_93,
                BarcodeFormat.CODABAR,
                BarcodeFormat.EAN_13,
                BarcodeFormat.EAN_8,
                BarcodeFormat.UPC_A,
                BarcodeFormat.UPC_E,
                BarcodeFormat.ITF,
                BarcodeFormat.QR_CODE,
                BarcodeFormat.DATA_MATRIX,
                BarcodeFormat.PDF_417,
                BarcodeFormat.AZTEC,
                BarcodeFormat.MAXICODE
            }},
            { DecodeHintType.TRY_HARDER, true },
            { DecodeHintType.ALSO_INVERTED, true }
        };

        var reader = new MultiFormatReader();
        reader.Hints = hints;

        try
        {
            var binaryBitmap = new BinaryBitmap(new HybridBinarizer(luminanceSource));
            var result = reader.decode(binaryBitmap);

            if (result != null)
            {
                Log.Information("[BarcodeDebug] Barcode detected: Format={Format}, Value={Value}, RawBytes={RawBytesLen}",
                    result.BarcodeFormat, result.Text, result.RawBytes?.Length ?? 0);

                if (IsSeparatorBarcode(result.Text))
                {
                    var separatorType = GetSeparatorType(result.Text);
                    Log.Information("SEPARATOR barcode found: {Value}, Type: {Type}", result.Text, separatorType);
                    return new BarcodeDetectionResult
                    {
                        Found = true,
                        Value = result.Text,
                        Format = result.BarcodeFormat.ToString(),
                        IsSeparator = true,
                        IsDocSeparator = separatorType == SeparatorBarcodeType.DocSeparator
                    };
                }
                else
                {
                    Log.Warning("[BarcodeDebug] Barcode found but value '{Value}' does not match pattern '{Pattern}'",
                        result.Text, DefaultFileBarcodePattern);
                }
            }
        }
        catch (Exception decodeEx)
        {
            Log.Debug("[BarcodeDebug] No barcode in this region: {Message}", decodeEx.Message);
        }

        return new BarcodeDetectionResult { Found = false };
    }

    public bool IsSeparatorBarcode(string barcodeValue)
    {
        if (string.IsNullOrWhiteSpace(barcodeValue))
        {
            return false;
        }

        var trimmed = barcodeValue.Trim();
        return trimmed.Equals(DefaultFileBarcodePattern, StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals(DefaultDocBarcodePattern, StringComparison.OrdinalIgnoreCase);
    }

    public SeparatorBarcodeType GetSeparatorType(string barcodeValue)
    {
        if (string.IsNullOrWhiteSpace(barcodeValue))
        {
            return SeparatorBarcodeType.None;
        }

        var trimmed = barcodeValue.Trim();
        if (trimmed.Equals(DefaultDocBarcodePattern, StringComparison.OrdinalIgnoreCase))
        {
            return SeparatorBarcodeType.DocSeparator;
        }
        if (trimmed.Equals(DefaultFileBarcodePattern, StringComparison.OrdinalIgnoreCase))
        {
            return SeparatorBarcodeType.FileSeparator;
        }
        return SeparatorBarcodeType.None;
    }

    public List<List<ScanPage>> GroupPagesIntoFiles(List<ScanPage> pages)
    {
        var files = new List<List<ScanPage>>();
        var currentFile = new List<ScanPage>();

        foreach (var page in pages)
        {
            if (page.IsBarcodeSeparator || page.IsDocSeparator)
            {
                if (currentFile.Count > 0)
                {
                    files.Add(currentFile);
                    currentFile = new List<ScanPage>();
                }
            }
            else
            {
                currentFile.Add(page);
            }
        }

        if (currentFile.Count > 0)
        {
            files.Add(currentFile);
        }

        Log.Information("Split pages into {FileCount} file groups", files.Count);
        return files;
    }

    public List<DocumentGroupResponse> GroupPagesIntoDocuments(List<ScanPage> pages, string? baseUrl = null)
    {
        var documents = new List<DocumentGroupResponse>();
        var currentDoc = new DocumentGroupResponse { DocIndex = 0 };
        var currentFile = new FileGroupResponse { FileIndex = 0 };

        foreach (var page in pages)
        {
            if (page.IsDocSeparator)
            {
                if (currentFile.Pages.Count > 0)
                {
                    currentFile.PageCount = currentFile.Pages.Count;
                    currentDoc.Files.Add(currentFile);
                }
                if (currentDoc.Files.Count > 0)
                {
                    documents.Add(currentDoc);
                }

                currentDoc = new DocumentGroupResponse { DocIndex = documents.Count };
                currentFile = new FileGroupResponse { FileIndex = 0 };
            }
            else if (page.IsBarcodeSeparator)
            {
                if (currentFile.Pages.Count > 0)
                {
                    currentFile.PageCount = currentFile.Pages.Count;
                    currentDoc.Files.Add(currentFile);
                }
                currentFile = new FileGroupResponse { FileIndex = currentDoc.Files.Count };
            }
            else
            {
                var pageInfo = new PageInfo
                {
                    PageIndex = page.PageIndex,
                    ImagePath = page.ImagePath,
                    IsBarcodeSeparator = page.IsBarcodeSeparator,
                    IsDocSeparator = page.IsDocSeparator,
                    BarcodeValue = page.BarcodeValue,
                    Side = page.Side,
                    ScannedAt = page.ScannedAt
                };

                if (!string.IsNullOrEmpty(baseUrl))
                {
                    pageInfo.ImageUrl = $"{baseUrl}/api/scanner/preview/{page.PageIndex}";
                    pageInfo.PdfUrl = $"{baseUrl}/api/scanner/page-pdf/{page.PageIndex}";
                }

                currentFile.Pages.Add(pageInfo);
            }
        }

        if (currentFile.Pages.Count > 0)
        {
            currentFile.PageCount = currentFile.Pages.Count;
            currentDoc.Files.Add(currentFile);
        }
        if (currentDoc.Files.Count > 0)
        {
            documents.Add(currentDoc);
        }

        if (documents.Count == 0 && pages.Count > 0)
        {
            documents.Add(currentDoc);
        }

        Log.Information("Grouped pages into {DocCount} documents with {FileCount} total files",
            documents.Count, documents.Sum(d => d.Files.Count));
        return documents;
    }

    public void ProcessSessionPages(List<ScanPage> pages)
    {
        ProcessSessionPagesAsync(pages).GetAwaiter().GetResult();
    }

    public async Task ProcessSessionPagesAsync(List<ScanPage> pages)
    {
        if (pages.Count == 0) return;

        Log.Information("Processing {Count} pages for barcode detection (parallel)", pages.Count);

        const int maxConcurrency = 5;
        using var semaphore = new SemaphoreSlim(maxConcurrency);

        var tasks = pages.Select(async page =>
        {
            await semaphore.WaitAsync();
            try
            {
                var result = await DetectBarcodeAsync(page.ImagePath);

                if (result != null && result.Found)
                {
                    var separatorType = GetSeparatorType(result.Value!);
                    page.IsDocSeparator = separatorType == SeparatorBarcodeType.DocSeparator;
                    page.IsBarcodeSeparator = separatorType == SeparatorBarcodeType.FileSeparator;
                    page.BarcodeValue = result.Value;
                    Log.Information("Page {Index} marked: IsDocSeparator={IsDoc}, IsFileSeparator={IsFile}, Barcode={Value}",
                        page.PageIndex, page.IsDocSeparator, page.IsBarcodeSeparator, result.Value);
                }
                else
                {
                    page.IsBarcodeSeparator = false;
                    page.IsDocSeparator = false;
                }
            }
            finally
            {
                semaphore.Release();
            }
        }).ToList();

        await Task.WhenAll(tasks);

        Log.Information("Barcode detection completed for {Count} pages", pages.Count);
    }
}

public class BarcodeDetectionResult
{
    public bool Found { get; set; }
    public string? Value { get; set; }
    public string? Format { get; set; }
    public bool IsSeparator { get; set; }
    public bool IsDocSeparator { get; set; }
    public string? Error { get; set; }
    public string? RegionScanned { get; set; }
}

public enum SeparatorBarcodeType
{
    None,
    FileSeparator,
    DocSeparator
}
