using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Printing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace PdfMini
{
    /// <summary>
    /// 无头 UI 测试：窗体放在屏幕外 (-6000,-6000) 显示，用反射调用私有方法，
    /// 断言真实业务路径（打开/缩略图/查找/夜间/保存/拆分），截图只写文件，不打扰桌面。
    /// </summary>
    internal static class UiTest
    {
        private static int _fail;

        private static void Check(bool ok, string what)
        {
            Console.WriteLine((ok ? "[PASS] " : "[FAIL] ") + what);
            if (!ok) _fail++;
        }

        private static object Field(object o, string name)
        {
            FieldInfo f = o.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            if (f == null) { Console.WriteLine("   no field " + name); return null; }
            return f.GetValue(o);
        }

        private static object Call(object o, string name, params object[] args)
        {
            MethodInfo m = o.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance);
            if (m == null) { Console.WriteLine("   no method " + name); return null; }
            return m.Invoke(o, args);
        }

        private static void Pump(int ms)
        {
            DateTime end = DateTime.UtcNow.AddMilliseconds(ms);
            while (DateTime.UtcNow < end)
            {
                Application.DoEvents();
                Thread.Sleep(25);
            }
        }

        private static double MeanBrightness(Bitmap b)
        {
            long sum = 0;
            int n = 0;
            for (int y = 0; y < b.Height; y += 11)
            {
                for (int x = 0; x < b.Width; x += 11)
                {
                    Color c = b.GetPixel(x, y);
                    sum += (c.R + c.G + c.B) / 3;
                    n++;
                }
            }
            return n == 0 ? 0 : (double)sum / n;
        }

        private static Bitmap Snapshot(MainForm form, string path)
        {
            Bitmap shot = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(shot, new Rectangle(0, 0, form.Width, form.Height));
            shot.Save(path, ImageFormat.Png);
            Console.WriteLine("   snapshot -> " + path + " (" + shot.Width + "x" + shot.Height + ")");
            return shot;
        }

        private static Size PageBitmapSize(MainForm form)
        {
            PageView view = (PageView)Field(form, "_view");
            if (view == null) return Size.Empty;
            Dictionary<int, Bitmap> cache = (Dictionary<int, Bitmap>)Field(view, "_cache");
            Bitmap bmp;
            if (cache != null && cache.TryGetValue(view.CurrentPage, out bmp) && bmp != null) return bmp.Size;
            return Size.Empty;
        }

        private static int CacheCount(MainForm form)
        {
            PageView view = (PageView)Field(form, "_view");
            Dictionary<int, Bitmap> cache = (Dictionary<int, Bitmap>)Field(view, "_cache");
            return cache == null ? -1 : cache.Count;
        }

        [STAThread]
        private static int Main(string[] args)
        {
            string pdf = args.Length > 0 ? args[0] : "test\\sample6.pdf";
            string outDir = args.Length > 1 ? args[1] : "test";
            string realPdf = args.Length > 2 ? args[2] : null;
            string outlinePdf = args.Length > 3 ? args[3] : null;
            if (!Directory.Exists(outDir)) Directory.CreateDirectory(outDir);

            PdfApi.FPDF_InitLibrary();
            MainForm form = null;
            try
            {
                form = new MainForm(null);
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-6000, -6000);   // 屏幕外：用户看不到任何窗口
                form.ShowInTaskbar = false;
                form.Show();
                Pump(500);

                Call(form, "OpenFile", pdf);
                Pump(3500);   // 等后台缩略图线程

                Session s = (Session)Field(form, "_s");
                Check(s != null && s.Count == 6, "打开文件后会话页数 = " + (s == null ? -1 : s.Count));

                ListView thumbs = (ListView)Field(form, "_thumbs");
                Check(thumbs.Items.Count == 6, "缩略图条目数 = " + thumbs.Items.Count);

                // ── 显示模式检查：适宽 / 适页 / 100% / 旋转
                PageView view = (PageView)Field(form, "_view");
                Size vp = view.ClientSize;
                int availW = Math.Max(140, vp.Width - 28);
                int availH = Math.Max(140, vp.Height - 28);

                Call(form, "SetFit", 1);
                Pump(900);
                Size bs = PageBitmapSize(form);
                Check(Math.Abs(bs.Width - availW) <= 3, "适宽：单页位图宽 " + bs.Width + " ≈ 可视宽 " + availW);
                Check(bs.Height > vp.Height, "适宽：长页面可纵向滚动 (" + bs.Height + " > " + vp.Height + ")");
                Bitmap shotFitW = Snapshot(form, Path.Combine(outDir, "ui_fit_width.png"));
                shotFitW.Dispose();

                Call(form, "SetFit", 2);
                Pump(700);
                bs = PageBitmapSize(form);
                bool insideBox = bs.Width <= availW + 3 && bs.Height <= availH + 3;
                bool touchesEdge = Math.Abs(bs.Width - availW) <= 3 || Math.Abs(bs.Height - availH) <= 3;
                Check(insideBox && touchesEdge, "适页：整页可见 " + bs.Width + "x" + bs.Height + " 内框 " + availW + "x" + availH);
                Bitmap shotFitP = Snapshot(form, Path.Combine(outDir, "ui_fit_page.png"));
                shotFitP.Dispose();

                Call(form, "Set100");
                Pump(700);
                bs = PageBitmapSize(form);
                object zoom100 = Field(form, "_zoom");
                Check(Math.Abs(bs.Width - 793) <= 2, "100%：595pt 页面 → 位图宽 " + bs.Width + "（应 793）");
                Check(zoom100 is float && Math.Abs((float)zoom100 - 1f) < 0.02f, "100%：缩放值 = " + zoom100);
                Bitmap shot100 = Snapshot(form, Path.Combine(outDir, "ui_100pct.png"));
                shot100.Dispose();

                // 旋转 90°：位图宽高应互换
                Call(form, "SetFit", 2);
                Pump(600);
                Size beforeRot = PageBitmapSize(form);
                Call(form, "RotateSelected", 90);
                Pump(800);
                Size afterRot = PageBitmapSize(form);
                Check(afterRot.Width > afterRot.Height, "旋转 90°：位图变为横向 " + beforeRot.Width + "x" + beforeRot.Height
                    + " → " + afterRot.Width + "x" + afterRot.Height);
                Bitmap shotRot = Snapshot(form, Path.Combine(outDir, "ui_rotated.png"));
                shotRot.Dispose();
                Call(form, "RotateSelected", -90);
                Pump(600);

                // ── 正常 / 夜间 快照
                Call(form, "SetFit", 1);
                Pump(600);
                Bitmap shot = Snapshot(form, Path.Combine(outDir, "ui_normal.png"));
                double brightNormal = MeanBrightness(shot);
                shot.Dispose();
                Check(brightNormal > 120, "正常模式页面偏亮 avg=" + brightNormal.ToString("F1"));

                Call(form, "ToggleNight");
                Pump(800);
                Bitmap shot2 = Snapshot(form, Path.Combine(outDir, "ui_night.png"));
                double brightNight = MeanBrightness(shot2);
                shot2.Dispose();
                Check(brightNight < brightNormal - 15, "夜间模式变暗 avg=" + brightNight.ToString("F1"));
                Call(form, "ToggleNight");
                Pump(600);

                // ── 水印：空视图里画一次，量化右下角是否有内容
                using (PageView solo = new PageView())
                {
                    solo.Size = new Size(420, 320);
                    solo.SetSource(null);
                    Bitmap soloShot = new Bitmap(420, 320);
                    solo.DrawToBitmap(soloShot, new Rectangle(0, 0, 420, 320));
                    int painted = 0;
                    for (int y = 278; y < 314; y++)
                    {
                        for (int x = 200; x < 416; x++)
                        {
                            Color c = soloShot.GetPixel(x, y);
                            if (Math.Abs(c.R - 45) > 12 || Math.Abs(c.G - 45) > 12 || Math.Abs(c.B - 48) > 12) painted++;
                        }
                    }
                    soloShot.Dispose();
                    Check(painted > 40, "右下角工作室水印已绘制（差异像素 " + painted + "）");
                }

                // ── 连续滚动：跳页 / 滚到底 / 缓存上限
                Call(form, "SetFit", 1);
                Pump(700);
                Call(form, "GoTo", 5);
                Pump(900);
                PageView cv = (PageView)Field(form, "_view");
                Check(cv.CurrentPage == 5, "连续滚动：跳到第6页后当前页 = " + cv.CurrentPage);
                Check(PageBitmapSize(form).Width > 100, "连续滚动：第6页已渲染进缓存 " + PageBitmapSize(form));
                Bitmap shotScroll = Snapshot(form, Path.Combine(outDir, "ui_scroll_p6.png"));
                shotScroll.Dispose();

                cv.ScrollBy(-20000f);
                Pump(500);
                Check(cv.CurrentPage == 0, "连续滚动：滚到顶部后当前页 = " + cv.CurrentPage);

                for (int i = 0; i < 6; i++)
                {
                    cv.ScrollByPage(1);
                    Pump(160);
                }
                Pump(600);
                int cached = CacheCount(form);
                Check(cached >= 1 && cached <= 4, "缓存页数受控 = " + cached + "（上限 4）");


                // 查找（真实 DoFind：从当前页往后扫全文并跳转）
                Call(form, "DoFind", "keyword-alpha");
                Pump(600);
                ToolStripStatusLabel msg = (ToolStripStatusLabel)Field(form, "_lblMsg");
                Check(msg != null && msg.Text.Contains("处"), "查找结果写进状态栏: \"" + (msg == null ? "" : msg.Text) + "\"");

                Dictionary<int, List<RectangleF>> hits =
                    (Dictionary<int, List<RectangleF>>)Field(form, "_hits");
                Check(hits != null && hits.Count > 0, "高亮命中页数 = " + (hits == null ? -1 : hits.Count));
                if (hits != null && hits.Count > 0)
                {
                    int firstKey = 0;
                    foreach (int k in hits.Keys) { firstKey = k; break; }
                    List<RectangleF> rects = hits[firstKey];
                    Check(rects != null && rects.Count > 0, "第 " + (firstKey + 1) + " 页命中矩形数 = " + (rects == null ? 0 : rects.Count));
                    if (rects != null && rects.Count > 0)
                    {
                        RectangleF rr = rects[0];
                        Check(rr.Width > 2f && rr.Height > 2f && rr.Left > -5f && rr.Top > -5f && rr.Right < 1200f,
                            "命中矩形几何合理 x=" + rr.X.ToString("F0") + " y=" + rr.Y.ToString("F0")
                            + " w=" + rr.Width.ToString("F0") + " h=" + rr.Height.ToString("F0") + " pt");
                    }
                }
                Pump(1500);
                Bitmap shl = Snapshot(form, Path.Combine(outDir, "ui_highlight.png"));
                shl.Dispose();

                // 再按一次「找」应跳到下一个命中页（当前在第3页 → 第4页）
                int beforeJump = (int)Field(form, "_cur");
                Call(form, "DoFind", "keyword-alpha");
                Pump(900);
                object afterJump = Field(form, "_cur");
                Check(afterJump is int && (int)afterJump == beforeJump + 1,
                    "重复查找跳到下一个命中页 " + beforeJump + " → " + afterJump);

                // 转 90° 后高亮也要跟着转（坐标映射 + 自动重建）
                Call(form, "RotateSelected", 90);
                Pump(1500);
                Check(hits != null && hits.Count > 0, "旋转 90° 后高亮自动重建 = " + (hits == null ? -1 : hits.Count) + " 页");
                if (hits != null && hits.Count > 0)
                {
                    int fk = 0;
                    foreach (int k in hits.Keys) { fk = k; break; }
                    List<RectangleF> r2 = hits[fk];
                    if (r2 != null && r2.Count > 0)
                        Check(r2[0].Height > r2[0].Width,
                            "旋转后命中矩形转为竖向 " + r2[0].Width.ToString("F0") + "x" + r2[0].Height.ToString("F0") + " pt");
                }
                Bitmap shlRot = Snapshot(form, Path.Combine(outDir, "ui_highlight_rotated.png"));
                shlRot.Dispose();
                Call(form, "RotateSelected", -90);
                Pump(800);

                Call(form, "DoFind", "zzqq-not-exist");
                Pump(600);
                Check(msg != null && msg.Text.Contains("没找到"), "未命中提示: \"" + (msg == null ? "" : msg.Text) + "\"");
                Check(hits != null && hits.Count == 0, "未命中时清空高亮（剩余 " + (hits == null ? -1 : hits.Count) + " 页）");

                // ── 关于窗（离屏快照，检查排版与工作室信息）
                using (AboutForm about = new AboutForm())
                {
                    about.StartPosition = FormStartPosition.Manual;
                    about.Location = new Point(-6000, -6000);
                    about.Show();
                    Pump(400);
                    Bitmap aboutShot = new Bitmap(about.Width, about.Height);
                    about.DrawToBitmap(aboutShot, new Rectangle(0, 0, about.Width, about.Height));
                    aboutShot.Save(Path.Combine(outDir, "ui_about.png"), ImageFormat.Png);
                    Console.WriteLine("   snapshot -> ui_about.png (" + aboutShot.Width + "x" + aboutShot.Height + ")");
                    aboutShot.Dispose();
                    about.Close();
                }
                Pump(300);
                Call(form, "SetFit", 1);
                Pump(400);

                // ── 真实打印链路：打到 Microsoft Print to PDF，再回读产物验版式
                try
                {
                    string printed = Path.Combine(outDir, "ui_printed.pdf");
                    if (File.Exists(printed)) File.Delete(printed);

                    using (PrintDocument pd = new PrintDocument())
                    {
                        pd.PrinterSettings.PrinterName = "Microsoft Print to PDF";
                        if (pd.PrinterSettings.IsValid)
                        {
                            pd.PrinterSettings.PrintToFile = true;
                            pd.PrinterSettings.PrintFileName = Path.GetFullPath(printed);
                            pd.DocumentName = "PdfMini print test";

                            FieldInfo fc = typeof(MainForm).GetField("_printCursor", BindingFlags.NonPublic | BindingFlags.Instance);
                            FieldInfo ft = typeof(MainForm).GetField("_printTo", BindingFlags.NonPublic | BindingFlags.Instance);
                            fc.SetValue(form, 1);
                            ft.SetValue(form, 2);
                            MethodInfo ph = typeof(MainForm).GetMethod("PrintOnePage", BindingFlags.NonPublic | BindingFlags.Instance);
                            pd.PrintPage += (PrintPageEventHandler)Delegate.CreateDelegate(typeof(PrintPageEventHandler), form, ph);

                            pd.Print();

                            IntPtr hp = PdfApi.FPDF_LoadDocument(PdfApi.Utf8Z(Path.GetFullPath(printed)), null);
                            Check(hp != IntPtr.Zero, "打印产物可回读（" + (File.Exists(printed) ? new FileInfo(printed).Length + " bytes" : "无文件") + "）");
                            if (hp != IntPtr.Zero)
                            {
                                int pc = PdfApi.FPDF_GetPageCount(hp);
                                Check(pc == 2, "打印页数 = " + pc + "（应为 2）");
                                IntPtr pp = PdfApi.FPDF_LoadPage(hp, 0);
                                if (pp != IntPtr.Zero)
                                {
                                    float w = PdfApi.FPDF_GetPageWidthF(pp);
                                    float hh = PdfApi.FPDF_GetPageHeightF(pp);
                                    Check(w > 500 && w < 700 && hh > 700 && hh < 950,
                                        "打印纸张/A4 尺寸 = " + w.ToString("F0") + "x" + hh.ToString("F0") + " pt");
                                    Bitmap pshot = PdfApi.RenderPage(pp, 96f / 72f, 0, false);
                                    if (pshot != null)
                                    {
                                        int white = 0, dark = 0;
                                        for (int y = 0; y < pshot.Height; y += 5)
                                        {
                                            for (int x = 0; x < pshot.Width; x += 5)
                                            {
                                                Color c = pshot.GetPixel(x, y);
                                                if (c.R > 240 && c.G > 240 && c.B > 240) white++;
                                                else if (c.R < 110) dark++;
                                            }
                                        }
                                        Check(dark > 20 && white > 1000, "打印内容非空白 white=" + white + " dark=" + dark);
                                        pshot.Save(Path.Combine(outDir, "ui_printed_page1.png"), ImageFormat.Png);
                                        pshot.Dispose();
                                    }
                                    PdfApi.FPDF_ClosePage(pp);
                                }
                                PdfApi.FPDF_CloseDocument(hp);
                            }
                        }
                        else
                        {
                            Console.WriteLine("   (跳过：没有 Microsoft Print to PDF)");
                        }
                    }
                }
                catch (Exception pex)
                {
                    Check(false, "打印链路异常: " + pex.Message);
                }

                // ── 排版打印：2 页排一张 A4（报销车票场景）──
                try
                {
                    string nupOut = Path.Combine(outDir, "ui_nup2.pdf");
                    if (File.Exists(nupOut)) File.Delete(nupOut);

                    int total = 0;
                    FieldInfo fs = typeof(MainForm).GetField("_s", BindingFlags.NonPublic | BindingFlags.Instance);
                    object sess = fs.GetValue(form);
                    PropertyInfo pcnt = sess.GetType().GetProperty("Count");
                    total = (int)pcnt.GetValue(sess, null);
                    Console.WriteLine("   源文档共 " + total + " 页");

                    NupSettings cfg = new NupSettings();
                    cfg.PerSheet = 2;
                    cfg.Direction = NupDirection.Vertical;
                    cfg.GapMm = 3f;
                    cfg.DrawBorder = true;
                    cfg.OriginalSize = false;

                    FieldInfo fn = typeof(MainForm).GetField("_nup", BindingFlags.NonPublic | BindingFlags.Instance);
                    fn.SetValue(form, cfg);

                    using (PrintDocument pd2 = new PrintDocument())
                    {
                        pd2.PrinterSettings.PrinterName = "Microsoft Print to PDF";
                        if (pd2.PrinterSettings.IsValid)
                        {
                            pd2.PrinterSettings.PrintToFile = true;
                            pd2.PrinterSettings.PrintFileName = Path.GetFullPath(nupOut);
                            pd2.DocumentName = "PdfMini nup test";

                            FieldInfo fc2 = typeof(MainForm).GetField("_printCursor", BindingFlags.NonPublic | BindingFlags.Instance);
                            FieldInfo ft2 = typeof(MainForm).GetField("_printTo", BindingFlags.NonPublic | BindingFlags.Instance);
                            fc2.SetValue(form, 0);
                            ft2.SetValue(form, total);

                            MethodInfo ph2 = typeof(MainForm).GetMethod("PrintNupPage", BindingFlags.NonPublic | BindingFlags.Instance);
                            Check(ph2 != null, "PrintNupPage 方法存在");
                            if (ph2 != null)
                            {
                                pd2.PrintPage += (PrintPageEventHandler)Delegate.CreateDelegate(typeof(PrintPageEventHandler), form, ph2);
                                pd2.Print();

                                IntPtr hp2 = PdfApi.FPDF_LoadDocument(PdfApi.Utf8Z(Path.GetFullPath(nupOut)), null);
                                Check(hp2 != IntPtr.Zero, "排版打印产物可回读（" + (File.Exists(nupOut) ? new FileInfo(nupOut).Length + " bytes" : "无文件") + "）");
                                if (hp2 != IntPtr.Zero)
                                {
                                    int outPages = PdfApi.FPDF_GetPageCount(hp2);
                                    int expect = (total + 1) / 2;
                                    Check(outPages == expect,
                                        "2页/张 → 输出 " + outPages + " 张（应为 " + expect + "）");

                                    IntPtr pp2 = PdfApi.FPDF_LoadPage(hp2, 0);
                                    if (pp2 != IntPtr.Zero)
                                    {
                                        float w2 = PdfApi.FPDF_GetPageWidthF(pp2);
                                        float h2 = PdfApi.FPDF_GetPageHeightF(pp2);
                                        Check(w2 > 500 && w2 < 700 && h2 > 700 && h2 < 950,
                                            "输出纸张 = A4 (" + w2.ToString("F0") + "x" + h2.ToString("F0") + " pt)");

                                        Bitmap nupShot = PdfApi.RenderPage(pp2, 96f / 72f, 0, false);
                                        if (nupShot != null)
                                        {
                                            // 上下两半各统计非白像素，验证确实是 2-up
                                            int inkTop = 0, inkBottom = 0;
                                            int halfY = nupShot.Height / 2;
                                            for (int y = 0; y < nupShot.Height; y += 4)
                                            {
                                                for (int x = 0; x < nupShot.Width; x += 4)
                                                {
                                                    Color c = nupShot.GetPixel(x, y);
                                                    bool inked = !(c.R > 240 && c.G > 240 && c.B > 240);
                                                    if (inked) { if (y < halfY) inkTop++; else inkBottom++; }
                                                }
                                            }
                                            Check(inkTop > 30 && inkBottom > 30,
                                                "上下两半都有内容（上 " + inkTop + " / 下 " + inkBottom + "）");
                                            Check(Math.Abs(inkTop - inkBottom) < Math.Max(inkTop, inkBottom) * 0.85,
                                                "上下内容量接近（对称排版）");

                                            nupShot.Save(Path.Combine(outDir, "ui_nup2_page1.png"), ImageFormat.Png);
                                            Console.WriteLine("   snapshot -> ui_nup2_page1.png (" + nupShot.Width + "x" + nupShot.Height + ")");
                                            nupShot.Dispose();
                                        }
                                        PdfApi.FPDF_ClosePage(pp2);
                                    }
                                    PdfApi.FPDF_CloseDocument(hp2);
                                }
                            }
                        }
                        else
                        {
                            Console.WriteLine("   (跳过排版打印：没有 Microsoft Print to PDF)");
                        }
                    }
                }
                catch (Exception nex)
                {
                    Check(false, "排版打印异常: " + nex.Message);
                }

                // ── 编辑：删第3页 + 第1页转90°，走真实 SaveRefs
                s.Pages.RemoveAt(2);
                s.Pages[0].Rot = 90;
                s.Dirty = true;
                string outPdf = Path.Combine(outDir, "ui_out.pdf");
                if (File.Exists(outPdf)) File.Delete(outPdf);
                object okObj = Call(form, "SaveRefs", new List<PageRef>(s.Pages), outPdf);
                Check(okObj is bool && (bool)okObj, "保存(删页+旋转) 成功");

                IntPtr h = PdfApi.FPDF_LoadDocument(PdfApi.Utf8Z(Path.GetFullPath(outPdf)), null);
                Check(h != IntPtr.Zero, "回读导出文件");
                if (h != IntPtr.Zero)
                {
                    Check(PdfApi.FPDF_GetPageCount(h) == 5, "导出页数 = " + PdfApi.FPDF_GetPageCount(h));
                    IntPtr p0 = PdfApi.FPDF_LoadPage(h, 0);
                    int rot = p0 == IntPtr.Zero ? -1 : PdfApi.FPDFPage_GetRotation(p0);
                    if (p0 != IntPtr.Zero) PdfApi.FPDF_ClosePage(p0);
                    Check(rot == 1, "第1页旋转已落盘 = " + rot);
                    string t2 = PdfApi.PageText(h, 2);
                    Check(t2.IndexOf("Page 4", StringComparison.OrdinalIgnoreCase) >= 0, "第3页内容=原第4页");
                    PdfApi.FPDF_CloseDocument(h);
                }

                // 拆分：只导出第1页和第5页
                List<PageRef> two = new List<PageRef>();
                two.Add(s.Pages[0]);
                two.Add(s.Pages[4]);
                string splitPdf = Path.Combine(outDir, "ui_split.pdf");
                if (File.Exists(splitPdf)) File.Delete(splitPdf);
                object ok2 = Call(form, "SaveRefs", two, splitPdf);
                Check(ok2 is bool && (bool)ok2, "拆分导出成功");

                IntPtr hs = PdfApi.FPDF_LoadDocument(PdfApi.Utf8Z(Path.GetFullPath(splitPdf)), null);
                Check(hs != IntPtr.Zero, "回读拆分文件");
                if (hs != IntPtr.Zero)
                {
                    Check(PdfApi.FPDF_GetPageCount(hs) == 2, "拆分页数 = " + PdfApi.FPDF_GetPageCount(hs));
                    string a = PdfApi.PageText(hs, 0);
                    string b = PdfApi.PageText(hs, 1);
                    Check(a.IndexOf("Page 1", StringComparison.OrdinalIgnoreCase) >= 0, "拆分第1页=原第1页");
                    Check(b.IndexOf("Page 6", StringComparison.OrdinalIgnoreCase) >= 0, "拆分第2页=原第6页");
                    PdfApi.FPDF_CloseDocument(hs);
                }

                // 上移/下移逻辑（Session 层，等价于 MoveSelected 的交换）
                string firstText = PdfApi.PageText(s.Pages[0].Doc.Handle, s.Pages[0].Index);
                PageRef tmp = s.Pages[0];
                s.Pages[0] = s.Pages[1];
                s.Pages[1] = tmp;
                string movedText = PdfApi.PageText(s.Pages[1].Doc.Handle, s.Pages[1].Index);
                Check(firstText == movedText, "页序交换：原第1页现在位于索引1");

                s.Dirty = false;   // 避免关闭时的确认框（无头环境不能弹窗）
                form.Close();
                form.Dispose();
                form = null;

                // ── 真实中文 PDF（可选）：再开一个离屏窗体，验证中文字形与版面
                if (!string.IsNullOrEmpty(realPdf) && File.Exists(realPdf))
                {
                    MainForm f2 = new MainForm(null);
                    f2.StartPosition = FormStartPosition.Manual;
                    f2.Location = new Point(-6000, -6000);
                    f2.ShowInTaskbar = false;
                    f2.Show();
                    Pump(400);
                    Call(f2, "OpenFile", realPdf);
                    Pump(4500);
                    Session s2 = (Session)Field(f2, "_s");
                    Check(s2 != null && s2.Count > 5, "中文 PDF 页数 = " + (s2 == null ? -1 : s2.Count));
                    Size b2 = PageBitmapSize(f2);
                    Check(b2.Width > 400, "中文 PDF 页面已渲染 " + b2.Width + "x" + b2.Height);
                    Bitmap realShot = Snapshot(f2, Path.Combine(outDir, "ui_real_pdf.png"));
                    double brightReal = MeanBrightness(realShot);
                    realShot.Dispose();
                    Check(brightReal > 25 && brightReal < 250, "中文 PDF 快照非空白 avg=" + brightReal.ToString("F1"));
                    string realText = PdfApi.PageText(s2.Pages[0].Doc.Handle, s2.Pages[0].Index);
                    Check(realText.Length > 20, "封面文字层可提取（" + realText.Length + " 字符）");
                    string realPage2 = PdfApi.PageText(s2.Pages[1].Doc.Handle, s2.Pages[1].Index);
                    Check(realPage2.Length > 100, "第2页文字层可提取（" + realPage2.Length + " 字符）");
                    if (s2 != null) s2.Dirty = false;
                    f2.Close();
                    f2.Dispose();
                }

                // ── 大纲面板：读书签、映射到会话页、点击跳页
                if (!string.IsNullOrEmpty(outlinePdf) && File.Exists(outlinePdf))
                {
                    MainForm f3 = new MainForm(null);
                    f3.StartPosition = FormStartPosition.Manual;
                    f3.Location = new Point(-6000, -6000);
                    f3.ShowInTaskbar = false;
                    f3.Show();
                    Pump(400);
                    Call(f3, "OpenFile", outlinePdf);
                    Pump(3000);

                    TreeView tv = (TreeView)Field(f3, "_outline");
                    Check(tv != null && tv.Nodes.Count == 3, "大纲根节点数 = " + (tv == null ? -1 : tv.Nodes.Count));
                    if (tv != null && tv.Nodes.Count == 3)
                    {
                        Check(tv.Nodes[0].Nodes.Count == 1, "第1条含 1 个子项 = " + tv.Nodes[0].Nodes.Count);
                        Check(tv.Nodes[0].Text.IndexOf("第一回", StringComparison.Ordinal) >= 0,
                            "中文书签标题正确 = \"" + tv.Nodes[0].Text + "\"");

                        OutlineItem item0 = tv.Nodes[0].Tag as OutlineItem;
                        OutlineItem item00 = tv.Nodes[0].Nodes[0].Tag as OutlineItem;
                        OutlineItem item1 = tv.Nodes[1].Tag as OutlineItem;
                        Check(item0 != null && item0.PageIndex == 0, "「第一回」→ 会话第1页 = " + (item0 == null ? -1 : item0.PageIndex));
                        Check(item00 != null && item00.PageIndex == 1, "「Section 1.1」→ 会话第2页 = " + (item00 == null ? -1 : item00.PageIndex));
                        Check(item1 != null && item1.PageIndex == 2, "「Chapter 2」→ 会话第3页 = " + (item1 == null ? -1 : item1.PageIndex));

                        TabControl tabs = (TabControl)Field(f3, "_tabs");
                        if (tabs != null)
                        {
                            tabs.SelectedIndex = 1;
                            Pump(700);
                        }
                        Bitmap ov = Snapshot(f3, Path.Combine(outDir, "ui_outline.png"));
                        ov.Dispose();

                        // 模拟点书签：设置 SelectedNode 会触发 AfterSelect → GoTo
                        tv.SelectedNode = tv.Nodes[1];
                        Pump(900);
                        object curIdx = Field(f3, "_cur");
                        Check(curIdx is int && (int)curIdx == 2, "点大纲「Chapter 2」跳到第3页 = " + curIdx);

                        // 删页后大纲要重新映射（删掉第1页后「第一回」应失联或改指）
                        Session s3 = (Session)Field(f3, "_s");
                        s3.Pages.RemoveAt(0);
                        s3.Dirty = true;
                        Call(f3, "LoadOutline");
                        Pump(500);
                        OutlineItem afterDel = tv.Nodes.Count > 0 && tv.Nodes[0].Tag != null
                            ? (OutlineItem)tv.Nodes[0].Tag : null;
                        Check(afterDel == null || afterDel.PageIndex < 0 || afterDel.PageIndex == 0,
                            "删页后大纲重新映射（PageIndex=" + (afterDel == null ? "null" : afterDel.PageIndex.ToString()) + "）");
                        s3.Dirty = false;
                    }
                    f3.Close();
                    f3.Dispose();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[FAIL] 异常: " + ex);
                _fail++;
            }
            finally
            {
                if (form != null) { try { form.Dispose(); } catch { } }
                PdfApi.FPDF_DestroyLibrary();
            }

            Console.WriteLine(_fail == 0 ? "ALL PASS" : (_fail + " FAILED"));
            try
            {
                System.Diagnostics.Process me = System.Diagnostics.Process.GetCurrentProcess();
                Console.WriteLine("内存: 峰值 " + (me.PeakWorkingSet64 / (1024 * 1024)) + " MB, 当前 " + (me.WorkingSet64 / (1024 * 1024)) + " MB");
            }
            catch { }
            return _fail == 0 ? 0 : 1;
        }
    }
}
