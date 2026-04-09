namespace mbf_scan_service.Models;

public enum ScanSide
{
    Front,
    Back
}

public enum ScanStatus
{
    Idle,
    Scanning,
    Processing,
    Completed,
    Error
}

public class ScanPage
{
    public int PageIndex { get; set; }
    public string ImagePath { get; set; } = string.Empty;
    public bool IsBarcodeSeparator { get; set; }
    public ScanSide Side { get; set; }
    public DateTime ScannedAt { get; set; }
    public string? BarcodeValue { get; set; }

    public ScanPage()
    {
        ScannedAt = DateTime.Now;
    }

    public ScanPage(int pageIndex, string imagePath, ScanSide side)
    {
        PageIndex = pageIndex;
        ImagePath = imagePath;
        Side = side;
        ScannedAt = DateTime.Now;
    }
}
