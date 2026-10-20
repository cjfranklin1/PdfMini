using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace PdfMini
{
    /// <summary>pdfium 的最小 P/Invoke 封装：只包用得到的 API。</summary>
    internal static class PdfApi
    {
        private const string D = "pdfium.dll";

        public const int RenderAnnot = 0x01;
        public const int RenderLcd = 0x02;
        public const int BitmapFormatBgra = 4;
        public const int ErrPassword = 4;
        public const int ErrFormat = 3;
        public const int SaveNoIncremental = 2;

        [DllImport(D)] public static extern void FPDF_InitLibrary();
        [DllImport(D)] public static extern void FPDF_DestroyLibrary();
        [DllImport(D)] public static extern uint FPDF_GetLastError();

        [DllImport(D)] public static extern IntPtr FPDF_LoadDocument(byte[] path, byte[] password);
        [DllImport(D)] public static extern void FPDF_CloseDocument(IntPtr doc);
        [DllImport(D)] public static extern int FPDF_GetPageCount(IntPtr doc);

        [DllImport(D)] public static extern IntPtr FPDF_LoadPage(IntPtr doc, int index);
        [DllImport(D)] public static extern void FPDF_ClosePage(IntPtr page);
        [DllImport(D)] public static extern float FPDF_GetPageWidthF(IntPtr page);
        [DllImport(D)] public static extern float FPDF_GetPageHeightF(IntPtr page);

        [DllImport(D)] public static extern void FPDF_RenderPageBitmap(IntPtr bmp, IntPtr page,
            int startX, int startY, int sizeX, int sizeY, int rotate, int flags);

        [DllImport(D)] public static extern IntPtr FPDFBitmap_Create(int width, int height, int alpha);
        [DllImport(D)] public static extern IntPtr FPDFBitmap_CreateEx(int width, int height, int format, IntPtr firstScan, int stride);
        [DllImport(D)] public static extern void FPDFBitmap_FillRect(IntPtr bmp, int left, int top, int width, int height, uint color);
        [DllImport(D)] public static extern IntPtr FPDFBitmap_GetBuffer(IntPtr bmp);
        [DllImport(D)] public static extern void FPDFBitmap_Destroy(IntPtr bmp);

        [DllImport(D)] public static extern int FPDF_ImportPages(IntPtr destDoc, IntPtr srcDoc, byte[] pageRange, int index);
        [DllImport(D)] public static extern IntPtr FPDF_CreateNewDocument();
        [DllImport(D)] public static extern int FPDF_SaveAsCopy(IntPtr doc, ref FpdfFileWrite fileWrite, uint flags);

        [DllImport(D)] public static extern int FPDFPage_GetRotation(IntPtr page);
        [DllImport(D)] public static extern void FPDFPage_SetRotation(IntPtr page, int rotate);

        [DllImport(D)] public static extern IntPtr FPDFText_LoadPage(IntPtr page);
        [DllImport(D)] public static extern void FPDFText_ClosePage(IntPtr textPage);
        [DllImport(D)] public static extern int FPDFText_CountChars(IntPtr textPage);
        [DllImport(D)] public static extern int FPDFText_GetText(IntPtr textPage, int startIndex, int count, ushort[] result);

        // ── 文字搜索 / 命中矩形（高亮用）
        [DllImport(D)] public static extern IntPtr FPDFText_FindStart(IntPtr textPage, byte[] findWhat, uint flags, int startIndex);
        [DllImport(D)] public static extern int FPDFText_FindNext(IntPtr handle);
        [DllImport(D)] public static extern int FPDFText_GetSchResultIndex(IntPtr handle);
        [DllImport(D)] public static extern int FPDFText_GetSchCount(IntPtr handle);
        [DllImport(D)] public static extern void FPDFText_FindClose(IntPtr handle);
        [DllImport(D)] public static extern int FPDFText_CountRects(IntPtr textPage, int startIndex, int count);
        [DllImport(D)] public static extern int FPDFText_GetRect(IntPtr textPage, int rectIndex,
            out double left, out double top, out double right, out double bottom);

        /// <summary>UTF-16LE + 结尾 0（pdfium 的 FPDF_WIDESTRING）。</summary>
        public static byte[] Utf16Z(string s)
        {
            if (s == null) return null;
            byte[] body = Encoding.Unicode.GetBytes(s);
            byte[] buf = new byte[body.Length + 2];
            Buffer.BlockCopy(body, 0, buf, 0, body.Length);
            return buf;
        }

        /// <summary>
        /// 在一页里找 needle 的全部命中矩形（页面坐标 pt，未旋转）。
        /// 一次命中可能跨行 → 多个矩形，所以 rects 数量 ≥ matchCount。
        /// </summary>
        public static List<RectangleF> FindHitRects(IntPtr textPage, string needle, int maxRects, out int matchCount)
        {
            List<RectangleF> rects = new List<RectangleF>();
            matchCount = 0;
            if (textPage == IntPtr.Zero || string.IsNullOrEmpty(needle)) return rects;

            IntPtr handle = FPDFText_FindStart(textPage, Utf16Z(needle), 0, 0);
            if (handle == IntPtr.Zero) return rects;
            try
            {
                int guard = 0;
                while (FPDFText_FindNext(handle) != 0 && guard++ < 10000)
                {
                    int start = FPDFText_GetSchResultIndex(handle);
                    int count = FPDFText_GetSchCount(handle);
                    if (count <= 0) continue;
                    matchCount++;

                    int n = FPDFText_CountRects(textPage, start, count);
                    for (int r = 0; r < n && rects.Count < maxRects; r++)
                    {
                        double left, top, right, bottom;
                        if (FPDFText_GetRect(textPage, r, out left, out top, out right, out bottom) == 0) continue;
                        rects.Add(RectangleF.FromLTRB((float)left, (float)top, (float)right, (float)bottom));
                    }
                    if (rects.Count >= maxRects) break;
                }
            }
            finally
            {
                FPDFText_FindClose(handle);
            }
            return rects;
        }

        // ── 书签 / 大纲
        [DllImport(D)] public static extern IntPtr FPDFBookmark_GetFirstChild(IntPtr doc, IntPtr bookmark);
        [DllImport(D)] public static extern IntPtr FPDFBookmark_GetNextSibling(IntPtr doc, IntPtr bookmark);
        [DllImport(D)] public static extern uint FPDFBookmark_GetTitle(IntPtr bookmark, byte[] buffer, uint buflen);
        [DllImport(D)] public static extern IntPtr FPDFBookmark_GetDest(IntPtr doc, IntPtr bookmark);
        [DllImport(D)] public static extern int FPDFDest_GetDestPageIndex(IntPtr doc, IntPtr dest);

        /// <summary>读一条书签的标题（pdfium 给的是 UTF-16LE，含结尾 0）。</summary>
        public static string BookmarkTitle(IntPtr bookmark)
        {
            uint len = FPDFBookmark_GetTitle(bookmark, null, 0);
            if (len <= 2) return string.Empty;
            byte[] buf = new byte[len];
            FPDFBookmark_GetTitle(bookmark, buf, len);
            return Encoding.Unicode.GetString(buf, 0, (int)len - 2);
        }

        public static byte[] Utf8Z(string s)
        {
            if (s == null) return null;
            byte[] body = Encoding.UTF8.GetBytes(s);
            byte[] buf = new byte[body.Length + 1];
            Buffer.BlockCopy(body, 0, buf, 0, body.Length);
            return buf;
        }

        public static byte[] AsciiZ(string s)
        {
            if (s == null) return null;
            byte[] body = Encoding.ASCII.GetBytes(s);
            byte[] buf = new byte[body.Length + 1];
            Buffer.BlockCopy(body, 0, buf, 0, body.Length);
            return buf;
        }

        /// <summary>渲染一页到 Bitmap。scale: 页面 pt → 像素的缩放（1.0 = 72dpi）。</summary>
        public static Bitmap RenderPage(IntPtr page, float scale, int rotationQuarters, bool night)
        {
            float pw = FPDF_GetPageWidthF(page);
            float ph = FPDF_GetPageHeightF(page);
            if (pw <= 0 || ph <= 0) return null;

            bool swapped = (rotationQuarters % 2) != 0;
            float wPt = swapped ? ph : pw;
            float hPt = swapped ? pw : ph;

            // 防爆内存：单边最多 5000px
            float maxSide = Math.Max(wPt, hPt);
            if (maxSide * scale > 5000f) scale = 5000f / maxSide;
            if (scale < 0.02f) scale = 0.02f;

            int w = (int)Math.Round(wPt * scale);
            int h = (int)Math.Round(hPt * scale);
            if (w < 1) w = 1;
            if (h < 1) h = 1;

            // 直接渲染进托管位图：用 FPDFBitmap_CreateEx 绑定 Bitmap 的扫描行，
            // 避免“包装外部缓冲区再 Clone”导致的内存共享/悬垂指针。
            Bitmap result = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            BitmapData data = result.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            IntPtr bmp = FPDFBitmap_CreateEx(w, h, BitmapFormatBgra, data.Scan0, data.Stride);
            if (bmp == IntPtr.Zero)
            {
                result.UnlockBits(data);
                result.Dispose();
                throw new InvalidOperationException("位图分配失败（页面过大）");
            }
            try
            {
                FPDFBitmap_FillRect(bmp, 0, 0, w, h, 0xFFFFFFFF);
                FPDF_RenderPageBitmap(bmp, page, 0, 0, w, h, rotationQuarters % 4, RenderAnnot | RenderLcd);
            }
            finally
            {
                FPDFBitmap_Destroy(bmp);   // CreateEx 绑定的外部缓冲不会被释放
                result.UnlockBits(data);
            }

            if (night) InvertInPlace(result);
            return result;
        }

        /// <summary>
        /// 渲染页面的一条横向切片（打印用）：把整页按 sizeX/sizeY 排版，
        /// 只输出 [bandTop, bandTop+bandH) 这段像素，内存只占一条带子的量。
        /// </summary>
        public static Bitmap RenderBand(IntPtr page, float scale, int rotationQuarters,
            int fullW, int fullH, int bandTop, int bandH)
        {
            if (fullW < 1) fullW = 1;
            if (fullH < 1) fullH = 1;
            if (bandH < 1) bandH = 1;
            if (bandTop < 0) bandTop = 0;
            if (bandTop + bandH > fullH) bandH = fullH - bandTop;
            if (bandH < 1) bandH = 1;

            Bitmap result = new Bitmap(fullW, bandH, PixelFormat.Format32bppArgb);
            BitmapData data = result.LockBits(new Rectangle(0, 0, fullW, bandH), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            IntPtr bmp = FPDFBitmap_CreateEx(fullW, bandH, BitmapFormatBgra, data.Scan0, data.Stride);
            if (bmp == IntPtr.Zero)
            {
                result.UnlockBits(data);
                result.Dispose();
                throw new InvalidOperationException("打印位图分配失败");
            }
            try
            {
                FPDFBitmap_FillRect(bmp, 0, 0, fullW, bandH, 0xFFFFFFFF);
                FPDF_RenderPageBitmap(bmp, page, 0, -bandTop, fullW, fullH, rotationQuarters % 4, RenderAnnot | RenderLcd);
            }
            finally
            {
                FPDFBitmap_Destroy(bmp);
                result.UnlockBits(data);
            }
            return result;
        }

        public static void InvertInPlace(Bitmap target)
        {
            using (Bitmap src = (Bitmap)target.Clone())
            using (Graphics g = Graphics.FromImage(target))
            {
                ColorMatrix cm = new ColorMatrix(new float[][]
                {
                    new float[] { -1f, 0f, 0f, 0f, 0f },
                    new float[] { 0f, -1f, 0f, 0f, 0f },
                    new float[] { 0f, 0f, -1f, 0f, 0f },
                    new float[] { 0f, 0f, 0f, 1f, 0f },
                    new float[] { 1f, 1f, 1f, 0f, 1f }
                });
                using (ImageAttributes ia = new ImageAttributes())
                {
                    ia.SetColorMatrix(cm);
                    g.DrawImage(src, new Rectangle(0, 0, target.Width, target.Height),
                        0, 0, src.Width, src.Height, GraphicsUnit.Pixel, ia);
                }
            }
        }

        /// <summary>取整页文字（UCS-2 → string）。失败返回空串。</summary>
        public static string PageText(IntPtr doc, int index)
        {
            IntPtr page = FPDF_LoadPage(doc, index);
            if (page == IntPtr.Zero) return string.Empty;
            try
            {
                IntPtr tp = FPDFText_LoadPage(page);
                if (tp == IntPtr.Zero) return string.Empty;
                try
                {
                    int n = FPDFText_CountChars(tp);
                    if (n <= 0) return string.Empty;
                    ushort[] buf = new ushort[n + 1];
                    FPDFText_GetText(tp, 0, n, buf);
                    StringBuilder sb = new StringBuilder(n);
                    for (int i = 0; i < n; i++)
                    {
                        char c = (char)buf[i];
                        if (c == '\r') continue;
                        sb.Append(c);
                    }
                    return sb.ToString();
                }
                finally { FPDFText_ClosePage(tp); }
            }
            finally { FPDF_ClosePage(page); }
        }

        public static string ErrorName(uint code)
        {
            switch (code)
            {
                case 0: return "无错误";
                case 1: return "未知错误";
                case 2: return "文件无法打开";
                case 3: return "不是有效的 PDF，或文件已损坏";
                case 4: return "需要密码";
                case 5: return "不支持的安全方案";
                case 6: return "页面错误";
                default: return "错误码 " + code;
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct FpdfFileWrite
    {
        public int version;
        public IntPtr writeBlock;
    }

    /// <summary>FPDF_SaveAsCopy 的回调式写出器。</summary>
    internal sealed class PdfWriter
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int WriteBlockFn(IntPtr self, IntPtr data, uint size);

        private FileStream _fs;
        private byte[] _buf = new byte[65536];
        private bool _failed;

        public static bool Save(IntPtr doc, string path)
        {
            PdfWriter w = new PdfWriter();
            return w.SaveCore(doc, path);
        }

        private bool SaveCore(IntPtr doc, string path)
        {
            WriteBlockFn fn = WriteBlock;
            IntPtr fnPtr = Marshal.GetFunctionPointerForDelegate(fn);
            FpdfFileWrite fw = new FpdfFileWrite();
            fw.version = 1;
            fw.writeBlock = fnPtr;

            _fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            try
            {
                int ok = PdfApi.FPDF_SaveAsCopy(doc, ref fw, PdfApi.SaveNoIncremental);
                _fs.Flush();
                GC.KeepAlive(fn);
                return ok != 0 && !_failed && _fs.Length > 0;
            }
            finally
            {
                _fs.Dispose();
                _fs = null;
            }
        }

        private int WriteBlock(IntPtr self, IntPtr data, uint size)
        {
            try
            {
                int n = (int)size;
                if (n > _buf.Length) _buf = new byte[n];
                Marshal.Copy(data, _buf, 0, n);
                _fs.Write(_buf, 0, n);
                return 1;
            }
            catch
            {
                _failed = true;
                return 0;
            }
        }
    }
}
