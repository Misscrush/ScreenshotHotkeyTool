using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace ScreenshotHotkeyTool
{
    internal static class Program
    {
        private static Mutex singleInstanceMutex;

        [STAThread]
        private static void Main(string[] args)
        {
            DpiAwareness.Enable();

            if (args != null && args.Length >= 2 && string.Equals(args[0], "--test-scroll-capture", StringComparison.OrdinalIgnoreCase))
            {
                RunScrollCaptureTest(args[1]);
                return;
            }

            bool createdNew;
            singleInstanceMutex = new Mutex(true, "ScreenshotHotkeyTool.SingleInstance", out createdNew);
            if (!createdNew)
                return;

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new TrayAppContext());
            singleInstanceMutex.ReleaseMutex();
            singleInstanceMutex.Dispose();
        }

        private static void RunScrollCaptureTest(string outputPath)
        {
            var region = Screen.FromPoint(Cursor.Position).WorkingArea;
            ScrollingCaptureRunner.DebugFrameDirectory = outputPath + ".frames";
            using (var bitmap = ScrollingCaptureRunner.Capture(region))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
                bitmap.Save(outputPath, ImageFormat.Png);
                var report = "Saved=" + outputPath + Environment.NewLine
                    + "Frames=" + ScrollingCaptureRunner.LastCaptureCount + Environment.NewLine
                    + "Size=" + bitmap.Width + "x" + bitmap.Height + Environment.NewLine
                    + "Stitch=" + ScrollingCaptureRunner.LastStitchSummary + Environment.NewLine;
                File.WriteAllText(outputPath + ".txt", report, Encoding.UTF8);
                Console.Write(report);
            }
        }
    }

    internal static class DpiAwareness
    {
        public static void Enable()
        {
            try
            {
                if (SetProcessDpiAwareness(2) == 0)
                    return;
            }
            catch
            {
            }

            try
            {
                SetProcessDPIAware();
            }
            catch
            {
            }
        }

        [DllImport("shcore.dll")]
        private static extern int SetProcessDpiAwareness(int value);

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();
    }

    internal sealed class TrayAppContext : ApplicationContext
    {
        private readonly HotkeyWindow screenshotHotkeyWindow;
        private readonly HotkeyWindow ocrHotkeyWindow;
        private readonly NotifyIcon trayIcon;
        private readonly Icon trayAppIcon;
        private readonly Control uiInvoker;
        private HotkeySettings settings;
        private bool isCapturing;

        public TrayAppContext()
        {
            settings = HotkeySettings.Load();
            screenshotHotkeyWindow = new HotkeyWindow(7301, TriggerSnip);
            ocrHotkeyWindow = new HotkeyWindow(7302, TriggerOcr);
            uiInvoker = new Control();
            uiInvoker.CreateControl();

            if (!screenshotHotkeyWindow.Register(settings.Modifiers, settings.KeyCode))
            {
                MessageBox.Show(settings.DisplayText + " 快捷键监听失败，请重启工具或尝试以管理员身份运行。", "截图快捷键", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            if (settings.OcrEnabled && !ocrHotkeyWindow.Register(settings.OcrModifiers, settings.OcrKeyCode))
            {
                MessageBox.Show(settings.OcrDisplayText + " OCR 快捷键监听失败，请重启工具或尝试以管理员身份运行。", "截图快捷键", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            var menu = new ContextMenuStrip();
            menu.Items.Add("立即截图", null, delegate { TriggerSnip(); });
            menu.Items.Add("滚动截屏", null, delegate { TriggerScrollingSnip(); });
            menu.Items.Add("识别文字", null, delegate { TriggerOcr(); });
            menu.Items.Add("设置", null, delegate { OpenSettings(); });
            menu.Items.Add("退出", null, delegate { ExitThread(); });

            trayAppIcon = TrayIconFactory.Create();
            trayIcon = new NotifyIcon
            {
                Icon = trayAppIcon,
                ContextMenuStrip = menu,
                Visible = true
            };
            trayIcon.DoubleClick += delegate { TriggerSnip(); };
            UpdateTrayText();
        }

        private void OpenSettings()
        {
            using (var form = new SettingsForm(settings))
            {
                if (form.ShowDialog() != DialogResult.OK)
                    return;

                var oldSettings = settings;
                var newSettings = form.SelectedSettings;
                if (!ApplyHotkeySettings(newSettings))
                {
                    ApplyHotkeySettings(oldSettings);
                    MessageBox.Show("快捷键监听失败，请重启工具或尝试以管理员身份运行。", "截图快捷键", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                settings = newSettings;
                settings.Save();
                UpdateTrayText();
            }
        }

        private void UpdateTrayText()
        {
            trayIcon.Text = Shorten("截图：" + settings.DisplayText + " OCR：" + settings.OcrDisplayText, 63);
        }

        private bool ApplyHotkeySettings(HotkeySettings candidate)
        {
            if (!screenshotHotkeyWindow.Register(candidate.Modifiers, candidate.KeyCode))
                return false;

            ocrHotkeyWindow.Unregister();
            if (candidate.OcrEnabled && !ocrHotkeyWindow.Register(candidate.OcrModifiers, candidate.OcrKeyCode))
                return false;

            return true;
        }

        private static string Shorten(string text, int maxLength)
        {
            if (text.Length <= maxLength)
                return text;
            return text.Substring(0, maxLength);
        }

        private void TriggerSnip()
        {
            StartScreenshotEditorSelection(false);
        }

        private void TriggerScrollingSnip()
        {
            StartFullScreenScrollingCapture();
        }

        private void TriggerOcr()
        {
            if (!settings.OcrEnabled)
            {
                MessageBox.Show("OCR 未启用，请在设置里开启。", "截图快捷键", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            StartScreenshotEditorSelection(true);
        }

        private void StartScreenshotEditorSelection(bool recognizeImmediately)
        {
            if (isCapturing)
                return;

            isCapturing = true;
            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "last-trigger.txt"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            Bitmap screenshot = null;
            try
            {
                var bounds = SystemInformation.VirtualScreen;
                screenshot = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
                using (var graphics = Graphics.FromImage(screenshot))
                {
                    graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
                }

                var overlay = new SelectionOverlayForm(bounds, screenshot, SaveBitmap, RecognizeText, settings, recognizeImmediately);
                overlay.FormClosed += delegate
                {
                    overlay.Dispose();
                    isCapturing = false;
                };
                overlay.Show();
                screenshot = null;
            }
            catch
            {
                if (screenshot != null)
                    screenshot.Dispose();
                isCapturing = false;
                throw;
            }
            finally
            {
            }
        }

        private void StartFullScreenScrollingCapture()
        {
            if (isCapturing)
                return;

            isCapturing = true;
            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "last-trigger.txt"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            try
            {
                CaptureScrollingRegion(GetFullScreenScrollingRegion());
            }
            catch
            {
                isCapturing = false;
                throw;
            }
        }

        private static Rectangle GetFullScreenScrollingRegion()
        {
            return Screen.FromPoint(Cursor.Position).WorkingArea;
        }

        private void CaptureScrollingRegion(Rectangle region)
        {
            AppendScrollingCaptureLog("selected " + region);
            ThreadPool.QueueUserWorkItem(delegate
            {
                Bitmap captured = null;
                Exception error = null;
                try
                {
                    var captureRegion = ScrollingCaptureRunner.ExpandScrollingRegionToWindowBottom(region, SystemInformation.VirtualScreen);
                    AppendScrollingCaptureLog("expanded scrolling region " + region + " -> " + captureRegion);
                    AppendScrollingCaptureLog("capture started");
                    captured = ScrollingCaptureRunner.Capture(captureRegion);
                    AppendScrollingCaptureLog("capture finished frames=" + ScrollingCaptureRunner.LastCaptureCount + " output=" + captured.Width + "x" + captured.Height + " stitch=" + ScrollingCaptureRunner.LastStitchSummary);
                }
                catch (Exception ex)
                {
                    error = ex;
                    AppendScrollingCaptureLog("capture failed " + ex);
                }

                uiInvoker.BeginInvoke((MethodInvoker)delegate
                {
                    AppendScrollingCaptureLog("ui callback");
                    isCapturing = false;
                    if (error != null)
                    {
                        if (captured != null)
                            captured.Dispose();
                        MessageBox.Show("滚动截屏失败：" + Environment.NewLine + error.Message, "截图快捷键", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }

                    if (captured == null)
                        return;

                    var preview = new PreviewForm(captured, SaveBitmap, RecognizeText, settings, true);
                    preview.Text = "滚动截屏预览";
                    preview.Show();
                    preview.WindowState = FormWindowState.Normal;
                    preview.TopMost = true;
                    preview.BringToFront();
                    preview.Activate();
                    preview.TopMost = false;
                    AppendScrollingCaptureLog("preview shown");
                });
            });
        }

        private static void AppendScrollingCaptureLog(string message)
        {
            try
            {
                File.AppendAllText(
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "scrolling-capture.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + message + Environment.NewLine,
                    Encoding.UTF8);
            }
            catch
            {
            }
        }

        private void StartSelection(Action<Bitmap> onCaptured)
        {
            if (isCapturing)
                return;

            isCapturing = true;
            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "last-trigger.txt"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            try
            {
                var bounds = SystemInformation.VirtualScreen;
                var screenshot = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
                using (var graphics = Graphics.FromImage(screenshot))
                {
                    graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
                }

                using (var overlay = new SelectionOverlayForm(bounds, screenshot, onCaptured))
                {
                    overlay.ShowDialog();
                }
            }
            finally
            {
                isCapturing = false;
            }
        }

        private void SaveCapturedImage(Bitmap image)
        {
            isCapturing = false;
            var preview = new PreviewForm(image, SaveBitmap, RecognizeText, settings);
            preview.Show();
        }

        private void RecognizeCapturedImage(Bitmap image)
        {
            isCapturing = false;
            try
            {
                var text = RecognizeText(image);
                var result = new OcrResultForm(text, settings);
                result.Show();
            }
            finally
            {
                image.Dispose();
            }
        }

        private string RecognizeText(Bitmap image)
        {
            try
            {
                return OcrRunner.Recognize(image, settings);
            }
            catch (Exception ex)
            {
                return "OCR 失败：" + Environment.NewLine + ex.Message;
            }
        }

        private string SaveBitmap(Bitmap image)
        {
            var directory = settings.SaveDirectory;
            if (string.IsNullOrWhiteSpace(directory))
                directory = HotkeySettings.DefaultSaveDirectory();

            Directory.CreateDirectory(directory);
            var filename = "截图_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png";
            string path;
            using (var dialog = new SaveFileDialog())
            {
                dialog.Title = "保存截图";
                dialog.InitialDirectory = directory;
                dialog.FileName = filename;
                dialog.Filter = "PNG 图片 (*.png)|*.png";
                dialog.DefaultExt = "png";
                dialog.AddExtension = true;
                dialog.OverwritePrompt = true;

                if (dialog.ShowDialog() != DialogResult.OK)
                    return null;

                path = dialog.FileName;
            }

            image.Save(path, ImageFormat.Png);
            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "last-save.txt"), path);
            return path;
        }

        protected override void ExitThreadCore()
        {
            if (trayIcon != null)
            {
                trayIcon.Visible = false;
                trayIcon.Dispose();
            }
            if (trayAppIcon != null)
                trayAppIcon.Dispose();
            if (uiInvoker != null)
                uiInvoker.Dispose();
            if (screenshotHotkeyWindow != null)
                screenshotHotkeyWindow.Dispose();
            if (ocrHotkeyWindow != null)
                ocrHotkeyWindow.Dispose();
            base.ExitThreadCore();
        }
    }

    internal sealed class SelectionOverlayForm : Form
    {
        private readonly Rectangle virtualBounds;
        private readonly Bitmap screenshot;
        private readonly Action<Bitmap> onCaptured;
        private readonly Action<Rectangle> onRegionCaptured;
        private readonly Func<Bitmap, string> saveImage;
        private readonly Func<Bitmap, string> recognizeText;
        private readonly HotkeySettings settings;
        private readonly bool inlineEditingMode;
        private readonly bool recognizeImmediately;
        private ImageCanvasControl editorCanvas;
        private Bitmap selectedOriginalImage;
        private InlineOcrTextControl inlineOcrBox;
        private Panel ocrResizeGrip;
        private FlowLayoutPanel editorToolbar;
        private FlowLayoutPanel ocrToolbar;
        private FlowLayoutPanel styleToolbar;
        private ToolTip toolTip;
        private Button drawButton;
        private Button rectangleButton;
        private Button textButton;
        private Button arrowButton;
        private Button numberButton;
        private Button mosaicButton;
        private Button cropButton;
        private Label sizeLabel;
        private Point startPoint;
        private Point currentPoint;
        private bool selecting;
        private bool editing;
        private bool movingSelectedImage;
        private bool movingInlineOcrBox;
        private bool resizingSelectedImage;
        private bool resizingInlineOcrBox;
        private bool inlineOcrFormatRemoved;
        private bool inlineOcrShowingTranslation;
        private bool resizeLeft;
        private bool resizeTop;
        private bool resizeRight;
        private bool resizeBottom;
        private Rectangle selectedBounds;
        private Rectangle moveStartBounds;
        private Rectangle resizeStartBounds;
        private Rectangle resizeStartWindowBounds;
        private Point moveStartPoint;
        private Point resizeStartPoint;
        private string inlineOcrFormattedText;
        private string inlineOcrTextBeforeTranslation;
        private static readonly Color TransparentEditorColor = Color.FromArgb(255, 1, 2, 3);

        public SelectionOverlayForm(Rectangle virtualBounds, Bitmap screenshot, Action<Bitmap> onCaptured)
            : this(virtualBounds, screenshot, onCaptured, null, null, null, false, false)
        {
        }

        public SelectionOverlayForm(Rectangle virtualBounds, Bitmap screenshot, Action<Rectangle> onRegionCaptured)
            : this(virtualBounds, screenshot, null, onRegionCaptured, null, null, null, false, false)
        {
        }

        public SelectionOverlayForm(Rectangle virtualBounds, Bitmap screenshot, Func<Bitmap, string> saveImage, Func<Bitmap, string> recognizeText, HotkeySettings settings, bool recognizeImmediately)
            : this(virtualBounds, screenshot, null, saveImage, recognizeText, settings, true, recognizeImmediately)
        {
        }

        private SelectionOverlayForm(Rectangle virtualBounds, Bitmap screenshot, Action<Bitmap> onCaptured, Func<Bitmap, string> saveImage, Func<Bitmap, string> recognizeText, HotkeySettings settings, bool inlineEditingMode, bool recognizeImmediately)
            : this(virtualBounds, screenshot, onCaptured, null, saveImage, recognizeText, settings, inlineEditingMode, recognizeImmediately)
        {
        }

        private SelectionOverlayForm(Rectangle virtualBounds, Bitmap screenshot, Action<Bitmap> onCaptured, Action<Rectangle> onRegionCaptured, Func<Bitmap, string> saveImage, Func<Bitmap, string> recognizeText, HotkeySettings settings, bool inlineEditingMode, bool recognizeImmediately)
        {
            this.virtualBounds = virtualBounds;
            this.screenshot = screenshot;
            this.onCaptured = onCaptured;
            this.onRegionCaptured = onRegionCaptured;
            this.saveImage = saveImage;
            this.recognizeText = recognizeText;
            this.settings = settings ?? HotkeySettings.Default();
            this.inlineEditingMode = inlineEditingMode;
            this.recognizeImmediately = recognizeImmediately;

            FormBorderStyle = FormBorderStyle.None;
            AutoScaleMode = AutoScaleMode.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Bounds = virtualBounds;
            Cursor = Cursors.Cross;
            KeyPreview = true;
            DoubleBuffered = true;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Activate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (editing)
            {
                e.Graphics.Clear(BackColor);

                if (inlineOcrBox != null)
                {
                    DrawInlineOcrResult(e.Graphics);
                    return;
                }

                DrawInlineScreenshotResult(e.Graphics);
                return;
            }

            e.Graphics.DrawImageUnscaled(screenshot, 0, 0);

            using (var overlayBrush = new SolidBrush(Color.FromArgb(95, Color.Black)))
            {
                e.Graphics.FillRectangle(overlayBrush, ClientRectangle);
            }

            var selection = editing ? selectedBounds : CurrentSelection;
            if (selection.Width > 0 && selection.Height > 0)
            {
                if (!editing)
                {
                    e.Graphics.SetClip(selection);
                    e.Graphics.DrawImageUnscaled(screenshot, 0, 0);
                    e.Graphics.ResetClip();
                }

                using (var borderPen = new Pen(Color.White, 2))
                using (var guidePen = new Pen(Color.FromArgb(210, 24, 119, 242), 1))
                {
                    e.Graphics.DrawRectangle(borderPen, selection);
                    e.Graphics.DrawRectangle(guidePen, selection.X + 2, selection.Y + 2, Math.Max(1, selection.Width - 4), Math.Max(1, selection.Height - 4));
                }
            }

            if (!editing)
                DrawHint(e.Graphics);
        }

        private void DrawInlineScreenshotResult(Graphics graphics)
        {
            Image image = editorCanvas != null ? editorCanvas.Image : selectedOriginalImage;
            if (image != null)
            {
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.DrawImage(image, selectedBounds);
            }

            using (var borderPen = new Pen(Color.Black, 2))
            using (var innerPen = new Pen(Color.White, 1))
            using (var guidePen = new Pen(Color.FromArgb(210, 24, 119, 242), 1))
            {
                graphics.DrawRectangle(borderPen, selectedBounds);
                graphics.DrawRectangle(innerPen, selectedBounds.X + 2, selectedBounds.Y + 2, Math.Max(1, selectedBounds.Width - 4), Math.Max(1, selectedBounds.Height - 4));
                graphics.DrawRectangle(guidePen, selectedBounds.X + 4, selectedBounds.Y + 4, Math.Max(1, selectedBounds.Width - 8), Math.Max(1, selectedBounds.Height - 8));
            }
        }

        private void DrawInlineOcrResult(Graphics graphics)
        {
            using (var background = new SolidBrush(Color.White))
            using (var textBrush = new SolidBrush(Color.Black))
            using (var borderPen = new Pen(Color.Black, 2))
            {
                graphics.FillRectangle(background, selectedBounds);
                graphics.DrawRectangle(borderPen, selectedBounds);

                var textBounds = new Rectangle(
                    selectedBounds.Left + 14,
                    selectedBounds.Top + 14,
                    Math.Max(1, selectedBounds.Width - 28),
                    Math.Max(1, selectedBounds.Height - 28));
                var displayText = string.IsNullOrWhiteSpace(inlineOcrBox.Text) ? "未识别到文字" : inlineOcrBox.Text;
                TextRenderer.DrawText(
                    graphics,
                    displayText,
                    inlineOcrBox.Font,
                    textBounds,
                    Color.Black,
                    Color.White,
                    TextFormatFlags.TextBoxControl | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (editing)
            {
                if (inlineOcrBox != null)
                    BeginResizeInlineOcrBox(this, e);
                else if (selectedBounds.Contains(e.Location))
                    BeginResizeSelectedImage(this, e);
                return;
            }

            if (e.Button != MouseButtons.Left)
                return;

            selecting = true;
            startPoint = e.Location;
            currentPoint = e.Location;
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (editing)
            {
                if (inlineOcrBox != null)
                    ResizeInlineOcrBox(this, e);
                else if (resizingSelectedImage)
                    ResizeSelectedImage(this, e);
                else if (movingSelectedImage)
                    MoveSelectedImage(this, e);
                else
                    UpdateSelectedImageResizeCursor(this, e.Location);
                return;
            }

            if (!selecting)
                return;

            currentPoint = e.Location;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (editing)
            {
                if (inlineOcrBox != null)
                    EndResizeInlineOcrBox(this, e);
                else if (resizingSelectedImage)
                    EndResizeSelectedImage(this, e);
                else if (movingSelectedImage)
                    EndMoveSelectedImage(this, e);
                return;
            }

            if (e.Button != MouseButtons.Left || !selecting)
                return;

            selecting = false;
            currentPoint = e.Location;

            var selection = CurrentSelection;
            if (selection.Width < 3 || selection.Height < 3)
            {
                Close();
                return;
            }

            if (inlineEditingMode)
            {
                BeginInlineEditing(selection);
                return;
            }

            Hide();
            if (onRegionCaptured != null)
            {
                onRegionCaptured(new Rectangle(
                    virtualBounds.Left + selection.Left,
                    virtualBounds.Top + selection.Top,
                    selection.Width,
                    selection.Height));
                Close();
                return;
            }

            using (var cropped = screenshot.Clone(selection, PixelFormat.Format32bppArgb))
            {
                if (onCaptured != null)
                    onCaptured((Bitmap)cropped.Clone());
            }
            Close();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
                Close();
            base.OnKeyDown(e);
        }

        private void BeginInlineEditing(Rectangle selection)
        {
            selecting = false;
            editing = true;
            Cursor = Cursors.Default;

            selectedOriginalImage = screenshot.Clone(selection, PixelFormat.Format32bppArgb);
            SwitchToFloatingEditorWindow(selection);
            editorCanvas = new ImageCanvasControl((Bitmap)selectedOriginalImage.Clone())
            {
                Bounds = selectedBounds,
                BackColor = Color.White,
                Cursor = Cursors.SizeAll,
                Visible = true
            };
            editorCanvas.ImageCropped += delegate { ResizeAfterImageCrop(); };
            editorCanvas.MouseDown += BeginResizeSelectedImage;
            editorCanvas.MouseMove += ResizeSelectedImage;
            editorCanvas.MouseUp += EndResizeSelectedImage;

            editorToolbar = CreateEditorToolbar();
            styleToolbar = CreateStyleToolbar();
            PositionFloatingToolbars();
            HideEditorToolbars();

            Controls.Add(editorCanvas);
            Controls.Add(editorToolbar);
            Controls.Add(styleToolbar);
            editorCanvas.BringToFront();
            editorToolbar.BringToFront();
            styleToolbar.BringToFront();
            UpdateOverlayRegion();
            Invalidate();

            if (recognizeImmediately)
                ShowInlineOcrResult(RecognizeImages(editorCanvas.GetImagesForOcr(selectedOriginalImage)));
            else
                ShowEditorToolbars();
        }

        private void SwitchToFloatingEditorWindow(Rectangle selection)
        {
            var toolbarReserve = 190;
            var windowWidth = Math.Min(virtualBounds.Width, Math.Max(selection.Width, 360));
            var windowHeight = Math.Min(virtualBounds.Height, selection.Height + toolbarReserve);
            var screenLeft = virtualBounds.Left + selection.Left;
            var screenTop = virtualBounds.Top + selection.Top;
            var left = Clamp(screenLeft, virtualBounds.Left, Math.Max(virtualBounds.Left, virtualBounds.Right - windowWidth));
            var top = Clamp(screenTop, virtualBounds.Top, Math.Max(virtualBounds.Top, virtualBounds.Bottom - windowHeight));

            Bounds = new Rectangle(left, top, windowWidth, windowHeight);
            BackColor = Color.FromArgb(245, 247, 250);
            TransparencyKey = Color.Empty;
            TopMost = true;
            ShowInTaskbar = false;
            selectedBounds = new Rectangle(
                Math.Max(0, screenLeft - left),
                Math.Max(0, screenTop - top),
                selection.Width,
                selection.Height);
            Activate();
        }

        private void ShowEditorToolbars()
        {
            if (inlineOcrBox != null)
                return;

            if (editorToolbar != null)
                editorToolbar.Visible = true;
            if (styleToolbar != null && editorCanvas != null)
                styleToolbar.Visible = editorCanvas.Mode != AnnotationMode.None;
            PositionFloatingToolbars();
            UpdateOverlayRegion();
        }

        private void HideEditorToolbars()
        {
            if (editorToolbar != null)
                editorToolbar.Visible = false;
            if (styleToolbar != null)
                styleToolbar.Visible = false;
            UpdateOverlayRegion();
        }

        private FlowLayoutPanel CreateEditorToolbar()
        {
            var toolbar = CreateFloatingToolbar(48);

            var pinButton = CreateToolButton("●", "主工具");
            rectangleButton = CreateToolButton("□", "框选");
            var arrowIconButton = CreateToolButton("↗", "箭头");
            arrowButton = arrowIconButton;
            drawButton = CreateToolButton("✎", "画笔");
            textButton = CreateToolButton("A", "文字");
            numberButton = CreateToolButton("①", "序号");
            mosaicButton = CreateToolButton("▦", "马赛克");
            cropButton = CreateToolButton("裁", "裁剪");
            var ocrButton = CreateToolButton("中A", "识别文字");
            var undoButton = CreateToolButton("↶", "撤销");
            var saveButton = CreateToolButton("↓", "保存");
            var copyButton = CreateToolButton("复制", "复制截图");
            var cancelButton = CreateToolButton("×", "取消");
            var doneButton = CreateToolButton("✓", "完成");

            toolbar.Controls.Add(pinButton);
            toolbar.Controls.Add(rectangleButton);
            toolbar.Controls.Add(arrowIconButton);
            toolbar.Controls.Add(drawButton);
            toolbar.Controls.Add(textButton);
            toolbar.Controls.Add(numberButton);
            toolbar.Controls.Add(mosaicButton);
            toolbar.Controls.Add(cropButton);
            toolbar.Controls.Add(ocrButton);
            toolbar.Controls.Add(undoButton);
            toolbar.Controls.Add(saveButton);
            toolbar.Controls.Add(copyButton);
            toolbar.Controls.Add(cancelButton);
            toolbar.Controls.Add(doneButton);

            rectangleButton.Click += delegate { ToggleEditorMode(AnnotationMode.Rectangle); };
            arrowIconButton.Click += delegate { ToggleEditorMode(AnnotationMode.Arrow); };
            drawButton.Click += delegate { ToggleEditorMode(AnnotationMode.Freehand); };
            textButton.Click += delegate { ToggleEditorMode(AnnotationMode.Text); };
            numberButton.Click += delegate { ToggleEditorMode(AnnotationMode.Number); };
            mosaicButton.Click += delegate { ToggleEditorMode(AnnotationMode.Mosaic); };
            cropButton.Click += delegate { ToggleEditorMode(AnnotationMode.Crop); };
            undoButton.Click += delegate { editorCanvas.Undo(); };
            saveButton.Click += delegate { SaveEditedImage(); };
            copyButton.Click += delegate { CopyEditedImage(); };
            ocrButton.Click += delegate
            {
                ShowInlineOcrResult(RecognizeImages(editorCanvas.GetImagesForOcr(selectedOriginalImage)));
            };
            cancelButton.Click += delegate { Close(); };
            doneButton.Click += delegate
            {
                Clipboard.SetImage((Bitmap)editorCanvas.Image.Clone());
                Close();
            };

            return toolbar;
        }

        private void CopyEditedImage()
        {
            Clipboard.SetImage((Bitmap)editorCanvas.Image.Clone());
        }

        private FlowLayoutPanel CreateStyleToolbar()
        {
            var toolbar = CreateFloatingToolbar(44);
            toolbar.Visible = false;

            toolbar.Controls.Add(CreateStyleButton("细", delegate { SetStrokeWidth(3); }));
            toolbar.Controls.Add(CreateStyleButton("粗", delegate { SetStrokeWidth(6); }));
            toolbar.Controls.Add(CreateStyleButton("红", delegate { SetStrokeColor(Color.Red); }));
            toolbar.Controls.Add(CreateStyleButton("黄", delegate { SetStrokeColor(Color.Gold); }));
            toolbar.Controls.Add(CreateStyleButton("白", delegate { SetStrokeColor(Color.White); }));

            sizeLabel = new Label
            {
                Text = "4",
                Width = 34,
                Height = 30,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.White,
                Margin = new Padding(8, 6, 2, 4)
            };
            toolbar.Controls.Add(sizeLabel);

            return toolbar;
        }

        private FlowLayoutPanel CreateOcrToolbar()
        {
            var toolbar = CreateFloatingToolbar(48);
            var translateButton = CreateToolButton("\u7ffb\u8bd1", "\u9009\u62e9\u7ffb\u8bd1\u76ee\u6807");
            var formatButton = CreateToolButton("\u53bb\u683c\u5f0f", "\u53bb\u683c\u5f0f");
            var copyButton = CreateToolButton("\u9009\u62e9\u590d\u5236", "\u5f00\u542f\u6587\u5b57\u9009\u4e2d\uff0c\u53ef\u53f3\u952e\u590d\u5236\u9009\u4e2d\u6bb5\u843d");
            var saveTextButton = CreateToolButton("\u4fdd\u5b58", "\u4fdd\u5b58\u6587\u5b57");
            var closeButton = CreateToolButton("\u5173\u95ed", "\u5173\u95ed");
            translateButton.Width = 72;
            copyButton.Width = 96;

            var translateMenu = new ContextMenuStrip();
            translateMenu.Items.Add("\u8f6c\u4e2d\u6587", null, delegate { TranslateInlineOcrText("zh-CN", translateButton); });
            translateMenu.Items.Add("\u8f6c\u82f1\u6587", null, delegate { TranslateInlineOcrText("en", translateButton); });
            translateMenu.Items.Add("\u8f6c\u5fb7\u6587", null, delegate { TranslateInlineOcrText("de", translateButton); });
            translateMenu.Items.Add("\u8f6c\u6cd5\u8bed", null, delegate { TranslateInlineOcrText("fr", translateButton); });
            translateMenu.Items.Add("\u8f6c\u897f\u73ed\u7259\u8bed", null, delegate { TranslateInlineOcrText("es", translateButton); });
            translateMenu.Items.Add("\u8f6c\u610f\u5927\u5229\u8bed", null, delegate { TranslateInlineOcrText("it", translateButton); });
            translateMenu.Items.Add("\u8f6c\u4fc4\u8bed", null, delegate { TranslateInlineOcrText("ru", translateButton); });
            translateMenu.Items.Add("\u8f6c\u8377\u5170\u8bed", null, delegate { TranslateInlineOcrText("nl", translateButton); });

            toolbar.Controls.Add(translateButton);
            toolbar.Controls.Add(formatButton);
            toolbar.Controls.Add(copyButton);
            toolbar.Controls.Add(saveTextButton);
            toolbar.Controls.Add(closeButton);

            translateButton.Click += delegate
            {
                if (inlineOcrShowingTranslation)
                {
                    RestoreInlineOcrOriginal(translateButton);
                    return;
                }

                translateMenu.Show(translateButton, new Point(0, translateButton.Height));
            };
            formatButton.Click += delegate
            {
                if (inlineOcrBox == null)
                    return;

                if (inlineOcrFormatRemoved)
                {
                    SetInlineOcrText(inlineOcrFormattedText);
                    inlineOcrFormatRemoved = false;
                    formatButton.Text = "\u53bb\u683c\u5f0f";
                    toolTip.SetToolTip(formatButton, "\u53bb\u683c\u5f0f");
                }
                else
                {
                    SetInlineOcrText(RemoveTextFormatting(inlineOcrFormattedText));
                    inlineOcrFormatRemoved = true;
                    formatButton.Text = "\u590d\u539f\u683c\u5f0f";
                    toolTip.SetToolTip(formatButton, "\u590d\u539f\u683c\u5f0f");
                }
                ClearInlineTranslationState(translateButton);
            };
            copyButton.Click += delegate
            {
                if (inlineOcrBox == null)
                    return;

                inlineOcrBox.SelectionCopyMode = !inlineOcrBox.SelectionCopyMode;
                movingInlineOcrBox = false;
                resizingInlineOcrBox = false;
                copyButton.Text = inlineOcrBox.SelectionCopyMode ? "\u9000\u51fa\u9009\u62e9" : "\u9009\u62e9\u590d\u5236";
                toolTip.SetToolTip(
                    copyButton,
                    inlineOcrBox.SelectionCopyMode
                        ? "\u5df2\u53ef\u9009\u4e2d\u6587\u5b57\uff1b\u9009\u4e2d\u540e\u53f3\u952e\u590d\u5236\uff0c\u518d\u70b9\u4e00\u6b21\u6062\u590d\u62d6\u52a8"
                        : "\u5f00\u542f\u6587\u5b57\u9009\u4e2d\uff0c\u53ef\u53f3\u952e\u590d\u5236\u9009\u4e2d\u6bb5\u843d");
            };
            saveTextButton.Click += delegate { SaveInlineOcrText(); };
            closeButton.Click += delegate { Close(); };

            return toolbar;
        }
        private static FlowLayoutPanel CreateFloatingToolbar(int height)
        {
            return new FlowLayoutPanel
            {
                Height = height,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(8, 6, 8, 6),
                FlowDirection = FlowDirection.LeftToRight,
                BackColor = Color.FromArgb(42, 42, 42),
                WrapContents = false
            };
        }

        private Button CreateToolButton(string text, string tip)
        {
            if (toolTip == null)
            {
                toolTip = new ToolTip
                {
                    AutomaticDelay = 250,
                    AutoPopDelay = 4000,
                    InitialDelay = 250,
                    ReshowDelay = 100,
                    ShowAlways = true
                };
            }

            var button = new Button
            {
                Text = text,
                Width = text.Length >= 3 ? 58 : (text.Length > 1 ? 48 : 38),
                Height = 30,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(42, 42, 42),
                ForeColor = Color.White,
                Margin = new Padding(4, 3, 4, 3)
            };
            button.FlatAppearance.BorderSize = 0;
            button.Tag = tip;
            toolTip.SetToolTip(button, tip);
            return button;
        }

        private Button CreateStyleButton(string text, EventHandler click)
        {
            var button = CreateToolButton(text, text);
            button.Width = 42;
            button.Click += click;
            return button;
        }

        private void PositionFloatingToolbars()
        {
            var mainToolbar = ocrToolbar ?? editorToolbar;
            var toolbarSize = mainToolbar.GetPreferredSize(Size.Empty);
            var showStyleToolbar = styleToolbar != null && styleToolbar.Visible && editorCanvas != null && editorCanvas.Mode != AnnotationMode.None;
            var styleSize = showStyleToolbar ? styleToolbar.GetPreferredSize(Size.Empty) : Size.Empty;
            EnsureFloatingWindowCanFitToolbars(toolbarSize, styleSize);

            toolbarSize = mainToolbar.GetPreferredSize(Size.Empty);
            showStyleToolbar = styleToolbar != null && styleToolbar.Visible && editorCanvas != null && editorCanvas.Mode != AnnotationMode.None;
            styleSize = showStyleToolbar ? styleToolbar.GetPreferredSize(Size.Empty) : Size.Empty;
            var toolbarX = Clamp(selectedBounds.Left + (selectedBounds.Width - toolbarSize.Width) / 2, 8, Math.Max(8, ClientSize.Width - toolbarSize.Width - 8));
            var toolbarY = selectedBounds.Bottom + 10;
            if (toolbarY + toolbarSize.Height + styleSize.Height + 18 > ClientSize.Height)
                toolbarY = Math.Max(8, selectedBounds.Top - toolbarSize.Height - styleSize.Height - 18);
            if (selectedBounds.Bottom + 10 + toolbarSize.Height + styleSize.Height + 8 <= ClientSize.Height)
                toolbarY = selectedBounds.Bottom + 10;
            toolbarY = Clamp(toolbarY, 8, Math.Max(8, ClientSize.Height - toolbarSize.Height - 8));

            mainToolbar.Location = new Point(toolbarX, toolbarY);
            if (styleToolbar != null)
            {
                styleToolbar.Location = new Point(
                    Clamp(selectedBounds.Left + (selectedBounds.Width - styleSize.Width) / 2, 8, Math.Max(8, ClientSize.Width - styleSize.Width - 8)),
                    toolbarY + toolbarSize.Height + 8);
            }
            UpdateOverlayRegion();
        }

        private void FitFloatingWindowBottom(int toolbarY, Size toolbarSize, Size styleSize)
        {
            var toolbarBottom = toolbarY + toolbarSize.Height;
            if (styleSize.Height > 0)
                toolbarBottom += styleSize.Height + 8;

            var requiredHeight = Math.Max(selectedBounds.Bottom, toolbarBottom) + 8;
            requiredHeight = Math.Min(virtualBounds.Height, Math.Max(requiredHeight, selectedBounds.Bottom + 8));
            if (Math.Abs(ClientSize.Height - requiredHeight) <= 3)
                return;

            Bounds = new Rectangle(Left, Top, Width, requiredHeight);
        }

        private void EnsureFloatingWindowCanFitToolbars(Size toolbarSize, Size styleSize)
        {
            var requiredWidth = Math.Max(selectedBounds.Width, Math.Max(toolbarSize.Width, styleSize.Width) + 16);
            var toolbarStackHeight = toolbarSize.Height + (styleSize.Height > 0 ? styleSize.Height + 8 : 0);
            var requiredHeight = selectedBounds.Bottom + 10 + toolbarStackHeight + 8;
            var needsWidth = ClientSize.Width < requiredWidth && Width < virtualBounds.Width;
            var needsHeight = ClientSize.Height < requiredHeight && Height < virtualBounds.Height;
            if (!needsWidth && !needsHeight)
                return;

            var imageScreenLeft = Left + selectedBounds.Left;
            var imageScreenTop = Top + selectedBounds.Top;
            var newWidth = needsWidth ? Math.Min(virtualBounds.Width, requiredWidth) : Width;
            var newHeight = needsHeight ? Math.Min(virtualBounds.Height, requiredHeight) : Height;
            var newLeft = Clamp(
                imageScreenLeft + selectedBounds.Width / 2 - newWidth / 2,
                virtualBounds.Left,
                Math.Max(virtualBounds.Left, virtualBounds.Right - newWidth));
            var newTop = Clamp(
                Top,
                virtualBounds.Top,
                Math.Max(virtualBounds.Top, virtualBounds.Bottom - newHeight));

            Bounds = new Rectangle(newLeft, newTop, newWidth, newHeight);
            selectedBounds = new Rectangle(
                Math.Max(0, imageScreenLeft - newLeft),
                Math.Max(0, imageScreenTop - newTop),
                selectedBounds.Width,
                selectedBounds.Height);

            if (editorCanvas != null)
                editorCanvas.Bounds = selectedBounds;
            if (inlineOcrBox != null)
                inlineOcrBox.Bounds = selectedBounds;
            if (ocrResizeGrip != null)
                ocrResizeGrip.Bounds = ResizeGripBounds();
        }

        private void UpdateOverlayRegion()
        {
            if (!editing)
                return;

            if (Region != null)
            {
                var oldRegion = Region;
                Region = null;
                oldRegion.Dispose();
            }
        }

        private static void AddVisibleControlToRegion(GraphicsPath path, Control control)
        {
            if (control != null && control.Visible)
                AddControlBoundsToRegion(path, control);
        }

        private static void AddControlBoundsToRegion(GraphicsPath path, Control control)
        {
            if (control == null)
                return;

            var bounds = control.Bounds;
            var preferred = control.GetPreferredSize(Size.Empty);
            if (preferred.Width > bounds.Width || preferred.Height > bounds.Height)
                bounds = new Rectangle(control.Location, preferred);

            if (bounds.Width > 0 && bounds.Height > 0)
                path.AddRectangle(bounds);
        }

        private void ToggleEditorMode(AnnotationMode mode)
        {
            editorCanvas.Mode = editorCanvas.Mode == mode ? AnnotationMode.None : mode;
            editorCanvas.Visible = true;
            editorCanvas.BringToFront();
            if (editorToolbar != null)
                editorToolbar.BringToFront();
            if (styleToolbar != null)
                styleToolbar.BringToFront();
            styleToolbar.Visible = editorCanvas.Mode != AnnotationMode.None && editorCanvas.Mode != AnnotationMode.Crop;
            UpdateEditorButtons();
            UpdateOverlayRegion();
            Invalidate();
        }

        private void UpdateEditorButtons()
        {
            MarkToolButton(drawButton, editorCanvas.Mode == AnnotationMode.Freehand);
            MarkToolButton(rectangleButton, editorCanvas.Mode == AnnotationMode.Rectangle);
            MarkToolButton(textButton, editorCanvas.Mode == AnnotationMode.Text);
            MarkToolButton(arrowButton, editorCanvas.Mode == AnnotationMode.Arrow);
            MarkToolButton(numberButton, editorCanvas.Mode == AnnotationMode.Number);
            MarkToolButton(mosaicButton, editorCanvas.Mode == AnnotationMode.Mosaic);
            MarkToolButton(cropButton, editorCanvas.Mode == AnnotationMode.Crop);
            editorCanvas.Cursor = editorCanvas.Mode == AnnotationMode.None ? Cursors.SizeAll : Cursors.Cross;
        }

        private void ResizeAfterImageCrop()
        {
            if (editorCanvas == null)
                return;

            if (selectedOriginalImage != null)
                selectedOriginalImage.Dispose();
            selectedOriginalImage = (Bitmap)editorCanvas.Image.Clone();
            var screenBounds = new Rectangle(
                Left + selectedBounds.Left,
                Top + selectedBounds.Top,
                editorCanvas.Image.Width,
                editorCanvas.Image.Height);
            ResizeFloatingEditorWindow(screenBounds, 190);
        }

        private static void MarkToolButton(Button button, bool selected)
        {
            if (button == null)
                return;

            button.BackColor = selected ? Color.FromArgb(24, 119, 242) : Color.FromArgb(42, 42, 42);
        }

        private void BeginMoveSelectedImage(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || editorCanvas == null || editorCanvas.Mode != AnnotationMode.None || inlineOcrBox != null)
                return;

            ShowEditorToolbars();
            movingSelectedImage = true;
            var sourceControl = sender as Control;
            moveStartPoint = sourceControl.PointToScreen(e.Location);
            moveStartBounds = Bounds;
            editorCanvas.Cursor = Cursors.SizeAll;
        }

        private void MoveSelectedImage(object sender, MouseEventArgs e)
        {
            if (!movingSelectedImage || editorCanvas == null)
                return;

            var sourceControl = sender as Control;
            var currentPoint = sourceControl.PointToScreen(e.Location);
            var dx = currentPoint.X - moveStartPoint.X;
            var dy = currentPoint.Y - moveStartPoint.Y;
            var newLeft = Clamp(moveStartBounds.Left + dx, virtualBounds.Left, Math.Max(virtualBounds.Left, virtualBounds.Right - moveStartBounds.Width));
            var newTop = Clamp(moveStartBounds.Top + dy, virtualBounds.Top, Math.Max(virtualBounds.Top, virtualBounds.Bottom - moveStartBounds.Height));
            Bounds = new Rectangle(newLeft, newTop, moveStartBounds.Width, moveStartBounds.Height);
        }

        private void EndMoveSelectedImage(object sender, MouseEventArgs e)
        {
            if (!movingSelectedImage)
                return;

            movingSelectedImage = false;
            if (editorCanvas != null)
                editorCanvas.Cursor = editorCanvas.Mode == AnnotationMode.None ? Cursors.SizeAll : Cursors.Cross;
        }

        private void BeginResizeSelectedImage(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || editorCanvas == null || editorCanvas.Mode != AnnotationMode.None || inlineOcrBox != null)
                return;

            if (!GetSelectedImageResizeEdges(sender, e.Location))
            {
                BeginMoveSelectedImage(sender, e);
                return;
            }

            ShowEditorToolbars();
            resizingSelectedImage = true;
            var sourceControl = sender as Control;
            resizeStartPoint = sourceControl.PointToScreen(e.Location);
            resizeStartBounds = selectedBounds;
            resizeStartWindowBounds = Bounds;
            Cursor = CursorForResizeEdges(true);
            editorCanvas.Cursor = Cursor;
        }

        private void ResizeSelectedImage(object sender, MouseEventArgs e)
        {
            if (editorCanvas == null)
                return;

            if (!resizingSelectedImage)
            {
                if (movingSelectedImage)
                    MoveSelectedImage(sender, e);
                else
                    UpdateSelectedImageResizeCursor(sender, e.Location);
                return;
            }

            var sourceControl = sender as Control;
            var currentPoint = sourceControl.PointToScreen(e.Location);
            var dx = currentPoint.X - resizeStartPoint.X;
            var dy = currentPoint.Y - resizeStartPoint.Y;
            var imageLeft = resizeStartWindowBounds.Left + resizeStartBounds.Left;
            var imageTop = resizeStartWindowBounds.Top + resizeStartBounds.Top;
            var left = imageLeft;
            var top = imageTop;
            var right = imageLeft + resizeStartBounds.Width;
            var bottom = imageTop + resizeStartBounds.Height;
            const int minWidth = 160;
            const int minHeight = 90;
            const int toolbarReserve = 96;

            if (resizeLeft)
                left = Clamp(imageLeft + dx, virtualBounds.Left, right - minWidth);
            if (resizeTop)
                top = Clamp(imageTop + dy, virtualBounds.Top, bottom - minHeight);
            if (resizeRight)
                right = Clamp(right + dx, left + minWidth, virtualBounds.Right);
            if (resizeBottom)
                bottom = Clamp(bottom + dy, top + minHeight, virtualBounds.Bottom - toolbarReserve);

            ResizeFloatingEditorWindow(new Rectangle(left, top, right - left, bottom - top), toolbarReserve);
        }

        private void EndResizeSelectedImage(object sender, MouseEventArgs e)
        {
            if (!resizingSelectedImage)
            {
                if (movingSelectedImage)
                    EndMoveSelectedImage(sender, e);
                return;
            }

            resizingSelectedImage = false;
            resizeLeft = false;
            resizeTop = false;
            resizeRight = false;
            resizeBottom = false;
            Cursor = Cursors.Default;
            if (editorCanvas != null)
                editorCanvas.Cursor = editorCanvas.Mode == AnnotationMode.None ? Cursors.SizeAll : Cursors.Cross;
        }

        private void ResizeFloatingEditorWindow(Rectangle imageScreenBounds, int toolbarReserve)
        {
            var windowWidth = Math.Min(virtualBounds.Width, Math.Max(imageScreenBounds.Width, 360));
            var windowHeight = Math.Min(virtualBounds.Height, imageScreenBounds.Height + toolbarReserve);
            var left = Clamp(imageScreenBounds.Left, virtualBounds.Left, Math.Max(virtualBounds.Left, virtualBounds.Right - windowWidth));
            var top = Clamp(imageScreenBounds.Top, virtualBounds.Top, Math.Max(virtualBounds.Top, virtualBounds.Bottom - windowHeight));

            Bounds = new Rectangle(left, top, windowWidth, windowHeight);
            selectedBounds = new Rectangle(
                Math.Max(0, imageScreenBounds.Left - left),
                Math.Max(0, imageScreenBounds.Top - top),
                imageScreenBounds.Width,
                imageScreenBounds.Height);
            if (editorCanvas != null)
                editorCanvas.Bounds = selectedBounds;
            if (inlineOcrBox != null)
                inlineOcrBox.Bounds = selectedBounds;
            if (ocrResizeGrip != null)
                ocrResizeGrip.Bounds = ResizeGripBounds();
            PositionFloatingToolbars();
            UpdateOverlayRegion();
            Invalidate();
        }

        private void UpdateSelectedImageResizeCursor(object sender, Point location)
        {
            if (editorCanvas == null || editorCanvas.Mode != AnnotationMode.None || inlineOcrBox != null)
                return;

            var active = GetSelectedImageResizeEdges(sender, location);
            var cursor = active ? CursorForResizeEdges(true) : Cursors.SizeAll;
            Cursor = cursor;
            editorCanvas.Cursor = cursor;
        }

        private bool GetSelectedImageResizeEdges(object sender, Point location)
        {
            resizeLeft = false;
            resizeTop = false;
            resizeRight = false;
            resizeBottom = false;

            var control = sender as Control;
            if (control == null)
                return false;

            Point point = location;
            if (control != this)
                point = PointToClient(control.PointToScreen(location));

            const int margin = 10;
            var nearHorizontal = point.Y >= selectedBounds.Top - margin && point.Y <= selectedBounds.Bottom + margin;
            var nearVertical = point.X >= selectedBounds.Left - margin && point.X <= selectedBounds.Right + margin;
            resizeLeft = nearHorizontal && Math.Abs(point.X - selectedBounds.Left) <= margin;
            resizeRight = nearHorizontal && Math.Abs(point.X - selectedBounds.Right) <= margin;
            resizeTop = nearVertical && Math.Abs(point.Y - selectedBounds.Top) <= margin;
            resizeBottom = nearVertical && Math.Abs(point.Y - selectedBounds.Bottom) <= margin;
            return resizeLeft || resizeRight || resizeTop || resizeBottom;
        }

        private void SetStrokeWidth(int width)
        {
            editorCanvas.StrokeWidth = width;
            if (sizeLabel != null)
                sizeLabel.Text = width.ToString();
        }

        private void SetStrokeColor(Color color)
        {
            editorCanvas.StrokeColor = color;
        }

        private void SaveEditedImage()
        {
            using (var copy = (Bitmap)editorCanvas.Image.Clone())
            {
                saveImage(copy);
            }
        }

        private void ShowInlineOcrResult(string text)
        {
            inlineOcrFormattedText = text ?? string.Empty;
            inlineOcrFormatRemoved = false;
            inlineOcrShowingTranslation = false;
            inlineOcrTextBeforeTranslation = null;
            var ocrFont = new Font("Microsoft YaHei UI", 14, FontStyle.Regular);
            EnsureFloatingWindowHasOcrWorkspace(inlineOcrFormattedText, ocrFont);

            if (editorCanvas != null)
                editorCanvas.Visible = false;
            if (editorToolbar != null)
                editorToolbar.Visible = false;
            if (styleToolbar != null)
                styleToolbar.Visible = false;

            inlineOcrBox = new InlineOcrTextControl
            {
                Bounds = selectedBounds,
                WordWrap = true,
                Font = ocrFont,
                BorderStyle = BorderStyle.FixedSingle,
                Text = string.IsNullOrWhiteSpace(inlineOcrFormattedText) ? "未识别到文字" : inlineOcrFormattedText,
                BackColor = Color.White,
                ForeColor = Color.Black,
                SelectionCopyMode = false,
                Cursor = Cursors.SizeAll,
                Visible = true
            };
            inlineOcrBox.MouseDown += BeginResizeInlineOcrBox;
            inlineOcrBox.MouseMove += ResizeInlineOcrBox;
            inlineOcrBox.MouseUp += EndResizeInlineOcrBox;
            ocrResizeGrip = new Panel
            {
                Bounds = ResizeGripBounds(),
                BackColor = Color.FromArgb(24, 119, 242),
                Cursor = Cursors.SizeNWSE
            };
            ocrResizeGrip.MouseDown += BeginResizeInlineOcrBox;
            ocrResizeGrip.MouseMove += ResizeInlineOcrBox;
            ocrResizeGrip.MouseUp += EndResizeInlineOcrBox;

            ocrToolbar = CreateOcrToolbar();
            PositionFloatingToolbars();
            Controls.Add(ocrResizeGrip);
            Controls.Add(ocrToolbar);
            Controls.Add(inlineOcrBox);
            inlineOcrBox.BringToFront();
            ocrResizeGrip.BringToFront();
            ocrToolbar.BringToFront();
            UpdateOverlayRegion();
            Invalidate();
        }

        private void SetInlineOcrText(string text)
        {
            if (inlineOcrBox == null)
                return;

            inlineOcrBox.Text = string.IsNullOrWhiteSpace(text) ? "\u672a\u8bc6\u522b\u5230\u6587\u5b57" : text;
            ExpandInlineOcrBoxForText(inlineOcrBox.Text);
            Invalidate();
        }

        private void EnsureFloatingWindowHasOcrWorkspace(string text, Font font)
        {
            var toolbarReserve = 86;
            var textScreenLeft = Bounds.Left + selectedBounds.Left;
            var textScreenTop = Bounds.Top + selectedBounds.Top;
            var textWidth = selectedBounds.Width;
            var textHeight = selectedBounds.Height;
            var desiredSize = MeasureInlineOcrBoxSize(text, font, new Size(textWidth, textHeight), toolbarReserve);
            var targetWidth = desiredSize.Width;
            var targetHeight = desiredSize.Height;
            var windowWidth = targetWidth;
            var windowHeight = Math.Min(virtualBounds.Height, targetHeight + toolbarReserve);
            var left = Clamp(textScreenLeft, virtualBounds.Left, Math.Max(virtualBounds.Left, virtualBounds.Right - windowWidth));
            var top = Clamp(textScreenTop, virtualBounds.Top, Math.Max(virtualBounds.Top, virtualBounds.Bottom - windowHeight));

            Bounds = new Rectangle(left, top, windowWidth, windowHeight);
            selectedBounds = new Rectangle(
                0,
                0,
                targetWidth,
                targetHeight);
        }

        private void ExpandInlineOcrBoxForText(string text)
        {
            if (inlineOcrBox == null)
                return;

            var desiredSize = MeasureInlineOcrBoxSize(text, inlineOcrBox.Font, selectedBounds.Size, 86);
            if (desiredSize.Width <= selectedBounds.Width && desiredSize.Height <= selectedBounds.Height)
                return;

            var screenBounds = new Rectangle(
                Left + selectedBounds.Left,
                Top + selectedBounds.Top,
                Math.Max(selectedBounds.Width, desiredSize.Width),
                Math.Max(selectedBounds.Height, desiredSize.Height));
            ResizeFloatingEditorWindow(screenBounds, 86);
        }

        private Size MeasureInlineOcrBoxSize(string text, Font font, Size minimumSize, int toolbarReserve)
        {
            var displayText = string.IsNullOrWhiteSpace(text) ? "\u672a\u8bc6\u522b\u5230\u6587\u5b57" : text;
            var minWidth = Math.Max(minimumSize.Width, 260);
            var minHeight = Math.Max(minimumSize.Height, 120);
            var maxWidth = Math.Min(Math.Max(minWidth, 900), Math.Max(minWidth, virtualBounds.Width - 24));
            var maxHeight = Math.Min(Math.Max(minHeight, 720), Math.Max(minHeight, virtualBounds.Height - toolbarReserve - 24));

            var longestLineWidth = minWidth;
            var lines = displayText.Replace("\r\n", "\n").Split('\n');
            foreach (var line in lines)
            {
                var measuredLine = TextRenderer.MeasureText(
                    string.IsNullOrEmpty(line) ? " " : line,
                    font,
                    new Size(int.MaxValue, int.MaxValue),
                    TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                longestLineWidth = Math.Max(longestLineWidth, measuredLine.Width + 36);
            }

            var targetWidth = Clamp(Math.Min(longestLineWidth, 760), minWidth, maxWidth);
            var targetHeight = MeasureWrappedInlineOcrTextHeight(displayText, font, targetWidth);
            while (targetHeight > maxHeight && targetWidth < maxWidth)
            {
                targetWidth = Math.Min(maxWidth, targetWidth + 120);
                targetHeight = MeasureWrappedInlineOcrTextHeight(displayText, font, targetWidth);
            }

            return new Size(
                targetWidth,
                Clamp(targetHeight, minHeight, maxHeight));
        }

        private static int MeasureWrappedInlineOcrTextHeight(string text, Font font, int boxWidth)
        {
            var textWidth = Math.Max(1, boxWidth - 28);
            var measured = TextRenderer.MeasureText(
                text,
                font,
                new Size(textWidth, int.MaxValue),
                TextFormatFlags.TextBoxControl | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            return measured.Height + 32;
        }

        private void TranslateInlineOcrText(string targetLanguage, Button primaryButton, params Button[] secondaryButtons)
        {
            if (inlineOcrBox == null)
                return;

            if (inlineOcrShowingTranslation)
            {
                RestoreInlineOcrOriginal(primaryButton);
                return;
            }

            var sourceText = inlineOcrBox.Text;
            if (string.IsNullOrWhiteSpace(sourceText))
                return;

            inlineOcrTextBeforeTranslation = sourceText;
            SetTranslationButtonsEnabled(false, primaryButton, secondaryButtons);
            ThreadPool.QueueUserWorkItem(delegate
            {
                string translatedText = null;
                Exception error = null;
                try
                {
                    translatedText = TranslationRunner.TranslatePreservingLines(sourceText, targetLanguage, settings);
                }
                catch (Exception ex)
                {
                    error = ex;
                }

                BeginInvoke((MethodInvoker)delegate
                {
                    SetTranslationButtonsEnabled(true, primaryButton, secondaryButtons);
                    if (error != null)
                    {
                        SetInlineOcrText("\u7ffb\u8bd1\u5931\u8d25" + Environment.NewLine + error.Message + Environment.NewLine + Environment.NewLine + sourceText);
                        ClearInlineTranslationState(CombineButtons(primaryButton, secondaryButtons));
                        return;
                    }

                    SetInlineOcrText(translatedText);
                    inlineOcrShowingTranslation = true;
                    primaryButton.Text = "\u590d\u539f";
                    toolTip.SetToolTip(primaryButton, "\u590d\u539f\u539f\u6587");
                });
            });
        }

        private void SetTranslationButtonsEnabled(bool enabled, Button primaryButton, Button[] secondaryButtons)
        {
            if (primaryButton != null)
                primaryButton.Enabled = enabled;
            if (secondaryButtons == null)
                return;

            foreach (var button in secondaryButtons)
            {
                if (button != null)
                    button.Enabled = enabled;
            }
        }

        private Button[] CombineButtons(Button primaryButton, Button[] secondaryButtons)
        {
            var buttons = new List<Button>();
            if (primaryButton != null)
                buttons.Add(primaryButton);
            if (secondaryButtons != null)
                buttons.AddRange(secondaryButtons);
            return buttons.ToArray();
        }

        private void ClearInlineTranslationState(params Button[] buttons)
        {
            inlineOcrShowingTranslation = false;
            inlineOcrTextBeforeTranslation = null;
            if (buttons == null)
                return;

            foreach (var button in buttons)
            {
                if (button != null)
                    SetTranslationButtonText(button, string.IsNullOrEmpty(button.AccessibleName) ? null : button.AccessibleName);
            }
        }

        private void SetTranslationButtonText(Button button, string targetLanguage)
        {
            if (button == null)
                return;

            button.Text = TranslationButtonText(targetLanguage);
            toolTip.SetToolTip(button, button.Text);
        }

        private void RestoreInlineOcrOriginal(Button translateButton)
        {
            SetInlineOcrText(inlineOcrTextBeforeTranslation ?? inlineOcrFormattedText);
            inlineOcrShowingTranslation = false;
            inlineOcrTextBeforeTranslation = null;
            SetTranslationButtonText(translateButton, null);
        }

        private static string TranslationButtonText(string targetLanguage)
        {
            if (string.IsNullOrEmpty(targetLanguage))
                return "\u7ffb\u8bd1";
            if (targetLanguage == "en")
                return "\u8f6c\u82f1";
            if (targetLanguage == "de")
                return "\u8f6c\u5fb7";
            if (targetLanguage == "fr")
                return "\u8f6c\u6cd5";
            if (targetLanguage == "es")
                return "\u8f6c\u897f";
            if (targetLanguage == "it")
                return "\u8f6c\u610f";
            if (targetLanguage == "ru")
                return "\u8f6c\u4fc4";
            if (targetLanguage == "nl")
                return "\u8f6c\u8377";
            return "\u8f6c\u4e2d";
        }

        private void SaveInlineOcrText()
        {
            if (inlineOcrBox == null)
                return;

            using (var dialog = new SaveFileDialog())
            {
                dialog.Title = "保存文字";
                dialog.FileName = "识别文字_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt";
                dialog.Filter = "文本文件 (*.txt)|*.txt";
                dialog.DefaultExt = "txt";
                dialog.AddExtension = true;
                if (dialog.ShowDialog() == DialogResult.OK)
                File.WriteAllText(dialog.FileName, inlineOcrBox.Text);
            }
        }

        private Rectangle ResizeGripBounds()
        {
            var size = 16;
            return new Rectangle(selectedBounds.Right - size, selectedBounds.Bottom - size, size, size);
        }

        private void BeginResizeInlineOcrBox(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || inlineOcrBox == null)
                return;
            if (sender == inlineOcrBox && inlineOcrBox.SelectionCopyMode)
                return;

            var edges = GetInlineOcrResizeEdges(sender, e.Location);
            if (!edges)
            {
                BeginMoveInlineOcrBox(sender, e);
                return;
            }

            resizingInlineOcrBox = true;
            var sourceControl = sender as Control;
            resizeStartPoint = sourceControl.PointToScreen(e.Location);
            resizeStartBounds = selectedBounds;
            resizeStartWindowBounds = Bounds;
        }

        private void ResizeInlineOcrBox(object sender, MouseEventArgs e)
        {
            if (sender == inlineOcrBox && inlineOcrBox != null && inlineOcrBox.SelectionCopyMode)
                return;

            if (!resizingInlineOcrBox && inlineOcrBox != null)
            {
                if (movingInlineOcrBox)
                {
                    MoveInlineOcrBox(sender, e);
                    return;
                }

                var active = GetInlineOcrResizeEdges(sender, e.Location);
                var cursor = active ? CursorForResizeEdges(true) : Cursors.SizeAll;
                if (sender == this)
                    Cursor = cursor;
                else
                    inlineOcrBox.Cursor = cursor;
                return;
            }

            if (!resizingInlineOcrBox || inlineOcrBox == null)
                return;

            var sourceControl = sender as Control;
            var currentPoint = sourceControl.PointToScreen(e.Location);
            var dx = currentPoint.X - resizeStartPoint.X;
            var dy = currentPoint.Y - resizeStartPoint.Y;
            var textLeft = resizeStartWindowBounds.Left + resizeStartBounds.Left;
            var textTop = resizeStartWindowBounds.Top + resizeStartBounds.Top;
            var left = textLeft;
            var top = textTop;
            var right = textLeft + resizeStartBounds.Width;
            var bottom = textTop + resizeStartBounds.Height;
            const int minWidth = 180;
            const int minHeight = 110;
            const int toolbarReserve = 86;

            if (resizeLeft)
                left = Clamp(textLeft + dx, virtualBounds.Left, right - minWidth);
            if (resizeTop)
                top = Clamp(textTop + dy, virtualBounds.Top, bottom - minHeight);
            if (resizeRight)
                right = Clamp(right + dx, left + minWidth, virtualBounds.Right);
            if (resizeBottom)
                bottom = Clamp(bottom + dy, top + minHeight, virtualBounds.Bottom - toolbarReserve);

            ResizeFloatingEditorWindow(new Rectangle(left, top, right - left, bottom - top), toolbarReserve);
        }

        private void EndResizeInlineOcrBox(object sender, MouseEventArgs e)
        {
            if (sender == inlineOcrBox && inlineOcrBox != null && inlineOcrBox.SelectionCopyMode)
                return;

            if (!resizingInlineOcrBox)
            {
                if (movingInlineOcrBox)
                    EndMoveInlineOcrBox(sender, e);
                return;
            }

            resizingInlineOcrBox = false;
            resizeLeft = false;
            resizeTop = false;
            resizeRight = false;
            resizeBottom = false;
        }

        private void BeginMoveInlineOcrBox(object sender, MouseEventArgs e)
        {
            var sourceControl = sender as Control;
            if (sourceControl == null || sourceControl == ocrResizeGrip)
                return;

            movingInlineOcrBox = true;
            moveStartPoint = sourceControl.PointToScreen(e.Location);
            moveStartBounds = Bounds;
            Cursor = Cursors.SizeAll;
            inlineOcrBox.Cursor = Cursors.SizeAll;
        }

        private void MoveInlineOcrBox(object sender, MouseEventArgs e)
        {
            if (!movingInlineOcrBox || inlineOcrBox == null)
                return;

            var sourceControl = sender as Control;
            var currentPoint = sourceControl.PointToScreen(e.Location);
            var dx = currentPoint.X - moveStartPoint.X;
            var dy = currentPoint.Y - moveStartPoint.Y;
            var left = Clamp(moveStartBounds.Left + dx, virtualBounds.Left, Math.Max(virtualBounds.Left, virtualBounds.Right - moveStartBounds.Width));
            var top = Clamp(moveStartBounds.Top + dy, virtualBounds.Top, Math.Max(virtualBounds.Top, virtualBounds.Bottom - moveStartBounds.Height));
            Bounds = new Rectangle(left, top, moveStartBounds.Width, moveStartBounds.Height);
        }

        private void EndMoveInlineOcrBox(object sender, MouseEventArgs e)
        {
            movingInlineOcrBox = false;
            Cursor = Cursors.Default;
            if (inlineOcrBox != null)
                inlineOcrBox.Cursor = inlineOcrBox.SelectionCopyMode ? Cursors.IBeam : Cursors.SizeAll;
        }

        private bool GetInlineOcrResizeEdges(object sender, Point location)
        {
            resizeLeft = false;
            resizeTop = false;
            resizeRight = false;
            resizeBottom = false;

            var control = sender as Control;
            if (control == null)
                return false;

            if (control == ocrResizeGrip)
            {
                resizeRight = true;
                resizeBottom = true;
                return true;
            }

            var margin = 8;
            if (control == this)
            {
                var nearHorizontal = location.Y >= selectedBounds.Top - margin && location.Y <= selectedBounds.Bottom + margin;
                var nearVertical = location.X >= selectedBounds.Left - margin && location.X <= selectedBounds.Right + margin;
                resizeLeft = nearHorizontal && Math.Abs(location.X - selectedBounds.Left) <= margin;
                resizeRight = nearHorizontal && Math.Abs(location.X - selectedBounds.Right) <= margin;
                resizeTop = nearVertical && Math.Abs(location.Y - selectedBounds.Top) <= margin;
                resizeBottom = nearVertical && Math.Abs(location.Y - selectedBounds.Bottom) <= margin;
                return resizeLeft || resizeRight || resizeTop || resizeBottom;
            }

            resizeLeft = location.X <= margin;
            resizeRight = location.X >= control.Width - margin;
            resizeTop = location.Y <= margin;
            resizeBottom = location.Y >= control.Height - margin;
            return resizeLeft || resizeRight || resizeTop || resizeBottom;
        }

        private Cursor CursorForResizeEdges(bool active)
        {
            if (!active)
                return Cursors.IBeam;
            if ((resizeLeft && resizeTop) || (resizeRight && resizeBottom))
                return Cursors.SizeNWSE;
            if ((resizeRight && resizeTop) || (resizeLeft && resizeBottom))
                return Cursors.SizeNESW;
            if (resizeLeft || resizeRight)
                return Cursors.SizeWE;
            return Cursors.SizeNS;
        }

        private static string RemoveTextFormatting(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            return Regex.Replace(text, @"\s+", " ").Trim();
        }

        private string RecognizeImages(List<Bitmap> images)
        {
            var parts = new List<string>();
            try
            {
                foreach (var image in images)
                {
                    using (image)
                    {
                        var text = recognizeText(image);
                        if (!string.IsNullOrWhiteSpace(text))
                            parts.Add(text);
                    }
                }
            }
            finally
            {
                foreach (var image in images)
                    image.Dispose();
            }

            return string.Join(Environment.NewLine + Environment.NewLine, parts.ToArray());
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (screenshot != null)
                    screenshot.Dispose();
                if (selectedOriginalImage != null)
                    selectedOriginalImage.Dispose();
                if (editorCanvas != null)
                    editorCanvas.Dispose();
                if (inlineOcrBox != null)
                    inlineOcrBox.Dispose();
                if (ocrResizeGrip != null)
                    ocrResizeGrip.Dispose();
                if (toolTip != null)
                    toolTip.Dispose();
            }
            base.Dispose(disposing);
        }

        private Rectangle CurrentSelection
        {
            get
            {
                var x = Math.Min(startPoint.X, currentPoint.X);
                var y = Math.Min(startPoint.Y, currentPoint.Y);
                var width = Math.Abs(startPoint.X - currentPoint.X);
                var height = Math.Abs(startPoint.Y - currentPoint.Y);
                return new Rectangle(x, y, width, height);
            }
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min)
                return min;
            if (value > max)
                return max;
            return value;
        }

        private void DrawHint(Graphics graphics)
        {
            var text = "拖动鼠标框选截图区域，按 Esc 取消";
            using (var font = new Font("Microsoft YaHei UI", 10))
            {
                var size = graphics.MeasureString(text, font);
                var x = Math.Max(12, (ClientSize.Width - size.Width) / 2);
                var y = 18;
                using (var background = new SolidBrush(Color.FromArgb(170, Color.Black)))
                using (var foreground = new SolidBrush(Color.White))
                {
                    graphics.FillRectangle(background, x - 10, y - 6, size.Width + 20, size.Height + 12);
                    graphics.DrawString(text, font, foreground, x, y);
                }
            }
        }
    }

    internal sealed class PreviewForm : Form
    {
        private readonly Func<Bitmap, string> saveImage;
        private readonly Func<Bitmap, string> recognizeText;
        private readonly ImageCanvasControl canvas;
        private readonly Bitmap originalImage;
        private readonly HotkeySettings settings;
        private readonly bool fitWidthScrollPreview;
        private readonly Panel scrollPanel;
        private double scrollPreviewFitScale = 1.0;
        private double scrollPreviewZoom = 1.0;
        private bool previewDisposing;
        private readonly Button drawButton;
        private readonly Button rectangleButton;
        private readonly Button textButton;
        private readonly Button arrowButton;
        private readonly Label statusLabel;

        public PreviewForm(Bitmap image, Func<Bitmap, string> saveImage, Func<Bitmap, string> recognizeText, HotkeySettings settings)
            : this(image, saveImage, recognizeText, settings, false)
        {
        }

        public PreviewForm(Bitmap image, Func<Bitmap, string> saveImage, Func<Bitmap, string> recognizeText, HotkeySettings settings, bool fitWidthScrollPreview)
        {
            this.saveImage = saveImage;
            this.recognizeText = recognizeText;
            this.settings = settings ?? HotkeySettings.Default();
            this.fitWidthScrollPreview = fitWidthScrollPreview;
            originalImage = (Bitmap)image.Clone();

            Text = "截图预览";
            AutoScaleMode = AutoScaleMode.None;
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = false;
            MinimumSize = new Size(760, 520);
            ClientSize = new Size(1040, 720);

            var toolbar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 54,
                Padding = new Padding(12, 10, 12, 8),
                FlowDirection = FlowDirection.LeftToRight,
                BackColor = Color.FromArgb(245, 247, 250)
            };

            var copyButton = new Button { Text = "复制", Width = 78, Height = 30 };
            var saveButton = new Button { Text = "保存", Width = 78, Height = 30 };
            var ocrButton = new Button { Text = "识别文字", Width = 96, Height = 30 };
            drawButton = new Button { Text = "画图", Width = 78, Height = 30 };
            var rectangleButton = new Button { Text = "框选", Width = 78, Height = 30 };
            this.rectangleButton = rectangleButton;
            textButton = new Button { Text = "文字", Width = 78, Height = 30 };
            arrowButton = new Button { Text = "箭头", Width = 78, Height = 30 };
            var undoButton = new Button { Text = "撤销", Width = 78, Height = 30 };
            var clearButton = new Button { Text = "清空", Width = 78, Height = 30 };
            var closeButton = new Button { Text = "关闭", Width = 78, Height = 30 };

            toolbar.Controls.Add(copyButton);
            toolbar.Controls.Add(saveButton);
            toolbar.Controls.Add(ocrButton);
            toolbar.Controls.Add(drawButton);
            toolbar.Controls.Add(rectangleButton);
            toolbar.Controls.Add(textButton);
            toolbar.Controls.Add(arrowButton);
            toolbar.Controls.Add(undoButton);
            toolbar.Controls.Add(clearButton);
            toolbar.Controls.Add(closeButton);

            canvas = new ImageCanvasControl(image)
            {
                Dock = fitWidthScrollPreview ? DockStyle.None : DockStyle.Fill
            };

            statusLabel = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 26,
                Padding = new Padding(12, 5, 12, 0),
                BackColor = Color.FromArgb(250, 250, 250),
                ForeColor = Color.FromArgb(80, 80, 80),
                Text = "未框选时识别整张截图；框选后识别最后一个框选区域"
            };

            if (fitWidthScrollPreview)
            {
                scrollPanel = new Panel
                {
                    Dock = DockStyle.Fill,
                    AutoScroll = true,
                    BackColor = Color.FromArgb(34, 34, 34)
                };
                scrollPanel.Controls.Add(canvas);
                scrollPanel.Resize += HandleScrollPreviewResize;
                scrollPanel.MouseWheel += HandleScrollPreviewMouseWheel;
                canvas.MouseWheel += HandleScrollPreviewMouseWheel;
                canvas.MouseEnter += delegate { canvas.Focus(); };
                Controls.Add(scrollPanel);
            }
            else
            {
                Controls.Add(canvas);
            }
            Controls.Add(statusLabel);
            Controls.Add(toolbar);
            if (fitWidthScrollPreview)
                LayoutFitWidthCanvas();

            copyButton.Click += delegate
            {
                Clipboard.SetImage((Bitmap)canvas.Image.Clone());
                Text = "截图预览 - 已复制到剪贴板";
            };

            saveButton.Click += delegate
            {
                using (var copy = (Bitmap)canvas.Image.Clone())
                {
                    var path = saveImage(copy);
                    if (!string.IsNullOrEmpty(path))
                        Text = "截图预览 - 已保存：" + path;
                }
            };

            ocrButton.Click += delegate
            {
                var result = new OcrResultForm(RecognizeImages(canvas.GetImagesForOcr(originalImage)), settings);
                result.Show();
                statusLabel.Text = canvas.HasRectangleSelection ? "已识别全部框选区域" : "已识别整张截图";
            };

            drawButton.Click += delegate
            {
                canvas.Mode = canvas.Mode == AnnotationMode.Freehand ? AnnotationMode.None : AnnotationMode.Freehand;
                UpdateToolButtons();
            };

            rectangleButton.Click += delegate
            {
                canvas.Mode = canvas.Mode == AnnotationMode.Rectangle ? AnnotationMode.None : AnnotationMode.Rectangle;
                UpdateToolButtons();
            };

            textButton.Click += delegate
            {
                canvas.Mode = canvas.Mode == AnnotationMode.Text ? AnnotationMode.None : AnnotationMode.Text;
                UpdateToolButtons();
            };

            arrowButton.Click += delegate
            {
                canvas.Mode = canvas.Mode == AnnotationMode.Arrow ? AnnotationMode.None : AnnotationMode.Arrow;
                UpdateToolButtons();
            };

            undoButton.Click += delegate
            {
                canvas.Undo();
                UpdateStatus();
            };
            clearButton.Click += delegate
            {
                canvas.Restore(originalImage);
                UpdateStatus();
            };
            closeButton.Click += delegate { Close(); };
        }

        private void LayoutFitWidthCanvas()
        {
            if (previewDisposing || !fitWidthScrollPreview || scrollPanel == null || canvas == null || canvas.IsDisposed || !canvas.ImageIsUsable)
                return;

            var availableWidth = Math.Max(120, scrollPanel.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 4);
            scrollPreviewFitScale = Math.Min(1.0, availableWidth / (double)canvas.Image.Width);
            ApplyScrollPreviewZoom();
        }

        private void HandleScrollPreviewResize(object sender, EventArgs e)
        {
            LayoutFitWidthCanvas();
        }

        private double PreviewZoom
        {
            get { return scrollPreviewZoom; }
        }

        private void HandleScrollPreviewMouseWheel(object sender, MouseEventArgs e)
        {
            if (!fitWidthScrollPreview || (ModifierKeys & Keys.Control) != Keys.Control)
                return;

            scrollPreviewZoom = ClampZoom(scrollPreviewZoom * (e.Delta > 0 ? 1.15 : 1.0 / 1.15));
            ApplyScrollPreviewZoom();
            var handled = e as HandledMouseEventArgs;
            if (handled != null)
                handled.Handled = true;
        }

        private void ApplyScrollPreviewZoom()
        {
            if (previewDisposing || !fitWidthScrollPreview || scrollPanel == null || canvas == null || canvas.IsDisposed || !canvas.ImageIsUsable)
                return;

            var scale = Math.Max(0.05, scrollPreviewFitScale * scrollPreviewZoom);
            var displayWidth = Math.Max(1, (int)Math.Round(canvas.Image.Width * scale));
            var displayHeight = Math.Max(1, (int)Math.Round(canvas.Image.Height * scale));
            canvas.Location = new Point(0, 0);
            canvas.Size = new Size(displayWidth, displayHeight);
            canvas.Invalidate();
        }

        private static double ClampZoom(double value)
        {
            if (value < 0.25)
                return 0.25;
            if (value > 5.0)
                return 5.0;
            return value;
        }

        private void UpdateToolButtons()
        {
            drawButton.Text = canvas.Mode == AnnotationMode.Freehand ? "停止画图" : "画图";
            rectangleButton.Text = canvas.Mode == AnnotationMode.Rectangle ? "停止框选" : "框选";
            textButton.Text = canvas.Mode == AnnotationMode.Text ? "停止文字" : "文字";
            arrowButton.Text = canvas.Mode == AnnotationMode.Arrow ? "停止箭头" : "箭头";
            canvas.Cursor = canvas.Mode == AnnotationMode.None ? Cursors.Default : Cursors.Cross;
            UpdateStatus();
        }

        private void UpdateStatus()
        {
            if (canvas.Mode == AnnotationMode.Freehand)
                statusLabel.Text = "画图模式";
            else if (canvas.Mode == AnnotationMode.Rectangle)
                statusLabel.Text = "框选模式：可画多个区域，识别文字时会依次识别全部框选";
            else if (canvas.Mode == AnnotationMode.Text)
                statusLabel.Text = "文字模式：拖动框选文字区域，松手后输入文字";
            else if (canvas.Mode == AnnotationMode.Arrow)
                statusLabel.Text = "箭头模式：拖动设置箭头方向，松手后画出箭头";
            else
                statusLabel.Text = canvas.HasRectangleSelection ? "已有框选：识别文字时会依次识别全部框选" : "未框选时识别整张截图；框选后识别全部框选区域";
        }

        private string RecognizeImages(List<Bitmap> images)
        {
            var parts = new List<string>();
            try
            {
                foreach (var image in images)
                {
                    using (image)
                    {
                        var text = recognizeText(image);
                        if (!string.IsNullOrWhiteSpace(text))
                            parts.Add(text);
                    }
                }
            }
            finally
            {
                foreach (var image in images)
                    image.Dispose();
            }

            return string.Join(Environment.NewLine + Environment.NewLine, parts.ToArray());
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
                Close();
            base.OnKeyDown(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                previewDisposing = true;
                if (scrollPanel != null)
                {
                    scrollPanel.Resize -= HandleScrollPreviewResize;
                    scrollPanel.MouseWheel -= HandleScrollPreviewMouseWheel;
                }
                if (canvas != null)
                    canvas.MouseWheel -= HandleScrollPreviewMouseWheel;
                originalImage.Dispose();
                canvas.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    internal sealed class TextAnnotationForm : Form
    {
        private readonly TextBox textBox;

        public TextAnnotationForm()
        {
            Text = "添加文字";
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(360, 136);

            var label = new Label { Text = "输入要添加到截图上的文字", Left = 14, Top = 14, Width = 320 };
            textBox = new TextBox { Left = 16, Top = 42, Width = 328 };
            var okButton = new Button { Text = "确定", Left = 186, Top = 92, Width = 76, DialogResult = DialogResult.OK };
            var cancelButton = new Button { Text = "取消", Left = 268, Top = 92, Width = 76, DialogResult = DialogResult.Cancel };

            Controls.Add(label);
            Controls.Add(textBox);
            Controls.Add(okButton);
            Controls.Add(cancelButton);

            AcceptButton = okButton;
            CancelButton = cancelButton;
        }

        public string AnnotationText
        {
            get { return textBox.Text.Trim(); }
        }
    }

    internal sealed class OcrResultForm : Form
    {
        private readonly TextBox resultBox;
        private readonly Label statusLabel;
        private readonly string formattedText;
        private readonly HotkeySettings settings;
        private readonly ComboBox translationProviderBox;
        private readonly Button translateToEnglishButton;
        private readonly Button translateToChineseButton;
        private readonly Button translateToGermanButton;
        private readonly Button translateToFrenchButton;
        private readonly Button translateToSpanishButton;
        private readonly Button translateToItalianButton;
        private readonly Button translateToRussianButton;
        private readonly Button translateToDutchButton;
        private const string TranslateToEnglishText = "转英文";
        private const string TranslateToChineseText = "转中文";
        private const string RestoreOriginalTextLabel = "复原原文";
        private const string TranslateToGermanText = "转德文";
        private const string TranslateToFrenchText = "转法语";
        private const string TranslateToSpanishText = "转西语";
        private const string TranslateToItalianText = "转意语";
        private const string TranslateToRussianText = "转俄语";
        private const string TranslateToDutchText = "转荷语";
        private string textBeforeTranslation;
        private bool formatRemoved;
        private bool formatRemovedBeforeTranslation;
        private bool showingTranslation;

        public OcrResultForm(string text, HotkeySettings settings)
        {
            formattedText = text ?? string.Empty;
            this.settings = settings ?? HotkeySettings.Default();
            Text = "文字识别结果";
            AutoScaleMode = AutoScaleMode.None;
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = false;
            MinimumSize = new Size(620, 420);
            ClientSize = new Size(760, 520);

            var header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 44,
                BackColor = Color.FromArgb(245, 247, 250),
                Padding = new Padding(12, 10, 12, 0)
            };

            statusLabel = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(65, 65, 65),
                Text = string.IsNullOrWhiteSpace(text) ? "未识别到文字" : "已识别 " + text.Trim().Length + " 个字符"
            };

            var toolbar = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 54,
                Padding = new Padding(12, 10, 12, 8),
                FlowDirection = FlowDirection.RightToLeft,
                BackColor = Color.FromArgb(245, 247, 250)
            };

            var closeButton = new Button { Text = "关闭", Width = 78, Height = 30 };
            var saveButton = new Button { Text = "保存", Width = 78, Height = 30 };
            var copyButton = new Button { Text = "复制", Width = 78, Height = 30 };
            var formatButton = new Button { Text = "去格式", Width = 86, Height = 30 };
            translateToEnglishButton = new Button { Text = TranslateToEnglishText, Width = 78, Height = 30 };
            translateToChineseButton = new Button { Text = TranslateToChineseText, Width = 78, Height = 30 };
            translateToGermanButton = new Button { Text = TranslateToGermanText, Width = 78, Height = 30 };
            translateToFrenchButton = new Button { Text = TranslateToFrenchText, Width = 78, Height = 30 };
            translateToSpanishButton = new Button { Text = TranslateToSpanishText, Width = 78, Height = 30 };
            translateToItalianButton = new Button { Text = TranslateToItalianText, Width = 78, Height = 30 };
            translateToRussianButton = new Button { Text = TranslateToRussianText, Width = 78, Height = 30 };
            translateToDutchButton = new Button { Text = TranslateToDutchText, Width = 78, Height = 30 };
            translationProviderBox = new ComboBox { Width = 90, Height = 30, DropDownStyle = ComboBoxStyle.DropDownList };
            translationProviderBox.Items.Add("Google");
            translationProviderBox.Items.Add("Baidu");
            translationProviderBox.SelectedItem = string.IsNullOrWhiteSpace(this.settings.TranslationProvider) ? "Google" : this.settings.TranslationProvider;
            if (translationProviderBox.SelectedIndex < 0)
                translationProviderBox.SelectedItem = "Google";

            var translationProviderLabel = new Label
            {
                Text = "翻译源",
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleCenter,
                Padding = new Padding(0, 7, 0, 0),
                Margin = new Padding(8, 3, 0, 3)
            };

            resultBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                Font = new Font("Consolas", 11),
                BorderStyle = BorderStyle.FixedSingle,
                Text = formattedText
            };

            header.Controls.Add(statusLabel);
            toolbar.Controls.Add(closeButton);
            toolbar.Controls.Add(saveButton);
            toolbar.Controls.Add(copyButton);
            toolbar.Controls.Add(formatButton);
            toolbar.Controls.Add(translateToEnglishButton);
            toolbar.Controls.Add(translateToChineseButton);
            toolbar.Controls.Add(translateToGermanButton);
            toolbar.Controls.Add(translateToFrenchButton);
            toolbar.Controls.Add(translateToSpanishButton);
            toolbar.Controls.Add(translateToItalianButton);
            toolbar.Controls.Add(translateToRussianButton);
            toolbar.Controls.Add(translateToDutchButton);
            toolbar.Controls.Add(translationProviderBox);
            toolbar.Controls.Add(translationProviderLabel);
            Controls.Add(resultBox);
            Controls.Add(toolbar);
            Controls.Add(header);

            copyButton.Click += delegate
            {
                if (!string.IsNullOrEmpty(resultBox.Text))
                    Clipboard.SetText(resultBox.Text);
                statusLabel.Text = "已复制到剪贴板";
            };

            formatButton.Click += delegate
            {
                if (formatRemoved)
                {
                    resultBox.Text = formattedText;
                    formatRemoved = false;
                    ClearTranslationState();
                    formatButton.Text = "去格式";
                    statusLabel.Text = "已复原格式";
                }
                else
                {
                    resultBox.Text = RemoveTextFormatting(formattedText);
                    formatRemoved = true;
                    ClearTranslationState();
                    formatButton.Text = "复原格式";
                    statusLabel.Text = "已去除格式";
                }
            };

            translateToEnglishButton.Click += delegate { TranslateCurrentText("en", translateToEnglishButton, translateToChineseButton); };
            translateToChineseButton.Click += delegate { TranslateCurrentText("zh-CN", translateToChineseButton, translateToEnglishButton); };
            translateToGermanButton.Click += delegate { TranslateCurrentText("de", translateToGermanButton, translateToChineseButton, translateToEnglishButton); };
            translateToFrenchButton.Click += delegate { TranslateCurrentText("fr", translateToFrenchButton, translateToChineseButton, translateToEnglishButton, translateToGermanButton); };
            translateToSpanishButton.Click += delegate { TranslateCurrentText("es", translateToSpanishButton, translateToChineseButton, translateToEnglishButton, translateToGermanButton); };
            translateToItalianButton.Click += delegate { TranslateCurrentText("it", translateToItalianButton, translateToChineseButton, translateToEnglishButton, translateToGermanButton); };
            translateToRussianButton.Click += delegate { TranslateCurrentText("ru", translateToRussianButton, translateToChineseButton, translateToEnglishButton, translateToGermanButton); };
            translateToDutchButton.Click += delegate { TranslateCurrentText("nl", translateToDutchButton, translateToChineseButton, translateToEnglishButton, translateToGermanButton); };
            translationProviderBox.SelectedIndexChanged += delegate
            {
                if (translationProviderBox.SelectedItem == null)
                    return;

                settings.TranslationProvider = Convert.ToString(translationProviderBox.SelectedItem);
                settings.Save();
                statusLabel.Text = "翻译源已切换为 " + settings.TranslationProvider;
            };

            saveButton.Click += delegate
            {
                using (var dialog = new SaveFileDialog())
                {
                    dialog.Title = "保存文字";
                    dialog.FileName = "识别文字_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt";
                    dialog.Filter = "文本文件 (*.txt)|*.txt";
                    dialog.DefaultExt = "txt";
                    dialog.AddExtension = true;
                    if (dialog.ShowDialog() == DialogResult.OK)
                    {
                        File.WriteAllText(dialog.FileName, resultBox.Text);
                        statusLabel.Text = "已保存：" + dialog.FileName;
                    }
                }
            };

            closeButton.Click += delegate { Close(); };
        }

        private void TranslateCurrentText(string targetLanguage, Button primaryButton, params Button[] secondaryButtons)
        {
            if (showingTranslation)
            {
                RestoreOriginalText();
                return;
            }

            var sourceText = resultBox.Text;
            if (string.IsNullOrWhiteSpace(sourceText))
            {
                statusLabel.Text = "没有可翻译的文字";
                return;
            }

            textBeforeTranslation = sourceText;
            formatRemovedBeforeTranslation = formatRemoved;
            SetResultTranslationButtonsEnabled(false, primaryButton, secondaryButtons);
            statusLabel.Text = "正在翻译为" + TranslationLanguageName(targetLanguage) + "...";

            ThreadPool.QueueUserWorkItem(delegate
            {
                string translatedText = null;
                Exception error = null;
                try
                {
                    translatedText = TranslationRunner.TranslatePreservingLines(sourceText, targetLanguage, settings);
                }
                catch (Exception ex)
                {
                    error = ex;
                }

                BeginInvoke((MethodInvoker)delegate
                {
                    SetResultTranslationButtonsEnabled(true, primaryButton, secondaryButtons);
                    if (error != null)
                    {
                        ClearTranslationState();
                        statusLabel.Text = "翻译失败：" + error.Message;
                        return;
                    }

                    resultBox.Text = translatedText;
                    formatRemoved = false;
                    showingTranslation = true;
                    SetTranslationButtonLabels(primaryButton);
                    statusLabel.Text = "已翻译为" + TranslationLanguageName(targetLanguage);
                });
            });
        }

        private void RestoreOriginalText()
        {
            if (textBeforeTranslation == null)
                return;

            resultBox.Text = textBeforeTranslation;
            formatRemoved = formatRemovedBeforeTranslation;
            ClearTranslationState();
            statusLabel.Text = "已复原原文";
        }

        private void ClearTranslationState()
        {
            showingTranslation = false;
            textBeforeTranslation = null;
            SetTranslationButtonLabels(null);
        }

        private void SetTranslationButtonLabels(Button restoreButton)
        {
            translateToEnglishButton.Text = TranslateToEnglishText;
            translateToChineseButton.Text = TranslateToChineseText;
            translateToGermanButton.Text = TranslateToGermanText;
            translateToFrenchButton.Text = TranslateToFrenchText;
            translateToSpanishButton.Text = TranslateToSpanishText;
            translateToItalianButton.Text = TranslateToItalianText;
            translateToRussianButton.Text = TranslateToRussianText;
            translateToDutchButton.Text = TranslateToDutchText;
            if (restoreButton != null)
                restoreButton.Text = RestoreOriginalTextLabel;
        }

        private void SetResultTranslationButtonsEnabled(bool enabled, Button primaryButton, Button[] secondaryButtons)
        {
            if (primaryButton != null)
                primaryButton.Enabled = enabled;
            if (secondaryButtons == null)
                return;

            foreach (var button in secondaryButtons)
            {
                if (button != null)
                    button.Enabled = enabled;
            }
        }

        private static string TranslationLanguageName(string targetLanguage)
        {
            if (targetLanguage == "en")
                return "英文";
            if (targetLanguage == "de")
                return "德文";
            if (targetLanguage == "fr")
                return "法语";
            if (targetLanguage == "es")
                return "西班牙语";
            if (targetLanguage == "it")
                return "意大利语";
            if (targetLanguage == "ru")
                return "俄语";
            if (targetLanguage == "nl")
                return "荷兰语";
            return "中文";
        }

        private static string RemoveTextFormatting(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            return Regex.Replace(text, @"\s+", " ").Trim();
        }
    }

    internal static class ScrollingCaptureRunner
    {
        private const int MaxCaptures = 24;
        private const int SeamTrimPixels = 3;
        private const int MinimumAppendedFrameHeight = 36;
        private const int MinimumContinuedScrollAppendHeight = 180;
        public static int LastCaptureCount { get; private set; }
        public static string LastStitchSummary { get; private set; }
        public static string DebugFrameDirectory { get; set; }

        public static Rectangle ExpandScrollingRegionToWindowBottom(Rectangle region, Rectangle virtualBounds)
        {
            if (region.Width < 1 || region.Height < 1)
                return region;

            var hwnd = WindowFromPoint(new NativePoint
            {
                X = region.Left + region.Width / 2,
                Y = region.Top + region.Height / 2
            });
            if (hwnd == IntPtr.Zero)
                return region;

            var root = GetAncestor(hwnd, GA_ROOT);
            if (root != IntPtr.Zero)
                hwnd = root;

            NativeRect windowRect;
            if (!GetWindowRect(hwnd, out windowRect))
                return region;

            var windowBottom = Clamp(windowRect.Bottom - 8, region.Bottom, virtualBounds.Bottom);
            if (windowBottom <= region.Bottom)
                return region;

            return new Rectangle(region.Left, region.Top, region.Width, windowBottom - region.Top);
        }

        public static Bitmap Capture(Rectangle region)
        {
            if (region.Width < 10 || region.Height < 10)
                throw new InvalidOperationException("滚动截屏区域太小。");

            ActivateWindowAtRegion(region);
            WaitForMouseButtonsReleased();
            Thread.Sleep(500);
            var captures = new List<Bitmap>();
            try
            {
                LastCaptureCount = 0;
                Bitmap previous = null;
                for (var i = 0; i < MaxCaptures; i++)
                {
                    if (i > 0 && UserRequestedStop())
                        break;

                    var current = CaptureRegion(region);
                    if (previous != null && ImagesAreNearlySame(previous, current))
                    {
                        current.Dispose();
                        RetryScrollRegion(region);
                        if (!WaitForScrollSettleOrUserStop())
                            break;
                        current = CaptureRegion(region);
                        if (ImagesAreNearlySame(previous, current))
                        {
                            current.Dispose();
                            break;
                        }
                    }

                    captures.Add(current);
                    SaveDebugFrame(current, captures.Count);
                    previous = current;
                    LastCaptureCount = captures.Count;
                    if (i == MaxCaptures - 1)
                        break;

                    ScrollRegion(region);
                    if (!WaitForScrollSettleOrUserStop())
                        break;
                }

                if (captures.Count == 0)
                    throw new InvalidOperationException("没有捕获到可用画面。");
                if (captures.Count == 1)
                    throw new InvalidOperationException("页面没有发生滚动，只捕获到当前框选区域。请先点一下要滚动的网页/文档内容，再用滚动截屏。");

                var scrollingContentBounds = DetectScrollingContentBounds(captures);
                return Stitch(captures, scrollingContentBounds);
            }
            finally
            {
                foreach (var capture in captures)
                    capture.Dispose();
            }
        }

        private static Bitmap CaptureRegion(Rectangle region)
        {
            var bitmap = new Bitmap(region.Width, region.Height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(region.Left, region.Top, 0, 0, region.Size, CopyPixelOperation.SourceCopy);
            }
            return bitmap;
        }

        private static void SaveDebugFrame(Bitmap frame, int index)
        {
            if (string.IsNullOrWhiteSpace(DebugFrameDirectory))
                return;

            Directory.CreateDirectory(DebugFrameDirectory);
            frame.Save(Path.Combine(DebugFrameDirectory, "frame-" + index.ToString("00") + ".png"), ImageFormat.Png);
        }

        private static void ScrollRegion(Rectangle region)
        {
            SetCursorPos(region.Left + region.Width / 2, region.Top + region.Height / 2);
            for (var i = 0; i < 2; i++)
            {
                SendMouseWheel(-120);
                Thread.Sleep(30);
            }
        }

        private static void RetryScrollRegion(Rectangle region)
        {
            SetCursorPos(region.Left + region.Width / 2, region.Top + region.Height / 2);
            for (var i = 0; i < 8; i++)
            {
                SendMouseWheel(-120);
                Thread.Sleep(25);
            }
        }

        private static bool WaitForScrollSettleOrUserStop()
        {
            var waited = 0;
            while (waited < 900)
            {
                Thread.Sleep(50);
                waited += 50;
                if (UserRequestedStop())
                    return false;
            }
            return true;
        }

        private static void WaitForMouseButtonsReleased()
        {
            var waited = 0;
            while (waited < 2000 && IsAnyMouseButtonDown())
            {
                Thread.Sleep(40);
                waited += 40;
            }
        }

        private static bool UserRequestedStop()
        {
            return IsAnyMouseButtonDown();
        }

        private static bool IsAnyMouseButtonDown()
        {
            return IsVirtualKeyDown(VK_LBUTTON) || IsVirtualKeyDown(VK_RBUTTON) || IsVirtualKeyDown(VK_MBUTTON);
        }

        private static bool IsVirtualKeyDown(int virtualKey)
        {
            return (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
        }

        private static void ActivateWindowAtRegion(Rectangle region)
        {
            var point = new Point(region.Left + region.Width / 2, region.Top + region.Height / 2);
            SetCursorPos(point.X, point.Y);
            var hwnd = WindowFromPoint(new NativePoint { X = point.X, Y = point.Y });
            if (hwnd != IntPtr.Zero)
                SetForegroundWindow(hwnd);
        }

        private static void SendMouseWheel(int delta)
        {
            var input = new INPUT();
            input.type = INPUT_MOUSE;
            input.mi.dwFlags = MOUSEEVENTF_WHEEL;
            input.mi.mouseData = delta;
            SendInput(1, new[] { input }, Marshal.SizeOf(typeof(INPUT)));
        }

        private static Bitmap Stitch(List<Bitmap> captures, Rectangle scrollingContentBounds)
        {
            var width = captures[0].Width;
            var height = captures[0].Height;
            LastStitchSummary = string.Empty;
            scrollingContentBounds = NormalizeScrollingContentBounds(scrollingContentBounds, width, height);
            var parts = new List<StitchFrame>();
            parts.Add(new StitchFrame(0, 0, height));
            var outputHeight = height;

            for (var i = 1; i < captures.Count; i++)
            {
                var match = FindBestVerticalOverlap(captures[i - 1], captures[i], scrollingContentBounds);
                var sourceTop = Clamp(match.CurrentTop + match.Overlap + SeamTrimPixels, 0, height - 1);
                var sourceHeight = Math.Max(1, height - sourceTop);
                var minimumSourceHeight = i < captures.Count - 1
                    ? Math.Min(height / 2, Math.Max(height / 3, MinimumContinuedScrollAppendHeight))
                    : MinimumAppendedFrameHeight;
                if (sourceHeight < minimumSourceHeight)
                {
                    sourceTop = Clamp(height - minimumSourceHeight, 0, height - 1);
                    sourceHeight = height - sourceTop;
                }
                parts.Add(new StitchFrame(i, sourceTop, sourceHeight));
                outputHeight += sourceHeight;
            }

            LastStitchSummary = BuildStitchSummary(parts, scrollingContentBounds, outputHeight);

            var output = new Bitmap(width, outputHeight, PixelFormat.Format32bppArgb);

            using (var graphics = Graphics.FromImage(output))
            {
                graphics.Clear(Color.White);
                var targetY = 0;
                foreach (var part in parts)
                {
                    var seamY = targetY;
                    if (part.CaptureIndex == 0)
                        PreserveFixedAreasInFirstFrame(graphics, captures[part.CaptureIndex], part, width, targetY);
                    else
                        DrawMovingContentOnlyForAppendedFrames(graphics, captures[part.CaptureIndex], part, scrollingContentBounds, targetY);
                    targetY += part.SourceHeight;
                    if (seamY > 0)
                        BlendStitchSeam(output, seamY, width);
                }
            }

            return output;
        }

        private static void PreserveFixedAreasInFirstFrame(Graphics graphics, Bitmap source, StitchFrame part, int width, int targetY)
        {
            using (var slice = source.Clone(
                new Rectangle(0, part.SourceTop, width, part.SourceHeight),
                PixelFormat.Format32bppArgb))
            {
                graphics.DrawImageUnscaled(slice, 0, targetY);
            }
        }

        private static void DrawMovingContentOnlyForAppendedFrames(Graphics graphics, Bitmap source, StitchFrame part, Rectangle scrollingContentBounds, int targetY)
        {
            var sourceRectangle = new Rectangle(
                scrollingContentBounds.Left,
                part.SourceTop,
                scrollingContentBounds.Width,
                part.SourceHeight);
            using (var slice = source.Clone(sourceRectangle, PixelFormat.Format32bppArgb))
            {
                graphics.DrawImageUnscaled(slice, scrollingContentBounds.Left, targetY);
            }
        }

        private static Rectangle NormalizeScrollingContentBounds(Rectangle bounds, int width, int height)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0)
                return new Rectangle(0, 0, width, height);

            var left = Clamp(bounds.Left, 0, width - 1);
            var top = Clamp(bounds.Top, 0, height - 1);
            var right = Clamp(bounds.Right, left + 1, width);
            var bottom = Clamp(bounds.Bottom, top + 1, height);
            return new Rectangle(left, top, right - left, bottom - top);
        }

        private static string BuildStitchSummary(List<StitchFrame> parts, Rectangle scrollingContentBounds, int outputHeight)
        {
            var heights = new List<string>();
            foreach (var part in parts)
                heights.Add(part.CaptureIndex + ":" + part.SourceHeight);
            return "content=" + scrollingContentBounds + " outputHeight=" + outputHeight + " parts=[" + string.Join(",", heights.ToArray()) + "]";
        }

        private static void BlendStitchSeam(Bitmap image, int seamY, int width)
        {
            const int band = 3;
            var topReference = seamY - band - 1;
            var bottomReference = seamY + band + 1;
            if (topReference < 0 || bottomReference >= image.Height)
                return;

            for (var y = seamY - band; y <= seamY + band; y++)
            {
                if (y < 0 || y >= image.Height)
                    continue;

                var weight = (y - (seamY - band) + 1) / (double)(band * 2 + 2);
                for (var x = 0; x < width && x < image.Width; x++)
                {
                    var top = image.GetPixel(x, topReference);
                    var bottom = image.GetPixel(x, bottomReference);
                    image.SetPixel(x, y, Color.FromArgb(
                        BlendChannel(top.R, bottom.R, weight),
                        BlendChannel(top.G, bottom.G, weight),
                        BlendChannel(top.B, bottom.B, weight)));
                }
            }
        }

        private static int BlendChannel(int first, int second, double weight)
        {
            return Clamp((int)Math.Round(first * (1.0 - weight) + second * weight), 0, 255);
        }

        private static Rectangle DetectScrollingContentBounds(List<Bitmap> captures)
        {
            if (captures == null || captures.Count < 2)
                return Rectangle.Empty;

            var previous = captures[0];
            var current = captures[1];
            var width = Math.Min(previous.Width, current.Width);
            var height = Math.Min(previous.Height, current.Height);
            if (width < 80 || height < 80)
                return new Rectangle(0, 0, width, height);

            const int blockWidth = 10;
            var scores = new List<int>();
            var maxScore = 0;
            for (var x = 0; x < width; x += blockWidth)
            {
                var score = ColumnMotionScore(previous, current, x, Math.Min(width, x + blockWidth));
                scores.Add(score);
                maxScore = Math.Max(maxScore, score);
            }

            if (maxScore < 8)
                return new Rectangle(0, 0, width, height);

            var threshold = Math.Max(8, maxScore / 5);
            var leftBlock = -1;
            var rightBlock = -1;
            for (var i = 0; i < scores.Count; i++)
            {
                if (scores[i] < threshold)
                    continue;

                if (leftBlock < 0)
                    leftBlock = i;
                rightBlock = i;
            }

            if (leftBlock < 0 || rightBlock < leftBlock)
                return new Rectangle(0, 0, width, height);

            var left = Math.Max(0, leftBlock * blockWidth - blockWidth);
            var right = Math.Min(width, (rightBlock + 2) * blockWidth);
            if (left < 40 && width - right < 40)
                return new Rectangle(0, 0, width, height);
            if (right - left < Math.Max(80, width / 4))
                return new Rectangle(0, 0, width, height);

            return new Rectangle(left, 0, right - left, height);
        }

        private static int ColumnMotionScore(Bitmap previous, Bitmap current, int left, int right)
        {
            long total = 0;
            var samples = 0;
            var width = Math.Min(previous.Width, current.Width);
            var height = Math.Min(previous.Height, current.Height);
            var stepX = Math.Max(1, (right - left) / 2);
            var stepY = Math.Max(1, height / 24);

            for (var x = left; x < right && x < width; x += stepX)
            {
                for (var y = stepY; y < height; y += stepY)
                {
                    var a = previous.GetPixel(x, y);
                    var b = current.GetPixel(x, y);
                    total += Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);
                    samples++;
                }
            }

            return samples == 0 ? 0 : (int)(total / samples);
        }

        private static OverlapMatch FindBestVerticalOverlap(Bitmap previous, Bitmap current, Rectangle contentBounds)
        {
            var height = Math.Min(previous.Height, current.Height);
            var minOverlap = Math.Min(height - 1, Math.Max(80, height / 5));
            var maxOverlap = Math.Max(minOverlap, height - 2);
            var maxCurrentTop = Math.Min(180, Math.Max(0, height / 4));
            var best = new OverlapMatch(0, minOverlap);
            var bestScore = long.MaxValue;

            for (var currentTop = 0; currentTop <= maxCurrentTop; currentTop += 8)
            {
                var candidateMaxOverlap = Math.Min(maxOverlap, height - currentTop - 1);
                for (var overlap = minOverlap; overlap <= candidateMaxOverlap; overlap += 8)
                {
                    var score = OverlapScore(previous, current, contentBounds, currentTop, overlap)
                        + currentTop * 2;
                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = new OverlapMatch(currentTop, overlap);
                    }
                }
            }

            return best;
        }

        private static long OverlapScore(Bitmap previous, Bitmap current, Rectangle contentBounds, int currentTop, int overlap)
        {
            long total = 0;
            var samples = 0;
            var width = Math.Min(previous.Width, current.Width);
            var left = Clamp(contentBounds.Left, 0, width - 1);
            var right = Clamp(contentBounds.Right, left + 1, width);
            var stepX = Math.Max(1, (right - left) / 14);
            var stepY = Math.Max(1, overlap / 10);
            var startPrevY = previous.Height - overlap;

            for (var y = 0; y < overlap; y += stepY)
            {
                var previousY = startPrevY + y;
                var currentY = currentTop + y;
                for (var x = left + stepX / 2; x < right; x += stepX)
                {
                    var a = previous.GetPixel(x, previousY);
                    var b = current.GetPixel(x, currentY);
                    total += Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);
                    samples++;
                }
            }

            return samples == 0 ? long.MaxValue : total / samples;
        }

        private struct StitchFrame
        {
            public readonly int CaptureIndex;
            public readonly int SourceTop;
            public readonly int SourceHeight;

            public StitchFrame(int captureIndex, int sourceTop, int sourceHeight)
            {
                CaptureIndex = captureIndex;
                SourceTop = sourceTop;
                SourceHeight = sourceHeight;
            }
        }

        private struct OverlapMatch
        {
            public readonly int CurrentTop;
            public readonly int Overlap;

            public OverlapMatch(int currentTop, int overlap)
            {
                CurrentTop = currentTop;
                Overlap = overlap;
            }
        }

        private static bool ImagesAreNearlySame(Bitmap first, Bitmap second)
        {
            if (first.Width != second.Width || first.Height != second.Height)
                return false;

            long totalDiff = 0;
            var samples = 0;
            var changedSamples = 0;
            var stepX = Math.Max(1, first.Width / 16);
            var stepY = Math.Max(1, first.Height / 16);
            for (var y = stepY; y < first.Height; y += stepY)
            {
                for (var x = stepX; x < first.Width; x += stepX)
                {
                    var a = first.GetPixel(x, y);
                    var b = second.GetPixel(x, y);
                    var diff = Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);
                    totalDiff += diff;
                    if (diff > 12)
                        changedSamples++;
                    samples++;
                }
            }

            return samples > 0
                && totalDiff / samples < 2
                && changedSamples <= Math.Max(2, samples / 200);
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min)
                return min;
            if (value > max)
                return max;
            return value;
        }

        private const int INPUT_MOUSE = 0;
        private const int VK_LBUTTON = 0x01;
        private const int VK_RBUTTON = 0x02;
        private const int VK_MBUTTON = 0x04;
        private const uint GA_ROOT = 2;
        private const uint MOUSEEVENTF_WHEEL = 0x0800;

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(NativePoint point);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect lpRect);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public int type;
            public MOUSEINPUT mi;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public int mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }
    }

    internal static class TranslationRunner
    {
        private static readonly Dictionary<string, string> translationCache = new Dictionary<string, string>();
        private static readonly object cacheLock = new object();

        public static string TranslatePreservingLines(string text, string targetLanguage, HotkeySettings settings)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
            var provider = settings == null || string.IsNullOrWhiteSpace(settings.TranslationProvider) ? "Google" : settings.TranslationProvider;
            var cacheKey = provider + "|" + targetLanguage + "|" + normalized;
            lock (cacheLock)
            {
                if (translationCache.ContainsKey(cacheKey))
                    return translationCache[cacheKey];
            }

            var translated = string.Equals(provider, "Baidu", StringComparison.OrdinalIgnoreCase)
                ? BaiduTranslate(normalized, targetLanguage, settings)
                : GoogleTranslate(normalized, targetLanguage);
            translated = translated.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", Environment.NewLine);
            lock (cacheLock)
            {
                translationCache[cacheKey] = translated;
            }
            return translated;
        }

        private static string GoogleTranslate(string text, string targetLanguage)
        {
            try
            {
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
            }
            catch
            {
            }

            var errors = new List<string>();
            foreach (var url in BuildTranslateUrls(text, targetLanguage))
            {
                try
                {
                    using (var client = new TimeoutWebClient())
                    {
                        client.Encoding = Encoding.UTF8;
                        client.Headers.Add("User-Agent", "Mozilla/5.0 ScreenshotHotkeyTool");
                        var json = client.DownloadString(url);
                        var translated = ParseGoogleTranslateResult(json);
                        if (!string.IsNullOrWhiteSpace(translated))
                            return translated;
                    }
                }
                catch (Exception ex)
                {
                    errors.Add(ex.Message);
                }
            }

            throw new InvalidOperationException("无法连接到可用的翻译服务：" + string.Join("；", errors.ToArray()));
        }

        private static string BaiduTranslate(string text, string targetLanguage, HotkeySettings settings)
        {
            if (settings == null || string.IsNullOrWhiteSpace(settings.BaiduAppId) || string.IsNullOrWhiteSpace(settings.BaiduSecretKey))
                throw new InvalidOperationException("请先在设置里填写百度翻译 App ID 和密钥。");

            var to = BaiduTargetLanguage(targetLanguage);
            var salt = DateTime.UtcNow.Ticks.ToString();
            var sign = Md5(settings.BaiduAppId + text + salt + settings.BaiduSecretKey);
            var body = "q=" + Uri.EscapeDataString(text)
                + "&from=auto"
                + "&to=" + Uri.EscapeDataString(to)
                + "&appid=" + Uri.EscapeDataString(settings.BaiduAppId)
                + "&salt=" + Uri.EscapeDataString(salt)
                + "&sign=" + Uri.EscapeDataString(sign);

            using (var client = new TimeoutWebClient())
            {
                client.Encoding = Encoding.UTF8;
                client.Headers[HttpRequestHeader.ContentType] = "application/x-www-form-urlencoded";
                var json = client.UploadString("https://fanyi-api.baidu.com/api/trans/vip/translate", body);
                return ParseBaiduTranslateResult(json);
            }
        }

        private static string BaiduTargetLanguage(string targetLanguage)
        {
            if (targetLanguage == "en")
                return "en";
            if (targetLanguage == "de")
                return "de";
            if (targetLanguage == "fr")
                return "fra";
            if (targetLanguage == "es")
                return "spa";
            if (targetLanguage == "it")
                return "it";
            if (targetLanguage == "ru")
                return "ru";
            if (targetLanguage == "nl")
                return "nl";
            return "zh";
        }

        private static IEnumerable<string> BuildTranslateUrls(string text, string targetLanguage)
        {
            var target = Uri.EscapeDataString(targetLanguage);
            var query = Uri.EscapeDataString(text);
            yield return "https://translate.googleapis.com/translate_a/single?client=gtx&sl=auto&tl=" + target + "&dt=t&q=" + query;
            yield return "https://clients5.google.com/translate_a/t?client=dict-chrome-ex&sl=auto&tl=" + target + "&q=" + query;
            yield return "https://translate.google.com/translate_a/single?client=gtx&sl=auto&tl=" + target + "&dt=t&q=" + query;
        }

        private sealed class TimeoutWebClient : WebClient
        {
            protected override WebRequest GetWebRequest(Uri address)
            {
                var request = base.GetWebRequest(address);
                request.Timeout = 8000;
                return request;
            }
        }

        private static string ParseGoogleTranslateResult(string json)
        {
            var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            var root = serializer.DeserializeObject(json) as object[];
            if (root == null || root.Length == 0)
                return string.Empty;

            var sentences = root[0] as object[];
            if (sentences == null)
                return string.Empty;

            if (sentences.Length > 0 && sentences[0] is string)
                return Convert.ToString(sentences[0]);

            var builder = new StringBuilder();
            foreach (var item in sentences)
            {
                var segment = item as object[];
                if (segment != null && segment.Length > 0)
                    builder.Append(Convert.ToString(segment[0]));
            }

            return builder.ToString();
        }

        private static string ParseBaiduTranslateResult(string json)
        {
            var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            var root = serializer.Deserialize<Dictionary<string, object>>(json);
            if (root.ContainsKey("error_code"))
                throw new InvalidOperationException("百度翻译错误 " + Convert.ToString(root["error_code"]) + "：" + (root.ContainsKey("error_msg") ? Convert.ToString(root["error_msg"]) : ""));

            var result = root.ContainsKey("trans_result") ? root["trans_result"] as System.Collections.ArrayList : null;
            if (result == null)
                return string.Empty;

            var builder = new StringBuilder();
            foreach (var item in result)
            {
                var dict = item as Dictionary<string, object>;
                if (dict != null && dict.ContainsKey("dst"))
                {
                    if (builder.Length > 0)
                        builder.AppendLine();
                    builder.Append(Convert.ToString(dict["dst"]));
                }
            }
            return builder.ToString();
        }

        private static string Md5(string value)
        {
            using (var md5 = MD5.Create())
            {
                var bytes = md5.ComputeHash(Encoding.UTF8.GetBytes(value));
                var builder = new StringBuilder();
                foreach (var b in bytes)
                    builder.Append(b.ToString("x2"));
                return builder.ToString();
            }
        }
    }

    internal static class OcrRunner
    {
        public static string Recognize(Bitmap image, HotkeySettings settings)
        {
            var enginePath = ResolveEnginePath(settings.OcrEnginePath);
            var language = string.IsNullOrWhiteSpace(settings.OcrLanguage) ? "chi_sim+eng" : settings.OcrLanguage.Trim();
            var tessdataDirectory = ResolveTessdataDirectory();
            var tempDirectory = Path.Combine(Path.GetTempPath(), "ScreenshotHotkeyToolOcr");
            Directory.CreateDirectory(tempDirectory);

            var inputPath = Path.Combine(tempDirectory, Guid.NewGuid().ToString("N") + ".png");
            var outputBasePath = Path.Combine(tempDirectory, Guid.NewGuid().ToString("N"));
            var outputTextPath = outputBasePath + ".txt";
            var outputTsvPath = outputBasePath + ".tsv";

            try
            {
                using (var preparedImage = PrepareImageForOcr(image))
                {
                    preparedImage.Save(inputPath, ImageFormat.Png);
                }
                var candidates = new List<string>();
                foreach (var pageSegMode in new[] { 6, 4, 11 })
                {
                    var passOutputBasePath = outputBasePath + "_" + pageSegMode;
                    var passOutputTextPath = passOutputBasePath + ".txt";
                    var passOutputTsvPath = passOutputBasePath + ".tsv";
                    try
                    {
                        RunTesseract(enginePath, inputPath, passOutputBasePath, language, tessdataDirectory, "tsv", pageSegMode);
                        var formattedText = ReadTsvOutput(passOutputTsvPath);
                        if (!string.IsNullOrWhiteSpace(formattedText))
                            candidates.Add(formattedText);

                        RunTesseract(enginePath, inputPath, passOutputBasePath, language, tessdataDirectory, string.Empty, pageSegMode);
                        if (File.Exists(passOutputTextPath))
                        {
                            var plainText = RemoveCjkInterCharacterSpaces(File.ReadAllText(passOutputTextPath));
                            if (!string.IsNullOrWhiteSpace(plainText))
                                candidates.Add(plainText);
                        }
                    }
                    finally
                    {
                        TryDelete(passOutputTextPath);
                        TryDelete(passOutputTsvPath);
                    }
                }

                return ChooseBestOcrText(candidates);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("请先安装 Tesseract OCR，或在设置里填写 tesseract.exe 路径。当前错误：" + ex.Message, ex);
            }
            finally
            {
                TryDelete(inputPath);
                TryDelete(outputTextPath);
                TryDelete(outputTsvPath);
            }
        }

        private static void RunTesseract(string enginePath, string inputPath, string outputBasePath, string language, string tessdataDirectory, string outputFormat, int pageSegMode)
        {
            var arguments = Quote(inputPath) + " " + Quote(outputBasePath) + " -l " + language + " --oem 1 --psm " + pageSegMode + " --dpi 300" + TessdataArgument(tessdataDirectory) + " -c preserve_interword_spaces=1";
            if (string.Equals(outputFormat, "tsv", StringComparison.OrdinalIgnoreCase))
                arguments += " -c tessedit_create_tsv=1";
            else if (!string.IsNullOrWhiteSpace(outputFormat))
                arguments += " " + outputFormat;

            var processInfo = new ProcessStartInfo
            {
                FileName = enginePath,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true
            };

            using (var process = Process.Start(processInfo))
            {
                if (process == null)
                    throw new InvalidOperationException("无法启动 OCR 引擎。");

                if (!process.WaitForExit(30000))
                {
                    try { process.Kill(); } catch { }
                    throw new TimeoutException("OCR 超时，请缩小截图区域后重试。");
                }

                var error = process.StandardError.ReadToEnd();
                if (process.ExitCode != 0)
                    throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "OCR 引擎返回失败。" : error.Trim());
            }
        }

        private static string ChooseBestOcrText(List<string> candidates)
        {
            if (candidates == null || candidates.Count == 0)
                return string.Empty;

            var bestText = string.Empty;
            var bestScore = int.MinValue;
            foreach (var candidate in candidates)
            {
                var normalized = NormalizeOcrCandidate(candidate);
                if (string.IsNullOrWhiteSpace(normalized))
                    continue;

                var score = ScoreOcrCandidate(normalized);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestText = normalized;
                }
            }

            return bestText;
        }

        private static string NormalizeOcrCandidate(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var cleaned = new List<string>();
            foreach (var line in lines)
                cleaned.Add(line.TrimEnd());

            return string.Join(Environment.NewLine, cleaned.ToArray()).Trim();
        }

        private static int ScoreOcrCandidate(string text)
        {
            var score = 0;
            var cjkCount = 0;
            var latinCount = 0;
            var digitCount = 0;
            var punctuationCount = 0;
            var gibberishRuns = 0;
            var currentRepeated = 1;
            var previous = '\0';

            foreach (var ch in text)
            {
                if (ch >= '\u4e00' && ch <= '\u9fff')
                {
                    cjkCount++;
                    score += 4;
                }
                else if ((ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z'))
                {
                    latinCount++;
                    score += 2;
                }
                else if (char.IsDigit(ch))
                {
                    digitCount++;
                    score += 2;
                }
                else if ("。！？；：，、,.:-_/()[]{}·•●○".IndexOf(ch) >= 0)
                {
                    punctuationCount++;
                    score += 1;
                }
                else if (!char.IsWhiteSpace(ch))
                {
                    score -= 3;
                }

                if (!char.IsWhiteSpace(ch) && ch == previous)
                {
                    currentRepeated++;
                    if (currentRepeated >= 3)
                        gibberishRuns++;
                }
                else
                {
                    currentRepeated = 1;
                    previous = ch;
                }
            }

            var nonEmptyLines = 0;
            foreach (var line in text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
            {
                if (!string.IsNullOrWhiteSpace(line))
                    nonEmptyLines++;
            }

            score += nonEmptyLines * 12;
            score += Math.Min(80, cjkCount + latinCount + digitCount + punctuationCount);
            score -= gibberishRuns * 20;
            return score;
        }

        private static Bitmap PrepareImageForOcr(Bitmap image)
        {
            var scale = CalculateOcrScale(image.Width, image.Height);
            var width = Math.Max(1, (int)Math.Round(image.Width * scale));
            var height = Math.Max(1, (int)Math.Round(image.Height * scale));
            var prepared = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            prepared.SetResolution(300, 300);

            using (var graphics = Graphics.FromImage(prepared))
            {
                graphics.Clear(Color.White);
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.SmoothingMode = SmoothingMode.HighQuality;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.CompositingQuality = CompositingQuality.HighQuality;
                graphics.DrawImage(image, new Rectangle(0, 0, width, height), new Rectangle(0, 0, image.Width, image.Height), GraphicsUnit.Pixel);
            }

            ApplyContrastForOcr(prepared);
            return prepared;
        }

        private static double CalculateOcrScale(int width, int height)
        {
            var largestSide = Math.Max(width, height);
            if (largestSide <= 0)
                return 2.0;

            return Math.Max(1.0, Math.Min(2.0, 5000.0 / largestSide));
        }

        private static void ApplyContrastForOcr(Bitmap image)
        {
            var rectangle = new Rectangle(0, 0, image.Width, image.Height);
            var data = image.LockBits(rectangle, ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb);
            try
            {
                var stride = data.Stride;
                var bytes = Math.Abs(stride) * image.Height;
                var buffer = new byte[bytes];
                Marshal.Copy(data.Scan0, buffer, 0, bytes);

                for (var y = 0; y < image.Height; y++)
                {
                    var row = y * stride;
                    for (var x = 0; x < image.Width; x++)
                    {
                        var index = row + x * 3;
                        var blue = buffer[index];
                        var green = buffer[index + 1];
                        var red = buffer[index + 2];
                        var luminance = (red * 299 + green * 587 + blue * 114) / 1000;
                        var contrasted = ClampToByte((int)Math.Round((luminance - 128) * 1.45 + 128));
                        if (contrasted > 242)
                            contrasted = 255;
                        else if (contrasted < 35)
                            contrasted = 0;

                        buffer[index] = contrasted;
                        buffer[index + 1] = contrasted;
                        buffer[index + 2] = contrasted;
                    }
                }

                Marshal.Copy(buffer, 0, data.Scan0, bytes);
            }
            finally
            {
                image.UnlockBits(data);
            }
        }

        private static byte ClampToByte(int value)
        {
            if (value < 0)
                return 0;
            if (value > 255)
                return 255;
            return (byte)value;
        }

        private static string ReadTsvOutput(string outputTsvPath)
        {
            if (!File.Exists(outputTsvPath))
                return string.Empty;

            return RemoveCjkInterCharacterSpaces(ReconstructFormattedText(File.ReadAllLines(outputTsvPath)));
        }

        private static string RemoveCjkInterCharacterSpaces(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            var cleaned = Regex.Replace(text, @"(?<=[\u4e00-\u9fff])[\t ]+(?=[\u4e00-\u9fff])", string.Empty);
            cleaned = Regex.Replace(cleaned, @"(?<=[\u4e00-\u9fff])[\t ]+(?=[，。！？；：、）】》])", string.Empty);
            cleaned = Regex.Replace(cleaned, @"(?<=[（【《])[\t ]+(?=[\u4e00-\u9fff])", string.Empty);
            return cleaned;
        }

        private static string ReconstructFormattedText(string[] tsvLines)
        {
            var lines = new List<List<OcrWord>>();
            List<OcrWord> currentLine = null;
            var currentKey = string.Empty;

            for (var i = 1; i < tsvLines.Length; i++)
            {
                var columns = tsvLines[i].Split('\t');
                if (columns.Length < 12 || columns[0] != "5")
                    continue;

                var text = columns[11];
                if (string.IsNullOrWhiteSpace(text))
                    continue;

                int left;
                int top;
                int width;
                int height;
                if (!int.TryParse(columns[6], out left) ||
                    !int.TryParse(columns[7], out top) ||
                    !int.TryParse(columns[8], out width) ||
                    !int.TryParse(columns[9], out height))
                    continue;

                var key = columns[1] + "." + columns[2] + "." + columns[3] + "." + columns[4];
                if (currentLine == null || key != currentKey)
                {
                    currentLine = new List<OcrWord>();
                    lines.Add(currentLine);
                    currentKey = key;
                }

                currentLine.Add(new OcrWord(left, top, width, height, text));
            }

            if (lines.Count == 0)
                return string.Empty;

            var result = new StringBuilder();
            OcrWord previousLineFirstWord = null;
            var previousLineHeight = 0;
            foreach (var line in lines)
            {
                line.Sort(delegate (OcrWord first, OcrWord second) { return first.Left.CompareTo(second.Left); });
                if (line.Count == 0)
                    continue;

                if (previousLineFirstWord != null)
                {
                    var verticalGap = line[0].Top - (previousLineFirstWord.Top + previousLineHeight);
                    if (verticalGap > previousLineHeight)
                        result.AppendLine();
                    result.AppendLine();
                }

                result.Append(ReconstructLine(line));
                previousLineFirstWord = line[0];
                previousLineHeight = MaxHeight(line);
            }

            return result.ToString();
        }

        private static string ReconstructLine(List<OcrWord> words)
        {
            var left = words[0].Left;
            var charWidth = EstimateCharacterWidth(words);
            var line = new StringBuilder();

            foreach (var word in words)
            {
                var column = Math.Max(0, (int)Math.Round((word.Left - left) / charWidth));
                while (line.Length < column)
                    line.Append(' ');
                if (line.Length > 0 && line[line.Length - 1] != ' ' && column <= line.Length)
                    line.Append(' ');
                line.Append(word.Text);
            }

            return line.ToString();
        }

        private static double EstimateCharacterWidth(List<OcrWord> words)
        {
            var totalWidth = 0.0;
            var totalCharacters = 0;
            foreach (var word in words)
            {
                totalWidth += Math.Max(1, word.Width);
                totalCharacters += Math.Max(1, word.Text.Length);
            }

            if (totalCharacters == 0)
                return 8.0;

            return Math.Max(4.0, totalWidth / totalCharacters);
        }

        private static int MaxHeight(List<OcrWord> words)
        {
            var height = 1;
            foreach (var word in words)
                height = Math.Max(height, word.Height);
            return height;
        }

        private sealed class OcrWord
        {
            public readonly int Left;
            public readonly int Top;
            public readonly int Width;
            public readonly int Height;
            public readonly string Text;

            public OcrWord(int left, int top, int width, int height, string text)
            {
                Left = left;
                Top = top;
                Width = width;
                Height = height;
                Text = text;
            }
        }

        private static string ResolveEnginePath(string configuredPath)
        {
            if (!string.IsNullOrWhiteSpace(configuredPath))
                return configuredPath.Trim();

            var bundledPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tesseract-OCR", "tesseract.exe");
            if (File.Exists(bundledPath))
                return bundledPath;

            var localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tesseract.exe");
            if (File.Exists(localPath))
                return localPath;

            var installedPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Tesseract-OCR", "tesseract.exe");
            if (File.Exists(installedPath))
                return installedPath;

            return "tesseract.exe";
        }

        private static string ResolveTessdataDirectory()
        {
            var localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tessdata");
            if (Directory.Exists(localPath))
                return localPath;

            return string.Empty;
        }

        private static string TessdataArgument(string tessdataDirectory)
        {
            if (string.IsNullOrWhiteSpace(tessdataDirectory))
                return string.Empty;

            return " --tessdata-dir " + Quote(tessdataDirectory);
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
            }
        }
    }

    internal sealed class InlineOcrTextControl : RichTextBox
    {
        private readonly ContextMenuStrip copyMenu;
        private bool selectionCopyMode;

        public InlineOcrTextControl()
        {
            BackColor = Color.White;
            ForeColor = Color.Black;
            ReadOnly = true;
            DetectUrls = false;
            HideSelection = false;
            ShortcutsEnabled = true;
            ScrollBars = RichTextBoxScrollBars.Both;
            Cursor = Cursors.SizeAll;
            BorderStyle = BorderStyle.FixedSingle;

            copyMenu = new ContextMenuStrip();
            copyMenu.Items.Add("\u590d\u5236\u9009\u4e2d\u6587\u5b57", null, delegate
            {
                if (!string.IsNullOrEmpty(SelectedText))
                    Clipboard.SetText(SelectedText);
            });
            copyMenu.Opening += delegate(object sender, System.ComponentModel.CancelEventArgs e)
            {
                e.Cancel = !selectionCopyMode || string.IsNullOrEmpty(SelectedText);
            };
        }

        public bool SelectionCopyMode
        {
            get { return selectionCopyMode; }
            set
            {
                selectionCopyMode = value;
                Cursor = selectionCopyMode ? Cursors.IBeam : Cursors.SizeAll;
                ContextMenuStrip = selectionCopyMode ? copyMenu : null;
                if (!selectionCopyMode)
                    Select(0, 0);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (selectionCopyMode)
            {
                base.OnMouseDown(e);
                return;
            }

            Select(0, 0);
            base.OnMouseDown(e);
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_SETCURSOR = 0x0020;
            const int WM_CONTEXTMENU = 0x007B;

            if (!selectionCopyMode && m.Msg == WM_CONTEXTMENU)
            {
                return;
            }

            base.WndProc(ref m);

            if (!selectionCopyMode && m.Msg == WM_SETCURSOR)
                Cursor.Current = Cursors.SizeAll;
        }
    }

    internal sealed class ImageCanvasControl : Control
    {
        private readonly Stack<Bitmap> undoStack = new Stack<Bitmap>();
        private readonly Stack<bool> undoRectangleFlags = new Stack<bool>();
        private readonly Stack<Rectangle> rectangleSelections = new Stack<Rectangle>();
        private Bitmap image;
        private Point lastImagePoint;
        private Point rectangleStartPoint;
        private Point rectangleCurrentPoint;
        private Point rectangleStartControlPoint;
        private Point rectangleCurrentControlPoint;
        private Rectangle lastRectangleSelection;
        private bool hasRectangleSelection;
        private bool isDrawing;
        private int nextNumber = 1;

        public ImageCanvasControl(Bitmap image)
        {
            this.image = image;
            StrokeColor = Color.Red;
            StrokeWidth = 4;
            DoubleBuffered = true;
            BackColor = Color.FromArgb(34, 34, 34);
            TabStop = true;
            SetStyle(ControlStyles.Selectable, true);
        }

        public Bitmap Image
        {
            get { return image; }
        }

        public bool ImageIsUsable
        {
            get
            {
                if (image == null)
                    return false;

                try
                {
                    var unused = image.Width;
                    return true;
                }
                catch (ArgumentException)
                {
                    return false;
                }
            }
        }

        public AnnotationMode Mode { get; set; }

        public event EventHandler ImageCropped;

        public Color StrokeColor { get; set; }

        public int StrokeWidth { get; set; }

        public bool HasRectangleSelection
        {
            get { return hasRectangleSelection; }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (!ImageIsUsable)
                return;

            e.Graphics.Clear(BackColor);
            e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            e.Graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
            var bounds = ImageBounds;
            if (bounds.Width <= 0 || bounds.Height <= 0)
                return;

            e.Graphics.DrawImage(image, bounds);
            DrawFloatingScreenshotBorder(e.Graphics);

            if (Mode == AnnotationMode.Arrow && isDrawing)
                DrawPreviewArrow(e.Graphics, rectangleStartControlPoint, rectangleCurrentControlPoint);
            else if ((Mode == AnnotationMode.Rectangle || Mode == AnnotationMode.Text || Mode == AnnotationMode.Mosaic || Mode == AnnotationMode.Crop) && isDrawing)
                DrawPreviewRectangle(e.Graphics, rectangleStartControlPoint, rectangleCurrentControlPoint);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (Mode == AnnotationMode.None || e.Button != MouseButtons.Left)
                return;

            if (Mode == AnnotationMode.Number)
            {
                PushUndo(false);
                DrawNumberAnnotation(ToImagePoint(e.Location));
                Invalidate();
                return;
            }

            if (Mode != AnnotationMode.Text && Mode != AnnotationMode.Crop)
                PushUndo(Mode == AnnotationMode.Rectangle);
            isDrawing = true;
            var imagePoint = ToImagePoint(e.Location);
            lastImagePoint = imagePoint;
            rectangleStartPoint = imagePoint;
            rectangleCurrentPoint = imagePoint;
            rectangleStartControlPoint = e.Location;
            rectangleCurrentControlPoint = e.Location;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!isDrawing)
                return;

            var nextPoint = ToImagePoint(e.Location);
            if (Mode == AnnotationMode.Rectangle || Mode == AnnotationMode.Text || Mode == AnnotationMode.Arrow || Mode == AnnotationMode.Mosaic || Mode == AnnotationMode.Crop)
            {
                rectangleCurrentPoint = nextPoint;
                rectangleCurrentControlPoint = e.Location;
                Invalidate();
                return;
            }

            using (var graphics = Graphics.FromImage(image))
            using (var pen = new Pen(StrokeColor, CurrentStrokeWidth))
            {
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                graphics.DrawLine(pen, lastImagePoint, nextPoint);
            }

            lastImagePoint = nextPoint;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (Mode == AnnotationMode.Text && isDrawing)
            {
                rectangleCurrentPoint = ToImagePoint(e.Location);
                rectangleCurrentControlPoint = e.Location;
                isDrawing = false;
                var textRectangle = NormalizeRectangle(rectangleStartPoint, rectangleCurrentPoint);
                if (textRectangle.Width < 4 || textRectangle.Height < 4)
                {
                    Invalidate();
                    return;
                }

                using (var form = new TextAnnotationForm())
                {
                    if (form.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(form.AnnotationText))
                    {
                        PushUndo(false);
                        DrawTextAnnotation(NormalizeRectangle(rectangleStartPoint, rectangleCurrentPoint), form.AnnotationText);
                    }
                }
                Invalidate();
                return;
            }

            if (isDrawing && Mode == AnnotationMode.Arrow)
            {
                rectangleCurrentPoint = ToImagePoint(e.Location);
                rectangleCurrentControlPoint = e.Location;
                using (var graphics = Graphics.FromImage(image))
                {
                    DrawArrow(graphics, rectangleStartPoint, rectangleCurrentPoint);
                    isDrawing = false;
                }
                Invalidate();
                return;
            }

            if (isDrawing && Mode == AnnotationMode.Crop)
            {
                rectangleCurrentPoint = ToImagePoint(e.Location);
                rectangleCurrentControlPoint = e.Location;
                isDrawing = false;
                ApplyCrop(NormalizeRectangle(rectangleStartPoint, rectangleCurrentPoint));
                Invalidate();
                return;
            }

            if (isDrawing && Mode == AnnotationMode.Rectangle)
            {
                rectangleCurrentPoint = ToImagePoint(e.Location);
                rectangleCurrentControlPoint = e.Location;
                using (var graphics = Graphics.FromImage(image))
                {
                    DrawRectangle(graphics, rectangleStartPoint, rectangleCurrentPoint);
                    isDrawing = false;
                }
                RememberRectangleSelection(rectangleStartPoint, rectangleCurrentPoint);
                Invalidate();
                return;
            }

            if (isDrawing && Mode == AnnotationMode.Mosaic)
            {
                rectangleCurrentPoint = ToImagePoint(e.Location);
                rectangleCurrentControlPoint = e.Location;
                ApplyMosaic(NormalizeRectangle(rectangleStartPoint, rectangleCurrentPoint));
                isDrawing = false;
                Invalidate();
                return;
            }

            isDrawing = false;
        }

        public void Undo()
        {
            if (undoStack.Count == 0)
                return;

            var previousSize = image.Size;
            image.Dispose();
            image = undoStack.Pop();
            if (undoRectangleFlags.Count > 0 && undoRectangleFlags.Pop())
                PopRectangleSelection();
            if (image.Size != previousSize)
                RaiseImageCropped();
            Invalidate();
        }

        public void Restore(Bitmap original)
        {
            PushUndo(false);
            image.Dispose();
            image = (Bitmap)original.Clone();
            rectangleSelections.Clear();
            hasRectangleSelection = false;
            lastRectangleSelection = Rectangle.Empty;
            nextNumber = 1;
            RaiseImageCropped();
            Invalidate();
        }

        private void PushUndo()
        {
            PushUndo(false);
        }

        private void PushUndo(bool includesRectangleSelection)
        {
            undoStack.Push((Bitmap)image.Clone());
            undoRectangleFlags.Push(includesRectangleSelection);
            while (undoStack.Count > 20)
            {
                var oldItems = undoStack.ToArray();
                var oldFlags = undoRectangleFlags.ToArray();
                undoStack.Clear();
                undoRectangleFlags.Clear();
                for (var i = oldItems.Length - 2; i >= 0; i--)
                {
                    undoStack.Push(oldItems[i]);
                    undoRectangleFlags.Push(oldFlags[i]);
                }
                oldItems[oldItems.Length - 1].Dispose();
            }
        }

        public Bitmap GetImageForOcr(Bitmap originalImage)
        {
            return GetImagesForOcr(originalImage)[0];
        }

        public List<Bitmap> GetImagesForOcr(Bitmap originalImage)
        {
            var images = new List<Bitmap>();
            if (!hasRectangleSelection)
            {
                images.Add((Bitmap)originalImage.Clone());
                return images;
            }

            var rectangles = rectangleSelections.ToArray();
            Array.Reverse(rectangles);
            foreach (var selectedRectangle in rectangles)
            {
                var rectangle = ClampRectangle(selectedRectangle, originalImage.Width, originalImage.Height);
                if (rectangle.Width >= 2 && rectangle.Height >= 2)
                    images.Add(CropBitmap(originalImage, rectangle));
            }

            if (images.Count == 0)
                images.Add((Bitmap)originalImage.Clone());

            return images;
        }

        private Rectangle ImageBounds
        {
            get
            {
                if (image.Width == 0 || image.Height == 0 || Width == 0 || Height == 0)
                    return Rectangle.Empty;

                var scale = Math.Min((double)Width / image.Width, (double)Height / image.Height);
                var displayWidth = (int)Math.Round(image.Width * scale);
                var displayHeight = (int)Math.Round(image.Height * scale);
                return new Rectangle((Width - displayWidth) / 2, (Height - displayHeight) / 2, displayWidth, displayHeight);
            }
        }

        private Point ToImagePoint(Point controlPoint)
        {
            var bounds = ImageBounds;
            if (bounds.Width == 0 || bounds.Height == 0)
                return Point.Empty;

            var x = (int)Math.Round((controlPoint.X - bounds.Left) * (double)image.Width / bounds.Width);
            var y = (int)Math.Round((controlPoint.Y - bounds.Top) * (double)image.Height / bounds.Height);
            return new Point(Clamp(x, 0, image.Width - 1), Clamp(y, 0, image.Height - 1));
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min)
                return min;
            if (value > max)
                return max;
            return value;
        }

        private void DrawRectangle(Graphics graphics, Point first, Point second)
        {
            var x = Math.Min(first.X, second.X);
            var y = Math.Min(first.Y, second.Y);
            var width = Math.Abs(first.X - second.X);
            var height = Math.Abs(first.Y - second.Y);
            if (width < 2 || height < 2)
                return;

            using (var pen = new Pen(StrokeColor, CurrentStrokeWidth))
            {
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                graphics.DrawRectangle(pen, x, y, width, height);
            }
        }

        private void DrawArrow(Graphics graphics, Point first, Point second)
        {
            if (Distance(first, second) < 4)
                return;

            using (var pen = CreateArrowPen(CurrentStrokeWidth, StrokeColor))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.DrawLine(pen, first, second);
            }
        }

        private void DrawTextAnnotation(Rectangle rectangle, string text)
        {
            rectangle = ClampRectangle(rectangle, image.Width, image.Height);
            if (rectangle.Width < 4 || rectangle.Height < 4)
                return;

            using (var graphics = Graphics.FromImage(image))
            using (var background = new SolidBrush(Color.FromArgb(190, Color.White)))
            using (var foreground = new SolidBrush(StrokeColor))
            {
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                graphics.FillRectangle(background, rectangle);
                using (var font = CreateFittingFont(graphics, text, rectangle))
                using (var format = new StringFormat())
                {
                    format.Alignment = StringAlignment.Near;
                    format.LineAlignment = StringAlignment.Center;
                    format.Trimming = StringTrimming.EllipsisCharacter;
                    graphics.DrawString(text, font, foreground, rectangle, format);
                }
            }
        }

        private void DrawNumberAnnotation(Point point)
        {
            var radius = Math.Max(12, CurrentStrokeWidth * 4);
            var rectangle = new Rectangle(point.X - radius, point.Y - radius, radius * 2, radius * 2);
            rectangle = ClampRectangle(rectangle, image.Width, image.Height);
            using (var graphics = Graphics.FromImage(image))
            using (var background = new SolidBrush(StrokeColor))
            using (var foreground = new SolidBrush(Color.White))
            using (var border = new Pen(Color.White, Math.Max(2, CurrentStrokeWidth / 2)))
            using (var font = new Font("Arial", Math.Max(10, radius), FontStyle.Bold))
            using (var format = new StringFormat())
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                format.Alignment = StringAlignment.Center;
                format.LineAlignment = StringAlignment.Center;
                graphics.FillEllipse(background, rectangle);
                graphics.DrawEllipse(border, rectangle);
                graphics.DrawString(nextNumber.ToString(), font, foreground, rectangle, format);
            }
            nextNumber++;
        }

        private void ApplyMosaic(Rectangle rectangle)
        {
            rectangle = ClampRectangle(rectangle, image.Width, image.Height);
            if (rectangle.Width < 4 || rectangle.Height < 4)
                return;

            var blockSize = Math.Max(6, CurrentStrokeWidth * 2);
            using (var graphics = Graphics.FromImage(image))
            {
                for (var y = rectangle.Top; y < rectangle.Bottom; y += blockSize)
                {
                    for (var x = rectangle.Left; x < rectangle.Right; x += blockSize)
                    {
                        var sampleX = Clamp(x + blockSize / 2, 0, image.Width - 1);
                        var sampleY = Clamp(y + blockSize / 2, 0, image.Height - 1);
                        using (var brush = new SolidBrush(image.GetPixel(sampleX, sampleY)))
                        {
                            graphics.FillRectangle(brush, x, y, Math.Min(blockSize, rectangle.Right - x), Math.Min(blockSize, rectangle.Bottom - y));
                        }
                    }
                }
            }
        }

        private void ApplyCrop(Rectangle rectangle)
        {
            rectangle = ClampRectangle(rectangle, image.Width, image.Height);
            if (rectangle.Width < 4 || rectangle.Height < 4)
                return;

            PushUndo(false);
            var cropped = CropBitmap(image, rectangle);
            image.Dispose();
            image = cropped;
            rectangleSelections.Clear();
            hasRectangleSelection = false;
            lastRectangleSelection = Rectangle.Empty;
            nextNumber = 1;
            RaiseImageCropped();
        }

        private void RaiseImageCropped()
        {
            var handler = ImageCropped;
            if (handler != null)
                handler(this, EventArgs.Empty);
        }

        private static Font CreateFittingFont(Graphics graphics, string text, Rectangle rectangle)
        {
            var size = Math.Max(8, Math.Min(36, rectangle.Height - 4));
            while (size > 8)
            {
                using (var testFont = new Font("Microsoft YaHei UI", size, FontStyle.Bold))
                {
                    var measured = graphics.MeasureString(text, testFont, rectangle.Width);
                    if (measured.Height <= rectangle.Height && measured.Width <= rectangle.Width + 2)
                        break;
                }
                size -= 1;
            }

            return new Font("Microsoft YaHei UI", size, FontStyle.Bold);
        }

        private void RememberRectangleSelection(Point first, Point second)
        {
            var rectangle = NormalizeRectangle(first, second);
            if (rectangle.Width < 2 || rectangle.Height < 2)
                return;

            lastRectangleSelection = rectangle;
            hasRectangleSelection = true;
            rectangleSelections.Push(rectangle);
        }

        private void PopRectangleSelection()
        {
            if (rectangleSelections.Count > 0)
                rectangleSelections.Pop();

            if (rectangleSelections.Count == 0)
            {
                hasRectangleSelection = false;
                lastRectangleSelection = Rectangle.Empty;
                return;
            }

            lastRectangleSelection = rectangleSelections.Peek();
            hasRectangleSelection = true;
        }

        private static Rectangle NormalizeRectangle(Point first, Point second)
        {
            var x = Math.Min(first.X, second.X);
            var y = Math.Min(first.Y, second.Y);
            var width = Math.Abs(first.X - second.X);
            var height = Math.Abs(first.Y - second.Y);
            return new Rectangle(x, y, width, height);
        }

        private static Rectangle ClampRectangle(Rectangle rectangle, int maxWidth, int maxHeight)
        {
            var left = Clamp(rectangle.Left, 0, maxWidth - 1);
            var top = Clamp(rectangle.Top, 0, maxHeight - 1);
            var right = Clamp(rectangle.Right, left + 1, maxWidth);
            var bottom = Clamp(rectangle.Bottom, top + 1, maxHeight);
            return new Rectangle(left, top, right - left, bottom - top);
        }

        private static Bitmap CropBitmap(Bitmap originalImage, Rectangle rectangle)
        {
            var cropped = new Bitmap(rectangle.Width, rectangle.Height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(cropped))
            {
                graphics.DrawImage(originalImage, new Rectangle(0, 0, cropped.Width, cropped.Height), rectangle, GraphicsUnit.Pixel);
            }
            return cropped;
        }

        private void DrawPreviewRectangle(Graphics graphics, Point first, Point second)
        {
            var imageBounds = ImageBounds;
            var x = Clamp(Math.Min(first.X, second.X), imageBounds.Left, imageBounds.Right);
            var y = Clamp(Math.Min(first.Y, second.Y), imageBounds.Top, imageBounds.Bottom);
            var right = Clamp(Math.Max(first.X, second.X), imageBounds.Left, imageBounds.Right);
            var bottom = Clamp(Math.Max(first.Y, second.Y), imageBounds.Top, imageBounds.Bottom);
            var width = right - x;
            var height = bottom - y;
            if (width < 2 || height < 2)
                return;

            using (var pen = new Pen(StrokeColor, Math.Max(2, StrokeWidth)))
            {
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                graphics.DrawRectangle(pen, x, y, width, height);
            }
        }

        private void DrawPreviewArrow(Graphics graphics, Point first, Point second)
        {
            var imageBounds = ImageBounds;
            var start = new Point(
                Clamp(first.X, imageBounds.Left, imageBounds.Right),
                Clamp(first.Y, imageBounds.Top, imageBounds.Bottom));
            var end = new Point(
                Clamp(second.X, imageBounds.Left, imageBounds.Right),
                Clamp(second.Y, imageBounds.Top, imageBounds.Bottom));
            if (Distance(start, end) < 4)
                return;

            using (var pen = CreateArrowPen(Math.Max(2, StrokeWidth), StrokeColor))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.DrawLine(pen, start, end);
            }
        }

        private void DrawFloatingScreenshotBorder(Graphics graphics)
        {
            var bounds = ImageBounds;
            if (bounds.Width < 2 || bounds.Height < 2)
                return;

            using (var outerPen = new Pen(Color.Black, 2))
            using (var innerPen = new Pen(Color.FromArgb(230, Color.White), 1))
            {
                graphics.DrawRectangle(outerPen, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
                graphics.DrawRectangle(innerPen, bounds.X + 2, bounds.Y + 2, Math.Max(1, bounds.Width - 5), Math.Max(1, bounds.Height - 5));
            }
        }

        private int CurrentStrokeWidth
        {
            get { return StrokeWidth > 0 ? StrokeWidth : Math.Max(3, image.Width / 220); }
        }

        private static Pen CreateArrowPen(int width, Color color)
        {
            var pen = new Pen(color, width);
            pen.StartCap = LineCap.Round;
            pen.EndCap = LineCap.Custom;
            pen.CustomEndCap = new AdjustableArrowCap(width + 3, width + 5, true);
            return pen;
        }

        private static double Distance(Point first, Point second)
        {
            var dx = first.X - second.X;
            var dy = first.Y - second.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                image.Dispose();
                while (undoStack.Count > 0)
                    undoStack.Pop().Dispose();
            }
            base.Dispose(disposing);
        }
    }

    internal enum AnnotationMode
    {
        None,
        Freehand,
        Rectangle,
        Text,
        Arrow,
        Number,
        Mosaic,
        Crop
    }

    internal static class TrayIconFactory
    {
        public static Icon Create()
        {
            using (var bitmap = new Bitmap(32, 32))
            using (var graphics = Graphics.FromImage(bitmap))
            using (var background = new SolidBrush(Color.FromArgb(24, 119, 242)))
            using (var whitePen = new Pen(Color.White, 3))
            using (var palePen = new Pen(Color.FromArgb(180, 230, 245, 255), 2))
            {
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                graphics.Clear(Color.Transparent);
                graphics.FillEllipse(background, 2, 2, 28, 28);

                graphics.DrawLine(whitePen, 9, 11, 9, 8);
                graphics.DrawLine(whitePen, 9, 8, 13, 8);
                graphics.DrawLine(whitePen, 23, 11, 23, 8);
                graphics.DrawLine(whitePen, 23, 8, 19, 8);
                graphics.DrawLine(whitePen, 9, 21, 9, 24);
                graphics.DrawLine(whitePen, 9, 24, 13, 24);
                graphics.DrawLine(whitePen, 23, 21, 23, 24);
                graphics.DrawLine(whitePen, 23, 24, 19, 24);
                graphics.DrawRectangle(palePen, 12, 12, 8, 8);

                var handle = bitmap.GetHicon();
                try
                {
                    using (var icon = Icon.FromHandle(handle))
                    {
                        return (Icon)icon.Clone();
                    }
                }
                finally
                {
                    DestroyIcon(handle);
                }
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);
    }

    internal sealed class SettingsForm : Form
    {
        private readonly CheckBox ctrlBox;
        private readonly CheckBox shiftBox;
        private readonly CheckBox altBox;
        private readonly CheckBox winBox;
        private readonly ComboBox keyBox;
        private readonly CheckBox ocrEnabledBox;
        private readonly CheckBox ocrCtrlBox;
        private readonly CheckBox ocrShiftBox;
        private readonly CheckBox ocrAltBox;
        private readonly CheckBox ocrWinBox;
        private readonly ComboBox ocrKeyBox;
        private readonly ComboBox ocrLanguageBox;
        private readonly TextBox ocrEnginePathBox;
        private readonly ComboBox translationProviderBox;
        private readonly TextBox baiduAppIdBox;
        private readonly TextBox baiduSecretKeyBox;
        private readonly TextBox saveDirectoryBox;
        private readonly Button saveButton;
        private readonly Button cancelButton;

        public HotkeySettings SelectedSettings { get; private set; }

        public SettingsForm(HotkeySettings current)
        {
            Text = "截图快捷键设置";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(430, 700);

            var hotkeyTitle = new Label { Text = "截图快捷键", Left = 16, Top = 16, Width = 360 };
            ctrlBox = new CheckBox { Text = "Ctrl", Left = 18, Top = 44, Width = 70 };
            shiftBox = new CheckBox { Text = "Shift", Left = 90, Top = 44, Width = 70 };
            altBox = new CheckBox { Text = "Alt", Left = 162, Top = 44, Width = 70 };
            winBox = new CheckBox { Text = "Win", Left = 234, Top = 44, Width = 70 };

            keyBox = new ComboBox { Left = 18, Top = 78, Width = 390, DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (var key in HotkeySettings.AllowedKeys)
                keyBox.Items.Add(key);

            var saveDirectoryLabel = new Label { Text = "截图保存位置", Left = 16, Top = 118, Width = 360 };
            saveDirectoryBox = new TextBox { Left = 18, Top = 144, Width = 305 };
            var browseButton = new Button { Text = "浏览", Left = 330, Top = 142, Width = 78 };
            browseButton.Click += delegate
            {
                using (var dialog = new FolderBrowserDialog())
                {
                    dialog.Description = "选择截图保存位置";
                    dialog.SelectedPath = Directory.Exists(saveDirectoryBox.Text) ? saveDirectoryBox.Text : HotkeySettings.DefaultSaveDirectory();
                    if (dialog.ShowDialog() == DialogResult.OK)
                        saveDirectoryBox.Text = dialog.SelectedPath;
                }
            };

            var ocrTitle = new Label { Text = "OCR 文字识别", Left = 16, Top = 190, Width = 360 };
            ocrEnabledBox = new CheckBox { Text = "启用 OCR 快捷键", Left = 18, Top = 216, Width = 180 };
            ocrCtrlBox = new CheckBox { Text = "Ctrl", Left = 18, Top = 250, Width = 70 };
            ocrShiftBox = new CheckBox { Text = "Shift", Left = 90, Top = 250, Width = 70 };
            ocrAltBox = new CheckBox { Text = "Alt", Left = 162, Top = 250, Width = 70 };
            ocrWinBox = new CheckBox { Text = "Win", Left = 234, Top = 250, Width = 70 };

            ocrKeyBox = new ComboBox { Left = 18, Top = 284, Width = 390, DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (var key in HotkeySettings.AllowedKeys)
                ocrKeyBox.Items.Add(key);

            var ocrLanguageLabel = new Label { Text = "识别语言", Left = 16, Top = 324, Width = 360 };
            ocrLanguageBox = new ComboBox { Left = 18, Top = 350, Width = 390, DropDownStyle = ComboBoxStyle.DropDown };
            ocrLanguageBox.Items.Add("chi_sim+eng");
            ocrLanguageBox.Items.Add("eng");
            ocrLanguageBox.Items.Add("chi_sim");
            ocrLanguageBox.Items.Add("chi_tra+eng");

            var ocrEngineLabel = new Label { Text = "Tesseract 路径（可留空）", Left = 16, Top = 390, Width = 360 };
            ocrEnginePathBox = new TextBox { Left = 18, Top = 416, Width = 305 };
            var ocrBrowseButton = new Button { Text = "浏览", Left = 330, Top = 414, Width = 78 };
            ocrBrowseButton.Click += delegate
            {
                using (var dialog = new OpenFileDialog())
                {
                    dialog.Title = "选择 tesseract.exe";
                    dialog.Filter = "Tesseract (*.exe)|*.exe|所有文件 (*.*)|*.*";
                    if (dialog.ShowDialog() == DialogResult.OK)
                        ocrEnginePathBox.Text = dialog.FileName;
                }
            };

            var translationTitle = new Label { Text = "翻译服务", Left = 16, Top = 462, Width = 360 };
            translationProviderBox = new ComboBox { Left = 18, Top = 488, Width = 390, DropDownStyle = ComboBoxStyle.DropDownList };
            translationProviderBox.Items.Add("Google");
            translationProviderBox.Items.Add("Baidu");

            var baiduAppIdLabel = new Label { Text = "百度翻译 App ID", Left = 16, Top = 526, Width = 360 };
            baiduAppIdBox = new TextBox { Left = 18, Top = 552, Width = 390 };
            var baiduSecretLabel = new Label { Text = "百度翻译密钥", Left = 16, Top = 584, Width = 360 };
            baiduSecretKeyBox = new TextBox { Left = 18, Top = 610, Width = 390, UseSystemPasswordChar = true };

            saveButton = new Button { Text = "保存", Left = 250, Top = 654, Width = 76, DialogResult = DialogResult.OK };
            cancelButton = new Button { Text = "取消", Left = 332, Top = 654, Width = 76, DialogResult = DialogResult.Cancel };

            Controls.Add(hotkeyTitle);
            Controls.Add(ctrlBox);
            Controls.Add(shiftBox);
            Controls.Add(altBox);
            Controls.Add(winBox);
            Controls.Add(keyBox);
            Controls.Add(saveDirectoryLabel);
            Controls.Add(saveDirectoryBox);
            Controls.Add(browseButton);
            Controls.Add(ocrTitle);
            Controls.Add(ocrEnabledBox);
            Controls.Add(ocrCtrlBox);
            Controls.Add(ocrShiftBox);
            Controls.Add(ocrAltBox);
            Controls.Add(ocrWinBox);
            Controls.Add(ocrKeyBox);
            Controls.Add(ocrLanguageLabel);
            Controls.Add(ocrLanguageBox);
            Controls.Add(ocrEngineLabel);
            Controls.Add(ocrEnginePathBox);
            Controls.Add(ocrBrowseButton);
            Controls.Add(translationTitle);
            Controls.Add(translationProviderBox);
            Controls.Add(baiduAppIdLabel);
            Controls.Add(baiduAppIdBox);
            Controls.Add(baiduSecretLabel);
            Controls.Add(baiduSecretKeyBox);
            Controls.Add(saveButton);
            Controls.Add(cancelButton);

            AcceptButton = saveButton;
            CancelButton = cancelButton;

            ctrlBox.Checked = (current.Modifiers & HotkeyModifiers.Control) != 0;
            shiftBox.Checked = (current.Modifiers & HotkeyModifiers.Shift) != 0;
            altBox.Checked = (current.Modifiers & HotkeyModifiers.Alt) != 0;
            winBox.Checked = (current.Modifiers & HotkeyModifiers.Win) != 0;
            keyBox.SelectedItem = HotkeySettings.KeyNameFromCode(current.KeyCode);
            if (keyBox.SelectedIndex < 0)
                keyBox.SelectedItem = "R";
            saveDirectoryBox.Text = string.IsNullOrWhiteSpace(current.SaveDirectory) ? HotkeySettings.DefaultSaveDirectory() : current.SaveDirectory;
            ocrEnabledBox.Checked = current.OcrEnabled;
            ocrCtrlBox.Checked = (current.OcrModifiers & HotkeyModifiers.Control) != 0;
            ocrShiftBox.Checked = (current.OcrModifiers & HotkeyModifiers.Shift) != 0;
            ocrAltBox.Checked = (current.OcrModifiers & HotkeyModifiers.Alt) != 0;
            ocrWinBox.Checked = (current.OcrModifiers & HotkeyModifiers.Win) != 0;
            ocrKeyBox.SelectedItem = HotkeySettings.KeyNameFromCode(current.OcrKeyCode);
            if (ocrKeyBox.SelectedIndex < 0)
                ocrKeyBox.SelectedItem = "T";
            ocrLanguageBox.Text = string.IsNullOrWhiteSpace(current.OcrLanguage) ? "chi_sim+eng" : current.OcrLanguage;
            ocrEnginePathBox.Text = current.OcrEnginePath ?? string.Empty;
            translationProviderBox.SelectedItem = string.IsNullOrWhiteSpace(current.TranslationProvider) ? "Google" : current.TranslationProvider;
            if (translationProviderBox.SelectedIndex < 0)
                translationProviderBox.SelectedItem = "Google";
            baiduAppIdBox.Text = current.BaiduAppId ?? string.Empty;
            baiduSecretKeyBox.Text = current.BaiduSecretKey ?? string.Empty;

            saveButton.Click += delegate
            {
                var selected = BuildSettings();
                if (!selected.HasModifier)
                {
                    MessageBox.Show("请至少选择一个组合键：Ctrl、Shift、Alt 或 Win。", "截图快捷键");
                    DialogResult = DialogResult.None;
                    return;
                }
                if (selected.OcrEnabled && !selected.HasOcrModifier)
                {
                    MessageBox.Show("请至少选择一个 OCR 组合键：Ctrl、Shift、Alt 或 Win。", "截图快捷键");
                    DialogResult = DialogResult.None;
                    return;
                }

                try
                {
                    Directory.CreateDirectory(selected.SaveDirectory);
                }
                catch
                {
                    MessageBox.Show("保存位置不可用，请换一个文件夹。", "截图快捷键");
                    DialogResult = DialogResult.None;
                    return;
                }

                SelectedSettings = selected;
            };
        }

        private HotkeySettings BuildSettings()
        {
            var modifiers = HotkeyModifiers.None;
            if (ctrlBox.Checked)
                modifiers |= HotkeyModifiers.Control;
            if (shiftBox.Checked)
                modifiers |= HotkeyModifiers.Shift;
            if (altBox.Checked)
                modifiers |= HotkeyModifiers.Alt;
            if (winBox.Checked)
                modifiers |= HotkeyModifiers.Win;

            var ocrModifiers = HotkeyModifiers.None;
            if (ocrCtrlBox.Checked)
                ocrModifiers |= HotkeyModifiers.Control;
            if (ocrShiftBox.Checked)
                ocrModifiers |= HotkeyModifiers.Shift;
            if (ocrAltBox.Checked)
                ocrModifiers |= HotkeyModifiers.Alt;
            if (ocrWinBox.Checked)
                ocrModifiers |= HotkeyModifiers.Win;

            return new HotkeySettings
            {
                Modifiers = modifiers,
                KeyCode = HotkeySettings.KeyCodeFromName((string)keyBox.SelectedItem),
                SaveScreenshot = true,
                SaveDirectory = string.IsNullOrWhiteSpace(saveDirectoryBox.Text) ? HotkeySettings.DefaultSaveDirectory() : saveDirectoryBox.Text.Trim(),
                OcrEnabled = ocrEnabledBox.Checked,
                OcrModifiers = ocrModifiers,
                OcrKeyCode = HotkeySettings.KeyCodeFromName((string)ocrKeyBox.SelectedItem),
                OcrLanguage = string.IsNullOrWhiteSpace(ocrLanguageBox.Text) ? "chi_sim+eng" : ocrLanguageBox.Text.Trim(),
                OcrEnginePath = string.IsNullOrWhiteSpace(ocrEnginePathBox.Text) ? string.Empty : ocrEnginePathBox.Text.Trim(),
                TranslationProvider = translationProviderBox.SelectedItem == null ? "Google" : Convert.ToString(translationProviderBox.SelectedItem),
                BaiduAppId = string.IsNullOrWhiteSpace(baiduAppIdBox.Text) ? string.Empty : baiduAppIdBox.Text.Trim(),
                BaiduSecretKey = string.IsNullOrWhiteSpace(baiduSecretKeyBox.Text) ? string.Empty : baiduSecretKeyBox.Text.Trim()
            };
        }
    }

    internal sealed class HotkeySettings
    {
        private const string SettingsFileName = "settings.json";

        public static readonly string[] AllowedKeys = new[]
        {
            "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M",
            "N", "O", "P", "Q", "R", "S", "T", "U", "V", "W", "X", "Y", "Z",
            "0", "1", "2", "3", "4", "5", "6", "7", "8", "9",
            "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12"
        };

        public HotkeyModifiers Modifiers { get; set; }
        public uint KeyCode { get; set; }
        public bool SaveScreenshot { get; set; }
        public string SaveDirectory { get; set; }
        public bool OcrEnabled { get; set; }
        public HotkeyModifiers OcrModifiers { get; set; }
        public uint OcrKeyCode { get; set; }
        public string OcrLanguage { get; set; }
        public string OcrEnginePath { get; set; }
        public string TranslationProvider { get; set; }
        public string BaiduAppId { get; set; }
        public string BaiduSecretKey { get; set; }

        public bool HasModifier
        {
            get { return Modifiers != HotkeyModifiers.None; }
        }

        public bool HasOcrModifier
        {
            get { return OcrModifiers != HotkeyModifiers.None; }
        }

        public string DisplayText
        {
            get
            {
                return DisplayTextFor(Modifiers, KeyCode);
            }
        }

        public string OcrDisplayText
        {
            get
            {
                return OcrEnabled ? DisplayTextFor(OcrModifiers, OcrKeyCode) : "未启用";
            }
        }

        public static HotkeySettings Default()
        {
            return new HotkeySettings
            {
                Modifiers = HotkeyModifiers.Control | HotkeyModifiers.Shift,
                KeyCode = KeyCodeFromName("R"),
                SaveScreenshot = true,
                SaveDirectory = DefaultSaveDirectory(),
                OcrEnabled = false,
                OcrModifiers = HotkeyModifiers.Control | HotkeyModifiers.Shift,
                OcrKeyCode = KeyCodeFromName("T"),
                OcrLanguage = "chi_sim+eng",
                OcrEnginePath = string.Empty,
                TranslationProvider = "Google",
                BaiduAppId = string.Empty,
                BaiduSecretKey = string.Empty
            };
        }

        public static string DefaultSaveDirectory()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "captures");
        }

        public static HotkeySettings Load()
        {
            var path = SettingsPath();
            if (!File.Exists(path))
                return Default();

            try
            {
                var serializer = new JavaScriptSerializer();
                var data = serializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(path));
                var defaults = Default();
                var loaded = new HotkeySettings
                {
                    Modifiers = data.ContainsKey("modifiers") ? (HotkeyModifiers)Convert.ToUInt32(data["modifiers"]) : defaults.Modifiers,
                    KeyCode = data.ContainsKey("keyCode") ? Convert.ToUInt32(data["keyCode"]) : defaults.KeyCode,
                    SaveScreenshot = data.ContainsKey("saveScreenshot") ? Convert.ToBoolean(data["saveScreenshot"]) : true,
                    SaveDirectory = data.ContainsKey("saveDirectory") ? Convert.ToString(data["saveDirectory"]) : DefaultSaveDirectory(),
                    OcrEnabled = data.ContainsKey("ocrEnabled") ? Convert.ToBoolean(data["ocrEnabled"]) : defaults.OcrEnabled,
                    OcrModifiers = data.ContainsKey("ocrModifiers") ? (HotkeyModifiers)Convert.ToUInt32(data["ocrModifiers"]) : defaults.OcrModifiers,
                    OcrKeyCode = data.ContainsKey("ocrKeyCode") ? Convert.ToUInt32(data["ocrKeyCode"]) : defaults.OcrKeyCode,
                    OcrLanguage = data.ContainsKey("ocrLanguage") ? Convert.ToString(data["ocrLanguage"]) : defaults.OcrLanguage,
                    OcrEnginePath = data.ContainsKey("ocrEnginePath") ? Convert.ToString(data["ocrEnginePath"]) : defaults.OcrEnginePath,
                    TranslationProvider = data.ContainsKey("translationProvider") ? Convert.ToString(data["translationProvider"]) : defaults.TranslationProvider,
                    BaiduAppId = data.ContainsKey("baiduAppId") ? Convert.ToString(data["baiduAppId"]) : defaults.BaiduAppId,
                    BaiduSecretKey = data.ContainsKey("baiduSecretKey") ? Convert.ToString(data["baiduSecretKey"]) : defaults.BaiduSecretKey
                };

                if (string.IsNullOrWhiteSpace(loaded.SaveDirectory))
                    loaded.SaveDirectory = DefaultSaveDirectory();
                if (string.IsNullOrWhiteSpace(loaded.OcrLanguage))
                    loaded.OcrLanguage = defaults.OcrLanguage;
                if (string.IsNullOrWhiteSpace(loaded.TranslationProvider))
                    loaded.TranslationProvider = defaults.TranslationProvider;
                return loaded;
            }
            catch
            {
                return Default();
            }
        }

        public void Save()
        {
            var serializer = new JavaScriptSerializer();
            var data = new Dictionary<string, object>
            {
                { "modifiers", (uint)Modifiers },
                { "keyCode", KeyCode },
                { "displayText", DisplayText },
                { "saveScreenshot", SaveScreenshot },
                { "saveDirectory", SaveDirectory },
                { "ocrEnabled", OcrEnabled },
                { "ocrModifiers", (uint)OcrModifiers },
                { "ocrKeyCode", OcrKeyCode },
                { "ocrDisplayText", OcrDisplayText },
                { "ocrLanguage", OcrLanguage },
                { "ocrEnginePath", OcrEnginePath },
                { "translationProvider", TranslationProvider },
                { "baiduAppId", BaiduAppId },
                { "baiduSecretKey", BaiduSecretKey }
            };
            File.WriteAllText(SettingsPath(), serializer.Serialize(data));
        }

        private static string DisplayTextFor(HotkeyModifiers modifiers, uint keyCode)
        {
            var parts = new List<string>();
            if ((modifiers & HotkeyModifiers.Control) != 0)
                parts.Add("Ctrl");
            if ((modifiers & HotkeyModifiers.Shift) != 0)
                parts.Add("Shift");
            if ((modifiers & HotkeyModifiers.Alt) != 0)
                parts.Add("Alt");
            if ((modifiers & HotkeyModifiers.Win) != 0)
                parts.Add("Win");
            parts.Add(KeyNameFromCode(keyCode));
            return string.Join(" + ", parts.ToArray());
        }

        public static uint KeyCodeFromName(string name)
        {
            if (name.Length == 1)
            {
                var ch = name[0];
                if (ch >= 'A' && ch <= 'Z')
                    return ch;
                if (ch >= '0' && ch <= '9')
                    return ch;
            }

            if (name.StartsWith("F", StringComparison.OrdinalIgnoreCase))
            {
                int number;
                if (int.TryParse(name.Substring(1), out number) && number >= 1 && number <= 12)
                    return (uint)(0x70 + number - 1);
            }

            return 'R';
        }

        public static string KeyNameFromCode(uint code)
        {
            if (code >= 'A' && code <= 'Z')
                return ((char)code).ToString();
            if (code >= '0' && code <= '9')
                return ((char)code).ToString();
            if (code >= 0x70 && code <= 0x7B)
                return "F" + (code - 0x70 + 1);
            return "R";
        }

        private static string SettingsPath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, SettingsFileName);
        }
    }

    [Flags]
    internal enum HotkeyModifiers : uint
    {
        None = 0x0000,
        Alt = 0x0001,
        Control = 0x0002,
        Shift = 0x0004,
        Win = 0x0008
    }

    internal sealed class HotkeyWindow : NativeWindow, IDisposable
    {
        private const int WM_HOTKEY = 0x0312;
        private const int WM_FALLBACK_HOTKEY = 0x0451;
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYUP = 0x0105;
        private readonly int hotkeyId;
        private readonly Action onHotkey;
        private LowLevelKeyboardProc hookCallback;
        private IntPtr hookHandle;
        private HotkeyModifiers fallbackModifiers;
        private uint fallbackKeyCode;
        private bool registered;
        private bool fallbackActive;
        private bool fallbackCombinationDown;

        public HotkeyWindow(int hotkeyId, Action onHotkey)
        {
            this.hotkeyId = hotkeyId;
            this.onHotkey = onHotkey;
            CreateHandle(new CreateParams());
        }

        public bool Register(HotkeyModifiers modifiers, uint keyCode)
        {
            Unregister();

            registered = RegisterHotKey(Handle, hotkeyId, (uint)modifiers, keyCode);
            if (registered)
                return true;

            return RegisterFallbackHook(modifiers, keyCode);
        }

        public void Unregister()
        {
            if (registered)
            {
                UnregisterHotKey(Handle, hotkeyId);
                registered = false;
            }

            UnregisterFallbackHook();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == hotkeyId)
                onHotkey();
            else if (m.Msg == WM_FALLBACK_HOTKEY && m.WParam.ToInt32() == hotkeyId)
                onHotkey();
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            Unregister();
            DestroyHandle();
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private bool RegisterFallbackHook(HotkeyModifiers modifiers, uint keyCode)
        {
            fallbackModifiers = modifiers;
            fallbackKeyCode = keyCode;
            fallbackCombinationDown = false;
            hookCallback = HookProc;
            hookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, hookCallback, GetModuleHandle(null), 0);
            fallbackActive = hookHandle != IntPtr.Zero;
            return fallbackActive;
        }

        private void UnregisterFallbackHook()
        {
            if (!fallbackActive)
                return;

            UnhookWindowsHookEx(hookHandle);
            hookHandle = IntPtr.Zero;
            hookCallback = null;
            fallbackActive = false;
            fallbackCombinationDown = false;
        }

        private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                var message = wParam.ToInt32();
                var keyCode = (uint)Marshal.ReadInt32(lParam);
                if (message == WM_KEYDOWN || message == WM_SYSKEYDOWN)
                {
                    if (keyCode == fallbackKeyCode && IsFallbackCombinationPressed())
                    {
                        if (!fallbackCombinationDown)
                        {
                            fallbackCombinationDown = true;
                            PostMessage(Handle, WM_FALLBACK_HOTKEY, new IntPtr(hotkeyId), IntPtr.Zero);
                        }
                        return new IntPtr(1);
                    }
                }
                else if (message == WM_KEYUP || message == WM_SYSKEYUP)
                {
                    if (keyCode == fallbackKeyCode || !IsFallbackCombinationPressed())
                        fallbackCombinationDown = false;
                }
            }

            return CallNextHookEx(hookHandle, nCode, wParam, lParam);
        }

        private bool IsFallbackCombinationPressed()
        {
            if ((fallbackModifiers & HotkeyModifiers.Control) != 0 && !IsKeyPressed(Keys.ControlKey))
                return false;
            if ((fallbackModifiers & HotkeyModifiers.Shift) != 0 && !IsKeyPressed(Keys.ShiftKey))
                return false;
            if ((fallbackModifiers & HotkeyModifiers.Alt) != 0 && !IsKeyPressed(Keys.Menu))
                return false;
            if ((fallbackModifiers & HotkeyModifiers.Win) != 0 && !IsKeyPressed(Keys.LWin) && !IsKeyPressed(Keys.RWin))
                return false;
            return true;
        }

        private static bool IsKeyPressed(Keys key)
        {
            return (GetAsyncKeyState((int)key) & 0x8000) != 0;
        }

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);
    }
}
