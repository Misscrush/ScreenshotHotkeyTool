$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$source = Get-Content -LiteralPath (Join-Path $root 'src\ScreenshotHotkeyTool.cs') -Raw -Encoding UTF8

$scrollingLabel = ([char]0x6EDA).ToString() + ([char]0x52A8).ToString() + ([char]0x622A).ToString() + ([char]0x5C4F).ToString()

if ($source -notmatch [Regex]::Escape($scrollingLabel)) {
    throw 'Tray menu should include a scrolling screenshot entry.'
}

if ($source -notmatch 'StartFullScreenScrollingCapture') {
    throw 'Tray app should start scrolling capture directly from the current screen.'
}

if ($source -notmatch 'GetFullScreenScrollingRegion') {
    throw 'Scrolling capture should use a full-screen region instead of requiring user selection.'
}

if ($source -notmatch 'Screen\.FromPoint\(Cursor\.Position\)\.WorkingArea') {
    throw 'Full-screen scrolling capture should use the screen working area so the Windows taskbar is not stitched into the long image.'
}

if ($source -match 'Screen\.FromPoint\(Cursor\.Position\)\.Bounds') {
    throw 'Full-screen scrolling capture should not use raw screen bounds because that includes the Windows taskbar.'
}

if ($source -match 'SelectionOverlayForm\(bounds, screenshot, \(Action<Rectangle>\)CaptureScrollingRegion\)') {
    throw 'Scrolling capture should not open a selection overlay.'
}

if ($source -notmatch 'uiInvoker\.BeginInvoke') {
    throw 'Scrolling capture should marshal preview creation back to the WinForms UI thread through a control.'
}

if ($source -match 'uiContext\.Post') {
    throw 'Scrolling capture should not rely on a manually-created SynchronizationContext for preview creation.'
}

if ($source -notmatch 'scrolling-capture\.log') {
    throw 'Scrolling capture should write a small diagnostic log for failed user-side repros.'
}

if ($source -notmatch '--test-scroll-capture') {
    throw 'Scrolling capture should have a hidden end-to-end test mode that saves the actual stitched image.'
}

if ($source -notmatch 'ScrollingCaptureRunner') {
    throw 'Scrolling capture should be isolated in ScrollingCaptureRunner.'
}

if ($source -notmatch 'SendInput' -or $source -notmatch 'MOUSEEVENTF_WHEEL') {
    throw 'Scrolling capture should send mouse wheel input through SendInput between captures.'
}

if ($source -notmatch 'SetForegroundWindow') {
    throw 'Scrolling capture should activate the window under the selected region before scrolling.'
}

if ($source -notmatch 'ExpandScrollingRegionToWindowBottom') {
    throw 'Scrolling capture should expand the selected region down to the current window bottom.'
}

if ($source -notmatch 'GetWindowRect' -or $source -notmatch 'GetAncestor' -or $source -notmatch 'GA_ROOT') {
    throw 'Scrolling capture should use the selected point window bounds to avoid stopping at the selection bottom.'
}

if ($source -notmatch 'expanded scrolling region') {
    throw 'Scrolling capture should log the expanded capture rectangle for diagnosis.'
}

if ($source -notmatch 'captures\.Count == 1') {
    throw 'Scrolling capture should fail clearly instead of previewing a single unscrolled frame.'
}

if ($source -notmatch 'ImagesAreNearlySame') {
    throw 'Scrolling capture should stop when scrolling no longer changes the image.'
}

if ($source -notmatch 'RetryScrollRegion') {
    throw 'Scrolling capture should retry with stronger scrolling before deciding the page has reached the bottom.'
}

if ($source -notmatch 'changedSamples') {
    throw 'Scrolling capture should not stop early just because fixed panels and blank areas dominate the average image difference.'
}

if ($source -notmatch 'FindBestVerticalOverlap') {
    throw 'Scrolling capture should find the best vertical overlap between adjacent frames.'
}

if ($source -notmatch 'FindBestVerticalOverlap\(captures\[i - 1\], captures\[i\], scrollingContentBounds\)') {
    throw 'Scrolling capture should match frame overlap inside the scrolling content area, not the whole screenshot.'
}

if ($source -notmatch 'MinimumAppendedFrameHeight') {
    throw 'Scrolling capture should keep a minimum appended height so the final partial scroll is not lost.'
}

if ($source -notmatch 'MinimumContinuedScrollAppendHeight') {
    throw 'Scrolling capture should keep a larger minimum appended height for middle frames so content is not collapsed into the first page.'
}

if ($source -match 'Math\.Min\(height / 3, MinimumContinuedScrollAppendHeight\)') {
    throw 'Scrolling capture should not cap middle-frame appended height at a small constant.'
}

if ($source -notmatch 'LastStitchSummary') {
    throw 'Scrolling capture should log stitch part heights for real end-to-end diagnosis.'
}

if ($source -notmatch 'i < captures\.Count - 1') {
    throw 'Scrolling capture should distinguish middle frames from the final partial frame.'
}

if ($source -notmatch 'StitchFrame') {
    throw 'Scrolling capture should stitch each frame using the detected overlap, not a fixed crop.'
}

if ($source -notmatch 'DetectScrollingContentBounds') {
    throw 'Scrolling capture should detect the horizontally scrolling content area when fixed sidebars are present.'
}

if ($source -match 'CropCapturesToScrollingContent') {
    throw 'Scrolling capture should not crop fixed sidebars out of the whole long screenshot.'
}

if ($source -notmatch 'PreserveFixedAreasInFirstFrame') {
    throw 'Scrolling capture should preserve fixed sidebars in the first full frame.'
}

if ($source -notmatch 'DrawMovingContentOnlyForAppendedFrames') {
    throw 'Scrolling capture should append only the moving content area after the first frame.'
}

if ($source -notmatch 'ColumnMotionScore') {
    throw 'Scrolling capture should compare adjacent frames by column to find fixed and moving areas.'
}

if ($source -notmatch 'SeamTrimPixels') {
    throw 'Scrolling capture should trim a few pixels at stitch seams to avoid visible horizontal lines.'
}

if ($source -notmatch 'PreserveFixedAreasInFirstFrame' -or $source -notmatch 'DrawMovingContentOnlyForAppendedFrames') {
    throw 'Scrolling capture should draw stitched parts without scaled DrawImage interpolation.'
}

if ($source -notmatch 'BlendStitchSeam') {
    throw 'Scrolling capture should blend stitched boundaries to hide residual horizontal seam lines.'
}

if ($source -match 'var overlap = Math\.Min\(140') {
    throw 'Scrolling capture should not use a fixed overlap crop for stitching.'
}

if ($source -match 'height \* 0\.85') {
    throw 'Scrolling capture should support very high overlap on the final partial scroll.'
}

if ($source -match 'height \* 0\.52') {
    throw 'Scrolling capture should not bias stitching toward one fixed scroll distance.'
}

if ($source -notmatch 'GetAsyncKeyState') {
    throw 'Scrolling capture should stop when the user clicks a mouse button during capture.'
}

if ($source -notmatch 'WaitForMouseButtonsReleased') {
    throw 'Scrolling capture should wait for the selection mouse button to be released before listening for cancel clicks.'
}

if ($source -notmatch 'Stitch\(captures, scrollingContentBounds\)') {
    throw 'Scrolling capture should stitch multiple captures into one long image.'
}

if ($source -notmatch 'fitWidthScrollPreview') {
    throw 'Scrolling capture preview should use a width-fit scrollable preview mode.'
}

if ($source -notmatch 'AutoScroll = true') {
    throw 'Long scrolling screenshot preview should expose scrollbars.'
}

if ($source -notmatch 'PreviewZoom') {
    throw 'Scrolling screenshot preview should keep an internal zoom factor.'
}

if ($source -notmatch 'HandleScrollPreviewMouseWheel') {
    throw 'Scrolling screenshot preview should handle Ctrl+mouse wheel zoom as a default interaction.'
}

if ($source -notmatch 'ModifierKeys & Keys\.Control') {
    throw 'Scrolling screenshot preview should use Ctrl+mouse wheel for zoom while preserving normal wheel scrolling.'
}

if ($source -notmatch 'ApplyScrollPreviewZoom') {
    throw 'Scrolling screenshot preview should resize the image canvas when zoom changes.'
}

if ($source -notmatch 'bounds\.Width <= 0 \|\| bounds\.Height <= 0') {
    throw 'Image canvas should skip drawing empty bounds to avoid GDI+ parameter errors.'
}

if ($source -notmatch 'previewDisposing') {
    throw 'Long screenshot preview should guard layout during close/dispose.'
}

if ($source -notmatch 'scrollPanel\.Resize -= HandleScrollPreviewResize') {
    throw 'Long screenshot preview should detach resize handlers before disposing child controls.'
}

if ($source -notmatch 'canvas\.ImageIsUsable') {
    throw 'Long screenshot preview layout should not read dimensions from a disposed image.'
}

Write-Host 'Scrolling capture feature test passed.'
