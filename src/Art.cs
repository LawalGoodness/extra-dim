using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace ExtraDim
{
    // Shared drawing for the app icon and the floating bubble, so both look the same.
    static class Art
    {
        public static readonly Color OnTop = Color.FromArgb(0x6A, 0x4F, 0xE0);
        public static readonly Color OnBottom = Color.FromArgb(0x14, 0x0F, 0x3A);
        public static readonly Color OffTop = Color.FromArgb(0x55, 0x5B, 0x6A);
        public static readonly Color OffBottom = Color.FromArgb(0x1E, 0x21, 0x29);
        public static readonly Color MoonLight = Color.FromArgb(0xFF, 0xF4, 0xCF);
        public static readonly Color MoonDeep = Color.FromArgb(0xFF, 0xC2, 0x5E);

        // Crescent = circle (cx,cy,R) minus a "bite" circle offset up-right.
        public static GraphicsPath Crescent(float cx, float cy, float R)
        {
            float r = R * 0.86f;
            float bx = cx + R * 0.38f, by = cy - R * 0.26f;
            double dx = bx - cx, dy = by - cy, d = Math.Sqrt(dx * dx + dy * dy);
            double a = (R * R - r * r + d * d) / (2 * d);
            double h = Math.Sqrt(Math.Max(0, R * R - a * a));
            double angB = Math.Atan2(dy, dx) * 180 / Math.PI;
            double alpha = Math.Acos(a / R) * 180 / Math.PI;
            double beta = Math.Atan2(h, d - a) * 180 / Math.PI;

            var p = new GraphicsPath();
            p.AddArc(cx - R, cy - R, 2 * R, 2 * R, (float)(angB + alpha), (float)(360 - 2 * alpha));
            p.AddArc(bx - r, by - r, 2 * r, 2 * r, (float)(angB + 180 + beta), (float)(-2 * beta));
            p.CloseFigure();
            return p;
        }

        public static void FillMoon(Graphics g, float cx, float cy, float R, Color light, Color deep)
        {
            using (var path = Crescent(cx, cy, R))
            using (var b = new LinearGradientBrush(new RectangleF(cx - R, cy - R - 1, 2 * R, 2 * R + 2), light, deep, 90f))
                g.FillPath(b, path);
        }

        public static void Glow(Graphics g, float cx, float cy, float radius, Color c, int alpha)
        {
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(cx - radius, cy - radius, radius * 2, radius * 2);
                using (var b = new PathGradientBrush(path))
                {
                    b.CenterColor = Color.FromArgb(alpha, c);
                    b.SurroundColors = new[] { Color.FromArgb(0, c) };
                    g.FillPath(b, path);
                }
            }
        }

        // Four-point sparkle.
        public static void Sparkle(Graphics g, float x, float y, float s, Color c)
        {
            float i = s * 0.26f;
            var pts = new PointF[8];
            for (int k = 0; k < 8; k++)
            {
                double ang = Math.PI / 4 * k - Math.PI / 2;
                float rr = (k % 2 == 0) ? s : i;
                pts[k] = new PointF(x + (float)(Math.Cos(ang) * rr), y + (float)(Math.Sin(ang) * rr));
            }
            using (var b = new SolidBrush(c)) g.FillPolygon(b, pts);
        }

        // Level ring: faint full track plus a bright arc from 12 o'clock clockwise.
        public static void Ring(Graphics g, float cx, float cy, float radius, float width, float fraction, Color track, Color arc)
        {
            var rect = new RectangleF(cx - radius, cy - radius, radius * 2, radius * 2);
            using (var p = new Pen(track, width)) g.DrawEllipse(p, rect);
            if (fraction <= 0) return;
            using (var p = new Pen(arc, width))
            {
                p.StartCap = LineCap.Round;
                p.EndCap = LineCap.Round;
                g.DrawArc(p, rect, -90f, Math.Max(1f, 360f * Math.Min(1f, fraction)));
            }
        }

        public static GraphicsPath RoundRect(RectangleF r, float radius)
        {
            float d = radius * 2;
            var p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }
}
