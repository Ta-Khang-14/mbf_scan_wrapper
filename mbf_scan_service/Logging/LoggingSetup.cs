namespace mbf_scan_service.Logging;

using Serilog;
using Serilog.Events;

public static class LoggingSetup
{
    private static bool _isConfigured = false;

    public static void Configure()
    {
        if (_isConfigured) return;

        var logFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
        Directory.CreateDirectory(logFolder);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithThreadId()
            .WriteTo.File(
                path: Path.Combine(logFolder, "scan_service_.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] [{ThreadId}] {Message:lj}{NewLine}{Exception}",
                shared: true,
                flushToDiskInterval: TimeSpan.FromSeconds(2))
            .WriteTo.Console(
                outputTemplate: "{Timestamp:HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        _isConfigured = true;

        Log.Information("=== Scan Service Started ===");
        Log.Information("Log folder: {LogFolder}", logFolder);
    }

    public static void Shutdown()
    {
        Log.Information("=== Scan Service Shutting Down ===");
        Log.CloseAndFlush();
    }
}
