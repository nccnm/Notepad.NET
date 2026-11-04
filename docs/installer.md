# Trình cài đặt

`Notepad.NET-<version>-setup-x64.exe` và `-arm64.exe`, build bằng Inno Setup từ `installer/Notepad.NET.iss`. Cài cho người dùng hiện tại, không cần quyền admin, vào `%LOCALAPPDATA%\Programs\Notepad.NET`.

Máy phát triển vẫn dùng cách trong `local-install.md`. Không chạy trình cài thật trên máy đó, vì nó ghi đè đúng các cấu hình mà cách kia đã đặt.

## Luôn cài

| Nơi | Nội dung |
|---|---|
| Thư mục cài | `Notepad.NET.exe`, `LICENSE`, `NotepadCommand.ps1` |
| Start menu | Lối tắt Notepad.NET |
| `HKCU\Software\Microsoft\Windows\CurrentVersion\App Paths\Notepad.NET.exe` | Win+R `Notepad.NET` |
| `HKCU\Software\Classes\Applications\Notepad.NET.exe`, `HKCU\Software\Classes\.txt\OpenWithList\Notepad.NET.exe` | App xuất hiện trong Open with của file `.txt` |

Trình cài không đặt app làm mặc định cho `.txt`: `UserChoice` có hash và Windows không cho chương trình tự ghi, nên người dùng phải tự chọn trong Open with.

Trình cài không tạo `HKCU\Software\Notepad.NET`. Khóa đó còn thiếu là dấu hiệu lần chạy đầu của app, lúc app nhận settings của tên cũ hoặc của Notepad gốc (xem `parity.md`).

## Ô "notepad command"

Mặc định không tick, vì nó thay một lệnh của Windows. `System32` đứng trước PATH của người dùng, nên mỗi nơi gõ lệnh cần một cấu hình riêng, do `installer/NotepadCommand.ps1` đặt:

| Nơi gõ `notepad` | Cấu hình | Khi tắt hoặc gỡ |
|---|---|---|
| PowerShell 7 và Windows PowerShell | Khối có đánh dấu chứa `function notepad` ở cuối profile của mỗi shell | Chỉ bỏ khối đó, phần còn lại giữ nguyên từng byte. Profile do trình cài tạo thì bị xóa |
| cmd | `doskey notepad="..." $*` thêm vào đầu giá trị `AutoRun` của `HKCU\Software\Microsoft\Command Processor`, `AutoRun` cũ vẫn chạy sau đó | Chỉ bỏ macro; giá trị do trình cài tạo thì bị xóa |
| Win+R | `HKCU\...\App Paths\notepad.exe`; giá trị trước đó được lưu trong `HKCU\Software\Notepad.NET Setup` | Trả lại giá trị cũ, hoặc xóa khóa nếu trước đó không có. Nếu chương trình khác đã đổi khóa này, chẳng hạn Notepad bản Store khi cập nhật, thì để nguyên |

Cấu hình được tắt khi gỡ app, hoặc khi chạy lại trình cài mà bỏ tick. Nâng cấp mà vẫn tick thì giữ giá trị cũ đã lưu.

Giới hạn:

- Shell chỉ được thêm profile khi đọc được policy `RemoteSigned`, `Unrestricted` hoặc `Bypass`, vì profile không ký sẽ báo lỗi mỗi lần shell khởi động với `Restricted` hoặc `AllSigned`. Dưới `Restricted`, chính `Get-ExecutionPolicy` có thể lỗi vì không tải được module của nó, nên không đọc được policy cũng tính là không được phép. Windows PowerShell trên Windows client mặc định là `Restricted`, nên ở đó `notepad` vẫn mở Notepad của Windows cho tới khi người dùng đổi policy và chạy lại trình cài. Lý do được ghi trong `notepad-command.log` ở thư mục cài.
- Cửa sổ PowerShell và cmd đang mở chỉ nhận cấu hình mới khi mở lại.
- Chương trình gọi thẳng đường dẫn đầy đủ của `notepad.exe` vẫn mở Notepad của Windows.
- `App Paths` có tác dụng với Win+R trên Windows 11 (đã chạy `ShellExecute("notepad")` trên máy phát triển, 2026-11-04). Trên Windows Server 2025 của máy CI, cùng lệnh đó vẫn mở `System32\notepad.exe` dù khóa đã đúng, nên CI chỉ kiểm tra giá trị của khóa. Lý do khác nhau giữa hai bản Windows chưa được xác minh.

## Gỡ cài đặt

Gỡ xong còn lại `HKCU\Software\Notepad.NET` và `%LOCALAPPDATA%\Notepad.NET`. Ghi chú trong đó là bản duy nhất của văn bản nên không bị xóa; muốn bỏ hẳn thì xóa hai nơi này bằng tay.

## Build và test

```
./installer/Build-Installer.ps1 -Version 1.2.0 -Arch x64 -SourceDir publish/win-x64 -OutDir dist
```

`SourceDir` chứa `Notepad.NET.exe` bản single-file. Cần Inno Setup 6; trên CI, script tải đúng bản đã ghim và kiểm tra SHA-256.

`installer/Test-Installer.ps1` cài, nâng cấp và gỡ trình cài thật trên máy CI, gồm cả trường hợp người dùng đã có sẵn `AutoRun`, `App Paths` và profile, rồi so lại từng byte. Vì sửa cấu hình của người dùng hiện tại, script từ chối chạy ngoài CI. Bản ARM64 được build nhưng không được chạy thử, vì máy CI là x64.
