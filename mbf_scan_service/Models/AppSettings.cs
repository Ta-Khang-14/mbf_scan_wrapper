namespace mbf_scan_service.Models;

public class ScannerConfig
{
    public int DefaultMaxPages { get; set; } = 400;
    public bool DefaultEnableDuplex { get; set; } = true;
    public int DefaultDPI { get; set; } = 300;
    public string DefaultColorMode { get; set; } = "Color";
    public string DefaultPaperSize { get; set; } = "A4";
    public int ScanTimeoutSeconds { get; set; } = 120;
}

public class CleanupConfig
{
    public int TempRetentionDays { get; set; } = 1;
    public int OutputRetentionDays { get; set; } = 30;
    public int CleanupIntervalHours { get; set; } = 1;
}

public class AppSettings
{
    public ScannerConfig Scanner { get; set; } = new();
    public CleanupConfig Cleanup { get; set; } = new();
    public SignConfig Sign { get; set; } = new();
    public string TempFolder { get; set; } = "temp";
    public string OutputFolder { get; set; } = "output";
}

public class SignConfig
{
    public string UrlApi { get; set; } = "";
    public string UrlSignTokenPdf { get; set; } = "";
    public string UrlUploadPath { get; set; } = "";
}
