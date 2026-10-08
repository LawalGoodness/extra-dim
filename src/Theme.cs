using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ExtraDim
{
    // Dark purple + gold look shared by the menu and the bubble's hover card.
    static class Theme
    {
        public static readonly Color MenuBg = Color.FromArgb(0x1C, 0x19, 0x34);
        public static readonly Color Border = Color.FromArgb(0x3A, 0x34, 0x66);
        public static readonly Color Hover = Color.FromArgb(0x2E, 0x28, 0x58);
        public static readonly Color Fg = Color.FromArgb(0xEC, 0xE8, 0xFF);
        public static readonly Color Muted = Color.FromArgb(0x9C, 0x96, 0xC4);
        public static readonly Color SwitchOff = Color.FromArgb(0x3A, 0x34, 0x66);
        public static readonly Color Danger = Color.FromArgb(0xFF, 0x8E, 0x8E);

        public static float Scale { get { return Native.Dpi / 96f; } }
        public static int Px(float v) { return (int)Math.Round(v * Scale); }

        // Windows 11: native rounded corners and a themed 1px border. Returns false on older Windows.
        public static bool RoundCorners(IntPtr hwnd)
        {
            try
            {
                int round = 2;
                int border = Border.R | (Border.G << 8) | (Border.B << 16);
                bool ok = DwmSetWindowAttribute(hwnd, 33, ref round, 4) == 0;
                DwmSetWindowAttribute(hwnd, 34, ref border, 4);
                return ok;
            }
            catch { return false; }
        }

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        public static void StyleMenu(ContextMenuStrip menu)
        {
            var renderer = new DarkMenuRenderer();
            StyleDropDown(menu, renderer);
            foreach (ToolStripItem it in menu.Items)
            {
                var mi = it as ToolStripMenuItem;
                if (mi != null && mi.HasDropDownItems) StyleDropDown(mi.DropDown, renderer);
            }
        }

        static void StyleDropDown(ToolStripDropDown dd, DarkMenuRenderer renderer)
        {
            dd.Renderer = renderer;
            dd.Font = new Font("Segoe UI", 10f);
            dd.Padding = new Padding(Px(4), Px(6), Px(4), Px(6));
            dd.BackColor = MenuBg;
            var ddm = dd as ToolStripDropDownMenu;
            if (ddm != null) { ddm.ShowCheckMargin = false; ddm.ShowImageMargin = true; ddm.ImageScalingSize = new Size(Px(18), Px(18)); }
            dd.HandleCreated += (s, e) => renderer.NativeBorder = RoundCorners(dd.Handle);
            foreach (ToolStripItem it in dd.Items)
            {
                it.ForeColor = Fg;
                if (!(it is ToolStripSeparator)) it.Padding = new Padding(Px(2), Px(5), Px(2), Px(5));
            }
        }

        // Marks a menu item as an on/off switch (drawn on the right instead of a check mark).
        public static void MakeSwitch(ToolStripMenuItem item)
        {
            item.Tag = "switch";
            item.ShortcutKeyDisplayString = new string(' ', 12);   // reserves room for the switch
        }

        public static Bitmap Icon(string kind)
        {
            int s = Px(18);
            var bmp = new Bitmap(s, s, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                float c = s / 2f, w = Math.Max(1.5f, s * 0.1f);
                switch (kind)
                {
                    case "moon":
                        Art.FillMoon(g, c - s * 0.02f, c + s * 0.03f, s * 0.38f, Art.MoonLight, Art.MoonDeep);
                        break;
                    case "level":
                        Art.Ring(g, c, c, s * 0.34f, w * 1.2f, 0.65f, Color.FromArgb(70, Muted), Art.MoonDeep);
                        break;
                    case "settings":
                        using (var p = new Pen(Muted, w) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                        using (var b = new SolidBrush(Fg))
                        {
                            float[] ys = { s * 0.25f, s * 0.5f, s * 0.75f };
                            float[] ks = { s * 0.65f, s * 0.32f, s * 0.58f };
                            for (int i = 0; i < 3; i++)
                            {
                                g.DrawLine(p, s * 0.14f, ys[i], s * 0.86f, ys[i]);
                                g.FillEllipse(b, ks[i] - s * 0.1f, ys[i] - s * 0.1f, s * 0.2f, s * 0.2f);
                            }
                        }
                        break;
                    case "exit":
                        using (var p = new Pen(Danger, w) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                        {
                            float r = s * 0.32f;
                            g.DrawArc(p, c - r, c - r + s * 0.04f, 2 * r, 2 * r, -60, 300);
                            g.DrawLine(p, c, s * 0.12f, c, s * 0.46f);
                        }
                        break;
                }
            }
            return bmp;
        }

        public static GraphicsPath Round(RectangleF r, float radius) { return Art.RoundRect(r, radius); }
    }

    class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public bool NativeBorder;

        public DarkMenuRenderer() : base(new DarkColors()) { RoundedEdges = false; }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using (var b = new SolidBrush(Theme.MenuBg)) e.Graphics.FillRectangle(b, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            if (NativeBorder) return;
            using (var p = new Pen(Theme.Border))
                e.Graphics.DrawRectangle(p, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        }

        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (e.Item.Selected && e.Item.Enabled)
            {
                var r = new RectangleF(Theme.Px(2), 1, e.Item.Width - Theme.Px(4), e.Item.Height - 2);
                using (var path = Theme.Round(r, Theme.Px(6)))
                using (var b = new SolidBrush(Theme.Hover))
                    g.FillPath(b, path);
            }

            var mi = e.Item as ToolStripMenuItem;
            if (mi != null && "switch".Equals(mi.Tag)) DrawSwitch(g, mi);
        }

        static void DrawSwitch(Graphics g, ToolStripMenuItem mi)
        {
            float h = Theme.Px(16), w = h * 1.8f;
            float x = mi.Width - Theme.Px(14) - w, y = (mi.Height - h) / 2f;
            var track = new RectangleF(x, y, w, h);
            using (var path = Theme.Round(track, h / 2f - 0.01f))
            using (var b = new SolidBrush(mi.Checked ? Art.MoonDeep : Theme.SwitchOff))
                g.FillPath(b, path);
            float k = h - Theme.Px(4);
            float kx = mi.Checked ? x + w - k - Theme.Px(2) : x + Theme.Px(2);
            using (var b = new SolidBrush(mi.Checked ? Color.FromArgb(0x2A, 0x1A, 0x05) : Theme.Muted))
                g.FillEllipse(b, kx, y + Theme.Px(2), k, k);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            // Only the dim-level presets use this: a gold dot beside the current level.
            if (!(e.Item.Tag is int)) return;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = e.ImageRectangle;
            float d = Math.Min(r.Width, r.Height) * 0.45f;
            using (var b = new SolidBrush(Art.MoonDeep))
                g.FillEllipse(b, r.X + (r.Width - d) / 2f, r.Y + (r.Height - d) / 2f, d, d);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? (e.Item.Tag as string == "danger" ? Theme.Danger : Theme.Fg) : Theme.Muted;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.Height / 2;
            using (var p = new Pen(Theme.Border))
                e.Graphics.DrawLine(p, Theme.Px(10), y, e.Item.Width - Theme.Px(10), y);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Theme.Muted;
            base.OnRenderArrow(e);
        }
    }

    class DarkColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return Theme.MenuBg; } }
        public override Color ImageMarginGradientBegin { get { return Theme.MenuBg; } }
        public override Color ImageMarginGradientMiddle { get { return Theme.MenuBg; } }
        public override Color ImageMarginGradientEnd { get { return Theme.MenuBg; } }
        public override Color MenuBorder { get { return Theme.Border; } }
        public override Color MenuItemBorder { get { return Theme.Hover; } }
        public override Color MenuItemSelected { get { return Theme.Hover; } }
        public override Color SeparatorDark { get { return Theme.Border; } }
        public override Color SeparatorLight { get { return Theme.MenuBg; } }
    }

    // A dark two-line hover card: first line bold, second line muted.
    static class HoverCard
    {
        public static ToolTip Create()
        {
            var tip = new ToolTip { ShowAlways = true, InitialDelay = 700, AutoPopDelay = 8000, OwnerDraw = true };
            var title = new Font("Segoe UI Semibold", 9.75f);
            var hint = new Font("Segoe UI", 8.75f);
            int pad = Theme.Px(10), gap = Theme.Px(3);

            tip.Popup += (s, e) =>
            {
                string[] lines = (tip.GetToolTip(e.AssociatedControl) ?? "").Split('\n');
                Size a = TextRenderer.MeasureText(lines[0], title);
                Size b = lines.Length > 1 ? TextRenderer.MeasureText(lines[1], hint) : Size.Empty;
                e.ToolTipSize = new Size(Math.Max(a.Width, b.Width) + pad * 2, a.Height + (lines.Length > 1 ? b.Height + gap : 0) + pad * 2);
                try
                {
                    var hp = typeof(ToolTip).GetProperty("Handle", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (hp != null) Theme.RoundCorners((IntPtr)hp.GetValue(tip, null));
                }
                catch { }
            };

            tip.Draw += (s, e) =>
            {
                var g = e.Graphics;
                using (var bg = new SolidBrush(Theme.MenuBg)) g.FillRectangle(bg, e.Bounds);
                using (var p = new Pen(Theme.Border)) g.DrawRectangle(p, 0, 0, e.Bounds.Width - 1, e.Bounds.Height - 1);
                string[] lines = (e.ToolTipText ?? "").Split('\n');
                TextRenderer.DrawText(g, lines[0], title, new Point(pad, pad), Theme.Fg);
                if (lines.Length > 1)
                {
                    int y = pad + TextRenderer.MeasureText(lines[0], title).Height + gap;
                    TextRenderer.DrawText(g, lines[1], hint, new Point(pad, y), Theme.Muted);
                }
            };
            return tip;
        }
    }
}
