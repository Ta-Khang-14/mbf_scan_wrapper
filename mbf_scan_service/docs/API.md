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

### 2.3 Get Pages - Lấy Danh Sách Pages

Lấy danh sách tất cả pages đã quét trong session hiện tại.

|---|---|
| **Method** | `GET` |
| **URL** | `/api/scanner/pages` |
| **Auth** | Không |

**Headers:**
| Header | Required | Description |
|--------|----------|-------------|
| `X-Session-Id` | ❌ | Session ID. Nếu không gửi, dùng session cuối cùng (để chính xác nhất cần truyền lên) |

**Response:**
```json
{
  "success": true,
  "data": {
    "totalPages": 5,
    "pages": [
      {
        "pageIndex": 0,
        "imagePath": "temp/scan_001_001.tif",
        "imageUrl": "http://localhost:5000/api/scanner/preview/0",
        "pdfUrl": "http://localhost:5000/api/scanner/page-pdf/0",
        "isBarcodeSeparator": false,
        "barcodeValue": null,
        "side": "Front",
        "scannedAt": "2026-04-09T10:30:00"
      },
      {
        "pageIndex": 1,
        "imagePath": "temp/scan_001_002.tif",
        "imageUrl": "http://localhost:5000/api/scanner/preview/1",
        "pdfUrl": "http://localhost:5000/api/scanner/page-pdf/1",
        "isBarcodeSeparator": true,
        "barcodeValue": "PAGE_001",
        "side": "Front",
        "scannedAt": "2026-04-09T10:30:05"
      }
    ]
  }
}
```

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

**Response:** Hình ảnh (TIFF file)

**Content-Type:** `image/jpeg`, `image/png`, hoặc `image/tiff`

---

### 2.5 Get Page PDF - Lấy PDF Của 1 Page

Chuyển đổi 1 page thành PDF để xem/xem trước.

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

**Content-Type:** `application/pdf`

---

### 2.6 Delete Page - Xóa Page

Xóa 1 page khỏi session.

|---|---|
| **Method** | `DELETE` |
| **URL** | `/api/scanner/pages/{pageIndex}` |
| **Auth** | Không |

**Path Parameters:**
| Param | Type | Description |
|-------|------|-------------|
| `index` | number | Index của page cần xóa (0) |

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

Xử lý các pages đã quét: nhận diện barcode, nhóm pages, tạo PDF và OCR.

|---|---|
| **Method** | `POST` |
| **URL** | `/api/scanner/process` |
| **Auth** | Không |
| **Content-Type** | `application/json` |

**Headers:**
| Header | Required | Description |
|--------|----------|-------------|
| `X-Session-Id` | ❌ | Session ID (dùng session cuối nếu không gửi) |

**Request Body:**
```json
{
  "number": 12345678
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `number` | long | ✅ | Số hồ sơ để đặt tên file PDF |

**Quy tắc đặt tên file:**
- File 1: `{Number}_ddMMyyyy_HHmmss.pdf`
- File 2: `{Number}_ddMMyyyy_HHmmss_1.pdf`
- File N: `{Number}_ddMMyyyy_HHmmss_{N-1}.pdf`

**Ví dụ:**
- `12345678_09042026_143000.pdf`
- `12345678_09042026_143000_1.pdf`

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
        "fileName": "12345678_09042026_143000.pdf",
        "downloadUrl": "http://localhost:5000/api/files/xyz78901",
        "totalPages": 3,
        "fileSize": 1024000,
        "ocrResult": "Nội dung văn bản được OCR từ trang đầu tiên...",
        "createdAt": "2026-04-09T10:35:00"
      },
      {
        "fileId": "abc12345",
        "fileName": "12345678_09042026_143000_1.pdf",
        "downloadUrl": "http://localhost:5000/api/files/abc12345",
        "totalPages": 2,
        "fileSize": 512000,
        "ocrResult": "Nội dung văn bản được OCR từ trang đầu tiên...",
        "createdAt": "2026-04-09T10:35:00"
      }
    ]
  }
}
```
---

### 2.8 Get All Stored Files - Danh Sách File Đã Lưu

Lấy danh sách tất cả file PDF đã được tạo và lưu trữ.

| | |
|---|---|
| **Method** | `GET` |
| **URL** | `/api/scanner/stored-files` |
| **Auth** | Không |

**Response:**
```json
{
  "success": true,
  "message": "File list retrieved",
  "data": [
    {
      "id": "xyz78901",
      "fileName": "scan_xyz78901.pdf",
      "filePath": "output/scan_xyz78901.pdf",
      "downloadUrl": "http://localhost:5000/api/files/xyz78901",
      "fileSize": 1024000,
      "formattedFileSize": "1000 KB",
      "createdAt": "2026-04-09T10:35:00"
    }
  ]
}
```

---

### 2.9 Get File - Xem File PDF

Lấy file PDF để hiển thị (inline).

| | |
|---|---|
| **Method** | `GET` |
| **URL** | `/api/scanner/files/{id}` |
| **Auth** | Không |

**Path Parameters:**
| Param | Type | Description |
|-------|------|-------------|
| `id` | string | File ID |

**Response:** File PDF (Content-Type: `application/pdf`)

---

### 2.10 Delete File - Xóa File

Xóa file PDF đã lưu.

|---|---|
| **Method** | `DELETE` |
| **URL** | `/api/scanner/files/{id}` |
| **Auth** | Không |

**Path Parameters:**
| Param | Type | Description |
|-------|------|-------------|
| `id` | string | File ID |

**Response:**
```json
{
  "success": true,
  "message": "File xyz78901 deleted"
}
```

---

## 3. Files

### Download File - Tải File PDF

Tải file PDF để download (hỗ trợ range request).

|---|---|
| **Method** | `GET` |
| **URL** | `/api/files/{id}` |
| **Auth** | Không |

**Path Parameters:**
| Param | Type | Description |
|-------|------|-------------|
| `id` | string | File ID |

**Response:** File PDF (Content-Type: `application/pdf`)

**Header Response:**
```
Content-Disposition: attachment; filename="scan_xyz78901.pdf"
```

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
