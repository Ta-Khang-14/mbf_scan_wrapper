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
    private static CleanupService? _cleanupService;
    private static SignService? _signService;
    private static DocumentExtractor? _documentExtractor;

    // Demo services
    private static DemoScanService? _demoScanService;
    private static DemoSignService? _demoSignService;
    private const bool DEMO_MODE = false; // Demo mode enabled

    public static void Initialize(
        ScannerService scannerService,
        BarcodeService barcodeService,
        OCRService ocrService,
        PDFService pdfService,
        FileService fileService,
        ImageService imageService,
        CleanupService cleanupService,
        SignService signService)
    {
        _scannerService = scannerService;
        _barcodeService = barcodeService;
        _ocrService = ocrService;
        _pdfService = pdfService;
        _fileService = fileService;
        _imageService = imageService;
        _cleanupService = cleanupService;
        _signService = signService;
        _documentExtractor = new DocumentExtractor();

        // Initialize demo services
        _demoScanService = new DemoScanService();
        _demoSignService = new DemoSignService();

        Log.Information("ScanController initialized (Demo Mode: {DemoMode})", DEMO_MODE);
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

    public static IResult RunDiagnostic()
    {
        try
        {
            var result = ScannerService.RunDiagnostic();
            return Results.Ok(ApiResponse<DiagnosticResult>.Ok(result, result.Summary));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error running diagnostic");
            return Results.BadRequest(ApiResponse.Fail(ex.Message, "DIAGNOSTIC_FAILED"));
        }
    }

    public static IResult Scan(ScanRequest? request)
    {
        Log.Information("API: Scan called with scanner: {Scanner}, sessionId: {SessionId} [Demo Mode: {DemoMode}]",
            request?.ScannerName, request?.SessionId, DEMO_MODE);

        if (string.IsNullOrWhiteSpace(request?.ScannerName))
        {
            return Results.BadRequest(ApiResponse.Fail("Scanner name is required", "INVALID_REQUEST"));
        }

        try
        {
            ScanSession session;
            lock (_lockObject)
            {
                if (!string.IsNullOrWhiteSpace(request?.SessionId))
                {
                    if (_currentSession == null || request.SessionId != _currentSession.SessionId)
                    {
                        return Results.BadRequest(ApiResponse.Fail("Phiên scan không đúng", "INVALID_SESSION"));
                    }
                    session = _currentSession;
                    Log.Information("Continuing scan on existing session: {SessionId}", session.SessionId);
                }
                else
                {
                    session = new ScanSession(request.ScannerName)
                    {
                        Settings = request?.Settings ?? new ScanSettings()
                    };
                    _currentSession = session;
                    _sessions[session.SessionId] = session;
                    Log.Information("Created new session: {SessionId}", session.SessionId);
                }
            }

            if (DEMO_MODE)
            {
                // Demo Mode: Copy files from DemoSource folder
                if (_demoScanService == null)
                {
                    return Results.BadRequest(ApiResponse.Fail("Demo scan service not initialized", "SERVICE_NOT_READY"));
                }
                _demoScanService.SaveSession(session);
                session = _demoScanService.ScanDemo(session);
            }
            else
            {
                // Real Mode: Use actual scanner
                if (_scannerService == null)
                {
                    return Results.BadRequest(ApiResponse.Fail("Scanner service not initialized", "SERVICE_NOT_READY"));
                }
                _scannerService.SelectScanner(request?.ScannerName!);
                session = _scannerService.ScanAsync(session) ?? session;
            }

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

        if (!DEMO_MODE && _scannerService != null)
        {
            session.Pages = _scannerService.GetCurrentSession()?.Pages ?? session.Pages;
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

        var pageToDelete = session.Pages[index];
        session.Pages.RemoveAt(index);

        if (session.Pages.Count > 0 && !string.IsNullOrEmpty(pageToDelete.ImagePath))
        {
            _imageService?.InvalidateCache(pageToDelete.ImagePath);
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
            var webToken = context.Request.Headers["X-Web-Token"].FirstOrDefault();
            var roleId = context.Request.Headers["X-Role-Id"].FirstOrDefault();
            var userId = context.Request.Headers["X-User-Id"].FirstOrDefault();

            Log.Information("ProcessScan headers - WebToken: {HasToken}, RoleId: {RoleId}, UserId: {UserId}",
                !string.IsNullOrEmpty(webToken), roleId, userId);

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
                            fileRequest.DocIndex, fileRequest.FileIndex,
                            fileRequest.SignInfo, webToken, roleId, userId);
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
                        fileRequest.DocIndex, fileRequest.FileIndex,
                        fileRequest.SignInfo, webToken, roleId, userId);
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
        int fileIndex,
        SignInfo? signInfo = null,
        string? webToken = null,
        string? roleId = null,
        string? userId = null)
    {
        var fileId = Guid.NewGuid().ToString("N")[..8];
        var pdfPath = _pdfService?.CreatePdfFromPages(filePages, fileId);

        if (!string.IsNullOrEmpty(pdfPath) && _fileService != null)
        {
            var metadata = _fileService.SavePdf(pdfPath, fileName);

                if (metadata.IsSuccess)
                {
                    string? ocrText = null;
                    DocumentMetadata? ocrExtract = null;
                    if (_ocrService != null && ocrPages.Count > 0)
                    {
                        var ocrTexts = ocrPages
                            .Select(p => _ocrService.PerformOCR(p.ImagePath))
                            .Where(t => !string.IsNullOrWhiteSpace(t))
                            .ToList();
                        ocrText = string.Join("\n\n--- Page Break ---\n\n", ocrTexts);
                        if (!string.IsNullOrWhiteSpace(ocrText) && _documentExtractor != null)
                        {
                            ocrExtract = _documentExtractor.Extract(ocrText);
                        }
                    }

                    var scanFile = new ScanFile(filePages)
                    {
                        FileId = metadata.Id ?? fileId,
                        FileName = metadata.FileName ?? fileName,
                        PDFPath = pdfPath,
                        DownloadUrl = $"{scheme}://{host}/api/files/{metadata.Id ?? fileId}",
                        OCRResult = ocrText,
                        OCRExtract = ocrExtract,
                        FileSize = metadata.FileSize,
                        CreatedAt = DateTime.Now
                    };

                session.Files.Add(scanFile);

                var fileResponse = new ProcessFileResponse
                {
                    DocIndex = docIndex,
                    FileIndex = fileIndex,
                    FileId = scanFile.FileId,
                    FileName = scanFile.FileName,
                    DownloadUrl = scanFile.DownloadUrl,
                    TotalPages = scanFile.Pages.Count,
                    FileSize = scanFile.FileSize,
                    OCRResult = scanFile.OCRResult,
                    OCRExtract = scanFile.OCRExtract,
                    CreatedAt = scanFile.CreatedAt
                };

                if (signInfo != null)
                {
                    if (DEMO_MODE)
                    {
                        // Demo Mode: Simulate sign response, then download real file from server
                        Log.Information("DemoSign: Simulating sign for file: {FileName}", fileName);

                        signInfo.FilePath = pdfPath;
                        signInfo.FileName = scanFile.FileName;

                        var demoSignResponse = _demoSignService?.SimulateSignResponse(scanFile.FileName);
                        if (demoSignResponse != null && demoSignResponse.Success && _signService != null)
                        {
                            // Download real signed file from server using mock filePath and folderKey
                            var downloadResult = _signService.DownloadSignedFileAsync(
                                demoSignResponse.FilePath ?? string.Empty,
                                scanFile.FileName,
                                demoSignResponse.FolderKey ?? string.Empty,
                                webToken, roleId, userId
                            ).GetAwaiter().GetResult();

                            if (downloadResult.IsSuccess && !string.IsNullOrEmpty(downloadResult.LocalFilePath))
                            {
                                var signedMetadata = _fileService?.SaveSignedFile(downloadResult.LocalFilePath, scanFile.FileName);
                                if (signedMetadata != null && signedMetadata.IsSuccess)
                                {
                                    scanFile.FileId = signedMetadata.Id ?? scanFile.FileId;
                                    scanFile.FileName = signedMetadata.FileName ?? scanFile.FileName;
                                    scanFile.PDFPath = signedMetadata.FilePath;
                                    scanFile.DownloadUrl = $"{scheme}://{host}/api/files/{signedMetadata.Id}";
                                    scanFile.FileSize = signedMetadata.FileSize;

                                    fileResponse.FileId = scanFile.FileId;
                                    fileResponse.FileName = scanFile.FileName;
                                    fileResponse.DownloadUrl = scanFile.DownloadUrl;
                                    fileResponse.FileSize = scanFile.FileSize;

                                    Log.Information("DemoSign: Signed file saved and ready for download: {DownloadUrl}", scanFile.DownloadUrl);
                                }
                                else
                                {
                                    Log.Warning("DemoSign: Failed to save signed file: {Error}", signedMetadata?.Error);
                                }
                            }
                            else
                            {
                                Log.Warning("DemoSign: Failed to download signed file: {Error}", downloadResult.ErrorMessage);
                            }

                            fileResponse.SignInfo = new ProcessFileSignInfo
                            {
                                Success = true,
                                Message = demoSignResponse.Message,
                                FileName = demoSignResponse.FileName,
                                FilePath = demoSignResponse.FilePath,
                                FileServer = demoSignResponse.FileServer,
                                FolderKey = demoSignResponse.FolderKey,
                                Description = demoSignResponse.Description
                            };
                        }
                    }
                    else if (_signService != null)
                    {
                        // Real Mode: Use actual sign service
                        Log.Information("Starting sign process for file: {FileName}, SignType: {SignType}", fileName, signInfo.SignType);

                        signInfo.FilePath = pdfPath;
                        signInfo.FileName = scanFile.FileName;

                        if (signInfo.SignType == 0 && string.IsNullOrEmpty(signInfo.FileBase64))
                        {
                            try
                            {
                                var fileBytes = File.ReadAllBytes(pdfPath);
                                signInfo.FileBase64 = Convert.ToBase64String(fileBytes);
                            }
                            catch (Exception ex)
                            {
                                Log.Warning(ex, "Failed to read file for Base64 encoding");
                            }
                        }

                        var signResult = _signService.SignAsync(signInfo, webToken, roleId, userId).GetAwaiter().GetResult();

                        if (signResult.IsSuccess && !string.IsNullOrEmpty(signResult.SignedFilePath))
                        {
                            Log.Information("Sign successful for file: {FileName}. Downloading signed file...", fileName);

                            var downloadResult = _signService.DownloadSignedFileAsync(
                                signResult.SignedFilePath,
                                scanFile.FileName,
                                signResult.FolderKey ?? signInfo.FolderKey ?? string.Empty,
                                webToken, roleId, userId
                            ).GetAwaiter().GetResult();

                            if (downloadResult.IsSuccess && !string.IsNullOrEmpty(downloadResult.LocalFilePath))
                            {
                                var signedMetadata = _fileService?.SaveSignedFile(downloadResult.LocalFilePath, scanFile.FileName);
                                if (signedMetadata != null && signedMetadata.IsSuccess)
                                {
                                    scanFile.FileId = signedMetadata.Id ?? scanFile.FileId;
                                    scanFile.FileName = signedMetadata.FileName ?? scanFile.FileName;
                                    scanFile.PDFPath = signedMetadata.FilePath;
                                    scanFile.DownloadUrl = $"{scheme}://{host}/api/files/{signedMetadata.Id}";
                                    scanFile.FileSize = signedMetadata.FileSize;

                                    fileResponse.FileId = scanFile.FileId;
                                    fileResponse.FileName = scanFile.FileName;
                                    fileResponse.DownloadUrl = scanFile.DownloadUrl;
                                    fileResponse.FileSize = scanFile.FileSize;

                                    Log.Information("Signed file saved and ready for download: {DownloadUrl}", scanFile.DownloadUrl);
                                }
                                else
                                {
                                    Log.Warning("Failed to save signed file: {Error}", signedMetadata?.Error);
                                }
                            }
                            else
                            {
                                Log.Warning("Failed to download signed file: {Error}", downloadResult.ErrorMessage);
                            }
                        }
                        else
                        {
                            Log.Warning("Sign failed for file: {FileName}, Error: {Error}", fileName, signResult.ErrorMessage);
                        }
                    }
                }

                return fileResponse;
            }
        }
        return null;
    }

    public static IResult ExtractOCR(HttpContext context)
    {
        Log.Information("API: ExtractOCR called");

        ExtractOCRRequest? request = null;
        if (context.Request.ContentLength > 0)
        {
            using var reader = new StreamReader(context.Request.Body);
            var body = reader.ReadToEndAsync().GetAwaiter().GetResult();
            request = JsonSerializer.Deserialize<ExtractOCRRequest>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }

        if (request == null)
        {
            return Results.BadRequest(ApiResponse.Fail("Request body is required", "INVALID_REQUEST"));
        }

        string? ocrText = request.OCRText;

        if (string.IsNullOrWhiteSpace(ocrText))
        {
            if (!string.IsNullOrWhiteSpace(request.FileId) && _fileService != null)
            {
                var fileMetadata = _fileService.GetFileById(request.FileId);
                if (fileMetadata == null || string.IsNullOrEmpty(fileMetadata.FilePath))
                {
                    return Results.NotFound(ApiResponse.Fail($"File not found: {request.FileId}", "FILE_NOT_FOUND"));
                }

                if (_ocrService == null)
                {
                    return Results.BadRequest(ApiResponse.Fail("OCR service not available", "SERVICE_NOT_READY"));
                }

                ocrText = _ocrService.PerformOCR(fileMetadata.FilePath);
                if (string.IsNullOrWhiteSpace(ocrText))
                {
                    return Results.Ok(ApiResponse<ExtractOCRResponse>.Ok(
                        new ExtractOCRResponse { FileId = request.FileId, Warning = "No text found in document" },
                        "OCR produced no text"));
                }
            }
            else
            {
                return Results.BadRequest(ApiResponse.Fail("Either OCRText or FileId is required", "INVALID_REQUEST"));
            }
        }

        if (_documentExtractor == null)
        {
            return Results.BadRequest(ApiResponse.Fail("Document extractor not initialized", "SERVICE_NOT_READY"));
        }

        var metadata = _documentExtractor.Extract(ocrText);

        Log.Information("ExtractOCR completed - DocType: {DocType}, Notation: {Notation}, PublishUnit: {PublishUnit}",
            metadata.DocType, metadata.Notation, metadata.PublishUnit);

        return Results.Ok(ApiResponse<ExtractOCRResponse>.Ok(
            new ExtractOCRResponse
            {
                FileId = request.FileId,
                Metadata = metadata,
                Warning = metadata.IsNonStandard ? "Document is non-standard format" : null
            },
            "OCR extraction completed"));
    }

    public static IResult TriggerCleanup()
    {
        Log.Information("API: TriggerCleanup called");

        if (_cleanupService == null)
        {
            return Results.BadRequest(ApiResponse.Fail("Cleanup service not initialized", "SERVICE_NOT_READY"));
        }

        try
        {
            var result = _cleanupService.CleanupAll();
            return Results.Ok(ApiResponse<CleanupResult>.Ok(result, "Cleanup completed"));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error during manual cleanup");
            return Results.BadRequest(ApiResponse.Fail(ex.Message, "CLEANUP_FAILED"));
        }
    }

    public static IResult GetCleanupStats()
    {
        if (_cleanupService == null)
        {
            return Results.Ok(ApiResponse.Fail("Cleanup service not initialized", "SERVICE_NOT_READY"));
        }

        var (tempCount, outputCount) = _cleanupService.GetFileCounts();
        var response = new
        {
            TempFiles = tempCount,
            OutputFiles = outputCount,
            Total = tempCount + outputCount
        };

        return Results.Ok(ApiResponse.Ok(response, "Stats retrieved"));
    }
}

public class ScanRequest
{
    public string? SessionId { get; set; }
    public string? ScannerName { get; set; }
    public ScanSettings? Settings { get; set; }
}
