using System;
using System.Collections.Generic;
using System.IO;

namespace PdfMini
{
    /// <summary>一个已打开的源 PDF（pdfium 文档句柄）。</summary>
    internal sealed class PdfDoc
    {
        public IntPtr Handle;
        public string Path;

        public PdfDoc(IntPtr handle, string path)
        {
            Handle = handle;
            Path = path;
        }
    }

    /// <summary>会话里的一页：指向某个源文档的某一页，外加额外旋转。</summary>
    internal sealed class PageRef
    {
        public PdfDoc Doc;
        public int Index;      // 源文档内的 0 基页号
        public int Rot;        // 额外旋转角度：0/90/180/270

        public PageRef(PdfDoc doc, int index)
        {
            Doc = doc;
            Index = index;
            Rot = 0;
        }
    }

    /// <summary>
    /// 编辑会话：页面顺序由 Pages 列表决定，与源文件无关；
    /// 保存时按此顺序逐段导入新文档，因此不会改动原文件。
    /// </summary>
    internal sealed class Session
    {
        public readonly List<PdfDoc> Docs = new List<PdfDoc>();
        public readonly List<PageRef> Pages = new List<PageRef>();
        public readonly object Sync = new object();

        /// <summary>会话对应的文件路径（首次打开的来源；合并后可能为 null）。</summary>
        public string BasePath;

        public bool Dirty;

        public int Count { get { return Pages.Count; } }

        public void Dispose()
        {
            lock (Sync)
            {
                for (int i = 0; i < Docs.Count; i++)
                {
                    if (Docs[i].Handle != IntPtr.Zero)
                    {
                        PdfApi.FPDF_CloseDocument(Docs[i].Handle);
                        Docs[i].Handle = IntPtr.Zero;
                    }
                }
                Docs.Clear();
                Pages.Clear();
            }
        }
    }
}
