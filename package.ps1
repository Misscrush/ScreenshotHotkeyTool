$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$version = Get-Date -Format 'yyyyMMdd'
$dist = Join-Path $root 'dist'
$packageRoot = Join-Path $dist "ScreenshotHotkeyTool-$version"
$zipPath = Join-Path $dist "ScreenshotHotkeyTool-Portable-$version.zip"
$tesseractSource = 'C:\Program Files\Tesseract-OCR'

if (-not (Test-Path -LiteralPath (Join-Path $root 'ScreenshotHotkeyTool.exe'))) {
    throw 'Missing ScreenshotHotkeyTool.exe. Run build.ps1 first.'
}

if (-not (Test-Path -LiteralPath (Join-Path $tesseractSource 'tesseract.exe'))) {
    throw "Missing bundled OCR source: $tesseractSource"
}

Remove-Item -LiteralPath $packageRoot -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $zipPath -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $packageRoot | Out-Null

Copy-Item -LiteralPath (Join-Path $root 'ScreenshotHotkeyTool.exe') -Destination $packageRoot -Force
Copy-Item -LiteralPath (Join-Path $root 'README.md') -Destination $packageRoot -Force
Copy-Item -LiteralPath (Join-Path $root 'CHANGELOG.md') -Destination $packageRoot -Force
Copy-Item -LiteralPath (Join-Path $root 'settings.example.json') -Destination $packageRoot -Force
Copy-Item -LiteralPath (Join-Path $root 'tessdata') -Destination (Join-Path $packageRoot 'tessdata') -Recurse -Force

$settings = '{"modifiers":6,"keyCode":82,"displayText":"Ctrl + Shift + R","saveScreenshot":true,"saveDirectory":"captures","ocrEnabled":true,"ocrModifiers":6,"ocrKeyCode":84,"ocrDisplayText":"Ctrl + Shift + T","ocrLanguage":"chi_sim+eng","ocrEnginePath":"","translationProvider":"Google","baiduAppId":"","baiduSecretKey":""}'
Set-Content -LiteralPath (Join-Path $packageRoot 'settings.json') -Encoding UTF8 -Value $settings

$bundledTesseract = Join-Path $packageRoot 'Tesseract-OCR'
New-Item -ItemType Directory -Path $bundledTesseract | Out-Null
Copy-Item -LiteralPath (Join-Path $tesseractSource 'tesseract.exe') -Destination $bundledTesseract -Force
Get-ChildItem -LiteralPath $tesseractSource -Filter '*.dll' | Copy-Item -Destination $bundledTesseract -Force

$installScript = @'
@echo off
setlocal
set "APPDIR=%LOCALAPPDATA%\ScreenshotHotkeyTool"
if not exist "%APPDIR%" mkdir "%APPDIR%"
xcopy "%~dp0*" "%APPDIR%\" /E /I /Y >nul
powershell -NoProfile -ExecutionPolicy Bypass -Command "$s=(New-Object -COM WScript.Shell).CreateShortcut([Environment]::GetFolderPath('Desktop') + '\ScreenshotHotkeyTool.lnk'); $s.TargetPath=$env:LOCALAPPDATA + '\ScreenshotHotkeyTool\ScreenshotHotkeyTool.exe'; $s.WorkingDirectory=$env:LOCALAPPDATA + '\ScreenshotHotkeyTool'; $s.Save()"
start "" "%APPDIR%\ScreenshotHotkeyTool.exe"
echo.
echo Installed and started ScreenshotHotkeyTool.
echo Desktop shortcut created.
pause
'@
Set-Content -LiteralPath (Join-Path $packageRoot 'Install.bat') -Encoding ASCII -Value $installScript

$runScript = @'
@echo off
start "" "%~dp0ScreenshotHotkeyTool.exe"
'@
Set-Content -LiteralPath (Join-Path $packageRoot 'Run.bat') -Encoding ASCII -Value $runScript

Compress-Archive -LiteralPath $packageRoot -DestinationPath $zipPath -Force
Write-Host "Built package: $zipPath"
