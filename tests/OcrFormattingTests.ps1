$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$source = Get-Content -LiteralPath (Join-Path $root 'src\ScreenshotHotkeyTool.cs') -Raw -Encoding UTF8

if ($source -notmatch 'preserve_interword_spaces=1') {
    throw 'OCR should ask Tesseract to preserve spacing from the image.'
}

if ($source -notmatch 'ReadTsvOutput') {
    throw 'OCR should read TSV output to reconstruct the original text layout.'
}

if ($source -notmatch 'tessedit_create_tsv=1') {
    throw 'OCR should create TSV output without depending on external config files.'
}

if ($source -notmatch 'ReconstructFormattedText') {
    throw 'OCR should reconstruct line breaks and spacing from word coordinates.'
}

if ($source -notmatch 'RemoveCjkInterCharacterSpaces') {
    throw 'OCR should remove false spaces inserted between CJK characters.'
}

if ($source -notmatch '\\u4e00-\\u9fff') {
    throw 'CJK spacing cleanup should target Chinese characters specifically.'
}

if ($source -match 'return File\.ReadAllText\(outputTextPath\)\.Trim\(\);') {
    throw 'OCR runner should not trim recognized text because it removes original formatting.'
}

if ($source -match 'parts\.Add\(text\.Trim\(\)\);') {
    throw 'Multi-region OCR should not trim each region result.'
}

if ($source -notmatch 'formatButton') {
    throw 'OCR result window should include a remove-formatting button.'
}

if ($source -notmatch 'formattedText') {
    throw 'OCR result window should keep the original formatted text for restoring.'
}

if ($source -notmatch 'formatRemoved') {
    throw 'OCR result window should track whether formatting is currently removed.'
}

if ($source -notmatch 'formatRemoved = true;') {
    throw 'Remove-formatting button should switch to restore-formatting mode.'
}

if ($source -notmatch 'formatRemoved = false;') {
    throw 'Restore-formatting mode should switch back to remove-formatting mode.'
}

if ($source -notmatch 'RemoveTextFormatting') {
    throw 'OCR result window should have a formatting cleanup helper.'
}

if ($source -notmatch 'WordWrap = false') {
    throw 'OCR result window should keep original line layout instead of wrapping text.'
}

Write-Host 'OCR formatting test passed.'
