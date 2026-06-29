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
    $selectedField = $formType.GetField('selectedBounds', $bindingFlags)
    $canvasField = $formType.GetField('editorCanvas', $bindingFlags)
    $movingField = $formType.GetField('movingSelectedImage', $bindingFlags)
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
}
finally {
    if ($form -ne $null) {
        $form.Close()
        $form.Dispose()
    }
    $screenshot.Dispose()
}

Write-Host 'Screenshot resize behavior test passed.'
