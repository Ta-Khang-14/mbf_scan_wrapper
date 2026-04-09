namespace mbf_scan_service.Services;

using mbf_scan_service.Models;
using Serilog;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PdfSharp.Drawing;
using PdfSharp;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Formats.Jpeg;

public class PDFService
{
    private readonly string _outputFolder;
    private const double A4Width = 595.276;  // Points (A4 width in points)
    private const double A4Height = 841.889; // Points (A4 height in points)

    public PDFService()
    {
        _outputFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "output");
        EnsureOutputFolder();
    }

    private void EnsureOutputFolder()
    {
        if (!Directory.Exists(_outputFolder))
        {
            Directory.CreateDirectory(_outputFolder);
            Log.Information("Created output folder: {Folder}", _outputFolder);
        }
    }

    public string ConvertTiffToPdf(string tiffPath, string outputFileName)
    {
        if (!File.Exists(tiffPath))
        {
            Log.Warning("TIFF file not found: {Path}", tiffPath);
            return string.Empty;
        }

        try
        {
            var outputPath = Path.Combine(_outputFolder, outputFileName);
            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var document = new PdfDocument();
            document.Info.Title = Path.GetFileNameWithoutExtension(outputFileName);

            using var image = Image.Load<Rgba32>(tiffPath);

            var page = document.AddPage();
            page.Size = PageSize.A4;

            using var gfx = XGraphics.FromPdfPage(page);

            var imageWidth = image.Width;
            var imageHeight = image.Height;

            var scale = Math.Min(A4Width / imageWidth, A4Height / imageHeight);

            var scaledWidth = imageWidth * scale;
            var scaledHeight = imageHeight * scale;

            var x = (A4Width - scaledWidth) / 2;
            var y = (A4Height - scaledHeight) / 2;

            using var stream = new MemoryStream();
            image.Save(stream, JpegFormat.Instance);
            stream.Position = 0;

            using var pdfImage = XImage.FromStream(stream);
            gfx.DrawImage(pdfImage, x, y, scaledWidth, scaledHeight);

            document.Save(outputPath);
            Log.Information("Converted TIFF to PDF: {OutputPath}", outputPath);

            return outputPath;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error converting TIFF to PDF: {Path}", tiffPath);
            return string.Empty;
        }
    }

    public string MergeTiffsToPdf(List<string> tiffPaths, string outputFileName)
    {
        if (tiffPaths.Count == 0)
        {
            Log.Warning("No TIFF files to merge");
            return string.Empty;
        }

        try
        {
            var outputPath = Path.Combine(_outputFolder, outputFileName);
            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var document = new PdfDocument();
            document.Info.Title = Path.GetFileNameWithoutExtension(outputFileName);

            foreach (var tiffPath in tiffPaths)
            {
                if (!File.Exists(tiffPath))
                {
                    Log.Warning("TIFF file not found, skipping: {Path}", tiffPath);
                    continue;
                }

                using var image = Image.Load<Rgba32>(tiffPath);

                var page = document.AddPage();
                page.Size = PageSize.A4;

                using var gfx = XGraphics.FromPdfPage(page);

                var imageWidth = image.Width;
                var imageHeight = image.Height;

                var scale = Math.Min(A4Width / imageWidth, A4Height / imageHeight);

                var scaledWidth = imageWidth * scale;
                var scaledHeight = imageHeight * scale;

                var x = (A4Width - scaledWidth) / 2;
                var y = (A4Height - scaledHeight) / 2;

                using var stream = new MemoryStream();
                image.Save(stream, JpegFormat.Instance);
                stream.Position = 0;

                using var pdfImage = XImage.FromStream(stream);
                gfx.DrawImage(pdfImage, x, y, scaledWidth, scaledHeight);
            }

            document.Save(outputPath);
            Log.Information("Merged {Count} TIFFs to PDF: {OutputPath}", tiffPaths.Count, outputPath);

            return outputPath;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error merging TIFFs to PDF");
            return string.Empty;
        }
    }

    public string CreatePdfFromPages(List<ScanPage> pages, string fileId)
    {
        var tiffPaths = pages.Select(p => p.ImagePath).ToList();
        var outputFileName = $"scan_{fileId}.pdf";
        return MergeTiffsToPdf(tiffPaths, outputFileName);
    }

    public string AddPageToPdf(string pdfPath, string tiffPath)
    {
        if (!File.Exists(pdfPath))
        {
            Log.Warning("PDF file not found: {Path}", pdfPath);
            return string.Empty;
        }

        if (!File.Exists(tiffPath))
        {
            Log.Warning("TIFF file not found: {Path}", tiffPath);
            return string.Empty;
        }

        try
        {
            using var document = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Modify);

            using var image = Image.Load<Rgba32>(tiffPath);

            var page = document.AddPage();
            page.Size = PageSize.A4;

            using var gfx = XGraphics.FromPdfPage(page);

            var imageWidth = image.Width;
            var imageHeight = image.Height;

            var scale = Math.Min(A4Width / imageWidth, A4Height / imageHeight);

            var scaledWidth = imageWidth * scale;
            var scaledHeight = imageHeight * scale;

            var x = (A4Width - scaledWidth) / 2;
            var y = (A4Height - scaledHeight) / 2;

            using var stream = new MemoryStream();
            image.Save(stream, JpegFormat.Instance);
            stream.Position = 0;

            using var pdfImage = XImage.FromStream(stream);
            gfx.DrawImage(pdfImage, x, y, scaledWidth, scaledHeight);

            document.Save(pdfPath);
            Log.Information("Added page to PDF: {PdfPath}", pdfPath);

            return pdfPath;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error adding page to PDF: {PdfPath}", pdfPath);
            return string.Empty;
        }
    }

    public string DeletePageFromPdf(string pdfPath, int pageIndex)
    {
        if (!File.Exists(pdfPath))
        {
            Log.Warning("PDF file not found: {Path}", pdfPath);
            return string.Empty;
        }

        try
        {
            using var document = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Modify);

            if (pageIndex < 0 || pageIndex >= document.Pages.Count)
            {
                Log.Warning("Invalid page index: {Index}, Total pages: {Total}", pageIndex, document.Pages.Count);
                return string.Empty;
            }

            document.Pages.RemoveAt(pageIndex);

            var tempPath = pdfPath + ".tmp";
            document.Save(tempPath);

            File.Delete(pdfPath);
            File.Move(tempPath, pdfPath);

            Log.Information("Deleted page {Index} from PDF: {PdfPath}", pageIndex, pdfPath);

            return pdfPath;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error deleting page from PDF: {PdfPath}, Index: {Index}", pdfPath, pageIndex);
            return string.Empty;
        }
    }

    public string DeletePageAndCreateNew(List<ScanPage> pages, int pageIndexToRemove, string fileId)
    {
        var newPages = new List<ScanPage>();

        for (int i = 0; i < pages.Count; i++)
        {
            if (i == pageIndexToRemove)
                continue;
            newPages.Add(pages[i]);
        }

        if (newPages.Count == 0)
        {
            Log.Warning("All pages deleted, no PDF to create");
            return string.Empty;
        }

        return CreatePdfFromPages(newPages, fileId);
    }

    public int GetPageCount(string pdfPath)
    {
        if (!File.Exists(pdfPath))
        {
            return 0;
        }

        try
        {
            using var document = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Import);
            return document.Pages.Count;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error getting page count: {Path}", pdfPath);
            return 0;
        }
    }

    public long GetFileSize(string pdfPath)
    {
        if (!File.Exists(pdfPath))
        {
            return 0;
        }

        return new FileInfo(pdfPath).Length;
    }

    public string GetOutputFolder()
    {
        return _outputFolder;
    }

    public void DeletePdf(string pdfPath)
    {
        try
        {
            if (File.Exists(pdfPath))
            {
                File.Delete(pdfPath);
                Log.Debug("Deleted PDF: {Path}", pdfPath);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error deleting PDF: {Path}", pdfPath);
        }
    }
}
