# Temporary: pictures of the wizard pages on the CI runner, for a look at them.
param([string]$Setup, [string]$OutDir)
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
New-Item -ItemType Directory -Force $OutDir | Out-Null
function Shot($name) {
    $bounds = [Windows.Forms.Screen]::PrimaryScreen.Bounds
    $bitmap = New-Object Drawing.Bitmap $bounds.Width, $bounds.Height
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $graphics.CopyFromScreen($bounds.Location, [Drawing.Point]::Empty, $bounds.Size)
    $bitmap.Save((Join-Path (Resolve-Path $OutDir).Path "$name.png"))
    $graphics.Dispose()
    $bitmap.Dispose()
}
$process = Start-Process $Setup -PassThru
try {
    Start-Sleep -Seconds 5
    Shot '1-license'
    [Windows.Forms.SendKeys]::SendWait('%a')
    Start-Sleep -Milliseconds 700
    [Windows.Forms.SendKeys]::SendWait('{ENTER}')
    Start-Sleep -Seconds 2
    Shot '2-after-license'
    [Windows.Forms.SendKeys]::SendWait('{ENTER}')
    Start-Sleep -Seconds 2
    Shot '3-next'
    [Windows.Forms.SendKeys]::SendWait('{ENTER}')
    Start-Sleep -Seconds 2
    Shot '4-next'
}
finally {
    Get-Process | Where-Object { $_.ProcessName -like 'Notepad.NET*setup*' -or $_.ProcessName -like 'is-*' } | Stop-Process -Force -ErrorAction SilentlyContinue
    Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2
}
