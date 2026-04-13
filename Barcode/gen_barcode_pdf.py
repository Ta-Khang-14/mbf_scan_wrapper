# -*- coding: utf-8 -*-
"""
Script gen PDF với barcode 1D Code-128
Tạo 2 file PDF chứa barcode với pattern "File Separate" và "Doc Separate"
"""
import sys
import io

# Set UTF-8 encoding cho stdout
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')

from reportlab.lib.pagesizes import A4
from reportlab.lib.units import mm
from reportlab.pdfgen import canvas
from reportlab.lib import colors
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
import barcode
from barcode.writer import ImageWriter
import io
import os

# Đường dẫn thư mục output
OUTPUT_DIR = os.path.dirname(os.path.abspath(__file__))

# Đăng ký font hỗ trợ Unicode/Vietnamese
# Ưu tiên Arial Unicode vì hỗ trợ tiếng Việt tốt nhất
font_priority = [
    (r"C:\Windows\Fonts\ARIALUNI.ttf", "ArialUnicode", "ArialUnicode-Bold"),
    (r"C:\Windows\Fonts\arial.ttf", "Arial", "Arial-Bold"),
]

FONT_NAME = None
FONT_BOLD_NAME = None

for font_path, font_name, font_bold_name in font_priority:
    if os.path.exists(font_path):
        try:
            pdfmetrics.registerFont(TTFont(font_name, font_path))
            pdfmetrics.registerFont(TTFont(font_bold_name, font_path))
            FONT_NAME = font_name
            FONT_BOLD_NAME = font_bold_name
            print(f"Da dang ky font: {font_path}")
            break
        except Exception as e:
            print(f"Khong the dang ky font {font_path}: {e}")

if FONT_NAME is None:
    # Fallback - sử dụng font có sẵn của reportlab
    FONT_NAME = "Helvetica"
    FONT_BOLD_NAME = "Helvetica-Bold"
    print("Su dung font mac dinh: Helvetica")

def create_pdf_with_barcode(pattern_text, description, filename):
    """
    Tạo file PDF chứa barcode Code-128 và text label
    
    Args:
        pattern_text: Text hiển thị dưới barcode
        filename: Tên file PDF output
    """
    try:
        # Kích thước trang A4
        page_width, page_height = A4
        
        # Tạo PDF canvas
        pdf_path = os.path.join(OUTPUT_DIR, filename)
        c = canvas.Canvas(pdf_path, pagesize=A4)
        
        # Tiêu đề trang
        """
        title = "Barcode"
        c.setFont("Helvetica-Bold", 18)
        c.drawCentredString(page_width / 2, page_height - 50*mm, title)
        """
        
        # Vẽ border trang
        """
        c.setStrokeColor(colors.grey)
        c.setLineWidth(0)
        c.rect(20*mm, 20*mm, page_width - 40*mm, page_height - 60*mm)
        """
        
        # Generate barcode và lưu tạm ra file
        code128 = barcode.get_barcode_class('code128')
        barcode_obj = code128(pattern_text, writer=ImageWriter())
        
        # Render barcode thành PIL Image
        pil_image = barcode_obj.render()
        
        # Tạo temp file path
        temp_barcode_path = os.path.join(OUTPUT_DIR, '_temp_barcode.png')
        
        # Lưu PIL Image ra file PNG
        pil_image.save(temp_barcode_path)
        
        # Tính toán vị trí để căn giữa barcode, đẩy lên phía trên
        barcode_width = 140*mm
        barcode_height = 60*mm
        x = (page_width - barcode_width) / 2
        y = page_height - 80*mm  # Đẩy barcode lên phía trên top
        
        # Vẽ barcode lên PDF từ file
        c.drawImage(temp_barcode_path, x, y, width=barcode_width, height=barcode_height)
        
        # Xóa file tạm
        os.remove(temp_barcode_path)
        
        # Vẽ text label bên dưới barcode
        c.setFont(FONT_BOLD_NAME, 12)
        c.setFillColor(colors.black)
        c.drawCentredString(page_width / 2, y - 10*mm, description)
        
        # Vẽ text giải thích
        c.setFont(FONT_NAME, 10)
        c.setFillColor(colors.grey)
        c.drawCentredString(page_width / 2, y - 20*mm, "Barcode Type: Code-128")
        
        # Lưu PDF
        c.save()
        
        print(f"Da tao: {pdf_path}")
        return pdf_path
        
    except Exception as e:
        print(f"Loi: {type(e).__name__}: {str(e)}")
        import traceback
        traceback.print_exc()
        return None


def main():
    """Hàm chính để tạo 2 file PDF với barcode Code-128"""
    
    print("=" * 50)
    print("Bắt đầu gen PDF với Barcode Code-128")
    print("=" * 50)
    print()
    
    # Danh sách các pattern cần tạo (pattern, mô tả, tên file)
    patterns = [
        ("File_Separate", "Barcode dùng để tách file", "FileSeparator.pdf"),
        ("Doc_Separate", "Barcode dùng để tách văn bản", "DocSeparate.pdf"),
    ]
    
    for pattern_text, description, filename in patterns:
        print(f"📄 Đang tạo PDF cho pattern: '{pattern_text}'...")
        try:
            pdf_path = create_pdf_with_barcode(pattern_text, description, filename)
            print(f"   ➡️  Output: {pdf_path}")
        except Exception as e:
            print(f"   ❌ Lỗi: {str(e)}")
        print()
    
    print("=" * 50)
    print("Hoàn thành!")
    print("=" * 50)


if __name__ == "__main__":
    main()