<#
.SYNOPSIS
  Regenerates src/NotepadNet.App/app.ico from assets/icon.svg. The .ico is committed;
  rerun only after changing the SVG.
#>
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
dotnet run --project (Join-Path $PSScriptRoot 'IconGen') -c Release -- `
    (Join-Path $root 'assets\icon.svg') (Join-Path $root 'src\NotepadNet.App\app.ico')
if ($LASTEXITCODE -ne 0) { throw "IconGen failed with exit code $LASTEXITCODE" }
