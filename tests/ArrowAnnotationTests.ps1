$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$source = Get-Content -LiteralPath (Join-Path $root 'src\ScreenshotHotkeyTool.cs') -Raw -Encoding UTF8

if ($source -notmatch 'AnnotationMode\.Arrow') {
    throw 'Canvas should include an arrow annotation mode.'
}

if ($source -notmatch 'arrowButton') {
    throw 'Preview toolbar should include an arrow button.'
}

if ($source -notmatch 'DrawArrow\(graphics, rectangleStartPoint, rectangleCurrentPoint\)') {
    throw 'Arrow tool should commit an arrow on mouse up.'
}

if ($source -notmatch 'DrawPreviewArrow\(e\.Graphics, rectangleStartControlPoint, rectangleCurrentControlPoint\)') {
    throw 'Arrow tool should preview the arrow while dragging.'
}

if ($source -notmatch 'AdjustableArrowCap') {
    throw 'Arrow drawing should use a real arrow head.'
}

Write-Host 'Arrow annotation test passed.'
