using mbf_scan_service.Models;
using mbf_scan_service.Services;
using Serilog;
using Microsoft.AspNetCore.Http;
using System.Text.Json;

namespace mbf_scan_service.Controllers;

public static class ScanController
{
    private static ScanSession? _currentSession;
    private static readonly Dictionary<string, ScanSession> _sessions = new();
    private static readonly object _lockObject = new();

    private static ScannerService? _scannerService;
    private static BarcodeService? _barcodeService;
    private static OCRService? _ocrService;
    private static PDFService? _pdfService;
    private static FileService? _fileService;

    public static void Initialize(
        ScannerService scannerService,
        BarcodeService barcodeService,
        OCRService ocrService,
        PDFService pdfService,
        FileService fileService)
    {
        _scannerService = scannerService;
        _barcodeService = barcodeService;
        _ocrService = ocrService;
        _pdfService = pdfService;
        _fileService = fileService;
        Log.Information("ScanController initialized with services");
    }

    private static ScanSession? GetSessionFromRequest(HttpRequest request)
    {
        var sessionId = request.Headers["X-Session-Id"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return _currentSession;
        }
        _sessions.TryGetValue(sessionId, out var session);
        return session ?? _currentSession;
    }

    private static IResult SessionNotFound(string sessionId) =>
        Results.BadRequest(ApiResponse.Fail($"Session not found: {sessionId}", "SESSION_NOT_FOUND"));

    public static IResult ListScanners()
    {
        Log.Information("API: ListScanners called");

        try
        {
            var scanners = ScannerService.GetAvailableScanners();
            var scannerInfos = scanners.Select(s => new ScannerInfo
            {
                Name = s,
                ProductName = s,
                IsAvailable = true
            }).ToList();

            if (scannerInfos.Count == 0)
            {
                scannerInfos.Add(new ScannerInfo
                {
                    Name = "No scanner found",
                    ProductName = "No scanner found",
                    IsAvailable = false
                });
            }

            return Results.Ok(ApiResponse<List<ScannerInfo>>.Ok(scannerInfos, "Scanner list retrieved"));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error listing scanners");
            return Results.Ok(ApiResponse<List<ScannerInfo>>.Ok(
                new List<ScannerInfo>(), "Using mock scanners"));
        }
    }

    public static IResult Scan(ScanRequest? request)
    {
        Log.Information("API: Scan called with scanner: {Scanner}", request?.ScannerName);

        if (string.IsNullOrWhiteSpace(request?.ScannerName))
        {
            return Results.BadRequest(ApiResponse.Fail("Scanner name is required", "INVALID_REQUEST"));
        }

        try
        {
            if (_scannerService == null)
            {
                return Results.BadRequest(ApiResponse.Fail("Scanner service not initialized", "SERVICE_NOT_READY"));
            }

            ScanSession session;
            lock (_lockObject)
            {
                session = new ScanSession(request.ScannerName)
                {
                    Settings = request?.Settings ?? new ScanSettings()
                };
                _currentSession = session;
                _sessions[session.SessionId] = session;
                _scannerService.SaveSession(session);
            }

            _scannerService.SelectScanner(request?.ScannerName!);

            var resultSession = _scannerService.ScanAsync(session);

            return Results.Ok(ApiResponse<ScanStatusResponse>.Ok(
                new ScanStatusResponse
                {
                    SessionId = _currentSession.SessionId,
                    Status = _currentSession.Status,
                    TotalPages = _currentSession.TotalPages,
                    TotalFiles = _currentSession.TotalFiles,
                    ScannerName = _currentSession.ScannerName,
                    ElapsedTime = _currentSession.ElapsedTime
                },
                "Scan completed"));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to scan");
            return Results.BadRequest(ApiResponse.Fail(ex.Message, "SCAN_FAILED"));
        }
    }

    public static IResult GetPages(HttpContext context)
    {
        var session = GetSessionFromRequest(context.Request);
        if (session == null)
        {
            return Results.Ok(ApiResponse<PageListResponse>.Ok(
                new PageListResponse { TotalPages = 0, Pages = new List<PageInfo>() }));
        }

        if (_scannerService != null)
        {
            session.Pages = _scannerService.GetCurrentSession()?.Pages ?? new List<ScanPage>();
        }

        var baseUrl = $"{context.Request.Scheme}://{context.Request.Host}";

        var pages = session.Pages.Select((p, idx) => new PageInfo
        {
            PageIndex = p.PageIndex,
            ImagePath = p.ImagePath,
            ImageUrl = $"{baseUrl}/api/scanner/preview/{p.PageIndex}",
            PdfUrl = $"{baseUrl}/api/scanner/page-pdf/{p.PageIndex}",
            IsBarcodeSeparator = p.IsBarcodeSeparator,
            BarcodeValue = p.BarcodeValue,
            Side = p.Side,
            ScannedAt = p.ScannedAt
        }).ToList();

        return Results.Ok(ApiResponse<PageListResponse>.Ok(
            new PageListResponse { TotalPages = pages.Count, Pages = pages }));
    }

    public static IResult GetPagePreview(int index, HttpContext context)
    {
        var session = GetSessionFromRequest(context.Request);
        if (session == null)
        {
            return Results.NotFound(ApiResponse.Fail("Session not found", "SESSION_NOT_FOUND"));
        }

        if (index < 0 || index >= session.Pages.Count)
        {
            return Results.NotFound(ApiResponse.Fail($"Page index {index} not found", "PAGE_NOT_FOUND"));
        }

        var page = session.Pages[index];
        if (string.IsNullOrEmpty(page.ImagePath) || !File.Exists(page.ImagePath))
        {
            return Results.NotFound(ApiResponse.Fail("Image file not found", "FILE_NOT_FOUND"));
        }

        var extension = Path.GetExtension(page.ImagePath).ToLowerInvariant();
        var contentType = extension switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".tif" or ".tiff" => "image/tiff",
            ".bmp" => "image/bmp",
            _ => "application/octet-stream"
        };

        return Results.File(page.ImagePath, contentType);
    }

    public static IResult GetPagePdf(int index, HttpContext context)
    {
        var session = GetSessionFromRequest(context.Request);
        if (session == null)
        {
            return Results.NotFound(ApiResponse.Fail("Session not found", "SESSION_NOT_FOUND"));
        }

        if (index < 0 || index >= session.Pages.Count)
        {
            return Results.NotFound(ApiResponse.Fail($"Page index {index} not found", "PAGE_NOT_FOUND"));
        }

        var page = session.Pages[index];
        if (string.IsNullOrEmpty(page.ImagePath) || !File.Exists(page.ImagePath))
        {
            return Results.NotFound(ApiResponse.Fail("Image file not found", "FILE_NOT_FOUND"));
        }

        if (_pdfService == null)
        {
            return Results.BadRequest(ApiResponse.Fail("PDF service not available", "SERVICE_NOT_READY"));
        }

        var tempFileId = $"preview_{DateTime.Now:yyyyMMddHHmmss}_{index}";
        var pdfPath = _pdfService.ConvertTiffToPdf(page.ImagePath, $"{tempFileId}.pdf");

        if (string.IsNullOrEmpty(pdfPath))
        {
            return Results.BadRequest(ApiResponse.Fail("Failed to convert image to PDF", "CONVERSION_FAILED"));
        }

        return Results.File(pdfPath, "application/pdf", $"page_{index + 1}.pdf", enableRangeProcessing: true);
    }

    public static IResult DeletePage(int index, HttpContext context)
    {
        Log.Information("API: DeletePage called for index: {Index}", index);

        var session = GetSessionFromRequest(context.Request);
        if (session == null)
        {
            return SessionNotFound(context.Request.Headers["X-Session-Id"].FirstOrDefault() ?? "");
        }

        if (index < 0 || index >= session.Pages.Count)
        {
            return Results.BadRequest(ApiResponse.Fail($"Invalid page index: {index}", "INVALID_INDEX"));
        }

        session.Pages.RemoveAt(index);

        for (int i = 0; i < session.Pages.Count; i++)
        {
            session.Pages[i].PageIndex = i;
        }

        Log.Information("Page {Index} deleted. Remaining pages: {Count}", index, session.Pages.Count);

        var baseUrl = $"{context.Request.Scheme}://{context.Request.Host}";

        var pages = session.Pages.Select(p => new PageInfo
        {
            PageIndex = p.PageIndex,
            ImagePath = p.ImagePath,
            ImageUrl = $"{baseUrl}/api/scanner/preview/{p.PageIndex}",
            PdfUrl = $"{baseUrl}/api/scanner/page-pdf/{p.PageIndex}",
            IsBarcodeSeparator = p.IsBarcodeSeparator,
            BarcodeValue = p.BarcodeValue,
            Side = p.Side,
            ScannedAt = p.ScannedAt
        }).ToList();

        return Results.Ok(ApiResponse<PageListResponse>.Ok(
            new PageListResponse { TotalPages = pages.Count, Pages = pages },
            $"Page {index} deleted"));
    }

    public static IResult ProcessScan(HttpContext context)
    {
        Log.Information("API: ProcessScan called");

        var session = GetSessionFromRequest(context.Request);
        if (session == null || session.Pages.Count == 0)
        {
            return Results.BadRequest(ApiResponse.Fail("No pages to process", "NO_PAGES"));
        }

        try
        {
            ProcessRequest? request = null;
            if (context.Request.ContentLength > 0)
            {
                using var reader = new StreamReader(context.Request.Body);
                var body = reader.ReadToEndAsync().GetAwaiter().GetResult();
                request = JsonSerializer.Deserialize<ProcessRequest>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }

            var number = request?.Number ?? 0;
            var timestamp = DateTime.Now.ToString("ddMMyyyy_HHmmss");

            session.Status = ScanStatus.Processing;

            if (_barcodeService != null)
            {
                _barcodeService.ProcessSessionPages(session.Pages);

                var fileGroups = _barcodeService.GroupPagesIntoFiles(session.Pages);
                var groupIndex = 0;

                foreach (var group in fileGroups)
                {
                    if (group.Count == 0) continue;

                    var fileId = Guid.NewGuid().ToString("N")[..8];
                    var pdfPath = _pdfService?.CreatePdfFromPages(group, fileId);

                    if (!string.IsNullOrEmpty(pdfPath) && _fileService != null)
                    {
                        var fileName = groupIndex == 0
                            ? $"{number}_{timestamp}.pdf"
                            : $"{number}_{timestamp}_{groupIndex}.pdf";

                        var metadata = _fileService.SavePdf(pdfPath, fileName);

                        if (metadata.IsSuccess)
                        {
                            string? ocrText = null;
                            if (_ocrService != null)
                            {
                                var firstPage = group.First();
                                ocrText = _ocrService.PerformOCR(firstPage.ImagePath);
                            }

                            var scanFile = new ScanFile(group)
                            {
                                FileId = metadata.Id ?? fileId,
                                FileName = metadata.FileName ?? fileName,
                                PDFPath = pdfPath,
                                DownloadUrl = $"{context.Request.Scheme}://{context.Request.Host}/api/files/{metadata.Id ?? fileId}",
                                OCRResult = ocrText,
                                FileSize = metadata.FileSize,
                                CreatedAt = DateTime.Now
                            };

                            session.Files.Add(scanFile);
                        }
                    }

                    groupIndex++;
                }
            }

            session.Status = ScanStatus.Completed;
            session.CompletedAt = DateTime.Now;

            var response = new ProcessScanResponse
            {
                SessionId = session.SessionId,
                Number = number,
                Status = session.Status,
                TotalPages = session.TotalPages,
                Files = session.Files.Select(f => new ScanFileInfo
                {
                    FileId = f.FileId,
                    FileName = f.FileName,
                    DownloadUrl = $"{context.Request.Scheme}://{context.Request.Host}/api/files/{f.FileId}",
                    TotalPages = f.Pages.Count,
                    FileSize = f.FileSize,
                    OCRResult = f.OCRResult,
                    CreatedAt = f.CreatedAt
                }).ToList()
            };

            return Results.Ok(ApiResponse<ProcessScanResponse>.Ok(response, "Processing completed"));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to process scan");
            return Results.BadRequest(ApiResponse.Fail(ex.Message, "PROCESS_FAILED"));
        }
    }

    public static IResult GetFile(string id)
    {
        Log.Information("API: GetFile called for id: {FileId}", id);

        if (_fileService == null)
        {
            return Results.NotFound(ApiResponse.Fail("File service not initialized", "SERVICE_NOT_READY"));
        }

        var metadata = _fileService.GetFileById(id);
        if (metadata == null)
        {
            return Results.NotFound(ApiResponse.Fail($"File not found: {id}", "FILE_NOT_FOUND"));
        }

        return Results.File(metadata.FilePath!, "application/pdf", metadata.FileName);
    }

    public static IResult DownloadFile(string id)
    {
        Log.Information("API: DownloadFile called for id: {FileId}", id);

        if (_fileService == null)
        {
            return Results.NotFound(ApiResponse.Fail("File service not initialized", "SERVICE_NOT_READY"));
        }

        var metadata = _fileService.GetFileById(id);
        if (metadata == null)
        {
            return Results.NotFound(ApiResponse.Fail($"File not found: {id}", "FILE_NOT_FOUND"));
        }

        if (string.IsNullOrEmpty(metadata.FilePath) || !File.Exists(metadata.FilePath))
        {
            return Results.NotFound(ApiResponse.Fail("Physical file not found", "FILE_MISSING"));
        }

        return Results.File(metadata.FilePath, "application/pdf", metadata.FileName, enableRangeProcessing: true);
    }

    public static IResult GetAllStoredFiles(HttpContext context)
    {
        if (_fileService == null)
        {
            return Results.Ok(ApiResponse<List<FileMetadata>>.Ok(new List<FileMetadata>()));
        }

        var files = _fileService.GetAllFiles();
        var baseUrl = $"{context.Request.Scheme}://{context.Request.Host}";

        var filesWithFullUrl = files.Select(f =>
        {
            f.DownloadUrl = $"{baseUrl}/api/files/{f.Id}";
            return f;
        }).ToList();

        return Results.Ok(ApiResponse<List<FileMetadata>>.Ok(filesWithFullUrl, "File list retrieved"));
    }

    public static IResult DeleteStoredFile(string id)
    {
        Log.Information("API: DeleteStoredFile called for id: {FileId}", id);

        if (_fileService == null)
        {
            return Results.BadRequest(ApiResponse.Fail("File service not initialized", "SERVICE_NOT_READY"));
        }

        if (!_fileService.FileExists(id))
        {
            return Results.NotFound(ApiResponse.Fail($"File not found: {id}", "FILE_NOT_FOUND"));
        }

        _fileService.DeleteFile(id);
        return Results.Ok(ApiResponse.Ok($"File {id} deleted"));
    }
}

public class ProcessRequest
{
    public long Number { get; set; }
}

public class ScanRequest
{
    public string? ScannerName { get; set; }
    public ScanSettings? Settings { get; set; }
}
