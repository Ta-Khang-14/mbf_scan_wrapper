namespace mbf_scan_service.Models;

public class ScanSession
{
    public string SessionId { get; set; } = Guid.NewGuid().ToString("N");
    public string? ScannerName { get; set; }
    public ScanStatus Status { get; set; } = ScanStatus.Idle;
    public List<ScanPage> Pages { get; set; } = new();
    public List<ScanFile> Files { get; set; } = new();
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public ScanSettings? Settings { get; set; }
    public string? ErrorMessage { get; set; }

    public ScanSession()
    {
        StartedAt = DateTime.Now;
    }

    public ScanSession(string scannerName)
    {
        SessionId = Guid.NewGuid().ToString("N");
        ScannerName = scannerName;
        StartedAt = DateTime.Now;
        Status = ScanStatus.Idle;
    }

    public int TotalPages => Pages.Count;
    public int TotalFiles => Files.Count;
    public TimeSpan? ElapsedTime => CompletedAt.HasValue
        ? CompletedAt.Value - StartedAt
        : DateTime.Now - StartedAt;
}

public class ScanSettings
{
    public int? DPI { get; set; }
    public string? ColorMode { get; set; }
    public string? PaperSize { get; set; }
    public bool? EnableDuplex { get; set; }
}
