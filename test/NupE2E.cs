using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Printing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace PdfMini
{
    /// <summary>车票场景端到端测试：横版页面 2-up 是否自动选上下排列</summary>
    internal static class NupE2E
    {
        private static int _pass;
        private static int _fail;

        private static void Check(bool ok, string what)
        {
            if (ok) { _pass++; Console.WriteLine("  PASS  " + what); }
            else { _fail++; Console.WriteLine("  FAIL  " + what); }
        }

        private static void Pump(int ms)
        {
            DateTime end = DateTime.UtcNow.AddMilliseconds(ms);
            while (DateTime.UtcNow < end)
            {
                Application.DoEvents();
                System.Threading.Thread.Sleep(5);
            }
        }

        private static object GetField(object o, string name)
        {
            FieldInfo f = o.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            return f == null ? null : f.GetValue(o);
        }

        private static void SetField(object o, string name, object v)
        {
            FieldInfo f = o.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            if (f != null) f.SetValue(o, v);
        }

        private static object Call(object o, string name, params object[] args)
        {
            MethodInfo m = o.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance);
            return m == null ? null : m.Invoke(o, args);
        }

        private static int Main(string[] args)
        {
            string pdf = args.Length > 0 ? args[0] : "test\\ticket2.pdf";
            string outDir = args.Length > 1 ? args[1] : "test";
            if (!File.Exists(pdf)) { Console.WriteLine("找不到 " + pdf); return 2; }

            Console.WriteLine("═══ 车票 2-up 端到端测试 ═══");
            Console.WriteLine("源: " + pdf + "\n");

            Application.EnableVisualStyles();
            PdfApi.FPDF_InitLibrary();

            MainForm form = new MainForm(null);
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-6000, -6000);   // 屏幕外，用户看不到
            form.ShowInTaskbar = false;
            form.Show();
            Pump(500);

            string full = Path.GetFullPath(pdf);
            Console.WriteLine("  打开: " + full + "  存在=" + File.Exists(full));
            object rv = Call(form, "OpenFile", full);
            Console.WriteLine("  OpenFile 返回: " + (rv == null ? "(void)" : rv.ToString()));
            Pump(2500);

            object sess = GetField(form, "_s");
            if (sess == null)
            {
                Console.WriteLine("  [错误] _s 仍为 null，OpenFile 未成功");
                // 输出对话框标题与状态栏，辅助定位
                Console.WriteLine("  Text = " + form.Text);
                Check(false, "会话已建立");
                form.Close();
                return 1;
            }
            int total = (int)sess.GetType().GetProperty("Count").GetValue(sess, null);
            Console.WriteLine("  会话页数 = " + total);
            Check(total == 2, "源文档 2 页");

            // 检查页面是横版
            FieldInfo fPages = sess.GetType().GetField("Pages",
                BindingFlags.Public | BindingFlags.Instance);
            Check(fPages != null, "Session.Pages 字段可访问");
            System.Collections.IList pages = (System.Collections.IList)fPages.GetValue(sess);
            Check(pages != null && pages.Count == 2, "Pages 列表有 2 项");
            object p0 = pages[0];
            object doc0 = p0.GetType().GetField("Doc").GetValue(p0);
            IntPtr hDoc = (IntPtr)doc0.GetType().GetField("Handle").GetValue(doc0);
            int idx0 = (int)p0.GetType().GetField("Index").GetValue(p0);
            IntPtr page0 = PdfApi.FPDF_LoadPage(hDoc, idx0);
            float pw = PdfApi.FPDF_GetPageWidthF(page0);
            float ph = PdfApi.FPDF_GetPageHeightF(page0);
            PdfApi.FPDF_ClosePage(page0);
            Check(pw > ph, "源页面是横版 (" + pw.ToString("F0") + "x" + ph.ToString("F0") + " pt，宽>高)");

            // 自动方向判断
            bool land = (bool)Call(form, "FirstPageLandscape");
            Check(land, "FirstPageLandscape() = true");

            int rows, cols;
            NupLayout.RowsCols(2, NupDirection.Auto, land, out rows, out cols);
            Check(rows == 2 && cols == 1, "Auto + 横版 → 2行1列（上下排列），实际 " + rows + "行" + cols + "列");

            // 实际打印
            string outPdf = Path.Combine(outDir, "nup_ticket.pdf");
            if (File.Exists(outPdf)) File.Delete(outPdf);

            NupSettings cfg = new NupSettings();
            cfg.PerSheet = 2;
            cfg.Direction = NupDirection.Auto;
            cfg.GapMm = 2f;
            cfg.DrawBorder = true;
            SetField(form, "_nup", cfg);
            SetField(form, "_printCursor", 0);
            SetField(form, "_printTo", total);

            using (PrintDocument pd = new PrintDocument())
            {
                pd.PrinterSettings.PrinterName = "Microsoft Print to PDF";
                if (!pd.PrinterSettings.IsValid)
                {
                    Console.WriteLine("  (跳过打印：没有 Microsoft Print to PDF)");
                    form.Close();
                    return _fail == 0 ? 0 : 1;
                }

                pd.PrinterSettings.PrintToFile = true;
                pd.PrinterSettings.PrintFileName = Path.GetFullPath(outPdf);
                pd.DocumentName = "ticket nup test";

                MethodInfo ph2 = typeof(MainForm).GetMethod("PrintNupPage",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                Check(ph2 != null, "PrintNupPage 存在");
                if (ph2 == null) { form.Close(); return 1; }

                pd.PrintPage += (PrintPageEventHandler)Delegate.CreateDelegate(
                    typeof(PrintPageEventHandler), form, ph2);
                pd.Print();
            }

            IntPtr hp = PdfApi.FPDF_LoadDocument(PdfApi.Utf8Z(Path.GetFullPath(outPdf)), null);
            Check(hp != IntPtr.Zero, "输出可回读 " + new FileInfo(outPdf).Length + " bytes");
            if (hp != IntPtr.Zero)
            {
                int outPages = PdfApi.FPDF_GetPageCount(hp);
                Check(outPages == 1, "2 页车票 → 输出 1 张 A4（实际 " + outPages + "）");

                IntPtr pp = PdfApi.FPDF_LoadPage(hp, 0);
                float w = PdfApi.FPDF_GetPageWidthF(pp);
                float h = PdfApi.FPDF_GetPageHeightF(pp);
                Check(w > 500 && w < 700 && h > 700 && h < 950,
                    "输出纸张 A4 (" + w.ToString("F0") + "x" + h.ToString("F0") + " pt)");

                Bitmap shot = PdfApi.RenderPage(pp, 110f / 72f, 0, false);
                if (shot != null)
                {
                    int halfY = shot.Height / 2;
                    int inkTop = 0, inkBottom = 0;
                    int minX = int.MaxValue, maxX = -1;
                    int minY = int.MaxValue, maxY = -1;
                    for (int y = 0; y < shot.Height; y += 2)
                    {
                        for (int x = 0; x < shot.Width; x += 2)
                        {
                            Color c = shot.GetPixel(x, y);
                            // 只看深色（文字/边框），忽略浅色虚线
                            if (c.R < 150 || c.G < 150 || c.B < 150)
                            {
                                if (y < halfY) inkTop++; else inkBottom++;
                                if (x < minX) minX = x;
                                if (x > maxX) maxX = x;
                                if (y < minY) minY = y;
                                if (y > maxY) maxY = y;
                            }
                        }
                    }
                    Check(inkTop > 100, "上半有车票内容 (ink=" + inkTop + ")");
                    Check(inkBottom > 100, "下半有车票内容 (ink=" + inkBottom + ")");

                    double ratio = inkBottom / (double)Math.Max(1, inkTop);
                    Check(ratio > 0.6 && ratio < 1.7,
                        "上下墨量接近 (比 " + ratio.ToString("F2") + ")，说明两张票排版一致");

                    int contentH = maxY - minY;
                    Check(contentH < shot.Height * 0.98,
                        "内容未超出纸张 (高度占 " + (contentH * 100 / shot.Height) + "%)");

                    shot.Save(Path.Combine(outDir, "nup_ticket_page1.png"), ImageFormat.Png);
                    Console.WriteLine("   snapshot -> nup_ticket_page1.png (" + shot.Width + "x" + shot.Height + ")");
                    shot.Dispose();
                }
                PdfApi.FPDF_ClosePage(pp);
                PdfApi.FPDF_CloseDocument(hp);
            }

            form.Close();
            Pump(200);

            Console.WriteLine("\n═══════════════════════════════");
            Console.WriteLine("结果：" + _pass + " 通过 / " + _fail + " 失败");
            return _fail == 0 ? 0 : 1;
        }
    }
}
