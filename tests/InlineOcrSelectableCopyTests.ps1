$ErrorActionPreference = 'Stop'

$source = Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\src\ScreenshotHotkeyTool.cs') -Raw -Encoding UTF8

if ($source -notmatch 'SelectionCopyMode') {
    throw 'Inline OCR text control should expose a selectable-copy mode.'
}

if ($source -notmatch '\\u9009\\u62e9\\u590d\\u5236') {
    throw 'Inline OCR toolbar should include a select-copy button.'
}

if ($source -notmatch '\\u590d\\u5236\\u9009\\u4e2d\\u6587\\u5b57') {
    throw 'Inline OCR selectable mode should offer right-click copy for selected text.'
}

if ($source -notmatch 'sender == inlineOcrBox && inlineOcrBox.SelectionCopyMode') {
    throw 'Inline OCR drag handlers should not steal mouse events in selectable-copy mode.'
}

Write-Host 'Inline OCR selectable copy test passed.'
