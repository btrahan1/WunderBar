using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Web.WebView2.Core;
using System.IO;

namespace MofoBar
{
    public class FlyoutForm : Form
    {
        private WebView2 _webView;
        private string _currentType = "";

        public FlyoutForm(MofoBackend backend)
        {
            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.Manual;
            this.TopMost = true;
            this.ShowInTaskbar = false;
            this.BackColor = Color.Black;
            this.AllowTransparency = true;
            this.Size = new Size(400, 500);

            _webView = new WebView2();
            _webView.Dock = DockStyle.Fill;
            _webView.DefaultBackgroundColor = Color.Transparent;
            this.Controls.Add(_webView);

            InitializeWebView(backend);
            EnableBlur();
        }

        private async void InitializeWebView(MofoBackend backend)
        {
            await _webView.EnsureCoreWebView2Async();
            string uiPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ui");
            _webView.CoreWebView2.SetVirtualHostNameToFolderMapping("mofobar.local", uiPath, CoreWebView2HostResourceAccessKind.Allow);
            _webView.CoreWebView2.AddHostObjectToScript("mofo", backend);
            _webView.CoreWebView2.Navigate("https://mofobar.local/flyout.html");
        }

        public void ShowAt(string type, Point location)
        {
            _currentType = type;
            this.Location = location;
            this.Show();
            _webView.CoreWebView2.ExecuteScriptAsync($"showPanel('{type}')");
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            this.Hide();
        }

        #region P/Invoke for Blur
        [DllImport("user32.dll")]
        internal static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

        [StructLayout(LayoutKind.Sequential)]
        internal struct WindowCompositionAttributeData { public WindowCompositionAttribute Attribute; public IntPtr Data; public int SizeOfData; }
        internal enum WindowCompositionAttribute { WCA_ACCENT_POLICY = 19 }
        [StructLayout(LayoutKind.Sequential)]
        internal struct AccentPolicy { public AccentState AccentState; public int AccentFlags; public int GradientColor; public int AnimationId; }
        internal enum AccentState { ACCENT_ENABLE_ACRYLICBLURBEHIND = 4 }

        private void EnableBlur()
        {
            var accent = new AccentPolicy { AccentState = AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND, GradientColor = (0 << 24) | (0x000000 & 0xFFFFFF) };
            var accentStructSize = Marshal.SizeOf(accent);
            var accentPtr = Marshal.AllocHGlobal(accentStructSize);
            Marshal.StructureToPtr(accent, accentPtr, false);
            var data = new WindowCompositionAttributeData { Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY, SizeOfData = accentStructSize, Data = accentPtr };
            SetWindowCompositionAttribute(this.Handle, ref data);
            Marshal.FreeHGlobal(accentPtr);
        }
        #endregion
    }
}
