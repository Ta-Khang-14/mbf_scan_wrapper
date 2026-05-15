namespace mbf_scan_service.Services;

using mbf_scan_service.Models;
using NTwain;
using NTwain.Data;
using Serilog;
using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Threading;

public class ScannerService : IDisposable
{
    private readonly string _tempFolder;
    private readonly int _maxPages;
    private readonly int _defaultDpi;
    private readonly string _defaultColorMode;
    private readonly int _scanTimeoutMs;
    private readonly ConcurrentDictionary<string, ScanSession> _sessions = new();

    private TwainSession? _twainSession;
    private string? _selectedScannerName;
    private int _currentPageIndex = 0;
    private bool _isScanning = false;
    private ScanSession? _currentSession;
    private bool _hasTransferError = false;
    private string? _lastErrorMessage = null;

    private readonly object _lockObject = new();

    public ScannerService(ScannerConfig config)
    {
        _tempFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "temp");
        _maxPages = config.DefaultMaxPages;
        _defaultDpi = config.DefaultDPI;
        _defaultColorMode = config.DefaultColorMode;
        _scanTimeoutMs = config.ScanTimeoutSeconds * 1000;
        EnsureTempFolder();
    }

    private void EnsureTempFolder()
    {
        if (!Directory.Exists(_tempFolder))
        {
            Directory.CreateDirectory(_tempFolder);
            Log.Information("Created temp folder: {TempFolder}", _tempFolder);
        }
    }

    public static List<string> GetAvailableScanners()
    {
        var scanners = new List<string>();
        try
        {
            var appId = TWIdentity.CreateFromAssembly(DataGroups.Image, Assembly.GetExecutingAssembly());
            var twain = new TwainSession(appId);

            twain.Open();

            foreach (var ds in twain)
            {
                scanners.Add(ds.Name ?? "Unknown Scanner");
            }

            twain.Close();
            Log.Information("Found {Count} available scanners", scanners.Count);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error getting scanner list");
        }
        return scanners;
    }

    public static DiagnosticResult RunDiagnostic()
    {
        var result = new DiagnosticResult { Items = new List<DiagnosticItem>() };
        int passed = 0, failed = 0, warnings = 0;

        // 1. Kiểm tra TWAIN DSM
        var twainDsmItem = CheckTwainDSM();
        result.Items.Add(twainDsmItem);
        UpdateCounts(twainDsmItem.Status, ref passed, ref failed, ref warnings);

        // 2. Kiểm tra TWAIN Registry
        var twainRegItem = CheckTwainRegistry();
        result.Items.Add(twainRegItem);
        UpdateCounts(twainRegItem.Status, ref passed, ref failed, ref warnings);

        // 3. Kiểm tra FUJITSU Driver
        var fujitsuDriverItem = CheckFujitsuDriver();
        result.Items.Add(fujitsuDriverItem);
        UpdateCounts(fujitsuDriverItem.Status, ref passed, ref failed, ref warnings);

        // 4. Kiểm tra FUJITSU Registry
        var fujitsuRegItem = CheckFujitsuRegistry();
        result.Items.Add(fujitsuRegItem);
        UpdateCounts(fujitsuRegItem.Status, ref passed, ref failed, ref warnings);

        // 5. Kiểm tra PaperStream
        var paperStreamItem = CheckPaperStream();
        result.Items.Add(paperStreamItem);
        UpdateCounts(paperStreamItem.Status, ref passed, ref failed, ref warnings);

        // 6. Kiểm tra TWAIN Source Enumeration
        var twainSourceItem = CheckTwainSources();
        result.Items.Add(twainSourceItem);
        UpdateCounts(twainSourceItem.Status, ref passed, ref failed, ref warnings);

        // 7. Kiểm tra USB Connection
        var usbItem = CheckUSBConnection();
        result.Items.Add(usbItem);
        UpdateCounts(usbItem.Status, ref passed, ref failed, ref warnings);

        result.Summary = $"Passed: {passed}, Failed: {failed}, Warnings: {warnings}";
        result.Success = failed == 0;
        return result;
    }

    private static void UpdateCounts(string status, ref int passed, ref int failed, ref int warnings)
    {
        switch (status.ToUpperInvariant())
        {
            case "PASS": passed++; break;
            case "FAIL": failed++; break;
            case "WARN": warnings++; break;
        }
    }

    private static DiagnosticItem CheckTwainDSM()
    {
        var item = new DiagnosticItem { Category = "TWAIN DSM", Name = "TWAIN DSM Library" };
        try
        {
            // Thử load TWAIN DSM thực sự bằng cách tạo TwainSession
            var appId = TWIdentity.CreateFromAssembly(DataGroups.Image, Assembly.GetExecutingAssembly());
            TwainSession? twain = null;
            try
            {
                twain = new TwainSession(appId);
                twain.Open();
                twain.Close();
                twain = null;

                item.Status = "PASS";
                item.Message = "TWAIN DSM loaded successfully";
                item.Details = "TwainSession created and opened without errors";
            }
            catch
            {
                // Nếu không mở được, thử kiểm tra file
                var dllPaths = new[]
                {
                    @"C:\Windows\SysWOW64\TWAIN_32.DLL",
                    @"C:\Windows\SysWOW64\TWAIN.DLL",
                    @"C:\Windows\System32\TWAIN_32.DLL",
                    @"C:\Windows\System32\TWAIN.DLL",
                    @"C:\Windows\System32\twain_32.dll",
                    @"C:\Windows\SysWOW64\twain_32.dll"
                };

                var foundPaths = dllPaths.Where(File.Exists).ToList();
                if (foundPaths.Count > 0)
                {
                    item.Status = "PASS";
                    item.Message = "TWAIN DSM DLL found";
                    item.Details = string.Join(", ", foundPaths.Select(Path.GetFileName));
                }
                else
                {
                    item.Status = "WARN";
                    item.Message = "TWAIN DSM file not in standard location";
                    item.Details = "May be loaded from driver folder";
                    item.Suggestion = "If scan works, this warning can be ignored";
                }
            }
            finally
            {
                if (twain != null)
                {
                    try { twain.Close(); } catch { }
                }
            }
        }
        catch (Exception ex)
        {
            item.Status = "FAIL";
            item.Message = "Error checking TWAIN DSM";
            item.Details = ex.Message;
        }
        return item;
    }

    private static DiagnosticItem CheckTwainRegistry()
    {
        var item = new DiagnosticItem { Category = "Registry", Name = "TWAIN Registry Keys" };
        try
        {
            // Thử kiểm tra bằng cách enumerate TWAIN sources (cách reliable nhất)
            var appId = TWIdentity.CreateFromAssembly(DataGroups.Image, Assembly.GetExecutingAssembly());
            TwainSession? twain = null;
            try
            {
                twain = new TwainSession(appId);
                twain.Open();
                var sourceCount = twain.Count();
                twain.Close();
                twain = null;

                if (sourceCount > 0)
                {
                    item.Status = "PASS";
                    item.Message = "TWAIN DSM and sources available";
                    item.Details = $"{sourceCount} source(s) found";
                }
                else
                {
                    item.Status = "PASS";
                    item.Message = "TWAIN DSM loaded (no sources without scanner)";
                    item.Details = "DSM available, connect scanner to enumerate sources";
                }
            }
            catch
            {
                // Fallback: check registry
                var registryPaths = new[]
                {
                    @"SOFTWARE\TWAIN",
                    @"SOFTWARE\WOW6432Node\TWAIN",
                    @"SOFTWARE\WOW6432Node\TWAIN\ThirdParty"
                };

                var foundKeys = new List<string>();
                using var baseKey = Microsoft.Win32.Registry.LocalMachine;

                foreach (var path in registryPaths)
                {
                    try
                    {
                        using var key = baseKey.OpenSubKey(path);
                        if (key != null)
                        {
                            foundKeys.Add(path);
                        }
                    }
                    catch { }
                }

                if (foundKeys.Count > 0)
                {
                    item.Status = "PASS";
                    item.Message = "TWAIN registry keys found";
                    item.Details = string.Join(", ", foundKeys);
                }
                else
                {
                    item.Status = "WARN";
                    item.Message = "TWAIN registry not in standard location";
                    item.Suggestion = "If scan works, this warning can be ignored";
                }
            }
            finally
            {
                if (twain != null)
                {
                    try { twain.Close(); } catch { }
                }
            }
        }
        catch (Exception ex)
        {
            item.Status = "FAIL";
            item.Message = "Error checking TWAIN DSM";
            item.Details = ex.Message;
        }
        return item;
    }

    private static DiagnosticItem CheckFujitsuDriver()
    {
        var item = new DiagnosticItem { Category = "Driver", Name = "FUJITSU USB Driver" };
        try
        {
            using var baseKey = Microsoft.Win32.Registry.LocalMachine;
            var fujiKey = baseKey.OpenSubKey(@"SOFTWARE\WOW6432Node\FUJITSU") ??
                          baseKey.OpenSubKey(@"SOFTWARE\FUJITSU");

            if (fujiKey != null)
            {
                item.Status = "PASS";
                item.Message = "FUJITSU driver registry found";
                item.Details = fujiKey.Name;
            }
            else
            {
                item.Status = "FAIL";
                item.Message = "FUJITSU driver not installed";
                item.Suggestion = "Install FUJITSU PaperStream IP (TWAIN) driver from https://www.pfu.ricoh.com/global/scanners/fi/dl/";
            }
        }
        catch (Exception ex)
        {
            item.Status = "WARN";
            item.Message = "Error checking FUJITSU driver";
            item.Details = ex.Message;
        }
        return item;
    }

    private static DiagnosticItem CheckFujitsuRegistry()
    {
        var item = new DiagnosticItem { Category = "Registry", Name = "FUJITSU TWAIN Source" };
        try
        {
            var keys = new[]
            {
                @"SOFTWARE\WOW6432Node\TWAIN\ThirdParty",
                @"SOFTWARE\TWAIN\ThirdParty",
                @"SOFTWARE\WOW6432Node\TWAIN",
                @"SOFTWARE\TWAIN"
            };

            bool found = false;
            var foundPaths = new List<string>();
            using var baseKey = Microsoft.Win32.Registry.LocalMachine;

            foreach (var keyPath in keys)
            {
                try
                {
                    using var key = baseKey.OpenSubKey(keyPath);
                    if (key != null)
                    {
                        var subKeys = key.GetSubKeyNames();
                        var fujiRelated = subKeys.Where(k => k.Contains("FUJITSU", StringComparison.OrdinalIgnoreCase) ||
                                                            k.Contains("fi-", StringComparison.OrdinalIgnoreCase) ||
                                                            k.Contains("PaperStream", StringComparison.OrdinalIgnoreCase)).ToList();
                        foreach (var fujiKey in fujiRelated)
                        {
                            foundPaths.Add($"{keyPath}\\{fujiKey}");
                        }
                        if (fujiRelated.Count > 0)
                        {
                            found = true;
                        }
                    }
                }
                catch { }
            }

            if (found)
            {
                item.Status = "PASS";
                item.Message = "FUJITSU TWAIN source registered";
                item.Details = string.Join(", ", foundPaths);
            }
            else
            {
                item.Status = "FAIL";
                item.Message = "FUJITSU TWAIN source not registered";
                item.Suggestion = "Reinstall PaperStream IP (TWAIN) driver completely. If using fi-7140, download driver from https://www.pfu.ricoh.com/global/scanners/fi/dl/";
            }
        }
        catch (Exception ex)
        {
            item.Status = "WARN";
            item.Message = "Error checking FUJITSU registry";
            item.Details = ex.Message;
        }
        return item;
    }

    private static DiagnosticItem CheckPaperStream()
    {
        var item = new DiagnosticItem { Category = "Software", Name = "PaperStream IP" };
        try
        {
            var programPaths = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
            };

            var paperStreamPaths = new List<string>();
            foreach (var basePath in programPaths)
            {
                try
                {
                    var dirs = Directory.GetDirectories(basePath, "*PaperStream*", SearchOption.TopDirectoryOnly);
                    paperStreamPaths.AddRange(dirs);
                }
                catch { }
            }

            if (paperStreamPaths.Count > 0)
            {
                item.Status = "PASS";
                item.Message = "PaperStream IP installed";
                item.Details = string.Join(", ", paperStreamPaths);
            }
            else
            {
                item.Status = "WARN";
                item.Message = "PaperStream IP not found";
                item.Suggestion = "Install PaperStream IP (TWAIN) driver for full scanner support";
            }
        }
        catch (Exception ex)
        {
            item.Status = "WARN";
            item.Message = "Error checking PaperStream";
            item.Details = ex.Message;
        }
        return item;
    }

    private static DiagnosticItem CheckTwainSources()
    {
        var item = new DiagnosticItem { Category = "TWAIN", Name = "TWAIN Source Enumeration" };
        TwainSession? twain = null;
        try
        {
            var appId = TWIdentity.CreateFromAssembly(DataGroups.Image, Assembly.GetExecutingAssembly());
            twain = new TwainSession(appId);
            twain.Open();

            var sources = new List<string>();
            foreach (var ds in twain)
            {
                sources.Add(ds.Name ?? "Unknown");
            }
            twain.Close();

            if (sources.Count > 0)
            {
                item.Status = "PASS";
                item.Message = $"Found {sources.Count} TWAIN source(s)";
                item.Details = string.Join(", ", sources);
            }
            else
            {
                item.Status = "FAIL";
                item.Message = "No TWAIN sources found";
                item.Suggestion = "1. Check USB connection to scanner\n2. Reinstall PaperStream IP (TWAIN) driver\n3. Run Windows Update to install TWAIN components";
            }
        }
        catch (Exception ex)
        {
            item.Status = "FAIL";
            item.Message = "TWAIN enumeration failed";
            item.Details = $"{ex.GetType().Name}: {ex.Message}";
            item.Suggestion = "Check TWAIN DSM and driver installation";
        }
        finally
        {
            if (twain != null)
            {
                try { twain.Close(); } catch { }
            }
        }
        return item;
    }

    private static DiagnosticItem CheckUSBConnection()
    {
        var item = new DiagnosticItem { Category = "Hardware", Name = "USB Scanner Connection" };
        try
        {
            using var baseKey = Microsoft.Win32.Registry.LocalMachine;
            using var usbKey = baseKey.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\USB");
            using var enumKey = baseKey.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\USB");

            if (usbKey == null)
            {
                item.Status = "WARN";
                item.Message = "USB registry not accessible";
                return item;
            }

            var usbDevices = new List<string>();
            if (enumKey != null)
            {
                foreach (var vidKeyName in enumKey.GetSubKeyNames())
                {
                    if (!vidKeyName.StartsWith("VID_", StringComparison.OrdinalIgnoreCase))
                        continue;
                    using var vidKey = enumKey.OpenSubKey(vidKeyName);
                    if (vidKey == null) continue;
                    foreach (var pidKeyName in vidKey.GetSubKeyNames())
                    {
                        using var pidKey = vidKey.OpenSubKey(pidKeyName);
                        var deviceDesc = pidKey?.GetValue("DeviceDesc")?.ToString();
                        if (!string.IsNullOrEmpty(deviceDesc))
                        {
                            usbDevices.Add($"{vidKeyName}/{pidKeyName}: {deviceDesc}");
                        }
                    }
                }
            }

            var scannerDevices = usbDevices.Where(d =>
                d.Contains("FUJITSU", StringComparison.OrdinalIgnoreCase) ||
                d.Contains("fi-", StringComparison.OrdinalIgnoreCase) ||
                d.Contains("scanner", StringComparison.OrdinalIgnoreCase)).ToList();

            if (scannerDevices.Count > 0)
            {
                item.Status = "PASS";
                item.Message = "Scanner USB device found";
                item.Details = string.Join("\n", scannerDevices);
            }
            else
            {
                item.Status = "WARN";
                item.Message = "No FUJITSU scanner in USB devices";
                item.Details = usbDevices.Count > 0 ? $"USB devices: {usbDevices.Count}" : "No USB devices found";
                item.Suggestion = "1. Check USB cable connection\n2. Try different USB port\n3. Restart scanner and PC";
            }
        }
        catch (Exception ex)
        {
            item.Status = "WARN";
            item.Message = "Error checking USB connection";
            item.Details = ex.Message;
        }
        return item;
    }

    public ScanSession? SelectScanner(string scannerName)
    {
        lock (_lockObject)
        {
            try
            {
                _selectedScannerName = scannerName;
                Log.Information("Selected scanner: {ScannerName} (MaxPages={MaxPages})",
                    scannerName, _maxPages);

                var session = new ScanSession(scannerName);
                session.Settings = new ScanSettings
                {
                    DPI = _defaultDpi,
                    ColorMode = _defaultColorMode,
                    PaperSize = "A4"
                };

                _currentSession = session;
                SaveSession(session);
                return session;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error selecting scanner: {ScannerName}", scannerName);
                return null;
            }
        }
    }

    private string? GetSelectedScannerFromSession(ScanSession session)
    {
        if (!string.IsNullOrEmpty(session.ScannerName))
        {
            return session.ScannerName;
        }
        return _selectedScannerName;
    }

    public ScanSession? ScanAsync(ScanSession session)
    {
        lock (_lockObject)
        {
            if (_isScanning)
            {
                Log.Warning("Scan already in progress");
                return null;
            }

            // Reset state trước scan mới
            ResetScannerState();
            _hasTransferError = false;
            _lastErrorMessage = null;

            if (string.IsNullOrEmpty(_selectedScannerName))
            {
                _selectedScannerName = GetSelectedScannerFromSession(session);
            }

            if (string.IsNullOrEmpty(_selectedScannerName))
            {
                Log.Warning("No scanner selected");
                session.Status = ScanStatus.Error;
                session.ErrorMessage = "Chưa chọn máy scan";
                return null;
            }

            _currentSession = session;
            // Nếu session đã có pages thì tiếp tục từ index cuối, không reset
            _currentPageIndex = session.Pages.Count;
            _isScanning = true;
            session.Status = ScanStatus.Scanning;

            _scanWaitHandle.Reset();

            Log.Information("Starting scan session: {SessionId}", session.SessionId);

            try
            {
                var appId = TWIdentity.CreateFromAssembly(DataGroups.Image, Assembly.GetExecutingAssembly());
                _twainSession = new TwainSession(appId);

                _twainSession.StateChanged += (s, e) =>
                {
                    Log.Debug("TWAIN state changed to: {State}", _twainSession.State);
                };

                _twainSession.DataTransferred += OnDataTransferred;
                _twainSession.SourceDisabled += OnSourceDisabled;
                _twainSession.TransferError += OnTransferError;
                _twainSession.TransferCanceled += OnTransferCanceled;

                _twainSession.SynchronizationContext = SynchronizationContext.Current;

                _twainSession.Open();

                DataSource? ds = null;
                foreach (var dataSource in _twainSession)
                {
                    if (dataSource.Name?.Contains(_selectedScannerName, StringComparison.OrdinalIgnoreCase) == true ||
                        _selectedScannerName.Contains(dataSource.Name ?? "", StringComparison.OrdinalIgnoreCase))
                    {
                        ds = dataSource;
                        break;
                    }
                }

                if (ds == null)
                {
                    Log.Warning("Scanner not found: {ScannerName}", _selectedScannerName);
                    session.Status = ScanStatus.Error;
                    session.ErrorMessage = $"Không tìm thấy máy scan: {_selectedScannerName}";
                    _twainSession.Close();
                    _isScanning = false;
                    return session;
                }

                ds.Open();

                ConfigureScanner(ds, session);

                ds.Enable(SourceEnableMode.NoUI, false, IntPtr.Zero);

                Log.Information("Scanner enabled (NoUI mode), blocking until scan completes...");

                // Chờ với timeout
                bool scanCompleted = _scanWaitHandle.WaitOne(_scanTimeoutMs);

                if (!scanCompleted)
                {
                    Log.Warning("Scan timeout after {Timeout}s", _scanTimeoutMs / 1000);
                    session.Status = ScanStatus.Error;
                    session.ErrorMessage = $"Hết thời gian chờ ({_scanTimeoutMs / 1000}s)";
                    session.CompletedAt = DateTime.Now;
                    
                    // Force close scanner
                    ForceCloseScanner();
                    _isScanning = false;
                    return session;
                }

                Log.Information("Scan completed. Total pages: {TotalPages}", _currentPageIndex);

                // Cleanup TWAIN
                if (_twainSession.CurrentSource?.IsOpen == true)
                {
                    _twainSession.CurrentSource.Close();
                }
                _twainSession.Close();
                _twainSession = null;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error starting scan");
                session.Status = ScanStatus.Error;
                session.ErrorMessage = $"Lỗi quét: {ex.Message}";
            }
            _isScanning = false;

            // Nếu không có trang nào được quét → coi như lỗi
            if (_currentSession?.Pages.Count == 0 && session.Status != ScanStatus.Error)
            {
                session.Status = ScanStatus.Error;
                session.ErrorMessage = string.IsNullOrEmpty(_lastErrorMessage)
                    ? "Không quét được trang nào. Có thể máy scan hết giấy hoặc bị ngắt kết nối."
                    : _lastErrorMessage;
                Log.Warning("No pages scanned, setting error: {Error}", session.ErrorMessage);
            }
            else if (session.Status != ScanStatus.Error)
            {
                session.Status = ScanStatus.Completed;
            }

            // Fallback: đảm bảo luôn có message khi lỗi
            if (session.Status == ScanStatus.Error && string.IsNullOrEmpty(session.ErrorMessage))
            {
                session.ErrorMessage = "Quét tài liệu thất bại.";
                Log.Warning("Scan error with empty message, setting default: {Error}", session.ErrorMessage);
            }

            return session;
        }
    }

    private void ResetScannerState()
    {
        try
        {
            if (_twainSession != null)
            {
                try
                {
                    if (_twainSession.CurrentSource?.IsOpen == true)
                    {
                        _twainSession.CurrentSource.Close();
                    }
                    _twainSession.Close();
                }
                catch { }
                _twainSession = null;
            }
        }
        catch { }
        _isScanning = false;
        _scanWaitHandle.Reset();
    }

    private void ForceCloseScanner()
    {
        try
        {
            Log.Warning("Force closing scanner...");
            if (_twainSession != null)
            {
                try
                {
                    if (_twainSession.CurrentSource?.IsOpen == true)
                    {
                        _twainSession.CurrentSource.Close();
                    }
                    _twainSession.Close();
                }
                catch (Exception ex)
                {
                    Log.Debug("Error during force close: {Error}", ex.Message);
                }
                _twainSession = null;
            }
        }
        catch { }
        _isScanning = false;
        _scanWaitHandle.Set();
    }

    private ManualResetEvent _scanWaitHandle = new(false);

    private void OnTransferError(object? sender, EventArgs e)
    {
        if (e is not TransferErrorEventArgs tea)
            return;

        _hasTransferError = true;
        if (tea.Exception != null)
            _lastErrorMessage = tea.Exception.Message;
        else if (tea.SourceStatus != null)
            _lastErrorMessage = $"TWAIN status: {tea.SourceStatus.ConditionCode}";
        else
            _lastErrorMessage = $"Transfer error (return code: {tea.ReturnCode})";

        Log.Warning("Transfer error: {Error}", _lastErrorMessage);
    }

    private void OnTransferCanceled(object? sender, EventArgs e)
    {
        _hasTransferError = true;
        if (string.IsNullOrEmpty(_lastErrorMessage))
            _lastErrorMessage = "Quét bị hủy hoặc ngắt.";
        Log.Information("Transfer canceled");
    }

    private void ConfigureScanner(DataSource ds, ScanSession session)
    {
        try
        {
            // --- Cấu hình chế độ màu ---
            string colorMode = session.Settings?.ColorMode ?? _defaultColorMode;
            var pixelType = colorMode.ToLowerInvariant() switch
            {
                "bw" or "grayscale" or "gray" or "đen trắng" => PixelType.Gray,
                _ => PixelType.RGB
            };
            try
            {
                ds.Capabilities.ICapPixelType.SetValue(pixelType);
                Log.Information("Set pixel type to: {PixelType}", pixelType);
            }
            catch (Exception ex)
            {
                Log.Debug("Pixel type not supported: {Message}", ex.Message);
            }

            // --- Cấu hình DPI ---
            int dpi = session.Settings?.DPI ?? _defaultDpi;
            try
            {
                ds.Capabilities.ICapXResolution.SetValue(dpi);
                ds.Capabilities.ICapYResolution.SetValue(dpi);
                Log.Information("Set DPI to: {DPI}", dpi);
            }
            catch (Exception ex)
            {
                Log.Debug("DPI not supported: {Message}", ex.Message);
            }

            // --- Cấu hình duplex ---
            bool useDuplex = session.Settings?.EnableDuplex ?? false;
            try
            {
                if (useDuplex == true)
                {
                    ds.Capabilities.CapDuplexEnabled.SetValue(BoolType.True);
                    Log.Information("Enabled duplex scanning");
                }
                else
                {
                    ds.Capabilities.CapDuplexEnabled.SetValue(BoolType.False);
                    Log.Information("Disabled duplex scanning");
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Duplex not supported: {Message}", ex.Message);
            }

            Log.Information("Scanner configured: DPI={DPI}, ColorMode={ColorMode}, Duplex={Duplex}",
                dpi, colorMode, useDuplex);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Some scanner capabilities not supported, using defaults");
        }
    }

    private void OnSourceDisabled(object? sender, EventArgs e)
    {
        Log.Information("Source disabled, scan complete");

        if (_currentSession != null)
        {
            // Kiểm tra trạng thái scan
            if (_hasTransferError || _currentSession.Pages.Count == 0)
            {
                _currentSession.Status = ScanStatus.Error;
                if (!string.IsNullOrEmpty(_lastErrorMessage))
                {
                    _currentSession.ErrorMessage = _lastErrorMessage;
                }
                else if (_currentSession.Pages.Count == 0)
                {
                    _currentSession.ErrorMessage = "Không quét được trang nào. Có thể máy scan hết giấy hoặc bị ngắt kết nối.";
                }

                if (string.IsNullOrEmpty(_currentSession.ErrorMessage))
                {
                    _currentSession.ErrorMessage = "Quét tài liệu thất bại.";
                }
                Log.Warning("Scan ended with error: {Error}", _currentSession.ErrorMessage);
            }
            else
            {
                _currentSession.Status = ScanStatus.Processing;
            }
            _currentSession.CompletedAt = DateTime.Now;
        }

        _scanWaitHandle.Set();
    }

    private void OnDataTransferred(object? sender, DataTransferredEventArgs e)
    {
        if (_currentSession == null || !_isScanning)
        {
            return;
        }

        if (_currentPageIndex >= _maxPages)
        {
            Log.Warning("Max pages reached ({MaxPages})", _maxPages);
            return;
        }

        try
        {
            var stream = e.GetNativeImageStream();
            if (stream == null)
            {
                Log.Warning("Could not get image stream from scanner");
                return;
            }

            using var image = Image.FromStream(stream);
            var pageFileName = $"page_{_currentSession.SessionId}_{_currentPageIndex:D4}.tif";
            var pagePath = Path.Combine(_tempFolder, pageFileName);

            var tempImage = new Bitmap(image);
            tempImage.Save(pagePath, ImageFormat.Tiff);
            tempImage.Dispose();

            var scanPage = new ScanPage(_currentPageIndex, pagePath, ScanSide.Front)
            {
                ScannedAt = DateTime.Now
            };

            _currentSession.Pages.Add(scanPage);
            _currentPageIndex++;

            Log.Information("Page saved: {PagePath}, Total pages: {TotalPages}",
                pagePath, _currentSession.Pages.Count);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error processing transferred data for page {Index}", _currentPageIndex);
        }
    }

    public ScanSession? StopScan()
    {
        lock (_lockObject)
        {
            try
            {
                if (_twainSession != null)
                {
                    if (_twainSession.CurrentSource?.IsOpen == true)
                    {
                        _twainSession.CurrentSource.Close();
                    }
                    _twainSession.Close();
                    _twainSession = null;
                }

                _isScanning = false;
                _scanWaitHandle.Set(); // Báo hiệu nếu đang chờ

                if (_currentSession != null)
                {
                    _currentSession.Status = ScanStatus.Processing;
                    _currentSession.CompletedAt = DateTime.Now;
                    Log.Information("Scan stopped. Total pages: {TotalPages}", _currentSession.Pages.Count);
                }

                return _currentSession;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error stopping scan");
                if (_currentSession != null)
                {
                    _currentSession.Status = ScanStatus.Error;
                    _currentSession.ErrorMessage = ex.Message;
                }
                return _currentSession;
            }
        }
    }

    public void SaveSession(ScanSession session)
    {
        _sessions[session.SessionId] = session;
        Log.Information("Session saved: {SessionId}", session.SessionId);
    }

    public ScanSession? GetSession(string sessionId)
    {
        return _sessions.TryGetValue(sessionId, out var session) ? session : null;
    }

    public ScanSession? GetCurrentSession()
    {
        return _currentSession;
    }

    public int GetCurrentPageCount()
    {
        return _currentPageIndex;
    }

    public bool IsScanning()
    {
        return _isScanning;
    }

    public string GetTempFolder()
    {
        return _tempFolder;
    }

    public void CleanupTempFiles(string sessionId)
    {
        try
        {
            var files = Directory.GetFiles(_tempFolder, $"*_{sessionId}_*.tif");
            foreach (var file in files)
            {
                File.Delete(file);
                Log.Debug("Deleted temp file: {File}", file);
            }
            Log.Information("Cleaned up {Count} temp files for session {SessionId}",
                files.Length, sessionId);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error cleaning up temp files for session {SessionId}", sessionId);
        }
    }

    public void Dispose()
    {
        StopScan();
        foreach (var session in _sessions.Values)
        {
            CleanupTempFiles(session.SessionId);
        }
        _sessions.Clear();
        Log.Information("ScannerService disposed");
    }
}
