using System;
using System.Collections.Generic;

namespace PdfMini
{
    /// <summary>大纲（书签）节点：标题 + 在“会话页序”里的目标页。</summary>
    internal sealed class OutlineItem
    {
        public string Title = string.Empty;
        public int PageIndex = -1;          // 会话页索引；-1 表示该书签指向的页已被删掉/无目标
        public List<OutlineItem> Children = new List<OutlineItem>();
    }

    internal static class OutlineReader
    {
        /// <summary>
        /// 读书签树。目标页号要经过映射：PDF 里的书签指向“源文档页号”，
        /// 而用户可能已经删过页/调过顺序，所以要换成本会话的页索引。
        /// </summary>
        public static List<OutlineItem> Read(PdfDoc doc, Session session, out int total)
        {
            List<OutlineItem> roots = new List<OutlineItem>();
            total = 0;
            if (doc == null || doc.Handle == IntPtr.Zero || session == null) return roots;

            IntPtr first = PdfApi.FPDFBookmark_GetFirstChild(doc.Handle, IntPtr.Zero);
            IntPtr cursor = first;
            int guard = 0;
            while (cursor != IntPtr.Zero && guard++ < 5000)
            {
                roots.Add(Build(doc, session, cursor, 0, ref total));
                cursor = PdfApi.FPDFBookmark_GetNextSibling(doc.Handle, cursor);
            }
            return roots;
        }

        private static OutlineItem Build(PdfDoc doc, Session session, IntPtr bookmark, int depth, ref int total)
        {
            OutlineItem item = new OutlineItem();
            string title = PdfApi.BookmarkTitle(bookmark);
            item.Title = string.IsNullOrEmpty(title) ? "(无标题书签)" : title.Replace("\r", " ").Replace("\n", " ").Trim();
            item.PageIndex = MapPage(doc, session, bookmark);
            total++;

            if (depth < 12)
            {
                IntPtr child = PdfApi.FPDFBookmark_GetFirstChild(doc.Handle, bookmark);
                int guard = 0;
                while (child != IntPtr.Zero && guard++ < 5000)
                {
                    item.Children.Add(Build(doc, session, child, depth + 1, ref total));
                    child = PdfApi.FPDFBookmark_GetNextSibling(doc.Handle, child);
                }
            }
            return item;
        }

        private static int MapPage(PdfDoc doc, Session session, IntPtr bookmark)
        {
            IntPtr dest = PdfApi.FPDFBookmark_GetDest(doc.Handle, bookmark);
            if (dest == IntPtr.Zero) return -1;
            int sourcePage = PdfApi.FPDFDest_GetDestPageIndex(doc.Handle, dest);
            if (sourcePage < 0) return -1;

            for (int i = 0; i < session.Pages.Count; i++)
            {
                PageRef pr = session.Pages[i];
                if (object.ReferenceEquals(pr.Doc, doc) && pr.Index == sourcePage) return i;
            }
            return -1;
        }
    }
}
