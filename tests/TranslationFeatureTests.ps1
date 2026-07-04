$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$source = Get-Content -LiteralPath (Join-Path $root 'src\ScreenshotHotkeyTool.cs') -Raw -Encoding UTF8

if ($source -notmatch 'translateToEnglishButton') {
    throw 'OCR result window should include a translate-to-English button.'
}

if ($source -notmatch 'translateToChineseButton') {
    throw 'OCR result window should include a translate-to-Chinese button.'
}

if ($source -notmatch 'translateToGermanButton') {
    throw 'OCR result window should include a translate-to-German button.'
}

$toChineseLabel = ([char]0x8F6C).ToString() + ([char]0x4E2D).ToString() + ([char]0x6587).ToString()
$toEnglishLabel = ([char]0x8F6C).ToString() + ([char]0x82F1).ToString() + ([char]0x6587).ToString()
$toGermanLabel = ([char]0x8F6C).ToString() + ([char]0x5FB7).ToString() + ([char]0x6587).ToString()

if ($source -notmatch [Regex]::Escape($toChineseLabel)) {
    throw 'English OCR text should have a clear translate-to-Chinese button label.'
}

if ($source -notmatch [Regex]::Escape($toEnglishLabel)) {
    throw 'Chinese OCR text should have a clear translate-to-English button label.'
}

if ($source -notmatch [Regex]::Escape($toGermanLabel)) {
    throw 'OCR text should have a clear translate-to-German button label.'
}

if ($source -notmatch 'TranslateCurrentText\("de"') {
    throw 'OCR result window should translate to German.'
}

if ($source -notmatch 'TranslateInlineOcrText\("de"') {
    throw 'Inline OCR toolbar should translate to German.'
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

if ($source -notmatch 'BaiduTranslate') {
    throw 'Translation runner should support Baidu Translate for computers that cannot access Google.'
}

if ($source -notmatch 'TranslationProvider') {
    throw 'Settings should include a translation provider.'
}

if ($source -notmatch 'BaiduAppId') {
    throw 'Settings should include Baidu App ID.'
}

if ($source -notmatch 'BaiduSecretKey') {
    throw 'Settings should include Baidu Secret Key.'
}

if ($source -notmatch 'translationProviderBox') {
    throw 'OCR result window should expose a translation provider selector.'
}

if ($source -notmatch 'translationProviderLabel') {
    throw 'OCR result window should label the translation provider selector.'
}

if ($source -notmatch 'settings\.TranslationProvider = Convert\.ToString\(translationProviderBox\.SelectedItem\)') {
    throw 'Changing the OCR result translation provider should update settings.'
}

if ($source -notmatch 'settings\.Save\(\)') {
    throw 'Changing the OCR result translation provider should persist the choice.'
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

if ($source -notmatch 'showingTranslation') {
    throw 'OCR result window should track whether it is currently showing translated text.'
}

if ($source -notmatch 'textBeforeTranslation') {
    throw 'OCR result window should keep the source text so clicking translate again can restore it.'
}

if ($source -notmatch 'RestoreOriginalText') {
    throw 'OCR result window should restore the original text when a translation button is clicked again.'
}

if ($source -notmatch 'SetTranslationButtonLabels') {
    throw 'Translation buttons should switch between translate and restore labels.'
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

if ($source -notmatch 'GoogleTranslate\(normalized, targetLanguage\)') {
    throw 'Translation should send normalized text in a single request.'
}

Write-Host 'Translation feature test passed.'
