# ScreenshotHotkeyTool

一个轻量的 Windows 托盘截图工具：快捷键框选截图、预览标注、复制/保存，并支持离线 OCR 文字识别。

## 特点

- 托盘常驻：关闭主窗口不会打断快捷键。
- 区域截图：默认 `Ctrl + Shift + R` 框选屏幕区域。
- 独立 OCR：默认 `Ctrl + Shift + T` 框选区域并直接识别文字。
- 截图预览：复制、保存、识别文字、画图、框选、文字、箭头、撤销、清空。
- 多区域 OCR：预览里可画多个框选，点“识别文字”后依次识别全部区域。
- 格式处理：默认尽量保留换行和空格；可一键“去格式”，再点可“复原格式”。
- 中文优化：自动去掉中文字符之间被 OCR 误加的空格。
- 本地运行：截图和 OCR 都在本机处理，不上传图片。

## 安装

1. 下载仓库里的 `ScreenshotHotkeyTool.exe`。
2. 双击运行，图标会出现在 Windows 右下角托盘。
3. 右键托盘图标可打开截图、OCR、设置或退出。

OCR 需要安装 Tesseract OCR。推荐安装：

```powershell
winget install --id UB-Mannheim.TesseractOCR
```

仓库自带 `tessdata`，包含 `chi_sim`、`eng`、`osd`，默认 OCR 语言为 `chi_sim+eng`。

## 使用

### 截图

按 `Ctrl + Shift + R`，拖动框选区域，松手后进入预览窗口。

预览窗口可用：

- `复制`：复制当前图片到剪贴板。
- `保存`：选择路径保存 PNG。
- `识别文字`：识别整张截图；如果已有框选，则识别所有框选区域。
- `画图`：红色自由画笔。
- `框选`：红色矩形标注，也可作为 OCR 区域。
- `文字`：拖动框选文字位置，松手后输入文字。
- `箭头`：拖动设置箭头方向，松手后写入截图。
- `撤销` / `清空` / `关闭`。

### OCR

按 `Ctrl + Shift + T`，拖动框选区域，松手后直接弹出文字识别结果。

结果窗口可用：

- `复制`：复制识别文字。
- `保存`：保存为 `.txt`。
- `去格式`：清理换行和多余空格。
- `复原格式`：恢复识别时的原格式。

## 设置

右键托盘图标，点击“设置”：

- 修改截图快捷键。
- 修改 OCR 快捷键。
- 开启/关闭 OCR 快捷键。
- 设置 OCR 语言。
- 指定 `tesseract.exe` 路径。
- 修改截图默认保存目录。

配置保存在程序目录下的 `settings.json`。仓库提供 `settings.example.json` 作为示例。

## 构建

需要 Windows 和 .NET Framework C# 编译器。

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

输出文件：

```text
ScreenshotHotkeyTool.exe
```

## 测试

```powershell
powershell -ExecutionPolicy Bypass -File .\tests\OcrFeatureTests.ps1
powershell -ExecutionPolicy Bypass -File .\tests\OcrFormattingTests.ps1
powershell -ExecutionPolicy Bypass -File .\tests\OcrRectangleSelectionTests.ps1
powershell -ExecutionPolicy Bypass -File .\tests\TextAnnotationAndMultiOcrTests.ps1
powershell -ExecutionPolicy Bypass -File .\tests\ArrowAnnotationTests.ps1
```

## 隐私

工具不包含云端上传逻辑。截图、标注、OCR 识别和保存都在本机完成。

## 限制

- 当前仅面向 Windows。
- OCR 质量取决于 Tesseract、截图清晰度和语言包。
- 标注写入图片后不可单独移动，只能撤销或清空。

## License

MIT
