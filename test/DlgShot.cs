using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace PdfMini
{
    /// <summary>把打印对话框画出来存图，用来检查布局</summary>
    internal static class DlgShot
    {
        private static int Main(string[] args)
        {
            string outPath = args.Length > 0 ? args[0] : "test/nup_dialog.png";
            int pages = args.Length > 1 ? int.Parse(args[1]) : 6;
            bool land = args.Length > 2 ? (args[2] == "1") : true;

            Application.EnableVisualStyles();

            NupSettings cfg = new NupSettings();
            cfg.PerSheet = 2;
            cfg.Direction = NupDirection.Auto;
            cfg.GapMm = 2f;
            cfg.DrawBorder = true;
            cfg.PageFrom = 1;
            cfg.PageTo = pages;

            using (NupSettingsForm f = new NupSettingsForm(cfg, pages, land))
            {
                f.StartPosition = FormStartPosition.Manual;
                f.Location = new Point(-4000, -4000);
                f.ShowInTaskbar = false;
                f.Show();
                f.Refresh();
                Application.DoEvents();
                System.Threading.Thread.Sleep(350);
                Application.DoEvents();

                using (Bitmap bmp = new Bitmap(f.Width, f.Height))
                {
                    f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height));
                    bmp.Save(outPath, ImageFormat.Png);
                    Console.WriteLine("-> " + outPath + "  " + bmp.Width + "x" + bmp.Height);
                }
                f.Close();
            }
            return 0;
        }
    }
}
