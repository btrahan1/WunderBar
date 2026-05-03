using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Web.WebView2.Core;
using System.IO;

namespace WunderBar
{
    public partial class MainForm : Form
    {
        private WebView2? _webView;
        private WunderBackend _backend;
        private FlyoutForm? _flyout;
        public string CurrentDockSide = "bottom";
        public bool IsSystemMoving = false;
        public WebView2? GetWebView() => _webView;
        public FlyoutForm? GetFlyout() => _flyout;

        public MainForm()
        {
            InitializeForm();
            InitializeWebView();
            this.LocationChanged += MainForm_LocationChanged;
        }

        private System.Windows.Forms.Timer? _snapTimer;
        private void MainForm_LocationChanged(object? sender, EventArgs e)
        {
            if (IsSystemMoving || this.IsDisposed) return;

            try
            {
                // Update timer to check for orientation change after a short pause
                if (_snapTimer != null) _snapTimer.Stop();
                else
                {
                    _snapTimer = new System.Windows.Forms.Timer();
                    _snapTimer.Interval = 500;
                    _snapTimer.Tick += (s, ev) => 
                    { 
                        if (!this.IsDisposed)
                        {
                            _snapTimer.Stop(); 
                            UpdateOrientation(); 
                        }
                    };
                }
                _snapTimer.Start();
            }
            catch { /* Ignore issues during rapid movement */ }
        }

        private void UpdateOrientation()
        {
            if (this.IsDisposed) return;

            try
            {
                var screen = Screen.FromPoint(this.Location);
                if (screen == null) return;

                int threshold = 150; 
                string oldSide = CurrentDockSide;

                if (this.Left < screen.Bounds.Left + threshold) CurrentDockSide = "left";
                else if (this.Right > screen.Bounds.Right - threshold) CurrentDockSide = "right";
                else if (this.Top < screen.Bounds.Top + threshold) CurrentDockSide = "top";
                else if (this.Bottom > screen.Bounds.Bottom - threshold) CurrentDockSide = "bottom";
                else CurrentDockSide = "free";
                
                if (oldSide != CurrentDockSide)
                {
                    SetOrientation(CurrentDockSide);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Orientation update error: " + ex.Message);
            }
        }

        private void SetOrientation(string side)
        {
            if (_webView != null && _webView.CoreWebView2 != null && !this.IsDisposed)
            {
                _webView.CoreWebView2.ExecuteScriptAsync($"setOrientation('{side}')");
            }
        }

        private void InitializeForm()
        {
            this.Text = "WunderBar";
            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.Manual;
            this.TopMost = true;
            this.ShowInTaskbar = true; // Show in taskbar so we can see the icon

            try
            {
                string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WunderBar.ico");
                if (File.Exists(iconPath))
                {
                    this.Icon = new Icon(iconPath);
                }
            }
            catch { }
            
            // Initial size and position (bottom dock style)
            int screenWidth = Screen.PrimaryScreen?.Bounds.Width ?? 1920;
            int screenHeight = Screen.PrimaryScreen?.Bounds.Height ?? 1080;
            int barHeight = 80;
            int barWidth = 800;

            this.Size = new Size(barWidth, barHeight);
            this.Location = new Point((screenWidth - barWidth) / 2, screenHeight - barHeight - 20);

            // Enable transparency support
            this.BackColor = Color.Black;
            this.AllowTransparency = true;

            EnableBlur();
        }

        private async void InitializeWebView()
        {
            _webView = new WebView2();
            _webView.Dock = DockStyle.Fill;
            this.Controls.Add(_webView);

            _webView.DefaultBackgroundColor = Color.Transparent;

            await _webView.EnsureCoreWebView2Async();

            // Map a virtual host to the UI folder to avoid file:// security issues
            string uiPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ui");
            _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "wunderbar.local", uiPath, CoreWebView2HostResourceAccessKind.Allow);
            _webView.CoreWebView2.Navigate("https://wunderbar.local/index.html");
            _backend = new WunderBackend(this);
            _webView.CoreWebView2.AddHostObjectToScript("wunder", _backend);
            _webView.CoreWebView2.Settings.AreDevToolsEnabled = true;

            _flyout = new FlyoutForm(_backend);
        }

        public void ShowFlyout(string type, int x, int y)
        {
            this.Invoke(() => {
                if (_flyout != null)
                {
                    int width = type == "vitals" ? 600 : 400;
                    int height = 500;
                    _flyout.Width = width;
                    _flyout.Height = height;

                    Point clickPoint = this.PointToScreen(new Point(x, y));
                    Screen screen = Screen.FromPoint(clickPoint);
                    
                    int flyoutX = clickPoint.X - (width / 2);
                    int flyoutY = clickPoint.Y;

                    // Adjust based on dock side
                    if (CurrentDockSide == "bottom")
                    {
                        flyoutY = this.Top - height - 10;
                    }
                    else if (CurrentDockSide == "top")
                    {
                        flyoutY = this.Bottom + 10;
                    }
                    else if (CurrentDockSide == "left")
                    {
                        flyoutX = this.Right + 10;
                        flyoutY = clickPoint.Y - (height / 2);
                    }
                    else if (CurrentDockSide == "right")
                    {
                        flyoutX = this.Left - width - 10;
                        flyoutY = clickPoint.Y - (height / 2);
                    }

                    // Boundary checking (Clamp to WorkingArea)
                    flyoutX = Math.Max(screen.WorkingArea.Left + 10, Math.Min(flyoutX, screen.WorkingArea.Right - width - 10));
                    flyoutY = Math.Max(screen.WorkingArea.Top + 10, Math.Min(flyoutY, screen.WorkingArea.Bottom - height - 10));

                    _flyout.ShowAt(type, new Point(flyoutX, flyoutY));
                }
            });
        }

        #region P/Invoke for Blur

        [DllImport("user32.dll")]
        internal static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

        [StructLayout(LayoutKind.Sequential)]
        internal struct WindowCompositionAttributeData
        {
            public WindowCompositionAttribute Attribute;
            public IntPtr Data;
            public int SizeOfData;
        }

        internal enum WindowCompositionAttribute
        {
            WCA_ACCENT_POLICY = 19
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct AccentPolicy
        {
            public AccentState AccentState;
            public int AccentFlags;
            public int GradientColor;
            public int AnimationId;
        }

        internal enum AccentState
        {
            ACCENT_DISABLED = 0,
            ACCENT_ENABLE_GRADIENT = 1,
            ACCENT_ENABLE_TRANSPARENTGRADIENT = 2,
            ACCENT_ENABLE_BLURBEHIND = 3,
            ACCENT_ENABLE_ACRYLICBLURBEHIND = 4,
            ACCENT_INVALID_STATE = 5
        }

        private void EnableBlur()
        {
            var accent = new AccentPolicy();
            accent.AccentState = AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND;
            accent.GradientColor = (0 << 24) | (0x000000 & 0xFFFFFF); // Semi-transparent black

            var accentStructSize = Marshal.SizeOf(accent);
            var accentPtr = Marshal.AllocHGlobal(accentStructSize);
            Marshal.StructureToPtr(accent, accentPtr, false);

            var data = new WindowCompositionAttributeData();
            data.Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY;
            data.SizeOfData = accentStructSize;
            data.Data = accentPtr;

            SetWindowCompositionAttribute(this.Handle, ref data);

            Marshal.FreeHGlobal(accentPtr);
        }

        #endregion
    }
}
