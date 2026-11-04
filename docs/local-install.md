# Cài Notepad.NET làm lệnh `notepad` trên máy phát triển

Ghi lại cách app được cài trên máy phát triển ngày 2026-10-30 (Windows 11), để lệnh
`notepad` mở `Notepad.NET.exe` thay cho Notepad của Windows. Đây là cấu hình của một máy, không phải trình
cài đặt: không có gì trong đó được build hay test tự động.

Ngày 2026-11-04 app đổi tên từ "Notepad Classic" thành "Notepad.NET": thư mục cài đổi từ `C:\tools\NotepadClassic` và ba
cấu hình bên dưới được trỏ sang exe mới. Settings và ghi chú do app tự chuyển ở lần chạy đầu, xem mục "Đổi tên" của
`parity.md`.

## Bản build

```
dotnet publish src/NotepadNet.App -c Release -r win-x64 -o C:\tools\Notepad.NET
```

- Framework-dependent, ReadyToRun (`PublishReadyToRun` trong `NotepadNet.App.csproj`), cần .NET 10 Desktop
  Runtime trên máy. 14 file, khoảng 1,8 MB.
- Thư mục con `C:\tools\Notepad.NET\` thay vì thẳng `C:\tools` (đã có trong PATH người dùng), để không lẫn với
  công cụ khác.
- Nếu trước đó đã publish composite ReadyToRun thì xóa `src/NotepadNet.App/obj/Release/net10.0-windows/win-x64`
  trước (xem `performance.md`), nếu không app fail-fast lúc khởi động.
- Cập nhật: đóng mọi cửa sổ của app rồi chạy lại lệnh trên (file đang chạy bị khóa).

## Vì sao không chỉ đặt `notepad.exe` vào PATH

`C:\Windows\system32` và `C:\Windows` nằm trong PATH máy, luôn đứng trước PATH người dùng, nên một `notepad.exe`
khác trong PATH không bao giờ được tìm thấy trước. Mỗi nơi gõ lệnh có cơ chế riêng, nên cấu hình theo từng nơi.

## Những gì đã đổi

| Nơi gõ `notepad` | Cơ chế | Thay đổi |
|---|---|---|
| PowerShell 7 | Hàm được ưu tiên hơn file thực thi trong PATH | Thêm vào cuối `$PROFILE` (bản sao lưu `$PROFILE.bak-notepad`): `function notepad { & 'C:\tools\Notepad.NET\Notepad.NET.exe' @args }` |
| cmd | Macro doskey nạp khi cmd khởi động | File `C:\tools\Notepad.NET\cmd-macros.doskey` chứa `notepad="C:\tools\Notepad.NET\Notepad.NET.exe" $*`; giá trị `AutoRun` (REG_EXPAND_SZ) trong `HKCU\Software\Microsoft\Command Processor` = `doskey /macrofile="C:\tools\Notepad.NET\cmd-macros.doskey"` (trước đó không có giá trị này) |
| Win+R, `ShellExecute("notepad")` | `HKCU\Software\Microsoft\Windows\CurrentVersion\App Paths\notepad.exe` | `(Default)` = `C:\tools\Notepad.NET\Notepad.NET.exe`, `Path` = `C:\tools\Notepad.NET`. Trước đó khóa trỏ tới Notepad bản Store (`Microsoft.WindowsNotepad`), bản sao lưu ở `C:\tools\Notepad.NET\apppaths-notepad-backup.reg` |

Không đổi gì trong HKLM. Khóa `Image File Execution Options\notepad.exe` (bộ lọc `FilterFullPath` chuyển
`System32\notepad.exe` sang Notepad bản Store) vẫn như cũ, nên chương trình nào gọi thẳng đường dẫn đầy đủ của
Notepad Windows vẫn mở Notepad Windows. `notepad.exe` (có đuôi) trong PowerShell vẫn gọi bản của Windows.

## Đã kiểm tra

- `pwsh`: `(Get-Command notepad).CommandType` là `Function`; cmd: `doskey /macros` in ra macro trên.
- `ShellExecute("notepad")` (cùng đường với hộp Run): trước khi đổi mở Notepad bản Store, sau khi đổi mở
  `Notepad.NET.exe`; với tham số là file có khoảng trắng, tiêu đề là `thu nghiem.txt - Notepad.NET`.
- Win+R `notepad` bấm tay: mở app (người dùng xác nhận, trước khi đổi tên).

## Chưa đổi

- Bấm đúp file `.txt` trong Explorer vẫn mở Notepad bản Store: liên kết loại file (`UserChoice`) có hash, Windows
  không cho chương trình tự ghi. Đổi bằng tay: chuột phải file `.txt`, Open with, Choose another app, duyệt tới
  `C:\tools\Notepad.NET\Notepad.NET.exe`, chọn Always.
- Khi Notepad bản Store cập nhật, nó có thể ghi lại khóa `App Paths`. Khi Win+R lại mở bản Store, chạy lại:

  ```
  reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\App Paths\notepad.exe" /ve /d "C:\tools\Notepad.NET\Notepad.NET.exe" /f
  reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\App Paths\notepad.exe" /v Path /d "C:\tools\Notepad.NET" /f
  ```

## Gỡ bỏ

```
Copy-Item "$PROFILE.bak-notepad" $PROFILE -Force
reg delete "HKCU\Software\Microsoft\Command Processor" /v AutoRun /f
reg import C:\tools\Notepad.NET\apppaths-notepad-backup.reg
```

Sau đó mới xóa `C:\tools\Notepad.NET\` (bản sao lưu `App Paths` nằm trong đó). Settings của app
(`HKCU\Software\Notepad.NET`) và thư mục autosave (`%LOCALAPPDATA%\Notepad.NET`) không bị đụng.
