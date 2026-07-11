$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root 'ScreenshotHotkeyTool.exe'
if (-not (Test-Path -LiteralPath $exe)) {
    throw 'Build ScreenshotHotkeyTool.exe before running this behavior test.'
}

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

$assembly = [System.Reflection.Assembly]::LoadFrom($exe)
$canvasType = $assembly.GetType('ScreenshotHotkeyTool.ImageCanvasControl', $true)
$modeType = $assembly.GetType('ScreenshotHotkeyTool.AnnotationMode', $true)
$bindingFlags = [System.Reflection.BindingFlags]'Instance,NonPublic'
$publicFlags = [System.Reflection.BindingFlags]'Instance,Public'

$bitmap = New-Object System.Drawing.Bitmap 200, 100
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.Clear([System.Drawing.Color]::White)
$graphics.Dispose()

$constructor = $canvasType.GetConstructor($publicFlags, $null, @([System.Drawing.Bitmap]), $null)
if ($null -eq $constructor) {
    throw 'ImageCanvasControl constructor was not found.'
}

$canvas = $constructor.Invoke(@([System.Drawing.Bitmap]$bitmap))
try {
    $canvas.Size = [System.Drawing.Size]::new(200, 100)
    $canvasType.GetProperty('Mode', $publicFlags).SetValue($canvas, [System.Enum]::Parse($modeType, 'Crop'), $null)

    $onMouseDown = $canvasType.GetMethod('OnMouseDown', $bindingFlags)
    $onMouseMove = $canvasType.GetMethod('OnMouseMove', $bindingFlags)
    $onMouseUp = $canvasType.GetMethod('OnMouseUp', $bindingFlags)

    $down = New-Object System.Windows.Forms.MouseEventArgs -ArgumentList ([System.Windows.Forms.MouseButtons]::Left), 1, 20, 10, 0
    $move = New-Object System.Windows.Forms.MouseEventArgs -ArgumentList ([System.Windows.Forms.MouseButtons]::Left), 1, 120, 60, 0
    $up = New-Object System.Windows.Forms.MouseEventArgs -ArgumentList ([System.Windows.Forms.MouseButtons]::Left), 1, 120, 60, 0

    $mouseArgs = [object[]]::new(1)
    $mouseArgs[0] = [System.Windows.Forms.MouseEventArgs]$down
    $onMouseDown.Invoke($canvas, $mouseArgs)
    $mouseArgs[0] = [System.Windows.Forms.MouseEventArgs]$move
    $onMouseMove.Invoke($canvas, $mouseArgs)
    $mouseArgs[0] = [System.Windows.Forms.MouseEventArgs]$up
    $onMouseUp.Invoke($canvas, $mouseArgs)

    $image = $canvasType.GetProperty('Image', $publicFlags).GetValue($canvas, $null)
    if ($image.Width -ne 100 -or $image.Height -ne 50) {
        throw "Crop should replace the image with the selected region. Actual=$($image.Width)x$($image.Height)"
    }

    $undo = $canvasType.GetMethod('Undo', $publicFlags)
    $undo.Invoke($canvas, @())
    $image = $canvasType.GetProperty('Image', $publicFlags).GetValue($canvas, $null)
    if ($image.Width -ne 200 -or $image.Height -ne 100) {
        throw "Undo should restore the image before crop. Actual=$($image.Width)x$($image.Height)"
    }
}
finally {
    if ($canvas -ne $null) {
        $canvas.Dispose()
    }
    $bitmap.Dispose()
}

Write-Host 'Crop feature test passed.'
