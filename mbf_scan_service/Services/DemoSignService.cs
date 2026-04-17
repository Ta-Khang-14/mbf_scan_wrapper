namespace mbf_scan_service.Services;

using mbf_scan_service.Models;
using Serilog;

public class DemoSignService
{
    public SignApiResponse SimulateSignResponse(string originalFileName)
    {
        var guid = Guid.NewGuid().ToString("N")[..16];
        var dateFolder = DateTime.Now.ToString("yyyy/MM/dd");
        var signedFileName = $"{Path.GetFileNameWithoutExtension(originalFileName)}_signed.pdf";

        Log.Information("DemoSign: Simulating sign response for {FileName}", originalFileName);

        return new SignApiResponse
        {
            Success = true,
            Message = "Tải tài liệu đính kèm thành công.",
            FileName = signedFileName,
            Description = $"{originalFileName}TEST_KY_SO_signed.pdf",
            FilePath = $"/Contents/1/{dateFolder}/doc_1_Signed_{guid}.pdf",
            Extension = "pdf",
            FileServer = $"/Contents/1/{dateFolder}/doc_1_Signed_{guid}.pdf",
            FolderKey = "F2"
        };
    }
}
