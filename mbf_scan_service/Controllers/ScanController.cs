using mbf_scan_service.Models;
using mbf_scan_service.Services;
using Serilog;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
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
    private static ImageService? _imageService;

    public static void Initialize(
        ScannerService scannerService,
        BarcodeService barcodeService,
        OCRService ocrService,
        PDFService pdfService,
        FileService fileService,
        ImageService imageService)
    {
        _scannerService = scannerService;
        _barcodeService = barcodeService;
        _ocrService = ocrService;
        _pdfService = pdfService;
        _fileService = fileService;
        _imageService = imageService;
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
                    ElapsedTime = _currentSession.ElapsedTime,
                    ErrorMessage = _currentSession.ErrorMessage
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
            return Results.Ok(ApiResponse<GetPagesResponse>.Ok(new GetPagesResponse()));
        }

        if (_scannerService != null)
        {
            session.Pages = _scannerService.GetCurrentSession()?.Pages ?? new List<ScanPage>();
        }

        if (_barcodeService != null && session.Pages.Count > 0)
        {
            _barcodeService.ProcessSessionPages(session.Pages);
        }

        var baseUrl = $"{context.Request.Scheme}://{context.Request.Host}";

        var documents = _barcodeService?.GroupPagesIntoDocuments(session.Pages, baseUrl)
            ?? new List<DocumentGroupResponse>();

        var totalFiles = documents.Sum(d => d.Files.Count);

        var response = new GetPagesResponse
        {
            TotalPages = session.Pages.Count,
            TotalDocuments = documents.Count,
            TotalFiles = totalFiles,
            Documents = documents
        };

        return Results.Ok(ApiResponse<GetPagesResponse>.Ok(response));
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

        var previewPath = _imageService?.GetPreviewPath(page.ImagePath);
        if (string.IsNullOrEmpty(previewPath) || !File.Exists(previewPath))
        {
            return Results.NotFound(ApiResponse.Fail("Failed to create preview", "PREVIEW_FAILED"));
        }

        return Results.File(previewPath, "image/jpeg");
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

        if (!string.IsNullOrEmpty(session.Pages[index > 0 ? index - 1 : 0].ImagePath))
        {
            _imageService?.InvalidateCache(session.Pages[index > 0 ? index - 1 : 0].ImagePath);
        }

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

            if (request == null)
            {
                return Results.BadRequest(ApiResponse.Fail("Request body is required", "INVALID_REQUEST"));
            }

            var hasDocuments = request.Documents != null && request.Documents.Count > 0;
            var hasFiles = request.Files != null && request.Files.Count > 0;

            if (!hasDocuments && !hasFiles)
            {
                return Results.BadRequest(ApiResponse.Fail("Documents or Files list is required", "INVALID_REQUEST"));
            }

            session.Status = ScanStatus.Processing;
            session.Files.Clear();

            var documentResponses = new List<ProcessDocumentResponse>();

            if (request.Documents != null && request.Documents.Count > 0)
            {
                foreach (var docRequest in request.Documents)
                {
                    var docResponse = new ProcessDocumentResponse { DocIndex = docRequest.DocIndex };

                    if (docRequest.Files == null) continue;

                    foreach (var fileRequest in docRequest.Files)
                    {
                        if (fileRequest.Pages == null || fileRequest.Pages.Count == 0)
                            continue;

                        if (string.IsNullOrWhiteSpace(fileRequest.FileName))
                            continue;

                        var filePages = fileRequest.Pages
                            .Where(p => p.Index >= 0 && p.Index < session.Pages.Count)
                            .Select(p => session.Pages[p.Index])
                            .ToList();

                        var ocrPages = fileRequest.Pages
                            .Where(p => p.IsOCR && p.Index >= 0 && p.Index < session.Pages.Count)
                            .Select(p => session.Pages[p.Index])
                            .ToList();

                        if (filePages.Count == 0)
                            continue;

                        var fileResult = CreateScanFile(
                            session, filePages, ocrPages, fileRequest.FileName,
                            context.Request.Scheme, context.Request.Host,
                            fileRequest.DocIndex, fileRequest.FileIndex);
                        if (fileResult != null)
                        {
                            docResponse.Files.Add(fileResult);
                        }
                    }

                    if (docResponse.Files.Count > 0)
                    {
                        documentResponses.Add(docResponse);
                    }
                }
            }
            else if (request.Files != null && request.Files.Count > 0)
            {
                var docResponse = new ProcessDocumentResponse { DocIndex = 0 };

                foreach (var fileRequest in request.Files)
                {
                    if (fileRequest.Pages == null || fileRequest.Pages.Count == 0)
                        continue;

                    if (string.IsNullOrWhiteSpace(fileRequest.FileName))
                        continue;

                    var filePages = fileRequest.Pages
                        .Where(p => p.Index >= 0 && p.Index < session.Pages.Count)
                        .Select(p => session.Pages[p.Index])
                        .ToList();

                    var ocrPages = fileRequest.Pages
                        .Where(p => p.IsOCR && p.Index >= 0 && p.Index < session.Pages.Count)
                        .Select(p => session.Pages[p.Index])
                        .ToList();

                    if (filePages.Count == 0)
                        continue;

                    var fileResult = CreateScanFile(
                        session, filePages, ocrPages, fileRequest.FileName,
                        context.Request.Scheme, context.Request.Host,
                        fileRequest.DocIndex, fileRequest.FileIndex);
                    if (fileResult != null)
                    {
                        docResponse.Files.Add(fileResult);
                    }
                }

                if (docResponse.Files.Count > 0)
                {
                    documentResponses.Add(docResponse);
                }
            }

            session.Status = ScanStatus.Completed;
            session.CompletedAt = DateTime.Now;

            var response = new ProcessScanResponse
            {
                SessionId = session.SessionId,
                Number = request.Number ?? 0,
                Status = session.Status,
                TotalPages = session.TotalPages,
                Documents = documentResponses
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

    private static ProcessFileResponse? CreateScanFile(
        ScanSession session,
        List<ScanPage> filePages,
        List<ScanPage> ocrPages,
        string fileName,
        string scheme,
        HostString host,
        int docIndex,
        int fileIndex)
    {
        var fileId = Guid.NewGuid().ToString("N")[..8];
        var pdfPath = _pdfService?.CreatePdfFromPages(filePages, fileId);

        if (!string.IsNullOrEmpty(pdfPath) && _fileService != null)
        {
            var metadata = _fileService.SavePdf(pdfPath, fileName);

            if (metadata.IsSuccess)
            {
                string? ocrText = null;
                if (_ocrService != null && ocrPages.Count > 0)
                {
                    ocrText = _ocrService.PerformOCR(ocrPages.First().ImagePath);
                }

                var scanFile = new ScanFile(filePages)
                {
                    FileId = metadata.Id ?? fileId,
                    FileName = metadata.FileName ?? fileName,
                    PDFPath = pdfPath,
                    DownloadUrl = $"{scheme}://{host}/api/files/{metadata.Id ?? fileId}",
                    OCRResult = ocrText,
                    FileSize = metadata.FileSize,
                    CreatedAt = DateTime.Now
                };

                session.Files.Add(scanFile);

                return new ProcessFileResponse
                {
                    DocIndex = docIndex,
                    FileIndex = fileIndex,
                    FileId = scanFile.FileId,
                    FileName = scanFile.FileName,
                    DownloadUrl = scanFile.DownloadUrl,
                    TotalPages = scanFile.Pages.Count,
                    FileSize = scanFile.FileSize,
                    OCRResult = scanFile.OCRResult,
                    CreatedAt = scanFile.CreatedAt
                };
            }
        }
        return null;
    }
}

public class ScanRequest
{
    public string? ScannerName { get; set; }
    public ScanSettings? Settings { get; set; }
}
