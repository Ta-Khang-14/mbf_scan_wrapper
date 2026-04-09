# KẾ HOẠCH PHÁT TRIỂN — MBF SCAN SERVICE

> **Ngày tạo:** 08/04/2026
> **Ngày cập nhật:** 08/04/2026
> **Trạng thái:** Đang chờ triển khai

---

## 1. Tổng quan

Phần mềm WinForms hỗ trợ web giao tiếp máy scan qua REST API, sử dụng thư viện TWAIN để quét tài liệu.

## 2. Yêu cầu chức năng

| # | Chức năng | Mô tả | Ưu tiên |
|---|-----------|-------|---------|
| 1 | Quét tài liệu | Quét trả về danh sách page + file PDF merge | P0 |
| 2 | Xoá page | Xoá 1 page và trả lại PDF đã merge lại | P0 |
| 3 | Phân tách file bằng Barcode | Dùng barcode "SEPARATOR" để tách file | P0 |
| 4 | OCR | OCR trang đầu tiên của mỗi file (tiếng Anh + Việt) | P0 |
| 5 | Danh sách máy scan | API lấy danh sách máy scan | P0 |
| 6 | Hệ thống log | Ghi log bằng Serilog | P1 |

## 3. Yêu cầu kỹ thuật

- Tối đa **400 trang** (~200 tờ giấy)
- Quét **mặc định:** màu (color) + 2 mặt (duplex)
- Barcode detection trên **TIFF gốc** (trước khi merge)
- OCR trên **TIFF gốc** (trước khi convert PDF)
- File trả về qua **URL download**
- **Không cần authentication**

---

## 4. Thư viện sử dụng (Miễn phí / Mã nguồn mở)

| Chức năng | Thư viện | Phiên bản | Giấy phép |
|-----------|----------|-----------|-----------|
| TWAIN Scanner | NTwain | 3.7.5 | MIT |
| Barcode Detection | ZXing.Net | 0.16.x | Apache 2.0 |
| OCR | Tesseract.NET (Tesseract) | 5.x | Apache 2.0 |
| PDF Processing | PDFsharp | 6.x | MIT |
| REST API | ASP.NET Core Minimal API | 8.x | MIT |
| Logging | Serilog + Serilog.Sinks.File | 3.x | Apache 2.0 |
| Image Processing | SixLabors.ImageSharp | 3.x | Apache 2.0 |

---

## 5. Cấu trúc thư mục

```
mbf_scan_service/
├── Controllers/
│   └── ScanController.cs          # REST API endpoints
├── Services/
│   ├── ScannerService.cs          # TWAIN wrapper (NTwain)
│   ├── BarcodeService.cs          # Barcode detection (ZXing)
│   ├── OCRService.cs              # OCR processing (Tesseract)
│   ├── PDFService.cs              # PDF merge/split (PDFsharp)
│   └── FileService.cs             # File management & download URL
├── Models/
│   ├── ScanSession.cs             # Phiên quét
│   ├── ScanPage.cs                # Trang đã quét
│   ├── ScanFile.cs                # File sau khi tách
│   └── ApiResponse.cs             # Response model
├── Logging/
│   └── LoggingSetup.cs            # Serilog configuration
├── Program.cs                     # Entry point (WinForms + API)
├── Form1.cs                       # Minimal WinForms (log display)
└── appsettings.json
```

---

## 6. REST API Endpoints

| Method | Endpoint | Mô tả |
|--------|----------|-------|
| `GET` | `/api/scanner/list` | Lấy danh sách máy scan |
| `POST` | `/api/scanner/select` | Chọn máy scan |
| `POST` | `/api/scan/start` | Bắt đầu phiên quét |
| `POST` | `/api/scan/stop` | Kết thúc & xử lý |
| `GET` | `/api/scan/pages` | Danh sách trang đã quét |
| `POST` | `/api/scan/delete-page/{index}` | Xoá page (trả lại PDF mới) |
| `GET` | `/api/scan/files` | Danh sách file (sau barcode split) |
| `GET` | `/api/scan/file/{id}` | Download file PDF (URL) |
| `GET` | `/api/scan/ocr/{id}` | Kết quả OCR trang đầu |
| `GET` | `/api/scan/status` | Trạng thái phiên quét |

---

## 7. Chi tiết từng bước thực hiện

### Bước 1: Setup Project & Dependencies
- [x] Update `.csproj` thêm tất cả NuGet packages
- [x] Cấu hình Serilog ghi log ra file `logs/scan_service_.log`
- [x] Cấu hình ASP.NET Core Minimal API chạy song song WinForms
- [x] Tạo cấu trúc thư mục

### Bước 2: Models & Data Structures
- [x] Tạo `ScanSession.cs` - Phiên quét
- [x] Tạo `ScanPage.cs` - Trang đã quét
- [x] Tạo `ScanFile.cs` - File sau khi tách
- [x] Tạo `ApiResponse.cs` - Response model

### Bước 3: TWAIN Scanner Service
- [x] Tích hợp NTwain
- [x] Lấy danh sách máy scan
- [x] Chọn và kết nối máy scan
- [x] Cấu hình quét mặc định: DPI=300, Color, A4, Duplex
- [x] Quản lý memory cho 400 trang (lưu tạm ra disk)

### Bước 4: Barcode Detection
- [x] Tích hợp ZXing.Net
- [x] Pattern cố định: barcode có nội dung **"SEPARATOR"**
- [x] Detect trên TIFF gốc (trước khi merge)
- [x] Quét cả mặt trước & mặt sau, vùng trên/dưới (1 lần quét - không phân biệt front/back)

### Bước 5: OCR Service
- [x] Tích hợp Tesseract.NET với data `eng` + `vie`
- [x] OCR trang đầu tiên của mỗi file (sau khi tách)
- [x] OCR trên TIFF gốc trước khi convert PDF

### Bước 6: PDF Service
- [x] Tích hợp PDFsharp 6.x
- [x] Convert TIFF → PDF (ImageSharp + PDFsharp)
- [x] Merge pages → PDF
- [x] Xoá page & trả lại PDF đã cập nhật
- [x] Tách file dựa trên barcode marker (thông qua BarcodeService)

### Bước 7: File Service & Download URL
- [x] Lưu file PDF vào thư mục output
- [x] Generate URL download cho web (relative path: /api/files/{id})
- [x] Quản lý metadata (ID, filename, size, created date)
- [x] File tạm lưu trữ vĩnh viễn (chưa cần cleanup)

### Bước 8: REST API Controllers
- [x] Triển khai tất cả endpoints (Scanner, Scan, Files)
- [x] Tích hợp các services (Scanner, Barcode, OCR, PDF, File)
- [x] Validate request/response
- [x] Xử lý lỗi và trả về mã HTTP phù hợp
- [x] Endpoint download file: GET /api/files/{id}

### Bước 9: WinForms & Logging
- [x] Minimal WinForms UI (log display)
- [x] System Tray icon (minimize to tray)
- [x] Serilog ghi log ra file (logs/mbf_scan_.log, rolling daily, keep 7 days)
- [x] Serilog hiển thị log lên TextBox real-time
- [ ] Test tích hợp toàn bộ hệ thống (bỏ qua - tự test)

---

## 8. Luồng xử lý quét

```
1. Web gọi POST /api/scan/start
   └── ScannerService bắt đầu quét

2. Mỗi trang quét xong:
   ├── Lưu TIFF vào thư mục tạm
   ├── BarcodeService detect barcode trên trang
   │   └── Nếu là "SEPARATOR" → đánh dấu tách file
   └── Tiếp tục quét

3. Khi gọi POST /api/scan/stop:
   ├── BarcodeService tách pages thành các file
   ├── OCRService OCR trang đầu mỗi file
   ├── PDFService merge → PDF
   └── Trả về danh sách file + URL download

4. Xoá page (POST /api/scan/delete-page/{index}):
   ├── Xoá page khỏi danh sách
   ├── PDFService merge lại PDF
   └── Trả về URL download mới
```

---

## 9. Mô hình dữ liệu

```csharp
// ScanSession - Phiên quét
{
    SessionId: string,
    ScannerName: string,
    Status: "Idle" | "Scanning" | "Processing",
    Pages: List<ScanPage>,
    Files: List<ScanFile>
}

// ScanPage - Trang đã quét
{
    PageIndex: int,
    ImagePath: string (TIFF),
    IsBarcodeSeparator: bool,
    Side: "Front" | "Back"
}

// ScanFile - File sau khi tách
{
    FileId: string,
    Pages: List<ScanPage>,
    PDFPath: string,
    DownloadUrl: string,
    OCRResult: string (trang đầu)
}
```

---

## 10. Cấu hình mặc định

| Tham số | Giá trị mặc định |
|---------|-----------------|
| DPI | 300 |
| Color Mode | Color |
| Duplex | Enable (2 mặt) |
| Paper Size | A4 |
| Barcode Pattern | "SEPARATOR" |
| Max Pages | 400 |

---

## 11. Progress Tracking

| Bước | Mô tả | Trạng thái | Ghi chú |
|------|-------|-----------|---------|
| 1 | Setup Project & Dependencies | [x] | |
| 2 | Models & Data Structures | [x] | |
| 3 | TWAIN Scanner Service | [x] | ScannerService.cs đã hoàn thành |
| 4 | Barcode Detection | [x] | BarcodeService.cs đã hoàn thành |
| 5 | OCR Service | [x] | OCRService.cs đã hoàn thành |
| 6 | PDF Service | [x] | PDFService.cs đã hoàn thành |
| 7 | File Service & Download URL | [x] | FileService.cs đã hoàn thành |
| 8 | REST API Controllers | [x] | ScanController.cs đã hoàn thành |
| 9 | WinForms & Logging | [x] | Form1.cs + System Tray + Serilog |

## 12. Testing

- [x] Postman collection: `MBF_Scan_Service_API.postman_collection.json`
- [ ] Import vào Postman và test các endpoints

---

## 12. Known Issues & Notes

> **Ghi chú:**
> - NTwain yêu cầu máy scan hỗ trợ TWAIN driver
> - Tesseract cần download trained data cho `eng` và `vie`
> - File tạm nên được cleanup định kỳ hoặc khi session kết thúc
> - Cân nhắc dùng ImageSharp thay vì System.Drawing để tương thích tốt hơn

---

## 13. References

- NTwain: https://github.com/soukoku/ntwain
- ZXing.Net: https://github.com/micolous/metrodroid/wiki/ZXing.Net
- Tesseract.NET: https://github.com/charlesw/tesseract
- PDFsharp: https://www.pdfsharp.net/
- Serilog: https://serilog.net/
- SixLabors.ImageSharp: https://sixlabors.com/products/imagesharp/
