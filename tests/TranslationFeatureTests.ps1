$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$source = Get-Content -LiteralPath (Join-Path $root 'src\ScreenshotHotkeyTool.cs') -Raw -Encoding UTF8

if ($source -notmatch 'translateToEnglishButton') {
    throw 'OCR result window should include a translate-to-English button.'
}

if ($source -notmatch 'translateToChineseButton') {
    throw 'OCR result window should include a translate-to-Chinese button.'
}

if ($source -notmatch 'internal static class TranslationRunner') {
    throw 'Translation logic should be isolated in TranslationRunner.'
}

if ($source -notmatch 'translate.googleapis.com') {
    throw 'Translation runner should call a translation endpoint.'
}

if ($source -notmatch 'clients5.google.com') {
    throw 'Translation runner should try a fallback translation endpoint.'
}

if ($source -notmatch 'TimeoutWebClient') {
    throw 'Translation requests should use a timeout instead of hanging.'
}

if ($source -notmatch 'BuildTranslateUrls') {
    throw 'Translation endpoints should be built in one place.'
}

if ($source -notmatch 'TranslateCurrentText') {
    throw 'OCR result window should use a shared translation handler.'
}

if ($source -notmatch 'TranslatePreservingLines') {
    throw 'Translation should preserve line breaks where possible.'
}

if ($source -notmatch 'translationCache') {
    throw 'Translation results should be cached for repeated toggles.'
}

if ($source -match 'foreach\s*\(var line in lines\)[\s\S]*Translate\(line, targetLanguage\)') {
    throw 'Translation should not call the network once per line.'
}

if ($source -notmatch 'Translate\(normalized, targetLanguage\)') {
    throw 'Translation should send normalized text in a single request.'
}

Write-Host 'Translation feature test passed.'
