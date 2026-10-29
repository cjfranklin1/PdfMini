using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Printing;
using System.Globalization;
using System.Windows.Forms;

namespace PdfMini
{
    /// <summary>排列方向</summary>
    internal enum NupDirection
    {
        Auto = 0,        // 按页面长宽比自动决定
        Vertical = 1,    // 上下排列（每格占满宽度）
        Horizontal = 2   // 左右排列（每格占满高度）
    }

    /// <summary>打印排版参数</summary>
    internal sealed class NupSettings
    {
        public int PerSheet = 2;                      // 每张纸放几页
        public NupDirection Direction = NupDirection.Auto;
        public float GapMm = 2f;                      // 页间距（毫米）
        public bool DrawBorder = true;                // 画裁剪虚线框
        public bool OriginalSize;                     // 尽量按原始大小（仍会缩到格内）
    }

    /// <summary>版面计算：把可打印区域切成 N 个格子（单位 1/100 英寸）</summary>
    internal static class NupLayout
    {
        public static void RowsCols(int perSheet, NupDirection dir, bool pageIsLandscape,
                                    out int rows, out int cols)
        {
            rows = 1; cols = 1;
            switch (perSheet)
            {
                case 1:
                    rows = 1; cols = 1; break;
                case 2:
                    {
                        // 横版页面（车票就是这种）上下排更省纸；
                        // 竖版页面左右排更省纸。
                        bool pairHorizontally =
                            dir == NupDirection.Horizontal ||
                            (dir == NupDirection.Auto && !pageIsLandscape);
                        rows = pairHorizontally ? 1 : 2;
                        cols = pairHorizontally ? 2 : 1;
                        break;
                    }
                case 4:
                    rows = 2; cols = 2; break;
                case 6:
                    rows = 3; cols = 2; break;
                case 8:
                    rows = 4; cols = 2; break;
                default:
                    rows = 1; cols = 1; break;
            }
        }

        public static Rectangle[] Compute(Rectangle area, int perSheet,
                                          NupDirection dir, float gapMm,
                                          bool pageIsLandscape)
        {
            int rows, cols;
            RowsCols(perSheet, dir, pageIsLandscape, out rows, out cols);

            int gap = (int)Math.Round(gapMm / 25.4f * 100f);
            if (gap < 0) gap = 0;

            int cw = (area.Width - gap * (cols - 1)) / cols;
            int ch = (area.Height - gap * (rows - 1)) / rows;
            if (cw < 1) cw = 1;
            if (ch < 1) ch = 1;

            Rectangle[] cells = new Rectangle[rows * cols];
            int k = 0;
            for (int r = 0; r < rows; r++)
            {
                int top = area.Y + r * (ch + gap);
                // 最后一行/列吃掉整数除法的余数，保证精确贴边（不留 1px 白边）
                int h = (r == rows - 1) ? (area.Bottom - top) : ch;
                for (int c = 0; c < cols; c++)
                {
                    int left = area.X + c * (cw + gap);
                    int w = (c == cols - 1) ? (area.Right - left) : cw;
                    cells[k++] = new Rectangle(left, top, w, h);
                }
            }
            return cells;
        }
    }

    /// <summary>打印排版设置对话框</summary>
    internal sealed class NupSettingsForm : Form
    {
        public NupSettings Result;

        private readonly RadioButton _rb1;
        private readonly RadioButton _rb2;
        private readonly RadioButton _rb4;
        private readonly ComboBox _cmbDir;
        private readonly NumericUpDown _numGap;
        private readonly CheckBox _chkBorder;
        private readonly CheckBox _chkOriginal;
        private readonly Label _lblHint;

        private readonly int _pageCount;
        private readonly bool _firstLandscape;

        public NupSettingsForm(NupSettings init, int pageCount, bool firstIsLandscape)
        {
            _pageCount = Math.Max(1, pageCount);
            _firstLandscape = firstIsLandscape;

            Text = "打印排版";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(430, 336);
            Font = new Font("Microsoft YaHei UI", 9f);

            Label title = new Label();
            title.Text = "一张纸上排几页";
            title.Location = new Point(18, 16);
            title.AutoSize = true;
            Controls.Add(title);

            _rb1 = new RadioButton();
            _rb1.Text = "1 页（原样打印）";
            _rb1.Location = new Point(30, 42);
            _rb1.AutoSize = true;
            Controls.Add(_rb1);

            _rb2 = new RadioButton();
            _rb2.Text = "2 页（报销车票常用）";
            _rb2.Location = new Point(30, 66);
            _rb2.AutoSize = true;
            Controls.Add(_rb2);

            _rb4 = new RadioButton();
            _rb4.Text = "4 页";
            _rb4.Location = new Point(30, 90);
            _rb4.AutoSize = true;
            Controls.Add(_rb4);

            if (init.PerSheet == 1) _rb1.Checked = true;
            else if (init.PerSheet == 4) _rb4.Checked = true;
            else _rb2.Checked = true;

            Label l2 = new Label();
            l2.Text = "排列方向";
            l2.Location = new Point(18, 126);
            l2.AutoSize = true;
            Controls.Add(l2);

            _cmbDir = new ComboBox();
            _cmbDir.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbDir.Location = new Point(110, 123);
            _cmbDir.Width = 200;
            _cmbDir.Items.Add("自动（按页面形状）");
            _cmbDir.Items.Add("上下排列");
            _cmbDir.Items.Add("左右排列");
            _cmbDir.SelectedIndex = (int)init.Direction;
            Controls.Add(_cmbDir);

            Label l3 = new Label();
            l3.Text = "页间距";
            l3.Location = new Point(18, 162);
            l3.AutoSize = true;
            Controls.Add(l3);

            _numGap = new NumericUpDown();
            _numGap.Location = new Point(110, 159);
            _numGap.Width = 70;
            _numGap.DecimalPlaces = 1;
            _numGap.Increment = 0.5m;
            _numGap.Minimum = 0m;
            _numGap.Maximum = 50m;
            _numGap.Value = (decimal)Math.Max(0f, Math.Min(50f, init.GapMm));
            Controls.Add(_numGap);

            Label l3b = new Label();
            l3b.Text = "mm";
            l3b.Location = new Point(186, 162);
            l3b.AutoSize = true;
            Controls.Add(l3b);

            _chkBorder = new CheckBox();
            _chkBorder.Text = "画裁剪虚线框（方便剪开）";
            _chkBorder.Location = new Point(30, 196);
            _chkBorder.AutoSize = true;
            _chkBorder.Checked = init.DrawBorder;
            Controls.Add(_chkBorder);

            _chkOriginal = new CheckBox();
            _chkOriginal.Text = "尽量按原始大小（放不下才缩）";
            _chkOriginal.Location = new Point(30, 220);
            _chkOriginal.AutoSize = true;
            _chkOriginal.Checked = init.OriginalSize;
            Controls.Add(_chkOriginal);

            _lblHint = new Label();
            _lblHint.Location = new Point(18, 250);
            _lblHint.Size = new Size(394, 40);
            _lblHint.ForeColor = Color.FromArgb(90, 90, 90);
            Controls.Add(_lblHint);

            Button ok = new Button();
            ok.Text = "预览并打印";
            ok.Location = new Point(206, 296);
            ok.Size = new Size(110, 28);
            ok.DialogResult = DialogResult.OK;
            Controls.Add(ok);

            Button cancel = new Button();
            cancel.Text = "取消";
            cancel.Location = new Point(324, 296);
            cancel.Size = new Size(76, 28);
            cancel.DialogResult = DialogResult.Cancel;
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;

            _rb1.CheckedChanged += OnAnyChanged;
            _rb2.CheckedChanged += OnAnyChanged;
            _rb4.CheckedChanged += OnAnyChanged;
            _cmbDir.SelectedIndexChanged += OnAnyChanged;
            _numGap.ValueChanged += OnAnyChanged;
            _chkOriginal.CheckedChanged += OnAnyChanged;

            UpdateHint();

            FormClosing += delegate (object s, FormClosingEventArgs e)
            {
                if (DialogResult == DialogResult.OK) Collect();
            };
        }

        private int PerSheet
        {
            get { return _rb4.Checked ? 4 : (_rb1.Checked ? 1 : 2); }
        }

        private void OnAnyChanged(object sender, EventArgs e)
        {
            UpdateHint();
        }

        private void UpdateHint()
        {
            int per = PerSheet;
            int sheets = (int)Math.Ceiling(_pageCount / (double)per);
            string shape = _firstLandscape ? "横版" : "竖版";
            string autoNote = "";
            if (_cmbDir.SelectedIndex == 0 && per == 2)
                autoNote = _firstLandscape
                    ? "（首张是" + shape + "，将上下排列）"
                    : "（首张是" + shape + "，将左右排列）";

            _lblHint.Text = string.Format(
                CultureInfo.InvariantCulture,
                "共 {0} 页 → 输出 {1} 张纸。{2}",
                _pageCount, sheets, autoNote);
        }

        private void Collect()
        {
            NupSettings r = new NupSettings();
            r.PerSheet = PerSheet;
            r.Direction = (NupDirection)_cmbDir.SelectedIndex;
            r.GapMm = (float)_numGap.Value;
            r.DrawBorder = _chkBorder.Checked;
            r.OriginalSize = _chkOriginal.Checked;
            Result = r;
        }
    }
}
