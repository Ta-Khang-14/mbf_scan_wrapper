namespace mbf_scan_service.Models;

public class ApiResponse<T>
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public T? Data { get; set; }
    public string? ErrorCode { get; set; }
    public DateTime Timestamp { get; set; }

    public ApiResponse()
    {
        Timestamp = DateTime.Now;
    }

    public static ApiResponse<T> Ok(T data, string? message = null)
    {
        return new ApiResponse<T>
        {
            Success = true,
            Data = data,
            Message = message
        };
    }

    public static ApiResponse<T> Fail(string message, string? errorCode = null)
    {
        return new ApiResponse<T>
        {
            Success = false,
            Message = message,
            ErrorCode = errorCode
        };
    }
}

public class ApiResponse : ApiResponse<object>
{
    public static ApiResponse Ok(string? message = null)
    {
        return new ApiResponse
        {
            Success = true,
            Message = message
        };
    }

    public static new ApiResponse Fail(string message, string? errorCode = null)
    {
        return new ApiResponse
        {
            Success = false,
            Message = message,
            ErrorCode = errorCode
        };
    }
}

public class ScannerInfo
{
    public string Name { get; set; } = string.Empty;
    public string? ProductName { get; set; }
    public string? Manufacturer { get; set; }
    public bool IsAvailable { get; set; }
}

public class ScanStatusResponse
{
    public string SessionId { get; set; } = string.Empty;
    public ScanStatus Status { get; set; }
    public int TotalPages { get; set; }
    public int TotalFiles { get; set; }
    public string? ScannerName { get; set; }
    public TimeSpan? ElapsedTime { get; set; }
    public string? ErrorMessage { get; set; }
}

public class PageListResponse
{
    public long? Number { get; set; }
    public int TotalPages { get; set; }
    public int TotalFiles { get; set; }
    public List<PageInfo> Pages { get; set; } = new();
    public List<FileInfoResponse> Files { get; set; } = new();
}

public class FileInfoResponse
{
    public string FileName { get; set; } = string.Empty;
    public List<PageInfo> Pages { get; set; } = new();
}

public class PageInfo
{
    public int PageIndex { get; set; }
    public string ImagePath { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public string? PdfUrl { get; set; }
    public bool IsBarcodeSeparator { get; set; }
    public bool IsDocSeparator { get; set; }
    public string? BarcodeValue { get; set; }
    public ScanSide Side { get; set; }
    public DateTime ScannedAt { get; set; }
}

public class FileListResponse
{
    public int TotalFiles { get; set; }
    public List<ScanFileInfo> Files { get; set; } = new();
}

public class ScanFileInfo
{
    public string FileId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
    public int TotalPages { get; set; }
    public long FileSize { get; set; }
    public string? OCRResult { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class ProcessScanResponse
{
    public string SessionId { get; set; } = string.Empty;
    public long Number { get; set; }
    public ScanStatus Status { get; set; }
    public int TotalPages { get; set; }
    public List<ProcessDocumentResponse> Documents { get; set; } = new();
}

public class ProcessDocumentResponse
{
    public int DocIndex { get; set; }
    public List<ProcessFileResponse> Files { get; set; } = new();
}

public class ProcessFileResponse
{
    public int DocIndex { get; set; }
    public int FileIndex { get; set; }
    public string FileId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
    public int TotalPages { get; set; }
    public long FileSize { get; set; }
    public string? OCRResult { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class GetPagesRequest
{
    public long? Number { get; set; }
}

public class GetPagesResponse
{
    public int TotalPages { get; set; }
    public int TotalDocuments { get; set; }
    public int TotalFiles { get; set; }
    public List<DocumentGroupResponse> Documents { get; set; } = new();
}

public class DocumentGroupResponse
{
    public int DocIndex { get; set; }
    public List<FileGroupResponse> Files { get; set; } = new();
}

public class FileGroupResponse
{
    public int FileIndex { get; set; }
    public int PageCount { get; set; }
    public List<PageInfo> Pages { get; set; } = new();
}

public class ProcessRequest
{
    public long? Number { get; set; }
    public List<ProcessDocumentRequest> Documents { get; set; } = new();
    public List<ProcessFileRequest> Files { get; set; } = new();
}

public class ProcessDocumentRequest
{
    public int DocIndex { get; set; }
    public List<ProcessFileRequest> Files { get; set; } = new();
}

public class ProcessFileRequest
{
    public int DocIndex { get; set; }
    public int FileIndex { get; set; }
    public string FileName { get; set; } = string.Empty;
    public List<PageSelection> Pages { get; set; } = new();
}

public class PageSelection
{
    public int Index { get; set; }
    public bool IsOCR { get; set; } = false;
}
