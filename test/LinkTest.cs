using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace PdfMini
{
    /// <summary>验证左栏「大纲」「页面」点击后右侧是否跟随</summary>
    internal static class LinkTest
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

        private static object F(object o, string name)
        {
            FieldInfo f = o.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            return f == null ? null : f.GetValue(o);
        }

        private static void SetF(object o, string name, object v)
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
            string pdf = args.Length > 0 ? args[0] : "test\\outline.pdf";
            if (!File.Exists(pdf)) { Console.WriteLine("找不到 " + pdf); return 2; }

            Console.WriteLine("═══ 左栏 → 右侧 联动测试 ═══");
            Console.WriteLine("源: " + pdf + "\n");

            Application.EnableVisualStyles();
            PdfApi.FPDF_InitLibrary();

            MainForm form = new MainForm(null);
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-6000, -6000);
            form.ShowInTaskbar = false;
            form.Show();
            Pump(500);

            Call(form, "OpenFile", Path.GetFullPath(pdf));
            Pump(3000);

            object sess = F(form, "_s");
            if (sess == null) { Console.WriteLine("  [致命] 会话未建立"); return 1; }
            int total = (int)sess.GetType().GetProperty("Count").GetValue(sess, null);
            Console.WriteLine("  文档页数 = " + total);

            object view = F(form, "_view");
            object outline = F(form, "_outline");
            object thumbs = F(form, "_thumbs");
            TabControl tabs = F(form, "_tabs") as TabControl;

            // ── 1) 大纲树有没有内容 ──
            TreeView tv = outline as TreeView;
            Check(tv != null, "_outline 是 TreeView");
            if (tv == null) { form.Close(); return 1; }
            Console.WriteLine("  大纲根节点数 = " + tv.Nodes.Count);
            Check(tv.Nodes.Count > 0, "大纲有节点（outline.pdf 应有书签）");

            // 收集所有节点（含子节点）
            System.Collections.Generic.List<TreeNode> all = new System.Collections.Generic.List<TreeNode>();
            CollectNodes(tv.Nodes, all);
            Console.WriteLine("  大纲节点总数 = " + all.Count);
            foreach (TreeNode n in all)
                Console.WriteLine("    · \"" + n.Text + "\"");

            // ── 2) 点「Chapter 3」（指向第 4 页 = 索引 3）──
            TreeNode target = null;
            foreach (TreeNode n in all)
                if (n.Text != null && n.Text.IndexOf("Chapter 3", StringComparison.OrdinalIgnoreCase) >= 0)
                    target = n;

            Check(target != null, "找到「Chapter 3」节点");
            if (target != null)
            {
                OutlineItem oi = target.Tag as OutlineItem;
                Check(oi != null, "节点 Tag 是 OutlineItem");
                if (oi != null)
                {
                    Console.WriteLine("    PageIndex = " + oi.PageIndex + "（期望 3 = 第 4 页）");
                    Check(oi.PageIndex == 3, "PageIndex 指向第 4 页");
                }

                // 点击前状态
                {
                    object c0 = F(form, "_cur");
                    System.Collections.IList t0 = (System.Collections.IList)F(view, "_tops");
                    float sy0 = (float)F(view, "_scrollY");
                    Console.WriteLine("    点击前: _cur=" + (c0 == null ? "?" : c0.ToString())
                        + "  _tops.Count=" + (t0 == null ? "null" : t0.Count.ToString())
                        + "  _scrollY=" + sy0.ToString("F1")
                        + "  ClientH=" + ((Control)view).ClientSize.Height);
                }

                // 先切到「大纲」标签（真实用户必然先看到标签页）
                if (tabs != null)
                {
                    Console.WriteLine("    标签页: " + tabs.TabCount + " 个，当前 index=" + tabs.SelectedIndex);
                    tabs.SelectedIndex = 1;   // 大纲
                    Pump(600);
                    Console.WriteLine("    切到大纲后 TreeView.Handle 已建=" + (tv.IsHandleCreated)
                        + "  Visible=" + tv.Visible);
                }

                tv.SelectedNode = target;   // 模拟点击
                Pump(900);

                {
                    System.Collections.IList t0 = (System.Collections.IList)F(view, "_tops");
                    Console.WriteLine("    点击后: _tops.Count=" + (t0 == null ? "null" : t0.Count.ToString())
                        + "  _scrollY=" + ((float)F(view, "_scrollY")).ToString("F1"));
                }

                object curObj = F(form, "_cur");
                int cur = curObj == null ? -1 : (int)curObj;
                Console.WriteLine("    点击后 _cur = " + cur + "（期望 3）");
                Check(cur == 3, "点大纲后当前页 = 第 4 页");

                // 右侧滚动位置应等于第 4 页顶部
                float scrollY = (float)F(view, "_scrollY");
                System.Collections.IList tops =
                    (System.Collections.IList)F(view, "_tops");
                if (tops != null && tops.Count > 3)
                {
                    float top3 = (float)tops[3];
                    float contentH = (float)F(view, "_contentH");
                    float scale = (float)F(view, "_scale");
                    Control vc = view as Control;
                    int clientH = vc == null ? -1 : vc.ClientSize.Height;
                    Console.WriteLine("    诊断: _scrollY=" + scrollY.ToString("F1")
                        + "  _tops[3]=" + top3.ToString("F1")
                        + "  _contentH=" + contentH.ToString("F1")
                        + "  ClientH=" + clientH
                        + "  _scale=" + scale.ToString("F4")
                        + "  maxScroll=" + Math.Max(0f, contentH - clientH).ToString("F1"));
                    Check(Math.Abs(scrollY - top3) < 2f, "右侧已滚到第 4 页顶部");
                }
                else
                {
                    Check(false, "_tops 未就绪（页面布局没完成）");
                }
            }

            // ── 3) 点缩略图第 2 页，右侧也应跟随 ──
            ListView lv = thumbs as ListView;
            if (lv != null && lv.Items.Count >= 2)
            {
                if (tabs != null) tabs.SelectedIndex = 0;   // 页面
                Pump(500);
                Console.WriteLine("    切到页标签: lv.Handle 已建=" + lv.IsHandleCreated
                    + "  Visible=" + lv.Visible + "  items=" + lv.Items.Count);

                // 复现现场：当前已选中第 4 页（索引 3），多选累积状态下再点第 2 页（索引 1）
                lv.Items[3].Selected = true;
                Pump(600);
                Console.WriteLine("    先选中第 4 页, _cur=" + F(form, "_cur"));
                SetF(form, "_thumbClickIdx", 1);     // 模拟鼠标命中的是第 2 页
                lv.Items[1].Selected = true;          // 多选累积
                lv.Focus();
                Pump(900);

                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                foreach (int si in lv.SelectedIndices) { if (si >= 0) sb.Append(si).Append(','); }
                Console.WriteLine("    SelectedIndices=[" + sb.ToString().TrimEnd(',') + "]（多选累积）");
                int cur = (int)F(form, "_cur");
                Console.WriteLine("    多选累积下点第 2 页, _cur = " + cur + "（期望 1）");
                Check(cur == 1, "多选累积下点前面的页也能跳（修复点）");
            }
            else
            {
                Check(false, "缩略图列表可用（items=" + (lv == null ? "?" : lv.Items.Count.ToString()) + "）");
            }

            form.Close();
            Pump(300);

            Console.WriteLine("\n═══════════════════════════════");
            Console.WriteLine("结果：" + _pass + " 通过 / " + _fail + " 失败");
            return _fail == 0 ? 0 : 1;
        }

        private static void CollectNodes(TreeNodeCollection nodes, System.Collections.Generic.List<TreeNode> outList)
        {
            foreach (TreeNode n in nodes)
            {
                outList.Add(n);
                if (n.Nodes.Count > 0) CollectNodes(n.Nodes, outList);
            }
        }
    }
}
