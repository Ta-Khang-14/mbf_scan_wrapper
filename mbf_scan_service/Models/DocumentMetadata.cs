namespace mbf_scan_service.Models;

public class DocumentMetadata
{
    public string? DocType { get; set; }
    public string? PublishUnit { get; set; }
    public string? Notation { get; set; }
    public string? PublishDateStr { get; set; }
    public string? PublishDate { get; set; }
    public string? Abstract { get; set; }
    public string? Number { get; set; }
    public string? ReceivedDate { get; set; }
    public string? RecipientUnit { get; set; }
    public string? SignerRole { get; set; }
    public string? Signer { get; set; }
    public string Urgent { get; set; } = "Bình thường";
    public bool IsNonStandard { get; set; }

    public string ToDisplayString()
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(DocType)) parts.Add($"Loại: {DocType}");
        if (!string.IsNullOrEmpty(PublishUnit)) parts.Add($"Nơi gửi: {PublishUnit}");
        if (!string.IsNullOrEmpty(Notation)) parts.Add($"Số: {Notation}");
        if (!string.IsNullOrEmpty(PublishDate)) parts.Add($"Ngày: {PublishDate}");
        if (!string.IsNullOrEmpty(Abstract)) parts.Add($"Trích yếu: {Abstract}");
        if (!string.IsNullOrEmpty(Number)) parts.Add($"Số đến: {Number}");
        if (!string.IsNullOrEmpty(ReceivedDate)) parts.Add($"Ngày đến: {ReceivedDate}");
        if (!string.IsNullOrEmpty(RecipientUnit)) parts.Add($"Nơi nhận: {RecipientUnit}");
        if (!string.IsNullOrEmpty(SignerRole)) parts.Add($"Chức vụ: {SignerRole}");
        if (!string.IsNullOrEmpty(Signer)) parts.Add($"Người ký: {Signer}");
        if (Urgent != "Bình thường") parts.Add($"Độ khẩn: {Urgent}");
        return string.Join(" | ", parts);
    }
}

public class ExtractOCRRequest
{
    public string FileId { get; set; } = string.Empty;
    public string? OCRText { get; set; }
}
