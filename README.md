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
- 翻译：OCR 结果可一键译英或译中，重复翻译会使用本地缓存。
- 本地运行：截图和 OCR 都在本机处理，不上传图片。

## 安装说明

### 推荐方式：下载安装包

1. 打开 GitHub Releases。
2. 下载 `ScreenshotHotkeyTool-Portable-YYYYMMDD.zip`。
3. 解压 zip。
4. 双击 `Install.bat`。
5. 安装脚本会复制到当前用户目录、创建桌面快捷方式，并自动启动工具。

安装包已经内置：

- `ScreenshotHotkeyTool.exe`
- Tesseract OCR
- 简体中文/英文 OCR 语言包
- 默认配置

如果 Windows 弹出安全提示，选择“更多信息”后点击“仍要运行”。这是因为 exe 没有代码签名。

### 方式二：只下载 exe

如果你只下载 `ScreenshotHotkeyTool.exe`，截图和标注可以直接用。

但 OCR 需要 Tesseract。推荐用完整安装包，或者自行安装：

```powershell
winget install --id UB-Mannheim.TesseractOCR
```

安装后通常路径是：

```text
C:\Program Files\Tesseract-OCR\tesseract.exe
```

仓库和安装包自带 `tessdata`，包含：

- `chi_sim`：简体中文
- `eng`：英文
- `osd`：方向检测

默认 OCR 语言为 `chi_sim+eng`。完整安装包会自动使用内置 OCR，不需要手动填写路径。

### 配置文件

首次运行会使用默认配置。设置会保存到程序目录下的 `settings.json`。

仓库提供 `settings.example.json` 作为示例，不需要手动复制也能运行。

## 使用说明

### 截图

1. 按 `Ctrl + Shift + R`。
2. 屏幕进入框选状态。
3. 按住鼠标左键拖动，选择截图区域。
4. 松开鼠标后进入截图预览窗口。

预览窗口可用：

- `复制`：复制当前图片到剪贴板。
- `保存`：选择路径保存 PNG。
- `识别文字`：识别整张截图；如果已有框选，则识别所有框选区域。
- `画图`：红色自由画笔。
- `框选`：红色矩形标注，也可作为 OCR 区域。
- `文字`：拖动框选文字位置，松手后输入文字。
- `箭头`：拖动设置箭头方向，松手后写入截图。
- `撤销` / `清空` / `关闭`。

### 直接识别文字

1. 按 `Ctrl + Shift + T`。
2. 拖动框选要识别的文字区域。
3. 松开鼠标。
4. 程序会直接弹出文字识别结果窗口。

结果窗口可用：

- `复制`：复制识别文字。
- `保存`：保存为 `.txt`。
- `去格式`：清理换行和多余空格。
- `复原格式`：恢复识别时的原格式。
- `译英`：把当前文字翻译成英文。
- `译中`：把当前文字翻译成中文。

如果某个翻译接口连接失败，工具会自动尝试备用接口；如果全部不可用，会在结果窗口顶部显示失败原因。

### 截图后识别文字

1. 按 `Ctrl + Shift + R` 截图。
2. 在预览窗口里，如果只想识别局部内容，先点“框选”，画出一个或多个识别区域。
3. 点“识别文字”。
4. 如果没有画框选区域，会识别整张截图。
5. 如果画了多个框选区域，会按画框顺序依次识别并合并结果。

### 添加标注

- 画图：点“画图”，按住鼠标拖动即可画红线。
- 框选：点“框选”，拖动生成红色矩形。
- 文字：点“文字”，拖动框选文字位置，松手后输入文字。
- 箭头：点“箭头”，从起点拖到终点，松手后生成箭头。
- 撤销：撤销上一步标注。
- 清空：恢复到刚截图时的原图。

### 修改快捷键

1. 右键托盘图标。
2. 点击“设置”。
3. 修改截图快捷键或 OCR 快捷键。
4. 点击“保存”。

默认快捷键：

- 截图：`Ctrl + Shift + R`
- OCR：`Ctrl + Shift + T`

如果快捷键被其他软件占用，保存时会提示换一个组合。

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

截图、标注、OCR 识别和保存都在本机完成。

翻译功能需要网络，会把当前 OCR 文本发送到翻译接口；不使用翻译按钮时不会发送。

## 限制

- 当前仅面向 Windows。
- OCR 质量取决于 Tesseract、截图清晰度和语言包。
- 翻译功能依赖网络和翻译接口可用性。
- 标注写入图片后不可单独移动，只能撤销或清空。

## License

MIT
