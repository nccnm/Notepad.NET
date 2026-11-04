<#
.SYNOPSIS
  Makes the notepad command open Notepad.NET, or puts back what was there before.
.DESCRIPTION
  System32 comes before the user's PATH, so each place that takes the command needs its own setting:
  a function in the PowerShell profiles, a doskey macro in the AutoRun value of cmd, and the App Paths key for
  Win+R. -Disable removes only what -Enable added, and leaves anything changed since then alone. Runs under
  Windows PowerShell 5.1, which every supported Windows has. See docs/installer.md.
#>
param(
    [switch]$Enable,
    [switch]$Disable,
    [string]$Exe
)
$ErrorActionPreference = 'Stop'

$StatePath = 'Software\Notepad.NET Setup'
$AppPathsPath = 'Software\Microsoft\Windows\CurrentVersion\App Paths\notepad.exe'
$CommandProcessorPath = 'Software\Microsoft\Command Processor'
$BlockStart = '# >>> Notepad.NET >>>'
$BlockEnd = '# <<< Notepad.NET <<<'
$Hkcu = [Microsoft.Win32.Registry]::CurrentUser
$KeepNames = [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames

function Log($text) {
    Add-Content -LiteralPath (Join-Path $PSScriptRoot 'notepad-command.log') -Value "$(Get-Date -Format s) $text" -Encoding UTF8
}

# ---- PowerShell profiles ----

function Get-Profiles {
    $documents = [Environment]::GetFolderPath('MyDocuments')
    @(
        @{ Shell = 'pwsh.exe'; Path = Join-Path $documents 'PowerShell\Microsoft.PowerShell_profile.ps1' }
        @{ Shell = 'powershell.exe'; Path = Join-Path $documents 'WindowsPowerShell\Microsoft.PowerShell_profile.ps1' }
    )
}

# A profile a shell may not run would print an error at each start of that shell.
function Test-ShellRunsProfiles($shell) {
    if (-not (Get-Command $shell -ErrorAction SilentlyContinue)) { return $false }
    # The -ExecutionPolicy of this process is inherited through this variable.
    $inherited = $env:PSExecutionPolicyPreference
    $env:PSExecutionPolicyPreference = $null
    try {
        $policy = (& $shell -NoProfile -NonInteractive -Command 'Get-ExecutionPolicy' | Out-String).Trim()
    }
    finally {
        $env:PSExecutionPolicyPreference = $inherited
    }

    Log "$shell execution policy: $policy"
    $policy -notin 'Restricted', 'AllSigned'
}

# Keeps the bytes of the rest of the file, whatever its encoding; the block itself is plain ASCII.
function Get-ProfileEncoding([byte[]]$bytes) {
    if ($bytes.Length -ge 2 -and $bytes[0] -eq 0xFF -and $bytes[1] -eq 0xFE) { return [Text.Encoding]::Unicode }
    if ($bytes.Length -ge 2 -and $bytes[0] -eq 0xFE -and $bytes[1] -eq 0xFF) { return [Text.Encoding]::BigEndianUnicode }
    [Text.Encoding]::GetEncoding(28591)
}

function Remove-ProfileBlock($path) {
    if (-not (Test-Path -LiteralPath $path)) { return }
    $bytes = [IO.File]::ReadAllBytes($path)
    $encoding = Get-ProfileEncoding $bytes
    $text = $encoding.GetString($bytes)
    $pattern = '(?s)\r\n' + [regex]::Escape($BlockStart) + '.*?' + [regex]::Escape($BlockEnd) + '\r\n'
    $without = [regex]::Replace($text, $pattern, '')
    if ($without -eq $text) { return }
    if ($without.Length -eq 0) {
        [IO.File]::Delete($path)
        return
    }

    [IO.File]::WriteAllBytes($path, $encoding.GetBytes($without))
}

function Add-ProfileBlock($path, $exe) {
    $folder = Split-Path -Parent $path
    if (-not (Test-Path -LiteralPath $folder)) { [void][IO.Directory]::CreateDirectory($folder) }
    # Not through an if expression: PowerShell turns the empty array it returns into null.
    $bytes = New-Object byte[] 0
    if (Test-Path -LiteralPath $path) { $bytes = [IO.File]::ReadAllBytes($path) }
    $encoding = Get-ProfileEncoding $bytes
    $target = if ($exe -cmatch '^[\x20-\x7E]*$') {
        "'" + $exe.Replace("'", "''") + "'"
    }
    else {
        # Other characters would depend on the encoding of the profile.
        "([Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('" + [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($exe)) + "')))"
    }

    $block = "`r`n$BlockStart`r`nfunction notepad { & $target @args }`r`n$BlockEnd`r`n"
    [IO.File]::WriteAllBytes($path, $encoding.GetBytes($encoding.GetString($bytes) + $block))
}

# ---- cmd ----

function Get-Macro($exe) { 'doskey notepad="' + $exe + '" $*' }

function Remove-Macro($exe) {
    $key = $Hkcu.OpenSubKey($CommandProcessorPath, $true)
    if ($null -eq $key) { return }
    try {
        $value = $key.GetValue('AutoRun', $null, $KeepNames)
        if ($null -eq $value) { return }
        $macro = Get-Macro $exe
        $rest = if ($value -eq $macro) { '' } else { $value.Replace("$macro & ", '') }
        if ($rest -eq $value) { return }
        if ($rest.Length -eq 0) { $key.DeleteValue('AutoRun') } else { $key.SetValue('AutoRun', $rest, $key.GetValueKind('AutoRun')) }
    }
    finally {
        $key.Dispose()
    }
}

function Add-Macro($exe) {
    $key = $Hkcu.CreateSubKey($CommandProcessorPath)
    try {
        $macro = Get-Macro $exe
        $value = $key.GetValue('AutoRun', $null, $KeepNames)
        if ([string]::IsNullOrEmpty($value)) {
            $key.SetValue('AutoRun', $macro, [Microsoft.Win32.RegistryValueKind]::ExpandString)
        }
        else {
            $key.SetValue('AutoRun', "$macro & $value", $key.GetValueKind('AutoRun'))
        }
    }
    finally {
        $key.Dispose()
    }
}

# ---- Win+R ----

function Save-AppPaths($state) {
    $key = $Hkcu.OpenSubKey($AppPathsPath)
    if ($null -eq $key) { return }
    try {
        $state.SetValue('AppPathsExisted', 1, [Microsoft.Win32.RegistryValueKind]::DWord)
        foreach ($pair in @(@('', 'AppPathsDefault'), @('Path', 'AppPathsPath'))) {
            $value = $key.GetValue($pair[0], $null, $KeepNames)
            if ($null -ne $value) { $state.SetValue($pair[1], $value, $key.GetValueKind($pair[0])) }
        }
    }
    finally {
        $key.Dispose()
    }
}

function Set-AppPaths($exe) {
    $key = $Hkcu.CreateSubKey($AppPathsPath)
    try {
        $key.SetValue('', $exe)
        $key.SetValue('Path', (Split-Path -Parent $exe))
    }
    finally {
        $key.Dispose()
    }
}

# Something else that took the key since then, such as an update of the Store Notepad, keeps it.
function Restore-AppPaths($state, $exe) {
    $key = $Hkcu.OpenSubKey($AppPathsPath, $true)
    if ($null -eq $key) { return }
    try {
        if ($key.GetValue('') -ne $exe) {
            Log 'App Paths was changed by something else: left alone'
            return
        }

        if ($null -eq $state.GetValue('AppPathsExisted')) {
            $key.Dispose()
            $key = $null
            $Hkcu.DeleteSubKeyTree($AppPathsPath)
            return
        }

        foreach ($pair in @(@('', 'AppPathsDefault'), @('Path', 'AppPathsPath'))) {
            $value = $state.GetValue($pair[1], $null, $KeepNames)
            if ($null -ne $value) {
                $key.SetValue($pair[0], $value, $state.GetValueKind($pair[1]))
            }
            elseif ($null -ne $key.GetValue($pair[0])) {
                $key.DeleteValue($pair[0])
            }
        }
    }
    finally {
        if ($null -ne $key) { $key.Dispose() }
    }
}

# ---- The two actions ----

function Invoke-Disable {
    $state = $Hkcu.OpenSubKey($StatePath)
    if ($null -eq $state) { return }
    try {
        $exe = $state.GetValue('Exe')
        foreach ($item in Get-Profiles) { Remove-ProfileBlock $item.Path }
        Remove-Macro $exe
        Restore-AppPaths $state $exe
    }
    finally {
        $state.Dispose()
    }

    $Hkcu.DeleteSubKeyTree($StatePath)
    Log 'Disabled'
}

function Invoke-Enable($exe) {
    $state = $Hkcu.OpenSubKey($StatePath, $true)
    if ($null -eq $state) {
        # What the first -Enable found is what -Disable puts back; a repeat, as in an upgrade, keeps it.
        $state = $Hkcu.CreateSubKey($StatePath)
        Save-AppPaths $state
    }

    try {
        $previous = $state.GetValue('Exe')
        if ($null -ne $previous) { Remove-Macro $previous }
        foreach ($item in Get-Profiles) {
            Remove-ProfileBlock $item.Path
            if (Test-ShellRunsProfiles $item.Shell) {
                Add-ProfileBlock $item.Path $exe
            }
            else {
                Log "$($item.Shell) is missing or may not run profiles: skipped"
            }
        }

        Add-Macro $exe
        Set-AppPaths $exe
        $state.SetValue('Exe', $exe)
    }
    finally {
        $state.Dispose()
    }

    Log "Enabled for $exe"
}

if ($Enable -eq $Disable) { throw 'Give either -Enable -Exe <path> or -Disable.' }
try {
    if ($Enable) {
        if ([string]::IsNullOrEmpty($Exe)) { throw '-Enable needs -Exe.' }
        Invoke-Enable $Exe
    }
    else {
        Invoke-Disable
    }
}
catch {
    Log "Failed: $_"
    throw
}
