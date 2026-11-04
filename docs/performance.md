# Hiệu năng (giai đoạn g, cập nhật sau đợt tối ưu 2026-10-30)

Đo trên máy phát triển ngày 2026-10-30: Intel Core Ultra 7 270K Plus (24 luồng), 47 GB RAM, NVMe SSD (ổ C, D), Windows
11 10.0.26300, .NET 10.0.12 (SDK 10.0.401), màn hình 150% (144 DPI). Thời gian chỉ đo local, **không gate CI** (máy CI
khác máy này và dao động); allocation là số tất định nên được dùng làm ngưỡng test (mục "Ngưỡng allocation"). Notepad gốc
là `notepad.exe` classic 10.0.26100.8457 của Windows 11; registry của nó (`HKCU\Software\Microsoft\Notepad`)
được export trước mỗi lần đo và khôi phục sau (xóa khóa, `reg import`, export lại và so hash: giống hệt). Mọi số đo khi
máy rảnh (không build, không benchmark chạy song song): số đo trong lúc máy bận cao hơn 10-30 ms và không dùng.

## Công cụ

| Công cụ | Đo gì | Lệnh |
|---|---|---|
| `bench/NotepadNet.Benchmarks` | BenchmarkDotNet trong tiến trình, `MemoryDiagnoser`: các đường nóng của Core với dịch vụ Win32 thật (`IsTextUnicode`, `MultiByteToWideChar`, `FindNLSString`, `CreateFileW`), văn bản 50 triệu ký tự sinh lúc chạy | `dotnet run --project bench/NotepadNet.Benchmarks -c Release -- --filter "*HotPath*"` |
| `bench/NotepadNet.PerfHarness launch` | Ngoài tiến trình, cùng phương pháp cho port và gốc: từ `CreateProcess` tới cửa sổ cấp cao đầu tiên hiện (`EVENT_OBJECT_SHOW` qua `SetWinEventHook`) và tới khi `WM_GETTEXTLENGTH` của Edit bằng số ký tự mong đợi. File mẫu 1/50/500 MB (ASCII, UTF-8 có chữ Việt, UTF-16 LE có BOM) sinh lúc chạy trong `%TEMP%\npport-perf` rồi xóa. Lần đầu báo riêng, median và min của các lần sau | `dotnet run --project bench/NotepadNet.PerfHarness -c Release -- launch --exe <exe> --sizes 1,50 --kinds ascii,utf8,utf16 --runs 5 [--hive <hive settings của port>]` |
| `bench/NotepadNet.PerfHarness editload` | Ba cách đưa văn bản vào Edit control trong cùng tiến trình | `... -- editload --sizes 1,50,500 --runs 5` |
| `bench/NotepadNet.PerfHarness find` | Ngoài tiến trình, cùng phương pháp cho port và gốc: khởi động exe, `WM_SETTEXT` văn bản sinh như benchmark (50 triệu ký tự, ASCII hoặc có "tiếng Việt " mỗi 400 ký tự) vào Edit, Ctrl+F bằng `SendInput`, điền chuỗi tìm vào hộp Find (`#32770`, control 1152), bỏ Match case và Wrap around, chọn Down, rồi mỗi lần click: `EM_SETSEL(0,0)`, `WM_COMMAND` nút Find Next, đo tới khi hộp "Cannot find" hiện (`EVENT_OBJECT_SHOW`), đóng hộp. `--repeat` là số click trong một tiến trình (click 1 của port gồm chi phí JIT lần đầu) | `... -- find --exe <exe> --chars 50000000 --kinds ascii,mixed --pattern zqxj --runs 5 --repeat 3` |
| `NOTEPADNET_STARTUP_TRACE=<file>` | Dòng thời gian khởi động của chính app: mỗi mốc trong `Program`, `AppHost`, `MainForm` ghi ms tính từ lúc tiến trình được tạo, cộng số method JIT đã biên dịch (`JitInfo`), thời gian JIT và số GC; ghi ra file khi message loop rảnh lần đầu. Không đặt biến thì chỉ tốn một lần đọc môi trường | `$env:NOTEPADNET_STARTUP_TRACE = "trace.txt"; .\Notepad.NET.exe` |

Port luôn chạy với hive settings và thư mục autosave tạm (biến môi trường), nên không đụng cài đặt thật. "Lạnh" thật
(xóa file cache của hệ điều hành) cần quyền admin nên không đo; "lần đầu" trong bảng là lần đầu sau khi file được sinh.

## Ngân sách và kết quả

Ngân sách đề xuất trong kế hoạch tổng, chốt lại sau lần đo này. Số là mean của BenchmarkDotNet (job mặc định), văn bản
50 triệu ký tự.

| Thao tác | Ngân sách | Đo được | Đạt | Ghi chú |
|---|---|---|---|---|
| Detect 50 MB UTF-8 | <= 15 ms | 2,6 ms | có | `DetectUtf8`, 0 B |
| Nạp 50 MB (detect + decode vào buffer `LocalAlloc`, NUL thành dấu cách) | decode <= 50 ms | UTF-8 40,7 ms; UTF-16 35,2 ms; ANSI 36,3 ms | có | `LoadUtf8/LoadUtf16/LoadAnsi`, 80 B mỗi lần. Phần lớn là chạm trang mới của buffer 100 MB |
| Find hết 50 MB không khớp | <= 150 ms | ASCII 6,3 ms (Match case 6,3 ms); có chữ Việt mỗi 400 ký tự 27,7 ms | có | Trước: 108,6 / 49,8 / 355,2 ms, một lần gọi `FindNLSString`. Nay đường nhanh trong `NlsTextSearcher` cho cùng kết quả (mục "Find"); kết quả khớp vẫn do `FindNLSString` quyết định |
| Replace All 50 MB, 100 000 lần khớp | <= 300 ms | 41,7 ms | có | Trước 239,2 ms. `ReplaceAll`, cấp đúng một chuỗi kết quả (99,4 MB) |
| Encode + ghi 50 MB | <= 150 ms | UTF-8 20,2 ms; ANSI 62,2 ms | có | `SaveUtf8/SaveAnsi`, mở tại chỗ, BOM, `SetEndOfFile`; khoảng 150 B |
| Khởi động tới cửa sổ hiện (ấm) | <= 190 ms (chốt lại, xem "Khởi động") | port 173 ms (min 168); gốc 31 ms | có với ngân sách chốt lại; **không** với "<= 2x gốc" | Bản publish ReadyToRun framework-dependent. Sàn WinForms trên máy này: Form rỗng 100 ms, Form + MenuStrip + StatusStrip + TextBox 127 ms |

## Khởi động

`PerfHarness launch` không file, 11 lần ấm, máy rảnh, 2026-10-30 (median / min, ms; mốc là cửa sổ cấp cao đầu tiên hiện):

| Bản | Median | Min |
|---|---|---|
| Notepad gốc | 31 | 30 |
| Port, `dotnet publish -r win-x64` (ReadyToRun, framework-dependent; bản phát hành) | 173 | 168 |
| Port, publish ReadyToRun **composite** self-contained (`PublishReadyToRunComposite=true`, framework trong cùng version bubble; 229 file, 123 MB) | 151 | 148 |
| Port, bản ReadyToRun với `DOTNET_TC_QuickJit=0` | 201 | 197 |
| Port, `dotnet build` (không ReadyToRun; số đo đợt trước) | 194 | 189 |
| Sàn 1: app WinForms chỉ có một `Form` rỗng (`PerMonitorV2`, visual styles, publish ReadyToRun như port) | 100 | 97 |
| Sàn 2: `Form` + `MenuStrip` 5 menu 30 mục + `StatusStrip` 5 ô + `TextBox` dock fill, publish như trên | 127 | 121 |

Hai app sàn nằm ngoài repo (chỉ để đo, mã vài chục dòng); sàn 2 dùng đúng các control mà quyết định cố định của dự án
giữ lại (MenuStrip, StatusStrip, Edit control). Khoảng cách port - sàn 2 là 46 ms, là toàn bộ phần port có thể tác động.

Phân rã một lần khởi động ấm của bản ReadyToRun (`NOTEPADNET_STARTUP_TRACE`, ms từ lúc tạo tiến trình; JIT ở
lúc rảnh đầu tiên: 450 method, 33 ms, 0 GC):

| Mốc | Tới ms | Bước | ms |
|---|---|---|---|
| `Main` | 37 | host .NET, nạp runtime và WinForms | 37 |
| Application configured | 42 | `SetHighDpiMode`, `EnableVisualStyles` | 5 |
| Registry opened | 43 | `Win32SettingsRegistry` | 1 |
| Services registered | 48 | đăng ký DI, đọc `AssemblyMetadata`, feature module | 5 |
| Provider built | 53 | `BuildServiceProvider` | 5 |
| Settings loaded | 56 | đọc settings (resolve đầu tiên) | 4 |
| Form base constructed | 107 | constructor `Form` đầu tiên (khởi tạo WinForms; sàn 1 cũng trả giá này) | 51 |
| Status strip built | 120 | `StatusStrip` 5 ô | 13 |
| Main window handle created | 126 | `CreateHandle` | 7 |
| Edit control created | 130 | `EditorSurface` (USER32 Edit) | 4 |
| Services resolved | 136 | resolve và tạo các service, registry lệnh | 5 |
| Menu built | 151 | `MenuBuilder` dựng `MenuStrip` | 15 |
| Bar fonts read | 153 | `SystemParametersInfoForDpi` + 2 font | 2 |
| Status text origin measured | 157 | status bar ẩn, `WM_PRINTCLIENT`, so pixel | 4 |
| Menu items measured | 170 | cửa sổ dò với menu Win32 thật, `GetMenuItemRect` | 13 |
| Menu set and bars measured | 173 | layout | 4 |
| Placement, autosave registered | 176 | font, vị trí cửa sổ, `RegisterApplicationRestart` | 3 |
| Host created | 177 | tiêu đề, status bar | 1 |
| Window shown | 192 | `Show` tới khi hiện | 15 |

Đã thử, đo và không nhận (số là cửa sổ hiện, median, cùng điều kiện):

- Composite ReadyToRun self-contained: bớt 22 ms (151 so với 173) vì framework được biên dịch trước trong cùng version
  bubble (hết 450 method JIT lúc khởi động). Giá: bản phát hành 123 MB thay vì vài MB, mang runtime riêng nên phải
  publish lại khi runtime có bản vá. Không chọn làm mặc định; ai cần có thể publish bằng
  `dotnet publish src/NotepadNet.App -c Release -r win-x64 --self-contained true -p:PublishReadyToRunComposite=true`
  (sau đó, xóa `obj/Release/net10.0-windows/win-x64/R2R` trước khi publish framework-dependent lại: SDK dùng lại các
  assembly thành phần của composite và chúng fail-fast trong `coreclr.dll` khi không có ảnh composite, gặp 2026-10-30).
- `TieredCompilation.QuickJit=false`: cộng 28 ms (201) vì 450 method ngoài ReadyToRun bị biên dịch tối ưu ngay. Được
  cân nhắc vì nó xóa chi phí JIT lần đầu của Find (mục "Find"), nhưng người dùng khởi động nhiều hơn tìm.
- `TieredPGO=0`, `TieredCompilation=0` (+30), `TC_QuickJitForLoops=0` (+18), `gcConcurrent=0`, `GCgen0size`: không bớt
  hoặc chậm hơn. `ReadyToRun=0` trên bản publish: 427 ms. `ProfileOptimization` (multicore JIT): 0 ms (đợt trước).
- Bỏ DI container (đăng ký + build + resolve đầu khoảng 12-15 ms): sẽ đổi điểm thay thế service mà toàn bộ App tests
  và E2E đi qua (`AppHost.Create(configure)`), lợi khoảng 7% khởi động; không làm trong đợt này, ghi lại là cách còn lại.
- Cửa sổ dò menu (13 ms): đo riêng từng bước của `NativeBarMetrics.MenuItems` trong tiến trình rỗng thì tạo cửa sổ 2-3
  ms, `SetMenu` 1-3 ms, hủy 1-2 ms, tổng 5-8 ms; các style cửa sổ khác (`WS_POPUP|WS_CAPTION`, `WS_OVERLAPPED` không
  caption) cho cùng hình chữ nhật và chỉ nhanh hơn 1-2 ms; `WS_POPUP` trần cho số khác (46 so với 48 px). Giữ nguyên.
- Dò locale cho đường nhanh của Find (8 ms CPU) chạy bằng `Task.Run` ngay khi `NlsTextSearcher` được tạo; không thấy
  trong các mốc trên (máy 24 luồng).

Đã sửa đợt trước: `NativeBarMetrics.StatusTextOrigin` đọc pixel bằng `GetPixel` từng điểm (khoảng 18 ms ở 144 DPI); nay
đọc một lần bằng `LockBits` (4 ms).

Kết luận: runtime .NET tới `Main` và `Form` đầu tiên (khoảng 90 ms) đã gấp ba thời gian khởi động của gốc, và một app
WinForms có đúng các control của port đã mất 127 ms, nên ngân sách "<= 2x gốc" không đạt được với WinForms trên .NET
(quyết định cố định: giữ WinForms, MenuStrip/StatusStrip). Ngân sách chốt lại: **cửa sổ hiện <= 190 ms ấm trên máy này,
bản publish ReadyToRun framework-dependent, máy rảnh** (cơ sở: sàn 127 ms + 46 ms việc riêng của port đã đo từng bước +
khoảng 15 ms dao động giữa các lần đo), dùng để phát hiện thoái lui khi đo local. Cửa sổ hiện trước khi nạp file như
gốc, nên với file lớn người dùng thấy cửa sổ trống trước, như gốc.

## Nạp file vào cửa sổ

`PerfHarness launch`, median, ms, "cửa sổ / văn bản đủ". Cài đặt của gốc trên máy này: Word Wrap bật, font mặc định
của máy (Consolas 11). Hive của port được đặt cùng font và cùng Word Wrap cho mỗi dòng so sánh.

| File | Word Wrap | Gốc | Port |
|---|---|---|---|
| ASCII 1 MB | bật | 37 / 213 | 224 / 402 |
| ASCII 50 MB | bật | 36 / 6163 | 193 / 6354 |
| ASCII 1 MB | tắt | 35 / 97 | 191 / 247 |
| ASCII 50 MB | tắt | 34 / 211 | 199 / 352 |
| UTF-8 50 MB | bật | 33 / 6134 | |
| UTF-16 50 MB | bật | 37 / 3143 | |
| ASCII 500 MB | bật | 39 / 62093 | |
| ASCII 500 MB | tắt, Lucida Console 10 (hive mới) | | 194 / 1183 |

Sau khi cửa sổ hiện, port nạp nhanh ngang gốc (wrap tắt: 153 ms so với 177 ms cho 50 MB; wrap bật: khoảng 6,2 s cả
hai). Với Word Wrap, gần hết thời gian nằm trong Edit control của USER32 (tính chỗ ngắt dòng cho cả văn bản, đồng bộ trên
UI thread); port không làm nhanh hơn được bước đó và không hứa "không treo" khi nạp file lớn có Word Wrap. Bảng thêm của
port với hive mới (Word Wrap tắt, Lucida Console 10): ASCII 1 / 50 MB 246 / 341 ms, UTF-8 254 / 348 ms, UTF-16 250 /
304 ms.

## Cách nạp vào Edit control

`PerfHarness editload`: Edit style `0x50300104` (không wrap), font hệ thống mặc định của control (không `WM_SETFONT`),
`EM_LIMITTEXT(0)`; thời gian gồm decode UTF-8 và kết thúc khi `WM_GETTEXTLENGTH` trả về, median ms.

| Văn bản | `WM_SETTEXT` từ `string` | `WM_SETTEXT` từ `char[]` ghim | `EM_SETHANDLE` với `LocalAlloc` |
|---|---|---|---|
| 1 MB | 84 | 84 | 80 |
| 50 MB | 4154 | 4164 | 4124 |
| 500 MB | 57885 | 57788 | 57233 |

Ba cách gần như bằng nhau: thời gian nằm trong Edit control (font tỉ lệ nên chậm hơn nhiều so với Consolas ở bảng
trên). Giữ `EM_SETHANDLE` như gốc: nhanh nhất một chút, và văn bản được decode thẳng vào bộ nhớ mà control giữ luôn, không
có bản sao thứ hai (với 500 MB, `WM_SETTEXT` cần thêm một buffer 1 GB trong lúc control chép).

## Find

Kết quả khớp của port vẫn là của `FindNLSString(LOCALE_USER_DEFAULT, FIND_FROMSTART|FIND_FROMEND, NORM_IGNORECASE trừ
khi Match case)` như gốc (`parity.md`). Đo bằng một console tạm (2026-10-30): một lần gọi trên 50 triệu ký tự tốn khoảng
2,2 ns/ký tự khi văn bản chỉ có ASCII cho tới ký tự không ASCII đầu tiên, từ đó khoảng 7 ns/ký tự cho phần còn lại, nên
chỉ một chữ Việt ở đầu văn bản làm cả lần tìm chậm gấp ba. Vì thế có **đường nhanh** trong
`Platform/Text/NlsTextSearcher.cs`, kết quả giống hệt một lần gọi `FindNLSString` cùng cờ:

- "Ký tự đơn giản" là TAB, LF, CR và ASCII in được 0x20-0x7E (`SimpleCharProfile.SimpleChars`, một `SearchValues<char>`).
  Đường nhanh chỉ bật khi chuỗi tìm toàn ký tự đơn giản; còn lại gọi `FindNLSString` một lần như cũ.
- Các đoạn văn bản toàn ký tự đơn giản được tìm bằng `IndexOf` thứ tự (`OrdinalIgnoreCase` trừ khi Match case, vectơ
  hóa). Quanh mỗi ký tự khác, một cửa sổ nới mỗi bên `margin = 2p + 2` ký tự (p là độ dài chuỗi tìm; 3 là co chữ dài
  nhất của NLS, ví dụ "dž" trong hr-HR) được tìm bằng `FindNLSString` thật; các cụm ký tự khác cách nhau dưới
  `margin + 2` gộp thành một cửa sổ; đoạn thứ tự và cửa sổ chồng nhau p - 1 ký tự để khớp nằm ngang chỗ cắt không mất.
  Văn bản được duyệt theo khối (4096 ký tự, nhân đôi khi khối vẫn toàn ký tự đơn giản) để Replace All, gọi Find từ chỗ
  khớp trước, không thành bậc hai, và để các lần gọi vectơ hóa dài thay vì hàng nghìn lần ngắn.
- Điều kiện để chứng minh kết quả bằng nhau (trình bày đầy đủ trong doc comment của lớp): trong locale đang dùng, với ký
  tự đơn giản không có co chữ, không có ký tự bị bỏ qua, và hai ký tự đơn giản chỉ bằng nhau khi là cùng chữ khác hoa
  thường. `SimpleCharProfile.Probe()` dò các điều kiện này lúc chạy bằng chính `FindNLSString` (mọi cặp ký tự đơn giản,
  cả hai bộ cờ, hai chiều; các bộ ba chữ cái; 8 ms trên luồng nền); không thỏa thì `FastPathEnabled = false` và mọi lần
  tìm đi qua `FindNLSString`. Trên en-US (2026-10-30): không co chữ, không ký tự ASCII bị bỏ qua, chỉ bằng nhau qua hoa
  thường; BMP có 2533 ký tự bị bỏ qua (soft hyphen...), khớp không bao giờ bắt đầu ở ký tự bị bỏ qua và ký tự bị bỏ qua
  ở cuối không tính vào độ dài; nhiều ký tự không ASCII bằng chữ ASCII (ᵃ U+1D43, Kelvin U+212A) nên chúng là "ký tự
  khác" và luôn nằm trong cửa sổ `FindNLSString`.
- Đối chiếu với `FindNLSString` thật (`Platform.Tests/Text/NlsFastPathTests.cs`): 35 trường hợp biên (tiếng Việt, ß,
  soft hyphen, dấu kết hợp, İ, Σ, ᵃ, Kelvin, ZWJ, emoji, dž...), mọi khoảng cách giữa các cụm ký tự khác (0-12) với 8
  hình dạng văn bản và 13 chuỗi tìm, 6000 vòng văn bản ngẫu nhiên (tới 400 ký tự, chuỗi tìm 1-6 ký tự và chuỗi lấy từ
  chính văn bản), văn bản 2 triệu ký tự trộn, cả hai cờ và hai chiều; probe từ chối các locale giả có co chữ "ch",
  "dzs", ký tự bỏ qua `-`, hoặc `v` = `w`; đường nhanh cấp 0 byte.

BenchmarkDotNet, 50 triệu ký tự, không khớp (mean):

| Benchmark | Trước | Sau |
|---|---|---|
| `FindWithoutMatchAscii` | 108,6 ms | 6,3 ms |
| `FindWithoutMatchAsciiMatchCase` | 49,8 ms | 6,3 ms |
| `FindWithoutMatchMixed` (chữ Việt mỗi 400 ký tự) | 355,2 ms | 27,7 ms |
| `ReplaceAll` (100 000 lần khớp) | 239,2 ms | 41,7 ms |

Đầu-cuối, `PerfHarness find` (click Find Next tới hộp "Cannot find", 50 triệu ký tự, 5 tiến trình sau 1 lần bỏ, 3 click
mỗi tiến trình, median ms; gốc và port cùng phương pháp, cùng văn bản, máy rảnh, 2026-10-30):

| Văn bản | Click trong tiến trình | Gốc | Port, publish ReadyToRun (bản phát hành) | Port, composite self-contained |
|---|---|---|---|---|
| ASCII | 1 | 125 | 40 | 36 |
| ASCII | 2 | 118 | 21 | 21 |
| ASCII | 3 | 119 | 25 | 24 |
| Chữ Việt mỗi 400 ký tự | 1 | 372 | 314 | 95 |
| Chữ Việt mỗi 400 ký tự | 2 | 365 | 215 | 75 |
| Chữ Việt mỗi 400 ký tự | 3 | 366 | 40 | 78 |

Số đầu-cuối gồm cả việc hộp thoại hiện, nên lớn hơn benchmark trong tiến trình (ASCII 6 ms, trộn 28 ms) khoảng 15 ms.
Bản composite có sẵn mã biên dịch trước cho cả các instantiation generic của CoreLib nên click đầu trên văn bản trộn chỉ
95 ms, nhưng ổn định ở 75 ms (mã ReadyToRun không có PGO động) so với 40 ms của bản thường sau khi JIT xong. Harness đôi
khi (2/4 đợt chạy) hết 60 s chờ cửa sổ của một tiến trình ngay sau khi sinh văn bản; nay thông báo lỗi kèm trạng thái
tiến trình và cửa sổ, chưa tái hiện được với thông báo mới.

Lần click đầu của port trong một tiến trình chậm hơn các lần sau vì JIT phân tầng: các instantiation generic của
`SearchValues`/`IndexOfAnyAsciiSearcher` không có mã ReadyToRun trong CoreLib, chạy ở tier 0 (mã vectơ chưa tối ưu) cho
tới khi đủ 30 lần gọi và hết 100 ms trì hoãn tiering, rồi `TieredPGO` thêm một tầng đo đếm trước tier 1. Khối nhân đôi
giảm số lần gọi trên văn bản ASCII nên click đầu ASCII chỉ hơn các lần sau vài ms; văn bản có chữ Việt mỗi 400 ký tự buộc
125 000 cửa sổ ngắn nên click đầu trả khoảng 100-200 ms JIT một lần cho mỗi tiến trình (console tạm: lần đầu 120-230 ms,
sau đó 28 ms). Đã thử làm nóng trên luồng nền (10-40 vòng trên 64K ký tự, rồi nghỉ 400 ms): không ổn định vì các lần gọi
làm nóng rơi vào khoảng trì hoãn tiering nên không được đếm; `TieredPGO=0` chỉ còn 115-160 ms; `QuickJit=false` xóa hẳn
(29 ms ngay lần đầu) nhưng cộng 28 ms khởi động. Quyết định: không làm nóng, không đổi cấu hình JIT; click đầu trên văn
bản không ASCII vẫn nhanh hơn gốc.

<a id="nguong-allocation"></a>

## Ngưỡng allocation

`tests/NotepadNet.Core.Tests/Performance/AllocationTests.cs` đo `GC.GetAllocatedBytesForCurrentThread` cho một
lần gọi, lấy số nhỏ nhất của 5 lần sau 3 lần làm nóng, văn bản 1 triệu ký tự có chữ Việt. Lấy số nhỏ nhất vì JIT
(tiering, on-stack replacement) đôi khi cấp vài KB trên chính luồng gọi một lần duy nhất (đã gặp 8 KB trong khoảng
1/50 lần chạy cả bộ test); allocation tỉ lệ với văn bản thì lần nào cũng có nên vẫn bị bắt:

| Đường nóng | Ngưỡng |
|---|---|
| Detect | < 1 KiB |
| Nạp UTF-8, UTF-16 LE, UTF-16 BE vào buffer có sẵn | < 1 KiB |
| Ghi UTF-8, UTF-16 LE, UTF-16 BE (chunk từ `ArrayPool`) | < 1 KiB |
| Đếm khớp | 0 B |
| Replace All | đúng kích thước chuỗi kết quả, cộng < 1 KiB |

BenchmarkDotNet xác nhận cùng hình dạng với 50 triệu ký tự (80 B, 0 B, khoảng 150 B, 99,4 MB).

## ReadyToRun, PGO, Native AOT, trimming

- `NotepadNet.App.csproj` đặt `PublishReadyToRun=true` (áp dụng khi `dotnet publish -r win-x64`) và `TieredPGO=true`
  (mặc định của .NET 10, ghi rõ). ReadyToRun bớt khoảng 20 ms khởi động. `InvariantGlobalization=false` giữ nguyên.
  Composite ReadyToRun self-contained bớt thêm 22 ms với giá 123 MB (mục "Khởi động"), không làm mặc định.
- Native AOT, tra cứu 2026-10-30: tài liệu Microsoft "Native AOT deployment" ghi WinForms và WPF không được hỗ trợ trong
  .NET 9 và .NET 10; SDK chặn bằng `NETSDK1175` (chỉ vượt được bằng `_SuppressWinFormsTrimError`, không hỗ trợ). Epic
  dotnet/winforms#4649 (trimming/AOT cho WinForms) còn mở, milestone "Future"; dotnet/winforms#9911 (kích thước khi
  trim) còn mở ở milestone .NET 10; dotnet/winforms#10813 đề xuất feature switch. "Known trimming incompatibilities" của
  runtime ghi COM built-in không tương thích trim, mà hộp thoại file của port đi qua COM (`IFileOpenDialog`), và
  `System.Drawing`/`System.Resources` (resx) của WinForms dùng reflection. Chặn cụ thể trong port: hộp thoại COM, resx
  (`ResxStrings`), DI container (reflection khi resolve), `System.Drawing`. Native AOT còn cần Visual Studio Build Tools
  với workload C++ (máy này không có; cộng đồng báo khoảng 6-7 GB), chưa cài và không cài trong đợt này. Kết luận:
  **không dùng Native AOT, không dùng trimming** cho tới khi WinForms được hỗ trợ chính thức; khởi động được tối ưu bằng
  ReadyToRun. Thử nghiệm đợt trước (vượt chặn): link thất bại vì thiếu công cụ C++; trimming publish được (41 MB so với
  111 MB) và chạy trong lần thử ngắn, nhưng không có hỗ trợ nên không dùng.
