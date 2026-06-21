$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$source = Get-Content -LiteralPath (Join-Path $root 'src\ScreenshotHotkeyTool.cs') -Raw -Encoding UTF8

if ($source -notmatch 'GetImageForOcr') {
    throw 'Preview OCR should request an OCR-specific image from the canvas.'
}

if ($source -notmatch 'lastRectangleSelection') {
    throw 'Canvas should remember the last rectangle selection for OCR.'
}

if ($source -notmatch 'CropBitmap\(originalImage, rectangle\)') {
    throw 'OCR should crop the original screenshot when a rectangle selection exists.'
}

if ($source -notmatch 'rectangleSelections\.Clear\(\)') {
    throw 'Clear should remove rectangle selections.'
}

if ($source -notmatch 'PopRectangleSelection\(\)') {
    throw 'Undo should remove the matching rectangle selection.'
}

Write-Host 'OCR rectangle selection test passed.'
