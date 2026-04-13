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

**FE cần thực hiện:** Lưu `sessionId` vào biến để dùng cho các API tiếp theo, mỗi lần scan sẽ thực hiện tạo 1 session mới

---

### 2.3 Detect Files - Detect Barcode + Group Pages

Detect barcode trên các pages đã quét, nhóm pages thành các file và trả về danh sách file với preview names.

| | |
|---|---|
| **Method** | `POST` |
| **URL** | `/api/scanner/pages` |
| **Auth** | Không |
| **Content-Type** | `application/json` |

**Headers:**
| Header | Required | Description |
|--------|----------|-------------|
| `X-Session-Id` | ❌ | Session ID. Nếu không gửi, dùng session cuối cùng |

**Request Body:**
```json
{
  "number": 12345678
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `number` | long | ❌ | Số hồ sơ (để generate file names preview) |

**Response:**
```json
{
  "success": true,
  "message": null,
  "data": {
    "number": 12345678,
    "totalPages": 5,
    "totalFiles": 2,
    "files": [
      {
        "fileName": "12345678_13042026_105604.pdf",
        "pages": [
          {
            "pageIndex": 0,
            "imagePath": "temp/scan_001_001.tif",
            "imageUrl": "http://localhost:5000/api/scanner/preview/0",
            "pdfUrl": "http://localhost:5000/api/scanner/page-pdf/0",
            "isBarcodeSeparator": false,
            "barcodeValue": null,
            "side": "Front",
            "scannedAt": "2026-04-13T10:56:00"
          }
        ]
      },
      {
        "fileName": "12345678_13042026_105604_1.pdf",
        "pages": [
          {
            "pageIndex": 2,
            "imagePath": "temp/scan_001_003.tif",
            "imageUrl": "http://localhost:5000/api/scanner/preview/2",
            "pdfUrl": "http://localhost:5000/api/scanner/page-pdf/2",
            "isBarcodeSeparator": false,
            "barcodeValue": null,
            "side": "Front",
            "scannedAt": "2026-04-13T10:56:05"
          },
          {
            "pageIndex": 3,
            "imagePath": "temp/scan_001_004.tif",
            "imageUrl": "http://localhost:5000/api/scanner/preview/3",
            "pdfUrl": "http://localhost:5000/api/scanner/page-pdf/3",
            "isBarcodeSeparator": false,
            "barcodeValue": null,
            "side": "Front",
            "scannedAt": "2026-04-13T10:56:10"
          }
        ]
      }
    ]
  },
  "errorCode": null,
  "timestamp": "2026-04-13T10:56:04.2020458+07:00"
}
```

**Luồng sử dụng:**
1. Client scan xong → gọi `POST /api/scanner/pages` với `number`
2. Backend detect barcode, group pages, trả về `files` với preview file names và danh sách pages trong mỗi file
3. Client hiển thị danh sách file, cho phép edit/delete pages trên UI
4. Client gọi `DELETE /api/scanner/pages/{index}` để xóa page (nếu cần)
5. Client gọi `POST /api/scanner/process` với danh sách file đã chỉnh sửa

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

Tạo PDF và OCR theo danh sách file từ client.

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

**Request Body:**
```json
{
  "number": 12345678,
  "files": [
    {
      "fileName": "12345678_13042026_105604.pdf",
      "pageIndices": [0, 1]
    },
    {
      "fileName": "12345678_13042026_105604_1.pdf",
      "pageIndices": [2, 3, 4]
    }
  ]
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `number` | long | ❌ | Số hồ sơ |
| `files` | array | ✅ | Danh sách file cần tạo |
| `files[].fileName` | string | ✅ | Tên file PDF (do client quyết định) |
| `files[].pageIndices` | array | ✅ | Danh sách page indices để ghép thành file |

**Response:**
```json
{
  "success": true,
  "message": "Processing completed",
  "data": {
    "sessionId": "abc123def456",
    "number": 12345678,
    "status": "Completed",
    "totalPages": 5,
    "files": [
      {
        "fileId": "xyz78901",
        "fileName": "12345678_13042026_105604.pdf",
        "downloadUrl": "http://localhost:5000/api/files/xyz78901",
        "totalPages": 2,
        "fileSize": 1024000,
        "ocrResult": "Nội dung văn bản được OCR...",
        "createdAt": "2026-04-13T10:58:00"
      }
    ]
  }
}
```

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
