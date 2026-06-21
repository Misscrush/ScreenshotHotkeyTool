$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$source = Get-Content -LiteralPath (Join-Path $root 'src\ScreenshotHotkeyTool.cs') -Raw -Encoding UTF8

if ($source -notmatch 'private readonly HotkeyWindow screenshotHotkeyWindow;') {
    throw 'App should have a separate screenshot hotkey window.'
}
if ($source -notmatch 'private readonly HotkeyWindow ocrHotkeyWindow;') {
    throw 'App should have a separate OCR hotkey window.'
}
if ($source -notmatch 'TriggerOcr') {
    throw 'OCR hotkey should trigger a dedicated OCR capture flow.'
}
if ($source -notmatch 'OcrEnabled') {
    throw 'Settings should include an OCR enabled flag.'
}
if ($source -notmatch 'OcrLanguage') {
    throw 'Settings should include OCR language.'
}
if ($source -notmatch 'OcrEnginePath') {
    throw 'Settings should include OCR engine path.'
}
if ($source -notmatch 'internal static class OcrRunner') {
    throw 'OCR execution should be isolated in OcrRunner.'
}
if ($source -notmatch 'internal sealed class OcrResultForm') {
    throw 'OCR text should be shown in a result window.'
}
if ($source -notmatch 'ocrButton') {
    throw 'Preview window should include an OCR button.'
}

Write-Host 'OCR feature structure test passed.'
