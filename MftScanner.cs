using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace MofoBar
{
    public class FileItem
    {
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public bool IsFolder { get; set; }
        public long Size { get; set; }
        public int FileCount { get; set; }
        public int FolderCount { get; set; }
        public List<FileItem> Children { get; set; } = new List<FileItem>();
    }

    public class MftScanner
    {
        private Dictionary<ulong, (string Name, ulong Parent, bool IsFolder)> _idMap = new();
        private Dictionary<ulong, List<ulong>> _childrenMap = new();
        private Dictionary<ulong, long> _sizeMap = new();
        private Dictionary<ulong, (int Files, int Folders)> _countMap = new();
        private bool _isIndexing = false;
        private bool _isCalculatingSizes = false;
        private const int BufferSize = 65536;

        public bool IsIndexing => _isIndexing;
        public bool IsCalculatingSizes => _isCalculatingSizes;
        public int FileCount => _idMap.Count;
        public int SizedFileCount => _sizeMap.Count;

        public bool IsAdmin()
        {
            try
            {
                using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                var principal = new System.Security.Principal.WindowsPrincipal(identity);
                return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        public void StartBackgroundIndexing()
        {
            Task.Run(() => {
                try
                {
                    _isIndexing = true;
                    ScanDrive("C");
                    _isIndexing = false;

                    _isCalculatingSizes = true;
                    CalculateAllSizes();
                    _isCalculatingSizes = false;
                    
                    Console.WriteLine($"Full indexing and sizing complete. {_idMap.Count} entries.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Indexing failed: {ex.Message}");
                    _isIndexing = false;
                    _isCalculatingSizes = false;
                }
            });
        }

        private void CalculateAllSizes()
        {
            // Phase 1: Get individual file sizes
            var files = _idMap.Where(kvp => !kvp.Value.IsFolder).ToList();
            
            Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = 8 }, kvp => {
                string path = ReconstructPath(kvp.Key, _idMap, "C:");
                long size = GetFileSize(path);
                lock (_sizeMap) { _sizeMap[kvp.Key] = size; }
            });

            // Phase 2: Aggregate sizes and counts up the tree iteratively (the "climb")
            // This is more robust for MFT-based structures than recursion.
            foreach (var kvp in _idMap)
            {
                _sizeMap.TryGetValue(kvp.Key, out long size);
                bool isFolder = kvp.Value.IsFolder;

                ulong parent = kvp.Value.Parent;
                int depth = 0;
                while (_idMap.TryGetValue(parent, out var parentInfo) && depth < 1000)
                {
                    // Update size
                    _sizeMap[parent] = (_sizeMap.TryGetValue(parent, out long s) ? s : 0) + size;
                    
                    // Update counts
                    var c = _countMap.TryGetValue(parent, out var existing) ? existing : (Files: 0, Folders: 0);
                    if (isFolder && depth == 0) c.Folders++;
                    else if (!isFolder) c.Files++;
                    else if (isFolder) c.Folders++;
                    _countMap[parent] = c;

                    if (parent == parentInfo.Parent) break;
                    parent = parentInfo.Parent;
                    depth++;
                }
            }
        }

        public List<FileItem> Search(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return new List<FileItem>();
            
            var results = new List<FileItem>();
            var regex = query.Contains("*") ? new System.Text.RegularExpressions.Regex("^" + System.Text.RegularExpressions.Regex.Escape(query).Replace("\\*", ".*") + "$", System.Text.RegularExpressions.RegexOptions.IgnoreCase) : null;

            int count = 0;
            foreach (var kvp in _idMap)
            {
                if (count >= 50) break;
                if (regex != null ? regex.IsMatch(kvp.Value.Name) : kvp.Value.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    results.Add(new FileItem { 
                        Name = kvp.Value.Name, 
                        Path = ReconstructPath(kvp.Key, _idMap, "C:"), 
                        IsFolder = kvp.Value.IsFolder,
                        Size = _sizeMap.TryGetValue(kvp.Key, out long sz) ? sz : 0
                    });
                    count++;
                }
            }
            return results;
        }

        public FileItem GetFolderTree(string rootPath)
        {
            if (string.IsNullOrEmpty(rootPath) || rootPath.Equals("C:", StringComparison.OrdinalIgnoreCase))
                rootPath = "C:\\";
            
            rootPath = rootPath.TrimEnd('\\');
            if (rootPath.Length == 2 && rootPath[1] == ':') rootPath += "\\";
            
            ulong rootFrn = GetFrn(rootPath);
            if (rootFrn == 0 && rootPath.Length <= 3) rootFrn = 5;

            var root = new FileItem { Name = Path.GetFileName(rootPath) == "" ? rootPath : Path.GetFileName(rootPath), Path = rootPath, IsFolder = true };
            
            if (_childrenMap.TryGetValue(rootFrn, out var children))
            {
                foreach (var childFrn in children)
                {
                    if (!_idMap.TryGetValue(childFrn, out var info)) continue;
                    
                    _sizeMap.TryGetValue(childFrn, out long size);
                    _countMap.TryGetValue(childFrn, out var counts);

                    root.Children.Add(new FileItem {
                        Name = info.Name,
                        Path = Path.Combine(root.Path, info.Name),
                        IsFolder = info.IsFolder,
                        Size = size,
                        FileCount = counts.Files,
                        FolderCount = counts.Folders
                    });
                }
            }

            root.Children = root.Children
                .OrderByDescending(c => c.IsFolder)
                .ThenByDescending(c => c.Size)
                .ToList();
            return root;
        }

        private void ScanDrive(string driveLetter)
        {
            var drivePath = $@"\\.\{driveLetter}:";
            IntPtr hDrive = NativeMethods.CreateFile(drivePath, NativeMethods.GENERIC_READ, NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE, IntPtr.Zero, NativeMethods.OPEN_EXISTING, 0, IntPtr.Zero);
            if (hDrive == new IntPtr(-1)) return;

            try
            {
                if (!NativeMethods.DeviceIoControl(hDrive, NativeMethods.FSCTL_QUERY_USN_JOURNAL, IntPtr.Zero, 0, out var journalData, Marshal.SizeOf<NativeMethods.USN_JOURNAL_DATA>(), out _, IntPtr.Zero)) return;
                var enumData = new NativeMethods.MFT_ENUM_DATA { StartFileReferenceNumber = 0, LowUsn = 0, HighUsn = journalData.NextUsn };
                IntPtr pOutBuffer = Marshal.AllocHGlobal(BufferSize);

                try
                {
                    bool success;
                    do
                    {
                        success = NativeMethods.DeviceIoControl(hDrive, NativeMethods.FSCTL_ENUM_USN_DATA, ref enumData, Marshal.SizeOf(enumData), pOutBuffer, BufferSize, out uint bytesReturned, IntPtr.Zero);
                        if (success && bytesReturned > 8)
                        {
                            long offset = 8;
                            while (offset < bytesReturned)
                            {
                                var record = Marshal.PtrToStructure<NativeMethods.USN_RECORD_V2>(pOutBuffer + (int)offset);
                                string name = Marshal.PtrToStringUni(pOutBuffer + (int)offset + record.FileNameOffset, record.FileNameLength / 2);
                                ulong frn = record.FileReferenceNumber;
                                ulong parent = record.ParentFileReferenceNumber;
                                bool isDir = (record.FileAttributes & 0x10) == 0x10;

                                _idMap[frn] = (name, parent, isDir);
                                if (!_childrenMap.TryGetValue(parent, out var list)) { list = new List<ulong>(); _childrenMap[parent] = list; }
                                list.Add(frn);

                                offset += record.RecordLength;
                            }
                            enumData.StartFileReferenceNumber = (ulong)Marshal.ReadInt64(pOutBuffer);
                        }
                    } while (success);
                }
                finally { Marshal.FreeHGlobal(pOutBuffer); }
            }
            finally { NativeMethods.CloseHandle(hDrive); }
        }

        private static ulong GetFrn(string path)
        {
            IntPtr h = NativeMethods.CreateFile(path, 0, NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE, IntPtr.Zero, NativeMethods.OPEN_EXISTING, 0x02000000 /* BACKUP_SEMANTICS */, IntPtr.Zero);
            if (h == new IntPtr(-1)) return 0;
            try
            {
                if (NativeMethods.GetFileInformationByHandle(h, out var info))
                    return ((ulong)info.nFileIndexHigh << 32) | info.nFileIndexLow;
            }
            finally { NativeMethods.CloseHandle(h); }
            return 0;
        }

        private static long GetFileSize(string path)
        {
            var data = new NativeMethods.WIN32_FILE_ATTRIBUTE_DATA();
            if (NativeMethods.GetFileAttributesEx(path, 0, ref data))
                return ((long)data.nFileSizeHigh << 32) | data.nFileSizeLow;
            return 0;
        }

        private string ReconstructPath(ulong frn, Dictionary<ulong, (string Name, ulong Parent, bool IsFolder)> map, string drive)
        {
            var parts = new Stack<string>();
            var currentFrn = frn;
            int depth = 0;
            while (map.TryGetValue(currentFrn, out var info) && depth < 100)
            {
                parts.Push(info.Name);
                if (currentFrn == info.Parent) break;
                currentFrn = info.Parent;
                depth++;
            }
            return parts.Count == 0 ? drive + "\\" : Path.Combine(drive + "\\", string.Join("\\", parts));
        }

        private static class NativeMethods
        {
            [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
            public static extern IntPtr CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern bool DeviceIoControl(IntPtr hDevice, uint dwIoControlCode, ref MFT_ENUM_DATA lpInBuffer, int nInBufferSize, IntPtr lpOutBuffer, int nOutBufferSize, out uint lpBytesReturned, IntPtr lpOverlapped);

            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern bool DeviceIoControl(IntPtr hDevice, uint dwIoControlCode, IntPtr lpInBuffer, int nInBufferSize, out USN_JOURNAL_DATA lpOutBuffer, int nOutBufferSize, out uint lpBytesReturned, IntPtr lpOverlapped);

            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern bool GetFileInformationByHandle(IntPtr hFile, out BY_HANDLE_FILE_INFORMATION lpFileInformation);

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            public static extern bool GetFileAttributesEx(string lpFileName, int fInfoLevelId, ref WIN32_FILE_ATTRIBUTE_DATA lpFileInformation);

            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern bool CloseHandle(IntPtr hObject);

            public const uint GENERIC_READ = 0x80000000;
            public const uint FILE_SHARE_READ = 0x00000001;
            public const uint FILE_SHARE_WRITE = 0x00000002;
            public const uint OPEN_EXISTING = 3;
            public const uint FSCTL_ENUM_USN_DATA = 0x000900b3;
            public const uint FSCTL_QUERY_USN_JOURNAL = 0x000900f4;

            [StructLayout(LayoutKind.Sequential)]
            public struct MFT_ENUM_DATA { public ulong StartFileReferenceNumber; public long LowUsn; public long HighUsn; }

            [StructLayout(LayoutKind.Sequential)]
            public struct USN_JOURNAL_DATA { public ulong UsnJournalID; public long FirstUsn; public long NextUsn; public long LowestValidUsn; public long MaxUsn; public ulong MaximumSize; public ulong AllocationDelta; }

            [StructLayout(LayoutKind.Sequential)]
            public struct USN_RECORD_V2 { public uint RecordLength; public ushort MajorVersion; public ushort MinorVersion; public ulong FileReferenceNumber; public ulong ParentFileReferenceNumber; public long Usn; public long TimeStamp; public uint Reason; public uint SourceInfo; public uint SecurityId; public uint FileAttributes; public ushort FileNameLength; public ushort FileNameOffset; }

            [StructLayout(LayoutKind.Sequential)]
            public struct BY_HANDLE_FILE_INFORMATION { public uint dwFileAttributes; public System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime; public System.Runtime.InteropServices.ComTypes.FILETIME ftLastAccessTime; public System.Runtime.InteropServices.ComTypes.FILETIME ftLastWriteTime; public uint dwVolumeSerialNumber; public uint nFileSizeHigh; public uint nFileSizeLow; public uint nNumberOfLinks; public uint nFileIndexHigh; public uint nFileIndexLow; }

            [StructLayout(LayoutKind.Sequential)]
            public struct WIN32_FILE_ATTRIBUTE_DATA { public uint dwFileAttributes; public System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime; public System.Runtime.InteropServices.ComTypes.FILETIME ftLastAccessTime; public System.Runtime.InteropServices.ComTypes.FILETIME ftLastWriteTime; public uint nFileSizeHigh; public uint nFileSizeLow; }
        }
    }
}
