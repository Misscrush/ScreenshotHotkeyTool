$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$source = Get-Content -LiteralPath (Join-Path $root 'src\ScreenshotHotkeyTool.cs') -Raw -Encoding UTF8

if ($source -notmatch 'RegisterFallbackHook') {
    throw 'Hotkey registration should fall back to a keyboard hook when RegisterHotKey fails.'
}

if ($source -notmatch 'WH_KEYBOARD_LL') {
    throw 'Fallback hotkey handling should use a low-level keyboard hook.'
}

if ($source -notmatch 'SetWindowsHookEx\(WH_KEYBOARD_LL') {
    throw 'Fallback hook should be installed with SetWindowsHookEx.'
}

if ($source -notmatch 'PostMessage\(Handle, WM_FALLBACK_HOTKEY') {
    throw 'Fallback hook should marshal hotkey handling back to the app window.'
}

if ($source -notmatch 'return new IntPtr\(1\)') {
    throw 'Fallback hook should consume the conflicting shortcut so other apps do not handle it first.'
}

if ($source -notmatch 'UnregisterFallbackHook\(\)') {
    throw 'Fallback hook should be unregistered when hotkey settings change or the app exits.'
}

$oldConflictText = "已被占用，请"
if ($source.Contains($oldConflictText)) {
    throw "The app should not force the user to change a shortcut that is already registered by another app."
}

Write-Host 'Hotkey fallback test passed.'
