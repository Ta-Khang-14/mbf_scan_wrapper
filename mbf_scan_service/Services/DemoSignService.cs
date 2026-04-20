namespace mbf_scan_service.Services;

using mbf_scan_service.Models;
using Serilog;

public class DemoSignService
{
    public SignApiResponse SimulateSignResponse(string originalFileName)
    {
        var guid = Guid.NewGuid().ToString("N")[..16];
        var signedFileName = $"{Path.GetFileNameWithoutExtension(originalFileName)}_signed.pdf";

        Log.Information("DemoSign: Simulating sign response for {FileName}", originalFileName);

        return new SignApiResponse
        {
            Success = true,
            Message = "Tải tài liệu đính kèm thành công.",
            FileName = signedFileName,
            Description = $"{originalFileName}TEST_KY_SO_signed.pdf",
            FilePath = $"/Contents/1/2026/04/16/doc_1_Signed_1fed932ef1c94d9a86b3e3a65cda65f5.pdf",
            Extension = "pdf",
            FileServer = $"/Contents/1/2026/04/16/doc_1_Signed_1fed932ef1c94d9a86b3e3a65cda65f5.pdf",
            FolderKey = "F2"
        };
    }
}
