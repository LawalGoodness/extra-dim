using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace ExtraDim
{
    // Renders the Extra Dim icon at every Windows icon size and packs them into one .ico.
    static class MakeIcon
    {
        static readonly int[] Sizes = { 16, 20, 24, 32, 40, 48, 64, 96, 128, 256 };

        static int Main(string[] args)
        {
            string outDir = args.Length > 0 ? args[0] : ".";
            Directory.CreateDirectory(outDir);

            var images = new List<byte[]>();
            foreach (int n in Sizes)
            {
                using (var bmp = Draw(n))
                {
                    images.Add(n >= 256 ? Png(bmp) : Dib(bmp));
                    if (n == 256) bmp.Save(Path.Combine(outDir, "icon-256.png"), ImageFormat.Png);
                }
            }

            using (var fs = File.Create(Path.Combine(outDir, "ExtraDim.ico")))
            using (var w = new BinaryWriter(fs))
            {
                w.Write((short)0); w.Write((short)1); w.Write((short)Sizes.Length);
                int offset = 6 + 16 * Sizes.Length;
                for (int i = 0; i < Sizes.Length; i++)
                {
                    int n = Sizes[i];
                    w.Write((byte)(n >= 256 ? 0 : n)); w.Write((byte)(n >= 256 ? 0 : n));
                    w.Write((byte)0); w.Write((byte)0);
                    w.Write((short)1); w.Write((short)32);
                    w.Write(images[i].Length); w.Write(offset);
                    offset += images[i].Length;
                }
                foreach (var img in images) w.Write(img);
            }
            Console.WriteLine("Wrote ExtraDim.ico and icon-256.png to " + Path.GetFullPath(outDir));
            return 0;
        }

        static Bitmap Draw(int n)
        {
            var bmp = new Bitmap(n, n, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);

                float inset = n >= 48 ? n * 0.035f : 0f;
                var box = new RectangleF(inset, inset, n - 2 * inset, n - 2 * inset);
                float w = box.Width;
                float cx = box.X + w / 2, cy = box.Y + w / 2;

                using (var path = Art.RoundRect(box, w * 0.24f))
                {
                    using (var bg = new LinearGradientBrush(box, Art.OnTop, Art.OnBottom, 65f))
                        g.FillPath(bg, path);
                    g.SetClip(path);

                    // soft sheen across the top
                    using (var hl = new LinearGradientBrush(new RectangleF(box.X, box.Y, w, w * 0.6f),
                        Color.FromArgb(55, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f))
                        g.FillEllipse(hl, box.X - w * 0.3f, box.Y - w * 0.62f, w * 1.6f, w * 1.15f);

                    if (n >= 24)
                        Art.Ring(g, cx, cy, w * 0.355f, Math.Max(1.4f, w * 0.055f), 0.68f,
                            Color.FromArgb(55, 255, 255, 255), Art.MoonDeep);

                    float R = (n >= 24 ? 0.215f : 0.31f) * w;
                    float mx = cx - w * 0.02f, my = cy + w * 0.01f;
                    if (n >= 32) Art.Glow(g, mx, my, R * 1.65f, Art.MoonDeep, 70);
                    Art.FillMoon(g, mx, my, R, Art.MoonLight, Art.MoonDeep);

                    if (n >= 32)
                    {
                        Art.Sparkle(g, cx + w * 0.095f, cy - w * 0.095f, w * 0.068f, Art.MoonLight);
                        Art.Sparkle(g, cx + w * 0.17f, cy + w * 0.045f, w * 0.038f, Color.FromArgb(220, Art.MoonLight));
                    }
                    g.ResetClip();
                }
            }
            return bmp;
        }

        static byte[] Png(Bitmap bmp)
        {
            using (var ms = new MemoryStream()) { bmp.Save(ms, ImageFormat.Png); return ms.ToArray(); }
        }

        // Classic 32bpp DIB icon entry: header (double height), bottom-up BGRA, then AND mask.
        static byte[] Dib(Bitmap bmp)
        {
            int n = bmp.Width;
            int maskStride = ((n + 31) / 32) * 4;
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(40); w.Write(n); w.Write(n * 2);
                w.Write((short)1); w.Write((short)32);
                w.Write(0); w.Write(n * n * 4 + maskStride * n);
                w.Write(0); w.Write(0); w.Write(0); w.Write(0);

                var mask = new byte[maskStride * n];
                for (int y = n - 1; y >= 0; y--)
                {
                    int row = n - 1 - y;
                    for (int x = 0; x < n; x++)
                    {
                        Color c = bmp.GetPixel(x, y);
                        w.Write(c.B); w.Write(c.G); w.Write(c.R); w.Write(c.A);
                        if (c.A == 0) mask[row * maskStride + x / 8] |= (byte)(0x80 >> (x % 8));
                    }
                }
                w.Write(mask);
                return ms.ToArray();
            }
        }
    }
}
