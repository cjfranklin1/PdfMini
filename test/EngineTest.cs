using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;

namespace PdfMini
{
    /// <summary>
    /// 引擎自检（不经过 GUI）：验证 载入 / 渲染 / 取文字 / 跨文档导入 / 旋转 / 保存。
    /// 用法: EngineTest.exe &lt;源PDF&gt; &lt;附加PDF&gt; &lt;输出PDF&gt;
    /// </summary>
    internal static class EngineTest
    {
        private static int _fail;

        private static void Check(bool ok, string what)
        {
            Console.WriteLine((ok ? "[PASS] " : "[FAIL] ") + what);
            if (!ok) _fail++;
        }

        private static int Main(string[] args)
        {
            if (args.Length < 3)
            {
                Console.WriteLine("usage: EngineTest <src.pdf> <extra.pdf> <out.pdf>");
                return 2;
            }

            PdfApi.FPDF_InitLibrary();
            try
            {
                IntPtr h1 = PdfApi.FPDF_LoadDocument(PdfApi.Utf8Z(args[0]), null);
                Check(h1 != IntPtr.Zero, "载入源文件 " + Path.GetFileName(args[0]) + " err=" + PdfApi.ErrorName(PdfApi.FPDF_GetLastError()));
                if (h1 == IntPtr.Zero) return 1;

                int n1 = PdfApi.FPDF_GetPageCount(h1);
                Check(n1 == 6, "源文件页数 = " + n1);

                string text = PdfApi.PageText(h1, 0);
                Check(text.IndexOf("keyword-alpha", StringComparison.OrdinalIgnoreCase) >= 0,
                    "第1页文字层可提取, 长度=" + text.Length);

                IntPtr page = PdfApi.FPDF_LoadPage(h1, 0);
                Check(page != IntPtr.Zero, "FPDF_LoadPage(0)");
                Bitmap bmp = null;
                try { bmp = PdfApi.RenderPage(page, 96f / 72f, 0, false); }
                catch (Exception ex) { Console.WriteLine("   render exception: " + ex.Message); }
                Check(bmp != null, "渲染第1页");

                // 采样像素：应该是白底黑字（既有趋白也有趋黑）
                if (bmp != null)
                {
                    int white = 0, dark = 0;
                    for (int y = 0; y < bmp.Height; y += 7)
                    {
                        for (int x = 0; x < bmp.Width; x += 7)
                        {
                            Color c = bmp.GetPixel(x, y);
                            if (c.R > 240 && c.G > 240 && c.B > 240) white++;
                            else if (c.R < 90 && c.G < 90 && c.B < 90) dark++;
                        }
                    }
                    Check(white > 100 && dark > 5, "渲染内容非空白: white=" + white + " dark=" + dark);
                    Console.WriteLine("   size=" + bmp.Width + "x" + bmp.Height);

                    // 夜间反色：同点应接近反色
                    Color before = bmp.GetPixel(bmp.Width / 2, bmp.Height / 4);
                    PdfApi.InvertInPlace(bmp);
                    Color after = bmp.GetPixel(bmp.Width / 2, bmp.Height / 4);
                    Check(Math.Abs((255 - before.R) - after.R) <= 3, "夜间反色生效 (" + before.R + "->" + after.R + ")");
                    bmp.Dispose();
                }
                PdfApi.FPDF_ClosePage(page);

                // 打印走的分条带渲染：与整页渲染逐像素比对
                IntPtr bandPage = PdfApi.FPDF_LoadPage(h1, 0);
                if (bandPage != IntPtr.Zero)
                {
                    try
                    {
                        float bandScale = 200f / 72f;
                        using (Bitmap whole = PdfApi.RenderPage(bandPage, bandScale, 0, false))
                        {
                            int w = whole.Width;
                            int hgt = whole.Height;
                            const int bandH = 256;
                            int diff = 0;
                            long total = 0;
                            for (int top = 0; top < hgt; top += bandH)
                            {
                                using (Bitmap band = PdfApi.RenderBand(bandPage, bandScale, 0, w, hgt, top, bandH))
                                {
                                    for (int y = 0; y < band.Height; y += 2)
                                    {
                                        for (int x = 0; x < w; x += 2)
                                        {
                                            Color a = whole.GetPixel(x, top + y);
                                            Color b = band.GetPixel(x, y);
                                            if (Math.Abs(a.R - b.R) > 10 || Math.Abs(a.G - b.G) > 10 || Math.Abs(a.B - b.B) > 10) diff++;
                                            total++;
                                        }
                                    }
                                }
                            }
                            double pct = 100.0 * diff / Math.Max(1, total);
                            Console.WriteLine("   band-compare: " + diff + "/" + total + " 差异像素 (" + pct.ToString("F2") + "%)");
                            Check(diff * 500 < total, "分条带渲染与整页渲染一致（<0.2% 差异）");
                        }
                    }
                    finally
                    {
                        PdfApi.FPDF_ClosePage(bandPage);
                    }
                }
                else
                {
                    Check(false, "分条带渲染：加载页面");
                }

                IntPtr h2 = PdfApi.FPDF_LoadDocument(PdfApi.Utf8Z(args[1]), null);
                Check(h2 != IntPtr.Zero, "载入附加文件");
                if (h2 == IntPtr.Zero) { PdfApi.FPDF_CloseDocument(h1); return 1; }
                Check(PdfApi.FPDF_GetPageCount(h2) == 3, "附加文件页数 = 3");

                // 组合：源文件第 3 页 → 附加文件全部 → 源文件第 1-2 页，另加旋转
                IntPtr dest = PdfApi.FPDF_CreateNewDocument();
                Check(dest != IntPtr.Zero, "FPDF_CreateNewDocument");
                Check(PdfApi.FPDF_ImportPages(dest, h1, PdfApi.AsciiZ("3"), 0) != 0, "导入 src p3");
                Check(PdfApi.FPDF_ImportPages(dest, h2, PdfApi.AsciiZ("1-3"), 1) != 0, "导入 extra p1-3");
                Check(PdfApi.FPDF_ImportPages(dest, h1, PdfApi.AsciiZ("1-2"), 4) != 0, "导入 src p1-2");
                Check(PdfApi.FPDF_GetPageCount(dest) == 6, "组合后页数 = " + PdfApi.FPDF_GetPageCount(dest));

                IntPtr dp = PdfApi.FPDF_LoadPage(dest, 0);
                if (dp != IntPtr.Zero)
                {
                    int cur = PdfApi.FPDFPage_GetRotation(dp);
                    PdfApi.FPDFPage_SetRotation(dp, (cur + 1) % 4);
                    PdfApi.FPDF_ClosePage(dp);
                }
                Check(true, "设置第1页旋转 90°");

                bool saved = PdfWriter.Save(dest, args[2]);
                Check(saved, "保存 " + Path.GetFileName(args[2]) + " (" + (File.Exists(args[2]) ? new FileInfo(args[2]).Length + " bytes" : "无文件") + ")");
                PdfApi.FPDF_CloseDocument(dest);
                PdfApi.FPDF_CloseDocument(h2);
                PdfApi.FPDF_CloseDocument(h1);

                // 回读校验
                IntPtr re = PdfApi.FPDF_LoadDocument(PdfApi.Utf8Z(args[2]), null);
                Check(re != IntPtr.Zero, "回读生成文件");
                if (re != IntPtr.Zero)
                {
                    Check(PdfApi.FPDF_GetPageCount(re) == 6, "回读页数 = " + PdfApi.FPDF_GetPageCount(re));
                    string t0 = PdfApi.PageText(re, 0);
                    Check(t0.IndexOf("Page 3", StringComparison.OrdinalIgnoreCase) >= 0, "第1页内容来自 src 第3页");
                    string t1 = PdfApi.PageText(re, 1);
                    Check(t1.IndexOf("Extra Doc - Page 1", StringComparison.OrdinalIgnoreCase) >= 0, "第2页内容来自 extra 第1页");
                    string t5 = PdfApi.PageText(re, 5);
                    Check(t5.IndexOf("Page 2", StringComparison.OrdinalIgnoreCase) >= 0, "第6页内容来自 src 第2页");
                    IntPtr rp = PdfApi.FPDF_LoadPage(re, 0);
                    if (rp != IntPtr.Zero)
                    {
                        Check(PdfApi.FPDFPage_GetRotation(rp) == 1, "第1页旋转已落盘 = " + PdfApi.FPDFPage_GetRotation(rp));
                        PdfApi.FPDF_ClosePage(rp);
                    }
                    PdfApi.FPDF_CloseDocument(re);
                }
            }
            finally
            {
                PdfApi.FPDF_DestroyLibrary();
            }

            Console.WriteLine(_fail == 0 ? "ALL PASS" : (_fail + " FAILED"));
            return _fail == 0 ? 0 : 1;
        }
    }
}
