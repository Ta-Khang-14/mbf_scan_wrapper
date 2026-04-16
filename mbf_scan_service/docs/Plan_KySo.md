# Plan: Tích Hợp Ký Số Cho Process API

**Ngày tạo:** 2026-04-15
**Status:** Completed

---

## Mục Lục

1. [Tổng Quan](#tổng-quan)
2. [Thay Đổi API](#thay-đổi-api)
3. [Chi Tiết Các Bước Thực Hiện](#chi-tiết-các-bước-thực-hiện)
4. [File Changes](#file-changes)

---

## Tổng Quan

Tích hợp ký số vào quy trình scan, cho phép ký token hoặc ký SIM trên file PDF sau khi tạo.

### Luồng Ký Số

```
┌──────────────────────────────────────────────────────────────────────────────┐
│                         LUỒNG KÝ SỐ TÍCH HỢP                               │
└──────────────────────────────────────────────────────────────────────────────┘

  1. FE gọi POST /api/scanner/process với SignInfo trong body
  2. Backend tạo PDF bình thường
  3. Nếu có SignInfo → Gọi SignService để ký số
  4. SignService xử lý ký token hoặc ký SIM
  5. Backend cập nhật downloadUrl = filePath đã ký + URL_API
  6. Trả response về cho FE
```

### Hai Phương Thức Ký

| Phương thức | SignType | Mô tả |
|-------------|----------|--------|
| Ký Token | 0 | Hash file → gọi API ký → upload lên server |
| Ký SIM | 1 | Upload file trước → gọi API ký SIM |

---

## Thay Đổi API

### Headers Bổ Sung

| Header | Required | Mô tả |
|--------|----------|--------|
| `X-Web-Token` | Có | Web token cho authentication |
| `X-Role-Id` | Có | Role ID của user |
| `X-User-Id` | Có | User ID của user |

### Request Body - ProcessScan

Thêm `SignInfo` vào mỗi `ProcessFileRequest`:

```json
{
  "documents": [...],
  "files": [
    {
      "docIndex": 0,
      "fileIndex": 0,
      "fileName": "12345678.pdf",
      "pages": [...],
      "signInfo": {
        "phone": "0912345678",
        "messageToBeDisplayed": "",
        "fileName": "van-ban.pdf",
        "signType": 0,
        "listSignatureInfo": [
          {
            "page": 1,
            "x": 100.00,
            "y": 50.00,
            "signType": "IMAGE",
            "text": "",
            "imageWidth": 135,
            "imageHeight": 75,
            "base64Image": "iVBORw0KGgoAAAANSUhEUg..."
          }
        ]
      }
    }
  ]
}
```

### Response - ProcessFileResponse

Thêm field:

| Field | Type | Mô tả |
|-------|------|--------|
| `signedFileUrl` | string? | URL file đã ký (nếu có ký số) |
| `signedFilePath` | string? | Server path của file đã ký |
| `signSuccess` | bool | Trạng thái ký số |

---

## Chi Tiết Các Bước Thực Hiện

### Bước 1: Thêm Models cho Ký Số

**File:** `Models/SignModels.cs` (tạo mới)

- [x] `SignInfo` - thông tin ký số chính
- [x] `SignatureInfo` - vị trí ký trên page
- [x] `SignTokenRequest` - request body cho ký token
- [x] `SignSimRequest` - request body cho ký SIM
- [x] `SignApiResponse` - response từ API ký số
- [x] `UploadFileResponse` - response từ API upload file

### Bước 2: Thêm SignService

**File:** `Services/SignService.cs` (tạo mới)

- [x] Constructor nhận SignConfig và HttpClient
- [x] `GetActiveFolderKeyAsync()` - lấy và cache FolderKey
- [x] `SignWithTokenAsync(SignInfo)` - ký token (SignType = 0)
  - Hash file thành Base64
  - Gọi `POST {URL_SIGN_TOKEN_PDF}/sign-pdf`
  - Tạo downloadUrl từ response
- [x] `SignWithSimAsync(SignInfo, webToken, roleId, userId)` - ký SIM (SignType = 1)
  - Upload file qua multipart/form-data
  - Lấy folderKey từ response upload
  - Gọi `POST {URL_API}/Apis/SignPDF/SignSIM`
  - Tạo downloadUrl từ response
- [x] `SignAsync(SignInfo, webToken, roleId, userId)` - dispatch theo SignType
- [x] MemoryCache cho FolderKey với expiry check

### Bước 3: Cập Nhật AppSettings

**File:** `Models/AppSettings.cs`

- [x] Thêm class `SignConfig`
- [x] Thêm property `Sign` vào `AppSettings`

```csharp
public class SignConfig
{
    public string UrlApi { get; set; } = "";
    public string UrlSignTokenPdf { get; set; } = "";
    public string? FolderKey { get; set; }
}
```

### Bước 4: Cập Nhật ProcessRequest Model

**File:** `Models/ApiResponse.cs`

- [x] Thêm `SignInfo? SignInfo` vào `ProcessFileRequest`
- [x] Thêm `SignedFileUrl`, `SignedFilePath`, `SignSuccess` vào `ProcessFileResponse`

### Bước 5: Cập Nhật ScanController

**File:** `Controllers/ScanController.cs`

- [x] Thêm static field `_signService`
- [x] Cập nhật `Initialize()` để nhận SignService
- [x] Cập nhật `ProcessScan()`:
  - Đọc headers: `X-Web-Token`, `X-Role-Id`, `X-User-Id`
  - Sau khi tạo PDF, kiểm tra SignInfo
  - Gọi SignService nếu có SignInfo
  - Cập nhật response với thông tin file đã ký

### Bước 6: Khởi Tạo SignService

**File:** `Program.cs`

- [x] Thêm SignConfig vào appsettings loading
- [x] Đăng ký SignService vào DI
- [x] Inject vào ScanController.Initialize()

### Bước 7: Cập Nhật appsettings.json

**File:** `appsettings.json`

```json
{
  "Sign": {
    "UrlApi": "https://api.example.com",
    "UrlSignTokenPdf": "https://sign.example.com"
  }
}
```

### Bước 8: Cập Nhật Documentation

**File:** `docs/API.md`

- [x] Cập nhật headers cho ProcessScan
- [x] Thêm SignInfo vào request body
- [x] Cập nhật response model
- [x] Thêm section mô tả luồng ký số

**File:** `docs/Flow.md`

- [x] Thêm luồng ký số vào sơ đồ
- [x] Mô tả chi tiết SignInfo structure

---

## File Changes

| Action | File | Mô tả |
|--------|------|--------|
| Tạo mới | `Models/SignModels.cs` | Models cho ký số |
| Tạo mới | `Services/SignService.cs` | Service xử lý ký số |
| Sửa | `Models/AppSettings.cs` | Thêm SignConfig |
| Sửa | `Models/ApiResponse.cs` | Thêm SignInfo vào request/response |
| Sửa | `Controllers/ScanController.cs` | Tích hợp SignService |
| Sửa | `Program.cs` | Khởi tạo SignService |
| Sửa | `appsettings.json` | Thêm Sign config |
| Sửa | `docs/API.md` | Cập nhật tài liệu |
| Sửa | `docs/Flow.md` | Cập nhật flow |

---

## Checklist Hoàn Thành

- [x] Bước 1: Models
- [x] Bước 2: SignService
- [x] Bước 3: AppSettings
- [x] Bước 4: ProcessRequest Model
- [x] Bước 5: ScanController
- [x] Bước 6: Program.cs
- [x] Bước 7: appsettings.json
- [x] Bước 8: Documentation
- [ ] Test local
- [ ] Code review
