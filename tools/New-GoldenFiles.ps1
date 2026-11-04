<#
.SYNOPSIS
  Regenerates the encoding golden files in tests/NotepadNet.Platform.Tests/TestData/encoding.
  The files are committed; rerun only to change the set. Every file is described in the README there.
#>
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$dir = Join-Path $PSScriptRoot '..\tests\NotepadNet.Platform.Tests\TestData\encoding'
New-Item -ItemType Directory -Force $dir | Out-Null

# Code page encodings (1258, 936, 54936) need the provider on .NET; Windows PowerShell has them built in.
if ($PSVersionTable.PSEdition -eq 'Core') {
    [System.Text.Encoding]::RegisterProvider([System.Text.CodePagesEncodingProvider]::Instance)
}

function Save([string]$name, [byte[]]$bytes) { [System.IO.File]::WriteAllBytes((Join-Path $dir $name), $bytes) }
function Bytes([string]$text, [System.Text.Encoding]$enc) { , $enc.GetBytes($text) }

$vi = "Tiếng Việt có dấu: Xin chào thế giới.`r`n"

# CP1258 has precomposed letters for circumflex/breve/horn but only combining tone marks
# (grave, acute, tilde, hook above, dot below). Split each character into that form first.
function ConvertTo-Cp1258Form([string]$text) {
    $tones = @(0x0300, 0x0301, 0x0303, 0x0309, 0x0323)
    $sb = [System.Text.StringBuilder]::new()
    foreach ($ch in $text.ToCharArray()) {
        $parts = ([string]$ch).Normalize([System.Text.NormalizationForm]::FormD).ToCharArray()
        $tone = $parts | Where-Object { $tones -contains [int]$_ }
        $base = -join ($parts | Where-Object { $tones -notcontains [int]$_ })
        [void]$sb.Append($base.Normalize([System.Text.NormalizationForm]::FormC))
        if ($tone) { [void]$sb.Append($tone) }
    }
    $sb.ToString()
}
$zh = "中文测试，你好世界。`r`n"
$utf8 = [System.Text.UTF8Encoding]::new($false)

Save 'utf8-bom.txt'      ([byte[]](0xEF, 0xBB, 0xBF) + (Bytes $vi $utf8))
Save 'utf8-nobom.txt'    (Bytes $vi $utf8)
Save 'utf16le-bom.txt'   ([byte[]](0xFF, 0xFE) + (Bytes $vi ([System.Text.Encoding]::Unicode)))
Save 'utf16be-bom.txt'   ([byte[]](0xFE, 0xFF) + (Bytes $vi ([System.Text.Encoding]::BigEndianUnicode)))
Save 'utf16le-nobom.txt' (Bytes "Hello world, this is UTF-16 LE text without a byte order mark.`r`n" ([System.Text.Encoding]::Unicode))
$cp1258 = [System.Text.Encoding]::GetEncoding(1258, [System.Text.EncoderFallback]::ExceptionFallback, [System.Text.DecoderFallback]::ExceptionFallback)
Save 'ansi-vi-1258.txt'  (Bytes (ConvertTo-Cp1258Form $vi) $cp1258)
Save 'ansi-zh-936.txt'   (Bytes $zh ([System.Text.Encoding]::GetEncoding(936)))
Save 'gb18030.txt'       (Bytes ($zh + [char]::ConvertFromUtf32(0x20000)) ([System.Text.Encoding]::GetEncoding(54936)))
Save 'empty.txt'         ([byte[]]@())
Save 'nul-chars.txt'     ([byte[]](0x61, 0x62, 0x00, 0x63, 0x00, 0x00, 0x64))
Save 'bush.txt'          ([System.Text.Encoding]::ASCII.GetBytes('Bush hid the facts'))
Save 'utf16-odd.txt'     ([byte[]](0xFF, 0xFE, 0x41, 0x00, 0x42))
Save 'log.txt'           ([System.Text.Encoding]::ASCII.GetBytes(".LOG`r`nold note`r`n"))

# 2000 ASCII bytes with one invalid UTF-8 byte at offset 1500: the 1 KiB sniff says UTF-8, the full load says ANSI.
$late = [byte[]]::new(2000); for ($i = 0; $i -lt 2000; $i++) { $late[$i] = 0x61 }; $late[1500] = 0xE9
Save 'utf8-invalid-after-1k.txt' $late

# Valid UTF-8 whose three-byte character U+1E00 starts at offset 1022 and ends past the 1 KiB boundary.
$split = [byte[]]::new(1100); for ($i = 0; $i -lt 1100; $i++) { $split[$i] = 0x61 }
$split[1022] = 0xE1; $split[1023] = 0xB8; $split[1024] = 0x80
Save 'utf8-split-at-1k.txt' $split
