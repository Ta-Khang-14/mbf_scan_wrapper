using mbf_scan_service.Models;
using Serilog;
using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace mbf_scan_service.Services;

public class SignService
{
    private readonly SignConfig _config;
    private readonly HttpClient _httpClient;
    private readonly ConcurrentDictionary<string, CachedFolderKey> _folderKeyCache = new();
    private readonly ILogger _logger = Log.ForContext<SignService>();

    private class CachedFolderKey
    {
        public string Value { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
        public bool IsValid => DateTime.Now < ExpiresAt;
    }

    private record ApiCredentials(string? WebToken, string? RoleId, string? UserId);

    private HttpRequestMessage CreateRequest(HttpMethod method, string url, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, url) { Content = content };
        return request;
    }

    private void AddAuthHeaders(HttpRequestMessage request, ApiCredentials cred)
    {
        // Strip "Bearer " prefix if already present to avoid duplicate
        var token = cred.WebToken ?? "";
        if (token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            token = token.Substring(7);

        if (!string.IsNullOrEmpty(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (!string.IsNullOrEmpty(cred.RoleId))
            request.Headers.Add("roleid", cred.RoleId);
        if (!string.IsNullOrEmpty(cred.UserId))
            request.Headers.Add("userid", cred.UserId);
    }

    private async Task<(HttpResponseMessage Response, string Body)> SendAsync(HttpRequestMessage request)
    {
        var response = await _httpClient.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        return (response, body);
    }

    private async Task<(bool Success, T? Data, string? Error)> ParseResponseAsync<T>(HttpResponseMessage response, string body) where T : class
    {
        if (!response.IsSuccessStatusCode)
        {
            _logger.Warning("API returned non-success: {StatusCode}, Body: {Body}", response.StatusCode, body);
            return (false, null, $"HTTP {response.StatusCode}");
        }

        var result = JsonSerializer.Deserialize<T>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (result == null)
        {
            _logger.Warning("Failed to deserialize response: {Body}", body);
            return (false, null, "Deserialization failed");
        }

        return (true, result, null);
    }

    public SignService(SignConfig config, HttpClient httpClient)
    {
        _config = config;
        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromMinutes(5);
    }

    public async Task<SignResult> SignAsync(SignInfo signInfo, string? webToken, string? roleId, string? userId)
    {
        try
        {
            if (signInfo.SignType == 0)
            {
                return await SignWithTokenAsync(signInfo, webToken, roleId, userId);
            }
            else if (signInfo.SignType == 1)
            {
                return await SignWithSimAsync(signInfo, webToken, roleId, userId);
            }
            else
            {
                return new SignResult
                {
                    IsSuccess = false,
                    ErrorMessage = $"Invalid SignType: {signInfo.SignType}. Must be 0 (Token) or 1 (SIM)."
                };
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error during sign process");
            return new SignResult
            {
                IsSuccess = false,
                ErrorMessage = ex.Message
            };
        }
    }

    private async Task<SignResult> SignWithTokenAsync(SignInfo signInfo, string? webToken, string? roleId, string? userId)
    {
        _logger.Information("Starting token signing process for file: {FileName}", signInfo.FileName);

        // Ưu tiên FileBase64 từ client, nếu không có thì đọc từ FilePath
        string fileBase64 = signInfo.FileBase64 ?? string.Empty;
        if (string.IsNullOrEmpty(fileBase64) && !string.IsNullOrEmpty(signInfo.FilePath) && File.Exists(signInfo.FilePath))
        {
            fileBase64 = Convert.ToBase64String(await File.ReadAllBytesAsync(signInfo.FilePath));
        }

        if (string.IsNullOrEmpty(fileBase64))
        {
            return new SignResult
            {
                IsSuccess = false,
                ErrorMessage = "FileBase64 or valid FilePath is required for token signing"
            };
        }

        // Ưu tiên folderKey từ client, nếu không có thì lấy tự động từ server
        var folderKey = signInfo.FolderKey;
        if (string.IsNullOrEmpty(folderKey))
        {
            folderKey = await GetActiveFolderKeyAsync(webToken, roleId, userId);
        }

        if (string.IsNullOrEmpty(folderKey))
        {
            return new SignResult
            {
                IsSuccess = false,
                ErrorMessage = "Failed to get folder key"
            };
        }

        var request = new SignTokenRequest
        {
            FileBase64 = fileBase64,
            FileName = signInfo.FileName,
            UploadUrl = _config.UrlUploadPath,
            SignType = signInfo.SignType,
            FolderKey = folderKey,
            ListSignatureInfo = signInfo.ListSignatureInfo
        };

        var url = $"{_config.UrlSignTokenPdf.TrimEnd('/')}/sign-pdf";
        var httpContent = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");

        _logger.Information("Calling token sign API: {Url}", url);

        var httpRequest = CreateRequest(HttpMethod.Post, url, httpContent);
        var (response, body) = await SendAsync(httpRequest);

        if (!response.IsSuccessStatusCode)
        {
            _logger.Warning("Token sign API returned non-success: {StatusCode}, Body: {Body}", response.StatusCode, body);
            return new SignResult { IsSuccess = false, ErrorMessage = $"Token sign API error: {response.StatusCode} - {body}" };
        }

        var (success, signResponse, errorMsg) = await ParseResponseAsync<SignApiResponse>(response, body);

        if (!success || signResponse == null)
            return new SignResult { IsSuccess = false, ErrorMessage = signResponse?.Message ?? errorMsg ?? "Unknown error" };

        var filePath = signResponse.FilePath ?? signResponse.FileServer ?? string.Empty;
        if (string.IsNullOrEmpty(filePath))
            return new SignResult { IsSuccess = false, ErrorMessage = signResponse.Message ?? "No file path returned" };

        var downloadUrl = BuildDownloadUrl(filePath);
        _logger.Information("Token signing successful. DownloadUrl: {DownloadUrl}", downloadUrl);

        return new SignResult
        {
            IsSuccess = true,
            SignedFilePath = filePath,
            FolderKey = folderKey
        };
    }

    private async Task<SignResult> SignWithSimAsync(SignInfo signInfo, string? webToken, string? roleId, string? userId)
    {
        var cred = new ApiCredentials(webToken, roleId, userId);
        _logger.Information("Starting SIM signing process for file: {FileName}", signInfo.FileName);

        if (string.IsNullOrEmpty(webToken))
            return new SignResult { IsSuccess = false, ErrorMessage = "WebToken is required for SIM signing" };

        if (string.IsNullOrEmpty(signInfo.Phone))
            return new SignResult { IsSuccess = false, ErrorMessage = "Phone is required for SIM signing" };

        if (string.IsNullOrEmpty(signInfo.FilePath) || !File.Exists(signInfo.FilePath))
            return new SignResult { IsSuccess = false, ErrorMessage = $"File not found: {signInfo.FilePath}" };

        // Upload file trước
        var uploadedFilePath = await UploadFileForSimSignAsync(signInfo.FilePath, signInfo.FileName, cred);
        if (string.IsNullOrEmpty(uploadedFilePath))
            return new SignResult { IsSuccess = false, ErrorMessage = "Failed to upload file for SIM signing" };

        // Gọi API ký SIM
        var url = $"{_config.UrlApi.TrimEnd('/')}/Apis/SignPDF/SignSIM";
        var request = new SignSimRequest
        {
            Phone = signInfo.Phone,
            MessageToBeDisplayed = signInfo.MessageToBeDisplayed,
            FilePath = uploadedFilePath,
            SignType = signInfo.SignType,
            ListSignatureInfo = signInfo.ListSignatureInfo,
            FileName = signInfo.FileName,
            FolderKey = uploadedFilePath
        };

        var httpContent = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");
        var httpRequest = CreateRequest(HttpMethod.Post, url, httpContent);
        AddAuthHeaders(httpRequest, cred);

        _logger.Information("Calling SIM sign API: {Url}", url);
        var (response, body) = await SendAsync(httpRequest);

        if (!response.IsSuccessStatusCode)
            return new SignResult { IsSuccess = false, ErrorMessage = $"SIM sign API error: {response.StatusCode} - {body}" };

        var (success, signResponse, _) = await ParseResponseAsync<SignApiResponse>(response, body);

        // Map root-level fields to Data if Data is null
        if (signResponse?.Data == null && signResponse != null)
        {
            signResponse.Data = new SignData
            {
                FileName = signResponse.FileName ?? string.Empty,
                FilePath = signResponse.FilePath ?? signResponse.FileServer ?? string.Empty,
                FileServer = signResponse.FileServer,
                Extension = signResponse.Extension
            };
        }

        if (signResponse?.Data == null || string.IsNullOrEmpty(signResponse.Data.FilePath))
            return new SignResult { IsSuccess = false, ErrorMessage = signResponse?.Message ?? "Unknown error from SIM sign API" };

        var downloadUrl = BuildDownloadUrl(signResponse.Data.FilePath);
        _logger.Information("SIM signing successful. DownloadUrl: {DownloadUrl}", downloadUrl);

        return new SignResult
        {
            IsSuccess = true,
            SignedFilePath = signResponse.Data.FilePath,
            FolderKey = signInfo.FolderKey
        };
    }

    private async Task<string?> UploadFileForSimSignAsync(string filePath, string fileName, ApiCredentials cred)
    {
        var url = $"{_config.UrlApi.TrimEnd('/')}/apiservice/submissionversion/uploadFile";

        using var content = new MultipartFormDataContent();
        using var fileStream = File.OpenRead(filePath);
        using var streamContent = new StreamContent(fileStream);

        streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(streamContent, "files", fileName);

        var request = CreateRequest(HttpMethod.Post, url, content);
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.AcceptLanguage.ParseAdd("vi");
        request.Headers.Add("origin", "https://qlvb.mbfs.dev");
        request.Headers.Add("referer", "https://qlvb.mbfs.dev/");
        request.Headers.Add("priority", "u=1, i");
        request.Headers.Add("sec-fetch-mode", "cors");
        request.Headers.Add("sec-fetch-dest", "empty");
        request.Headers.Add("sec-fetch-site", "cross-site");
        AddAuthHeaders(request, cred);

        _logger.Information("Uploading file for SIM sign: {FileName} to {Url}", fileName, url);
        var (response, body) = await SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            _logger.Warning("Upload API returned non-success: {StatusCode}, Body: {Body}", response.StatusCode, body);
            return null;
        }

        var (success, uploadResponse, _) = await ParseResponseAsync<UploadFileResponse>(response, body);
        if (!success || uploadResponse?.Data == null || uploadResponse.Data.Count == 0)
        {
            _logger.Warning("Upload API returned invalid response: {Body}", body);
            return null;
        }

        var uploadedFile = uploadResponse.Data[0];
        _logger.Information("File uploaded successfully. FilePath: {FilePath}, FolderKey: {FolderKey}", uploadedFile.FilePath, uploadedFile.FolderKey);

        return uploadedFile.FilePath;
    }

    private async Task<string?> GetActiveFolderKeyAsync(string? webToken, string? roleId, string? userId)
    {
        if (_folderKeyCache.TryGetValue("active", out var cached) && cached.IsValid)
            return cached.Value;

        var cred = new ApiCredentials(webToken, roleId, userId);
        var url = $"{_config.UrlApi.TrimEnd('/')}/ApiService/ShareApi/GetActiveFolderKey";
        _logger.Information("Fetching active folder key from: {Url}", url);

        var request = CreateRequest(HttpMethod.Get, url);
        AddAuthHeaders(request, cred);

        var (response, body) = await SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            _logger.Warning("GetActiveFolderKey API returned: {StatusCode}, Body: {Body}", response.StatusCode, body);
            return null;
        }

        var (success, folderKeyResponse, _) = await ParseResponseAsync<FolderKeyResponse>(response, body);
        if (!success || string.IsNullOrEmpty(folderKeyResponse?.Data))
        {
            _logger.Warning("GetActiveFolderKey returned invalid response: {Body}", body);
            return null;
        }

        var folderKey = folderKeyResponse.Data;
        _folderKeyCache["active"] = new CachedFolderKey { Value = folderKey, ExpiresAt = DateTime.Now.AddHours(1) };
        _logger.Information("FolderKey cached: {FolderKey}", folderKey);

        return folderKey;
    }

    private string BuildDownloadUrl(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
            return string.Empty;

        var apiUrl = _config.UrlApi.TrimEnd('/');
        return $"{apiUrl}{filePath}";
    }

    public void ClearFolderKeyCache()
    {
        _folderKeyCache.Clear();
        _logger.Information("FolderKey cache cleared");
    }

    public async Task<DownloadResult> DownloadSignedFileAsync(string signedFilePath, string fileName, string folderKey, string? webToken, string? roleId, string? userId)
    {
        try
        {
            var cred = new ApiCredentials(webToken, roleId, userId);
            var url = _config.UrlViewFile.TrimEnd('/');

            var request = new ViewFileRequest
            {
                FilePath = signedFilePath,
                FileName = fileName,
                FolderKey = folderKey
            };

            var httpContent = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");
            var httpRequest = CreateRequest(HttpMethod.Post, url, httpContent);
            AddAuthHeaders(httpRequest, cred);
            httpRequest.Headers.Add("origin", "https://qlvb.mbfs.dev");
            httpRequest.Headers.Add("referer", "https://qlvb.mbfs.dev/");
            httpRequest.Headers.Add("accept", "application/json");
            httpRequest.Headers.Add("accept-language", "vi");

            _logger.Information("Calling ViewFile API to download signed file: {Url}, FilePath: {FilePath}", url, signedFilePath);

            var response = await _httpClient.SendAsync(httpRequest);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                _logger.Warning("ViewFile API returned non-success: {StatusCode}, Body: {Body}", response.StatusCode, body);
                return new DownloadResult { IsSuccess = false, ErrorMessage = $"ViewFile API error: {response.StatusCode}" };
            }

            var contentType = response.Content.Headers.ContentType?.MediaType;
            _logger.Information("ViewFile response ContentType: {ContentType}", contentType);

            var signedFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, _config.SignedFolder);
            if (!Directory.Exists(signedFolder))
            {
                Directory.CreateDirectory(signedFolder);
            }

            var extension = Path.GetExtension(fileName);
            if (string.IsNullOrEmpty(extension))
            {
                extension = ".pdf";
            }
            var uniqueFileName = $"{Path.GetFileNameWithoutExtension(fileName)}_{DateTime.Now:yyyyMMddHHmmss}{extension}";
            var localPath = Path.Combine(signedFolder, uniqueFileName);

            await using var fs = new FileStream(localPath, FileMode.Create, FileAccess.Write);
            await response.Content.CopyToAsync(fs);

            _logger.Information("Signed file downloaded successfully: {LocalPath}", localPath);

            return new DownloadResult
            {
                IsSuccess = true,
                LocalFilePath = localPath
            };
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error downloading signed file: {FilePath}", signedFilePath);
            return new DownloadResult { IsSuccess = false, ErrorMessage = ex.Message };
        }
    }
}
