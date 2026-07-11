$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$source = Get-Content -LiteralPath (Join-Path $root 'src\ScreenshotHotkeyTool.cs') -Raw -Encoding UTF8

if ($source -notmatch 'foreach \(var pageSegMode in new\[\] \{ 6, 4, 11 \}\)') {
    throw 'OCR should try multiple page segmentation modes for mixed UI text.'
}

if ($source -notmatch 'ChooseBestOcrText') {
    throw 'OCR should choose the best candidate instead of returning the first non-empty result.'
}

if ($source -notmatch 'ScoreOcrCandidate') {
    throw 'OCR candidate selection should score text quality.'
}

if ($source -notmatch 'gibberishRuns') {
    throw 'OCR scoring should penalize obvious repeated-character gibberish.'
}

if ($source -match 'RunTesseract\(enginePath, inputPath, outputBasePath, language, tessdataDirectory, "tsv"\);\s*var formattedText = ReadTsvOutput') {
    throw 'OCR should not immediately return the first TSV result from psm 6.'
}

Write-Host 'OCR quality test passed.'
