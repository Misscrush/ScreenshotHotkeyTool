$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$source = Get-Content -LiteralPath (Join-Path $root 'src\ScreenshotHotkeyTool.cs') -Raw -Encoding UTF8

if ($source -match 'using \(var preview = new PreviewForm\(image, SaveBitmap\)\)\s*\{\s*preview\.ShowDialog\(\);\s*\}') {
    throw 'Preview should not be shown modally, otherwise the hotkey stays blocked until preview is closed.'
}

if ($source -notmatch 'isCapturing = false;\s*var preview = new PreviewForm\(image, SaveBitmap, RecognizeText, settings\);\s*preview\.Show\(\);') {
    throw 'SaveCapturedImage should release capture state before showing a modeless preview.'
}

Write-Host 'Preview non-blocking hotkey test passed.'
