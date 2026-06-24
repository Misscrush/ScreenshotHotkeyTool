$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$source = Get-Content -LiteralPath (Join-Path $root 'src\ScreenshotHotkeyTool.cs') -Raw -Encoding UTF8

if ($source -notmatch 'PrepareImageForOcr') {
    throw 'OCR should preprocess screenshots before sending them to Tesseract.'
}

if ($source -notmatch 'SetResolution\(300, 300\)') {
    throw 'OCR should save a 300 DPI image so small web text is not treated as low-resolution input.'
}

if ($source -notmatch 'InterpolationMode\.HighQualityBicubic') {
    throw 'OCR preprocessing should upscale screenshots with a high-quality resampler.'
}

if ($source -notmatch 'ApplyContrastForOcr') {
    throw 'OCR preprocessing should improve text/background contrast for web screenshots.'
}

if ($source -notmatch '--psm 6') {
    throw 'OCR should use a page segmentation mode suited to selected web text blocks.'
}

if ($source -notmatch '--oem 1') {
    throw 'OCR should use the LSTM OCR engine for better modern screen text recognition.'
}

Write-Host 'OCR web text quality test passed.'
