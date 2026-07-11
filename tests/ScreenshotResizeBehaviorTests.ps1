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
    $beginResize = $formType.GetMethod('BeginResizeSelectedImage', $bindingFlags)
    $resize = $formType.GetMethod('ResizeSelectedImage', $bindingFlags)
    $endResize = $formType.GetMethod('EndResizeSelectedImage', $bindingFlags)
    $showInlineOcr = $formType.GetMethod('ShowInlineOcrResult', $bindingFlags)
    $beginOcrBoxDrag = $formType.GetMethod('BeginResizeInlineOcrBox', $bindingFlags)
    $moveOcrBox = $formType.GetMethod('ResizeInlineOcrBox', $bindingFlags)
    $endOcrBoxDrag = $formType.GetMethod('EndResizeInlineOcrBox', $bindingFlags)
    $selectedField = $formType.GetField('selectedBounds', $bindingFlags)
    $canvasField = $formType.GetField('editorCanvas', $bindingFlags)
    $ocrBoxField = $formType.GetField('inlineOcrBox', $bindingFlags)
    $movingField = $formType.GetField('movingSelectedImage', $bindingFlags)
    $movingOcrField = $formType.GetField('movingInlineOcrBox', $bindingFlags)
    $toolbarField = $formType.GetField('editorToolbar', $bindingFlags)

    $beginInlineArgs = [object[]]::new(1)
    $beginInlineArgs[0] = [System.Drawing.Rectangle]::new(20, 20, 220, 120)
    $beginInlineEditing.Invoke($form, $beginInlineArgs)
    [System.Windows.Forms.Application]::DoEvents()

    $canvas = $canvasField.GetValue($form)
    $toolbar = $toolbarField.GetValue($form)
    if ($toolbar.Right -gt $form.ClientSize.Width) {
        throw "Editor toolbar is clipped. Toolbar=$($toolbar.Bounds) ClientWidth=$($form.ClientSize.Width)"
    }
    $before = [System.Drawing.Rectangle]$selectedField.GetValue($form)

    $down = New-Object System.Windows.Forms.MouseEventArgs ([System.Windows.Forms.MouseButtons]::Left), 1, ($before.Width - 1), ($before.Height - 1), 0
    $move = New-Object System.Windows.Forms.MouseEventArgs ([System.Windows.Forms.MouseButtons]::Left), 1, ($before.Width + 80), ($before.Height + 60), 0
    $up = New-Object System.Windows.Forms.MouseEventArgs ([System.Windows.Forms.MouseButtons]::Left), 1, ($before.Width + 80), ($before.Height + 60), 0

    $resizeArgs = [object[]]::new(2)
    $resizeArgs[0] = [System.Windows.Forms.Control]$canvas
    $resizeArgs[1] = [System.Windows.Forms.MouseEventArgs]$down
    $beginResize.Invoke($form, $resizeArgs)
    $resizeArgs[1] = [System.Windows.Forms.MouseEventArgs]$move
    $resize.Invoke($form, $resizeArgs)
    $resizeArgs[1] = [System.Windows.Forms.MouseEventArgs]$up
    $endResize.Invoke($form, $resizeArgs)
    [System.Windows.Forms.Application]::DoEvents()

    $after = [System.Drawing.Rectangle]$selectedField.GetValue($form)
    if ($after.Width -le $before.Width -or $after.Height -le $before.Height) {
        throw "Screenshot resize did not grow the selected image. Before=$before After=$after"
    }

    if ($canvas.Bounds.Width -ne $after.Width -or $canvas.Bounds.Height -ne $after.Height) {
        throw "Editor canvas bounds did not follow the selected image. Canvas=$($canvas.Bounds) Selected=$after"
    }

    $windowBeforeMove = [System.Drawing.Rectangle]$form.Bounds
    $moveX = [Math]::Max(20, [int]($after.Width / 2))
    $moveY = [Math]::Max(20, [int]($after.Height / 2))
    $moveDown = New-Object System.Windows.Forms.MouseEventArgs -ArgumentList ([System.Windows.Forms.MouseButtons]::Left), 1, $moveX, $moveY, 0
    $moveDrag = New-Object System.Windows.Forms.MouseEventArgs -ArgumentList ([System.Windows.Forms.MouseButtons]::Left), 1, ($moveX + 30), ($moveY + 20), 0
    $moveUp = New-Object System.Windows.Forms.MouseEventArgs -ArgumentList ([System.Windows.Forms.MouseButtons]::Left), 1, ($moveX + 30), ($moveY + 20), 0
    $resizeArgs[1] = [System.Windows.Forms.MouseEventArgs]$moveDown
    $beginResize.Invoke($form, $resizeArgs)
    if (-not [bool]$movingField.GetValue($form)) {
        $currentSelected = [System.Drawing.Rectangle]$selectedField.GetValue($form)
        throw "Dragging inside the screenshot did not enter move mode. Selected=$currentSelected Point=($moveX,$moveY)"
    }
    $resizeArgs[1] = [System.Windows.Forms.MouseEventArgs]$moveDrag
    $resize.Invoke($form, $resizeArgs)
    $resizeArgs[1] = [System.Windows.Forms.MouseEventArgs]$moveUp
    $endResize.Invoke($form, $resizeArgs)
    [System.Windows.Forms.Application]::DoEvents()

    $windowAfterMove = [System.Drawing.Rectangle]$form.Bounds
    if ($windowAfterMove.Left -eq $windowBeforeMove.Left -and $windowAfterMove.Top -eq $windowBeforeMove.Top) {
        throw "Dragging inside the screenshot did not move the floating editor. Before=$windowBeforeMove After=$windowAfterMove"
    }

    $selectedBeforeOcr = [System.Drawing.Rectangle]$selectedField.GetValue($form)
    $showOcrArgs = [object[]]::new(1)
    $showOcrArgs[0] = "Line one`r`nLine two"
    $showInlineOcr.Invoke($form, $showOcrArgs)
    [System.Windows.Forms.Application]::DoEvents()

    $ocrBox = $ocrBoxField.GetValue($form)
    if ($ocrBox.Width -ne $selectedBeforeOcr.Width -or $ocrBox.Height -ne $selectedBeforeOcr.Height) {
        throw "Inline OCR text box should start at the selected region size. Selected=$selectedBeforeOcr OcrBox=$($ocrBox.Bounds)"
    }

    $ocrBitmap = New-Object System.Drawing.Bitmap $ocrBox.Width, $ocrBox.Height
    try {
        $ocrBox.DrawToBitmap($ocrBitmap, [System.Drawing.Rectangle]::new(0, 0, $ocrBitmap.Width, $ocrBitmap.Height))
        $darkPixels = 0
        for ($x = 0; $x -lt [Math]::Min($ocrBitmap.Width, 260); $x += 4) {
            for ($y = 0; $y -lt [Math]::Min($ocrBitmap.Height, 120); $y += 4) {
                $pixel = $ocrBitmap.GetPixel($x, $y)
                if ($pixel.R -lt 80 -and $pixel.G -lt 80 -and $pixel.B -lt 80) {
                    $darkPixels++
                }
            }
        }
        if ($darkPixels -lt 8) {
            throw "Inline OCR text box did not render visible dark text or border. DarkPixels=$darkPixels"
        }
    }
    finally {
        $ocrBitmap.Dispose()
    }

    $ocrBeforeRightResize = [System.Drawing.Rectangle]$selectedField.GetValue($form)
    $rightResizeDown = New-Object System.Windows.Forms.MouseEventArgs -ArgumentList ([System.Windows.Forms.MouseButtons]::Left), 1, ($ocrBox.Width - 1), ([Math]::Max(10, [int]($ocrBox.Height / 2))), 0
    $rightResizeDrag = New-Object System.Windows.Forms.MouseEventArgs -ArgumentList ([System.Windows.Forms.MouseButtons]::Left), 1, ($ocrBox.Width + 110), ([Math]::Max(10, [int]($ocrBox.Height / 2))), 0
    $rightResizeUp = New-Object System.Windows.Forms.MouseEventArgs -ArgumentList ([System.Windows.Forms.MouseButtons]::Left), 1, ($ocrBox.Width + 110), ([Math]::Max(10, [int]($ocrBox.Height / 2))), 0
    $ocrResizeArgs = [object[]]::new(2)
    $ocrResizeArgs[0] = [System.Windows.Forms.Control]$ocrBox
    $ocrResizeArgs[1] = [System.Windows.Forms.MouseEventArgs]$rightResizeDown
    $beginOcrBoxDrag.Invoke($form, $ocrResizeArgs)
    $ocrResizeArgs[1] = [System.Windows.Forms.MouseEventArgs]$rightResizeDrag
    $moveOcrBox.Invoke($form, $ocrResizeArgs)
    $ocrResizeArgs[1] = [System.Windows.Forms.MouseEventArgs]$rightResizeUp
    $endOcrBoxDrag.Invoke($form, $ocrResizeArgs)
    [System.Windows.Forms.Application]::DoEvents()

    $ocrAfterRightResize = [System.Drawing.Rectangle]$selectedField.GetValue($form)
    if ($ocrAfterRightResize.Width -le $ocrBeforeRightResize.Width) {
        throw "Dragging the OCR text box right edge did not expand it. Before=$ocrBeforeRightResize After=$ocrAfterRightResize"
    }

    $ocrBeforeLeftResize = [System.Drawing.Rectangle]$selectedField.GetValue($form)
    $leftResizeDown = New-Object System.Windows.Forms.MouseEventArgs -ArgumentList ([System.Windows.Forms.MouseButtons]::Left), 1, 1, ([Math]::Max(10, [int]($ocrBox.Height / 2))), 0
    $leftResizeDrag = New-Object System.Windows.Forms.MouseEventArgs -ArgumentList ([System.Windows.Forms.MouseButtons]::Left), 1, -80, ([Math]::Max(10, [int]($ocrBox.Height / 2))), 0
    $leftResizeUp = New-Object System.Windows.Forms.MouseEventArgs -ArgumentList ([System.Windows.Forms.MouseButtons]::Left), 1, -80, ([Math]::Max(10, [int]($ocrBox.Height / 2))), 0
    $ocrResizeArgs[1] = [System.Windows.Forms.MouseEventArgs]$leftResizeDown
    $beginOcrBoxDrag.Invoke($form, $ocrResizeArgs)
    $ocrResizeArgs[1] = [System.Windows.Forms.MouseEventArgs]$leftResizeDrag
    $moveOcrBox.Invoke($form, $ocrResizeArgs)
    $ocrResizeArgs[1] = [System.Windows.Forms.MouseEventArgs]$leftResizeUp
    $endOcrBoxDrag.Invoke($form, $ocrResizeArgs)
    [System.Windows.Forms.Application]::DoEvents()

    $ocrAfterLeftResize = [System.Drawing.Rectangle]$selectedField.GetValue($form)
    if ($ocrAfterLeftResize.Width -le $ocrBeforeLeftResize.Width) {
        throw "Dragging the OCR text box left edge did not expand it. Before=$ocrBeforeLeftResize After=$ocrAfterLeftResize"
    }

    $ocrBeforeMove = [System.Drawing.Rectangle]$selectedField.GetValue($form)
    $ocrWindowBeforeMove = [System.Drawing.Rectangle]$form.Bounds
    $ocrMoveX = [Math]::Max(20, [int]($ocrBeforeMove.Width / 2))
    $ocrMoveY = [Math]::Max(20, [int]($ocrBeforeMove.Height / 2))
    $ocrMoveDown = New-Object System.Windows.Forms.MouseEventArgs -ArgumentList ([System.Windows.Forms.MouseButtons]::Left), 1, $ocrMoveX, $ocrMoveY, 0
    $ocrMoveDrag = New-Object System.Windows.Forms.MouseEventArgs -ArgumentList ([System.Windows.Forms.MouseButtons]::Left), 1, ($ocrMoveX + 35), ($ocrMoveY + 25), 0
    $ocrMoveUp = New-Object System.Windows.Forms.MouseEventArgs -ArgumentList ([System.Windows.Forms.MouseButtons]::Left), 1, ($ocrMoveX + 35), ($ocrMoveY + 25), 0
    $ocrDragArgs = [object[]]::new(2)
    $ocrDragArgs[0] = [System.Windows.Forms.Control]$ocrBox
    $ocrDragArgs[1] = [System.Windows.Forms.MouseEventArgs]$ocrMoveDown
    $beginOcrBoxDrag.Invoke($form, $ocrDragArgs)
    if (-not [bool]$movingOcrField.GetValue($form)) {
        throw "Dragging inside the OCR text box did not enter move mode. Selected=$ocrBeforeMove Point=($ocrMoveX,$ocrMoveY)"
    }
    $ocrDragArgs[1] = [System.Windows.Forms.MouseEventArgs]$ocrMoveDrag
    $moveOcrBox.Invoke($form, $ocrDragArgs)
    $ocrDragArgs[1] = [System.Windows.Forms.MouseEventArgs]$ocrMoveUp
    $endOcrBoxDrag.Invoke($form, $ocrDragArgs)
    [System.Windows.Forms.Application]::DoEvents()

    $ocrWindowAfterMove = [System.Drawing.Rectangle]$form.Bounds
    if ($ocrWindowAfterMove.Left -eq $ocrWindowBeforeMove.Left -and $ocrWindowAfterMove.Top -eq $ocrWindowBeforeMove.Top) {
        throw "Dragging inside the OCR text box did not move the floating OCR window. Before=$ocrWindowBeforeMove After=$ocrWindowAfterMove"
    }
}
finally {
    if ($form -ne $null) {
        $form.Close()
        $form.Dispose()
    }
    $screenshot.Dispose()
}

Write-Host 'Screenshot resize behavior test passed.'
