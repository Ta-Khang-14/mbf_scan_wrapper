namespace mbf_scan_service.Services;

using System.Text.RegularExpressions;
using System.Globalization;
using mbf_scan_service.Models;

public class DocumentExtractor
{
    private static readonly string[] DocTypeKeywords = { "BÁO CÁO", "QUYẾT ĐỊNH", "TỜ TRÌNH", "THÔNG BÁO", "NGHỊ QUYẾT" };
    private static readonly string[] SignerRoleKeywords = { "GIÁM ĐỐC", "PHÓ GIÁM ĐỐC", "TỔNG GIÁM ĐỐC", "KT. TỔNG GIÁM ĐỐC", "CHỦ TỊCH" };
    private static readonly string[] UrgentKeywords = { "HỎA TỐC", "THƯỢNG KHẨN", "KHẨN", "TUYỆT MẬT", "TỐI MẬT", "MẬT" };

    private static readonly Regex RePublishUnit = new(@"(?i)(?:Tên\s*đơn\s*vị)(?:\s*kiểm\s*kê)?\s*:\s*(.+)", RegexOptions.Compiled);
    private static readonly Regex RePublishUnitNonStandard = new(@"(?i)(?:Tên\s+đơn\s+vị\s+kiểm\s+kê\s*:\s*(.+)|Cơ\s+quan\s+quản\s+lý.*:\s*(.+))", RegexOptions.Compiled);
    private static readonly Regex ReNotation = new(@"(?i)(?:Số|SỐ)\s*[:\.]?\s*([0-9a-zA-ZĐ/\\-]+)", RegexOptions.Compiled);
    private static readonly Regex RePublishDate = new(@"(?:ngày\s+\d{1,2}\s+tháng\s+\d{1,2}\s+năm\s+\d{4})", RegexOptions.Compiled);
    private static readonly Regex RePublishDateNonStandard = new(@"(?i)Hôm\s+nay,\s+ngày\s+(\d{1,2})\s+tháng\s+(\d{1,2})\s+năm\s+(\d{4})", RegexOptions.Compiled);
    private static readonly Regex ReReceivedNumber = new(@"(?i)ĐẾN\s*Số\s*[:\.]?\s*(\d+)", RegexOptions.Compiled);
    private static readonly Regex ReReceivedDate = new(@"(?i)Ngày\s*[:\.]?\s*(\d{1,2}[\s\-\/]+\d{1,2}[\s\-\/]+\d{4})", RegexOptions.Compiled);
    private static readonly Regex ReRecipientUnit = new(@"(?i)Kính\s+gửi\s*:\s*(.*)", RegexOptions.Compiled);
    private static readonly Regex ReCCUnit = new(@"(?i)Nơi\s+nhận\s*:\s*(.*)", RegexOptions.Compiled);
    private static readonly Regex ReVuPrefix = new(@"(?i)(?:V/v|VÍ\s*DỤ)\s*:?\s*(.+)", RegexOptions.Compiled | RegexOptions.Singleline);

    public DocumentMetadata Extract(string ocrText)
    {
        var result = new DocumentMetadata();
        if (string.IsNullOrWhiteSpace(ocrText))
            return result;

        var lines = ocrText.Split('\n').Select(l => l.Trim()).ToArray();

        result.DocType = ExtractDocType(lines, ocrText);
        result.IsNonStandard = DetectNonStandardForm(ocrText);

        if (result.IsNonStandard)
            ExtractNonStandard(ocrText, lines, result);
        else
            ExtractStandard(ocrText, lines, result);

        result.Urgent = ExtractUrgent(ocrText);

        return result;
    }

    private string? ExtractDocType(string[] lines, string ocrText)
    {
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0 && trimmed == trimmed.ToUpper() && trimmed.Length < 50)
            {
                foreach (var kw in DocTypeKeywords)
                {
                    if (trimmed.Contains(kw))
                        return kw;
                }
            }
        }

        if (Regex.IsMatch(ocrText, @"(?i)BIÊN\s*BẢN|BÁO\s*CÁO\s*KẾT\s*QUẢ"))
            return "Biểu mẫu";

        if (ReVuPrefix.IsMatch(ocrText))
            return "Công văn";

        return null;
    }

    private bool DetectNonStandardForm(string ocrText)
    {
        return !Regex.IsMatch(ocrText, @"(?i)CỘNG\s*HÒA\s*XÃ\s*HỘI\s*CHỦ\s*NGHĨA\s*VIỆT\s*NAM")
               && !Regex.IsMatch(ocrText, @"(?i)(?:Số|SỐ)\s*[:\.]");
    }

    private void ExtractStandard(string ocrText, string[] lines, DocumentMetadata result)
    {
        var notationLineIdx = -1;
        for (int i = 0; i < lines.Length; i++)
        {
            if (ReNotation.IsMatch(lines[i]))
            {
                notationLineIdx = i;
                break;
            }
        }

        if (notationLineIdx > 0)
        {
            for (int i = Math.Min(notationLineIdx - 1, 2); i >= 0; i--)
            {
                var m = RePublishUnit.Match(lines[i]);
                if (m.Success)
                {
                    result.PublishUnit = m.Groups[1].Value.Trim();
                    break;
                }
                if (i == notationLineIdx - 1 && lines[i].Any(char.IsUpper) && lines[i].Length < 80)
                {
                    result.PublishUnit = lines[i].Trim();
                    break;
                }
            }
        }

        if (notationLineIdx >= 0)
        {
            var m = ReNotation.Match(lines[notationLineIdx]);
            if (m.Success)
                result.Notation = m.Groups[1].Value.Trim();
        }

        var dateMatch = RePublishDate.Match(ocrText);
        if (dateMatch.Success)
            result.PublishDateStr = dateMatch.Value;

        result.PublishDate = ParseDate(dateMatch.Value);

        result.Abstract = ExtractAbstract(ocrText, lines, result.DocType);

        var rcvNum = ReReceivedNumber.Match(ocrText);
        if (rcvNum.Success)
            result.Number = rcvNum.Groups[1].Value.Trim();

        var rcvDate = ReReceivedDate.Match(ocrText);
        if (rcvDate.Success)
            result.ReceivedDate = ParseDate(rcvDate.Groups[1].Value.Trim());

        var kinhGui = ReRecipientUnit.Match(ocrText);
        if (kinhGui.Success)
            result.RecipientUnit = kinhGui.Groups[1].Value.Trim();

        ExtractSigner(ocrText, lines, result);
    }

    private void ExtractNonStandard(string ocrText, string[] lines, DocumentMetadata result)
    {
        var m = RePublishUnitNonStandard.Match(ocrText);
        if (m.Success)
            result.PublishUnit = (m.Groups[1].Success ? m.Groups[1] : m.Groups[2]).Value.Trim();

        var nonStdDate = RePublishDateNonStandard.Match(ocrText);
        if (nonStdDate.Success)
        {
            result.PublishDateStr = nonStdDate.Value;
            result.PublishDate = $"{nonStdDate.Groups[1].Value.Trim()}/{nonStdDate.Groups[2].Value.Trim()}/{nonStdDate.Groups[3].Value.Trim()}";
        }

        result.Abstract = ExtractAbstract(ocrText, lines, result.DocType);

        var rcvNum = ReReceivedNumber.Match(ocrText);
        if (rcvNum.Success)
            result.Number = rcvNum.Groups[1].Value.Trim();

        var rcvDate = ReReceivedDate.Match(ocrText);
        if (rcvDate.Success)
            result.ReceivedDate = ParseDate(rcvDate.Groups[1].Value.Trim());

        ExtractSigner(ocrText, lines, result);
    }

    private string? ExtractAbstract(string ocrText, string[] lines, string? docType)
    {
        var vuMatch = ReVuPrefix.Match(ocrText);
        if (vuMatch.Success)
        {
            var afterVu = vuMatch.Groups[1].Value;
            var cutoffIdx = Math.Max(
                afterVu.IndexOf("Kính gửi", StringComparison.OrdinalIgnoreCase),
                afterVu.IndexOf("Nơi nhận", StringComparison.OrdinalIgnoreCase));
            return cutoffIdx > 0 ? afterVu[..cutoffIdx].Trim() : afterVu.Trim();
        }

        if (docType == "Công văn" || docType == null)
        {
            var kinhGiuIdx = ocrText.IndexOf("Kính gửi", StringComparison.OrdinalIgnoreCase);
            if (kinhGiuIdx < 0) kinhGiuIdx = ocrText.IndexOf("Kính", StringComparison.OrdinalIgnoreCase);

            if (kinhGiuIdx > 0)
            {
                var segment = ocrText[..kinhGiuIdx].Trim();
                if (segment.Length > 10)
                    return segment;
            }

            var paragraph = ExtractFirstParagraph(ocrText);
            if (!string.IsNullOrWhiteSpace(paragraph) && paragraph.Length > 10)
                return paragraph;
        }

        var titleIdx = -1;
        foreach (var kw in DocTypeKeywords)
        {
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains(kw))
                {
                    titleIdx = i;
                    break;
                }
            }
            if (titleIdx >= 0) break;
        }

        if (titleIdx >= 0 && titleIdx + 1 < lines.Length)
        {
            var nextLine = lines[titleIdx + 1];
            if (!string.IsNullOrWhiteSpace(nextLine) && nextLine.Length > 5 && nextLine != nextLine.ToUpper())
                return nextLine.Trim();
        }

        return null;
    }

    private void ExtractSigner(string ocrText, string[] lines, DocumentMetadata result)
    {
        for (int i = lines.Length - 1; i >= 0; i--)
        {
            var line = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(line) || line.Length > 100)
                continue;

            foreach (var kw in SignerRoleKeywords)
            {
                if (line.Contains(kw, StringComparison.OrdinalIgnoreCase))
                {
                    result.SignerRole = line;
                    if (i + 1 < lines.Length)
                    {
                        var next = lines[i + 1].Trim();
                        if (!string.IsNullOrWhiteSpace(next) && next.Length < 60)
                            result.Signer = next;
                    }
                    return;
                }
            }
        }
    }

    private string? ExtractUrgent(string ocrText)
    {
        foreach (var kw in UrgentKeywords)
        {
            if (ocrText.Contains(kw, StringComparison.OrdinalIgnoreCase))
                return kw;
        }
        return "Bình thường";
    }

    private string? ExtractFirstParagraph(string ocrText)
    {
        var paragraphs = ocrText.Split(new[] { "\n\n", "\r\n\r\n" }, StringSplitOptions.RemoveEmptyEntries);
        return paragraphs.FirstOrDefault(p => p.Trim().Split('\n').Any(l => l.Trim().Length > 20))?.Trim();
    }

    private string? ParseDate(string dateStr)
    {
        try
        {
            var m = Regex.Match(dateStr, @"(\d{1,2})\s+tháng\s+(\d{1,2})\s+năm\s+(\d{4})");
            if (m.Success)
            {
                return $"{int.Parse(m.Groups[1].Value):d2}/{int.Parse(m.Groups[2].Value):d2}/{m.Groups[3].Value}";
            }

            m = Regex.Match(dateStr, @"(\d{1,2})[\s\-\/]+(\d{1,2})[\s\-\/]+(\d{4})");
            if (m.Success)
            {
                return $"{int.Parse(m.Groups[1].Value):d2}/{int.Parse(m.Groups[2].Value):d2}/{m.Groups[3].Value}";
            }
        }
        catch { }
        return dateStr;
    }
}
