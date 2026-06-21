$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$source = Get-Content -LiteralPath (Join-Path $root 'src\ScreenshotHotkeyTool.cs') -Raw -Encoding UTF8

if ($source -notmatch 'GetImagesForOcr') {
    throw 'Preview OCR should support multiple selected regions.'
}

if ($source -notmatch 'RecognizeImages') {
    throw 'Preview should combine OCR results from multiple selected regions.'
}

if ($source -notmatch 'AnnotationMode\.Text') {
    throw 'Canvas should include a text annotation mode.'
}

if ($source -notmatch 'textButton') {
    throw 'Preview toolbar should include a text button.'
}

if ($source -notmatch 'TextAnnotationForm') {
    throw 'Text annotation should prompt for user text.'
}

if ($source -notmatch 'DrawTextAnnotation') {
    throw 'Canvas should draw text annotations onto the screenshot.'
}

if ($source -notmatch 'Mode == AnnotationMode\.Text && isDrawing') {
    throw 'Text annotation should use a dragged rectangle area.'
}

if ($source -notmatch 'DrawTextAnnotation\(NormalizeRectangle\(rectangleStartPoint, rectangleCurrentPoint\), form\.AnnotationText\)') {
    throw 'Text annotation should draw text inside the selected rectangle.'
}

Write-Host 'Text annotation and multi OCR test passed.'
