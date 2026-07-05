$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$source = Get-Content -LiteralPath (Join-Path $root 'src\ScreenshotHotkeyTool.cs') -Raw -Encoding UTF8
$inlineStartMethod = [regex]::Match($source, 'private void StartScreenshotEditorSelection[\s\S]*?private void StartSelection').Value

if ($source -notmatch 'StartScreenshotEditorSelection') {
    throw 'Screenshot hotkey should start the inline editor selection flow.'
}

if ($source -notmatch 'private void TriggerOcr\(\)[\s\S]*StartScreenshotEditorSelection\(true\);') {
    throw 'OCR hotkey should use the same inline editor flow as screenshot capture.'
}

if ($source -notmatch 'private void TriggerSnip\(\)[\s\S]*StartScreenshotEditorSelection\(false\);') {
    throw 'Screenshot hotkey should start the inline editor without immediate OCR.'
}

if ($source -match 'private void TriggerOcr\(\)[\s\S]*StartSelection\(RecognizeCapturedImage\);') {
    throw 'OCR hotkey should not use the old direct-recognition overlay flow.'
}

if ($source -notmatch 'new SelectionOverlayForm\(bounds, screenshot, SaveBitmap, RecognizeText, settings, recognizeImmediately\)') {
    throw 'Screenshot selection should open the overlay in inline editing mode.'
}

if ($inlineStartMethod -notmatch 'overlay\.Show\(\);') {
    throw 'Screenshot and OCR inline overlay should be shown non-modally after hotkey capture.'
}

if ($inlineStartMethod -match 'overlay\.ShowDialog\(\);') {
    throw 'Screenshot and OCR inline overlay should not block other work with ShowDialog.'
}

if ($source -notmatch 'BeginInlineEditing') {
    throw 'Selection overlay should switch into inline editing after a screenshot region is selected.'
}

if ($source -notmatch 'SwitchToFloatingEditorWindow') {
    throw 'Selected screenshot should switch from the full-screen overlay into a floating editor window.'
}

if ($source -notmatch 'BackColor = Color\.FromArgb\(245, 247, 250\)[\s\S]*TransparencyKey = Color\.Empty') {
    throw 'Floating editor should use an opaque background so toolbar clicks are not passed through.'
}

if ($source -notmatch 'TopMost = true') {
    throw 'Floating editor should stay above the current desktop window while editing.'
}

if ($source -notmatch 'selection\.Height \+ toolbarReserve') {
    throw 'Floating editor should resize to a small window around the captured image and toolbar.'
}

if ($source -notmatch 'UpdateOverlayRegion\(\)[\s\S]*Region = null') {
    throw 'Floating editor should avoid clipping the toolbar out of the clickable window.'
}

if ($source -notmatch 'inlineOcrBox\.BringToFront\(\)') {
    throw 'Inline OCR text box should be brought above the floating editor background.'
}

if ($source -notmatch 'DrawFloatingScreenshotBorder') {
    throw 'Floating screenshot should draw a visible border around the captured image.'
}

if ($source -notmatch 'new Pen\(Color\.Black, 2\)') {
    throw 'Floating screenshot border should include a black outline.'
}

if ($source -notmatch 'ShowEditorToolbars') {
    throw 'Clicking the floating screenshot should show screenshot actions.'
}

if ($source -notmatch 'selectedBounds\.Contains\(e\.Location\)[\s\S]*BeginResizeSelectedImage\(this, e\)') {
    throw 'Parent floating overlay should also handle clicks and edge resizing on the screenshot area.'
}

if ($source -notmatch 'editorCanvas\.MouseDown \+= BeginResizeSelectedImage;') {
    throw 'Floating screenshot should route mouse down through resize handling before moving.'
}

if ($source -notmatch 'CreateEditorToolbar') {
    throw 'Inline screenshot editor should create a floating toolbar.'
}

if ($source -notmatch 'CreateStyleToolbar') {
    throw 'Inline screenshot editor should create a secondary style toolbar.'
}

if ($source -notmatch 'EnsureFloatingWindowCanFitToolbars') {
    throw 'Floating editor should expand wide enough to show the full toolbar.'
}

if ($source -notmatch 'Math\.Max\(8, ClientSize\.Width - toolbarSize\.Width - 8\)') {
    throw 'Floating toolbar placement should not use a negative right boundary.'
}

if ($source -notmatch 'ToolTip') {
    throw 'Inline screenshot editor should create tooltips for toolbar icons.'
}

if ($source -notmatch 'SetToolTip\(button, tip\)') {
    throw 'Toolbar icon buttons should show their function name on hover.'
}

if ($source -notmatch 'toolTip\.Dispose\(\)') {
    throw 'Toolbar tooltip should be disposed with the overlay.'
}

if ($source -notmatch 'AnnotationMode\.Number') {
    throw 'Inline screenshot editor should include numbered callout annotations.'
}

if ($source -notmatch 'AnnotationMode\.Mosaic') {
    throw 'Inline screenshot editor should include mosaic redaction.'
}

if ($source -notmatch 'Clipboard\.SetImage\(\(Bitmap\)editorCanvas\.Image\.Clone\(\)\)') {
    throw 'Inline editor done action should copy the edited screenshot to the clipboard.'
}

if ($source -notmatch 'var copyButton = CreateToolButton') {
    throw 'Inline screenshot editor should include a visible copy button.'
}

if ($source -notmatch 'copyButton\.Click \+= delegate \{ CopyEditedImage\(\); \};') {
    throw 'Inline screenshot copy button should copy without closing the editor.'
}

if ($source -notmatch 'ShowInlineOcrResult\(RecognizeImages\(editorCanvas\.GetImagesForOcr\(selectedOriginalImage\)\)\)') {
    throw 'Inline editor OCR should show recognized text inside the current overlay.'
}

if ($source -notmatch 'if \(recognizeImmediately\)[\s\S]*ShowInlineOcrResult\(RecognizeImages\(editorCanvas\.GetImagesForOcr\(selectedOriginalImage\)\)\)') {
    throw 'OCR hotkey should recognize immediately after the user selects a region.'
}

if ($source -notmatch 'CreateOcrToolbar') {
    throw 'Inline OCR result should replace the screenshot toolbar with OCR result actions.'
}

if ($source -notmatch 'inlineOcrBox') {
    throw 'Inline OCR result should display recognized text in the selected region.'
}

if ($source -notmatch 'InlineOcrTextControl inlineOcrBox') {
    throw 'Inline OCR result should use the custom visible text control in the floating editor.'
}

if ($source -notmatch 'translateToGermanButton') {
    throw 'Inline OCR toolbar should include German translation.'
}

if ($source -notmatch 'TranslateInlineOcrText\("de"') {
    throw 'Inline OCR toolbar should translate to German.'
}

if ($source -notmatch 'WordWrap = true') {
    throw 'Inline OCR result text should wrap automatically based on the current box width.'
}

if ($source -notmatch 'Font = new Font\("Microsoft YaHei UI", 14') {
    throw 'Inline OCR result should use a larger readable font.'
}

if ($source -notmatch 'ForeColor = Color\.Black') {
    throw 'Inline OCR result should render text in black.'
}

if ($source -notmatch 'BackColor = Color\.White') {
    throw 'Inline OCR result should render on a white background.'
}

if ($source -notmatch 'BorderStyle = BorderStyle\.FixedSingle') {
    throw 'Inline OCR result should draw a visible black border.'
}

if ($source -notmatch 'ocrResizeGrip') {
    throw 'Inline OCR result should include a resize grip.'
}

if ($source -notmatch 'BeginResizeInlineOcrBox') {
    throw 'Inline OCR result should start resizing from the resize grip.'
}

if ($source -notmatch 'BeginMoveInlineOcrBox') {
    throw 'Inline OCR result should move when dragging inside the text box.'
}

if ($source -notmatch 'MoveInlineOcrBox') {
    throw 'Inline OCR result should update position while dragging inside the text box.'
}

if ($source -notmatch 'ResizeInlineOcrBox') {
    throw 'Inline OCR result should resize while dragging the resize grip.'
}

if ($source -notmatch 'EnsureFloatingWindowHasOcrWorkspace') {
    throw 'Inline OCR result should reserve enough floating workspace for resizing.'
}

if ($source -notmatch 'GetInlineOcrResizeEdges') {
    throw 'Inline OCR result should support resizing from any edge or corner.'
}

if ($source -notmatch 'BeginMoveSelectedImage') {
    throw 'Selected screenshot should be movable before choosing an annotation tool.'
}

if ($source -notmatch 'MoveSelectedImage') {
    throw 'Dragging the selected screenshot should move the inline editor surface.'
}

if ($source -notmatch 'BeginResizeSelectedImage') {
    throw 'Selected screenshot should start resizing from its edges or corners.'
}

if ($source -notmatch 'ResizeSelectedImage') {
    throw 'Selected screenshot should resize while dragging an edge or corner.'
}

if ($source -notmatch 'GetSelectedImageResizeEdges') {
    throw 'Selected screenshot should detect resize edges and corners.'
}

if ($source -notmatch 'ResizeFloatingEditorWindow') {
    throw 'Floating editor window should resize with the selected screenshot.'
}

if ($source -notmatch 'TranslateInlineOcrText') {
    throw 'Inline OCR toolbar should support translation.'
}

if ($source -notmatch 'RemoveTextFormatting') {
    throw 'Inline OCR toolbar should support remove-format and restore-format actions.'
}

Write-Host 'Inline screenshot editor test passed.'
