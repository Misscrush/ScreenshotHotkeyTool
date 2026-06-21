$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$source = Get-Content -LiteralPath (Join-Path $root 'src\ScreenshotHotkeyTool.cs') -Raw -Encoding UTF8

if ($source -notmatch 'rectangleButton\.Click \+= delegate') {
    throw 'Preview toolbar should include a rectangle selection button.'
}

if ($source -notmatch 'internal enum AnnotationMode') {
    throw 'Canvas should use annotation modes for none, freehand, and rectangle.'
}

if ($source -notmatch 'AnnotationMode\.Rectangle') {
    throw 'Rectangle annotation mode is required.'
}

if ($source -notmatch 'DrawPreviewRectangle\(e\.Graphics, rectangleStartControlPoint, rectangleCurrentControlPoint\)') {
    throw 'Rectangle preview should use control coordinates so it follows the mouse.'
}

if ($source -notmatch 'DrawRectangle\(graphics, rectangleStartPoint, rectangleCurrentPoint\);\s*isDrawing = false;') {
    throw 'Rectangle tool should commit the rectangle on mouse up.'
}

Write-Host 'Rectangle annotation tool test passed.'
