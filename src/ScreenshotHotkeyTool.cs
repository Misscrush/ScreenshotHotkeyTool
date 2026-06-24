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
        private static void Main()
        {
            DpiAwareness.Enable();

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
        private HotkeySettings settings;
        private bool isCapturing;

        public TrayAppContext()
        {
            settings = HotkeySettings.Load();
            screenshotHotkeyWindow = new HotkeyWindow(7301, TriggerSnip);
            ocrHotkeyWindow = new HotkeyWindow(7302, TriggerOcr);

            if (!screenshotHotkeyWindow.Register(settings.Modifiers, settings.KeyCode))
            {
                MessageBox.Show(settings.DisplayText + " ?????????????????", "?????", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            if (settings.OcrEnabled && !ocrHotkeyWindow.Register(settings.OcrModifiers, settings.OcrKeyCode))
            {
                MessageBox.Show(settings.OcrDisplayText + " ????????????? OCR ????", "?????", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            var menu = new ContextMenuStrip();
            menu.Items.Add("????", null, delegate { TriggerSnip(); });
            menu.Items.Add("????", null, delegate { TriggerOcr(); });
            menu.Items.Add("??", null, delegate { OpenSettings(); });
            menu.Items.Add("??", null, delegate { ExitThread(); });

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
                    MessageBox.Show("???????????????", "?????", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                settings = newSettings;
                settings.Save();
                UpdateTrayText();
            }
        }

        private void UpdateTrayText()
        {
            trayIcon.Text = Shorten("???" + settings.DisplayText + " OCR?" + settings.OcrDisplayText, 63);
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
            StartSelection(SaveCapturedImage);
        }

        private void TriggerOcr()
        {
            if (!settings.OcrEnabled)
            {
                MessageBox.Show("OCR ????????????", "?????", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            StartSelection(RecognizeCapturedImage);
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
                return "OCR ???" + Environment.NewLine + ex.Message;
            }
        }

        private string SaveBitmap(Bitmap image)
        {
            var directory = settings.SaveDirectory;
            if (string.IsNullOrWhiteSpace(directory))
                directory = HotkeySettings.DefaultSaveDirectory();

            Directory.CreateDirectory(directory);
            var filename = "??_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png";
            string path;
            using (var dialog = new SaveFileDialog())
            {
                dialog.Title = "????";
                dialog.InitialDirectory = directory;
                dialog.FileName = filename;
                dialog.Filter = "PNG ?? (*.png)|*.png";
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
        private Point startPoint;
        private Point currentPoint;
        private bool selecting;

        public SelectionOverlayForm(Rectangle virtualBounds, Bitmap screenshot, Action<Bitmap> onCaptured)
        {
            this.virtualBounds = virtualBounds;
            this.screenshot = screenshot;
            this.onCaptured = onCaptured;

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
            e.Graphics.DrawImageUnscaled(screenshot, 0, 0);

            using (var overlayBrush = new SolidBrush(Color.FromArgb(95, Color.Black)))
            {
                e.Graphics.FillRectangle(overlayBrush, ClientRectangle);
            }

            var selection = CurrentSelection;
            if (selection.Width > 0 && selection.Height > 0)
            {
                e.Graphics.SetClip(selection);
                e.Graphics.DrawImageUnscaled(screenshot, 0, 0);
                e.Graphics.ResetClip();

                using (var borderPen = new Pen(Color.White, 2))
                using (var guidePen = new Pen(Color.FromArgb(210, 24, 119, 242), 1))
                {
                    e.Graphics.DrawRectangle(borderPen, selection);
                    e.Graphics.DrawRectangle(guidePen, selection.X + 2, selection.Y + 2, Math.Max(1, selection.Width - 4), Math.Max(1, selection.Height - 4));
                }
            }

            DrawHint(e.Graphics);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
                return;

            selecting = true;
            startPoint = e.Location;
            currentPoint = e.Location;
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (!selecting)
                return;

            currentPoint = e.Location;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
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

            Hide();
            using (var cropped = screenshot.Clone(selection, PixelFormat.Format32bppArgb))
            {
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

        protected override void Dispose(bool disposing)
        {
            if (disposing && screenshot != null)
                screenshot.Dispose();
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

        private void DrawHint(Graphics graphics)
        {
            var text = "???????????? Esc ??";
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
        private readonly Button drawButton;
        private readonly Button rectangleButton;
        private readonly Button textButton;
        private readonly Button arrowButton;
        private readonly Label statusLabel;

        public PreviewForm(Bitmap image, Func<Bitmap, string> saveImage, Func<Bitmap, string> recognizeText, HotkeySettings settings)
        {
            this.saveImage = saveImage;
            this.recognizeText = recognizeText;
            this.settings = settings ?? HotkeySettings.Default();
            originalImage = (Bitmap)image.Clone();

            Text = "????";
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

            var copyButton = new Button { Text = "??", Width = 78, Height = 30 };
            var saveButton = new Button { Text = "??", Width = 78, Height = 30 };
            var ocrButton = new Button { Text = "????", Width = 96, Height = 30 };
            drawButton = new Button { Text = "??", Width = 78, Height = 30 };
            var rectangleButton = new Button { Text = "??", Width = 78, Height = 30 };
            this.rectangleButton = rectangleButton;
            textButton = new Button { Text = "??", Width = 78, Height = 30 };
            arrowButton = new Button { Text = "??", Width = 78, Height = 30 };
            var undoButton = new Button { Text = "??", Width = 78, Height = 30 };
            var clearButton = new Button { Text = "??", Width = 78, Height = 30 };
            var closeButton = new Button { Text = "??", Width = 78, Height = 30 };

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
                Dock = DockStyle.Fill
            };

            statusLabel = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 26,
                Padding = new Padding(12, 5, 12, 0),
                BackColor = Color.FromArgb(250, 250, 250),
                ForeColor = Color.FromArgb(80, 80, 80),
                Text = "????????????????????????"
            };

            Controls.Add(canvas);
            Controls.Add(statusLabel);
            Controls.Add(toolbar);

            copyButton.Click += delegate
            {
                Clipboard.SetImage((Bitmap)canvas.Image.Clone());
                Text = "???? - ???????";
            };

            saveButton.Click += delegate
            {
                using (var copy = (Bitmap)canvas.Image.Clone())
                {
                    var path = saveImage(copy);
                    if (!string.IsNullOrEmpty(path))
                        Text = "???? - ????" + path;
                }
            };

            ocrButton.Click += delegate
            {
                var result = new OcrResultForm(RecognizeImages(canvas.GetImagesForOcr(originalImage)), settings);
                result.Show();
                statusLabel.Text = canvas.HasRectangleSelection ? "?????????" : "???????";
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

        private void UpdateToolButtons()
        {
            drawButton.Text = canvas.Mode == AnnotationMode.Freehand ? "????" : "??";
            rectangleButton.Text = canvas.Mode == AnnotationMode.Rectangle ? "????" : "??";
            textButton.Text = canvas.Mode == AnnotationMode.Text ? "????" : "??";
            arrowButton.Text = canvas.Mode == AnnotationMode.Arrow ? "????" : "??";
            canvas.Cursor = canvas.Mode == AnnotationMode.None ? Cursors.Default : Cursors.Cross;
            UpdateStatus();
        }

        private void UpdateStatus()
        {
            if (canvas.Mode == AnnotationMode.Freehand)
                statusLabel.Text = "????";
            else if (canvas.Mode == AnnotationMode.Rectangle)
                statusLabel.Text = "??????????????????????????";
            else if (canvas.Mode == AnnotationMode.Text)
                statusLabel.Text = "?????????????????????";
            else if (canvas.Mode == AnnotationMode.Arrow)
                statusLabel.Text = "?????????????????????";
            else
                statusLabel.Text = canvas.HasRectangleSelection ? "???????????????????" : "??????????????????????";
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
            Text = "????";
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(360, 136);

            var label = new Label { Text = "????????????", Left = 14, Top = 14, Width = 320 };
            textBox = new TextBox { Left = 16, Top = 42, Width = 328 };
            var okButton = new Button { Text = "??", Left = 186, Top = 92, Width = 76, DialogResult = DialogResult.OK };
            var cancelButton = new Button { Text = "??", Left = 268, Top = 92, Width = 76, DialogResult = DialogResult.Cancel };

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
        private bool formatRemoved;

        public OcrResultForm(string text, HotkeySettings settings)
        {
            formattedText = text ?? string.Empty;
            this.settings = settings ?? HotkeySettings.Default();
            Text = "??????";
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
                Text = string.IsNullOrWhiteSpace(text) ? "??????" : "??? " + text.Trim().Length + " ???"
            };

            var toolbar = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 54,
                Padding = new Padding(12, 10, 12, 8),
                FlowDirection = FlowDirection.RightToLeft,
                BackColor = Color.FromArgb(245, 247, 250)
            };

            var closeButton = new Button { Text = "??", Width = 78, Height = 30 };
            var saveButton = new Button { Text = "??", Width = 78, Height = 30 };
            var copyButton = new Button { Text = "??", Width = 78, Height = 30 };
            var formatButton = new Button { Text = "???", Width = 86, Height = 30 };
            var translateToEnglishButton = new Button { Text = "??", Width = 78, Height = 30 };
            var translateToChineseButton = new Button { Text = "??", Width = 78, Height = 30 };
            translationProviderBox = new ComboBox { Width = 90, Height = 30, DropDownStyle = ComboBoxStyle.DropDownList };
            translationProviderBox.Items.Add("Google");
            translationProviderBox.Items.Add("Baidu");
            translationProviderBox.SelectedItem = string.IsNullOrWhiteSpace(this.settings.TranslationProvider) ? "Google" : this.settings.TranslationProvider;
            if (translationProviderBox.SelectedIndex < 0)
                translationProviderBox.SelectedItem = "Google";

            var translationProviderLabel = new Label
            {
                Text = "???",
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
            toolbar.Controls.Add(translationProviderBox);
            toolbar.Controls.Add(translationProviderLabel);
            Controls.Add(resultBox);
            Controls.Add(toolbar);
            Controls.Add(header);

            copyButton.Click += delegate
            {
                if (!string.IsNullOrEmpty(resultBox.Text))
                    Clipboard.SetText(resultBox.Text);
                statusLabel.Text = "???????";
            };

            formatButton.Click += delegate
            {
                if (formatRemoved)
                {
                    resultBox.Text = formattedText;
                    formatRemoved = false;
                    formatButton.Text = "???";
                    statusLabel.Text = "?????";
                }
                else
                {
                    resultBox.Text = RemoveTextFormatting(formattedText);
                    formatRemoved = true;
                    formatButton.Text = "????";
                    statusLabel.Text = "?????";
                }
            };

            translateToEnglishButton.Click += delegate { TranslateCurrentText("en", translateToEnglishButton, translateToChineseButton); };
            translateToChineseButton.Click += delegate { TranslateCurrentText("zh-CN", translateToChineseButton, translateToEnglishButton); };
            translationProviderBox.SelectedIndexChanged += delegate
            {
                if (translationProviderBox.SelectedItem == null)
                    return;

                settings.TranslationProvider = Convert.ToString(translationProviderBox.SelectedItem);
                settings.Save();
                statusLabel.Text = "??????? " + settings.TranslationProvider;
            };

            saveButton.Click += delegate
            {
                using (var dialog = new SaveFileDialog())
                {
                    dialog.Title = "????";
                    dialog.FileName = "????_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt";
                    dialog.Filter = "???? (*.txt)|*.txt";
                    dialog.DefaultExt = "txt";
                    dialog.AddExtension = true;
                    if (dialog.ShowDialog() == DialogResult.OK)
                    {
                        File.WriteAllText(dialog.FileName, resultBox.Text);
                        statusLabel.Text = "????" + dialog.FileName;
                    }
                }
            };

            closeButton.Click += delegate { Close(); };
        }

        private void TranslateCurrentText(string targetLanguage, Button primaryButton, Button secondaryButton)
        {
            var sourceText = resultBox.Text;
            if (string.IsNullOrWhiteSpace(sourceText))
            {
                statusLabel.Text = "????????";
                return;
            }

            primaryButton.Enabled = false;
            secondaryButton.Enabled = false;
            statusLabel.Text = targetLanguage == "en" ? "???????..." : "???????...";

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
                    primaryButton.Enabled = true;
                    secondaryButton.Enabled = true;
                    if (error != null)
                    {
                        statusLabel.Text = "?????" + error.Message;
                        return;
                    }

                    resultBox.Text = translatedText;
                    formatRemoved = false;
                    statusLabel.Text = targetLanguage == "en" ? "??????" : "??????";
                });
            });
        }

        private static string RemoveTextFormatting(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            return Regex.Replace(text, @"\s+", " ").Trim();
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

            throw new InvalidOperationException("?????????????" + string.Join("?", errors.ToArray()));
        }

        private static string BaiduTranslate(string text, string targetLanguage, HotkeySettings settings)
        {
            if (settings == null || string.IsNullOrWhiteSpace(settings.BaiduAppId) || string.IsNullOrWhiteSpace(settings.BaiduSecretKey))
                throw new InvalidOperationException("???????????? App ID ????");

            var to = targetLanguage == "en" ? "en" : "zh";
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
                throw new InvalidOperationException("?????? " + Convert.ToString(root["error_code"]) + "?" + (root.ContainsKey("error_msg") ? Convert.ToString(root["error_msg"]) : ""));

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
                RunTesseract(enginePath, inputPath, outputBasePath, language, tessdataDirectory, "tsv");

                var formattedText = ReadTsvOutput(outputTsvPath);
                if (!string.IsNullOrWhiteSpace(formattedText))
                    return formattedText;

                RunTesseract(enginePath, inputPath, outputBasePath, language, tessdataDirectory, string.Empty);

                if (!File.Exists(outputTextPath))
                    return string.Empty;

                return RemoveCjkInterCharacterSpaces(File.ReadAllText(outputTextPath));
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("???? Tesseract OCR???????? tesseract.exe ????????" + ex.Message, ex);
            }
            finally
            {
                TryDelete(inputPath);
                TryDelete(outputTextPath);
                TryDelete(outputTsvPath);
            }
        }

        private static void RunTesseract(string enginePath, string inputPath, string outputBasePath, string language, string tessdataDirectory, string outputFormat)
        {
            var arguments = Quote(inputPath) + " " + Quote(outputBasePath) + " -l " + language + " --oem 1 --psm 6 --dpi 300" + TessdataArgument(tessdataDirectory) + " -c preserve_interword_spaces=1";
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
                    throw new InvalidOperationException("???? OCR ???");

                if (!process.WaitForExit(30000))
                {
                    try { process.Kill(); } catch { }
                    throw new TimeoutException("OCR ??????????????");
                }

                var error = process.StandardError.ReadToEnd();
                if (process.ExitCode != 0)
                    throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "OCR ???????" : error.Trim());
            }
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
            cleaned = Regex.Replace(cleaned, @"(?<=[\u4e00-\u9fff])[\t ]+(?=[??????????])", string.Empty);
            cleaned = Regex.Replace(cleaned, @"(?<=[???])[\t ]+(?=[\u4e00-\u9fff])", string.Empty);
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

        public ImageCanvasControl(Bitmap image)
        {
            this.image = image;
            DoubleBuffered = true;
            BackColor = Color.FromArgb(34, 34, 34);
        }

        public Bitmap Image
        {
            get { return image; }
        }

        public AnnotationMode Mode { get; set; }

        public bool HasRectangleSelection
        {
            get { return hasRectangleSelection; }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.Clear(BackColor);
            e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            e.Graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
            e.Graphics.DrawImage(image, ImageBounds);

            if (Mode == AnnotationMode.Arrow && isDrawing)
                DrawPreviewArrow(e.Graphics, rectangleStartControlPoint, rectangleCurrentControlPoint);
            else if ((Mode == AnnotationMode.Rectangle || Mode == AnnotationMode.Text) && isDrawing)
                DrawPreviewRectangle(e.Graphics, rectangleStartControlPoint, rectangleCurrentControlPoint);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (Mode == AnnotationMode.None || e.Button != MouseButtons.Left)
                return;

            if (Mode != AnnotationMode.Text)
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
            if (!isDrawing)
                return;

            var nextPoint = ToImagePoint(e.Location);
            if (Mode == AnnotationMode.Rectangle || Mode == AnnotationMode.Text || Mode == AnnotationMode.Arrow)
            {
                rectangleCurrentPoint = nextPoint;
                rectangleCurrentControlPoint = e.Location;
                Invalidate();
                return;
            }

            using (var graphics = Graphics.FromImage(image))
            using (var pen = new Pen(Color.Red, Math.Max(3, image.Width / 220)))
            {
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                graphics.DrawLine(pen, lastImagePoint, nextPoint);
            }

            lastImagePoint = nextPoint;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
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

            isDrawing = false;
        }

        public void Undo()
        {
            if (undoStack.Count == 0)
                return;

            image.Dispose();
            image = undoStack.Pop();
            if (undoRectangleFlags.Count > 0 && undoRectangleFlags.Pop())
                PopRectangleSelection();
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

            using (var pen = new Pen(Color.Red, Math.Max(3, image.Width / 220)))
            {
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                graphics.DrawRectangle(pen, x, y, width, height);
            }
        }

        private void DrawArrow(Graphics graphics, Point first, Point second)
        {
            if (Distance(first, second) < 4)
                return;

            using (var pen = CreateArrowPen(Math.Max(3, image.Width / 220)))
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
            using (var foreground = new SolidBrush(Color.Red))
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

            using (var pen = new Pen(Color.Red, 3))
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

            using (var pen = CreateArrowPen(3))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.DrawLine(pen, start, end);
            }
        }

        private static Pen CreateArrowPen(int width)
        {
            var pen = new Pen(Color.Red, width);
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
        Arrow
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
            Text = "???????";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(430, 700);

            var hotkeyTitle = new Label { Text = "?????", Left = 16, Top = 16, Width = 360 };
            ctrlBox = new CheckBox { Text = "Ctrl", Left = 18, Top = 44, Width = 70 };
            shiftBox = new CheckBox { Text = "Shift", Left = 90, Top = 44, Width = 70 };
            altBox = new CheckBox { Text = "Alt", Left = 162, Top = 44, Width = 70 };
            winBox = new CheckBox { Text = "Win", Left = 234, Top = 44, Width = 70 };

            keyBox = new ComboBox { Left = 18, Top = 78, Width = 390, DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (var key in HotkeySettings.AllowedKeys)
                keyBox.Items.Add(key);

            var saveDirectoryLabel = new Label { Text = "??????", Left = 16, Top = 118, Width = 360 };
            saveDirectoryBox = new TextBox { Left = 18, Top = 144, Width = 305 };
            var browseButton = new Button { Text = "??", Left = 330, Top = 142, Width = 78 };
            browseButton.Click += delegate
            {
                using (var dialog = new FolderBrowserDialog())
                {
                    dialog.Description = "????????";
                    dialog.SelectedPath = Directory.Exists(saveDirectoryBox.Text) ? saveDirectoryBox.Text : HotkeySettings.DefaultSaveDirectory();
                    if (dialog.ShowDialog() == DialogResult.OK)
                        saveDirectoryBox.Text = dialog.SelectedPath;
                }
            };

            var ocrTitle = new Label { Text = "OCR ????", Left = 16, Top = 190, Width = 360 };
            ocrEnabledBox = new CheckBox { Text = "?? OCR ???", Left = 18, Top = 216, Width = 180 };
            ocrCtrlBox = new CheckBox { Text = "Ctrl", Left = 18, Top = 250, Width = 70 };
            ocrShiftBox = new CheckBox { Text = "Shift", Left = 90, Top = 250, Width = 70 };
            ocrAltBox = new CheckBox { Text = "Alt", Left = 162, Top = 250, Width = 70 };
            ocrWinBox = new CheckBox { Text = "Win", Left = 234, Top = 250, Width = 70 };

            ocrKeyBox = new ComboBox { Left = 18, Top = 284, Width = 390, DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (var key in HotkeySettings.AllowedKeys)
                ocrKeyBox.Items.Add(key);

            var ocrLanguageLabel = new Label { Text = "????", Left = 16, Top = 324, Width = 360 };
            ocrLanguageBox = new ComboBox { Left = 18, Top = 350, Width = 390, DropDownStyle = ComboBoxStyle.DropDown };
            ocrLanguageBox.Items.Add("chi_sim+eng");
            ocrLanguageBox.Items.Add("eng");
            ocrLanguageBox.Items.Add("chi_sim");
            ocrLanguageBox.Items.Add("chi_tra+eng");

            var ocrEngineLabel = new Label { Text = "Tesseract ???????", Left = 16, Top = 390, Width = 360 };
            ocrEnginePathBox = new TextBox { Left = 18, Top = 416, Width = 305 };
            var ocrBrowseButton = new Button { Text = "??", Left = 330, Top = 414, Width = 78 };
            ocrBrowseButton.Click += delegate
            {
                using (var dialog = new OpenFileDialog())
                {
                    dialog.Title = "?? tesseract.exe";
                    dialog.Filter = "Tesseract (*.exe)|*.exe|???? (*.*)|*.*";
                    if (dialog.ShowDialog() == DialogResult.OK)
                        ocrEnginePathBox.Text = dialog.FileName;
                }
            };

            var translationTitle = new Label { Text = "????", Left = 16, Top = 462, Width = 360 };
            translationProviderBox = new ComboBox { Left = 18, Top = 488, Width = 390, DropDownStyle = ComboBoxStyle.DropDownList };
            translationProviderBox.Items.Add("Google");
            translationProviderBox.Items.Add("Baidu");

            var baiduAppIdLabel = new Label { Text = "???? App ID", Left = 16, Top = 526, Width = 360 };
            baiduAppIdBox = new TextBox { Left = 18, Top = 552, Width = 390 };
            var baiduSecretLabel = new Label { Text = "??????", Left = 16, Top = 584, Width = 360 };
            baiduSecretKeyBox = new TextBox { Left = 18, Top = 610, Width = 390, UseSystemPasswordChar = true };

            saveButton = new Button { Text = "??", Left = 250, Top = 654, Width = 76, DialogResult = DialogResult.OK };
            cancelButton = new Button { Text = "??", Left = 332, Top = 654, Width = 76, DialogResult = DialogResult.Cancel };

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
                    MessageBox.Show("???????????Ctrl?Shift?Alt ? Win?", "?????");
                    DialogResult = DialogResult.None;
                    return;
                }
                if (selected.OcrEnabled && !selected.HasOcrModifier)
                {
                    MessageBox.Show("??????? OCR ????Ctrl?Shift?Alt ? Win?", "?????");
                    DialogResult = DialogResult.None;
                    return;
                }

                try
                {
                    Directory.CreateDirectory(selected.SaveDirectory);
                }
                catch
                {
                    MessageBox.Show("????????????????", "?????");
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
                return OcrEnabled ? DisplayTextFor(OcrModifiers, OcrKeyCode) : "???";
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
        private readonly int hotkeyId;
        private readonly Action onHotkey;
        private bool registered;

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
            return registered;
        }

        public void Unregister()
        {
            if (!registered)
                return;

            UnregisterHotKey(Handle, hotkeyId);
            registered = false;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == hotkeyId)
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
    }
}
