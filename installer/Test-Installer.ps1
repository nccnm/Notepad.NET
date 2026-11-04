<#
.SYNOPSIS
  Installs, upgrades and removes a real setup exe and checks what each step leaves behind.
.DESCRIPTION
  Changes the PowerShell profiles, AutoRun and App Paths of the current user, so it runs only on a CI runner.
#>
param([Parameter(Mandatory)][string]$Setup)
$ErrorActionPreference = 'Stop'
if (-not $env:GITHUB_ACTIONS) { throw 'This test changes the settings of the current user: it runs only on a CI runner.' }

$Setup = (Resolve-Path $Setup).Path
$InstallDir = Join-Path $env:LOCALAPPDATA 'Programs\Notepad.NET'
$Exe = Join-Path $InstallDir 'Notepad.NET.exe'
$AppPaths = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\App Paths\notepad.exe'
$CommandProcessor = 'HKCU:\Software\Microsoft\Command Processor'
$Documents = [Environment]::GetFolderPath('MyDocuments')
$Profile7 = Join-Path $Documents 'PowerShell\Microsoft.PowerShell_profile.ps1'
$Profile5 = Join-Path $Documents 'WindowsPowerShell\Microsoft.PowerShell_profile.ps1'
$script:failures = 0

function Check($what, $condition) {
    if ($condition) { "  ok: $what" } else { "  FAILED: $what"; $script:failures++ }
}

function Run-Setup($file, [string[]]$arguments) {
    $process = Start-Process $file -ArgumentList (@('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART') + $arguments) -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "$file $arguments exited with $($process.ExitCode)." }
}

function Install([string]$tasks) {
    Run-Setup $Setup @("/TASKS=`"$tasks`"", "/LOG=`"$env:RUNNER_TEMP\setup.log`"")
    $log = Join-Path $InstallDir 'notepad-command.log'
    if (Test-Path -LiteralPath $log) { Get-Content -LiteralPath $log | Select-Object -Last 3 | ForEach-Object { "  log: $_" } }
}

# unins000.exe hands over to a copy of itself and returns at once; it removes itself last.
function Uninstall {
    $uninstaller = Join-Path $InstallDir 'unins000.exe'
    Run-Setup $uninstaller @()
    for ($i = 0; $i -lt 300 -and (Test-Path -LiteralPath $uninstaller); $i++) { Start-Sleep -Milliseconds 100 }
    if (Test-Path -LiteralPath $uninstaller) { throw 'The uninstaller did not finish.' }
}

function Hash($path) { if (Test-Path -LiteralPath $path) { (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash } else { 'none' } }
function AutoRun { (Get-ItemProperty $CommandProcessor -Name AutoRun -ErrorAction SilentlyContinue).AutoRun }
function AppPathDefault { if (Test-Path $AppPaths) { (Get-Item $AppPaths).GetValue('') } else { 'none' } }
function AppPathFolder { if (Test-Path $AppPaths) { (Get-Item $AppPaths).GetValue('Path') } else { 'none' } }

function Clear-All {
    Remove-Item $AppPaths -Recurse -Force -ErrorAction SilentlyContinue
    Remove-ItemProperty $CommandProcessor -Name AutoRun -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $Profile7, $Profile5 -Force -ErrorAction SilentlyContinue
}

function Show-SetupLog {
    Select-String -LiteralPath "$env:RUNNER_TEMP\setup.log" -Pattern 'Tasks|Filename:|powershell|exit code|Process exit' | ForEach-Object { "  setup: $($_.Line)" }
}

# With a loaded profile, as a user gets it, and the module paths of that shell and not of this one.
function Get-Definition($shell) {
    $inherited = $env:PSModulePath
    $env:PSModulePath = $null
    try {
        (& $shell -NonInteractive -Command '(Get-Command notepad).CommandType; (Get-Command notepad).Definition' 2>&1 | Out-String).Trim()
    }
    finally {
        $env:PSModulePath = $inherited
    }
}

function Expect-Enabled {
    Check 'App Paths points at the app' ((AppPathDefault) -eq $Exe -and (AppPathFolder) -eq $InstallDir)
    Check 'AutoRun has the macro' ((AutoRun) -like "*doskey notepad=*Notepad.NET.exe*")
    if (Get-Command pwsh -ErrorAction SilentlyContinue) {
        $definition = Get-Definition 'pwsh'
        Check "pwsh takes the function ($definition)" ($definition -like "Function*Notepad.NET.exe*")
    }

    Check 'Windows PowerShell takes the function' ((Get-Definition 'powershell.exe') -like 'Function*Notepad.NET.exe*')

    $macros = (cmd /c 'doskey /macros' | Out-String).Trim()
    Check "a new cmd has the macro ($macros)" ($macros -like 'notepad=*Notepad.NET.exe*')
}

"== Without the task, nothing of the user changes"
Clear-All
$settingsKey = 'HKCU:\Software\Notepad.NET'
Check 'no settings key yet' (-not (Test-Path $settingsKey))
Install ''
Check 'the exe is installed' (Test-Path -LiteralPath $Exe)
Check 'the settings key stays for the first run of the app' (-not (Test-Path $settingsKey))
Check 'App Paths untouched' ((AppPathDefault) -eq 'none')
Check 'AutoRun untouched' ($null -eq (AutoRun))
Check 'profile untouched' ((Hash $Profile7) -eq 'none' -and (Hash $Profile5) -eq 'none')
Check 'a Start menu shortcut' (Test-Path -LiteralPath (Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Notepad.NET.lnk'))
Check 'Open with knows the app' (Test-Path 'HKCU:\Software\Classes\Applications\Notepad.NET.exe\shell\open\command')
Uninstall
Check 'the exe is gone' (-not (Test-Path -LiteralPath $Exe))
Check 'Open with forgets the app' (-not (Test-Path 'HKCU:\Software\Classes\Applications\Notepad.NET.exe'))

"== With the task on a machine without any of these settings"
Clear-All
Install 'notepadcommand'
Show-SetupLog
Expect-Enabled
Uninstall
Check 'App Paths gone' ((AppPathDefault) -eq 'none')
Check 'AutoRun gone' ($null -eq (AutoRun))
Check 'profiles gone' ((Hash $Profile7) -eq 'none' -and (Hash $Profile5) -eq 'none')
Check 'no state left' (-not (Test-Path 'HKCU:\Software\Notepad.NET Setup'))

"== What the user had comes back byte for byte"
Clear-All
New-Item $AppPaths -Force | Out-Null
Set-ItemProperty $AppPaths '(default)' 'C:\Before\notepad.exe'
Set-ItemProperty $AppPaths 'Path' 'C:\Before'
New-Item $CommandProcessor -Force -ErrorAction SilentlyContinue | Out-Null
New-ItemProperty $CommandProcessor -Name AutoRun -Value 'doskey notepad="C:\Before\notepad.exe" $*' -PropertyType String -Force | Out-Null
New-Item -ItemType Directory -Force (Split-Path $Profile7), (Split-Path $Profile5) | Out-Null
[IO.File]::WriteAllBytes($Profile7, [byte[]](0xEF, 0xBB, 0xBF) + [Text.Encoding]::UTF8.GetBytes("function notepad { 'mine' }`n# ti$([char]0x1EBF)ng Vi$([char]0x1EC7)t, no newline at the end"))
[IO.File]::WriteAllBytes($Profile5, [Text.Encoding]::Unicode.GetPreamble() + [Text.Encoding]::Unicode.GetBytes("# utf-16`r`n"))
$hash7 = Hash $Profile7
$hash5 = Hash $Profile5
Install 'notepadcommand'
Expect-Enabled
Check 'the earlier AutoRun still runs' ((AutoRun) -like 'doskey notepad="C:\Before\notepad.exe" $`* & *')

"== An upgrade keeps it enabled and what -Disable will put back"
Install 'notepadcommand'
Expect-Enabled
Check 'the macro is there once' (([regex]::Matches((AutoRun), 'Notepad\.NET\.exe')).Count -eq 1)

"== Turning the task off in a later run restores everything"
Install ''
Check 'App Paths restored' ((AppPathDefault) -eq 'C:\Before\notepad.exe' -and (AppPathFolder) -eq 'C:\Before')
Check 'AutoRun restored' ((AutoRun) -ceq 'doskey notepad="C:\Before\notepad.exe" $*')
Check 'pwsh profile restored byte for byte' ((Hash $Profile7) -eq $hash7)
Check 'Windows PowerShell profile restored byte for byte' ((Hash $Profile5) -eq $hash5)

"== Removing the app restores everything"
Install 'notepadcommand'
Uninstall
Check 'App Paths restored' ((AppPathDefault) -eq 'C:\Before\notepad.exe' -and (AppPathFolder) -eq 'C:\Before')
Check 'AutoRun restored' ((AutoRun) -ceq 'doskey notepad="C:\Before\notepad.exe" $*')
Check 'pwsh profile restored byte for byte' ((Hash $Profile7) -eq $hash7)
Check 'Windows PowerShell profile restored byte for byte' ((Hash $Profile5) -eq $hash5)

"== App Paths taken by something else stays with it"
Install 'notepadcommand'
Set-ItemProperty $AppPaths '(default)' 'C:\Store\notepad.exe'
Uninstall
Check 'the newer App Paths stays' ((AppPathDefault) -eq 'C:\Store\notepad.exe')

"== A shell that may not run profiles gets no profile"
Clear-All
$policyKey = 'HKCU:\Software\Microsoft\PowerShell\1\ShellIds\Microsoft.PowerShell'
New-Item $policyKey -Force | Out-Null
Set-ItemProperty $policyKey ExecutionPolicy Restricted
Install 'notepadcommand'
Check 'no Windows PowerShell profile that would print an error' ((Hash $Profile5) -eq 'none')
$log = Get-Content -LiteralPath (Join-Path $InstallDir 'notepad-command.log') -Raw
Check 'the policy was read' ($log -like '*powershell.exe execution policy: Restricted*')
Check 'the reason is in the log' ($log -like '*powershell.exe is missing or may not run profiles*')
Check 'Windows PowerShell starts without an error about its profile' ((Get-Definition 'powershell.exe') -like 'Application*notepad.exe*')
Uninstall
Check 'the install folder is gone' (-not (Test-Path -LiteralPath $InstallDir))
Remove-ItemProperty $policyKey ExecutionPolicy

"== Settings and notes outlive the app"
Clear-All
Install ''
New-Item $settingsKey -Force | Out-Null
$notes = Join-Path $env:LOCALAPPDATA 'Notepad.NET\Autosave'
New-Item -ItemType Directory -Force $notes | Out-Null
Set-Content (Join-Path $notes 'note.txt') 'only copy'
Uninstall
Check 'the settings key stays' (Test-Path $settingsKey)
Check 'the note stays' (Test-Path -LiteralPath (Join-Path $notes 'note.txt'))
Remove-Item $settingsKey -Recurse -Force
Remove-Item (Join-Path $env:LOCALAPPDATA 'Notepad.NET') -Recurse -Force
Clear-All

if ($script:failures -gt 0) { throw "$($script:failures) checks failed." }
"All checks passed."
