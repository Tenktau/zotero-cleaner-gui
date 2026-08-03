// =====================================================================
// ZoteroPdfCleaner.cs — WinForms 界面（单文件便携 GUI）
//
// 编译方式见 build.cmd。注意：本文件必须保存为 UTF-8 带 BOM。
// =====================================================================

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace ZoteroCleaner
{
    /// <summary>主窗口：选路径 -> 扫描 -> 勾选 -> 移入回收站。</summary>
    public sealed class MainForm : Form
    {
        private readonly TextBox _zoteroBox = new TextBox();
        private readonly TextBox _targetBox = new TextBox();
        private readonly Button _browseZotero = new Button();
        private readonly Button _browseTarget = new Button();
        private readonly Button _scan = new Button();
        private readonly Button _cancel = new Button();
        private readonly ProgressBar _progress = new ProgressBar();
        private readonly Label _status = new Label();
        private readonly ListView _list = new ListView();
        private readonly Label _summary = new Label();
        private readonly Button _selectAll = new Button();
        private readonly Button _selectNone = new Button();
        private readonly Button _delete = new Button();

        private BackgroundWorker _worker;
        private CancellationTokenSource _cts;
        private List<DuplicateInfo> _current = new List<DuplicateInfo>();

        public MainForm()
        {
            Text = "Zotero 重复 PDF 清理";
            Font = new Font("Segoe UI", 9f);
            ClientSize = new Size(840, 520);
            MinimumSize = new Size(720, 460);
            StartPosition = FormStartPosition.CenterScreen;
            BuildUi();
            AutoDetectPaths();
        }

        // ==================== 界面搭建 ====================

        private void BuildUi()
        {
            // 结果列表（先加入：Dock=Fill 会自动占据顶部与底部面板之间的剩余区域。
            // 注意 Dock 布局按 Controls 集合逆序处理，须先加 Fill 再加边缘面板，
            // 否则 Fill 会盖住顶部/底部面板。）
            _list.Dock = DockStyle.Fill;
            _list.View = View.Details;
            _list.FullRowSelect = true;
            _list.CheckBoxes = true;
            _list.Columns.Add("文件名", 220);
            _list.Columns.Add("大小", 90, HorizontalAlignment.Right);
            _list.Columns.Add("所在位置", 260);
            _list.Columns.Add("对应 Zotero 附件", 280);
            Controls.Add(_list);

            // 顶部面板：两行路径 + 一行操作按钮
            Panel top = new Panel { Dock = DockStyle.Top, Width = ClientSize.Width };
            int y = 12;
            AddPathRow(top, ref y, "Zotero 附件目录", _zoteroBox, _browseZotero,
                new EventHandler(delegate { BrowseFolder(_zoteroBox); }));
            AddPathRow(top, ref y, "待清理文件夹", _targetBox, _browseTarget,
                new EventHandler(delegate { BrowseFolder(_targetBox); }));

            _scan.Text = "开始扫描";
            _scan.Width = 88; _scan.Left = 12; _scan.Top = y;
            _cancel.Text = "取消";
            _cancel.Width = 72; _cancel.Left = _scan.Right + 8; _cancel.Top = y; _cancel.Enabled = false;
            _progress.Style = ProgressBarStyle.Marquee;
            _progress.MarqueeAnimationSpeed = 30;
            _progress.Left = _cancel.Right + 12; _progress.Top = y + 7;
            _progress.Width = 260; _progress.Height = 16; _progress.Visible = false;
            _status.Text = "就绪。";
            _status.Left = _progress.Right + 10; _status.Top = y + 3;
            _status.Width = 280; _status.AutoEllipsis = true;
            y += 34;
            top.Height = y + 8;
            top.Controls.Add(_scan);
            top.Controls.Add(_cancel);
            top.Controls.Add(_progress);
            top.Controls.Add(_status);
            Controls.Add(top);

            // 底部面板：摘要 + 全选/全不选 + 移入回收站
            Panel bottom = new Panel { Dock = DockStyle.Bottom, Width = ClientSize.Width, Height = 46 };
            _summary.Text = "";
            _summary.Left = 12; _summary.Top = 16;
            _summary.AutoSize = false; _summary.AutoEllipsis = true;
            _selectAll.Text = "全选"; _selectAll.Width = 64; _selectAll.Top = 11; _selectAll.Enabled = false;
            _selectNone.Text = "全不选"; _selectNone.Width = 64; _selectNone.Top = 11; _selectNone.Enabled = false;
            _delete.Text = "移入回收站"; _delete.Width = 104; _delete.Top = 11; _delete.Enabled = false;
            bottom.Controls.Add(_summary);
            bottom.Controls.Add(_selectAll);
            bottom.Controls.Add(_selectNone);
            bottom.Controls.Add(_delete);
            RepositionBottom(bottom);
            bottom.Resize += delegate { RepositionBottom(bottom); };
            Controls.Add(bottom);

            // 事件
            _scan.Click += OnScan;
            _cancel.Click += OnCancel;
            _delete.Click += OnDelete;
            _selectAll.Click += delegate { SetAllChecked(true); };
            _selectNone.Click += delegate { SetAllChecked(false); };
            _list.ItemChecked += delegate { UpdateSummary(); };
        }

        private static void RepositionBottom(Panel bottom)
        {
            Control sum = bottom.Controls[0];
            Control sa = bottom.Controls[1];
            Control sn = bottom.Controls[2];
            Control del = bottom.Controls[3];
            int right = bottom.Width;
            del.Left = right - del.Width - 12;
            sn.Left = del.Left - sn.Width - 6;
            sa.Left = sn.Left - sa.Width - 6;
            sum.Left = 12;
            sum.Top = 16;
            sum.Width = Math.Max(50, sa.Left - 24);
        }

        private void AddPathRow(Panel parent, ref int y, string label,
                                TextBox box, Button btn, EventHandler click)
        {
            Label lbl = new Label { Text = label, AutoSize = true, Left = 12, Top = y + 3 };
            box.Left = 130; box.Top = y;
            box.Width = parent.Width - 130 - 80 - 8;
            box.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            btn.Text = "浏览…";
            btn.Width = 72; btn.Left = parent.Width - 80; btn.Top = y;
            btn.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btn.Click += click;
            parent.Controls.Add(lbl);
            parent.Controls.Add(box);
            parent.Controls.Add(btn);
            y += 32;
        }

        private void AutoDetectPaths()
        {
            string z = Scanner.GetZoteroStoragePath();
            _zoteroBox.Text = z;
            _targetBox.Text = Scanner.GetDownloadsPath();
            if (!Directory.Exists(z))
                _status.Text = "未自动找到 Zotero 附件目录，请点“浏览…”手动选择。";
        }

        private void BrowseFolder(TextBox box)
        {
            using (FolderBrowserDialog dlg = new FolderBrowserDialog())
            {
                if (box.Text.Length > 0 && Directory.Exists(box.Text)) dlg.SelectedPath = box.Text;
                if (dlg.ShowDialog(this) == DialogResult.OK) box.Text = dlg.SelectedPath;
            }
        }

        // ==================== 扫描 ====================

        private void OnScan(object sender, EventArgs e)
        {
            string zot = _zoteroBox.Text.Trim();
            string tgt = _targetBox.Text.Trim();
            if (zot.Length == 0 || tgt.Length == 0)
            {
                MessageBox.Show(this, "请填写 Zotero 附件目录和待清理文件夹。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!Directory.Exists(zot))
            {
                MessageBox.Show(this, "找不到 Zotero 附件目录：\n" + zot, "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!Directory.Exists(tgt))
            {
                MessageBox.Show(this, "找不到待清理文件夹：\n" + tgt, "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (PathsOverlap(zot, tgt))
            {
                MessageBox.Show(this, "待清理文件夹不能是 Zotero 附件目录本身或其子目录。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _list.Items.Clear();
            _summary.Text = "";
            _current = new List<DuplicateInfo>();
            _delete.Enabled = _selectAll.Enabled = _selectNone.Enabled = false;
            _scan.Enabled = false;
            _browseZotero.Enabled = _browseTarget.Enabled = false;
            _cancel.Enabled = true;
            _progress.Visible = true;
            _status.Text = "准备中…";

            _cts = new CancellationTokenSource();
            CancellationToken token = _cts.Token;

            _worker = new BackgroundWorker
            {
                WorkerReportsProgress = true,
                WorkerSupportsCancellation = true
            };
            _worker.DoWork += delegate(object s, DoWorkEventArgs args)
            {
                BackgroundWorker bgw = (BackgroundWorker)s;
                try
                {
                    args.Result = Scanner.FindDuplicates(zot, tgt,
                        delegate(string msg)
                        {
                            if (!bgw.CancellationPending) bgw.ReportProgress(0, msg);
                        },
                        token);
                }
                catch (OperationCanceledException)
                {
                    args.Cancel = true;
                }
            };
            _worker.ProgressChanged += delegate(object s, ProgressChangedEventArgs args)
            {
                _status.Text = args.UserState as string;
            };
            _worker.RunWorkerCompleted += delegate(object s, RunWorkerCompletedEventArgs args)
            {
                _progress.Visible = false;
                _cancel.Enabled = false;
                _scan.Enabled = true;
                _browseZotero.Enabled = _browseTarget.Enabled = true;
                if (_cts != null) { _cts.Dispose(); _cts = null; }

                if (args.Cancelled)
                {
                    _status.Text = "已取消。";
                    return;
                }
                if (args.Error != null)
                {
                    _status.Text = "扫描出错。";
                    MessageBox.Show(this, args.Error.Message, "出错了",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                _status.Text = "扫描完成。";
                Populate(args.Result as List<DuplicateInfo>);
            };
            _worker.RunWorkerAsync();
        }

        private void OnCancel(object sender, EventArgs e)
        {
            if (_cts != null) _cts.Cancel();
            _cancel.Enabled = false;
            _status.Text = "正在取消…";
        }

        private static bool PathsOverlap(string a, string b)
        {
            string fa = Path.GetFullPath(a).TrimEnd('\\', '/');
            string fb = Path.GetFullPath(b).TrimEnd('\\', '/');
            return string.Equals(fa, fb, StringComparison.OrdinalIgnoreCase)
                || fb.StartsWith(fa + "\\", StringComparison.OrdinalIgnoreCase);
        }

        // ==================== 结果展示 ====================

        private void Populate(List<DuplicateInfo> list)
        {
            if (list == null) list = new List<DuplicateInfo>();
            _current = list;
            _list.BeginUpdate();
            _list.Items.Clear();
            long total = 0;
            foreach (DuplicateInfo d in list)
            {
                total += d.Size;
                ListViewItem item = new ListViewItem(new string[]
                {
                    Path.GetFileName(d.Path),
                    FormatSize(d.Size),
                    Path.GetDirectoryName(d.Path),
                    d.MatchedZoteroFile
                });
                item.Tag = d;
                item.Checked = true;
                _list.Items.Add(item);
            }
            _list.EndUpdate();

            bool any = list.Count > 0;
            _delete.Enabled = _selectAll.Enabled = _selectNone.Enabled = any;
            _summary.Text = any
                ? String.Format("发现 {0} 个重复，可释放 {1}", list.Count, FormatSize(total))
                : "未发现与 Zotero 库重复的文件";
        }

        private void UpdateSummary()
        {
            int n = 0;
            long total = 0;
            foreach (ListViewItem item in _list.Items)
            {
                DuplicateInfo d = item.Tag as DuplicateInfo;
                if (item.Checked && d != null)
                {
                    n++;
                    total += d.Size;
                }
            }
            if (_list.Items.Count == 0) _summary.Text = "";
            else if (n == 0) _summary.Text = String.Format("共 {0} 个重复，未勾选任何文件", _list.Items.Count);
            else _summary.Text = String.Format("已勾选 {0} 个，可释放 {1}", n, FormatSize(total));
        }

        private void SetAllChecked(bool state)
        {
            foreach (ListViewItem item in _list.Items) item.Checked = state;
        }

        // ==================== 清理 ====================

        private void OnDelete(object sender, EventArgs e)
        {
            List<DuplicateInfo> selected = new List<DuplicateInfo>();
            long total = 0;
            foreach (ListViewItem item in _list.Items)
            {
                if (!item.Checked) continue;
                DuplicateInfo d = item.Tag as DuplicateInfo;
                if (d == null) continue;
                selected.Add(d);
                total += d.Size;
            }
            if (selected.Count == 0)
            {
                MessageBox.Show(this, "请先勾选要清理的文件。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            DialogResult answer = MessageBox.Show(this,
                String.Format("将把 {0} 个重复文件移入回收站（可恢复），释放 {1}。\n确定继续？",
                    selected.Count, FormatSize(total)),
                "确认清理", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (answer != DialogResult.Yes) return;

            _delete.Enabled = false;
            int ok = 0, fail = 0;
            List<string> failed = new List<string>();
            foreach (DuplicateInfo d in selected)
            {
                string err;
                if (Scanner.SendToRecycleBin(d.Path, out err)) ok++;
                else
                {
                    fail++;
                    failed.Add(err != null ? d.Path + "（" + err + "）" : d.Path);
                }
            }
            string msg = String.Format("完成：成功 {0} 个，失败 {1} 个。", ok, fail);
            if (failed.Count > 0) msg += "\n\n失败：\n" + String.Join("\n", failed);
            MessageBox.Show(this, msg, "清理结果",
                MessageBoxButtons.OK, fail > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);

            // 用仍存在的文件重建列表
            List<DuplicateInfo> remaining = _current.FindAll(
                delegate(DuplicateInfo d) { return File.Exists(d.Path); });
            Populate(remaining);
        }

        // ==================== 工具 ====================

        private static string FormatSize(long b)
        {
            if (b >= 1L << 30) return String.Format("{0:N2} GB", b / (double)(1L << 30));
            if (b >= 1L << 20) return String.Format("{0:N2} MB", b / (double)(1L << 20));
            if (b >= 1L << 10) return String.Format("{0:N2} KB", b / (double)(1L << 10));
            return b + " B";
        }
    }

#if !TESTING
    /// <summary>程序入口。</summary>
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
#endif
}
