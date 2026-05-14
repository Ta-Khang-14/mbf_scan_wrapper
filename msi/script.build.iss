[Setup]
; Tên ứng dụng
AppName=MBF Scan Service Demo
AppVersion=1.0.5
AppPublisher=MBF
DefaultDirName={userpf}\MBFScanServiceDemo
DefaultGroupName=MBF Scan Service Demo 
OutputBaseFilename=MBFScanDemoSetup
Compression=lzma
SolidCompression=yes

; Luôn hiển thị trang chọn thư mục cài đặt
DisableDirPage=no

; Đáp ứng per-user (không cần admin, không UAC)
PrivilegesRequired=lowest

; Icon cho installer
WizardImageFile=
WizardSmallImageFile=

[Languages]
; Name: "vietnamese"; MessagesFile: "compiler:Languages\Vietnamese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; File executable chính
Source: "D:\Repos\mbf_scan_wrapper\mbf_scan_service\bin\Release\net8.0-windows\publish\win-x86\mbf_scan_service.exe"; DestDir: "{app}"; Flags: ignoreversion

; Tất cả file trong thư mục publish
Source: "D:\Repos\mbf_scan_wrapper\mbf_scan_service\bin\Release\net8.0-windows\publish\win-x86\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
; Shortcut ở Start Menu
Name: "{group}\MBF Scan Service Demo"; Filename: "{app}\mbf_scan_service.exe"

; Shortcut trên Desktop (nếu được chọn)
Name: "{userdesktop}\MBF Scan Service Demo"; Filename: "{app}\mbf_scan_service.exe"; Tasks: desktopicon

[Registry]
; Tự khởi động cùng Windows (Startup)
Root: HKA; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; \
    ValueType: string; ValueName: "MBFScanServiceDemo"; ValueData: """{app}\mbf_scan_service.exe"""; \
    Flags: uninsdeletevalue

[Run]
; Chạy app ngay sau khi cài đặt xong
Filename: "{app}\mbf_scan_service.exe"; Description: "Khởi chạy ứng dụng ngay"; Flags: postinstall nowait

[UninstallDelete]
; Xóa thư mục logs khi gỡ cài đặt
Type: filesandordirs; Name: "{app}\logs"
Type: filesandordirs; Name: "{app}\temp"
