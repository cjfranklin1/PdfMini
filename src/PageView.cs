using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PdfMini
{
    /// <summary>PageView 取页面内容的方式（由 MainForm 实现，内部自己加 pdfium 锁）。</summary>
    internal interface IPageSource
    {
        int PageCount { get; }
        /// <summary>页尺寸（pt），已含额外旋转。</summary>
        SizeF GetPageSizePt(int index);
        /// <summary>按 pixelsPerPoint 渲染一页，返回位图（失败返回 null）。</summary>
        Bitmap RenderPageAt(int index, float pixelsPerPoint);
        /// <summary>该页的搜索命中矩形（pt，已按显示旋转）；没有则返回 null。</summary>
        IList<RectangleF> GetHighlights(int index);
    }

    /// <summary>
    /// 连续滚动视图：整份文档纵向排布，只缓存可见页附近的位图（默认 4 张），
    /// 其余位置先画占位、由定时器逐张补渲，滚动不卡界面。
    /// </summary>
    internal sealed class PageView : Control
    {
        private const int CacheLimit = 4;
        private const string Watermark = "cjfranklin 工作室";

        private readonly float _dpi;
        private readonly float _gap;
        private readonly Font _wmFont;
        private readonly System.Windows.Forms.Timer _pump;

        private IPageSource _src;
        private readonly List<SizeF> _sizes = new List<SizeF>();
        private readonly List<float> _tops = new List<float>();
        private readonly Dictionary<int, Bitmap> _cache = new Dictionary<int, Bitmap>();
        private readonly List<int> _lru = new List<int>();

        private float _scale = 96f / 72f;    // 每 pt 多少像素
        private float _scrollY;
        private float _contentH;
        private int _cur = -1;
        private string _hint = "拖入 PDF 文件，或按 Ctrl+O 打开";

        private bool _dragging;
        private Point _dragFrom;
        private float _dragScroll;

        public event EventHandler CurrentPageChanged;
        public event EventHandler ZoomInRequested;
        public event EventHandler ZoomOutRequested;

        public PageView()
        {
            _dpi = DeviceDpi / 96f;
            _gap = (float)Math.Round(10 * _dpi);
            _wmFont = new Font("Microsoft YaHei UI", 8f);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = Color.FromArgb(45, 45, 48);
            TabStop = true;

            _pump = new System.Windows.Forms.Timer();
            _pump.Interval = 25;
            _pump.Tick += delegate { PumpRender(); };
        }

        public string Hint
        {
            get { return _hint; }
            set { _hint = value; Invalidate(); }
        }

        /// <summary>100% = 96dpi 实际大小。</summary>
        public float Zoom
        {
            get { return _scale / (96f / 72f); }
        }

        public float Scale
        {
            get { return _scale; }
        }

        public int CurrentPage
        {
            get { return _cur; }
        }

        public int PageCount
        {
            get { return _src == null ? 0 : _src.PageCount; }
        }

        public void SetSource(IPageSource source)
        {
            _src = source;
            _sizes.Clear();
            _scrollY = 0f;
            _cur = source == null || source.PageCount == 0 ? -1 : 0;
            ClearCache();
            Relayout();
            if (_src != null && _src.PageCount > 0) StartPump();
        }

        // ───────────────────────────── 布局 ─────────────────────────────

        public void Relayout()
        {
            _tops.Clear();
            if (_src == null || _src.PageCount == 0)
            {
                _contentH = 0f;
                _scrollY = 0f;
                Invalidate();
                return;
            }

            if (_sizes.Count != _src.PageCount)
            {
                _sizes.Clear();
                for (int i = 0; i < _src.PageCount; i++) _sizes.Add(_src.GetPageSizePt(i));
            }

            float y = 0f;
            for (int i = 0; i < _sizes.Count; i++)
            {
                _tops.Add(y);
                y += _sizes[i].Height * _scale + _gap;
            }
            _contentH = Math.Max(0f, y - _gap);
            ClampScroll();
            Invalidate();
        }

        private float PageHeightPx(int index)
        {
            return _sizes[index].Height * _scale;
        }

        private float PageWidthPx(int index)
        {
            return _sizes[index].Width * _scale;
        }

        public RectangleF PageRect(int index)
        {
            if (index < 0 || index >= _tops.Count) return RectangleF.Empty;
            float w = PageWidthPx(index);
            float h = PageHeightPx(index);
            float x = (ClientSize.Width - w) / 2f;
            float y = _tops[index] - _scrollY;
            return new RectangleF(x, y, w, h);
        }

        private void ClampScroll()
        {
            float max = Math.Max(0f, _contentH - ClientSize.Height);
            if (_scrollY > max) _scrollY = max;
            if (_scrollY < 0f) _scrollY = 0f;
        }

        // ───────────────────────────── 滚动 / 缩放 ─────────────────────────────

        public void ScrollBy(float dy)
        {
            if (_contentH <= 0f) return;
            float old = _scrollY;
            _scrollY += dy;
            ClampScroll();
            if (Math.Abs(old - _scrollY) > 0.01f) Invalidate();
            UpdateCurrent();
            StartPump();
        }

        public void ScrollToPage(int index)
        {
            if (_src == null || index < 0 || index >= _tops.Count) return;
            _scrollY = _tops[index];
            ClampScroll();
            Invalidate();
            UpdateCurrent();
            StartPump();
        }

        public void ScrollByPage(int direction)
        {
            float step = Math.Max(60f, ClientSize.Height * 0.86f);
            ScrollBy(direction * step);
        }

        public void SetScale(float scale, bool keepAnchor)
        {
            if (scale < 0.08f) scale = 0.08f;
            if (scale > 8f) scale = 8f;
            if (Math.Abs(scale - _scale) < 0.0005f)
            {
                Invalidate();
                StartPump();
                return;
            }

            int anchorPage = -1;
            float anchorFrac = 0f;
            if (keepAnchor) GetAnchor(out anchorPage, out anchorFrac);

            _scale = scale;
            ClearCache();
            Relayout();

            if (keepAnchor && anchorPage >= 0)
            {
                _scrollY = _tops[anchorPage] + anchorFrac * PageHeightPx(anchorPage) - ClientSize.Height / 2f;
                ClampScroll();
            }
            Invalidate();
            UpdateCurrent();
            StartPump();
        }

        /// <summary>取视口中心所在的页与页内相对位置（0~1）。</summary>
        private void GetAnchor(out int page, out float frac)
        {
            page = -1;
            frac = 0f;
            if (_tops.Count == 0) return;
            float center = _scrollY + ClientSize.Height / 2f;
            int idx = 0;
            for (int i = 0; i < _tops.Count; i++)
            {
                if (_tops[i] <= center) idx = i;
                else break;
            }
            page = idx;
            float h = Math.Max(1f, PageHeightPx(idx));
            frac = Math.Min(1f, Math.Max(0f, (center - _tops[idx]) / h));
        }

        private void UpdateCurrent()
        {
            if (_src == null || _tops.Count == 0) return;
            int page;
            float frac;
            GetAnchor(out page, out frac);
            if (page < 0) return;
            if (page != _cur)
            {
                _cur = page;
                EventHandler h = CurrentPageChanged;
                if (h != null) h(this, EventArgs.Empty);
            }
        }

        // ───────────────────────────── 渲染泵 ─────────────────────────────

        public void InvalidateAll()
        {
            ClearCache();
            _sizes.Clear();
            Relayout();
            StartPump();
        }

        private void StartPump()
        {
            if (_src == null || _src.PageCount == 0) return;
            if (!_pump.Enabled) _pump.Start();
        }

        private void PumpRender()
        {
            if (_src == null || _src.PageCount == 0 || _tops.Count == 0)
            {
                _pump.Stop();
                return;
            }

            int first, last;
            VisibleRange(out first, out last);
            int center = _cur >= 0 ? _cur : (first + last) / 2;

            // 1) 先把“看得见但还没渲”的页补上（每 tick 一页）
            int target = -1;
            int bestDistance = int.MaxValue;
            for (int i = first; i <= last; i++)
            {
                if (_cache.ContainsKey(i)) continue;
                int d = Math.Abs(i - center);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    target = i;
                }
            }

            bool prefetch = false;
            if (target < 0)
            {
                // 2) 可见页都齐了：只在缓存放得下时预取一张相邻页，然后停泵。
                //    绝不在“渲完立刻被淘汰”的状态下循环重渲（那会无限烧 CPU）。
                if (_cache.Count < CacheLimit)
                {
                    for (int off = 1; off <= 2 && target < 0; off++)
                    {
                        if (first - off >= 0 && !_cache.ContainsKey(first - off)) target = first - off;
                        else if (last + off < _src.PageCount && !_cache.ContainsKey(last + off)) target = last + off;
                    }
                    prefetch = target >= 0;
                }
            }

            if (target < 0)
            {
                _pump.Stop();   // 都渲染完了，歇着
                return;
            }

            Bitmap bmp = null;
            try
            {
                bmp = _src.RenderPageAt(target, _scale);
            }
            catch
            {
                bmp = null;
            }

            if (bmp == null)
            {
                _pump.Stop();
                return;
            }

            Bitmap old;
            if (_cache.TryGetValue(target, out old)) old.Dispose();
            _cache[target] = bmp;
            TouchLru(target);
            Evict();
            Invalidate();

            if (prefetch) _pump.Stop();   // 预取只做一张，避免空转
        }

        private void VisibleRange(out int first, out int last)
        {
            first = 0;
            last = -1;
            if (_tops.Count == 0) return;

            float top = _scrollY;
            float bottom = _scrollY + ClientSize.Height;

            first = -1;
            for (int i = 0; i < _tops.Count; i++)
            {
                float pageTop = _tops[i];
                float pageBottom = pageTop + PageHeightPx(i);
                if (pageBottom < top) continue;
                if (pageTop > bottom) break;
                if (first < 0) first = i;
                last = i;
            }
            if (first < 0)
            {
                first = 0;
                last = 0;
            }
        }

        private void TouchLru(int index)
        {
            _lru.Remove(index);
            _lru.Add(index);
        }

        private void Evict()
        {
            if (_cache.Count <= CacheLimit) return;
            int first, last;
            VisibleRange(out first, out last);
            for (int i = 0; i < _lru.Count && _cache.Count > CacheLimit; i++)
            {
                int idx = _lru[i];
                if (idx >= first - 1 && idx <= last + 1) continue;   // 可见区附近一律不淘汰
                Bitmap bmp;
                if (_cache.TryGetValue(idx, out bmp))
                {
                    _cache.Remove(idx);
                    bmp.Dispose();
                }
                _lru.RemoveAt(i);
                i--;
            }
        }

        private void ClearCache()
        {
            foreach (Bitmap b in _cache.Values) b.Dispose();
            _cache.Clear();
            _lru.Clear();
        }

        // ───────────────────────────── 绘制 ─────────────────────────────

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);

            if (_src == null || _src.PageCount == 0)
            {
                TextRenderer.DrawText(g, _hint, Font, ClientRectangle, Color.Gainsboro,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                DrawWatermark(g, RectangleF.Empty);
                return;
            }

            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;

            int first, last;
            VisibleRange(out first, out last);
            for (int i = first; i <= last; i++)
            {
                RectangleF r = PageRect(i);
                Bitmap bmp;
                bool cached = _cache.TryGetValue(i, out bmp);
                if (!cached) bmp = null;

                using (SolidBrush shadow = new SolidBrush(Color.FromArgb(80, 0, 0, 0)))
                    g.FillRectangle(shadow, r.X + 3f, r.Y + 4f, r.Width, r.Height);

                if (bmp != null)
                {
                    g.DrawImage(bmp, r);
                    if (i == _cur)
                    {
                        using (Pen p = new Pen(Color.FromArgb(90, 60, 140, 220), Math.Max(1f, _dpi)))
                            g.DrawRectangle(p, r.X, r.Y, r.Width, r.Height);
                    }
                }
                else
                {
                    using (SolidBrush white = new SolidBrush(Color.FromArgb(250, 250, 252)))
                        g.FillRectangle(white, r);
                    using (Pen p = new Pen(Color.FromArgb(60, 0, 0, 0)))
                        g.DrawRectangle(p, r.X, r.Y, r.Width, r.Height);
                    string label = "第 " + (i + 1) + " 页 · 渲染中";
                    TextRenderer.DrawText(g, label, Font, Rectangle.Round(r), Color.FromArgb(150, 150, 155),
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                }

                DrawHighlights(g, i, r);
            }

            RectangleF centerPage = PageRect(_cur);
            DrawWatermark(g, centerPage);
        }

        /// <summary>搜索命中高亮：矩形是 pt 坐标（已含旋转），按当前比例换算成像素。</summary>
        private void DrawHighlights(Graphics g, int index, RectangleF pageRect)
        {
            if (_src == null) return;
            IList<RectangleF> rects = _src.GetHighlights(index);
            if (rects == null || rects.Count == 0) return;

            using (SolidBrush fill = new SolidBrush(Color.FromArgb(88, 255, 208, 0)))
            using (Pen edge = new Pen(Color.FromArgb(150, 226, 150, 0), 1f))
            {
                for (int i = 0; i < rects.Count; i++)
                {
                    RectangleF r = rects[i];
                    RectangleF d = new RectangleF(
                        pageRect.X + r.X * _scale,
                        pageRect.Y + r.Y * _scale,
                        Math.Max(1.5f, r.Width * _scale),
                        Math.Max(1.5f, r.Height * _scale));
                    g.FillRectangle(fill, d);
                    g.DrawRectangle(edge, d.X, d.Y, d.Width, d.Height);
                }
            }
        }

        /// <summary>右下角小水印：压在页面上时极淡，落在页外空白处淡浅灰。</summary>
        private void DrawWatermark(Graphics g, RectangleF pageRect)
        {
            Size sz = TextRenderer.MeasureText(Watermark, _wmFont);
            int x = ClientSize.Width - sz.Width - (int)(14 * _dpi);
            int y = ClientSize.Height - sz.Height - (int)(7 * _dpi);
            if (x < 4 || y < 4) return;

            bool overPage = pageRect.Width > 0f && pageRect.IntersectsWith(new RectangleF(x, y, sz.Width, sz.Height));
            Color color = overPage
                ? Color.FromArgb(42, 64, 64, 64)
                : Color.FromArgb(105, 176, 184, 192);
            TextRenderer.DrawText(g, Watermark, _wmFont, new Point(x, y), color, TextFormatFlags.NoPrefix);
        }

        // ───────────────────────────── 输入 ─────────────────────────────

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if ((ModifierKeys & Keys.Control) == Keys.Control)
            {
                if (e.Delta > 0) OnZoomIn(); else OnZoomOut();
                return;
            }
            if (_src == null) return;
            ScrollBy(-e.Delta * 1.6f);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            if (e.Button == MouseButtons.Left && _src != null)
            {
                _dragging = true;
                _dragFrom = e.Location;
                _dragScroll = _scrollY;
                Cursor = Cursors.Hand;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_dragging) return;
            _scrollY = _dragScroll - (e.Y - _dragFrom.Y);
            ClampScroll();
            Invalidate();
            UpdateCurrent();
            StartPump();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _dragging = false;
            Cursor = Cursors.Default;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            ClampScroll();
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _pump.Stop();
                _pump.Dispose();
                ClearCache();
                if (_wmFont != null) _wmFont.Dispose();
            }
            base.Dispose(disposing);
        }

        private void OnZoomIn() { EventHandler h = ZoomInRequested; if (h != null) h(this, EventArgs.Empty); }
        private void OnZoomOut() { EventHandler h = ZoomOutRequested; if (h != null) h(this, EventArgs.Empty); }
    }
}
