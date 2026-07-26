$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$page = Join-Path $PSScriptRoot 'scrolling-test-page.html'
$exe = Join-Path $root 'ScreenshotHotkeyTool.exe'
$out = Join-Path $root 'scrolling-smoke-output.png'

$chrome = 'C:\Program Files\Google\Chrome\Application\chrome.exe'
if (-not (Test-Path -LiteralPath $chrome)) {
    $chrome = 'C:\Program Files (x86)\Google\Chrome\Application\chrome.exe'
}
if (-not (Test-Path -LiteralPath $chrome)) {
    throw 'Chrome not found.'
}

Start-Process -FilePath $chrome -ArgumentList @('--new-window', (New-Object System.Uri($page)).AbsoluteUri)
Start-Sleep -Seconds 3

Add-Type -AssemblyName System.Drawing
$asm = [Reflection.Assembly]::LoadFile($exe)
$type = $asm.GetType('ScreenshotHotkeyTool.ScrollingCaptureRunner', $true)
$method = $type.GetMethod('Capture', [Reflection.BindingFlags]'Public, Static')
$countProp = $type.GetProperty('LastCaptureCount', [Reflection.BindingFlags]'Public, Static')

$region = [System.Drawing.Rectangle]::new(80, 120, 1360, 650)
$bitmap = $method.Invoke($null, @($region))
try {
    $bitmap.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
    $frames = $countProp.GetValue($null, $null)
    Write-Host "Saved=$out"
    Write-Host "Frames=$frames"
    Write-Host "Size=$($bitmap.Width)x$($bitmap.Height)"
    if ($frames -lt 3) {
        throw "Expected at least 3 scrolling frames, got $frames."
    }
    if ($bitmap.Height -le 1300) {
        throw "Expected a tall stitched image, got height $($bitmap.Height)."
    }
}
finally {
    if ($bitmap -ne $null) {
        $bitmap.Dispose()
    }
}
