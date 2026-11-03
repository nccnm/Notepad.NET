# Parity: hành vi Notepad classic và bản port

Đối chiếu từng hành vi của Notepad classic (`notepad.exe` 10.0.26100.8457 của Windows 11, SHA-256 `468ffe12...577e`) với bản port: nơi cài đặt, test, và port giống hay cố ý khác. Đây là đặc tả hành vi của dự án; test ghi mục mình kiểm bằng `[Trait("Doc", "parity.md#<mục>")]`, `<mục>` là anchor của các tiêu đề dưới đây.

Các `(giai đoạn a)`...`(giai đoạn h)` ở tiêu đề là thứ tự các phần được làm: a encoding, nạp và lưu file; b cửa sổ, menu, lệnh; c tìm kiếm, thay thế, Go To; d cài đặt, dòng lệnh, vị trí cửa sổ, kéo thả; e in và Page Setup; f autosave và Restart Manager; g hiệu năng; h E2E và hoàn thiện.

Trạng thái:
- **giống**: cài đặt đúng hành vi của bản gốc, có test;
- **cố ý khác**: khác bản gốc có chủ đích, lý do ghi ở cột ghi chú;
- **chưa làm**: chưa cài đặt (ghi giai đoạn dự kiến);
- **bỏ**: không port (telemetry, WIL, watermark, banner Store, EDP).

Mức bằng chứng: hành vi của gốc mới chỉ suy ra chứ chưa thấy khi chạy được ghi *(suy luận)* hoặc *(chưa xác minh)* ở đầu cột ghi chú, kèm cách đã kiểm (nếu đã kiểm). Ngày ghi kèm một quan sát là ngày chạy bản gốc trên máy phát triển.

<a id="encoding-va-nap-file"></a>

## Encoding và nạp file (giai đoạn a)

| Hành vi gốc | Nơi cài đặt | Test | Trạng thái | Ghi chú |
|---|---|---|---|---|
| Mã encoding 1-6 (ANSI, UTF-16 LE/BE, UTF-8 BOM, UTF-8, GB18030), mã lạ coi là ANSI | `Core/Text/TextEncodingKind.cs` | `SmallTypesTests` | giống | Giữ đúng giá trị số để import `iDefaultEncoding` |
| BOM `EF BB BF` (cần đủ 3 byte), `FF FE`, `FE FF`; BOM bị bỏ khỏi văn bản | `EncodingDetector.Detect` | `EncodingDetectorTests`, `GoldenEncodingTests` | giống | |
| `EF BB` thiếu `BF` rơi xuống nhánh không BOM | `EncodingDetector.Detect` | `Detect_treats_EF_BB_without_BF_as_no_BOM` | giống | |
| `IsTextUnicode` (mọi phép thử); chỉ đạt STATISTICS và < 100 byte thì bỏ qua | `EncodingDetector`, `Platform/Text/Win32UnicodeHeuristic.cs` | `Statistics_only_*`, `Bush_hid_the_facts_*` | giống | Đã quan sát trên máy này (2026-10-29): `bush.txt` cho `IsTextUnicode` = TRUE, cờ = `0x2` |
| UTF-8 nghiêm ngặt trên toàn file, ASCII thuần mở là UTF-8 | `Utf8.IsValid` | `Invalid_utf8_falls_back_to_ansi`, `Managed_strict_utf8_check_matches_MultiByteToWideChar` | giống | Kiểm bằng so sánh với `MultiByteToWideChar(MB_ERR_INVALID_CHARS)` trên tập ca biên |
| Không hợp lệ thì ANSI (`CP_ACP`) | `TextDecoder` + `Win32CodePageConverter` | `Legacy_code_page_files_detect_as_ansi` | giống | |
| File rỗng mở là UTF-8 | `EncodingDetector.Detect` | `Empty_file_is_utf8`, golden `empty.txt` | giống | *(suy luận)* `MultiByteToWideChar` với 0 byte trả lỗi 87, không phải 0x459, nên gốc coi là hợp lệ |
| Bảng encoding ép buộc (`forcedEncoding`) | `EncodingDetector.Forced` | `Forced_encoding_table` | giống, trừ dòng dưới | |
| Ép UTF-16 BE cho file không BOM vẫn copy little-endian; ép LE cho file `FE FF` lại đảo byte | `TextDecoder` | `Forced_utf16_be_on_file_without_bom_is_byte_swapped` | cố ý khác | Đảo byte theo encoding đã chọn. Bản gốc ở ca thứ hai còn đọc vượt buffer 1 WCHAR |
| Byte lẻ cuối của UTF-16 bị bỏ không báo | `TextDecoder` | `Odd_trailing_byte_of_utf16_is_dropped`, golden `utf16-odd.txt` | giống | |
| UTF-8 sai khi ép UTF-8: ký tự thay thế như `MultiByteToWideChar(65001, 0)` | `TextDecoder` (UTF-8 hợp lệ: managed; sai: qua Win32) | `Utf8_decoding_matches_MultiByteToWideChar_including_malformed_input`, `Malformed_utf8_goes_through_*` | giống | Phát hiện khi viết test: .NET và Windows sinh số U+FFFD khác nhau với overlong/surrogate/> U+10FFFF |
| GB18030 = code page 54936 | `TextDecoder`, `TextEncoder` | `Gb18030_*`, `Forced_gb18030_decodes_supplementary_characters` | giống | |
| Giới hạn 1 GiB (`>= 0x40000000`) | `DocumentLoader.IsTooLarge` | `Files_of_one_gibibyte_or_more_are_too_large`, `Files_of_one_gibibyte_are_refused_with_string_8` | giống | Hộp lỗi chuỗi 8 trong `DocumentService.Load` |
| NUL thành dấu cách trừ khi `fKeepNulls` | `DocumentLoader` | `Nul_characters_become_spaces`, `Keep_nulls_*` | giống | |
| `.LOG`: đúng 4 ký tự đầu, phân biệt hoa thường, sau BOM, chỉ khi mở | `DocumentLoader` (cờ); `DocumentService.Load` + `TimeDateInserter` (chèn) | `Log_marker_*`, `Log_file_gets_a_stamp_at_the_end_with_undo`, `Log_files_get_a_stamp_when_opened` | giống | *(đã chạy thử trên gốc 2026-10-29)* |
| Buffer `(cch + 1)` WCHAR, kết thúc NUL, giao cho Edit bằng `EM_SETHANDLE` | `ITextBuffer`/`ITextBufferFactory`, `Platform/Text/LocalTextBuffer.cs`, `App/Editor/EditorSurface.LoadBuffer` | `Round_trips_text_*`, `Local_buffer_works_with_the_document_loader`, `Buffers_are_handed_over_with_EM_SETHANDLE` | cố ý khác (nhỏ) | Gốc dùng lại khối của Edit bằng `LocalReAlloc`; port cấp khối `LocalAlloc(LMEM_MOVEABLE)` mới rồi `LocalFree` khối cũ sau `EM_SETHANDLE`. Kết quả trong Edit như nhau |
| Nhận diện sơ bộ 1 KiB cho hộp thoại Open; `EF BB` không `BF` ra mặc định; < 2 byte ra mặc định | `EncodingDetector.Sniff`, `DocumentService.Open` | `Sniff_rules`, `Sniff_of_first_kibibyte_can_disagree_with_full_load`, `Open_with_auto_detect_forces_the_sniffed_encoding` | giống | Kết quả sniff thành encoding ép buộc khi người dùng để Auto-Detect |
| Sniff: ký tự UTF-8 nhiều byte bị cắt ở biên 1 KiB làm kết quả thành ANSI | `EncodingDetector.IsValidUtf8Prefix` | `Sniff_accepts_a_sequence_cut_by_the_prefix_boundary_*` | cố ý khác | *(suy luận)* Lỗi rõ ràng của việc cắt prefix. Đã chạy trên gốc (2026-10-29, File > Open với Auto-Detect, file `utf8-split-at-1k.txt`): gốc mở thành ANSI, ô status "ANSI", U+1E00 hiện thành `á¸€`; port mở UTF-8, hiện `Ḁ`. Đối chứng `utf8-invalid-after-1k.txt`: cả hai mở UTF-8 với U+FFFD ở offset 1500, nên đường Open đúng là dùng kết quả sniff |
| Map file chỉ đọc, `CreateFileW(GENERIC_READ, share R+W, OPEN_EXISTING)` | `Platform/IO/Win32FileSystem.OpenRead` | `Win32FileSystemTests` | giống | |
| Giải mã hai lần (đo rồi chuyển) với code page 8-bit | `TextDecoder` | | giống | Tối ưu ở giai đoạn g |

<a id="luu-file"></a>

## Lưu file (giai đoạn a: phần encoding và ghi; luồng Save/Save As: giai đoạn b)

| Hành vi gốc | Nơi cài đặt | Test | Trạng thái | Ghi chú |
|---|---|---|---|---|
| Ghi đè tại chỗ: `OPEN_ALWAYS`, ghi từ đầu, `SetEndOfFile`, không file tạm | `Win32FileSystem.OpenForSave` | `Save_overwrites_in_place_*`, `Save_keeps_the_file_identity` | giống | Dự án chọn "tại chỗ y hệt gốc" (2026-10-29) |
| Save chia sẻ đọc; Save As chia sẻ đọc + ghi | `OpenForSave(shareWrite)` | `Save_shares_write_only_for_save_as` | giống | |
| `ERROR_ALREADY_EXISTS` quyết định có xóa file khi lưu lỗi | `IWritableFile.AlreadyExisted`, `DocumentService.SaveFile` | `Save_reports_whether_the_file_already_existed`, `Failed_write_deletes_only_a_file_this_save_created` | giống | |
| BOM theo encoding: LE `FF FE`, BE `FE FF`, UTF-8 BOM `EF BB BF` | `TextEncoder.GetPreamble` | `Preamble_per_encoding` | giống | |
| Không kiểm kết quả ghi BOM | `DocumentService.SaveFile` | `Bom_write_failure_is_ignored` | giống | |
| UTF-16 ghi nguyên buffer; BE đảo byte | `TextEncoder.WriteBody` | `Utf16_*` | giống | Gốc đảo buffer tại chỗ rồi đảo lại; port đảo từng khúc vào buffer riêng (cùng kết quả byte) |
| ANSI dùng `GetACP()` lúc lưu với `WC_NO_BEST_FIT_CHARS`; mất ký tự thì cảnh báo chuỗi 27 | `TextEncoder.RequiresLossConfirmation`, `DocumentService.SaveFile` | `Loss_warning_rules`, `No_best_fit_reports_unmapped_characters_in_1252`, `Lossy_ansi_save_asks_and_cancel_deletes_a_new_file` | giống | Hộp OK/Cancel chuỗi 27 |
| Đồng ý thì chuyển đổi lại có best-fit | `TextEncoder.WriteBody(allowBestFit)` | `Accepted_loss_*`, `Best_fit_maps_lookalikes_*` | giống | Đã quan sát: CP1252 best-fit `Ā` -> `A` |
| `n == 0` với văn bản không rỗng cũng cảnh báo (mọi code page byte) | `TextEncoder` | `Zero_byte_result_for_non_empty_text_warns` | giống | |
| UTF-8, GB18030 không đo `usedDefault` | `TextEncoder`, `Win32CodePageConverter` | `Gb18030_ignores_unmapped_default_char_report` | giống | |
| UTF-8: lone surrogate thành U+FFFD giống `WideCharToMultiByte(CP_UTF8, 0)` | `TextEncoder.WriteUtf8` | `Managed_utf8_encoding_matches_WideCharToMultiByte` | giống | *(kiểm khi viết port)* |
| Không chuyển đổi EOL khi lưu | `TextEncoder` | | giống | |
| Nạp lại file sau khi lưu ANSI có mất ký tự (có thể chèn thêm `.LOG`) | `DocumentService.SaveFile` | `Accepted_lossy_ansi_save_uses_best_fit_and_reloads_the_file_with_another_log_stamp` | cố ý khác (nhỏ) | Port đóng handle ghi trước khi nạp lại; gốc nạp lại khi handle còn mở. Nội dung nạp lại như nhau |

<a id="cua-so-va-edit-control"></a>

## Cửa sổ và Edit control (giai đoạn b)

| Hành vi gốc | Nơi cài đặt | Test | Trạng thái | Ghi chú |
|---|---|---|---|---|
| Edit control lớp `EDIT`, style `0x50300104` (không wrap) / `0x50200104` (wrap), ID `0xF`, không `WS_EX_CLIENTEDGE` | `App/Editor/EditHost.cs` | `Edit_control_is_created_like_the_original` | cố ý khác (nhỏ) | WinForms superclass lớp hệ thống nên tên lớp thật là `WindowsForms10.EDIT.app.0...`; thủ tục cửa sổ vẫn là Edit của comctl32 v6 |
| Cấu hình sau khi tạo: `EM_LIMITTEXT(0)`, `EM_SETEXTENDEDSTYLE` (CR/LF theo `fWindowsOnlyEOL`, chuyển EOL khi dán theo `fPasteOriginalEOL`, `0x10` zoom, `0x8`), `EM_ENABLESEARCHWEB(TRUE)`, tên trợ năng "Text Editor" | `EditorSurface.Configure` | `Edit_control_is_created_like_the_original` | giống | Cờ `0x8` chưa rõ nghĩa; gửi y như gốc |
| `EM_SETHANDLE` với khối `LocalAlloc`; `EN_ERRSPACE`/`EN_MAXTEXT` lúc nạp thì báo chuỗi 8 và về tài liệu mới | `EditorSurface.LoadBuffer`, `DocumentService.Load` | `Buffers_are_handed_over_with_EM_SETHANDLE`, `Out_of_space_while_loading_fails_with_string_8_and_a_new_document` | giống | Buffer không phải `LocalAlloc` (fake của test) đi qua `WM_SETTEXT` (`Managed_buffers_go_through_WM_SETTEXT`) |
| `EN_ERRSPACE`/`EN_MAXTEXT` ngoài lúc nạp: chuỗi 7 | `EditHost`, `DocumentService` | `Out_of_space_outside_a_load_shows_string_7` | giống | |
| Đổi Word Wrap tạo lại control: giữ text, zoom, modified, ex-style, font; mất undo và vùng chọn; tắt wrap thì caret về đầu dòng 1 | `EditorSurface.TrySetWordWrap` | `Word_wrap_recreates_the_control_keeping_text_zoom_and_modified_but_not_undo`, `Word_wrap_toggles_and_marks_settings_changed` | giống | Port tạo control mới trước, đặt extended style trước khi chép text (gốc đặt sau) và chỉ hủy control cũ khi thành công |
| Tạo lại thất bại: chuỗi 15, giữ control cũ | `FormatModule` | `Word_wrap_failure_shows_string_15_and_keeps_state` | giống | |
| Bố cục tay khi `WM_SIZE` (restored/maximized): menu trên, Edit giữa, status bar dưới | `MainForm.LayoutChildren` | `Layout_places_menu_editor_and_status_parts`, `Native_resizes_lay_out_with_the_new_client_size` | giống | Kích thước lấy bằng `GetClientRect`: `WM_SIZE` đến bên trong `WM_WINDOWPOSCHANGED`, trước khi WinForms cập nhật `ClientSize`. Trước đây (phát hiện 2026-10-29 khi đổi DPI) bố cục dùng kích thước của lần đổi trước, nên sau khi đổi DPI hoặc khi cửa sổ bị đổi kích thước từ ngoài WinForms thì Edit tràn ra ngoài hoặc hụt |
| `WM_GETMINMAXINFO`: chiều rộng tối thiểu `MulDiv(230, dpi, 96)` | `MainForm.WndProc` | `Minimum_track_width_is_230_logical_pixels` | giống | |
| `WM_ACTIVATE`/`WM_SETFOCUS` đưa focus về Edit | `MainForm.WndProc` | E2E thủ công (2026-10-29) | giống | |
| Lăn chuột (Ctrl+lăn đổi zoom) cập nhật ô zoom | `EditHost`, `MainForm` | E2E thủ công (2026-10-29) | giống | |
| `WM_DPICHANGED` tạo lại font theo DPI mới | `EditorSurface.ApplyDpi`, `MainForm.WndProc` | E2E đổi scale (xem "Đổi DPI") | giống | Đã kiểm 2026-10-29 bằng cách đổi scale màn hình khi hai app đang chạy (150% sang 100%, 125%, 175%, 200% rồi về 150%): vùng Edit 0 pixel khác gốc ở mọi mức. Font menu và status của port cũng được đặt lại theo DPI mới, sau `WM_DPICHANGED_AFTERPARENT` (WinForms tự phóng font của ToolStrip ở message đó) |
| Cập nhật Ln/Col bằng `SetWinEventHook(EVENT_OBJECT_LOCATIONCHANGE)` không lọc, chỉ gửi khi đổi | `App/Shell/CaretWatcher.cs`, `StatusBarPresenter` | `Line_column_is_only_resent_when_it_changes_or_is_forced` | giống | |
| Kiểm `fMLE_is_broken` | | | bỏ | Không cần với comctl32 v6 |
| Thiếu chuỗi tài nguyên lúc khởi động thì tự lưu | | | bỏ | Chuỗi nằm trong assembly, không thể thiếu |

<a id="menu-va-phim-tat"></a>

## Menu và phím tắt (giai đoạn b)

| Hành vi gốc | Nơi cài đặt | Test | Trạng thái | Ghi chú |
|---|---|---|---|---|
| Thứ tự menu, separator, chữ có `&`, chữ phím tắt như tài nguyên MUI ("Notepad" thành "Notepad Classic" ở chỗ là tên sản phẩm) | `Features/BuiltInModules.cs`, `App/Shell/MenuBuilder.cs`, `Resources/Strings.resx` | `Menus_follow_the_original_order`, `Every_menu_and_shortcut_text_exists_in_the_resources`, `Menu_bar_matches_the_original_resource_layout`, `Menu_items_take_the_geometry_of_a_win32_menu_bar_with_the_same_texts`, `Bar_fonts_come_from_the_non_client_metrics_of_the_window_dpi` | giống | Mỗi mục rộng và cao đúng bằng mục của một menu bar Win32 thật cùng chữ, tạo ẩn ở cùng DPI (`NativeBarMetrics.MenuItems`, `GetMenuItemRect`); chữ ở `(rộng - rộng chữ) / 2`, căn giữa dọc (`MenuBarRenderer`); font `lfMenuFont` của DPI cửa sổ. Kiểu vẽ khác: xem "So sánh hình" |
| Gạch chân phím truy cập trên thanh menu chỉ khi dùng bàn phím (đang giữ Alt, hoặc vào menu bằng Alt/F10), hoặc khi Windows luôn gạch chân | `MenuBarRenderer` | `Menu_bar_underlines_access_keys_only_in_keyboard_mode` | giống | *(quan sát)* Quan sát 2026-10-29: giữ Alt thì cả hai gạch chân, Esc thì ẩn; chuyển sang bằng Alt+Tab thì gốc không gạch chân, còn MenuStrip mặc định gạch chân và giữ (theo trạng thái UI của cửa sổ). Đã sửa, đo lại hai bên như nhau |
| Hai bảng accelerator: 20 phím Global (dịch trước, cả trong hộp thoại modeless), 8 phím Main (chỉ khi đích thuộc cửa sổ chính) | `App/Shell/KeyDispatcher.cs` | `Key_bindings_match_the_accelerator_resources`, `There_are_exactly_20_global_and_8_main_bindings`, `Main_keys_only_act_on_the_main_window_global_keys_act_anywhere`, `Global_keys_act_in_the_dialog_main_keys_do_not` | giống | Thứ tự như vòng lặp gốc: GlobalAcc -> `IsDialogMessage` của hộp Find/Replace (mọi message, không chỉ phím) -> MainAcc. Không có banner Store nên không có `IsDialogMessage` thứ hai |
| Trạng thái menu tính lúc mở menu (`UpdateMenuState`): vùng chọn rỗng xám Cut/Copy/Delete/Search with Bing; Paste theo `CF_TEXT`; text rỗng xám Find/Find Next/Find Previous; wrap bật xám Go To; Undo theo `EM_CANUNDO`; Replace/Select All/Time-Date luôn bật | `ICommand.CanExecute`, `MenuBuilder.UpdateState` | `Empty_selection_grays_cut_copy_delete_and_search_with_bing`, `Paste_follows_the_clipboard_undo_follows_can_undo`, `Empty_text_grays_find_commands_word_wrap_grays_go_to`, `Replace_select_all_and_time_date_are_never_grayed`, `Menu_state_follows_the_rules` | giống | |
| Phím của mục bị xám không chạy lệnh | `KeyDispatcher` | `Keys_of_disabled_commands_are_swallowed_without_running` | giống | *(suy luận: `TranslateAccelerator` không gửi `WM_COMMAND` cho mục xám)* Port tính lại `CanExecute` mỗi lần nhấn phím; gốc dựa vào trạng thái menu lần cuối được tính. Đã quan sát trên gốc (2026-10-29): bật Word Wrap qua menu rồi nhấn Ctrl+G (phím thật qua `keybd_event`) thì không có hộp Go To; port cũng vậy |
| Dấu tick Word Wrap, Status Bar | `MenuContribution.IsChecked` | `Check_marks_follow_word_wrap_and_status_bar` | giống | |
| Cut/Copy/Paste/Delete chỉ chạy khi focus ở Edit hoặc cửa sổ chính; Cut/Copy/Delete bỏ qua vùng chọn rỗng | `Contribution.WhenFocused`, `Contribution.OnSelection` | `Clipboard_commands_act_only_with_focus_in_editor_or_main_window`, `Cut_copy_delete_skip_an_empty_selection_even_when_run_directly` | giống | |

<a id="lenh"></a>

## Lệnh (giai đoạn b)

| Hành vi gốc | Nơi cài đặt | Test | Trạng thái | Ghi chú |
|---|---|---|---|---|
| New: CheckSave, về Untitled, encoding mặc định, giữ zoom/font/wrap | `DocumentService.New` | `New_returns_to_untitled_with_the_default_encoding_keeping_zoom_and_wrap`, `New_always_asks_but_only_a_requested_question_can_cancel` | giống | |
| New Window: chạy lại exe không tham số | `Win32Shell.StartNewWindow` | `File_commands_reach_the_document_service_shell_and_window` | giống | |
| Open: CheckSave, hộp thoại, Auto-Detect thì sniff 1 KiB, lỗi nạp thì khôi phục encoding và báo lỗi | `DocumentService.Open` | `Open_with_auto_detect_forces_the_sniffed_encoding`, `Open_failure_restores_the_encoding_and_shows_the_file_error`, `Open_is_abandoned_when_save_changes_is_cancelled_or_the_dialog_is_closed` | giống | |
| Lỗi file: 5 thành chuỗi 26 + tên; mã khác thành thông báo hệ thống (`FormatMessageW`); không có thì chuỗi 1 | `DocumentService.ShowFileError`, `Win32SystemErrorMessages` | `File_errors_use_string_26_the_system_message_or_string_1`, `System_messages_come_from_FormatMessage` | giống | |
| Save: có tên thì lưu thẳng (chia sẻ đọc), thất bại im lặng rơi xuống Save As | `DocumentService.Save` | `Save_of_a_named_file_writes_with_the_current_encoding_and_read_sharing_only`, `Failed_save_falls_through_to_save_as_without_an_error`, `Untitled_save_becomes_save_as` | giống | |
| Save As: gợi ý `*.txt` (Untitled) hoặc tên hiện tại; encoding chọn sẵn = hiện tại, không có thì mặc định; lỗi báo bằng hộp lỗi file | `DocumentService.SaveAs` | `Save_as_preselects_current_or_default_encoding`, `Save_as_failure_shows_the_file_error`, `Save_as_dialog_sets_the_dialog_open_flag_while_shown` | giống | Cờ "đang mở hộp thoại lưu" chặn `WM_QUERYENDSESSION` (giai đoạn f) |
| Wrap bật: vùng chọn được cất, đặt `(0,0)` lúc ghi rồi khôi phục (cả khi lỗi) | `DocumentService.SaveFile` | `Word_wrap_selection_is_parked_during_the_write_and_restored`, `Word_wrap_selection_is_restored_after_a_failed_save_too` | giống | |
| Cảnh báo mất ký tự bị Cancel | `DocumentService.SaveFile` | `CheckSave_save_as_loops_without_error_when_the_loss_warning_is_cancelled` | cố ý khác (nhỏ) | Gốc trả lỗi chung nên vòng Save As có thể hiện hộp lỗi; port coi Cancel là người dùng hủy, không báo lỗi |
| CheckSave: hỏi chuỗi 3001 với `Untitled` hoặc đường dẫn đầy đủ; Save / Don't Save / Cancel; vòng Save As báo lỗi rồi lặp | `DocumentService.CheckSave` | `CheckSave_*` (7 test) | giống | Lưu thẳng thành công thì port cũng cập nhật tiêu đề (gốc không; tiêu đề thường bị thay ngay sau đó) |
| Lỗi hộp thoại file khác Cancel | `DocumentService.ReportDialogFailure` | `Dialog_failure_other_than_cancel_shows_string_10` | cố ý khác | Gốc rơi về hộp thoại cũ `GetOpenFileNameW`; port không có hộp thoại cũ dự phòng: E_OUTOFMEMORY hiện chuỗi 7, lỗi khác hiện chuỗi 10 |
| Đóng cửa sổ: CheckSave, Cancel giữ cửa sổ | `MainForm.OnFormClosing`, `AppHost` | `Closing_asks_to_save_and_cancel_keeps_the_window` | giống | |
| Undo `EM_UNDO`; Select All `EM_SETSEL(0,-1)` + `EM_SCROLLCARET`; Time/Date | `EditModule` | `Undo_select_all_and_time_date` | giống | |
| Time/Date: `<giờ> <ngày>` (`GetTimeFormatEx` không giây, `GetDateFormatEx` `DATE_SHORTDATE` + cờ thứ tự đọc RTL); `.LOG` thêm `\r\n` hai đầu | `TimeDateInserter`, `DateFlagRules`, `Win32DateTimeFormatter` | `Time_comes_before_date_and_log_adds_crlf_on_both_sides`, `Date_reading_order_flags`, `Short_time_has_no_seconds_and_short_date_has_the_year`, `Date_flags_follow_the_rtl_rules` | giống | |
| Search with Bing: `EM_SEARCHWEB` (0x150F) | `EditModule.SearchWithBing`, `EditorSurface.SearchWeb` | `Search_with_bing_uses_the_edit_control_and_opens_bing_only_when_it_does_not_search` | cố ý khác (nhỏ) | Gốc `PostMessage`; port `SendMessage` để biết kết quả. Đã quan sát trên máy này (2026-10-29): control trả 1 sau khi tìm vùng chọn khác rỗng, 0 khi vùng chọn rỗng, và báo `EN_SEARCHWEB` (-1520) qua `WM_NOTIFY`; `EM_ENABLESEARCHWEB(FALSE)` không chặn `EM_SEARCHWEB`. Trả 0 với vùng chọn khác rỗng thì port mở `https://www.bing.com/search?q=<vùng chọn>` |
| Find, Find Next, Find Previous, Replace, Go To | `SearchModule` | xem "Tìm kiếm, thay thế, Go To" | giống | Giai đoạn c |
| Page Setup, Print | `FileModule`, `PrintService` | `File_commands_reach_the_document_service_shell_and_window`, `Menu_print_opens_the_print_dialog_and_page_setup_stores_the_result` | giống | Luôn bật; xem "In và Page Setup (giai đoạn e)" |
| Font: `ChooseFontW` cờ `0x01010041` dưới `DPI_AWARENESS_CONTEXT_SYSTEM_AWARE`; chỉ giữ cỡ mới khi tạo được font; luôn đánh dấu font đã đổi | `FormatModule`, `Win32DialogService.ChooseFont` | `Font_keeps_the_new_size_only_when_created_but_always_marks_changed`, `Font_cancel_changes_nothing`, `Font_spec_round_trips_through_LOGFONT`, `Long_face_names_are_cut_to_31_characters` | giống | E2E 2026-10-29: hộp Font mở với Lucida Console, Regular, 10 |
| Zoom: `num*100/den` (0 thành 100), bước 10, kẹp 10-500, Restore 100 | `ZoomPolicy`, `ViewModule` | `Zoom_percent`, `Zoom_steps_are_clamped`, `Zoom_commands_step_clamp_and_update_the_status_bar` | cố ý khác (nhỏ) | Mức dưới 10% (chỉ đạt được bằng Ctrl+lăn) được kẹp về 10 khi bấm Zoom In/Out |
| Status Bar bật/tắt | `ViewModule`, `MainForm.SetStatusBarVisible` | `Status_bar_toggles_visibility` | giống | |
| View Help, Send Feedback | `HelpModule`, `AppOptions` | `Help_links_are_hidden_and_disabled_without_a_url` | cố ý khác | Dự án chọn (2026-10-29): URL đọc từ `AssemblyMetadata` `HelpUrl`/`FeedbackUrl`; trống thì ẩn mục menu và F1 không làm gì |
| About: `ShellAboutW` với icon ứng dụng | `Win32DialogService.ShowAbout` | E2E thủ công (2026-10-29) | giống | Tiêu đề "About Notepad Classic"; icon tự vẽ (`assets/icon.svg`), resource 32512 |

<a id="tim-kiem"></a>

## Tìm kiếm, thay thế, Go To (giai đoạn c)

Test Core: `Search/FindEngineTests.cs` (18), `Search/SearchServiceTests.cs`, `Editing/GoToTests.cs`; Platform: `Text/SearchApiTests.cs`, `Services/GoToDialogTests.cs`; App: `SearchShellTests.cs`.

| Hành vi gốc | Nơi cài đặt | Test | Trạng thái | Ghi chú |
|---|---|---|---|---|
| Hộp `FindTextW`/`ReplaceTextW` modeless, `FR_HIDEWHOLEWORD`, `FR_SHOWWRAPAROUND`, buffer 128 WCHAR (127 ký tự) sống suốt app, báo về qua `FINDMSGSTRING` | `Platform/Services/Win32FindReplaceDialog.cs`, `MainForm.WndProc` | `Find_dialog_takes_tab_enter_and_escape_through_is_dialog_message`, `Patterns_from_the_dialog_are_cut_to_127_characters` | giống | Dự án chọn native, không hộp dự phòng (2026-10-29) |
| Find mở với `FR_DOWN` theo `fReverse`, Match case, Wrap around lần trước; Replace luôn `FR_DOWN` | `SearchService.OpenFind/OpenReplace` | `Find_opens_with_the_settings_of_last_time`, `Replace_always_searches_down_and_brings_the_replacement` | giống | |
| Mở Find/Replace thì `WM_CLOSE` hộp đang mở trước (hộp báo `FR_DIALOGTERM` đồng bộ) | `SearchService`, `Win32FindReplaceDialog.Close` | `Opening_a_dialog_closes_the_open_one_first`, `Global_keys_act_in_the_dialog_main_keys_do_not` | giống | |
| Điền sẵn tối đa 127 ký tự đầu của vùng chọn khác rỗng; vùng chọn rỗng giữ chuỗi cũ | `SearchService.PrefillFromSelection` | `The_selection_prefills_at_most_127_characters`, `A_short_selection_prefills_and_an_empty_one_keeps_the_old_pattern` | giống | |
| F3 / Shift+F3: tìm xuôi / ngược bất kể hướng của hộp, chỉ khi Find/Replace đã mở trong phiên và chuỗi khác rỗng, không con trỏ chờ; ngược lại mở hộp Find | `SearchService.FindNextFromKey`, `NotepadSettings.SearchChanged` | `F3_searches_down_and_shift_F3_up_whatever_the_dialog_direction`, `Find_next_opens_the_find_dialog_until_find_has_been_used_with_a_pattern`, `F3_before_find_was_used_opens_the_find_dialog` | giống | Chuỗi tìm nạp từ registry (giai đoạn d) chưa đủ để F3 tìm ngay, như gốc |
| Biên tìm: chọn tất cả coi `e = 0`; xuôi `[e, len)`, wrap `[0, e + (e - s))`; ngược `[0, L)` với `L = e - 1` khi có vùng chọn (ngược lại `e`), wrap `[L - (e - s), len)` | `Core/Search/FindEngine.Locate` | `FindEngineTests` | giống | |
| Biên wrap vượt ra ngoài văn bản | `FindEngine.Locate` | `Down_wrap_range_is_clamped_to_the_text` | cố ý khác | *(suy luận)* Gốc đọc qua NUL cuối (xuôi) hoặc bắt đầu ở -1 (ngược, `s = 0`); port kẹp vào `[0, len]`. Kết quả như nhau khi biên không vượt |
| So khớp `FindNLSString(LOCALE_USER_DEFAULT, FIND_FROMSTART/FIND_FROMEND, NORM_IGNORECASE trừ khi Match case)`; `cchFound` quyết định độ dài vùng chọn | `Platform/Text/NlsTextSearcher.cs` | `SearchApiTests`, `NlsFastPathTests` | giống | Gốc truyền -1 cho chuỗi tìm, port truyền độ dài (như nhau vì không có NUL). Port có đường nhanh (tìm thứ tự trên các đoạn chỉ gồm ASCII in được, `FindNLSString` thật trên các cửa sổ quanh ký tự khác) cho kết quả giống hệt một lần gọi `FindNLSString`, được chứng minh và đối chiếu trong `NlsFastPathTests` (xem `performance.md`). Quan sát 2026-10-29, locale en-US: bỏ hoa/thường không bỏ dấu tiếng Việt và không bỏ độ rộng (fullwidth); `e` + U+0301 khớp `é` (độ dài 2); `ß` khớp `SS` (độ dài khớp 6 với chuỗi tìm 7 ký tự); soft hyphen trong đoạn khớp được tính vào độ dài |
| Thấy: `EM_SETSEL` + `EM_SCROLLCARET`; ô status 0 = chuỗi 55 " Found next from the top" (xuôi qua wrap) / 54 " Found next from the bottom" (ngược qua wrap) / rỗng | `SearchService.FindNext` | `Wrapping_shows_where_the_search_came_from_in_status_part_0` | giống | Đã quan sát trên gốc chạy thật (2026-10-29, đọc `SB_GETTEXTW` qua `ReadProcessMemory`) |
| Không thấy: MessageBox chuỗi 6 `Cannot find "%%"`, `MB_ICONINFORMATION`, chủ là hộp Find nếu đang mở; ô status giữ nguyên | `SearchService.FindNext`, `Win32FindReplaceDialog.ShowNotFound` | `Not_found_shows_cannot_find_and_leaves_the_status_bar_and_selection`, `Not_found_from_the_dialog_is_owned_by_the_dialog_and_reported_back` | giống | Đã quan sát trên cả hai (2026-10-29): hộp có owner là hộp Find |
| `FINDMSGSTRING`: mọi thông báo lưu hướng/Match case/Wrap/chuỗi trước; `FR_FINDNEXT` tìm theo `fReverse` (con trỏ chờ); `FR_REPLACE` thay nếu khớp rồi tìm tiếp; `FR_REPLACEALL`; `FR_DIALOGTERM` xóa ô status 0; kết quả gửi lại bằng `0x464` (bit 8/0x10/0x20) | `SearchService.OnDialogRequest`, `Win32FindReplaceDialog.HandleOwnerMessage` | `Every_notification_stores_the_dialog_state_first`, `Closing_the_dialog_clears_status_part_0_and_the_find_dialog_keeps_the_replacement`, `Find_dialog_takes_tab_enter_and_escape_through_is_dialog_message` | giống | Thông báo cho trình đọc màn hình do chính comdlg32 làm khi nhận `0x464`. Đã quan sát trên port chạy thật (2026-10-29): control `0x440` của hộp Find có chữ "Found the next occurrence of text" sau khi tìm thấy, "Unable to find the next occurrence of text" sau khi đóng hộp "Cannot find" |
| Bit `FR_REPLACEALL` còn nằm trong `FINDREPLACE.Flags` sau Replace All, nên F3 lúc đó không scroll và không báo "Cannot find" | `SearchService` | | cố ý khác | *(suy luận)* Port chỉ coi là Replace All trong lúc đang thay |
| Replace: chỉ thay khi vùng chọn đúng là một đoạn khớp (`EM_REPLACESEL(TRUE)`, chọn đoạn mới, scroll), rồi tìm tiếp; lần bấm đầu thường chỉ tìm; chuỗi thay rỗng xóa đoạn khớp | `SearchService.ReplaceSelectionIfMatch` | `First_replace_only_finds_the_second_replaces_and_finds_the_next`, `Replacing_the_last_match_reports_replaced_and_cannot_find`, `Selection_matches_only_when_it_is_exactly_one_match` | giống | |
| Replace All: ép hướng xuôi và lưu lại, con trỏ chờ, đếm khớp không chồng, dựng buffer O(n), một `EM_REPLACESEL(TRUE)` (một lần undo), về `(0,0)`, scroll, cập nhật Ln/Col; không khớp thì không báo | `SearchService.ReplaceAll`, `FindEngine.BuildReplaceAll` | `Replace_all_is_one_undoable_replacement_from_the_start`, `Replace_all_without_a_match_changes_nothing_and_says_nothing`, `Replace_all_is_undone_in_one_step_on_the_real_control` | giống | |
| Replace All có đúng 1 lần khớp đi qua vòng "thay rồi FindNext"; bật Wrap và chuỗi thay chứa chuỗi tìm (`a` -> `aa`) thì vòng không dừng | `SearchService.ReplaceAll` | `Replace_all_of_a_single_match_whose_replacement_contains_it_ends` | cố ý khác | Đã chạy trên gốc (2026-10-29, văn bản `xay`, tìm `a`, thay `aa`, `BM_CLICK` Replace All): tắt Wrap ra `xaay`; bật Wrap thì cửa sổ ngừng phản hồi (`IsHungAppWindow` TRUE sau 5 s, vẫn treo ở 14 s, phải kill). Port cùng thao tác ra `xaay` ngay cả khi bật Wrap. Port dùng nhánh dựng buffer cho mọi n >= 1: cùng văn bản, cùng một lần undo. Khác nhỏ: vòng của gốc để ô status 0 rỗng, port giữ nguyên. Kết quả dài hơn giới hạn chuỗi .NET thì báo chuỗi 7 (gốc rơi về vòng lặp) |
| `FindTextW` trả NULL thì dùng bản sao hộp thoại riêng | `SearchService` | `A_dialog_that_cannot_open_reports_not_enough_memory` | bỏ | Dự án chọn (2026-10-29): port báo chuỗi 7 |
| `WM_APPCOMMAND` `APPCOMMAND_BROWSER_SEARCH` chạy Find, không qua quy tắc xám (cả khi văn bản rỗng) | `MainForm.WndProc`, `AppHost` | `Browser_search_app_command_opens_find_even_on_empty_text` | giống | Lệnh app khác vào `DefWindowProc`; không chép chỗ rơi vào nhánh `WM_COMMAND` |
| Go To (dialog 14): modal, mở với `EM_LINEFROMCHAR(selStart) + 1`; OK đọc tối đa 99 ký tự, `FoldStringW(MAP_FOLDDIGITS)`, `_wtol`; hợp lệ khi `n != 0` và `EM_LINEINDEX(n - 1) != -1`; nhảy `EM_FILELINEINDEX(n - 1)` + scroll | `Core/Editing/GoToService.cs`, `Platform/Services/Win32GoToDialog.cs` | `GoToTests`, `Real_dialog_shows_the_initial_line_rejects_letters_and_reports_errors`, `Go_to_jumps_on_the_real_edit_control` | giống | Template DLGTEMPLATEEX dựng trong code (`DialogTemplateBuilder`), bố cục DLU trùng dialog 14 (dự án chọn 2026-10-29); vòng modal native nên phím tắt của app không chạy trong hộp, như gốc. Gốc gập chữ số tại chỗ, port dùng hai buffer (cùng kết quả) |
| Nhập sai: MessageBox chuỗi 31, tiêu đề chuỗi 30, `MB_OK` không icon, chủ là hộp Go To; ô nhập thành `EM_GETLINECOUNT`; hộp vẫn mở | `Win32GoToDialog` | `Invalid_lines_show_the_error_and_put_the_line_count_in_the_box`, `Real_dialog_shows_the_initial_line_rejects_letters_and_reports_errors` | giống | Tiêu đề "Notepad Classic - Goto Line". Đã chạy trên gốc và port (2026-10-29, văn bản 4 dòng, đặt chữ bằng `WM_SETTEXT` rồi bấm Go To): `-1`, `-2`, `0`, `5`, `99999999999` báo lỗi rồi ô thành `4`; `٢` (Ả Rập-Ấn) nhảy dòng 2; `３` (fullwidth), ` 3`, `3x` nhảy dòng 3. Hai bên như nhau |
| Ô Line number có `ES_NUMBER`: gõ chữ và `-` bị từ chối (balloon), chữ số của hệ chữ khác vẫn nhận | template | `Real_dialog_shows_the_initial_line_rejects_letters_and_reports_errors` | giống | Đã quan sát trên cả hai (2026-10-29): gõ `a-5٣x` thành `5٣`, có balloon |
| Hai hệ dòng: kiểm tra theo dòng hiển thị, nhảy theo dòng vật lý | `GoToService` | `Check_uses_displayed_lines_and_the_jump_physical_ones`, `Go_to_checks_displayed_lines_and_jumps_to_physical_ones_on_the_real_control` | giống | Chỉ lệch khi Word Wrap bật, mà khi đó Go To xám và Ctrl+G không làm gì (đã quan sát trên gốc) |
| Vị trí hộp Find/Replace/Go To theo gốc vùng client của cửa sổ chủ (template không `DS_CENTER`) | `Platform/Services/DialogPlacement.cs`, hook `FR_ENABLEHOOK` | so hình bên dưới | giống | *(quan sát khi so hình)* MenuStrip nằm trong vùng client (menu Win32 của gốc ở non-client) nên port dời hộp xuống đúng chiều cao menu lúc `WM_INITDIALOG`, trước khi hiện, giữ trong work area. Find/Replace của port vì thế có thêm cờ `FR_ENABLEHOOK` |

Hiệu năng (đo 2026-10-29, cùng máy, Release, văn bản 48,9 triệu ký tự ASCII nạp bằng `WM_SETTEXT`, 100 000 lần khớp, thời gian `SendMessage(BM_CLICK)` từ tiến trình khác, vài lần chạy):

| Thao tác | Notepad gốc | Port |
|---|---|---|
| Find Next từ đầu tới khớp duy nhất ở cuối | 54-64 ms | 55-67 ms |
| Replace All 100 000 lần | 253 ms | 265 ms |
| Undo sau Replace All | 155 ms | 160 ms |

<a id="tieu-de-status-bar"></a>

## Tiêu đề, status bar, cài đặt (giai đoạn b)

| Hành vi gốc | Nơi cài đặt | Test | Trạng thái | Ghi chú |
|---|---|---|---|---|
| Tiêu đề `%1%2 - Notepad`, `*` khi dirty, chỉ ghi lại khi `*` đổi | `DocumentService.UpdateTitle` | `Title_shows_star_when_dirty_and_is_only_rewritten_when_the_star_changes` | giống | Tên sản phẩm "Notepad Classic" |
| Untitled rỗng không bao giờ dirty; còn lại theo `EM_GETMODIFY` | `DocumentService.IsDirty` | `Untitled_empty_document_is_never_dirty_even_when_modified`, `Named_empty_document_follows_the_modified_flag` | giống | |
| Tên file trên tiêu đề: tên đúng hoa thường từ `FindFirstFileW`, `GetFileTitleW` chỉ khi comdlg32 đã nạp | `Win32FileTitleProvider` | `Title_of_a_saved_file_uses_the_file_title_provider`, `Missing_file_title_comes_from_the_path` | cố ý khác | Dự án chọn (2026-10-29): luôn qua `GetFileTitleW` (ẩn đuôi theo cài đặt Explorer) |
| 5 ô status bar, mép phải 120/120/50/140 từ phải theo DPI, kẹp 0; chuỗi `"  Ln %d, Col %d"`, `" %d%%"`, tên EOL, tên encoding (6 thành GB18030, mã lạ thành ANSI) | `StatusBarPresenter`, `StatusBarLayout`, `MainForm` | `Status_bar_texts`, `End_of_line_names`, `Encoding_names`, `Status_bar_part_edges`, `Status_bar_part_widths`, `Message_part_is_free_text`, `Status_text_origin_is_measured_on_a_real_status_bar_and_the_gripper_shows` | giống | StatusStrip thay `msctls_statusbar32` (`CreateStatusWindowW(0x44800000)`; comctl32 bỏ `WS_BORDER`, cửa sổ thật có style `0x54000000`). Chữ đặt ở vị trí comctl32 vẽ, đo lúc chạy trên một status bar thật ẩn (`NativeBarMetrics.StatusTextOrigin`, `WM_PRINTCLIENT`), font `lfStatusFont` của DPI cửa sổ. Size grip theme (`Status`/`SP_GRIPPER`) như gốc: dù không có `SBARS_SIZEGRIP`, gốc vẫn vẽ vì cửa sổ cha đổi được kích thước. Kiểu vạch chia khác (xem "So sánh hình") |
| Mặc định cài đặt (font `Lucida Console` 10 pt, `fWrap` 0, `StatusBar` 1, `iDefaultEncoding` 5, `fWindowsOnlyEOL`/`fPasteOriginalEOL` 0) | `NotepadSettings`, `InMemorySettingsStore` | `Settings_defaults_match_the_registry_table` | giống | Đọc/ghi registry: xem "Cài đặt, dòng lệnh, vị trí cửa sổ, kéo thả (giai đoạn d)" |
| Chiều cao font `-MulDiv(pt10, dpi, 720)` | `FontSpec.HeightForDpi`, `Win32Math.MulDiv` | `Font_height_for_dpi`, `MulDiv_rounds_half_away_from_zero`, `Managed_MulDiv_matches_kernel32` | giống | |
| `NpMessageBox`: thay `%` đầu tiên, cắt 512 ký tự | `MessageText.InsertArgument` | `Insert_argument_replaces_the_first_placeholder`, `Insert_argument_cuts_inputs_to_512_characters` | giống | |

<a id="cai-dat-dong-lenh"></a>

## Cài đặt, dòng lệnh, vị trí cửa sổ, kéo thả (giai đoạn d)

Test Core: `Settings/RegistrySettingsStoreTests.cs`, `CommandLine/CommandLineTests.cs`, `CommandLine/StartupCoordinatorTests.cs`; Platform: `Settings/SettingsRegistryTests.cs` (hive tạm `RegLoadAppKey`), `Services/CommandLineParityTests.cs`; App: `StartupShellTests.cs`. Không test nào chạm `HKCU\Software\NotepadClassic` hay `HKCU\Software\Microsoft\Notepad` thật: app nhận store qua DI (test dùng bộ nhớ), Platform dùng hive tạm, và exe đọc biến môi trường `NOTEPADCLASSIC_SETTINGS_HIVE` (đường dẫn file hive) để chạy E2E trên hive tạm.

<a id="registry"></a>

### Registry

| Hành vi gốc | Nơi cài đặt | Test | Trạng thái | Ghi chú |
|---|---|---|---|---|
| Khóa `HKCU\Software\Microsoft\Notepad`, font mặc định cho cả máy ở `HKLM\Software\Microsoft\Notepad\DefaultFonts` | `Core/Settings/RegistrySettingsStore.cs`, `Platform/Settings/Win32SettingsRegistry.cs` | `Settings_round_trip_through_the_hive` | cố ý khác | Đã chốt: khóa riêng `HKCU\Software\NotepadClassic`, cùng tên giá trị, kiểu, mặc định; font máy ở `HKLM\Software\NotepadClassic\DefaultFonts`. Khóa của gốc chỉ được đọc (import) |
| Mọi giá trị và mặc định của bảng 4.1; DWORD đúng kiểu `REG_DWORD` và vừa 4 byte, chuỗi đúng `REG_SZ` và vừa buffer (32/128/40 WCHAR), sai thì lấy mặc định; trường BYTE của `LOGFONT` cắt còn byte thấp | `RegistrySettingsStore.Load` | `Every_value_is_read_from_the_user_key`, `Wrongly_typed_values_take_the_default`, `Kinds_and_sizes_are_checked_like_RegQueryValueExW`, `A_string_longer_than_the_buffer_is_ignored` | giống | Đọc bằng `RegQueryValueExW` thật vào buffer cùng cỡ |
| `RegReadString` không thêm NUL khi chuỗi dài đúng `cchMax` | `RegistrySettingsStore.TrimRegistryString` | `Kinds_and_sizes_are_checked_like_RegQueryValueExW`, `Registry_strings_stop_at_nul_and_fit_the_buffer` | giống | *(chưa xác minh)* Câu hỏi mở đã trả lời (quan sát 2026-10-30 trên hive tạm): dữ liệu `REG_SZ` đúng `cchMax` WCHAR không có NUL làm `RegQueryValueExW` trả `ERROR_MORE_DATA`, nên gốc lấy mặc định và buffer không bao giờ thiếu NUL. Port cũng lấy mặc định; thêm cắt `cchMax - 1` làm chốt chặn |
| Mặc định chuỗi rỗng cho `searchString`, `replaceString`; mất `szHeader`/`szTrailer` mặc định khi registry chưa có | `SettingsDefaults` | `Empty_registry_gives_the_defaults_of_the_table` | cố ý khác | Đã kiểm trên gốc (2026-10-30, `HKCU\Software\Microsoft\Notepad` không có `szHeader`/`szTrailer`): bản in PDF không có header lẫn footer, hộp Page Setup hiện hai ô trống. Port dùng `&f` / `Page &p` (chuỗi 18, 19) theo "Quyết định còn lại" 3 |
| Font mặc định HKLM chỉ dùng khi HKCU không có; không có thì `Lucida Console` 100 | `RegistrySettingsStore.MachineDefaultFont` | `Machine_default_font_is_used_only_without_a_user_font`, `Machine_default_key_with_bad_values_falls_back_to_lucida_console` | giống | |
| `lfHeight`/`lfWidth` không lưu, tính lại từ `iPointSize` và DPI | `FontSpec` | `Font_height_for_dpi` | giống | |
| 5 DWORD `fWindowsOnlyEOL`, `fPasteOriginalEOL`, `fReverse`, `fWrapAround`, `fMatchCase` thiếu hoặc sai kiểu thì tạo/ghi đè bằng 0 (tạo khóa nếu chưa có) | `RegistrySettingsStore.ReadOrCreateZero` | `Five_dwords_are_created_as_zero_on_first_load`, `A_value_of_the_wrong_kind_is_overwritten_with_zero_but_existing_ones_are_kept`, `First_load_creates_the_key_with_five_zero_dwords` | giống | |
| Lưu ở `WM_CLOSE` trước khi hỏi lưu; thoát sớm khi không có gì đổi; nhóm font / Word Wrap+Status Bar / Page Setup / vị trí / Find theo cờ của nhóm | `RegistrySettingsStore.Save`, `MainForm.OnFormClosing` | `Nothing_changed_writes_nothing`, `Each_flag_writes_its_own_group`, `Default_position_has_an_empty_baseline_and_settings_are_saved_before_the_question` | giống | |
| Không bao giờ ghi `iDefaultEncoding`, `fWindowsOnlyEOL`, `fPasteOriginalEOL`, `fSaveWindowPositions`, `fMLE_is_broken` | `RegistrySettingsStore.Save` | `Never_written_values_stay_untouched_even_when_every_group_changes` | giống | |
| Ghi chuỗi `(wcsnlen(s, cchMax) + 1) * 2` byte | `RegistrySettingsStore.Save`, `Win32SettingsRegistry` | `Saved_strings_are_cut_to_their_buffer` | giống | Chuỗi dài hơn buffer bị cắt `cchMax - 1` (gốc có thể kèm 1 ký tự ngoài buffer, suy luận) |
| `fMLE_is_broken`, `fSaveWindowPositions`, `ShowStoreBanner` | | | bỏ | Không đọc; `fSaveWindowPositions` vốn bị gốc bỏ kết quả |
| Import từ khóa của gốc | `RegistrySettingsStore.ImportLegacy` | `First_run_imports_the_valid_values_of_the_original_key_once`, `Nothing_to_import_without_the_original_key_or_with_only_unused_values`, `Machine_default_font_and_import_read_their_own_keys` | cố ý khác | *(quyết định của port)* Quyết định 2026-10-30 (tự quyết): chỉ khi khóa port chưa tồn tại (lần chạy đầu), chép mọi giá trị port dùng mà đúng kiểu và cỡ (font, Word Wrap, Status Bar, `iDefaultEncoding`, hai cờ EOL, Find, Page Setup, vị trí cửa sổ), không hỏi. Lý do: người chuyển từ Notepad giữ thói quen của mình mà không phải làm gì; một lần duy nhất nên sau đó hai app độc lập, và khóa của gốc không bao giờ bị ghi. Không chép giá trị port không dùng (`ShowStoreBanner`, `fMLE_is_broken`, `fSaveWindowPositions`). Không import lại khi thiếu từng giá trị, vì như vậy giá trị người dùng chưa từng đổi trong port sẽ trôi theo gốc |

<a id="vi-tri-cua-so"></a>

### Vị trí cửa sổ

| Hành vi gốc | Nơi cài đặt | Test | Trạng thái | Ghi chú |
|---|---|---|---|---|
| `iWindowPos*` mặc định `CW_USEDEFAULT`; chỉ áp dụng khi cả X và Y đã đặt: cửa sổ nhận X, Y, DX, DY như `CreateWindowExW`, rồi `SetWindowPlacement({X, Y, X+w, Y+h})` trong `DPI_AWARENESS_CONTEXT_UNAWARE`; rect đó là mốc lưu | `App/Shell/WindowPlacement.Apply` | `A_saved_position_is_applied_dpi_unaware_and_unchanged_it_is_not_saved_again` | giống | Chạy cả hai ngày 2026-10-30 (144 DPI, registry gốc export trước và khôi phục sau, port trên hive tạm) với X=120, Y=90, DX=900, DY=600: cả hai mở ở `GetWindowRect` (180, 135, 1530, 1035) |
| Lưu: `GetWindowPlacement` trong ngữ cảnh unaware, ghi X, Y, DX, DY khi khác mốc; mở ở vị trí mặc định thì mốc rỗng nên lần thoát đầu luôn ghi | `WindowPlacement.GetUnaware`, `RegistrySettingsStore.Save` | `A_window_opened_at_the_default_position_saves_its_position_on_exit`, `Position_equal_to_the_startup_baseline_is_not_written` | giống | Cùng lần chạy trên: dời cả hai tới (300, 200) cỡ 1200×700 rồi `WM_CLOSE`, cả hai ghi `[200, 133, 800, 467]` |
| Trạng thái maximize không lưu; cửa sổ mở theo `nCmdShow` | `Program` (WinForms dùng `STARTUPINFO` ở lần `ShowWindow` đầu) | | giống | |

<a id="dong-lenh"></a>

### Dòng lệnh

| Hành vi gốc | Nơi cài đặt | Test | Trạng thái | Ghi chú |
|---|---|---|---|---|
| Bỏ tên chương trình: tới dấu cách/tab đầu tiên ngoài ngoặc kép, rồi bỏ cách/tab | `CommandLineParser.StripProgramName` | `Program_name_ends_at_the_first_blank_outside_quotes` | giống | |
| Dòng lệnh đọc nguyên văn bằng `GetCommandLineW` | `Win32StartupInfo.CommandLine` | `The_raw_command_line_starts_with_this_program` | giống | Sửa sau giai đoạn d (2026-10-30): bản d dùng `Environment.CommandLine`, mà .NET dựng lại từ mảng tham số và bỏ ngoặc kép quanh tên không có dấu cách, nên `/PT "a.txt" "máy in"` bị coi là sai cú pháp. Tìm ra khi chạy `/PT` bằng exe thật |
| Tiền tố không phân biệt hoa thường qua `CharUpperW`; `/aXYZ.txt` = `/A` + `XYZ.txt` | `CommandLineParser.HasPrefix` | `Prefixes_are_compared_upper_cased`, `Upper_casing_matches_CharUpperW_for_every_character_of_the_option_prefixes` | giống | So trên toàn BMP với `CharUpperW` thật: `ToUpperInvariant` chỉ khác ở U+017F (`ſ`), được xử lý riêng |
| `/A` ép ANSI, `/W` ép UTF-16 LE, xét trước `STARTF_UNTRUSTEDSOURCE`; nguồn không tin cậy thì phần còn lại là tên file | `CommandLineParser.Parse`, `Win32StartupInfo.IsUntrustedSource` | `Options_and_their_order`, `Untrusted_source_keeps_a_and_w_and_takes_the_rest_as_a_file_name`, `Untrusted_source_opens_options_as_a_file_name` | giống | |
| Thứ tự `/.SETUP`, `/PT` trước `/P`, `RestartByRestartManager:` (đúng 36 ký tự) | `CommandLineParser.Parse` | `Options_and_their_order`, `Restart_takes_exactly_36_characters` | giống | |
| Tên file: ngoặc kép hoặc nháy đơn tới dấu đóng, không thì hết dòng; không thấy (`FindFirstFileW`, nhận wildcard) và không có phần mở rộng (`PathFindExtensionW`) thì thêm `.txt` | `FileArgument`, `PathRules.FindExtension`, `Platform/Services/StartupServices.cs` | `File_names_are_quoted_or_take_the_rest`, `Missing_names_without_extension_get_txt`, `Extension_matches_PathFindExtensionW`, `Resolve_adds_txt_only_when_nothing_is_found_and_there_is_no_extension` | giống | |
| Tên đơn (không `\`, `:`) giữ tương đối; tên trong ngoặc kiểm `PathIsFileSpecW` trên phần SAU tên (sai con trỏ) | `FileArgument.Resolve` | `Missing_names_without_extension_get_txt` | cố ý khác | Port luôn đổi thành đường dẫn đầy đủ (`GetFullPathNameW`). Chỉ đổi đường dẫn hiện trong câu hỏi lưu; file mở ra như nhau |
| File không tồn tại: chuỗi 2, `MB_YESNOCANCEL \| MB_ICONEXCLAMATION`; Yes tạo (`OPEN_ALWAYS`) và mở, No tài liệu trống, Cancel thoát Notepad; lỗi khác: hộp lỗi file | `StartupCoordinator.OpenFile` | `A_missing_file_asks_to_create_it_and_yes_creates_and_opens_it`, `No_keeps_an_empty_document_and_cancel_ends_the_program`, `Other_open_errors_show_the_file_error`, `A_missing_name_gets_txt_and_cancel_in_the_create_question_ends_the_program` | giống | Chạy cả hai 2026-10-30 với file không có: hộp cùng vị trí, cỡ (641×349), khác duy nhất chữ tiêu đề (392 pixel trong vùng tiêu đề); Cancel: cả hai thoát mã 0, không tạo file; Yes: cả hai tạo file 0 byte, tiêu đề `missing-probe.txt - ...` |
| Cửa sổ `Show` trước khi nạp file dòng lệnh | `Program.Main` | | giống | |
| `/.SETUP`: `SlipUpAcc` (MainAcc + Esc, Ctrl+C, Ctrl+D, Ctrl+Z = Exit; Ctrl+C/Ctrl+Z vẫn Copy/Undo vì đứng sau) | `FileModule` (`KeyTier.SetupOnly`), `KeyDispatcher.SetupMode` | `Setup_mode_sets_the_window_style_and_the_slip_up_keys`, `Escape_and_ctrl_d_do_nothing_outside_setup_mode` | giống | Giữ suy luận "chỉ Esc, Ctrl+D thoát" |
| `/.SETUP`: nuốt `SC_MINIMIZE`, `SC_NEXTWINDOW`, `SC_PREVWINDOW`; xám Minimize trong system menu | `MainForm.WndProc`, `MainForm.IsSetupBlockedCommand` | `Setup_mode_swallows_minimize_and_window_cycling`, `Setup_mode_sets_the_window_style_and_the_slip_up_keys` | giống | |
| `/.SETUP`: `SetWindowLongW(GWL_STYLE, 0x00FD0000)` sau `ShowWindow` | `MainForm.EnterSetupMode` | `Setup_mode_sets_the_window_style_and_the_slip_up_keys` | cố ý khác | Quan sát 2026-10-30 trên gốc (`notepad.exe /.SETUP`): style đọc lại `0x04FD0000`, tức mất `WS_VISIBLE`; `IsWindowVisible` = FALSE, cửa sổ chỉ còn là ảnh cũ trên màn hình (không status bar, không thanh cuộn Edit, tiêu đề như mất kích hoạt) và không nhận chuột. Lỗi rõ ràng, port chỉ bỏ `WS_MINIMIZEBOX` (ý định của chế độ) và giữ cửa sổ dùng được |
| `/.SETUP`: file không có thì hỏi tạo (Cancel không thoát); lỗi khác `MB_OKCANCEL \| MB_ICONEXCLAMATION` với 5 -> 26, 123 -> 13, khác -> 1 | `StartupCoordinator.OpenForSetup` | `Setup_asks_to_create_and_cancel_does_not_end_the_program`, `Setup_open_errors_use_an_ok_cancel_box`, `Setup_without_a_file_only_enters_setup_mode` | giống | |
| `/.SETUP` không mở được file thì thử tiếp cả chuỗi `/.SETUP <file>` như tên file thường | `StartupCoordinator` | `Setup_asks_to_create_and_cancel_does_not_end_the_program` | cố ý khác | *(suy luận)* Tự quyết: lỗi rõ ràng (hỏi tạo một file tên `/.SETUP x.txt`); port giữ tài liệu trống ở chế độ setup |
| `/P`, `/PT`: tham số trống, hoặc `/PT` thiếu tên máy in trong ngoặc kép, thì cả dòng `/P...` được mở như tên file; tên file không ngoặc thì tìm máy in từ ký tự thứ hai của tên | `CommandLineParser.ParsePrint` (`StartupAction.InvalidPrint`) | `Incomplete_print_arguments_do_nothing`, `Options_and_their_order` | cố ý khác | Tự quyết: không làm gì (tài liệu trống, không in, không đóng) thay vì hỏi tạo file tên `/P...` |
| `/P`, `/PT`: lỗi mở hiện hộp OK (2 -> chuỗi 2, 5 hoặc 65 -> 26, 123 -> 13, khác -> 1), rồi luôn đóng | `StartupCoordinator.PrintFile` | `Print_open_errors_show_an_ok_box_and_still_close` | giống | Chuỗi 2 có câu hỏi "tạo file?" nhưng chỉ có nút OK, như gốc |
| `/P`, `/PT`: nạp lỗi sau khi mở được file vẫn in | `StartupCoordinator.PrintFile` | `A_file_too_large_to_load_is_not_printed` | cố ý khác | Port chỉ in tài liệu đã nạp |
| `/PT` tên máy in tối đa 255 ký tự | `CommandLineParser` | `Printer_names_are_cut_to_255_characters`, `Print_to_takes_the_quoted_printer_after_a_quoted_file` | giống | In thật: giai đoạn e |
| Tên file bắt đầu bằng `/a`, `/w`, `/p` không mở được bằng dòng lệnh trần | `CommandLineParser` | `Options_and_their_order` (`/path.txt`) | giống | *(suy luận)* Tự quyết giữ: đó là hệ quả của quy tắc tiền tố đã ghi trong docs (`/aXYZ.txt`); đặt tên trong ngoặc kép thì mở được |
| `RestartByRestartManager:<36 ký tự>` khôi phục, không mở file | `StartupCoordinator`, `IStartupHost.Restore` | `Restart_restores_only_a_36_character_id_and_never_opens_a_file` | giống | Khôi phục thật: xem "Autosave và Restart Manager (giai đoạn f)" |

<a id="keo-tha"></a>

### Kéo thả

| Hành vi gốc | Nơi cài đặt | Test | Trạng thái | Ghi chú |
|---|---|---|---|---|
| `DragAcceptFiles(TRUE)`; `WM_DROPFILES` chỉ lấy file đầu, không có file thì chỉ `DragFinish`; `SetActiveWindow`, `CheckSave`, nạp với tự nhận diện; lỗi mở thì hộp lỗi file; không khôi phục encoding | `MainForm.OnDropFiles`, `DocumentService.OpenDropped` | `Dropping_files_opens_only_the_first_with_detection`, `Dropping_a_missing_file_shows_the_file_error`, `A_dropped_file_asks_to_save_then_opens_with_detection`, `A_drop_is_abandoned_on_cancel_and_open_errors_show_the_file_error` | giống | Port gọi `DragFinish` trước khi nạp (gốc sau); không ảnh hưởng hành vi |

Font lúc khởi động (làm ở giai đoạn h): `GetTextFaceW` so với tên đã lưu (không phân biệt hoa thường); khác thì
`EnumFontsW(tên đã lưu)` với callback chép `lfCharSet` (byte 0x17 của `LOGFONTW`) của font đầu tiên rồi
trả 0, sau đó tạo lại font. Kiểm lại ngày 2026-10-30: callback không chọn font thay thế mà chỉ sửa charset, nên
khi font không có trên máy thì không có gì được liệt kê và GDI tự thay font như trước. Port:
`EditorSurface.WithInstalledCharSet` (gọi một lần trong `AppHost`, chỉ sửa trong bộ nhớ; nhóm font chỉ được ghi khi người
dùng đổi font, như gốc); test `An_installed_face_with_its_own_character_set_is_kept`,
`A_character_set_the_face_lacks_is_replaced_by_the_installed_one` (Consolas với `SYMBOL_CHARSET` thành `ANSI_CHARSET`),
`A_face_that_is_not_installed_keeps_the_saved_font`: giống. Port so tên bằng `OrdinalIgnoreCase` thay cho `lstrcmpiW`
(chỉ có thể khác với tên font ngoài ASCII, chưa xác minh).

<a id="in-va-page-setup"></a>

## In và Page Setup (giai đoạn e)

Kiểm trên máy này ngày 2026-10-30 bằng cách in cùng một file (150 dòng, có tab, có dòng dài) từ Notepad gốc
(`notepad.exe /PT "file" "Microsoft Print to PDF"`) và từ port, rồi đọc PDF bằng PyMuPDF và so từng từ (vị trí tới
0,1 pt). Registry của gốc được export trước và khôi phục sau (xóa khóa rồi `reg import`, so lại bằng hash: giống hệt).
Port chạy trên hive tạm. Bản gốc dùng Consolas 11 (`HKLM\Software\Microsoft\Notepad\DefaultFonts` trên máy này), nên
hive của port được đặt cùng font khi so. Kết quả: không header/footer, và header/footer có các mã `&l &f &c &p &r &d`,
`&p+10`, `&t`, `&&`, `&q`: 0 từ khác (trừ giờ/ngày của lần in), cùng số trang (4), cùng chỗ ngắt trang và ngắt dòng.

| Hành vi gốc | Nơi cài đặt | Test | Trạng thái | Ghi chú |
|---|---|---|---|---|
| Mã header/footer từ chuỗi 45 (`fFpPtTdDcCrRlL`), cột mặc định giữa, `&p+N`, `&&`, `&` + ký tự lạ bỏ cả hai, giới hạn 256 bỏ cả phần mở rộng | `HeaderFooterExpander` | `HeaderFooterExpanderTests` (9 phương thức test) | giống | Kiểm bằng PDF như trên |
| `&` cuối chuỗi đọc vượt NUL | `HeaderFooterExpander` | `A_trailing_ampersand_is_dropped_instead_of_reading_past_the_end` | cố ý khác | Đã xác nhận trên gốc: header 39 ký tự (`&l`, 36 chữ `H`, `&`) in luôn footer (`FOOT1`) vào cột phải của header. Port bỏ `&` cuối |
| Chữ thường ghi vào cột không kiểm giới hạn | `HeaderFooterExpander` | `Plain_characters_stop_at_255_instead_of_overrunning_the_column` | cố ý khác | Port dừng ở 255 ký tự mỗi cột; gốc có thể ghi tràn (chưa xác minh hậu quả) |
| Chữ số sau `&p+` đọc bằng `iswdigit` | `HeaderFooterExpander` | `Page_plus_n_adds_the_decimal_digits_after_the_plus` | cố ý khác (nhỏ) | Port chỉ nhận `0`-`9`; gốc có thể nhận chữ số của chữ viết khác rồi cộng sai (chưa xác minh) |
| Font in: `lfHeight = -(pt10 * LOGPIXELSY / 720)` chia nguyên; font raster thay bằng font mặc định của driver; dòng = `tmHeight + tmExternalLeading` | `PrintLayoutCalculator.FontHeight`, `Win32PrintDevice.SelectFont` | `Font_height_truncates_point_size_times_resolution_over_720`, `A_raster_font_is_replaced_by_the_drivers_default_face` | giống | |
| Lề đổi sang pixel thiết bị bằng `MM_ANISOTROPIC` với `ptPaperSize` của `PAGESETUPDLGW` | `PrintLayoutCalculator.MarginsToDevice` | `Margins_convert_with_the_printer_resolution` | cố ý khác | Port đổi bằng `LOGPIXELSX/Y` của chính máy in; cùng kết quả khi khổ giấy khớp (PDF trùng tới 0,1 pt). Với `/PT` gốc lấy `ptPaperSize` từ máy in mặc định (máy này: "Export to WPS PDF"); lần in đầu tiên của gốc ra lề như khổ A4 (trái 55,6 pt, trên 67,9 pt thay vì 54 và 72), các lần sau đúng. Nguyên nhân chưa xác minh |
| Lề tính từ vùng in được, kẹp 0; header ở lề trên, footer cách lề dưới một dòng, mỗi cái lấy một dòng của thân; `(VERTRES - lề) / dòng - header - footer < 1` thì chuỗi 28 | `PrintLayoutCalculator.Compute` | `Body_takes_a_line_from_each_side_for_header_and_footer`, `Margins_are_measured_from_the_printable_area_and_never_negative`, `A_page_without_room_for_one_body_line_is_too_small`, `A_page_too_small_for_one_line_is_reported_before_the_job_starts` | giống | |
| Ba cột trên một dòng, không cắt, có thể đè nhau | `PrintLayout.ColumnX` | `Columns_sit_at_the_left_margin_the_middle_and_the_right_margin` | giống | |
| Phân trang bằng `DrawTextExW` (`DT_WORDBREAK \| DT_EXPANDTABS \| DT_NOPREFIX \| DT_EDITCONTROL`, thêm `DT_RTLREADING \| DT_RIGHT` khi Edit có `WS_EX_RTLREADING`), luôn ngắt dòng theo trang; trang không vẽ được ký tự nào thì chuỗi 28 và dừng | `PrintService.PrintPages`, `Win32PrintDevice.DrawBody` | `A_job_prints_every_page_with_header_and_footer_and_closes_everything`, `A_page_that_takes_no_character_ends_the_job_with_the_same_message`, `Empty_header_and_footer_give_the_body_the_whole_area`, `A_job_on_microsoft_print_to_pdf_writes_one_pdf_page_per_page` | giống | RTL chỉ kiểm bằng unit test |
| `iTabLength = tmAveCharWidth * 8` | `Win32PrintDevice.TabLength` | `Tabs_are_eight_average_characters_and_tab_length_is_ignored_without_dt_tabstop` | giống | Câu hỏi mở đã trả lời: PDF của gốc có tab dừng mỗi 8 ký tự (48 pt với ký tự 6 pt). `DrawTextExW` chỉ đọc `iTabLength` khi có `DT_TABSTOP`, mà gốc không đặt (đo bằng `DT_CALCRECT` trên DC của máy in PDF). Port truyền 8 cho rõ nghĩa; kết quả như nhau |
| Header vẽ trước `StartPage` | `PrintService.PrintPages` | `The_header_is_drawn_after_start_page` | cố ý khác (không thấy khác) | Câu hỏi mở đã trả lời: trên Microsoft Print to PDF header của gốc nằm đúng trên mọi trang, kể cả trang 1. Port vẽ sau `StartPage` theo đúng thứ tự API; PDF giống hệt |
| Văn bản rỗng: `StartDoc`/`EndDoc` không trang nào | `PrintService` | `An_empty_document_starts_and_ends_a_job_without_pages` | giống | |
| Tên tài liệu trong hàng đợi là tiêu đề cửa sổ; một thời điểm giờ/ngày cho mọi trang (`GetTimeFormatW` cờ 0, `DATE_LONGDATE`) | `PrintService`, `Win32DateTimeFormatter.FormatTime/FormatLongDate` | `Header_and_footer_values_use_the_page_and_the_start_time`, `The_progress_box_shows_the_name_after_the_last_separator_and_the_queue_gets_the_window_title` | giống | |
| Hộp "Now Printing" (dialog 11) không modal: tên file sau `:` hoặc `\` cuối, "Page %d", vô hiệu cửa sổ chính, mọi `WM_COMMAND` là hủy, `WM_INITMENU` làm xám Close | `Win32PrintProgress` | `Progress_box_disables_the_owner_shows_name_and_page_and_any_command_cancels`, `Progress_template_follows_dialog_11`, `Progress_label_rules` | giống | Template dựng bằng code theo bố cục dialog 11. Pixel: phần dưới thanh tiêu đề 0 pixel khác, chỉ khác chữ tiêu đề (tên sản phẩm); cùng vị trí so với vùng client của cửa sổ chính (`DialogPlacement` bù chiều cao menu) |
| Abort proc bơm message (hộp "Now Printing" qua `IsDialogMessageW` trước), trả FALSE khi đã hủy; hủy thì `AbortDoc`, không báo gì | `PrintAbort.Proc`, `PrintService` | `Abort_proc_pumps_messages_and_stops_once_cancelled`, `Cancel_aborts_the_document_silently`, `Cancel_while_the_document_starts_is_silent` | giống | |
| Lỗi in: `FormatMessageW` theo mã lỗi, không có thì chuỗi 12 với đường dẫn đầy đủ hoặc "Untitled"; `MB_ICONEXCLAMATION` | `PrintService.Print` | `A_failed_job_reports_the_system_message_or_string_12`, `The_error_names_the_full_path_of_a_saved_document`, `Errors_of_each_gdi_step_are_reported` | giống | Hủy hộp lưu của Microsoft Print to PDF: cả gốc và port hiện "The operation was canceled by the user." (`ERROR_CANCELLED` từ `StartDocW`, quan sát 2026-10-30) |
| Ánh xạ mã `SP_*` (-4 thành `ERROR_DISK_FULL`, -5 thành `ERROR_OUTOFMEMORY`, -1 thành `GetLastError`) | | | không port | `PrintDocument` của gốc luôn trả `GetLastError`, không trả mã `SP_*`, nên nhánh này không bao giờ chạy |
| File > Print: `PrintDlgExW` cờ `0x84010C` (không chọn trang, không in vùng chọn, bản sao do driver), dưới `DPI_AWARENESS_CONTEXT_SYSTEM_AWARE`; `PD_RESULT_APPLY` chỉ lưu cấu hình | `Win32PrinterService.FromDialog` | `Menu_print_opens_the_print_dialog_and_page_setup_stores_the_result` | giống | Windows 11 trên máy này hiện hộp in kiểu mới do explorer.exe chứa, với cả gốc lẫn port; đã kiểm mở bằng Ctrl+P, cửa sổ chính bị vô hiệu, đóng hộp thì không báo gì. Chưa tự động hóa việc in qua hộp này (đường in giống `/PT`). Port không gọi `PSD_RETURNDEFAULT` trước đó (không cần vì lề không dùng `ptPaperSize`) và giải phóng handle `DEVMODE`/`DEVNAMES` cũ (gốc rò rỉ) |
| `/P`: máy in đã chọn hoặc máy in mặc định (`PSD_RETURNDEFAULT`), `CreateDCW` với `DEVMODE`; lỗi thì chuỗi 25 | `Win32PrinterService.Default` | `Print_uses_the_default_printer`, `A_printer_that_cannot_be_opened_shows_string_25_and_a_silent_failure_shows_nothing` | giống | Không in thật: máy in mặc định của máy này không phải Microsoft Print to PDF |
| `/PT`: `OpenPrinterW` + `GetPrinterDriverW` level 1, `CreateDCW` không `DEVMODE`; chỉ lỗi `CreateDCW` được báo (chuỗi 25) | `Win32PrinterService.Named` | `Named_printers_open_and_unknown_names_fail_silently`, `Print_to_a_named_printer_loads_the_file_prints_and_closes` | giống | E2E bằng exe thật như trên |
| Page Setup: `PageSetupDlgW` với hook và template riêng dưới `SYSTEM_AWARE`; Header/Footer tối đa 39 ký tự, chọn hết header; đọc hai ô ở `WM_DESTROY`; link mở trang trợ giúp | `Win32PageSetupDialog`, `Win32PrinterService.ShowPageSetup` | `Page_setup_shows_header_and_footer_limits_them_and_returns_the_edited_values`, `Page_setup_cancel_returns_no_error_and_metric_margins_are_shown_in_millimeters`, `Page_setup_template_uses_the_standard_ids_and_adds_header_footer_and_link` | giống | Template dựng bằng code với ID chuẩn của comdlg32 theo bố cục dialog 12, cấp bằng `GlobalAlloc(GMEM_FIXED)` (comdlg32 dùng handle như con trỏ; vùng `GMEM_MOVEABLE` làm hiện hộp lỗi "Printing Error"). So pixel với gốc: cùng vị trí và kích thước (823x546 ở 144 DPI), mọi control cùng chỗ và chữ, kể cả "Margins (inches)" |
| Link "Input Values" (`https://go.microsoft.com/fwlink/p/?linkid=838060`) | `AppOptions.PageSetupHelpUrl` | `Page_setup_template_uses_the_standard_ids_and_adds_header_footer_and_link` | cố ý khác | Theo quyết định cho Help (2026-10-29): URL đọc từ `AssemblyMetadata` `PageSetupHelpUrl`, trống thì bỏ link (cần lớp SysLink của comctl32 v6; không có thì cũng bỏ). Ảnh so pixel chỉ khác ở link và hai ô Header/Footer (do mặc định `&f`/`Page &p`) |
| Page Setup: lỗi `0x1009`/`0x100B`/`0x100C` thì bỏ máy in đã chọn và mở lại; lỗi khác qua bảng của `ShowCommDlgError`; OK lưu header, footer, lề và đánh dấu nhóm Page Setup | `PrintService.PageSetup`, `CommonDialogErrors` | `Page_setup_ok_stores_header_footer_and_margins`, `Page_setup_resets_the_printer_and_opens_again_after_a_printer_error`, `Page_setup_reports_other_errors_once`, `Common_dialog_error_table`, `Other_common_dialog_errors_show_their_code` | giống | Bảng 8 mục của gốc: `0xFFFF`, 5, 6, `0x1005` thành chuỗi 7; 2 thành 10; `0xA` thành 11; `0x1004` thành 25; `0x1008` chuỗi rỗng (không hiện gì); mã khác "Common Dialog error (0x%04x)" |

Quyết định của giai đoạn e:

- `iTabLength` và thứ tự header/`StartPage`: in thử trên gốc trước (bảng trên). Tab giữ nguyên kết quả; header vẽ sau
  `StartPage` vì đó là thứ tự API định nghĩa và PDF không đổi.
- Header/footer mặc định khi registry chưa có: giữ `&f` / `Page &p` (gốc làm mất, đã quan sát).
- Lề: đổi bằng độ phân giải của máy in thay cho khổ giấy của Page Setup, để `/PT` không phụ thuộc máy in mặc định.
- Hộp in của Windows 11 nằm ngoài tiến trình, nên test tự động dừng ở `IPrinterService` giả; phần GDI được test thật
  trên Microsoft Print to PDF (in ra file trong thư mục tạm qua `DOCINFOW.lpszOutput`).
- Test Platform chạy với manifest comctl32 v6 (`tests/NotepadClassic.Platform.Tests/test.manifest`) như app, để có SysLink.

<a id="autosave-va-restart-manager"></a>

## Autosave và Restart Manager (giai đoạn f)

Kiểm trên máy này ngày 2026-10-30 bằng Restart Manager thật, chỉ trên tiến trình do probe tự khởi chạy: mở
`f docs, comma\doc.txt` (UTF-8 có BOM, chữ `old`) bằng Notepad gốc rồi bằng port, thay văn bản bằng
`EM_REPLACESEL`, rồi `RmStartSession` + `RmRegisterResources` (PID và thời điểm khởi động) + `RmShutdown(0)` +
`RmRestart`. Registry của gốc được export trước và khôi phục sau (xóa khóa rồi `reg import`, so bằng hash: giống hệt;
khóa `Autosave\165A7C00-...` có sẵn của người dùng còn nguyên). Thư mục `%LOCALAPPDATA%\Notepad` do lần chạy gốc tạo ra
(chỉ còn `probe.autosave`) đã được xóa. Port chạy với hive tạm và thư mục autosave tạm (`NOTEPADCLASSIC_AUTOSAVE_DIR`),
không tạo `HKCU\Software\NotepadClassic`. Kết quả:

- Cả hai: `GetApplicationRestartSettings` trả `RestartByRestartManager:<GUID>` cờ 0, mỗi lần chạy một GUID mới, nhóm
  thứ ba viết thường (`...-45a2-...`, `...-443c-...`). `RmShutdown` thành công không hỏi gì (0,05 s gốc, 0,1 s port),
  tiến trình thoát mã 0.
- Văn bản bẩn: gốc ghi khóa `Autosave\<GUID>` (tên là đường dẫn có dấu phẩy, dữ liệu `4,<...>.autosave,<thư mục hiện
  hành>`) và file `.autosave` 34 byte (UTF-16 LE có BOM), để lại `probe.autosave`; port ghi `<GUID>.json` và
  `<GUID>.txt` 32 byte (UTF-16 LE không BOM), không file nào khác. Sau `RmRestart`: tiêu đề `*doc.txt`, văn bản
  `new text\r\nline 2`, `EM_GETMODIFY` 1, status bar "UTF-8 with BOM"; dữ liệu autosave bị xóa ở cả hai. Ảnh cửa sổ
  khôi phục (hive của port đặt cùng font Consolas 11, Word Wrap, status bar như gốc): vùng client và status bar không
  khác pixel nào, chỉ khác chữ tiêu đề.
- Văn bản sạch: cả hai chỉ ghi đường dẫn và encoding (gốc dữ liệu `4,,<thư mục>`), sau restart mở lại `old`, không bẩn.
  Gốc không tạo `probe.autosave` trong trường hợp này (chỉ kiểm chỗ trống khi bẩn).

| Hành vi gốc | Nơi cài đặt | Test | Trạng thái | Ghi chú |
|---|---|---|---|---|
| GUID mỗi phiên `"%08lX-%04X-%04x-..."` (nhóm 3 viết thường), `RegisterApplicationRestart("RestartByRestartManager:<GUID>", 0)` lúc khởi động | `AutosaveSession`, `AutosaveService.Register`, `Win32RestartRegistration` | `Session_id_is_upper_case_except_the_third_group`, `Restart_command_line_is_the_prefix_and_the_id`, `Restart_registration_reaches_windows`, `Startup_registers_the_restart_command_line_with_a_new_id` | giống | Kiểm bằng `GetApplicationRestartSettings` trên tiến trình thật (trên) |
| `WM_QUERYENDSESSION`: hộp hỏi lưu/Save As đang mở thì `MessageBeep(0)` hai lần, chuỗi 24, trả FALSE; sạch thì TRUE; `ENDSESSION_CLOSEAPP` và `CanAutosave` thì TRUE không hỏi; còn lại `CheckSave` | `AutosaveService.QueryEndSession`, `MainForm.WndProc`, `MessageKind.ShutdownBlocked` | `An_open_save_dialog_blocks_the_session_end_with_string_24`, `A_clean_document_lets_the_session_end`, `A_restart_manager_close_agrees_without_asking_when_the_text_fits`, `A_normal_logoff_asks_the_save_changes_question`, `A_restart_manager_close_asks_when_the_autosave_folder_cannot_take_the_text`, `A_logoff_asks_the_save_changes_question_and_cancel_blocks_it`, `An_open_save_dialog_blocks_even_a_restart_manager_close` | giống | Hộp chuỗi 24 là `MB_SYSTEMMODAL` (0x1000). Port bỏ qua xử lý `WM_QUERYENDSESSION`/`WM_ENDSESSION` của WinForms (vốn đóng mọi form) |
| `CanAutosave`: tạo thư mục, tạo `probe.autosave` ẩn rồi để lại, đủ `(độ dài + 1) * 2` byte trống | `Win32AutosaveStore.CanSave` | `Can_save_creates_the_folder_and_leaves_no_probe_behind`, `Can_save_refuses_more_than_the_free_space` | cố ý khác | Probe của port mở với `FILE_FLAG_DELETE_ON_CLOSE`, không để lại gì (đã quan sát gốc để lại `probe.autosave`) |
| `WM_ENDSESSION` (`wParam` khác 0 và `ENDSESSION_CLOSEAPP`): đặt cờ armed, lưu | `AutosaveService.EndSession` | `Only_an_ending_restart_manager_session_saves`, `An_ending_restart_manager_session_arms_and_saves` | giống | |
| `WM_CLOSE`: lưu cài đặt trước; bẩn và (chưa armed hoặc lưu lỗi) thì `CheckSave`; armed và lưu được thì đóng không hỏi | `AutosaveService.ConfirmClose`, `MainForm.OnFormClosing` | `Close_of_a_clean_document_never_asks`, `Close_asks_while_the_session_is_not_armed`, `Close_of_an_armed_session_saves_again_without_asking`, `Close_of_an_armed_session_asks_when_the_save_fails`, `Restart_manager_close_saves_without_asking_and_a_restart_restores_the_text` | giống | |
| Lưu: encoding hiện tại, đường dẫn đầy đủ hoặc `*Untitled*`, thư mục hiện hành; văn bản bẩn ghi thành file; lỗi thì xóa khóa | `AutosaveService.Save`, `AutosaveManifest`, `Win32AutosaveStore` | `A_clean_document_saves_only_its_path_encoding_and_directory`, `An_untitled_document_saves_no_path`, `A_failed_text_write_leaves_no_manifest`, `A_failed_save_survives_a_failed_delete`, `Text_is_raw_utf16_le_and_each_write_replaces_the_file_whole`, `Manifest_round_trips_as_utf8` | cố ý khác (định dạng) | `%LOCALAPPDATA%\NotepadClassic\Autosave\<GUID>.json` (manifest JSON `version` 1, encoding theo tên) và `<GUID>.txt` (UTF-16 LE thô), không dùng registry. Văn bản ghi trước manifest; mỗi file ghi ra `.tmp`, flush xuống đĩa rồi đổi tên, nên phiên bị cắt giữa chừng không để lại file dở |
| Dữ liệu `"%i,%s,%s"` tách theo dấu phẩy: đường dẫn autosave hoặc thư mục có dấu phẩy làm hỏng | `AutosaveManifest` | `Manifest_round_trips_paths_with_commas`, `Restart_manager_close_saves_without_asking_and_a_restart_restores_the_text` | cố ý khác | JSON không có vấn đề này. Probe thật dùng thư mục có dấu phẩy |
| Khôi phục: `SetCurrentDirectoryW`; bẩn thì nạp file autosave (UTF-16 LE, giữ NUL, không `.LOG`), lấy đường dẫn, tiêu đề, encoding của file gốc, `EM_SETMODIFY(TRUE)`; sạch có tên thì mở lại theo đường dẫn với encoding cũ; sạch chưa đặt tên thì không làm gì; rồi xóa dữ liệu | `AutosaveService.Restore`, `DocumentService.RestoreAutosavedText` | `Dirty_text_comes_back_with_the_documents_path_title_and_encoding`, `Dirty_untitled_text_comes_back_untitled_and_dirty`, `Restored_text_keeps_nuls_a_leading_bom_character_and_gets_no_log_stamp`, `A_clean_document_is_opened_again_by_path_with_its_encoding`, `A_clean_document_whose_file_is_gone_shows_the_file_error`, `A_clean_untitled_document_opens_nothing`, `A_directory_that_cannot_be_made_current_does_not_stop_the_restore`, `Saved_state_restores_in_a_new_run` | giống | Kiểm bằng `RmRestart` (trên). File văn bản không có BOM nên ký tự U+FEFF đầu văn bản được giữ |
| Nạp file autosave lỗi vẫn gán tên file gốc, đánh dấu bẩn và xóa dữ liệu | `AutosaveService.Restore`, `DocumentService.RestoreAutosavedText` | `A_text_file_that_fails_to_load_keeps_the_data_and_the_untitled_document`, `A_text_the_editor_refuses_keeps_the_data` | cố ý khác | Port báo lỗi file, giữ tài liệu Untitled trống (để không ghi đè file gốc bằng văn bản rỗng) và giữ dữ liệu để chạy lại cùng dòng lệnh |
| Manifest không đọc được hoặc sai: không xóa | `AutosaveService.Restore` | `An_unreadable_manifest_is_kept`, `Manifests_this_version_cannot_restore_are_refused` | giống | Port còn từ chối `version` khác 1 |
| GUID từ dòng lệnh: chỉ kiểm đủ 36 ký tự rồi ghép vào đường dẫn | `AutosaveSession.IsValidId`, `Win32AutosaveStore` | `Only_hex_ids_in_the_guid_layout_name_autosave_files`, `Nothing_is_read_for_an_invalid_or_unknown_id`, `Ids_that_are_not_session_ids_never_become_paths` | cố ý khác | Port chỉ nhận dạng 8-4-4-4-12 chữ số hex, để dòng lệnh không trỏ ra ngoài thư mục autosave |
| Nạp file autosave đi qua `LoadFile` đầy đủ, nên `SHAddToRecentDocs` nhận đường dẫn file `.autosave` (suy luận từ `LoadFile`, chưa xác minh) | `DocumentService.RestoreAutosavedText` | `Dirty_text_comes_back_with_the_documents_path_title_and_encoding` | cố ý khác | Port không thêm file autosave vào Recent Documents |
| Không bao giờ dọn khóa/file của phiên không được khởi động lại | `AutosaveService.CleanOrphans` (sau dòng lệnh), `Win32AutosaveStore.List` | `Old_entries_are_deleted_at_startup_and_recent_ones_are_kept`, `Cleanup_failures_are_ignored`, `List_groups_files_by_session_and_skips_other_names` | cố ý khác | Lúc khởi động port xóa phiên có manifest đã cũ hơn 30 ngày và file không có manifest cũ hơn 1 ngày (tuổi theo lần ghi mới nhất); không đụng file không mang tên GUID |
| Nút Launch của banner Store | | | không port | Không có banner (quyết định cố định của dự án) |

Quyết định của giai đoạn f:

- Không đọc khóa `HKCU\Software\Microsoft\Notepad\Autosave` của Notepad gốc để khôi phục chéo: dòng lệnh
  `RestartByRestartManager:` do Windows gửi chỉ tới chương trình đã đăng ký GUID đó, nên GUID của gốc không bao giờ đến
  port; đọc khóa đó chỉ thêm rủi ro sửa dữ liệu của chương trình khác.
- Thời hạn dọn: 30 ngày cho phiên đầy đủ (Restart Manager khởi động lại sau vài phút, hoặc ở lần đăng nhập sau khi
  máy khởi động lại; mất văn bản chưa lưu tệ hơn để thừa vài file), 1 ngày cho phiên thiếu manifest (đủ để không xóa
  phiên của cửa sổ khác đang ghi). Dọn sau khi xử lý dòng lệnh để phiên đang khôi phục được dùng trước.
- Nạp lỗi: giữ Untitled thay vì gán tên file gốc như gốc, vì gán tên với văn bản rỗng bẩn khiến Save ghi đè file gốc.
- Biến môi trường `NOTEPADCLASSIC_AUTOSAVE_DIR` đổi thư mục autosave cho test và E2E, giống
  `NOTEPADCLASSIC_SETTINGS_HIVE`.

<a id="hieu-nang"></a>

## Hiệu năng (giai đoạn g)

Số đo, phương pháp và công cụ nằm trong `performance.md`. Quyết định của giai đoạn g:

- Giữ `EM_SETHANDLE` với buffer `LocalAlloc` như gốc: ba cách nạp vào Edit đo gần bằng nhau, cách này nhanh nhất một
  chút và không cần bản sao thứ hai của văn bản.
- Với cùng font và cùng Word Wrap, port nạp file nhanh ngang gốc sau khi cửa sổ hiện (50 MB không wrap: 153 ms so với
  177 ms; có wrap: khoảng 6,2 s cả hai, gần hết trong Edit control của USER32).
- Khởi động không đạt "<= 2x gốc" (port 173 ms, gốc 32 ms, bản publish ReadyToRun, đo lại 2026-10-30 lúc máy rảnh):
  một app WinForms rỗng đã mất 100 ms, thêm MenuStrip, StatusStrip và TextBox là 127 ms. Ngân sách chốt lại là <= 190 ms
  ấm trên máy đo (sàn 127 ms cộng 46 ms việc riêng của port đã đo từng bước, xem `performance.md`). Đã sửa một chỗ chậm
  (`GetPixel` thành `LockBits` khi đo vị trí chữ của status bar); các cách khác đã đo và không nhận (tắt QuickJit,
  composite ReadyToRun self-contained, bỏ DI) ghi ở đó.
- Find: kết quả khớp vẫn do `FindNLSString` quyết định, nhưng port thêm đường nhanh trong `NlsTextSearcher` (tìm thứ tự
  trên các đoạn chỉ gồm ASCII in được, `FindNLSString` thật trên cửa sổ quanh mọi ký tự khác, có lề đủ cho co chữ 3 đơn
  vị). Kết quả giống hệt một lần gọi `FindNLSString` cùng cờ; điều kiện của locale được dò lúc chạy và đường nhanh tự tắt
  khi không thỏa. 50 triệu ký tự không khớp: ASCII 108,6 thành 6,3 ms, có chữ Việt mỗi 400 ký tự 355,2 thành 27,7 ms;
  gốc đo cùng phương pháp đầu-cuối (click tới "Cannot find") xem `performance.md`.
- Bật `PublishReadyToRun` và `TieredPGO`. Native AOT và trimming: WinForms không được hỗ trợ trong .NET 10 (SDK chặn
  bằng `NETSDK1175`, epic dotnet/winforms#4649 còn mở), máy không có công cụ C++ cần cho AOT; không dùng, không cài thêm.
- Thời gian chỉ đo local; allocation là ngưỡng test (`AllocationTests`, chạy trong CI cùng Core tests).

<a id="e2e"></a>

## E2E và hoàn thiện (giai đoạn h)

`tests/NotepadClassic.E2E.Tests` dùng FlaUI (UIA3) chạy `NotepadClassic.exe` đã build, mỗi test một tiến trình với
thư mục tạm, hive settings tạm (`NOTEPADCLASSIC_SETTINGS_HIVE`) và thư mục autosave tạm
(`NOTEPADCLASSIC_AUTOSAVE_DIR`). Mọi chỗ chờ là chờ theo điều kiện (`Wait.Until`, hỏi lại mỗi 25 ms, tối đa 20 s),
không có `Sleep` cố định. App ghi gì ra stderr (exception không bắt) thì test đỏ. 8 test, xanh 3 lần liên tiếp
(2026-10-30):

| Luồng | Test |
|---|---|
| Mở file từ dòng lệnh: văn bản, tiêu đề, status bar | `Opens_the_file_named_on_the_command_line` |
| Gõ phím: `*` trên tiêu đề; Ctrl+S ghi đè tại chỗ | `Typing_marks_the_title_and_save_writes_the_file_in_place` |
| Ctrl+S khi Untitled: hộp Save As, ghi UTF-8 | `Save_of_an_untitled_document_goes_through_save_as` |
| Find Next chọn chỗ khớp; Replace All | `Find_next_selects_the_match_and_replace_all_replaces_every_one` |
| Go To dòng 3; Ln/Col trên status bar | `Go_to_moves_the_caret_to_the_line_and_the_status_bar_follows` |
| Zoom Ctrl+Plus; Word Wrap làm xám Go To, giữ văn bản | `Word_wrap_turns_go_to_off_and_zoom_shows_in_the_status_bar` |
| Đóng khi chưa lưu: hỏi, Don't Save thì thoát | `Closing_with_unsaved_changes_asks_and_dont_save_exits` |
| Restart Manager thật (`RmShutdown`), khởi động lại với `RestartByRestartManager:<id>` | `Restart_manager_shutdown_keeps_unsaved_text_and_the_restart_restores_it` |

Lỗi tìm ra nhờ E2E và đã sửa (commit riêng "Ignore caret events after the editor is disposed"): đóng cửa sổ khi văn bản
chưa lưu (Don't Save) làm app crash lúc thoát. WinForms dispose Edit control khi cửa sổ chính đóng trong lúc vòng message
vẫn giao các `EVENT_OBJECT_LOCATIONCHANGE` đang chờ; cập nhật status bar hỏi caret của control đã dispose và ném
`ObjectDisposedException`. Nay callback bỏ qua khi control đã dispose.

Gõ phím trong E2E dùng `SendInput` với `KEYEVENTF_UNICODE`: gõ bằng phím ảo đi qua bộ gõ của người dùng, và bộ gõ
tiếng Việt kiểu Telex trên máy này biến "ef" thành "è".

Test chập chờn đã tìm ra nguyên nhân và sửa (2026-10-30, commit riêng): `Closing_with_unsaved_changes_asks_and_dont_save_exits`
đỏ 1/5 lần khi chạy cả bộ `dotnet test --solution` (hết 20 s chờ) nhưng xanh khi chạy một mình. `dotnet test` chạy các
test project song song; Platform tests mở hộp thoại thật ("Go To Line", "Page Setup") và App tests tạo cửa sổ chính thật,
và vì tiến trình test là con của cửa sổ đang ở trước, Windows cho chúng quyền lên trước. Ghi lại cửa sổ trước
(`GetForegroundWindow` mỗi 5 ms) trong lúc chạy cả bộ thấy các cửa sổ đó lên trước 4 lần trong lúc E2E đang gửi phím
bằng `SendInput`, nên phím rơi vào tiến trình khác. Sửa: sau khi đưa app lên trước, `AppSession` gọi
`LockSetForegroundWindow(LSFW_LOCK)` (trong lúc `AttachThreadInput` với luồng đang ở trước), nên tiến trình khác không
lấy được foreground cho tới khi người dùng hoặc test bấm phím/chuột thật; và sau mỗi lần gõ, test kiểm tra cửa sổ trước
vẫn thuộc app, nếu không thì ném lỗi nêu rõ cửa sổ và tiến trình đã cướp foreground thay vì chờ hết 20 s. Không tăng
thời gian chờ nào.

`tests/NotepadClassic.ParityProbes` (console, không phải test project, không chạy trong CI) lái Notepad gốc bằng FlaUI
để kiểm lại các quan sát đã ghi: Go To xám khi Word Wrap bật (`goto-wordwrap`), Restart Manager lưu không hỏi và khôi
phục văn bản bẩn (`restart-manager`). Trước khi chạy nó export `HKCU\Software\Microsoft\Notepad`, sau đó xóa khóa,
`reg import` và so byte với bản export; file trong `%LOCALAPPDATA%\Notepad` do gốc tạo được xóa. Lần chạy 2026-10-30:
cả hai quan sát vẫn đúng, registry giống hệt.

Quyết định của giai đoạn h:

- E2E chạy trong CI (bước riêng sau App tests): runner Windows của GitHub chạy trong phiên desktop tương tác, đủ cho UI
  Automation. Đã chạy xanh trên runner thật ngày 2026-10-30.
- ParityProbes không vào CI vì cần một bản Notepad classic gốc và đụng registry của người dùng.
- `dotnet test --solution` chạy cả E2E, nên chạy cả bộ trên máy phát triển sẽ chiếm bàn phím và cửa sổ trước khoảng một
  phút. Trong lúc đó E2E khóa foreground (`LockSetForegroundWindow`) để hộp thoại của các test project chạy song song
  không cướp phím; cả bộ đã chạy xanh 5 lần liên tiếp sau khi sửa (xem "Test chập chờn" ở trên).

<a id="hop-thoai-he-thong"></a>

## Hộp thoại hệ thống (giai đoạn b)

| Hành vi gốc | Nơi cài đặt | Test | Trạng thái | Ghi chú |
|---|---|---|---|---|
| `IFileOpenDialog`/`IFileSaveDialog`, đuôi `txt`, 2 loại file, combobox Encoding: ANSI, UTF-16 LE, UTF-16 BE, UTF-8, UTF-8 with BOM (+ GB18030 khi ACP 936); Open có Auto-Detect chọn sẵn | `Win32DialogService`, `Platform/Interop/FileDialogCom.cs` | `Encoding_combo_items_and_selection` | giống | E2E 2026-10-29: Save As thật ghi `Hello` thành UTF-8 không BOM, tiêu đề thành `saved.txt - Notepad Classic`; Open hiện "Encoding: Auto-Detect" |
| Vị trí hộp Open, Save As, Font: theo gốc vùng client của cửa sổ chủ | `DialogPlacement.WhileShowing` (hook `WH_CBT` của luồng, `HCBT_ACTIVATE`) | `While_showing_moves_the_first_dialog_down_once_and_unhooks` | giống | *(quan sát khi so hình)* Trước đây cả ba của port cao hơn gốc 30 px (menu nằm trong vùng client). Đo lại 2026-10-29, cửa sổ chính ở (100,100) 1500×1050, 144 DPI: Open và Save As ở offset (11,75), Font ở (44,244), trùng gốc. Cờ `ChooseFontW` giữ nguyên `0x01010041` |
| Hỏi lưu bằng `TaskDialogIndirect`: "&Save" (6), "Do&n't Save" (7), Cancel, mặc định Save, không icon; lỗi thì `MessageBoxW(MB_YESNOCANCEL)` | `Win32DialogService.AskSaveChanges` | E2E thủ công (2026-10-29) | giống | |

<a id="so-sanh-hinh"></a>

## So sánh hình với Notepad gốc

Đã chạy Notepad gốc cạnh bản port ngày 2026-10-29, hai cửa sổ cùng 1500×1050 ở 144 DPI, Windows 11 26300, theme sáng. `HKCU\Software\Microsoft\Notepad` được export ra `.reg` trước, tạm đặt `lfFaceName = Lucida Console`, `iPointSize = 100`, `fWrap = 0` để hai bên cùng font, rồi khôi phục. Đo bằng ảnh chụp màn hình (so từng pixel), `GetWindowRect`, `EM_GETRECT`, `EM_GETMARGINS`.

| Vùng | Kết quả | Trạng thái |
|---|---|---|
| Vùng Edit (font, lề, khoảng dòng; formatting rect `0,0,1451,902`, margin 0/1) | 0 pixel khác trên 1448×925 | giống |
| Menu bar: chiều cao `SM_CYMENU` (30 px), vị trí chữ từng mục (x và y) | Trùng từng pixel | giống |
| Menu bar: nền và hover | Gốc nền trắng 255, hai dòng dưới 242/240, hover khối xám nhạt; MenuStrip nền 245, hover khung xanh | cố ý khác (dự án chọn giữ MenuStrip, chỉ sửa kích thước, 2026-10-29) |
| Menu thả xuống | Gốc là popup Win11 (bo góc, viền 229, nền 249, mục cao 35 px, hover 240 bo góc, không cột icon); ToolStripDropDown kiểu WinForms | cố ý khác (như trên) |
| Status bar: chiều cao (bằng `msctls_statusbar32` thật, 36 px), viền trên, vị trí chữ 4 ô, vị trí vạch chia, size grip | Trùng từng pixel | giống |
| Status bar: kiểu vạch chia | Gốc 1 px màu 215; StatusStrip etched 2 px (160/255) | cố ý khác (như trên) |
| Icon cửa sổ | Icon tự vẽ | cố ý khác (không dùng tài nguyên Microsoft) |

Size grip của gốc bị bỏ sót lần so đầu; đã thêm sau lần so ở mục "Đổi DPI".

<a id="doi-dpi"></a>

### Đổi DPI

Không có màn hình thứ hai nên đổi scale của màn hình duy nhất lúc đang chạy (`DisplayConfigSetDeviceInfo`, cùng việc Settings > Display > Scale làm), rồi trả về 150%. Registry export trước và khôi phục sau như trên. Ở mỗi mức có hai trường hợp: cửa sổ đang mở nhận `WM_DPICHANGED` thật, và cửa sổ mở mới ở mức đó. Cả hai cửa sổ đặt 1500×1050. So vùng Edit từng pixel; menu và status so vị trí pixel chữ (độ sáng < 100), vì màu nền và kiểu vạch chia vốn cố ý khác. Số liệu chính xác của gốc lấy bằng `GetMenuItemRect` và `SB_GETRECT`.

| Mức (DPI) | Vùng Edit | Menu (mở mới) | Status (mở mới) | Sau `WM_DPICHANGED` |
|---|---|---|---|---|
| 100% (96) | 0 pixel khác | trùng | trùng | trùng |
| 125% (120) | 0 pixel khác | trùng | trùng | Edit, status trùng; menu: xem dưới |
| 150% (144) | 0 pixel khác | trùng | trùng | (mức gốc) |
| 175% (168) | 0 pixel khác | trùng | trùng | Edit, status trùng; menu: xem dưới |
| 200% (192) | 0 pixel khác | trùng | trùng | Edit, status trùng; menu: xem dưới |

"Trùng" nghĩa là vị trí từng chữ (khung pixel chữ theo x và y) trùng gốc. Vài pixel ở viền chữ menu vẫn khác độ sáng (ví dụ 102 so với 96), vì chữ được làm mịn trên nền 255 (gốc) và 245 (MenuStrip). Khác biệt còn lại sau khi đổi DPI không thuộc phần port vẽ:
- Menu bar của chính gốc sau `WM_DPICHANGED` vẽ chữ thấp hơn 1 px (và ở 125%, 200% lệch phải 1 px) so với lúc gốc mở mới ở cùng DPI. Port vẽ như lúc mở mới, tức như gốc mới mở (cố ý khác: không chép trạng thái lệch tạm của USER32).
- Viền và góc bo của khung cửa sổ (DWM) khác màu theo cửa sổ nào đang được kích hoạt.

Các lỗi của port tìm ra qua lần so này và đã sửa:
- bố cục dùng kích thước client cũ (`Native_resizes_lay_out_with_the_new_client_size`);
- font menu và status không đổi theo DPI mới;
- lề và padding menu chỉ đúng ở 144 DPI: lệch 1 px mỗi mục ở 96 và 120 DPI, và ở 168 DPI. Nay lấy từ menu Win32 thật;
- chữ status lệch 1-2 px ở 96, 120, 192 DPI, kể cả theo chiều dọc. Nay đo trên status bar thật;
- thiếu size grip.

Test: `Layout_places_menu_editor_and_status_parts`, `Menu_items_take_the_geometry_of_a_win32_menu_bar_with_the_same_texts`, `Bar_fonts_come_from_the_non_client_metrics_of_the_window_dpi`, `Status_text_origin_is_measured_on_a_real_status_bar_and_the_gripper_shows`.

<a id="hop-thoai-tim-kiem"></a>

### Hộp thoại tìm kiếm (giai đoạn c)

So ngày 2026-10-29 cùng điều kiện như trên (registry export trước, tạm đặt font và `fReverse`/`fWrapAround`/`fMatchCase`/`searchString` về mặc định, khôi phục sau). Ảnh chụp theo `GetWindowRect` của hộp, so từng pixel.

| Hộp | Kết quả | Trạng thái |
|---|---|---|
| Find, Replace, Go To | 0 pixel khác, kể cả thanh tiêu đề; cùng vị trí trên màn hình | giống |
| "Cannot find", lỗi Go To | Khác duy nhất chữ tiêu đề (tên sản phẩm); cùng kích thước, vị trí; phần dưới thanh tiêu đề 0 pixel khác | cố ý khác (tên sản phẩm) |
| Gạch chân phím truy cập trong hộp | Theo trạng thái UI của hệ thống: mở bằng `WM_APPCOMMAND` (không qua phím) thì cả hai ẩn; sau Alt giả lập + phím tắt thì gốc lúc hiện lúc ẩn giữa các lần chạy, port cũng vậy | giống (không tất định ở cả hai) |

Lần so đầu tiên các hộp của port nằm cao hơn 30 px (bằng chiều cao menu ở 144 DPI); đã sửa bằng `DialogPlacement`.

Font mặc định trên máy này khác: `HKLM\Software\Microsoft\Notepad\DefaultFonts` có `Consolas`/110 nên Notepad gốc dùng Consolas 11; bản port đọc khóa máy riêng `HKLM\Software\NotepadClassic\DefaultFonts` (chưa có trên máy này) nên dùng Lucida Console 10.

<a id="trong-suot"></a>

## Trong suốt cửa sổ (tính năng thêm của port)

Bản gốc không có tính năng này; mọi dòng đều là **cố ý khác**. Khi cửa sổ đục (mặc định), hành vi và hình ảnh giống bản gốc, trừ mục menu mới và tổ hợp chuột bên dưới.

| Hành vi | Nơi cài đặt | Test | Trạng thái | Ghi chú |
|---|---|---|---|---|
| Menu `View > Transparency`: `More Transparent`, `Less Transparent`, `Opaque`; bước 10%, kẹp 30-100%; mục không còn tác dụng thì xám | `ViewModule`, `OpacityPolicy`, `Resources/Strings.resx` | `Transparency_commands_step_stop_at_the_limits_and_mark_settings_changed`, `Menu_bar_matches_the_original_resource_layout` | cố ý khác | Mức tối thiểu 30% để không mất dấu cửa sổ. Không thêm phím accelerator, nên hai bảng 20 và 8 phím giữ nguyên |
| Mờ cả cửa sổ gồm chữ, menu bar, status bar, title bar bằng `WS_EX_LAYERED` + `LWA_ALPHA`; 100% thì không có style layered | `MainForm.SetOpacity` (`Form.Opacity`) | `An_opaque_window_is_not_layered_and_the_menu_commands_change_its_alpha`, `The_saved_opacity_applies_when_the_window_is_created`, `Transparency_from_the_menu_changes_the_window_and_survives_a_restart` | cố ý khác | Khác Windows Terminal: chữ cũng mờ theo, vì `EDIT` vẽ bằng GDI nên không có alpha riêng cho từng pixel. Menu thả xuống và hộp thoại là cửa sổ riêng nên vẫn đục |
| `Ctrl+Shift+cuộn chuột` trên cửa sổ chính: lên đục hơn, xuống trong hơn | `KeyDispatcher.PreFilterMessage` | `Ctrl_shift_wheel_changes_opacity_and_is_kept_from_the_edit_control`, `Other_wheel_chords_stay_with_the_edit_control`, `The_wheel_chord_ignores_other_windows_and_an_open_menu`, `A_high_resolution_wheel_steps_once_per_notch_and_a_turn_back_starts_over` | cố ý khác | Trên bản gốc tổ hợp này tới `EDIT`; port giữ nó lại nên `Ctrl+cuộn` thường vẫn zoom như cũ, còn thêm Shift thì không zoom |
| Lưu ở giá trị `iOpacityPercent` (`REG_DWORD`) cùng nhóm Word Wrap + Status Bar; thiếu hoặc sai kiểu thì 100, ngoài khoảng thì kẹp | `RegistrySettingsStore` | `Opacity_is_read_within_its_limits`, `Opacity_defaults_to_opaque_is_saved_with_the_view_group_and_is_not_imported` | cố ý khác | Không import từ khóa của gốc |

<a id="chua-lam"></a>

## Các phần chưa làm

Không còn phần nào của các giai đoạn a-h. Các điểm "chưa xác minh" còn lại nằm trong ghi chú của từng bảng.

<a id="khong-port"></a>

## Không port

| Thành phần | Lý do |
|---|---|
| Telemetry TraceLogging, bộ đếm 16-bit | Yêu cầu dự án |
| WIL feature staging/usage reporting | Yêu cầu dự án |
| Hộp Find/Replace dự phòng (bản sao code COMDLG32) | Dự án chọn (2026-10-29); `FindTextW` lỗi thì báo chuỗi 7 |
| Watermark "Activate Windows" | Yêu cầu dự án |
| Banner Store | Yêu cầu dự án |
| EDP/WIP | Yêu cầu dự án. Điểm mở rộng `IFileAccessPolicy` (`CanOpen`, `AfterSave`), mặc định `AllowAllFileAccessPolicy`; `EN_BEFORE_PASTE` (0x800) không xử lý |
