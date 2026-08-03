// =====================================================================
// Scanner.cs — 核心去重逻辑（与 CLI 版 clean-zotero-duplicates.ps1 同源）
//
// 注意：本文件必须保存为 UTF-8 带 BOM，否则 csc 按系统 ANSI 编码读取，
//       中文注释与字符串会乱码。
// =====================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace ZoteroCleaner
{
    /// <summary>一条重复文件记录。</summary>
    public sealed class DuplicateInfo
    {
        public string Path;              // 目标文件夹中的重复文件
        public long Size;                // 文件大小（字节）
        public string MatchedZoteroFile; // 命中的 Zotero 附件完整路径
    }

    /// <summary>核心逻辑：路径探测、内容哈希去重、回收站删除。</summary>
    public static class Scanner
    {
        /// <summary>匹配的附件扩展名。</summary>
        public const string AttachmentExt = ".pdf";

        // ==================== 路径探测 ====================

        /// <summary>自动定位 Zotero 附件存储目录（...\storage）。</summary>
        public static string GetZoteroStoragePath()
        {
            try
            {
                // 从 profile 的 prefs.js 解析自定义数据目录
                string profilesDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Zotero", "Zotero", "Profiles");
                if (Directory.Exists(profilesDir))
                {
                    foreach (string prefs in Directory.EnumerateFiles(profilesDir, "prefs.js", SearchOption.AllDirectories))
                    {
                        string text;
                        try { text = File.ReadAllText(prefs); }
                        catch { continue; }

                        // user_pref("extensions.zotero.dataDir", "C:\\...\\Zotero");
                        int i = text.IndexOf("extensions.zotero.dataDir", StringComparison.Ordinal);
                        if (i < 0) continue;
                        int q1 = text.IndexOf('"', i);            // pref 名结束的引号
                        if (q1 < 0) continue;
                        int q2 = text.IndexOf('"', q1 + 1);       // 值开始的引号
                        if (q2 < 0) continue;
                        int q3 = text.IndexOf('"', q2 + 1);       // 值结束的引号
                        if (q3 < 0) continue;
                        string val = text.Substring(q2 + 1, q3 - q2 - 1)
                                         .Replace("\\\\", "\\"); // prefs.js 中的反斜杠转义
                        if (val.Length > 0) return Path.Combine(val, "storage");
                    }
                }
            }
            catch { }
            // 回退默认路径
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Zotero", "storage");
        }

        /// <summary>获取系统"下载"文件夹真实路径（兼容 OneDrive 等目录重定向）。</summary>
        public static string GetDownloadsPath()
        {
            Guid downloads = new Guid("374DE290-123F-4565-9164-39C4925E467B");
            try
            {
                IntPtr p;
                int hr = SHGetKnownFolderPath(ref downloads, 0, IntPtr.Zero, out p);
                if (hr == 0)
                {
                    string path = Marshal.PtrToStringUni(p);
                    Marshal.FreeCoTaskMem(p);
                    if (!string.IsNullOrEmpty(path)) return path;
                }
            }
            catch { }
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }

        // ==================== 去重扫描 ====================

        /// <summary>
        /// 找出目标文件夹中与 Zotero 附件内容重复的文件。
        /// 依据 SHA-256 内容哈希；先按文件大小过滤以显著提速。
        /// </summary>
        /// <param name="zoteroStorage">Zotero 附件存储目录。</param>
        /// <param name="targetDir">待清理目录。</param>
        /// <param name="log">进度消息回调（可为 null）。</param>
        /// <param name="ct">取消令牌。</param>
        public static List<DuplicateInfo> FindDuplicates(string zoteroStorage, string targetDir,
                                                         Action<string> log, CancellationToken ct)
        {
            List<DuplicateInfo> result = new List<DuplicateInfo>();
            string zoteroFull = Path.GetFullPath(zoteroStorage).TrimEnd('\\', '/');
            string targetFull = Path.GetFullPath(targetDir).TrimEnd('\\', '/');

            // ---- 1. 索引 Zotero 附件：size -> (hash -> 命中的附件路径) ----
            if (log != null) log("正在索引 Zotero 附件目录…");
            Dictionary<long, Dictionary<string, string>> index =
                new Dictionary<long, Dictionary<string, string>>();
            int zotCount = 0, zotIndexed = 0;
            foreach (string file in EnumerateFilesSafe(zoteroFull, AttachmentExt))
            {
                ct.ThrowIfCancellationRequested();
                zotCount++;
                try
                {
                    long size = new FileInfo(file).Length;
                    string hash = GetSha256(file);
                    if (hash == null) continue;
                    zotIndexed++;
                    Dictionary<string, string> byHash;
                    if (!index.TryGetValue(size, out byHash))
                    {
                        byHash = new Dictionary<string, string>();
                        index[size] = byHash;
                    }
                    if (!byHash.ContainsKey(hash)) byHash[hash] = file;
                }
                catch { }
            }
            if (log != null)
                log(String.Format("Zotero 附件 {0} 个，成功索引 {1} 个。", zotCount, zotIndexed));

            // ---- 2. 扫描目标文件夹 ----
            if (log != null) log("正在扫描目标文件夹…");
            int scanned = 0;
            foreach (string file in EnumerateFilesSafe(targetFull, AttachmentExt))
            {
                ct.ThrowIfCancellationRequested();
                scanned++;

                // 防御：目标目录可能是 Zotero 数据目录的上层目录，跳过 Zotero 自身附件
                if (file.StartsWith(zoteroFull + "\\", StringComparison.OrdinalIgnoreCase)) continue;

                long size;
                try { size = new FileInfo(file).Length; }
                catch { continue; }

                // 大小预过滤：大小不同则内容必不同，跳过哈希以提速
                Dictionary<string, string> byHash;
                if (!index.TryGetValue(size, out byHash)) continue;

                string hash = GetSha256(file);
                if (hash == null) continue;

                string matched;
                if (byHash.TryGetValue(hash, out matched))
                    result.Add(new DuplicateInfo { Path = file, Size = size, MatchedZoteroFile = matched });
            }
            if (log != null)
                log(String.Format("扫描完成：{0} 个文件，发现 {1} 个重复。", scanned, result.Count));
            return result;
        }

        /// <summary>把文件移入回收站（可恢复）。成功返回 true。</summary>
        public static bool SendToRecycleBin(string path, out string error)
        {
            error = null;
            try
            {
                SHFILEOPSTRUCT op = new SHFILEOPSTRUCT
                {
                    wFunc = FO_DELETE,
                    pFrom = path + "\0\0",
                    fFlags = (ushort)(FOF_SILENT | FOF_NOCONFIRMATION | FOF_ALLOWUNDO | FOF_NOERRORUI)
                };
                int hr = SHFileOperation(ref op);
                if (hr != 0)
                {
                    error = String.Format("错误 0x{0:X8}", hr);
                    return false;
                }
                return !File.Exists(path);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        // ==================== 内部实现 ====================

        /// <summary>递归枚举指定扩展名的文件，逐目录容错（无权限等直接跳过）。</summary>
        private static IEnumerable<string> EnumerateFilesSafe(string root, string ext)
        {
            Stack<string> stack = new Stack<string>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                string dir = stack.Pop();
                IEnumerable<string> subdirs;
                IEnumerable<string> files;
                try
                {
                    subdirs = Directory.EnumerateDirectories(dir);
                    files = Directory.EnumerateFiles(dir);
                }
                catch { continue; }
                foreach (string d in subdirs) stack.Push(d);
                foreach (string f in files)
                    if (f.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) yield return f;
            }
        }

        /// <summary>流式计算文件 SHA-256（小写十六进制）。失败返回 null。</summary>
        private static string GetSha256(string path)
        {
            try
            {
                using (FileStream fs = File.OpenRead(path))
                using (SHA256 sha = SHA256.Create())
                {
                    byte[] hash = sha.ComputeHash(fs);
                    StringBuilder sb = new StringBuilder(64);
                    foreach (byte b in hash) sb.Append(b.ToString("x2"));
                    return sb.ToString();
                }
            }
            catch { return null; }
        }

        // ==================== P/Invoke ====================

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHGetKnownFolderPath(ref Guid rfid, uint dwFlags,
                                                       IntPtr hToken, out IntPtr pszPath);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEOPSTRUCT
        {
            public IntPtr hwnd;
            public uint wFunc;
            [MarshalAs(UnmanagedType.LPWStr)] public string pFrom;
            [MarshalAs(UnmanagedType.LPWStr)] public string pTo;
            public ushort fFlags;
            [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
            public IntPtr hNameMappings;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpszProgressTitle;
        }

        private const uint FO_DELETE = 3;
        private const ushort FOF_SILENT = 0x0004;         // 不显示进度对话框
        private const ushort FOF_NOCONFIRMATION = 0x0010; // 不弹确认框
        private const ushort FOF_ALLOWUNDO = 0x0040;      // 允许撤销 => 移入回收站
        private const ushort FOF_NOERRORUI = 0x0400;      // 出错不弹 UI（由调用方报告）
    }
}
