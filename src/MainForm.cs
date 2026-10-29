using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Printing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace PdfMini
{
    internal sealed class MainForm : Form, IPageSource
    {
        private const float BaseScale = 96f / 72f;   // 100% = 96dpi

        private readonly SplitContainer _split;
        private readonly ListView _thumbs;
        private readonly ImageList _thumbImgs;
        private readonly TreeView _outline;
        private readonly TabControl _tabs;
        private readonly PageView _view;
        private readonly StatusStrip _status;
        private readonly ToolStripStatusLabel _lblPage;
        private readonly ToolStripStatusLabel _lblZoom;
        private readonly ToolStripStatusLabel _lblFile;
        private readonly ToolStripStatusLabel _lblMsg;
        private readonly ToolStripTextBox _txtFind;

        private Session _s;
        private int _cur;
        private int _thumbGen;
        private bool _night;
        private int _fit = 1;            // 0=固定缩放 1=适应宽度 2=适应整页
        private float _zoom = 1f;
        private bool _suppressSel;
        private System.Windows.Forms.Timer _resizeTimer;
        private int _printCursor;
        private int _printTo;
        private NupSettings _nup;                 // 排版打印参数（记忆上次选择）
        private readonly Dictionary<int, List<RectangleF>> _hits = new Dictionary<int, List<RectangleF>>();
        private string _hitQuery;
        private readonly List<SizeF> _sizesPt = new List<SizeF>();
        private readonly float _dpi;
        private readonly int _thumbW;
        private readonly int _thumbH;
        private readonly string _pendingOpen;

        private readonly Dictionary<string, Bitmap> _thumbCache = new Dictionary<string, Bitmap>();
        private readonly object _cacheLock = new object();
        private static readonly Dictionary<string, int> _lastPage = new Dictionary<string, int>();
        private static bool _stateLoaded;

        public MainForm(string openPath)
        {
            _dpi = DeviceDpi / 96f;
            _thumbW = (int)(88 * _dpi);
            _thumbH = (int)(118 * _dpi);
            _pendingOpen = openPath;

            Text = "PdfMini";
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            int winW = Math.Min(1440, wa.Width - 120);
            int winH = Math.Min(940, wa.Height - 90);
            if (winW < 900) winW = 900;
            if (winH < 620) winH = 620;
            ClientSize = new Size(winW, winH);
            MinimumSize = new Size(720, 460);
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;
            try { AllowDrop = true; } catch (InvalidOperationException) { }
            Font = new Font("Microsoft YaHei UI", 9f);
            DoubleBuffered = true;

            _thumbImgs = new ImageList();
            _thumbImgs.ImageSize = new Size(_thumbW, _thumbH);
            _thumbImgs.ColorDepth = ColorDepth.Depth32Bit;
            _thumbImgs.TransparentColor = Color.Transparent;

            _view = new PageView();
            _view.Dock = DockStyle.Fill;
            _view.ZoomInRequested += delegate { ZoomBy(1.15f); };
            _view.ZoomOutRequested += delegate { ZoomBy(1f / 1.15f); };
            _view.CurrentPageChanged += delegate { SyncCurrentFromView(); };

            _thumbs = new ListView();
            _thumbs.Dock = DockStyle.Fill;
            _thumbs.View = View.LargeIcon;
            _thumbs.LargeImageList = _thumbImgs;
            _thumbs.MultiSelect = true;
            _thumbs.HideSelection = false;
            _thumbs.BorderStyle = BorderStyle.None;
            _thumbs.BackColor = Color.FromArgb(58, 58, 62);
            _thumbs.ForeColor = Color.Gainsboro;
            _thumbs.SelectedIndexChanged += ThumbSelectionChanged;
            _thumbs.ContextMenuStrip = BuildThumbMenu();

            _outline = new TreeView();
            _outline.Dock = DockStyle.Fill;
            _outline.BorderStyle = BorderStyle.None;
            _outline.BackColor = Color.FromArgb(58, 58, 62);
            _outline.ForeColor = Color.Gainsboro;
            _outline.HideSelection = false;
            _outline.FullRowSelect = true;
            _outline.ShowLines = false;
            _outline.ShowRootLines = true;   // 必须留根节点展开箭头，否则子项点不开
            _outline.ShowPlusMinus = true;
            _outline.ShowNodeToolTips = true;
            _outline.ItemHeight = Math.Max(18, (int)(22 * _dpi));
            _outline.AfterSelect += OutlineSelected;

            TabPage tabPages = new TabPage("页面");
            tabPages.BackColor = Color.FromArgb(58, 58, 62);
            tabPages.Padding = new Padding(0);
            tabPages.Controls.Add(_thumbs);

            TabPage tabOutline = new TabPage("大纲");
            tabOutline.BackColor = Color.FromArgb(58, 58, 62);
            tabOutline.Padding = new Padding(0);
            tabOutline.Controls.Add(_outline);

            _tabs = new TabControl();
            _tabs.Dock = DockStyle.Fill;
            _tabs.TabPages.Add(tabPages);
            _tabs.TabPages.Add(tabOutline);

            _split = new SplitContainer();
            _split.Dock = DockStyle.Fill;
            _split.FixedPanel = FixedPanel.Panel1;
            _split.Panel1.BackColor = Color.FromArgb(58, 58, 62);
            _split.Panel2.BackColor = Color.FromArgb(45, 45, 48);
            _split.Panel1.Controls.Add(_tabs);
            _split.Panel2.Controls.Add(_view);

            _lblPage = new ToolStripStatusLabel("未打开文件");
            _lblPage.AutoSize = true;
            _lblZoom = new ToolStripStatusLabel("");
            _lblZoom.AutoSize = true;
            _lblZoom.BorderSides = ToolStripStatusLabelBorderSides.Left;
            _lblZoom.BorderStyle = Border3DStyle.Etched;
            _lblZoom.Padding = new Padding(8, 0, 8, 0);
            _lblFile = new ToolStripStatusLabel("");
            _lblFile.AutoSize = true;
            _lblFile.BorderSides = ToolStripStatusLabelBorderSides.Left;
            _lblFile.BorderStyle = Border3DStyle.Etched;
            _lblFile.Padding = new Padding(8, 0, 8, 0);

            _lblMsg = new ToolStripStatusLabel("");
            _lblMsg.Spring = true;
            _lblMsg.TextAlign = ContentAlignment.MiddleLeft;
            _lblMsg.BorderSides = ToolStripStatusLabelBorderSides.Left;
            _lblMsg.BorderStyle = Border3DStyle.Etched;
            _lblMsg.Padding = new Padding(8, 0, 0, 0);

            _status = new StatusStrip();
            _status.SizingGrip = true;
            _status.Items.Add(_lblPage);
            _status.Items.Add(_lblZoom);
            _status.Items.Add(_lblFile);
            _status.Items.Add(_lblMsg);

            ToolStrip bar = BuildToolbar();
            Controls.Add(_split);
            Controls.Add(bar);
            Controls.Add(_status);

            _txtFind = (ToolStripTextBox)bar.Items["findBox"];

            _resizeTimer = new System.Windows.Forms.Timer();
            _resizeTimer.Interval = 130;
            _resizeTimer.Tick += delegate
            {
                _resizeTimer.Stop();
                if (_fit != 0 && _s != null && _s.Count > 0) RenderCurrent();
            };

            AttachDrop(this);
            AttachDrop(_view);
            AttachDrop(_thumbs);

            LoadState();
            UpdateStatus();
        }

        // ───────────────────────────── UI 构建 ─────────────────────────────

        private ToolStripButton MakeButton(string text, string tip, EventHandler onClick)
        {
            ToolStripButton b = new ToolStripButton(text);
            b.DisplayStyle = ToolStripItemDisplayStyle.Text;
            b.AutoSize = true;
            b.Padding = new Padding(7, 0, 7, 0);
            b.ToolTipText = tip;
            b.Click += onClick;
            return b;
        }

        private ToolStrip BuildToolbar()
        {
            ToolStrip bar = new ToolStrip();
            bar.GripStyle = ToolStripGripStyle.Hidden;
            bar.Dock = DockStyle.Top;
            bar.RenderMode = ToolStripRenderMode.System;
            bar.Padding = new Padding(4, 2, 4, 2);

            bar.Items.Add(MakeButton("打开", "Ctrl+O", delegate { AskOpen(); }));
            bar.Items.Add(MakeButton("合并…", "把别的 PDF 追加到末尾", delegate { AskMerge(false); }));
            bar.Items.Add(MakeButton("插入…", "插到当前页之后", delegate { AskMerge(true); }));
            bar.Items.Add(MakeButton("另存…", "Ctrl+S 不会改动原文件", delegate { AskSaveAs(); }));
            bar.Items.Add(MakeButton("打印…", "Ctrl+P 打印当前文档（所见即所得）", delegate { AskPrint(); }));
            bar.Items.Add(MakeButton("排版打印…", "Ctrl+Shift+P 多页排一张纸（报销车票：2 页/A4）", delegate { AskNupPrint(); }));
            bar.Items.Add(new ToolStripSeparator());

            bar.Items.Add(MakeButton("删页", "Delete", delegate { DeleteSelected(); }));
            bar.Items.Add(MakeButton("↑", "上移（Ctrl+↑）", delegate { MoveSelected(-1); }));
            bar.Items.Add(MakeButton("↓", "下移（Ctrl+↓）", delegate { MoveSelected(1); }));
            bar.Items.Add(MakeButton("左转", "左转 90°", delegate { RotateSelected(-90); }));
            bar.Items.Add(MakeButton("右转", "右转 90°", delegate { RotateSelected(90); }));
            bar.Items.Add(MakeButton("拆分…", "把选中的页导出成新 PDF", delegate { ExportSelected(); }));
            bar.Items.Add(new ToolStripSeparator());

            bar.Items.Add(MakeButton("适宽", "页面宽度填满窗口", delegate { SetFit(1); }));
            bar.Items.Add(MakeButton("适页", "整页可见", delegate { SetFit(2); }));
            bar.Items.Add(MakeButton("100%", "实际大小", delegate { Set100(); }));
            bar.Items.Add(MakeButton("夜间", "反色阅读", delegate { ToggleNight(); }));
            bar.Items.Add(new ToolStripSeparator());

            bar.Items.Add(MakeButton("复制文字", "复制当前页全部文字 (Ctrl+C)", delegate { CopyPageText(); }));

            ToolStripLabel findLabel = new ToolStripLabel("查找");
            findLabel.Padding = new Padding(6, 0, 0, 0);
            bar.Items.Add(findLabel);

            ToolStripTextBox findBox = new ToolStripTextBox();
            findBox.Name = "findBox";
            findBox.AutoSize = false;
            findBox.Width = 150;
            findBox.ToolTipText = "回车查找（从当前页往后，含文字层）";
            findBox.KeyDown += delegate (object o, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    DoFind(findBox.Text);
                }
            };
            bar.Items.Add(findBox);
            bar.Items.Add(MakeButton("找", "查找", delegate { DoFind(findBox.Text); }));
            bar.Items.Add(new ToolStripSeparator());
            bar.Items.Add(MakeButton("关于", "关于 PdfMini · cjfranklin 工作室", delegate { ShowAbout(); }));

            return bar;
        }

        private ContextMenuStrip BuildThumbMenu()
        {
            ContextMenuStrip m = new ContextMenuStrip();
            m.Items.Add("删除所选页", null, delegate { DeleteSelected(); });
            m.Items.Add("上移", null, delegate { MoveSelected(-1); });
            m.Items.Add("下移", null, delegate { MoveSelected(1); });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("左转 90°", null, delegate { RotateSelected(-90); });
            m.Items.Add("右转 90°", null, delegate { RotateSelected(90); });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("导出所选页…", null, delegate { ExportSelected(); });
            m.Items.Add("复制本页文字", null, delegate { CopyPageText(); });
            return m;
        }

        /// <summary>
        /// 非交互式会话（从服务/命令行/WSL 启动）里没有真正的桌面窗口站，
        /// AllowDrop 会在创建句柄时触发 DragAcceptFiles 失败并抛
        /// InvalidOperationException("DragDrop 注册失败")。
        /// 拖放只是便捷功能，失败不应阻断启动。
        /// </summary>
        protected override void OnHandleCreated(EventArgs e)
        {
            try
            {
                base.OnHandleCreated(e);
            }
            catch (InvalidOperationException ex)
            {
                if (ex.Message == null || !ex.Message.Contains("DragDrop"))
                    throw;
            }
        }

        private void AttachDrop(Control c)
        {
            try
            {
                c.AllowDrop = true;
                c.DragEnter += DropEnter;
                c.DragDrop += DropFiles;
            }
            catch (InvalidOperationException)
            {
                // 同上：无桌面会话时跳过拖放支持
            }
        }

        private void DropEnter(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effect = DragDropEffects.None;
                return;
            }
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            e.Effect = (files != null && files.Length > 0 && IsPdf(files[0])) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        private void DropFiles(object sender, DragEventArgs e)
        {
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files == null || files.Length == 0) return;
            bool opened = false;
            for (int i = 0; i < files.Length; i++)
            {
                if (!IsPdf(files[i])) continue;
                if (!opened) { OpenFile(files[i]); opened = true; }
                else MergeFiles(new string[] { files[i] }, false);
            }
        }

        private static bool IsPdf(string path)
        {
            return path != null && path.ToLowerInvariant().EndsWith(".pdf");
        }

        // ───────────────────────────── 打开 / 关闭 ─────────────────────────────

        private void AskOpen()
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Filter = "PDF 文件 (*.pdf)|*.pdf";
                dlg.Title = "打开 PDF";
                if (dlg.ShowDialog(this) == DialogResult.OK) OpenFile(dlg.FileName);
            }
        }

        private void OpenFile(string path)
        {
            if (!File.Exists(path)) return;

            IntPtr handle = IntPtr.Zero;
            string pwd = null;
            uint err = 0;
            while (true)
            {
                handle = PdfApi.FPDF_LoadDocument(PdfApi.Utf8Z(path), PdfApi.Utf8Z(pwd));
                if (handle != IntPtr.Zero) break;
                err = PdfApi.FPDF_GetLastError();
                if (err != PdfApi.ErrPassword) break;
                pwd = AskPassword(path);
                if (pwd == null) return;
            }
            if (handle == IntPtr.Zero)
            {
                MessageBox.Show(this, "打不开这个文件：\n" + path + "\n\n原因：" + PdfApi.ErrorName(err),
                    "PdfMini", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            int count = PdfApi.FPDF_GetPageCount(handle);
            if (count <= 0)
            {
                PdfApi.FPDF_CloseDocument(handle);
                MessageBox.Show(this, "这个 PDF 没有可用页面。", "PdfMini", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            CloseSession();
            Session s = new Session();
            s.BasePath = path;
            PdfDoc doc = new PdfDoc(handle, path);
            s.Docs.Add(doc);
            for (int i = 0; i < count; i++) s.Pages.Add(new PageRef(doc, i));
            _s = s;

            int last;
            if (_lastPage.TryGetValue(FullKey(path), out last) && last >= 0 && last < count) _cur = last;
            else _cur = 0;

            BuildSizeCache();
            _view.SetSource(this);
            RebuildThumbs();
            RenderCurrent();
            _view.ScrollToPage(_cur);
            SelectThumb(_cur);
        }

        private void CloseSession()
        {
            _thumbGen++;
            _hits.Clear();
            _hitQuery = null;
            _suppressSel = true;
            _thumbs.Items.Clear();
            _thumbImgs.Images.Clear();
            _suppressSel = false;
            _view.SetSource(null);

            if (_s != null)
            {
                _s.Dispose();
                _s = null;
            }
            lock (_cacheLock)
            {
                foreach (Bitmap b in _thumbCache.Values) b.Dispose();
                _thumbCache.Clear();
            }
            _cur = 0;
        }

        private string AskPassword(string file)
        {
            using (PasswordDialog dlg = new PasswordDialog(Path.GetFileName(file)))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return null;
                return dlg.Password;
            }
        }

        // ───────────────────────────── 渲染 ─────────────────────────────

        /// <summary>按当前缩放模式给整个视图设定统一比例（连续滚动所有页同一比例）。</summary>
        private void RenderCurrent()
        {
            if (_s == null || _s.Count == 0)
            {
                _view.Hint = "拖入 PDF 文件，或按 Ctrl+O 打开";
                _view.SetSource(null);
                UpdateStatus();
                return;
            }
            if (_cur < 0) _cur = 0;
            if (_cur >= _s.Count) _cur = _s.Count - 1;

            _view.SetScale(ComputeScale(), _view.PageCount > 0);
            _zoom = _view.PixelScale / BaseScale;
            UpdateStatus();
        }

        /// <summary>统一比例：适宽按“最宽页”，适页按“最宽最高页”，保证所有页左右对齐。</summary>
        private float ComputeScale()
        {
            if (_s == null || _s.Count == 0) return BaseScale;
            if (_fit == 0) return BaseScale * _zoom;

            float availW = Math.Max(140f, _view.ClientSize.Width - 28f);
            float availH = Math.Max(140f, _view.ClientSize.Height - 28f);
            float maxW = 1f, maxH = 1f;
            for (int i = 0; i < _sizesPt.Count; i++)
            {
                if (_sizesPt[i].Width > maxW) maxW = _sizesPt[i].Width;
                if (_sizesPt[i].Height > maxH) maxH = _sizesPt[i].Height;
            }
            if (_fit == 1) return availW / maxW;
            return Math.Min(availW / maxW, availH / maxH);
        }

        /// <summary>页面尺寸缓存（pt，含旋转）：缩放/适宽都查这张表，不反复开页面。</summary>
        private void BuildSizeCache()
        {
            _sizesPt.Clear();
            if (_s == null || _s.Count == 0) return;
            lock (_s.Sync)
            {
                for (int i = 0; i < _s.Count; i++)
                {
                    PageRef pr = _s.Pages[i];
                    IntPtr page = PdfApi.FPDF_LoadPage(pr.Doc.Handle, pr.Index);
                    if (page == IntPtr.Zero)
                    {
                        _sizesPt.Add(new SizeF(595f, 842f));
                        continue;
                    }
                    try
                    {
                        float pw = PdfApi.FPDF_GetPageWidthF(page);
                        float ph = PdfApi.FPDF_GetPageHeightF(page);
                        bool swap = (pr.Rot % 180) != 0;
                        _sizesPt.Add(swap ? new SizeF(ph, pw) : new SizeF(pw, ph));
                    }
                    finally
                    {
                        PdfApi.FPDF_ClosePage(page);
                    }
                }
            }
        }

        /// <summary>页面结构变了（删/移/旋转/合并/换文件）后重建视图。</summary>
        private void RefreshDocumentView()
        {
            string query = _hitQuery;
            BuildSizeCache();
            _view.SetSource(this);
            if (_s != null && _cur >= 0 && _cur < _s.Count) _view.ScrollToPage(_cur);
            RenderCurrent();
            // 结构变了（删/移/旋转）但搜索还开着 → 重算高亮，不跳页
            if (!string.IsNullOrEmpty(query)) SearchAndHighlight(query, false);
        }

        private void SyncCurrentFromView()
        {
            if (_s == null || _s.Count == 0) return;
            int idx = _view.CurrentPage;
            if (idx < 0 || idx >= _s.Count || idx == _cur) return;
            _cur = idx;
            RememberPage();
            UpdateStatus();
            SelectThumb(_cur);
        }

        // ── IPageSource（PageView 通过它取页面）──
        int IPageSource.PageCount
        {
            get { return _s == null ? 0 : _s.Count; }
        }

        SizeF IPageSource.GetPageSizePt(int index)
        {
            if (index >= 0 && index < _sizesPt.Count) return _sizesPt[index];
            return new SizeF(595f, 842f);
        }

        Bitmap IPageSource.RenderPageAt(int index, float pixelsPerPoint)
        {
            if (_s == null || index < 0 || index >= _s.Count) return null;
            PageRef pr = _s.Pages[index];
            lock (_s.Sync)
            {
                IntPtr page = PdfApi.FPDF_LoadPage(pr.Doc.Handle, pr.Index);
                if (page == IntPtr.Zero) return null;
                try
                {
                    return PdfApi.RenderPage(page, pixelsPerPoint, pr.Rot / 90, _night);
                }
                finally
                {
                    PdfApi.FPDF_ClosePage(page);
                }
            }
        }

        IList<RectangleF> IPageSource.GetHighlights(int index)
        {
            List<RectangleF> list;
            if (_hits.TryGetValue(index, out list)) return list;
            return null;
        }

        private void SetFit(int mode)
        {
            _fit = mode;
            RenderCurrent();
        }

        private void Set100()
        {
            _fit = 0;
            _zoom = 1f;
            RenderCurrent();
        }

        private void ZoomBy(float factor)
        {
            if (_s == null || _s.Count == 0) return;
            float z = _zoom * factor;
            if (z < 0.15f) z = 0.15f;
            if (z > 6f) z = 6f;
            _zoom = z;
            _fit = 0;
            RenderCurrent();
        }

        private void ToggleNight()
        {
            _night = !_night;
            _view.InvalidateAll();
            RenderCurrent();
        }

        private void ShowAbout()
        {
            using (AboutForm about = new AboutForm())
            {
                about.ShowDialog(this);
            }
        }

        // ───────────────────────────── 大纲 ─────────────────────────────

        private void LoadOutline()
        {
            if (_outline == null) return;
            _outline.BeginUpdate();
            _outline.Nodes.Clear();

            List<OutlineItem> items = new List<OutlineItem>();
            int total = 0;
            if (_s != null && _s.Count > 0 && _s.Docs.Count > 0)
            {
                lock (_s.Sync)
                {
                    items = OutlineReader.Read(_s.Docs[0], _s, out total);
                }
            }

            if (items.Count == 0)
            {
                TreeNode none = _outline.Nodes.Add("这份 PDF 没有书签 / 大纲");
                none.ForeColor = Color.FromArgb(150, 150, 155);
                _tabs.TabPages[1].Text = "大纲";
            }
            else
            {
                for (int i = 0; i < items.Count; i++) AddOutlineNode(_outline.Nodes, items[i]);
                _tabs.TabPages[1].Text = "大纲 (" + total + ")";
            }
            _outline.EndUpdate();
        }

        private void AddOutlineNode(TreeNodeCollection parent, OutlineItem item)
        {
            TreeNode node = parent.Add(item.Title);
            node.Tag = item;
            node.ToolTipText = item.PageIndex >= 0
                ? "跳到第 " + (item.PageIndex + 1) + " 页"
                : "目标页已不在当前文档中";
            if (item.PageIndex < 0) node.ForeColor = Color.FromArgb(145, 145, 150);
            for (int i = 0; i < item.Children.Count; i++) AddOutlineNode(node.Nodes, item.Children[i]);
        }

        private void OutlineSelected(object sender, TreeViewEventArgs e)
        {
            if (e.Node == null) return;
            OutlineItem item = e.Node.Tag as OutlineItem;
            if (item == null || item.PageIndex < 0 || _s == null) return;
            GoTo(item.PageIndex);
        }

        // ───────────────────────────── 打印 ─────────────────────────────

        /// <summary>当前文档第一页是否为横版（用于自动排列方向）</summary>
        private bool FirstPageLandscape()
        {
            if (_s == null || _s.Count == 0) return false;
            lock (_s.Sync)
            {
                PageRef pr = _s.Pages[0];
                IntPtr page = PdfApi.FPDF_LoadPage(pr.Doc.Handle, pr.Index);
                if (page == IntPtr.Zero) return false;
                try
                {
                    float pw = PdfApi.FPDF_GetPageWidthF(page);
                    float ph = PdfApi.FPDF_GetPageHeightF(page);
                    bool swap = (pr.Rot % 180) != 0;
                    float w = swap ? ph : pw;
                    float h = swap ? pw : ph;
                    return w > h;
                }
                finally
                {
                    PdfApi.FPDF_ClosePage(page);
                }
            }
        }

        /// <summary>排版打印：多页排一张纸 + 打印预览</summary>
        private void AskNupPrint()
        {
            if (_s == null || _s.Count == 0)
            {
                MessageBox.Show(this, "当前没有可打印的页面。", "排版打印",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            NupSettings seed = _nup ?? new NupSettings();
            bool land = FirstPageLandscape();

            using (NupSettingsForm dlg = new NupSettingsForm(seed, _s.Count, land))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK || dlg.Result == null)
                    return;
                _nup = dlg.Result;
            }

            try
            {
                using (PrintDocument doc = new PrintDocument())
                {
                    doc.DocumentName = _lblFile.Text;
                    doc.PrintPage += PrintNupPage;

                    _printCursor = 0;
                    _printTo = _s.Count;

                    using (PrintPreviewDialog preview = new PrintPreviewDialog())
                    {
                        preview.Text = "打印预览 — " + _nup.PerSheet + " 页/张";
                        preview.Document = doc;
                        preview.Width = Math.Min(1100, Screen.PrimaryScreen.WorkingArea.Width - 80);
                        preview.Height = Math.Min(860, Screen.PrimaryScreen.WorkingArea.Height - 80);
                        preview.StartPosition = FormStartPosition.CenterParent;
                        preview.UseAntiAlias = true;
                        preview.ShowDialog(this);
                    }

                    if (_printCursor > 0 && _printCursor < _s.Count)
                        _lblMsg.Text = "排版打印已取消";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "打印出错：" + ex.Message, "排版打印",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>排版打印的 PrintPage：把 N 页画到一张纸上</summary>
        private void PrintNupPage(object sender, PrintPageEventArgs e)
        {
            if (_s == null || _printCursor >= _s.Count || _printCursor >= _printTo)
            {
                e.HasMorePages = false;
                return;
            }

            NupSettings cfg = _nup ?? new NupSettings();
            bool land = FirstPageLandscape();
            Rectangle[] cells = NupLayout.Compute(e.MarginBounds, cfg.PerSheet,
                cfg.Direction, cfg.GapMm, land);

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;

            int drawn = 0;
            for (int i = 0; i < cells.Length; i++)
            {
                int idx = _printCursor + i;
                if (idx >= _s.Count || idx >= _printTo) break;

                if (cfg.DrawBorder)
                {
                    using (Pen p = new Pen(Color.FromArgb(165, 170, 178), 1f))
                    {
                        p.DashStyle = DashStyle.Dash;
                        p.DashPattern = new float[] { 3f, 3f };
                        e.Graphics.DrawRectangle(p, cells[i]);
                    }
                }

                PrintPageToGraphics(e.Graphics, _s.Pages[idx], cells[i], cfg.OriginalSize);
                drawn++;
            }

            _printCursor += Math.Max(1, drawn);
            e.HasMorePages = _printCursor < _s.Count && _printCursor < _printTo;
        }

        private void AskPrint()
        {
            if (_s == null || _s.Count == 0) return;

            using (PrintDocument document = new PrintDocument())
            {
                document.DocumentName = _s.BasePath != null ? Path.GetFileName(_s.BasePath) : "PdfMini 文档";
                document.PrintPage += PrintOnePage;

                using (PrintDialog dialog = new PrintDialog())
                {
                    dialog.Document = document;
                    dialog.UseEXDialog = true;
                    dialog.AllowSomePages = true;
                    dialog.AllowSelection = false;
                    dialog.PrinterSettings.MinimumPage = 1;
                    dialog.PrinterSettings.MaximumPage = _s.Count;
                    dialog.PrinterSettings.FromPage = 1;
                    dialog.PrinterSettings.ToPage = _s.Count;
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;

                    int from = Math.Max(1, dialog.PrinterSettings.FromPage);
                    int to = Math.Min(_s.Count, dialog.PrinterSettings.ToPage);
                    if (to < from) { from = 1; to = _s.Count; }

                    _printCursor = from;
                    _printTo = to;
                    try
                    {
                        document.Print();
                        _lblMsg.Text = "已发送打印：" + document.PrinterSettings.PrinterName
                            + "  第 " + from + "-" + to + " 页";
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this, "打印失败：" + ex.Message, "PdfMini",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private void PrintOnePage(object sender, PrintPageEventArgs e)
        {
            int index = _printCursor - 1;
            if (_s == null || index < 0 || index >= _s.Count || _printCursor > _printTo)
            {
                e.HasMorePages = false;
                return;
            }
            PrintPageToGraphics(e.Graphics, _s.Pages[index], e.MarginBounds);
            _printCursor++;
            e.HasMorePages = _printCursor <= _printTo;
        }

        /// <summary>把一页画到打印 Graphics 上：300dpi 分条带渲染，峰值内存只一条带子。</summary>
        /// <param name="originalSize">true = 尽量按 100% 原始大小（放不下时仍等比缩小）</param>
        private void PrintPageToGraphics(Graphics g, PageRef pr, Rectangle margin)
        {
            PrintPageToGraphics(g, pr, margin, false);
        }

        private void PrintPageToGraphics(Graphics g, PageRef pr, Rectangle margin, bool originalSize)
        {
            const float printDpi = 300f;
            const int bandHeight = 768;

            lock (_s.Sync)
            {
                IntPtr page = PdfApi.FPDF_LoadPage(pr.Doc.Handle, pr.Index);
                if (page == IntPtr.Zero) return;
                try
                {
                    float pw = PdfApi.FPDF_GetPageWidthF(page);
                    float ph = PdfApi.FPDF_GetPageHeightF(page);
                    if (pw <= 0f || ph <= 0f) return;

                    bool swap = (pr.Rot % 180) != 0;
                    float wPt = swap ? ph : pw;
                    float hPt = swap ? pw : ph;

                    float scale = printDpi / 72f;
                    int fullW = (int)Math.Round(wPt * scale);
                    int fullH = (int)Math.Round(hPt * scale);
                    if (fullW < 1 || fullH < 1) return;

                    // e.MarginBounds 单位是 1/100 英寸；页尺寸也换算成 1/100 英寸
                    float pageW = wPt / 72f * 100f;
                    float pageH = hPt / 72f * 100f;
                    float fit = Math.Min(margin.Width / pageW, margin.Height / pageH);
                    if (originalSize)
                        fit = Math.Min(1f, fit);   // 原始大小：放不下才缩
                    float drawW = pageW * fit;
                    float drawH = pageH * fit;
                    float x = margin.X + (margin.Width - drawW) / 2f;
                    float y = margin.Y + (margin.Height - drawH) / 2f;

                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                    for (int top = 0; top < fullH; top += bandHeight)
                    {
                        using (Bitmap band = PdfApi.RenderBand(page, scale, pr.Rot / 90, fullW, fullH, top, bandHeight))
                        {
                            float bandY = y + drawH * top / fullH;
                            float bandH = drawH * band.Height / fullH;
                            g.DrawImage(band, new RectangleF(x, bandY, drawW, bandH));
                        }
                    }
                }
                finally
                {
                    PdfApi.FPDF_ClosePage(page);
                }
            }
        }

        // ───────────────────────────── 缩略图 ─────────────────────────────

        private static string ThumbKey(PageRef pr)
        {
            return pr.Doc.Path + "|" + pr.Index.ToString(CultureInfo.InvariantCulture) + "|" + pr.Rot.ToString(CultureInfo.InvariantCulture);
        }

        private void RebuildThumbs()
        {
            _thumbGen++;
            int gen = _thumbGen;
            _thumbCursor = 0;

            _suppressSel = true;
            _thumbs.BeginUpdate();
            _thumbs.Items.Clear();
            _thumbImgs.Images.Clear();
            _thumbs.EndUpdate();
            _suppressSel = false;

            LoadOutline();

            if (_s == null || _s.Count == 0) return;

            Thread t = new Thread(delegate () { ThumbLoop(gen); });
            t.IsBackground = true;
            t.Priority = ThreadPriority.BelowNormal;
            t.Start();
        }

        private void ThumbLoop(int gen)
        {
            while (true)
            {
                int index;
                PageRef pr;
                if (gen != _thumbGen) return;
                Session s = _s;
                if (s == null) return;

                lock (s.Sync)
                {
                    if (gen != _thumbGen) return;
                    if (_thumbCursor >= s.Count) return;
                    index = _thumbCursor++;
                    pr = s.Pages[index];
                }

                Bitmap thumb = null;
                string key = ThumbKey(pr);
                lock (_cacheLock)
                {
                    Bitmap hit;
                    if (_thumbCache.TryGetValue(key, out hit)) thumb = new Bitmap(hit);
                }
                if (thumb == null)
                {
                    try
                    {
                        lock (s.Sync)
                        {
                            if (pr.Doc.Handle != IntPtr.Zero)
                            {
                                IntPtr page = PdfApi.FPDF_LoadPage(pr.Doc.Handle, pr.Index);
                                if (page != IntPtr.Zero)
                                {
                                    thumb = MakeThumb(page, pr.Rot);
                                    PdfApi.FPDF_ClosePage(page);
                                }
                            }
                        }
                    }
                    catch { thumb = null; }

                    if (thumb != null)
                    {
                        lock (_cacheLock)
                        {
                            if (_thumbCache.Count > 400)
                            {
                                foreach (Bitmap b in _thumbCache.Values) b.Dispose();
                                _thumbCache.Clear();
                            }
                            _thumbCache[key] = new Bitmap(thumb);
                        }
                    }
                }
                if (thumb == null) continue;

                Bitmap payload = thumb;
                int idx = index;
                if (!IsHandleCreated) return;
                try
                {
                    BeginInvoke((Action)delegate
                    {
                        if (gen == _thumbGen) AddThumbItem(idx, payload);
                    });
                }
                catch
                {
                    return;
                }
            }
        }

        private int _thumbCursor;

        private Bitmap MakeThumb(IntPtr page, int rot)
        {
            float pw = PdfApi.FPDF_GetPageWidthF(page);
            float ph = PdfApi.FPDF_GetPageHeightF(page);
            if (pw <= 0f || ph <= 0f) return null;
            bool swap = (rot % 180) != 0;
            float wPt = swap ? ph : pw;
            float hPt = swap ? pw : ph;

            int margin = Math.Max(2, (int)(4 * _dpi));
            float scale = Math.Min((_thumbW - margin * 2) / wPt, (_thumbH - margin * 2) / hPt);
            Bitmap inner = PdfApi.RenderPage(page, scale, rot / 90, false);
            if (inner == null) return null;

            Bitmap canvas = new Bitmap(_thumbW, _thumbH);
            using (Graphics g = Graphics.FromImage(canvas))
            {
                g.Clear(Color.FromArgb(58, 58, 62));
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.DrawImage(inner, (_thumbW - inner.Width) / 2, (_thumbH - inner.Height) / 2);
            }
            inner.Dispose();
            return canvas;
        }

        private void AddThumbItem(int pageIndex, Bitmap thumb)
        {
            if (_s == null || pageIndex >= _s.Count) return;
            _thumbImgs.Images.Add(thumb);
            int imgIndex = _thumbImgs.Images.Count - 1;
            ListViewItem item = new ListViewItem((pageIndex + 1).ToString(CultureInfo.InvariantCulture), imgIndex);
            item.Tag = pageIndex;
            item.ToolTipText = "第 " + (pageIndex + 1).ToString(CultureInfo.InvariantCulture) + " 页";
            _thumbs.Items.Add(item);
            if (pageIndex == _cur) SelectThumb(pageIndex);
        }

        private void SelectThumb(int index)
        {
            if (index < 0 || index >= _thumbs.Items.Count) return;
            _suppressSel = true;
            _thumbs.SelectedIndices.Clear();
            _thumbs.Items[index].Selected = true;
            _thumbs.Items[index].Focused = true;
            _thumbs.EnsureVisible(index);
            _suppressSel = false;
        }

        private void ThumbSelectionChanged(object sender, EventArgs e)
        {
            if (_suppressSel || _s == null || _s.Count == 0) return;
            if (_thumbs.SelectedIndices.Count == 0) return;
            int idx = _thumbs.SelectedIndices[_thumbs.SelectedIndices.Count - 1];
            if (idx < 0 || idx >= _s.Count || idx == _cur) return;
            _cur = idx;
            RememberPage();
            RenderCurrent();
        }

        private List<int> SelectedPageIndices()
        {
            List<int> r = new List<int>();
            foreach (int i in _thumbs.SelectedIndices)
            {
                if (i >= 0 && _s != null && i < _s.Count) r.Add(i);
            }
            r.Sort();
            if (r.Count == 0 && _s != null && _s.Count > 0) r.Add(_cur);
            return r;
        }

        // ───────────────────────────── 页面操作 ─────────────────────────────

        private void NextPage() { _view.ScrollByPage(1); }
        private void PrevPage() { _view.ScrollByPage(-1); }

        private void GoTo(int index)
        {
            if (_s == null || _s.Count == 0) return;
            if (index < 0) index = 0;
            if (index >= _s.Count) index = _s.Count - 1;
            _cur = index;
            RememberPage();
            _view.ScrollToPage(index);
            UpdateStatus();
            SelectThumb(index);
        }

        private void DeleteSelected()
        {
            if (_s == null || _s.Count == 0) return;
            List<int> sel = SelectedPageIndices();
            if (sel.Count == 0) return;
            if (sel.Count >= _s.Count)
            {
                MessageBox.Show(this, "不能删掉全部页面，PDF 至少要留 1 页。", "PdfMini",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (MessageBox.Show(this, "删除选中的 " + sel.Count + " 页？\n（原始文件不会被改动，需要另存为新文件）",
                    "PdfMini", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;

            for (int i = sel.Count - 1; i >= 0; i--) _s.Pages.RemoveAt(sel[i]);
            _s.Dirty = true;
            _cur = Math.Min(sel[0], _s.Count - 1);
            RefreshDocumentView();
            RebuildThumbs();
            SelectThumb(_cur);
        }

        private void MoveSelected(int direction)
        {
            if (_s == null || _s.Count < 2) return;
            List<int> sel = SelectedPageIndices();
            if (sel.Count == 0) return;
            if (direction < 0 && sel[0] == 0) return;
            if (direction > 0 && sel[sel.Count - 1] == _s.Count - 1) return;

            if (direction < 0)
            {
                for (int i = 0; i < sel.Count; i++)
                {
                    PageRef tmp = _s.Pages[sel[i] - 1];
                    _s.Pages[sel[i] - 1] = _s.Pages[sel[i]];
                    _s.Pages[sel[i]] = tmp;
                }
            }
            else
            {
                for (int i = sel.Count - 1; i >= 0; i--)
                {
                    PageRef tmp = _s.Pages[sel[i] + 1];
                    _s.Pages[sel[i] + 1] = _s.Pages[sel[i]];
                    _s.Pages[sel[i]] = tmp;
                }
            }

            _s.Dirty = true;
            List<int> newSel = new List<int>();
            for (int i = 0; i < sel.Count; i++) newSel.Add(sel[i] + direction);
            _cur = Math.Max(0, Math.Min(_s.Count - 1, _cur + (sel.Contains(_cur) ? direction : 0)));

            RefreshDocumentView();
            RebuildThumbs();
            SelectThumb(_cur);
        }

        private void RotateSelected(int delta)
        {
            if (_s == null || _s.Count == 0) return;
            List<int> sel = SelectedPageIndices();
            for (int i = 0; i < sel.Count; i++)
            {
                PageRef pr = _s.Pages[sel[i]];
                pr.Rot = ((pr.Rot + delta) % 360 + 360) % 360;
            }
            _s.Dirty = true;
            RefreshDocumentView();
            RebuildThumbs();
            SelectThumb(_cur);
        }

        // ───────────────────────────── 合并 / 保存 ─────────────────────────────

        private void AskMerge(bool insertHere)
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Filter = "PDF 文件 (*.pdf)|*.pdf";
                dlg.Multiselect = true;
                dlg.Title = insertHere ? "插入 PDF（放在当前页之后）" : "合并 PDF（追加到末尾）";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                MergeFiles(dlg.FileNames, insertHere);
            }
        }

        private void MergeFiles(string[] files, bool insertHere)
        {
            if (_s == null) _s = new Session();
            int insertAt = insertHere && _s.Count > 0 ? Math.Min(_cur + 1, _s.Count) : _s.Count;
            int added = 0;
            int skipped = 0;

            for (int f = 0; f < files.Length; f++)
            {
                if (!IsPdf(files[f]) || !File.Exists(files[f])) continue;

                IntPtr handle = IntPtr.Zero;
                string pwd = null;
                while (true)
                {
                    handle = PdfApi.FPDF_LoadDocument(PdfApi.Utf8Z(files[f]), PdfApi.Utf8Z(pwd));
                    if (handle != IntPtr.Zero) break;
                    if (PdfApi.FPDF_GetLastError() != PdfApi.ErrPassword) break;
                    pwd = AskPassword(files[f]);
                    if (pwd == null) break;
                }
                if (handle == IntPtr.Zero) { skipped++; continue; }

                int n = PdfApi.FPDF_GetPageCount(handle);
                if (n <= 0) { PdfApi.FPDF_CloseDocument(handle); skipped++; continue; }

                PdfDoc doc = new PdfDoc(handle, files[f]);
                _s.Docs.Add(doc);
                for (int k = 0; k < n; k++) _s.Pages.Insert(insertAt + added++, new PageRef(doc, k));
            }

            if (added == 0)
            {
                MessageBox.Show(this, "没有加入任何页面。", "PdfMini", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            _s.Dirty = true;
            if (insertHere) _cur = insertAt;
            RefreshDocumentView();
            RebuildThumbs();
            SelectThumb(_cur);
            _lblMsg.Text = "已加入 " + added + " 页" + (skipped > 0 ? "（" + skipped + " 个文件失败）" : "");
        }

        private bool SaveRefs(List<PageRef> refs, string path)
        {
            IntPtr dest = PdfApi.FPDF_CreateNewDocument();
            if (dest == IntPtr.Zero) return false;
            try
            {
                int at = 0;
                int i = 0;
                while (i < refs.Count)
                {
                    PageRef first = refs[i];
                    int j = i;
                    while (j + 1 < refs.Count && object.ReferenceEquals(refs[j + 1].Doc, first.Doc)
                           && refs[j + 1].Index == refs[j].Index + 1) j++;

                    string range = j > i
                        ? (first.Index + 1).ToString(CultureInfo.InvariantCulture) + "-" + (refs[j].Index + 1).ToString(CultureInfo.InvariantCulture)
                        : (first.Index + 1).ToString(CultureInfo.InvariantCulture);

                    if (PdfApi.FPDF_ImportPages(dest, first.Doc.Handle, PdfApi.AsciiZ(range), at) == 0) return false;
                    at += (j - i + 1);
                    i = j + 1;
                }

                for (int k = 0; k < refs.Count; k++)
                {
                    int quarter = refs[k].Rot / 90;
                    if (quarter == 0) continue;
                    IntPtr page = PdfApi.FPDF_LoadPage(dest, k);
                    if (page == IntPtr.Zero) continue;
                    try
                    {
                        int cur = PdfApi.FPDFPage_GetRotation(page);
                        if (cur < 0) cur = 0;
                        PdfApi.FPDFPage_SetRotation(page, (cur + quarter) % 4);
                    }
                    finally
                    {
                        PdfApi.FPDF_ClosePage(page);
                    }
                }

                return PdfWriter.Save(dest, path);
            }
            finally
            {
                PdfApi.FPDF_CloseDocument(dest);
            }
        }

        private void AskSaveAs()
        {
            if (_s == null || _s.Count == 0) return;

            using (SaveFileDialog dlg = new SaveFileDialog())
            {
                dlg.Filter = "PDF 文件 (*.pdf)|*.pdf";
                dlg.Title = "另存为";
                dlg.FileName = SuggestName(string.Empty);
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                string target = dlg.FileName;
                if (!target.ToLowerInvariant().EndsWith(".pdf")) target += ".pdf";

                bool sameFile = _s.BasePath != null &&
                    string.Equals(Path.GetFullPath(target), Path.GetFullPath(_s.BasePath), StringComparison.OrdinalIgnoreCase);

                string writeTo = sameFile ? target + ".tmp" : target;
                bool ok;
                lock (_s.Sync)
                {
                    ok = SaveRefs(new List<PageRef>(_s.Pages), writeTo);
                }
                if (!ok)
                {
                    if (sameFile && File.Exists(writeTo)) { try { File.Delete(writeTo); } catch { } }
                    MessageBox.Show(this, "保存失败，可能文件被占用或目录不可写。", "PdfMini",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                if (sameFile)
                {
                    try
                    {
                        if (File.Exists(target)) File.Copy(target, target + ".bak", true);
                        File.Delete(target);
                        File.Move(writeTo, target);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this, "替换原文件失败：" + ex.Message + "\n临时文件在：\n" + writeTo,
                            "PdfMini", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                }

                OpenFile(target);
                _lblMsg.Text = "已保存：" + target;
            }
        }

        private void ExportSelected()
        {
            if (_s == null || _s.Count == 0) return;
            List<int> sel = SelectedPageIndices();
            if (sel.Count == 0) return;

            List<PageRef> refs = new List<PageRef>();
            for (int i = 0; i < sel.Count; i++) refs.Add(_s.Pages[sel[i]]);

            using (SaveFileDialog dlg = new SaveFileDialog())
            {
                dlg.Filter = "PDF 文件 (*.pdf)|*.pdf";
                dlg.Title = "导出选中的 " + sel.Count + " 页";
                dlg.FileName = SuggestName("_选中页");
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                string target = dlg.FileName;
                if (!target.ToLowerInvariant().EndsWith(".pdf")) target += ".pdf";

                bool ok;
                lock (_s.Sync)
                {
                    ok = SaveRefs(refs, target);
                }
                if (!ok)
                {
                    MessageBox.Show(this, "导出失败。", "PdfMini", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                _lblMsg.Text = "已导出 " + sel.Count + " 页 → " + Path.GetFileName(target);
            }
        }

        private string SuggestName(string suffix)
        {
            string baseName = _s != null && _s.BasePath != null
                ? Path.GetFileNameWithoutExtension(_s.BasePath)
                : "合并结果";
            return baseName + suffix + ".pdf";
        }

        // ───────────────────────────── 文字 ─────────────────────────────

        private void CopyPageText()
        {
            if (_s == null || _s.Count == 0) return;
            string text = string.Empty;
            lock (_s.Sync)
            {
                PageRef pr = _s.Pages[_cur];
                text = PdfApi.PageText(pr.Doc.Handle, pr.Index);
            }
            if (string.IsNullOrEmpty(text))
            {
                _lblMsg.Text = "这一页没有文字层（可能是扫描件）";
                return;
            }
            try
            {
                Clipboard.SetText(text);
                _lblMsg.Text = "已复制本页文字（" + text.Length + " 字符）";
            }
            catch (Exception ex)
            {
                _lblMsg.Text = "复制失败：" + ex.Message;
            }
        }

        private void DoFind(string needle)
        {
            needle = (needle ?? string.Empty).Trim();
            // 同一个词再按一次「找」= 跳到下一个命中页（循环）
            if (needle.Length > 0 && needle == _hitQuery && _hits.Count > 0)
            {
                JumpToNextHitPage();
                return;
            }
            SearchAndHighlight(needle, true);
        }

        /// <summary>按命中页顺序跳到下一个（到头回到第一个）。</summary>
        private void JumpToNextHitPage()
        {
            List<int> pages = new List<int>(_hits.Keys);
            pages.Sort();
            if (pages.Count == 0) return;

            int target = pages[0];
            for (int i = 0; i < pages.Count; i++)
            {
                if (pages[i] > _cur) { target = pages[i]; break; }
            }

            GoTo(target);
            int ordinal = pages.IndexOf(target) + 1;
            _lblMsg.Text = "“" + _hitQuery + "” 命中 " + pages.Count + " 页，当前第 " + ordinal
                + " 个（第 " + (target + 1) + " 页），已在页面上高亮";
        }

        /// <summary>扫全文并算高亮矩形；jumpToFirst=true 时跳到第一个命中页。</summary>
        private void SearchAndHighlight(string needle, bool jumpToFirst)
        {
            if (_s == null || _s.Count == 0) return;
            needle = (needle ?? string.Empty).Trim();
            if (needle.Length == 0)
            {
                ClearHighlights();
                _lblMsg.Text = "已清除高亮";
                return;
            }

            _hits.Clear();
            _hitQuery = needle;

            int firstHit = -1;
            int total = 0;
            int start = _cur;

            Cursor previous = Cursor;
            Cursor = Cursors.WaitCursor;
            try
            {
                for (int step = 0; step < _s.Count; step++)
                {
                    int i = (start + step) % _s.Count;
                    PageRef pr = _s.Pages[i];
                    List<RectangleF> rects = null;
                    int matches = 0;

                    lock (_s.Sync)
                    {
                        IntPtr page = PdfApi.FPDF_LoadPage(pr.Doc.Handle, pr.Index);
                        if (page != IntPtr.Zero)
                        {
                            try
                            {
                                IntPtr textPage = PdfApi.FPDFText_LoadPage(page);
                                if (textPage != IntPtr.Zero)
                                {
                                    try
                                    {
                                        float pw = PdfApi.FPDF_GetPageWidthF(page);
                                        float ph = PdfApi.FPDF_GetPageHeightF(page);
                                        List<RectangleF> raw = PdfApi.FindHitRects(textPage, needle, 800, out matches);
                                        if (matches > 0 && raw.Count > 0)
                                        {
                                            rects = new List<RectangleF>(raw.Count);
                                            for (int r = 0; r < raw.Count; r++)
                                            {
                                                RectangleF src = raw[r];
                                                // pdfium 的 FPDFText_GetRect 用“页面坐标”（y 向上为正），
                                                // 渲染位图是 y 向下为正 —— 先翻 y，再按额外旋转映射。
                                                RectangleF topDown = RectangleF.FromLTRB(
                                                    src.Left, ph - src.Bottom, src.Right, ph - src.Top);
                                                rects.Add(MapRectToDisplay(topDown, pr.Rot, pw, ph));
                                            }
                                        }
                                    }
                                    finally
                                    {
                                        PdfApi.FPDFText_ClosePage(textPage);
                                    }
                                }
                            }
                            finally
                            {
                                PdfApi.FPDF_ClosePage(page);
                            }
                        }
                    }

                    if (matches > 0)
                    {
                        total += matches;
                        if (rects != null && rects.Count > 0) _hits[i] = rects;
                        if (firstHit < 0) firstHit = i;
                    }
                }
            }
            finally
            {
                Cursor = previous;
            }

            if (firstHit < 0)
            {
                ClearHighlights();
                _lblMsg.Text = "没找到：“" + needle + "”";
                return;
            }

            if (jumpToFirst) GoTo(firstHit);
            _view.Invalidate();
            _lblMsg.Text = "“" + needle + "” 共 " + total + " 处，已在页面上高亮；第一处在第 " + (firstHit + 1) + " 页";
        }

        /// <summary>
        /// pdfium 给的是“未旋转页面坐标”，而显示用的是旋转后的位图，
        /// 所以要把命中矩形按页面的额外旋转一起转过去（单位仍是 pt）。
        /// </summary>
        private static RectangleF MapRectToDisplay(RectangleF r, int rotationDegrees, float pageW, float pageH)
        {
            float x1, y1, x2, y2;
            switch (((rotationDegrees % 360) + 360) % 360)
            {
                case 90:   // (x,y) -> (H - y, x)
                    x1 = pageH - r.Top; y1 = r.Left;
                    x2 = pageH - r.Bottom; y2 = r.Right;
                    break;
                case 180:  // (x,y) -> (W - x, H - y)
                    x1 = pageW - r.Left; y1 = pageH - r.Top;
                    x2 = pageW - r.Right; y2 = pageH - r.Bottom;
                    break;
                case 270:  // (x,y) -> (y, W - x)
                    x1 = r.Top; y1 = pageW - r.Left;
                    x2 = r.Bottom; y2 = pageW - r.Right;
                    break;
                default:
                    x1 = r.Left; y1 = r.Top; x2 = r.Right; y2 = r.Bottom;
                    break;
            }
            return RectangleF.FromLTRB(Math.Min(x1, x2), Math.Min(y1, y2), Math.Max(x1, x2), Math.Max(y1, y2));
        }

        private void ClearHighlights()
        {
            if (_hits.Count == 0 && string.IsNullOrEmpty(_hitQuery)) return;
            _hits.Clear();
            _hitQuery = null;
            if (_view != null) _view.Invalidate();
        }

        // ───────────────────────────── 状态 / 记忆 ─────────────────────────────

        private void UpdateStatus()
        {
            if (_s == null || _s.Count == 0)
            {
                _lblPage.Text = "未打开文件";
                _lblZoom.Text = "";
                _lblFile.Text = "";
                Text = "PdfMini";
                return;
            }
            _lblPage.Text = (_cur + 1) + " / " + _s.Count;
            _lblZoom.Text = Math.Round(_zoom * 100) + "%";

            string name = _s.BasePath == null ? "未保存的新文档" : Path.GetFileName(_s.BasePath);
            _lblFile.Text = (_s.Dirty ? "● " : "") + name;
            Text = "PdfMini — " + name + "  (" + (_cur + 1) + "/" + _s.Count + ")" + (_s.Dirty ? " *" : "");
        }

        private void RememberPage()
        {
            if (_s == null || _s.BasePath == null || _s.Dirty) return;
            _lastPage[FullKey(_s.BasePath)] = _cur;
        }

        private static string FullKey(string path)
        {
            try { return Path.GetFullPath(path); }
            catch { return path; }
        }

        private static string StateFile
        {
            get
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PdfMini");
                return Path.Combine(dir, "state.txt");
            }
        }

        private static void LoadState()
        {
            if (_stateLoaded) return;
            _stateLoaded = true;
            try
            {
                string file = StateFile;
                if (!File.Exists(file)) return;
                string[] lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    int tab = line.IndexOf('\t');
                    if (tab <= 0) continue;
                    int page;
                    if (!int.TryParse(line.Substring(tab + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out page)) continue;
                    string path = Encoding.UTF8.GetString(Convert.FromBase64String(line.Substring(0, tab)));
                    _lastPage[path] = page;
                }
            }
            catch { }
        }

        private static void SaveState()
        {
            try
            {
                string file = StateFile;
                string dir = Path.GetDirectoryName(file);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                StringBuilder sb = new StringBuilder();
                foreach (KeyValuePair<string, int> kv in _lastPage)
                {
                    sb.Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(kv.Key)));
                    sb.Append('\t');
                    sb.Append(kv.Value.ToString(CultureInfo.InvariantCulture));
                    sb.Append('\n');
                }
                File.WriteAllText(file, sb.ToString());
            }
            catch { }
        }

        // ───────────────────────────── 事件 ─────────────────────────────

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_resizeTimer != null)
            {
                _resizeTimer.Stop();
                _resizeTimer.Start();
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            int split = (int)(152 * _dpi);
            if (split > _split.Width / 2) split = Math.Max(120, _split.Width / 2);
            _split.SplitterDistance = split;

            if (!string.IsNullOrEmpty(_pendingOpen))
            {
                OpenFile(_pendingOpen);
                _view.Focus();
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Control && e.KeyCode == Keys.O) { AskOpen(); e.Handled = true; return; }
            if (e.Control && e.KeyCode == Keys.S) { AskSaveAs(); e.Handled = true; return; }
            if (e.Control && e.Shift && e.KeyCode == Keys.P) { AskNupPrint(); e.Handled = true; return; }
            if (e.Control && e.KeyCode == Keys.P) { AskPrint(); e.Handled = true; return; }
            if (e.Control && e.KeyCode == Keys.C) { CopyPageText(); e.Handled = true; return; }
            if (e.Control && e.KeyCode == Keys.F)
            {
                if (_txtFind != null) _txtFind.Focus();
                e.Handled = true;
                return;
            }
            if (e.KeyCode == Keys.Escape)
            {
                ClearHighlights();
                _lblMsg.Text = "";
                e.Handled = true;
                return;
            }
            if (e.Control && e.KeyCode == Keys.Up) { MoveSelected(-1); e.Handled = true; return; }
            if (e.Control && e.KeyCode == Keys.Down) { MoveSelected(1); e.Handled = true; return; }

            switch (e.KeyCode)
            {
                case Keys.PageDown:
                    if (!e.Control) { NextPage(); e.Handled = true; }
                    break;
                case Keys.PageUp:
                    if (!e.Control) { PrevPage(); e.Handled = true; }
                    break;
                case Keys.Delete:
                    DeleteSelected();
                    e.Handled = true;
                    break;
                case Keys.Home:
                    GoTo(0);
                    e.Handled = true;
                    break;
                case Keys.End:
                    GoTo(int.MaxValue);
                    e.Handled = true;
                    break;
                case Keys.Add:
                case Keys.Oemplus:
                    ZoomBy(1.15f);
                    e.Handled = true;
                    break;
                case Keys.Subtract:
                case Keys.OemMinus:
                    ZoomBy(1f / 1.15f);
                    e.Handled = true;
                    break;
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (_s != null && _s.Dirty && _s.Count > 0)
            {
                DialogResult r = MessageBox.Show(this, "有页面改动还没另存，直接退出会丢失这些改动。",
                    "PdfMini", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
                if (r != DialogResult.OK) { e.Cancel = true; return; }
            }
            if (_s != null && _s.BasePath != null && !_s.Dirty) _lastPage[FullKey(_s.BasePath)] = _cur;
            SaveState();
            CloseSession();
        }
    }

    /// <summary>密码输入小窗。</summary>
    internal sealed class PasswordDialog : Form
    {
        private readonly TextBox _box;

        public string Password { get { return _box.Text; } }

        public PasswordDialog(string file)
        {
            Text = "需要密码";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(400, 132);
            Font = new Font("Microsoft YaHei UI", 9f);

            Label tip = new Label();
            tip.Text = "这个 PDF 有密码保护：\n" + file;
            tip.AutoSize = false;
            tip.SetBounds(14, 12, 372, 40);
            tip.AutoEllipsis = true;

            _box = new TextBox();
            _box.UseSystemPasswordChar = true;
            _box.SetBounds(14, 56, 372, 26);

            Button ok = new Button();
            ok.Text = "确定";
            ok.DialogResult = DialogResult.OK;
            ok.SetBounds(226, 92, 78, 28);

            Button cancel = new Button();
            cancel.Text = "取消";
            cancel.DialogResult = DialogResult.Cancel;
            cancel.SetBounds(308, 92, 78, 28);

            Controls.Add(tip);
            Controls.Add(_box);
            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;
        }
    }
}
