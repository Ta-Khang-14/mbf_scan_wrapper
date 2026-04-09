# Luồng Xử Lý Scan - MBF Scan Service

## Tổng Quan

Service hỗ trợ quét tài liệu từ máy scan, xử lý barcode, tạo PDF và OCR.

---

## Luồng Chính

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           LUỒNG XỬ LÝ SCAN                                 │
└─────────────────────────────────────────────────────────────────────────────┘

  ┌──────────┐     ┌──────────┐     ┌──────────┐     ┌──────────┐
  │  Bước 1  │ ──► │  Bước 2  │ ──► │  Bước 3  │ ──► │  Bước 4  │
  │ Liệt kê │     │  Chọn &  │     │   Xem &  │     │  Xử lý   │
  │ máy scan│     │  Quét    │     │  Quản lý │     │  Tạo PDF │
  └──────────┘     └──────────┘     │  Pages   │     │  & OCR   │
       │                │            └──────────┘     └──────────┘
       │                │                  │                  │
       ▼                ▼                  ▼                  ▼
  GET /scanner   POST /scanner     GET /scanner       POST /scanner
      /list          /scan            /pages             /process
                                    DELETE /pages
```

---

## Chi Tiết Từng Bước

### Bước 1: Liệt Kê Máy Scan

**API:** `GET /api/scanner/list`

**Mục đích:** Lấy danh sách máy scan khả dụng

**Response:**
```json
{
  "success": true,
  "data": [
    { "name": "FUJITSU fi-760", "productName": "FUJITSU fi-760", "isAvailable": true }
  ]
}
```

---

### Bước 2: Chọn & Quét

**API:** `POST /api/scanner/scan`

**Request:**
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

**Lưu ý:** `MaxPages` (mặc định 400) và `EnableDuplex` (mặc định true) được thiết lập từ server config, client không gửi được.

**Response:**
```json
{
  "success": true,
  "data": {
    "sessionId": "abc123def456",
    "status": "Scanning",
    "totalPages": 5,
    "totalFiles": 0
  }
}
```

**FE cần lưu:** `sessionId` để dùng cho các API tiếp theo

---

### Bước 3: Xem & Quản Lý Pages

#### 3.1 Lấy danh sách Pages
**API:** `GET /api/scanner/pages`

**Header:** `X-Session-Id: <sessionId>`

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
        "side": "Front"
      }
    ]
  }
}
```

#### 3.2 Xem Preview Page
**API:** `GET /api/scanner/preview/{index}`

**Header:** `X-Session-Id: <sessionId>`

**Response:** Hình ảnh (JPEG/PNG/TIFF)

#### 3.3 Xem PDF của 1 Page
**API:** `GET /api/scanner/page-pdf/{index}`

**Header:** `X-Session-Id: <sessionId>`

**Response:** File PDF

#### 3.4 Xóa Page
**API:** `DELETE /api/scanner/pages/{index}`

**Header:** `X-Session-Id: <sessionId>`

**Response:**
```json
{
  "success": true,
  "data": {
    "totalPages": 4,
    "pages": [...]
  }
}
```

---

### Bước 4: Xử Lý - Tạo PDF & OCR

**API:** `POST /api/scanner/process`

**Headers:**
- `X-Session-Id: <sessionId>` (optional - dùng session cuối nếu không gửi)

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

**Xử lý bên trong:**
1. Barcode Detection - nhận diện barcode separator trên các trang
2. Group Pages - nhóm pages theo barcode thành từng file
3. Tạo PDF - ghép các page thành file PDF với tên theo rule trên
4. OCR - đọc text từ trang đầu tiên của mỗi file (nếu có service)

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
        "ocrResult": "Nội dung văn bản OCR...",
        "createdAt": "2026-04-09T10:30:00"
      },
      {
        "fileId": "abc12345",
        "fileName": "12345678_09042026_143000_1.pdf",
        "downloadUrl": "http://localhost:5000/api/files/abc12345",
        "totalPages": 2,
        "fileSize": 512000,
        "ocrResult": "Nội dung văn bản OCR...",
        "createdAt": "2026-04-09T10:30:00"
      }
    ]
  }
}
```

---

## Luồng Download File (Sau Khi Process)

### Cách 1: Dùng downloadUrl từ Process Response
- FE sử dụng trực tiếp `downloadUrl` trả về từ bước 4

### Cách 2: Download theo fileId
**API:** `GET /api/files/{fileId}`

**Response:** File PDF

---

## Luồng Quản Lý File Đã Lưu

```
┌──────────────────┐     ┌──────────────────┐     ┌──────────────────┐
│  Liệt kê File   │     │  Xem chi tiết    │     │   Xóa File       │
│ GET /stored-files│ ──► │ GET /files/{id}  │ ──► │DELETE /files/{id}│
└──────────────────┘     └──────────────────┘     └──────────────────┘
```

---

## Các Trạng Thái Session

| Status | Mô tả |
|--------|-------|
| `Idle` | Chưa bắt đầu |
| `Scanning` | Đang quét |
| `Processing` | Đang xử lý (barcode, PDF, OCR) |
| `Completed` | Hoàn thành |
| `Error` | Có lỗi xảy ra |

---

## Cấu Hình Server (appsettings.json)

```json
{
  "Scanner": {
    "DefaultMaxPages": 400,
    "DefaultEnableDuplex": true,
    "DefaultDPI": 300,
    "DefaultColorMode": "Color",
    "DefaultPaperSize": "A4"
  }
}
```

**Lưu ý:** `MaxPages` và `EnableDuplex` chỉ thiết lập được từ server, client không gửi lên được.

---

## Sơ Đồ Tương Tác FE ↔ API

```
┌─────────┐                              ┌─────────────┐
│   FE    │                              │ Scan Service│
└────┬────┘                              └──────┬──────┘
     │                                         │
     │  1. GET /scanner/list                   │
     │ ──────────────────────────────────────► │
     │ ◄────────────────────────────────────── │
     │     Danh sách máy scan                  │
     │                                         │
     │  2. POST /scanner/scan                  │
     │ ──────────────────────────────────────► │
     │ ◄────────────────────────────────────── │
     │     sessionId + trạng thái               │
     │                                         │
     │  3. GET /scanner/pages  (X-Session-Id)   │
     │ ──────────────────────────────────────► │
     │ ◄────────────────────────────────────── │
     │     Danh sách pages + imageUrl          │
     │                                         │
     │  4. DELETE /scanner/pages/{i} (nếu cần)  │
     │ ──────────────────────────────────────► │
     │ ◄────────────────────────────────────── │
     │     Danh sách pages mới                  │
     │                                         │
     │  5. POST /scanner/process (X-Session-Id)│
     │ ──────────────────────────────────────► │
     │                                         │
     │     [Xử lý Barcode → Group → PDF → OCR] │
     │                                         │
     │ ◄────────────────────────────────────── │
     │     File list + downloadUrl (full URL)  │
     │                                         │
     │  6. GET /scanner/preview/{i}            │
     │ ──────────────────────────────────────► │
     │ ◄────────────────────────────────────── │
     │     Hình ảnh page                       │
     │                                         │
     │  7. GET /api/files/{fileId}             │
     │ ──────────────────────────────────────► │
     │ ◄────────────────────────────────────── │
     │     File PDF để download                │
     │                                         │
```
