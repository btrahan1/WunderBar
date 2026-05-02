using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Drawing;
using System.Drawing.Imaging;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using System.Linq;

namespace MofoBar
{
    [ComVisible(true)]
    public class MofoBackend
    {
        private readonly MainForm _form;
        private readonly Dictionary<string, string> _iconCache = new();
        private readonly MftScanner _scanner = new MftScanner();
        private readonly ClipboardService _clipboard = new ClipboardService();
        private readonly MetricsService _metrics = new MetricsService();
        private readonly ProcessService _processes = new ProcessService();

        public MofoBackend(MainForm form)
        {
            _form = form;
            _clipboard.HistoryChanged += () => {
                _form.Invoke(() => {
                    if (!_form.IsDisposed)
                        _form.GetWebView().CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "clipboard", data = _clipboard.GetHistory() }));
                });
            };

            if (_scanner.IsAdmin())
            {
                _scanner.StartBackgroundIndexing();
            }
        }

        public string SearchFiles(string query) => JsonSerializer.Serialize(_scanner.Search(query));
        public string GetClipboardHistory() => JsonSerializer.Serialize(_clipboard.GetHistory());
        public async Task<string> GetSizeTree(string path) => await Task.Run(() => JsonSerializer.Serialize(_scanner.GetFolderTree(path)));
        public bool IsIndexing() => _scanner.IsIndexing;
        public bool IsCalculatingSizes() => _scanner.IsCalculatingSizes;
        public bool IsAdmin() => _scanner.IsAdmin();
        public int GetIndexedFileCount() => _scanner.FileCount;
        
        public void PinClip(string text, string imageBase64, bool pin)
        {
            _clipboard.PinClip(text, imageBase64, pin);
        }

        public void RemoveClip(string text, string imageBase64)
        {
            _clipboard.RemoveClip(text, imageBase64);
        }

        public void ClearClipboard()
        {
            _clipboard.ClearAll();
        }

        public string GetVitals() => JsonSerializer.Serialize(_metrics.GetVitals());
        public string GetProcesses() => JsonSerializer.Serialize(_processes.GetProcesses());
        public void KillProcess(int id) => _processes.KillProcess(id);
        public void SuspendProcess(int id) => _processes.SuspendProcess(id);
        public void ResumeProcess(int id) => _processes.ResumeProcess(id);

        public void SetClipboard(string text) => _form.Invoke(() => { if (!string.IsNullOrEmpty(text)) Clipboard.SetText(text); });
        public void SetClipboardImage(string imageBase64) => _clipboard.SetClipboardImage(imageBase64);

        public void StartDrag() => _form.Invoke(() => { ReleaseCapture(); SendMessage(_form.Handle, 0xA1, 0x2, 0); });

        public void ShowFlyout(string type, int x, int y) => _form.ShowFlyout(type, x, y);

        public void ShowFileResultMenu(string path, int x, int y)
        {
            _form.Invoke(() => {
                var menu = new ContextMenuStrip();
                var openFolder = menu.Items.Add("Open containing folder");
                openFolder.Click += (s, e) => {
                    try { Process.Start("explorer.exe", $"/select,\"{path}\""); }
                    catch { }
                };

                var copyPath = menu.Items.Add("Copy full path");
                copyPath.Click += (s, e) => {
                    try { Clipboard.SetText(path); }
                    catch { }
                };

                // Position relative to flyout window
                // Actually easier to just use cursor position
                menu.Show(Cursor.Position);
            });
        }

        public void ShowClipContextMenu(string text, string imageBase64, int x, int y)
        {
            _form.Invoke(() => {
                var menu = new ContextMenuStrip();
                var deleteItem = menu.Items.Add("Delete");
                deleteItem.Click += (s, e) => {
                    _clipboard.RemoveClip(text, imageBase64);
                };

                menu.Show(Cursor.Position);
            });
        }

        public void SimulateWinKey()
        {
            SendKeys.SendWait("^{ESC}"); // Ctrl+Esc is the standard software fallback for the Win key
            // Or use keybd_event for more direct LWIN simulation
            // keybd_event(0x5B, 0, 0, 0); // LWIN Down
            // keybd_event(0x5B, 0, 2, 0); // LWIN Up
        }

        [DllImport("shell32.dll", SetLastError = true)]
        private static extern int SHGetPropertyStoreForWindow(IntPtr hwnd, ref Guid iid, out IPropertyStore propertyStore);

        [Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPropertyStore
        {
            int GetCount(out uint cProps);
            int GetAt(uint iProp, out PropertyKey pkey);
            int GetValue(ref PropertyKey pkey, out PropVariant pv);
            int SetValue(ref PropertyKey pkey, ref PropVariant pv);
            int Commit();
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PropertyKey
        {
            public Guid fmtid;
            public uint pid;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct PropVariant
        {
            [FieldOffset(0)] public ushort vt;
            [FieldOffset(8)] public IntPtr ptr;
            
            public string GetString()
            {
                if (vt == 31) // VT_LPWSTR
                    return Marshal.PtrToStringUni(ptr) ?? "";
                return "";
            }
        }

        private static PropertyKey PKEY_AppUserModel_ID = new PropertyKey { fmtid = new Guid("9F4C7803-2605-4A15-8918-01052406111A"), pid = 5 };

        private string GetAppIDFromWindow(IntPtr hWnd)
        {
            try
            {
                Guid guid = new Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"); // IID_IPropertyStore
                if (SHGetPropertyStoreForWindow(hWnd, ref guid, out IPropertyStore propStore) == 0)
                {
                    propStore.GetValue(ref PKEY_AppUserModel_ID, out PropVariant pv);
                    string id = pv.GetString();
                    if (!string.IsNullOrEmpty(id)) return id;
                }
            }
            catch { }
            return "";
        }

        private string CalculateAppID(long handle, string exePath)
        {
            // First try the window's own AppID
            string id = GetAppIDFromWindow(new IntPtr(handle));
            if (!string.IsNullOrEmpty(id)) return id;

            // Fallback to known list for common apps
            var knownAppIDs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
                { "ssms.exe", "f01b4d95cf55d32a" },
                { "notepad.exe", "9b9cdc69c1c24e2b" },
                { "devenv.exe", "308050b165275bc2" },
                { "notepad++.exe", "6502246766468755" },
                { "code.exe", "da1233010b9da466" },
                { "chrome.exe", "5d696d5217514913" },
                { "msedge.exe", "275990261394f923" }
            };

            string fileName = Path.GetFileName(exePath);
            if (knownAppIDs.TryGetValue(fileName, out var knownId)) return knownId;

            return "";
        }

        public void ExitApp()
        {
            _form.Invoke(() => Application.Exit());
        }

        public void LaunchApp(string path)
        {
            try
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error launching {path}: {ex.Message}");
            }
        }

        public string GetOpenWindows()
        {
            var apps = new List<AppInfo>();
            try
            {
                foreach (var proc in Process.GetProcesses())
                {
                    try
                    {
                        IntPtr hWnd = proc.MainWindowHandle;
                        if (hWnd != IntPtr.Zero)
                        {
                            string title = proc.MainWindowTitle;
                            if (!string.IsNullOrEmpty(title) && title != "MofoBar")
                            {
                                apps.Add(new AppInfo 
                                { 
                                    Title = title, 
                                    Handle = hWnd.ToInt64(),
                                    Icon = GetWindowIconBase64(hWnd),
                                    ExePath = proc.MainModule?.FileName ?? ""
                                });
                            }
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex) { Console.WriteLine(ex.Message); }
            return JsonSerializer.Serialize(apps);
        }

        public string GetPinnedApps()
        {
            var pinned = new List<PinnedApp>();
            try
            {
                string[] paths = {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar")
                };

                foreach (var path in paths)
                {
                    if (Directory.Exists(path))
                    {
                        foreach (var file in Directory.GetFiles(path, "*.lnk"))
                        {
                            pinned.Add(new PinnedApp 
                            { 
                                Name = Path.GetFileNameWithoutExtension(file), 
                                Path = file,
                                Icon = GetFileIconBase64(file),
                                ExePath = file // For pinned, the path is the .lnk itself for now
                            });
                        }
                    }
                }
            }
            catch (Exception ex) { Console.WriteLine(ex.Message); }
            return JsonSerializer.Serialize(pinned);
        }

        public void FocusWindow(long handle)
        {
            IntPtr hWnd = new IntPtr(handle);
            if (IsIconic(hWnd)) ShowWindow(hWnd, SW_RESTORE);
            SetForegroundWindow(hWnd);
        }

        public void ShowAppMenu(long handle, string title, bool isPinned, string exePath, int x, int y)
        {
            _form.Invoke(() => {
                var menu = new ContextMenuStrip();
                menu.ShowImageMargin = true;

                var titleItem = new ToolStripMenuItem(title);
                titleItem.Enabled = false;
                titleItem.Font = new Font(titleItem.Font, FontStyle.Bold);
                menu.Items.Add(titleItem);
                menu.Items.Add(new ToolStripSeparator());

                // Keywords to help filter
                var filterMap = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase) {
                    { "Notepad", new[] { ".txt", ".log", ".json", ".ini", ".md" } },
                    { "Visual Studio", new[] { ".sln", ".csproj", ".cs", ".xaml", ".js", ".ts" } },
                    { "SQL", new[] { ".sql" } },
                    { "Chrome", new[] { ".html", ".htm" } },
                    { "Edge", new[] { ".html", ".htm" } },
                    { "Code", new[] { ".ts", ".js", ".cs", ".json", ".md" } }
                };

                string[]? relevantExts = null;
                foreach (var kvp in filterMap)
                {
                    if (title.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
                    {
                        relevantExts = kvp.Value;
                        break;
                    }
                }

                // Targeted Jump List Extraction
                bool targetedSuccess = false;
                try
                {
                    string appID = CalculateAppID(handle, exePath);
                    if (!string.IsNullOrEmpty(appID))
                    {
                        string autoDestPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), 
                            $@"Microsoft\Windows\Recent\AutomaticDestinations\{appID}.automaticDestinations-ms");

                        if (File.Exists(autoDestPath))
                        {
                            byte[] data = File.ReadAllBytes(autoDestPath);
                            string content = Encoding.Unicode.GetString(data);
                            var pathRegex = new System.Text.RegularExpressions.Regex(@"[a-zA-Z]:\\[^:?*""<>|]+", System.Text.RegularExpressions.RegexOptions.Compiled);
                            var matches = pathRegex.Matches(content);

                            int count = 0;
                            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                            foreach (System.Text.RegularExpressions.Match match in matches)
                            {
                                if (count >= 10) break;
                                string pathCandidate = match.Value.TrimEnd('\0');
                                
                                if (pathCandidate.Contains(".") && !seenPaths.Contains(pathCandidate))
                                {
                                    bool isRelevant = true;
                                    if (relevantExts != null)
                                    {
                                        isRelevant = false;
                                        foreach (var ext in relevantExts)
                                            if (pathCandidate.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) { isRelevant = true; break; }
                                    }

                                    if (isRelevant)
                                    {
                                        string name = Path.GetFileName(pathCandidate);
                                        var item = menu.Items.Add(name);
                                        item.Click += (s, e) => {
                                            try { Process.Start(new ProcessStartInfo(pathCandidate) { UseShellExecute = true }); }
                                            catch { }
                                        };
                                        seenPaths.Add(pathCandidate);
                                        count++;
                                        targetedSuccess = true;
                                    }
                                }
                            }
                            if (count > 0) menu.Items.Add(new ToolStripSeparator());
                        }
                    }
                }
                catch { }

                // Fallback to System Recent Items if Targeted scan failed
                if (!targetedSuccess)
                {
                    try
                    {
                        string recentPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Recent");
                        if (Directory.Exists(recentPath))
                        {
                            var files = Directory.GetFiles(recentPath, "*.lnk")
                                .Select(f => new FileInfo(f))
                                .OrderByDescending(f => f.LastWriteTime)
                                .Take(100);

                            int count = 0;
                            foreach (var file in files)
                            {
                                if (count >= 5) break;
                                string name = Path.GetFileNameWithoutExtension(file.Name);

                                if (name.StartsWith("ms-", StringComparison.OrdinalIgnoreCase)) continue;

                                bool isRelevant = false;
                                if (relevantExts != null)
                                {
                                    foreach (var ext in relevantExts)
                                        if (name.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) { isRelevant = true; break; }
                                }
                                else { isRelevant = true; }

                                if (isRelevant)
                                {
                                    var recentItem = menu.Items.Add(name);
                                    recentItem.Click += (s, e) => LaunchApp(file.FullName);
                                    count++;
                                }
                            }
                            if (count > 0) menu.Items.Add(new ToolStripSeparator());
                        }
                    }
                    catch { }
                }

                if (!isPinned && handle != 0)
                {
                    var closeItem = menu.Items.Add("Close window");
                    closeItem.Click += (s, e) => CloseWindow(handle);
                }

                var pinItem = menu.Items.Add(isPinned ? "Unpin from taskbar" : "Pin to taskbar");
                pinItem.Click += (s, e) => MessageBox.Show("Pinning coming soon!");

                Point screenPoint = _form.PointToScreen(new Point(x, y));
                _form.Activate();
                SetForegroundWindow(_form.Handle);
                menu.Show(screenPoint);
            });
        }
        
        // Helper to get target of a link (simplified)
        private string GetLnkTarget(string lnkPath)
        {
            // This usually requires a COM reference to Shell32 or WshShell
            // For now we'll just use the link name heuristic
            return "";
        }

        public void ShowGlobalMenu(int x, int y)
        {
            _form.Invoke(() => {
                var menu = new ContextMenuStrip();
                var exitItem = menu.Items.Add("Exit MofoBar");
                exitItem.Click += (s, e) => ExitApp();
                
                Point screenPoint = _form.PointToScreen(new Point(x, y));
                _form.Activate();
                SetForegroundWindow(_form.Handle);
                menu.Show(screenPoint);
            });
        }

        public void CloseWindow(long handle)
        {
            IntPtr hWnd = new IntPtr(handle);
            PostMessage(hWnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        }

        private const int WM_CLOSE = 0x0010;
        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        private string GetFileIconBase64(string path)
        {
            if (_iconCache.TryGetValue(path, out var cached)) return cached;

            try
            {
                using var icon = Icon.ExtractAssociatedIcon(path);
                if (icon == null) return "";
                using var bmp = icon.ToBitmap();
                string base64 = BitmapToBase64(bmp);
                _iconCache[path] = base64;
                return base64;
            }
            catch { return ""; }
        }

        private string GetWindowIconBase64(IntPtr hWnd)
        {
            try
            {
                IntPtr hIcon = SendMessage(hWnd, WM_GETICON, (IntPtr)ICON_BIG, IntPtr.Zero);
                if (hIcon == IntPtr.Zero) hIcon = GetClassLongPtr(hWnd, GCLP_HICON);
                
                if (hIcon != IntPtr.Zero)
                {
                    using var icon = Icon.FromHandle(hIcon);
                    using var bmp = icon.ToBitmap();
                    return BitmapToBase64(bmp);
                }
            }
            catch { }
            return "";
        }

        private string BitmapToBase64(Bitmap bmp)
        {
            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            return Convert.ToBase64String(ms.ToArray());
        }

        // P/Invoke
        private const int SW_RESTORE = 9;
        private const int WM_GETICON = 0x7F;
        private const int ICON_BIG = 1;
        private const int GCLP_HICON = -14;

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_APPWINDOW = 0x00040000;
        private const int GW_OWNER = 4;

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();
        [DllImport("user32.dll")]
        public static extern IntPtr SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);

        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HTCAPTION = 0x2;

        [DllImport("user32.dll", EntryPoint = "GetClassLongPtr")]
        private static extern IntPtr GetClassLongPtr(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hWnd, int uCmd);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

        public void ResizeWindow(int width, int height)
        {
            if (_form.IsDisposed) return;
            _form.Invoke(() => {
                try
                {
                    if (!_form.IsDisposed)
                    {
                        _form.IsSystemMoving = true;
                        _form.Width = width;
                        _form.Height = height;
                        _form.IsSystemMoving = false;
                    }
                }
                catch { }
            });
        }

        // P/Invoke for window enumeration
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern int GetWindowTextLength(IntPtr hWnd);
    }

    public class AppInfo
    {
        public string Title { get; set; } = "";
        public long Handle { get; set; }
        public string Icon { get; set; } = "";
        public string ExePath { get; set; } = "";
    }

    public class PinnedApp
    {
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public string Icon { get; set; } = "";
        public string ExePath { get; set; } = "";
    }
}
