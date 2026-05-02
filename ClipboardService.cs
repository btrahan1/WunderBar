using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;

namespace MofoBar
{
    public class ClipItem
    {
        public string Text { get; set; } = "";
        public string ImageBase64 { get; set; } = "";
        public DateTime Timestamp { get; set; }
        public bool IsPinned { get; set; }
    }

    public class ClipboardService : NativeWindow
    {
        private List<ClipItem> _history = new List<ClipItem>();
        private const int MaxHistory = 10;
        public event Action HistoryChanged;
        private string _storagePath;

        private const int WM_CLIPBOARDUPDATE = 0x031D;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool AddClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

        public ClipboardService()
        {
            _storagePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "clipboard.json");
            LoadHistory();
            CreateHandle(new CreateParams());
            AddClipboardFormatListener(Handle);
        }

        private void LoadHistory()
        {
            try
            {
                if (File.Exists(_storagePath))
                {
                    string json = File.ReadAllText(_storagePath);
                    _history = JsonSerializer.Deserialize<List<ClipItem>>(json) ?? new List<ClipItem>();
                }
            }
            catch { }
        }

        private void SaveHistory()
        {
            try
            {
                string json = JsonSerializer.Serialize(_history);
                File.WriteAllText(_storagePath, json);
            }
            catch { }
        }

        public List<ClipItem> GetHistory() => _history.OrderByDescending(c => c.IsPinned).ThenByDescending(c => c.Timestamp).ToList();

        public void PinClip(string text, string imageBase64, bool pin)
        {
            var item = _history.FirstOrDefault(c => c.Text == text && (string.IsNullOrEmpty(imageBase64) || c.ImageBase64 == imageBase64));
            if (item != null)
            {
                item.IsPinned = pin;
                SaveHistory();
                HistoryChanged?.Invoke();
            }
        }

        public void RemoveClip(string text, string imageBase64)
        {
            var item = _history.FirstOrDefault(c => c.Text == text && (string.IsNullOrEmpty(imageBase64) || c.ImageBase64 == imageBase64));
            if (item != null)
            {
                _history.Remove(item);
                SaveHistory();
                HistoryChanged?.Invoke();
            }
        }

        public void ClearAll()
        {
            _history.Clear();
            SaveHistory();
            HistoryChanged?.Invoke();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_CLIPBOARDUPDATE)
            {
                OnClipboardChanged();
            }
            base.WndProc(ref m);
        }

        private void OnClipboardChanged()
        {
            try
            {
                if (Clipboard.ContainsText())
                {
                    string text = Clipboard.GetText();
                    if (string.IsNullOrWhiteSpace(text)) return;

                    var existing = _history.FirstOrDefault(c => c.Text == text && string.IsNullOrEmpty(c.ImageBase64));
                    if (existing != null)
                    {
                        existing.Timestamp = DateTime.Now;
                    }
                    else
                    {
                        _history.Add(new ClipItem { Text = text, Timestamp = DateTime.Now });
                    }
                }
                else if (Clipboard.ContainsImage())
                {
                    using (var img = Clipboard.GetImage())
                    {
                        if (img != null)
                        {
                            using (var ms = new MemoryStream())
                            {
                                img.Save(ms, ImageFormat.Png);
                                string base64 = Convert.ToBase64String(ms.ToArray());
                                
                                var existing = _history.FirstOrDefault(c => c.ImageBase64 == base64);
                                if (existing != null)
                                {
                                    existing.Timestamp = DateTime.Now;
                                }
                                else
                                {
                                    _history.Add(new ClipItem { ImageBase64 = base64, Text = "[Image]", Timestamp = DateTime.Now });
                                }
                            }
                        }
                    }
                }
                else { return; }

                // Trim unpinned items
                var pinned = _history.Where(c => c.IsPinned).ToList();
                var unpinned = _history.Where(c => !c.IsPinned).OrderByDescending(c => c.Timestamp).Take(MaxHistory).ToList();
                _history = pinned.Concat(unpinned).ToList();

                SaveHistory();
                HistoryChanged?.Invoke();
            }
            catch { }
        }

        public void SetClipboardImage(string base64)
        {
            try
            {
                byte[] bytes = Convert.FromBase64String(base64);
                using (var ms = new MemoryStream(bytes))
                {
                    using (var img = Image.FromStream(ms))
                    {
                        Clipboard.SetImage(img);
                    }
                }
            }
            catch { }
        }

        public void Dispose()
        {
            try
            {
                RemoveClipboardFormatListener(Handle);
                DestroyHandle();
            }
            catch { }
        }
    }
}
