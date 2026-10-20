using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace PdfMini
{
    /// <summary>关于窗：工作室信息 + 版本 + 引擎版权。</summary>
    internal sealed class AboutForm : Form
    {
        internal const string StudioName = "cjfranklin 工作室";
        internal const string AppVersion = "1.0";

        public AboutForm()
        {
            float dpi = DeviceDpi / 96f;
            Text = "关于 PdfMini";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(Scale(452, dpi), Scale(292, dpi));
            Font = new Font("Microsoft YaHei UI", 9f);
            BackColor = Color.FromArgb(251, 251, 253);

            int m = Scale(20, dpi);

            Icon appIcon = null;
            try { appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { }

            PictureBox logo = new PictureBox();
            logo.SetBounds(m, m, Scale(64, dpi), Scale(64, dpi));
            logo.SizeMode = PictureBoxSizeMode.Zoom;
            if (appIcon != null) logo.Image = appIcon.ToBitmap();
            Controls.Add(logo);

            int tx = m + Scale(78, dpi);

            Label title = new Label();
            title.Text = "PdfMini  " + AppVersion;
            title.Font = new Font("Microsoft YaHei UI", 15f, FontStyle.Bold);
            title.ForeColor = Color.FromArgb(24, 44, 66);
            title.AutoSize = true;
            title.Location = new Point(tx, Scale(20, dpi));
            Controls.Add(title);

            Label sub = new Label();
            sub.Text = "极简 PDF 阅读器 · 页面拆分 / 合并";
            sub.ForeColor = Color.FromArgb(96, 100, 106);
            sub.AutoSize = true;
            sub.Location = new Point(tx, Scale(58, dpi));
            Controls.Add(sub);

            Panel rule = new Panel();
            rule.BackColor = Color.FromArgb(228, 231, 236);
            rule.SetBounds(m, Scale(102, dpi), ClientSize.Width - m * 2, 1);
            Controls.Add(rule);

            Label studio = new Label();
            studio.Text = StudioName;
            studio.Font = new Font("Microsoft YaHei UI", 12f, FontStyle.Bold);
            studio.ForeColor = Color.FromArgb(28, 52, 76);
            studio.AutoSize = true;
            studio.Location = new Point(m, Scale(118, dpi));
            Controls.Add(studio);

            Label info = new Label();
            info.Text = "PDF 渲染引擎：pdfium（BSD-3-Clause）\r\n"
                      + "运行环境：.NET Framework 4.8（系统自带，无需安装）\r\n"
                      + "本程序不联网，不上传任何文件；所有编辑只写入新文件。\r\n"
                      + "© 2026 " + StudioName;
            info.ForeColor = Color.FromArgb(110, 114, 120);
            info.SetBounds(m, Scale(152, dpi), ClientSize.Width - m * 2, Scale(76, dpi));
            Controls.Add(info);

            Button ok = new Button();
            ok.Text = "确定";
            ok.DialogResult = DialogResult.OK;
            ok.SetBounds(ClientSize.Width - m - Scale(92, dpi), ClientSize.Height - m - Scale(30, dpi),
                Scale(92, dpi), Scale(30, dpi));
            Controls.Add(ok);
            AcceptButton = ok;
            CancelButton = ok;
        }

        private static int Scale(int value, float dpi)
        {
            return (int)Math.Round(value * dpi);
        }
    }
}
