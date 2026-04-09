namespace mbf_scan_service.Models;

public class ScanFile
{
    public string FileId { get; set; } = Guid.NewGuid().ToString("N");
    public string FileName { get; set; } = string.Empty;
    public string PDFPath { get; set; } = string.Empty;
    public string? DownloadUrl { get; set; }
    public string? OCRResult { get; set; }
    public List<ScanPage> Pages { get; set; } = new();
    public int TotalPages => Pages.Count;
    public DateTime CreatedAt { get; set; }
    public long FileSize { get; set; }

    public ScanFile()
    {
        CreatedAt = DateTime.Now;
        FileName = $"scan_{FileId[..8]}.pdf";
    }

    public ScanFile(List<ScanPage> pages)
    {
        Pages = pages;
        CreatedAt = DateTime.Now;
        FileName = $"scan_{FileId[..8]}.pdf";
    }
}
