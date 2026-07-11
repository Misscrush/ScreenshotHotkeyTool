$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root 'ScreenshotHotkeyTool.exe'
if (-not (Test-Path -LiteralPath $exe)) {
    throw 'Build ScreenshotHotkeyTool.exe before running this behavior test.'
}

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

$assembly = [System.Reflection.Assembly]::LoadFrom($exe)
$formType = $assembly.GetType('ScreenshotHotkeyTool.SelectionOverlayForm', $true)
$settingsType = $assembly.GetType('ScreenshotHotkeyTool.HotkeySettings', $true)
$bindingFlags = [System.Reflection.BindingFlags]'Instance,NonPublic'
$publicFlags = [System.Reflection.BindingFlags]'Instance,Public'

$constructor = $formType.GetConstructor(
    $publicFlags,
    $null,
    @(
        [System.Drawing.Rectangle],
        [System.Drawing.Bitmap],
        [System.Func[System.Drawing.Bitmap,string]],
        [System.Func[System.Drawing.Bitmap,string]],
        $settingsType,
        [bool]
    ),
    $null)

if ($null -eq $constructor) {
    throw 'SelectionOverlayForm inline constructor was not found.'
}

$screenshot = New-Object System.Drawing.Bitmap 900, 520
$graphics = [System.Drawing.Graphics]::FromImage($screenshot)
$graphics.Clear([System.Drawing.Color]::White)
$graphics.Dispose()

$saveImage = [System.Func[System.Drawing.Bitmap,string]] { param($bitmap) return '' }
$recognizeText = [System.Func[System.Drawing.Bitmap,string]] { param($bitmap) return '' }
$constructorArgs = [object[]]::new(6)
$constructorArgs[0] = [System.Drawing.Rectangle]::new(0, 0, 900, 520)
$constructorArgs[1] = [System.Drawing.Bitmap]$screenshot
$constructorArgs[2] = $saveImage
$constructorArgs[3] = $recognizeText
$constructorArgs[4] = $null
$constructorArgs[5] = $false
$form = $constructor.Invoke($constructorArgs)

try {
    $form.Show()
    [System.Windows.Forms.Application]::DoEvents()

    $beginInlineEditing = $formType.GetMethod('BeginInlineEditing', $bindingFlags)
    $showInlineOcr = $formType.GetMethod('ShowInlineOcrResult', $bindingFlags)
    $selectedField = $formType.GetField('selectedBounds', $bindingFlags)
    $ocrBoxField = $formType.GetField('inlineOcrBox', $bindingFlags)

    $beginInlineArgs = [object[]]::new(1)
    $beginInlineArgs[0] = [System.Drawing.Rectangle]::new(20, 20, 80, 30)
    $beginInlineEditing.Invoke($form, $beginInlineArgs)
    [System.Windows.Forms.Application]::DoEvents()

    $before = [System.Drawing.Rectangle]$selectedField.GetValue($form)
    $ocrText = "Amazon product title first line`r`nSecond line with more product information`r`nThird line should be visible immediately"
    $showOcrArgs = [object[]]::new(1)
    $showOcrArgs[0] = $ocrText
    $showInlineOcr.Invoke($form, $showOcrArgs)
    [System.Windows.Forms.Application]::DoEvents()

    $ocrBox = $ocrBoxField.GetValue($form)
    if ($ocrBox.Width -le $before.Width -or $ocrBox.Height -le $before.Height) {
        throw "OCR result box should auto-expand for readable text. Before=$before OcrBox=$($ocrBox.Bounds)"
    }

    if ($ocrBox.Width -lt 260 -or $ocrBox.Height -lt 120) {
        throw "OCR result box should have a readable minimum size. OcrBox=$($ocrBox.Bounds)"
    }
}
finally {
    if ($form -ne $null) {
        $form.Close()
        $form.Dispose()
    }
    $screenshot.Dispose()
}

Write-Host 'OCR auto-fit test passed.'
