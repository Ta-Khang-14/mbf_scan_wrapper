# Đặc Tả API - MBF Scan Service

**Base URL:** `http://localhost:5000`

**Response Format:** Tất cả API đều trả về format:
```json
{
  "success": true,
  "message": "Mô tả kết quả",
  "data": { ... },
  "errorCode": null,
  "timestamp": "2026-04-09T10:30:00"
}
```

---

## Mục Lục

1. [System](#1-system)
2. [Scanner](#2-scanner)
3. [Files](#3-files)

---

## 1. System

### Health Check

Kiểm tra trạng thái service.

| | |
|---|---|
| **Method** | `GET` |
| **URL** | `/health` |
| **Auth** | Không |

**Response:**
```json
{
  "status": "Healthy",
  "timestamp": "2026-04-09T10:30:00"
}
```

---

## 2. Scanner

### 2.1 List Scanners - Liệt Kê Máy Scan

Lấy danh sách máy scan khả dụng.

| | |
|---|---|
| **Method** | `GET` |
| **URL** | `/api/scanner/list` |
| **Auth** | Không |

**Response:**
```json
{
  "success": true,
  "message": "Scanner list retrieved",
  "data": [
    {
      "name": "FUJITSU fi-760",
      "productName": "FUJITSU fi-760",
      "manufacturer": null,
      "isAvailable": true
    }
  ]
}
```

---

### 2.2 Scan - Quét Tài Liệu

Khởi tạo session và bắt đầu quét (blocking - đợi user scan xong).

| | |
|---|---|
| **Method** | `POST` |
| **URL** | `/api/scanner/scan` |
| **Auth** | Không |
| **Content-Type** | `application/json` |

**Request Body:**
```json
{
  "sessionId": "abc123def456",
  "scannerName": "FUJITSU fi-760",
  "settings": {
    "dpi": 300,
    "colorMode": "Color",
    "paperSize": "A4"
  }
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `sessionId` | string | ❌ | Session ID từ lần scan trước. Nếu rỗng sẽ tạo session mới. Nếu có giá trị phải trùng với session hiện tại, nếu khác sẽ báo lỗi "Phiên scan không đúng" |
| `scannerName` | string | ✅ | Tên máy scan (từ list scanners) |
| `settings.dpi` | number | ❌ | DPI quét (mặc định: 300) |
| `settings.colorMode` | string | ❌ | Chế độ màu: `Color`, `BW`, `Gray` (mặc định: `Color`) |
| `settings.paperSize` | string | ❌ | Khổ giấy: `A4`, `A3`, `Letter` (mặc định: `A4`) |


**Response:**
```json
{
  "success": true,
  "message": "Scan completed",
  "data": {
    "sessionId": "abc123def456",
    "status": "Scanning",
    "totalPages": 5,
    "totalFiles": 0,
    "scannerName": "FUJITSU fi-760",
    "elapsedTime": "00:01:30"
  }
}
```

**FE cần thực hiện:**
- Nếu `sessionId` rỗng: lần scan đầu tiên, server sẽ tạo session mới và trả về `sessionId`
- Nếu `sessionId` có giá trị: FE gửi lại `sessionId` để tiếp tục scan thêm pages vào session hiện tại (index pages sẽ tiếp tục tăng)
- Lưu `sessionId` để dùng cho các API tiếp theo

---

### 2.3 Detect Files - Detect Barcode + Group Pages

Detect barcode trên các pages đã quét, nhóm pages thành documents và files, trả về danh sách documents/files với thông tin chi tiết từng page.

| | |
|---|---|
| **Method** | `GET` |
| **URL** | `/api/scanner/pages` |
| **Auth** | Không |

**Headers:**
| Header | Required | Description |
|--------|----------|-------------|
| `X-Session-Id` | ❌ | Session ID. Nếu không gửi, dùng session cuối cùng |

**Response:**
```json
{
  "success": true,
  "message": null,
  "data": {
    "totalPages": 8,
    "totalDocuments": 2,
    "totalFiles": 3,
    "documents": [
      {
        "docIndex": 0,
        "files": [
          {
            "fileIndex": 0,
            "pageCount": 2,
            "pages": [
              {
                "pageIndex": 0,
                "imagePath": "temp/scan_001_001.tif",
                "imageUrl": "http://localhost:5000/api/scanner/preview/0",
                "pdfUrl": "http://localhost:5000/api/scanner/page-pdf/0",
                "isBarcodeSeparator": false,
                "isDocSeparator": false,
                "barcodeValue": null,
                "side": "Front",
                "scannedAt": "2026-04-13T10:56:00"
              },
              {
                "pageIndex": 1,
                "imagePath": "temp/scan_001_002.tif",
                "imageUrl": "http://localhost:5000/api/scanner/preview/1",
                "pdfUrl": "http://localhost:5000/api/scanner/page-pdf/1",
                "isBarcodeSeparator": false,
                "isDocSeparator": false,
                "barcodeValue": null,
                "side": "Front",
                "scannedAt": "2026-04-13T10:56:02"
              }
            ]
          },
          {
            "fileIndex": 1,
            "pageCount": 1,
            "pages": [
              {
                "pageIndex": 2,
                "imagePath": "temp/scan_001_003.tif",
                "imageUrl": "http://localhost:5000/api/scanner/preview/2",
                "pdfUrl": "http://localhost:5000/api/scanner/page-pdf/2",
                "isBarcodeSeparator": true,
                "isDocSeparator": false,
                "barcodeValue": "File_Separate",
                "side": "Front",
                "scannedAt": "2026-04-13T10:56:04"
              }
            ]
          }
        ]
      },
      {
        "docIndex": 1,
        "files": [
          {
            "fileIndex": 2,
            "pageCount": 3,
            "pages": [
              {
                "pageIndex": 3,
                "imagePath": "temp/scan_001_004.tif",
                "imageUrl": "http://localhost:5000/api/scanner/preview/3",
                "pdfUrl": "http://localhost:5000/api/scanner/page-pdf/3",
                "isBarcodeSeparator": false,
                "isDocSeparator": false,
                "barcodeValue": null,
                "side": "Front",
                "scannedAt": "2026-04-13T10:56:06"
              }
            ]
          }
        ]
      }
    ]
  },
  "errorCode": null,
  "timestamp": "2026-04-13T10:56:04.2020458+07:00"
}
```

| Field | Type | Description |
|-------|------|-------------|
| `totalPages` | number | Tổng số pages đã scan |
| `totalDocuments` | number | Tổng số documents (tách bởi `Doc_Separate` barcode) |
| `totalFiles` | number | Tổng số files (tách bởi `File_Separate` barcode) |
| `documents[].docIndex` | number | Index của document (0-based) |
| `documents[].files[].fileIndex` | number | Index của file trong document (0-based) |
| `documents[].files[].pageCount` | number | Số pages trong file |
| `documents[].files[].pages[]` | array | Danh sách chi tiết từng page |

**Barcode Separator:**
- `Doc_Separate`: Tách thành document mới. Mỗi document có thể chứa nhiều files.
- `File_Separate`: Tách thành file mới trong cùng document.

**Luồng sử dụng:**
1. Client scan xong → gọi `GET /api/scanner/pages`
2. Backend detect barcode, trả về cấu trúc documents/files/pages
3. Client hiển thị tree view Documents → Files → Pages, cho phép edit/delete
4. Client gọi `DELETE /api/scanner/pages/{index}` để xóa page (nếu cần)
5. Client gọi `POST /api/scanner/process` với danh sách documents/files đã chỉnh sửa

---

### 2.4 Get Page Preview - Xem Preview Page

Lấy hình ảnh của 1 page để xem trước.

| | |
|---|---|
| **Method** | `GET` |
| **URL** | `/api/scanner/preview/{index}` |
| **Auth** | Không |

**Path Parameters:**
| Param | Type | Description |
|-------|------|-------------|
| `index` | number | Index của page (0-based) |

**Headers:**
| Header | Required | Description |
|--------|----------|-------------|
| `X-Session-Id` | ❌ | Session ID |

**Response:** Hình ảnh (JPEG/PNG/TIFF)

---

### 2.5 Get Page PDF - Lấy PDF Của 1 Page

Chuyển đổi 1 page thành PDF để xem trước.

| | |
|---|---|
| **Method** | `GET` |
| **URL** | `/api/scanner/page-pdf/{index}` |
| **Auth** | Không |

**Path Parameters:**
| Param | Type | Description |
|-------|------|-------------|
| `index` | number | Index của page (0-based) |

**Headers:**
| Header | Required | Description |
|--------|----------|-------------|
| `X-Session-Id` | ❌ | Session ID |

**Response:** File PDF

---

### 2.6 Delete Page - Xóa Page

Xóa 1 page khỏi session.

| | |
|---|---|
| **Method** | `DELETE` |
| **URL** | `/api/scanner/pages/{pageIndex}` |
| **Auth** | Không |

**Path Parameters:**
| Param | Type | Description |
|-------|------|-------------|
| `pageIndex` | number | Index của page cần xóa (0-based) |

**Headers:**
| Header | Required | Description |
|--------|----------|-------------|
| `X-Session-Id` | ❌ | Session ID |

**Response:**
```json
{
  "success": true,
  "message": "Page 0 deleted",
  "data": {
    "totalPages": 4,
    "pages": [...]
  }
}
```

---

### 2.7 Process Scan - Xử Lý Scan

Tạo PDF, OCR và ký số (tùy chọn) theo danh sách documents/files từ client.

| | |
|---|---|
| **Method** | `POST` |
| **URL** | `/api/scanner/process` |
| **Auth** | Không |
| **Content-Type** | `application/json` |

**Headers:**
| Header | Required | Description |
|--------|----------|-------------|
| `X-Session-Id` | ❌ | Session ID |
| `X-Web-Token` | ❌ | Web token (bắt buộc nếu cần ký số) |
| `X-Role-Id` | ❌ | Role ID (bắt buộc nếu cần ký SIM) |
| `X-User-Id` | ❌ | User ID (bắt buộc nếu cần ký SIM) |

**Request Body (Cấu trúc Documents - khuyến nghị):**
```json
{
  "documents": [
    {
      "docIndex": 0,
      "files": [
        {
          "docIndex": 0,
          "fileIndex": 0,
          "fileName": "12345678_13042026_105604.pdf",
          "pages": [
            { "index": 0, "isOCR": true },
            { "index": 1, "isOCR": true }
          ],
          "signInfo": {
            "phone": "0912345678",
            "messageToBeDisplayed": "",
            "filePath": "",
            "fileBase64": "",
            "fileName": "12345678_13042026_105604.pdf",
            "uploadUrl": "",
            "signType": 0,
            "folderKey": "",
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
        },
        {
          "docIndex": 0,
          "fileIndex": 1,
          "fileName": "12345678_13042026_105604_1.pdf",
          "pages": [
            { "index": 2, "isOCR": false }
          ]
        }
      ]
    },
    {
      "docIndex": 1,
      "files": [
        {
          "docIndex": 1,
          "fileIndex": 2,
          "fileName": "87654321_13042026_110000.pdf",
          "pages": [
            { "index": 3, "isOCR": true },
            { "index": 4, "isOCR": true },
            { "index": 5, "isOCR": false }
          ]
        }
      ]
    }
  ]
}
```

**Request Body (Cấu trúc Files - legacy):**
```json
{
  "files": [
    {
      "docIndex": 0,
      "fileIndex": 0,
      "fileName": "12345678_13042026_105604.pdf",
      "pages": [
        { "index": 0, "isOCR": true },
        { "index": 1, "isOCR": true },
        { "index": 2, "isOCR": true }
      ]
    },
    {
      "docIndex": 0,
      "fileIndex": 1,
      "fileName": "12345678_13042026_105604_1.pdf",
      "pages": [
        { "index": 3, "isOCR": true },
        { "index": 4, "isOCR": false }
      ]
    }
  ]
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `documents` | array | ❌ | Danh sách documents (khuyến nghị) |
| `documents[].docIndex` | number | ✅ | Index của document (0-based) |
| `documents[].files` | array | ❌ | Danh sách files trong document |
| `documents[].files[].docIndex` | number | ✅ | Index của document chứa file này |
| `documents[].files[].fileIndex` | number | ✅ | Index của file trong document (0-based) |
| `documents[].files[].fileName` | string | ✅ | Tên file PDF (do client quyết định) |
| `documents[].files[].pages` | array | ✅ | Danh sách pages trong file |
| `documents[].files[].pages[].index` | number | ✅ | Index của page (0-based) |
| `documents[].files[].pages[].isOCR` | boolean | ✅ | Thực hiện OCR trên page này (mặc định: false) |
| `documents[].files[].signInfo` | object | ❌ | Thông tin ký số (nếu cần ký số) |
| `files` | array | ❌ | Danh sách files (legacy - dùng khi không có documents) |
| `files[].docIndex` | number | ✅ | Index của document (mặc định: 0) |
| `files[].fileIndex` | number | ✅ | Index của file (0-based) |
| `files[].fileName` | string | ✅ | Tên file PDF (do client quyết định) |
| `files[].pages` | array | ✅ | Danh sách pages trong file |
| `files[].pages[].index` | number | ✅ | Index của page (0-based) |
| `files[].pages[].isOCR` | boolean | ✅ | Thực hiện OCR trên page này (mặc định: false) |

### SignInfo Object

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `phone` | string | ✅ (ký SIM) | Số điện thoại đăng ký ký số (bắt buộc khi SignType=1) |
| `messageToBeDisplayed` | string | ❌ | Thông điệp hiển thị khi ký |
| `filePath` | string | ❌ | Đường dẫn file (backend tự set sau khi tạo PDF) |
| `fileBase64` | string | ❌ | File dưới dạng Base64 (backend tự convert nếu không truyền) |
| `fileName` | string | ✅ | Tên file PDF cần ký |
| `uploadUrl` | string | ❌ | URL upload sau khi ký (mặc định: /upload-signed) |
| `signType` | number | ✅ | 0 - Ký Token, 1 - Ký SIM |
| `folderKey` | string | ❌ | Folder key (backend tự lấy nếu không truyền) |
| `listSignatureInfo` | array | ✅ | SignatureInfo Danh sách vị trí ký trên trang |

### SignatureInfo Object

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `page` | number | ✅ | Số trang cần ký (1-based) |
| `x` | number | ✅ | Tọa độ X của vị trí ký |
| `y` | number | ✅ | Tọa độ Y của vị trí ký |
| `signType` | string | ✅ | "IMAGE" hoặc "TEXT" |
| `text` | string | ❌ | Text hiển thị (nếu signType = TEXT) |
| `imageWidth` | number | ✅ | Chiều rộng ảnh chữ ký |
| `imageHeight` | number |✅ | Chiều cao ảnh chữ ký |
| `base64Image` | string | ✅ | Ảnh chữ ký dưới dạng Base64 |

### SignApiResponse Object (Response từ API ký số)

Cả 2 loại ký (Token và SIM) đều trả về format chung `SignApiResponse`:

|| Field | Type | Description |
|-------|------|-------------|
| `success` | boolean | Trạng thái ký số |
| `message` | string | Thông điệp kết quả |
| `data` | object | Data object (có thể null nếu API trả flat format) |
| `errors` | array | Danh sách lỗi (nếu có) |
| `fileName` | string | Tên file đã ký (root-level) |
| `filePath` | string | Server path của file đã ký (root-level) |
| `fileServer` | string | Server path thay thế (root-level) |
| `extension` | string | Đuôi file (root-level) |
| `folderKey` | string | Folder key của file (root-level) |
| `description` | string | Mô tả file (root-level) |

**Response:**
```json
{
  "success": true,
  "message": "Processing completed",
  "data": {
    "sessionId": "abc123def456",
    "number": 0,
    "status": "Completed",
    "totalPages": 6,
    "documents": [
      {
        "docIndex": 0,
        "files": [
          {
            "docIndex": 0,
            "fileIndex": 0,
            "fileId": "xyz78901",
            "fileName": "12345678_13042026_105604.pdf",
            "downloadUrl": "http://localhost:5000/api/files/xyz78901",
            "totalPages": 2,
            "fileSize": 1024000,
            "ocrResult": "Nội dung văn bản được OCR...",
            "createdAt": "2026-04-13T10:58:00",
            "signedFileUrl": "https://api.example.com/signed/folder-123/12345678_signed.pdf",
            "signedFilePath": "/signed/folder-123/12345678_signed.pdf",
            "signSuccess": true
          },
          {
            "docIndex": 0,
            "fileIndex": 1,
            "fileId": "abc12345",
            "fileName": "12345678_13042026_105604_1.pdf",
            "downloadUrl": "http://localhost:5000/api/files/abc12345",
            "totalPages": 2,
            "fileSize": 980000,
            "ocrResult": "Nội dung văn bản được OCR...",
            "createdAt": "2026-04-13T10:58:05"
          }
        ]
      },
      {
        "docIndex": 1,
        "files": [
          {
            "docIndex": 1,
            "fileIndex": 2,
            "fileId": "def67890",
            "fileName": "87654321_13042026_110000.pdf",
            "downloadUrl": "http://localhost:5000/api/files/def67890",
            "totalPages": 2,
            "fileSize": 1050000,
            "ocrResult": "Nội dung văn bản được OCR...",
            "createdAt": "2026-04-13T10:58:10"
          }
        ]
      }
    ]
  }
}
```

**Response Fields (Sign):**
| Field | Type | Description |
|-------|------|-------------|
| `signedFileUrl` | string? | URL file đã ký (null nếu không ký số) |
| `signedFilePath` | string? | Server path của file đã ký |
| `signSuccess` | bool | Trạng thái ký số (false nếu không ký) |

---

### 2.8 Get All Stored Files - Danh Sách File Đã Lưu

Lấy danh sách tất cả file PDF đã được tạo.

| | |
|---|---|
| **Method** | `GET` |
| **URL** | `/api/scanner/stored-files` |
| **Auth** | Không |

---

### 2.9 Get File - Xem File PDF

Lấy file PDF để hiển thị inline.

| | |
|---|---|
| **Method** | `GET` |
| **URL** | `/api/scanner/files/{id}` |
| **Auth** | Không |

---

### 2.10 Delete File - Xóa File

Xóa file PDF đã lưu.

| | |
|---|---|
| **Method** | `DELETE` |
| **URL** | `/api/scanner/files/{id}` |
| **Auth** | Không |

---

## 3. Files

### Download File - Tải File PDF

Tải file PDF (hỗ trợ range request).

| | |
|---|---|
| **Method** | `GET` |
| **URL** | `/api/files/{id}` |
| **Auth** | Không |

---

## Error Codes

| Error Code | HTTP Status | Mô tả |
|------------|-------------|-------|
| `INVALID_REQUEST` | 400 | Request không hợp lệ |
| `SESSION_NOT_FOUND` | 404 | Session không tồn tại |
| `PAGE_NOT_FOUND` | 404 | Page không tồn tại |
| `FILE_NOT_FOUND` | 404 | File không tồn tại |
| `NO_PAGES` | 400 | Không có pages để xử lý |
| `SERVICE_NOT_READY` | 503 | Service chưa sẵn sàng |
| `PROCESS_FAILED` | 400 | Xử lý scan thất bại |
| `SCAN_FAILED` | 400 | Quét thất bại |

---

## Enum Values

### ScanStatus
- `Idle` (0) - Chưa bắt đầu
- `Scanning` (1) - Đang quét
- `Processing` (2) - Đang xử lý
- `Completed` (3) - Hoàn thành
- `Error` (4) - Có lỗi

### ColorMode
- `Color` - Màu toàn phần
- `BW` - Đen trắng
- `Gray` - Thang xám

### PaperSize
- `A4` - Khổ A4
- `A3` - Khổ A3
- `Letter` - Khổ Letter

### Barcode Separator Types
- `File_Separate` - Tách thành file mới trong cùng document
- `Doc_Separate` - Tách thành document mới (document có thể chứa nhiều files)
