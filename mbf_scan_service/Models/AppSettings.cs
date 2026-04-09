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

public class AppSettings
{
    public ScannerConfig Scanner { get; set; } = new();
    public string TempFolder { get; set; } = "temp";
    public string OutputFolder { get; set; } = "output";
}
