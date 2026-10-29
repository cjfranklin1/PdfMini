using System;
using System.Drawing;

namespace PdfMini
{
    /// <summary>排版布局单元测试：只验证 NupLayout 的几何计算</summary>
    internal static class NupTest
    {
        private static int _pass;
        private static int _fail;

        private static void Check(bool ok, string what)
        {
            if (ok) { _pass++; Console.WriteLine("  PASS  " + what); }
            else { _fail++; Console.WriteLine("  FAIL  " + what); }
        }

        private static void RowsCols(int per, NupDirection dir, bool land,
                                     int expRows, int expCols, string what)
        {
            int r, c;
            NupLayout.RowsCols(per, dir, land, out r, out c);
            Check(r == expRows && c == expCols,
                what + " → 期望 " + expRows + "行" + expCols + "列，实际 " + r + "行" + c + "列");
        }

        private static int Main(string[] args)
        {
            Console.WriteLine("═══ NupLayout 排版计算测试 ═══\n");

            // A4 可打印区域（1/100 英寸）：8.27" x 11.69"，扣 1cm 边距
            Rectangle a4 = new Rectangle(39, 39, 748, 1091);

            Console.WriteLine("[1] 行×列 决策");
            RowsCols(1, NupDirection.Auto, false, 1, 1, "1页/张");
            RowsCols(1, NupDirection.Auto, true, 1, 1, "1页/张(横版)");
            // 车票场景：横版页面，自动 → 上下排
            RowsCols(2, NupDirection.Auto, true, 2, 1, "2页/张 横版自动");
            // 竖版页面，自动 → 左右排
            RowsCols(2, NupDirection.Auto, false, 1, 2, "2页/张 竖版自动");
            RowsCols(2, NupDirection.Vertical, false, 2, 1, "2页/张 强制上下");
            RowsCols(2, NupDirection.Horizontal, true, 1, 2, "2页/张 强制左右");
            RowsCols(4, NupDirection.Auto, false, 2, 2, "4页/张");
            RowsCols(6, NupDirection.Auto, false, 3, 2, "6页/张");
            RowsCols(8, NupDirection.Auto, false, 4, 2, "8页/张");

            Console.WriteLine("\n[2] 2页/张 上下排（车票场景）");
            {
                Rectangle[] cells = NupLayout.Compute(a4, 2, NupDirection.Vertical, 2f, true);
                Check(cells.Length == 2, "格子数 = 2");
                Check(cells[0].Width == a4.Width, "上格宽度 = 可用宽度");
                Check(cells[1].Width == a4.Width, "下格宽度 = 可用宽度");
                Check(Math.Abs(cells[0].Height - cells[1].Height) <= 2,
                      "两格高度差 ≤ 2（最后一格吸收余数）");
                Check(cells[0].Top == a4.Top, "上格贴顶");
                Check(cells[1].Bottom == a4.Bottom, "下格贴底");
                int gap = cells[1].Top - cells[0].Bottom;
                Check(gap >= 7 && gap <= 9, "间距 ≈ 2mm（8 单位），实际 " + gap);
                Check(cells[0].Height * 2 <= a4.Height, "两格不超出可用区");
            }

            Console.WriteLine("\n[3] 2页/张 左右排");
            {
                Rectangle[] cells = NupLayout.Compute(a4, 2, NupDirection.Horizontal, 0f, false);
                Check(cells.Length == 2, "格子数 = 2");
                Check(cells[0].Height == a4.Height, "左格高度 = 可用高度");
                Check(cells[0].Left == a4.Left, "左格贴左");
                Check(cells[1].Right == a4.Right, "右格贴右");
                Check(Math.Abs(cells[0].Width - cells[1].Width) <= 2,
                      "两格宽度差 ≤ 2（最后一格吸收余数）");
                Check(cells[1].Left - cells[0].Right == 0, "间距 0（gap=0）");
            }

            Console.WriteLine("\n[4] 4页/张");
            {
                Rectangle[] cells = NupLayout.Compute(a4, 4, NupDirection.Auto, 3f, false);
                Check(cells.Length == 4, "格子数 = 4");
                Check(cells[0].Left == cells[2].Left, "上下两行左对齐");
                Check(cells[0].Top == cells[1].Top, "左右两列顶对齐");
                Check(Math.Abs(cells[0].Width - cells[1].Width) <= 2, "列宽度差 ≤ 2");
                Check(Math.Abs(cells[0].Height - cells[2].Height) <= 2, "行高度差 ≤ 2");
                // 阅读顺序：左上、右上、左下、右下
                Check(cells[0].Left < cells[1].Left && cells[0].Top == cells[1].Top,
                      "第1格在左上、第2格在右上");
                Check(cells[2].Top > cells[0].Top, "第3格在第二行");
            }

            Console.WriteLine("\n[5] 边界情况");
            {
                Rectangle tight = new Rectangle(0, 0, 100, 60);
                Rectangle[] c1 = NupLayout.Compute(tight, 2, NupDirection.Vertical, 50f, true);
                Check(c1.Length == 2, "极小区域仍返回 2 格");
                Check(c1[0].Width >= 1 && c1[0].Height >= 1, "格子尺寸不小于 1");

                Rectangle[] c2 = NupLayout.Compute(a4, 2, NupDirection.Vertical, 0f, true);
                Check(c2[0].Height + c2[1].Height <= a4.Height, "gap=0 时不超出");
            }

            Console.WriteLine("\n[6] 所有格子都在可用区域内");
            {
                foreach (int per in new int[] { 1, 2, 4, 6, 8 })
                {
                    Rectangle[] cells = NupLayout.Compute(a4, per, NupDirection.Auto, 2f, true);
                    bool inside = true;
                    foreach (Rectangle c in cells)
                    {
                        if (c.Left < a4.Left || c.Top < a4.Top ||
                            c.Right > a4.Right || c.Bottom > a4.Bottom)
                        { inside = false; break; }
                    }
                    Check(inside, per + "页/张 全部格子都在可用区域内");
                }
            }

            Console.WriteLine("\n═══════════════════════════════");
            Console.WriteLine(string.Format("结果：{0} 通过 / {1} 失败", _pass, _fail));
            return _fail == 0 ? 0 : 1;
        }
    }
}
