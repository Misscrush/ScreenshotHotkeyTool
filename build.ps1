$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$src = Join-Path $root 'src\ScreenshotHotkeyTool.cs'
$out = Join-Path $root 'ScreenshotHotkeyTool.exe'
$icon = Join-Path $root 'app.ico'
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'

if (-not (Test-Path -LiteralPath $csc)) {
    throw "Compiler not found: $csc"
}

Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class IconNative {
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool DestroyIcon(IntPtr hIcon);
}
'@

$bitmap = New-Object System.Drawing.Bitmap 64, 64
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.Clear([System.Drawing.Color]::Transparent)
$background = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(24,119,242))
$whitePen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), 6
$palePen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(180,230,245,255)), 4
$graphics.FillEllipse($background, 4, 4, 56, 56)
$graphics.DrawLine($whitePen, 18, 22, 18, 16)
$graphics.DrawLine($whitePen, 18, 16, 26, 16)
$graphics.DrawLine($whitePen, 46, 22, 46, 16)
$graphics.DrawLine($whitePen, 46, 16, 38, 16)
$graphics.DrawLine($whitePen, 18, 42, 18, 48)
$graphics.DrawLine($whitePen, 18, 48, 26, 48)
$graphics.DrawLine($whitePen, 46, 42, 46, 48)
$graphics.DrawLine($whitePen, 46, 48, 38, 48)
$graphics.DrawRectangle($palePen, 24, 24, 16, 16)
$handle = $bitmap.GetHicon()
try {
    $iconObject = [System.Drawing.Icon]::FromHandle($handle)
    $stream = [System.IO.File]::Create($icon)
    try {
        $iconObject.Save($stream)
    }
    finally {
        $stream.Dispose()
        $iconObject.Dispose()
    }
}
finally {
    [IconNative]::DestroyIcon($handle) | Out-Null
    $palePen.Dispose()
    $whitePen.Dispose()
    $background.Dispose()
    $graphics.Dispose()
    $bitmap.Dispose()
}

& $csc /nologo /codepage:65001 /target:winexe /platform:anycpu /optimize+ `
    /win32icon:$icon `
    /reference:System.dll `
    /reference:System.Drawing.dll `
    /reference:System.Web.Extensions.dll `
    /reference:System.Windows.Forms.dll `
    /out:$out `
    $src

if ($LASTEXITCODE -ne 0) {
    throw "Build failed"
}

Write-Host "Built: $out"
