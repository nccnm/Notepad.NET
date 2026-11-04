<#
.SYNOPSIS
  Builds one setup exe from a folder with the published Notepad.NET.exe.
.DESCRIPTION
  Uses Inno Setup from the machine. On a CI runner without it, installs the pinned version, checked by its hash,
  into the temporary folder of the job.
#>
param(
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][ValidateSet('x64', 'arm64')][string]$Arch,
    [Parameter(Mandatory)][string]$SourceDir,
    [Parameter(Mandatory)][string]$OutDir
)
$ErrorActionPreference = 'Stop'

$InnoUrl = 'https://github.com/jrsoftware/issrc/releases/download/is-6_7_3/innosetup-6.7.3.exe'
$InnoSha256 = '9c73c3bae7ed48d44112a0f48e66742c00090bdb5bef71d9d3c056c66e97b732'

function Find-Compiler {
    $found = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($found) { return $found.Source }
    foreach ($folder in "${env:ProgramFiles(x86)}\Inno Setup 6", "$env:ProgramFiles\Inno Setup 6", "$env:RUNNER_TEMP\inno") {
        if (Test-Path -LiteralPath "$folder\ISCC.exe") { return "$folder\ISCC.exe" }
    }

    if (-not $env:GITHUB_ACTIONS) { throw 'Inno Setup 6 is needed to build the setup.' }
    $setup = "$env:RUNNER_TEMP\innosetup.exe"
    Invoke-WebRequest $InnoUrl -OutFile $setup
    if ((Get-FileHash $setup -Algorithm SHA256).Hash -ne $InnoSha256) { throw 'The Inno Setup download does not match its hash.' }
    $process = Start-Process $setup -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/CURRENTUSER', "/DIR=`"$env:RUNNER_TEMP\inno`"" -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "Inno Setup did not install: $($process.ExitCode)." }
    "$env:RUNNER_TEMP\inno\ISCC.exe"
}

New-Item -ItemType Directory -Force $OutDir | Out-Null
$compiler = Find-Compiler
& $compiler /Q "/DAppVersion=$Version" "/DArch=$Arch" "/DSourceDir=$((Resolve-Path $SourceDir).Path)" "/DOutDir=$((Resolve-Path $OutDir).Path)" (Join-Path $PSScriptRoot 'Notepad.NET.iss')
if ($LASTEXITCODE -ne 0) { throw "ISCC failed: $LASTEXITCODE." }
Join-Path (Resolve-Path $OutDir).Path "Notepad.NET-$Version-setup-$Arch.exe"
