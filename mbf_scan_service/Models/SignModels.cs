namespace mbf_scan_service.Models;

public class SignInfo
{
    public string? Phone { get; set; }
    public string? MessageToBeDisplayed { get; set; }
    public string? FilePath { get; set; }
    public string? FileBase64 { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string? UploadUrl { get; set; }
    public int SignType { get; set; }
    public string? FolderKey { get; set; }
    public List<SignatureInfo> ListSignatureInfo { get; set; } = new();
}

public class SignatureInfo
{
    public int Page { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public string SignType { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public int ImageWidth { get; set; }
    public int ImageHeight { get; set; }
    public string Base64Image { get; set; } = string.Empty;
}

public class SignTokenRequest
{
    public string FileBase64 { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string UploadUrl { get; set; } = string.Empty;
    public int SignType { get; set; }
    public string FolderKey { get; set; } = string.Empty;
    public List<SignatureInfo> ListSignatureInfo { get; set; } = new();
}

public class SignSimRequest
{
    public string Phone { get; set; } = string.Empty;
    public string MessageToBeDisplayed { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public int SignType { get; set; }
    public List<SignatureInfo> ListSignatureInfo { get; set; } = new();
    public string FileName { get; set; } = string.Empty;
    public string FolderKey { get; set; } = string.Empty;
}

public class SignApiResponse
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public SignData? Data { get; set; }
    public List<object>? Errors { get; set; }
    public string? FileName { get; set; }
    public string? FilePath { get; set; }
    public string? FileServer { get; set; }
    public string? Extension { get; set; }
    public string? FolderKey { get; set; }
    public string? Description { get; set; }
}

public class SignData
{
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string? FileServer { get; set; }
    public string? Extension { get; set; }
}

public class UploadFileResponse
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public List<UploadedFileData>? Data { get; set; }
    public List<object>? Errors { get; set; }
}

public class UploadedFileData
{
    public string FileName { get; set; } = string.Empty;
    public string FileDescription { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;
    public string AttachmentType { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string? OriginAttachmentId { get; set; }
    public int Version { get; set; }
    public string FolderKey { get; set; } = string.Empty;
}

public class FolderKeyResponse
{
    public bool Success { get; set; }
    public string? Data { get; set; }
}

public class SignResult
{
    public bool IsSuccess { get; set; }
    public string? SignedFilePath { get; set; }
    public string? FolderKey { get; set; }
    public string? ErrorMessage { get; set; }
}

public class ViewFileRequest
{
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string FolderKey { get; set; } = string.Empty;
}

public class ViewFileResponse
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public string? Data { get; set; }
}

public class DownloadResult
{
    public bool IsSuccess { get; set; }
    public string? LocalFilePath { get; set; }
    public string? DownloadUrl { get; set; }
    public string? ErrorMessage { get; set; }
}
