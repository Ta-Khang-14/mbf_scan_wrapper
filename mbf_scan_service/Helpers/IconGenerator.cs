using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace mbf_scan_service.Helpers;

public static class IconGenerator
{
    public static void GenerateScannerIcon(string outputPath)
    {
        using var bitmap = new Bitmap(256, 256);
        using var graphics = Graphics.FromImage(bitmap);
        
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);

        // Scanner body - rounded rectangle
        var bodyRect = new Rectangle(20, 60, 216, 160);
        using var bodyBrush = new SolidBrush(Color.FromArgb(50, 50, 50));
        using var bodyPen = new Pen(Color.FromArgb(80, 80, 80), 3);
        
        var bodyPath = CreateRoundedRectangle(bodyRect, 20);
        graphics.FillPath(bodyBrush, bodyPath);
        graphics.DrawPath(bodyPen, bodyPath);

        // Scanner lid line
        using var lidPen = new Pen(Color.FromArgb(100, 100, 100), 2);
        graphics.DrawLine(lidPen, 30, 100, 226, 100);
        graphics.DrawLine(lidPen, 30, 180, 226, 180);

        // Light/indicator
        using var lightBrush = new SolidBrush(Color.FromArgb(0, 200, 100));
        graphics.FillEllipse(lightBrush, 200, 75, 20, 20);

        // Document slot
        using var slotBrush = new SolidBrush(Color.White);
        graphics.FillRectangle(slotBrush, 50, 105, 156, 70);

        // Scan lines (document content)
        using var linePen = new Pen(Color.FromArgb(180, 180, 180), 2);
        for (int i = 0; i < 5; i++)
        {
            graphics.DrawLine(linePen, 60, 115 + i * 12, 196, 115 + i * 12);
        }

        // Base
        using var baseBrush = new SolidBrush(Color.FromArgb(40, 40, 40));
        graphics.FillRectangle(baseBrush, 40, 220, 176, 20);

        // Tray
        using var trayBrush = new SolidBrush(Color.FromArgb(60, 60, 60));
        var trayPath = CreateRoundedRectangle(new Rectangle(60, 240, 136, 16), 5);
        graphics.FillPath(trayBrush, trayPath);

        // Save as ICO
        var iconPath = Path.Combine(outputPath, "scanner.ico");
        using var iconStream = new FileStream(iconPath, FileMode.Create);
        SaveAsIcon(bitmap, iconStream);
        
        Console.WriteLine($"Icon generated: {iconPath}");
    }

    private static GraphicsPath CreateRoundedRectangle(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        int diameter = radius * 2;
        
        path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        
        return path;
    }

    private static void SaveAsIcon(Bitmap bitmap, Stream stream)
    {
        // ICO header
        stream.WriteByte(0); // Reserved
        stream.WriteByte(0);
        stream.WriteByte(1); // Type: ICO
        stream.WriteByte(0);
        stream.WriteByte(1); // Number of images
        stream.WriteByte(0);

        // ICO directory entry for 256x256
        stream.WriteByte(0); // Width (0 = 256)
        stream.WriteByte(0); // Height (0 = 256)
        stream.WriteByte(0); // Color palette
        stream.WriteByte(0); // Reserved
        stream.WriteByte(1); // Color planes
        stream.WriteByte(0);
        stream.WriteByte(32); // Bits per pixel
        stream.WriteByte(0);

        // Calculate PNG size
        using var pngStream = new MemoryStream();
        bitmap.Save(pngStream, ImageFormat.Png);
        var pngBytes = pngStream.ToArray();
        
        // Size of image data
        stream.Write(BitConverter.GetBytes(pngBytes.Length), 0, 4);
        // Offset to image data
        stream.Write(BitConverter.GetBytes(22), 0, 4);
        
        // Image data (PNG)
        stream.Write(pngBytes, 0, pngBytes.Length);
    }
}
