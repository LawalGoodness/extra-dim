using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Extra Dim")]
[assembly: AssemblyProduct("Extra Dim")]
[assembly: AssemblyDescription("Adjustable extra screen dimmer")]
[assembly: AssemblyCompany("Lawal Goodness")]
[assembly: AssemblyCopyright("Made by Lawal Goodness © 2026")]
[assembly: AssemblyVersion("1.0.0.0")]

namespace ExtraDim
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            bool fromStartup = Array.IndexOf(args, "--startup") >= 0;
            try { Native.SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // A modified copy never runs, even if the real one is already open.
            if (!Integrity.IsOriginal())
            {
                Application.Run(new TamperWarning(DimApp.LoadIcon(32)));
                return;
            }

            bool created;
            using (var mutex = new System.Threading.Mutex(true, "ExtraDim.SingleInstance.7c1e", out created))
            {
                if (!created)
                {
                    // Already running: ask that copy to open its settings window.
                    Native.PostMessage(Native.HWND_BROADCAST, MsgWindow.WM_SHOWME, IntPtr.Zero, IntPtr.Zero);
                    return;
                }
                Application.Run(new DimApp(fromStartup));
            }
        }
    }

    class DimApp : ApplicationContext
    {
        public const int MaxLevel = 95;
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        readonly Settings settings;
        readonly List<Overlay> overlays = new List<Overlay>();
        readonly Control sync;
        readonly MsgWindow msg;
        readonly Timer fade, keepTop, saveLater;
        readonly NotifyIcon tray;
        readonly ContextMenuStrip menu;
        readonly Bubble bubble;
        readonly Icon bigIcon, smallIcon;
        ControlPanel panel;
        ToolStripMenuItem miToggle, miLevel, miBubble, miTuck, miStartup;
        int curAlpha, targetAlpha;
        bool overlaysShown;

        public bool On { get { return settings.On; } }
        public int Level { get { return settings.Level; } }
        public bool BubbleVisible { get { return settings.Bubble; } }
        public bool AutoTuck { get { return settings.AutoTuck; } }

        public DimApp(bool fromStartup)
        {
            settings = Settings.Load();
            if (!fromStartup) settings.On = true;      // opening the app means "dim now"
            if (settings.Level < 5) settings.Level = 50;

            sync = new Control();
            sync.CreateControl();
            bigIcon = LoadIcon(32);
            smallIcon = LoadIcon(SystemInformation.SmallIconSize.Width);

            menu = BuildMenu();
            tray = new NotifyIcon { Icon = smallIcon, ContextMenuStrip = menu, Visible = true };
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) Toggle(); };

            fade = new Timer { Interval = 15 };
            fade.Tick += FadeTick;
            keepTop = new Timer { Interval = 1000 };
            keepTop.Tick += (s, e) => KeepOnTop();
            keepTop.Start();
            saveLater = new Timer { Interval = 600 };
            saveLater.Tick += (s, e) => { saveLater.Stop(); settings.Save(); };

            BuildOverlays();
            SystemEvents.DisplaySettingsChanged += OnDisplayChanged;

            bubble = new Bubble(this, menu);
            bubble.Location = InitialBubbleSpot();
            bubble.AutoTuck = settings.AutoTuck;
            if (settings.Bubble) bubble.Show();

            msg = new MsgWindow();
            msg.Hotkey += OnHotkey;
            msg.ShowRequested += () => { if (!settings.Bubble) SetBubbleVisible(true); ShowPanel(); };
            RegisterHotkeys();

            Apply();
            Changed();
        }

        // ---------- state ----------

        public void Toggle()
        {
            settings.On = !settings.On;
            if (settings.On && settings.Level == 0) settings.Level = 50;
            Apply();
            Changed();
        }

        public void SetLevel(int level)
        {
            settings.Level = Math.Max(0, Math.Min(MaxLevel, level));
            if (settings.Level > 0) settings.On = true;
            Apply();
            Changed();
        }

        public void Step(int delta)
        {
            if (!settings.On) { Toggle(); return; }
            SetLevel(settings.Level + delta);
        }

        public void SetBubbleVisible(bool visible)
        {
            settings.Bubble = visible;
            if (visible) { bubble.Show(); bubble.KeepOnScreen(); KeepOnTop(); }
            else bubble.Hide();
            Changed();
        }

        public void SetAutoTuck(bool on)
        {
            settings.AutoTuck = on;
            bubble.SetAutoTuck(on);
            Changed();
        }

        public void SaveBubbleSpot(Point p)
        {
            settings.BubbleX = p.X;
            settings.BubbleY = p.Y;
            SaveSoon();
        }

        public bool StartWithWindows
        {
            get
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                    return k != null && k.GetValue("ExtraDim") != null;
            }
            set
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (value) k.SetValue("ExtraDim", "\"" + Application.ExecutablePath + "\" --startup");
                    else k.DeleteValue("ExtraDim", false);
                }
                Changed();
            }
        }

        void Changed()
        {
            tray.Text = "Extra Dim — " + settings.Level + "% (" + (settings.On ? "on" : "off") + ")";
            bubble.Redraw();
            if (panel != null && !panel.IsDisposed && panel.Visible) panel.Sync();
            SaveSoon();
        }

        void SaveSoon() { saveLater.Stop(); saveLater.Start(); }

        // ---------- overlays ----------

        void Apply()
        {
            targetAlpha = settings.On ? (int)Math.Round(settings.Level * 255 / 100.0) : 0;
            if (targetAlpha > 0) ShowOverlays();
            fade.Start();
        }

        void FadeTick(object sender, EventArgs e)
        {
            int d = targetAlpha - curAlpha;
            int step = Math.Max(4, Math.Abs(d) / 4);
            curAlpha = Math.Abs(d) <= step ? targetAlpha : curAlpha + Math.Sign(d) * step;
            foreach (var o in overlays) o.SetAlpha(curAlpha);
            if (curAlpha == targetAlpha)
            {
                fade.Stop();
                if (curAlpha == 0) HideOverlays();
            }
        }

        void ShowOverlays()
        {
            if (overlaysShown) return;
            overlaysShown = true;
            foreach (var o in overlays) { o.SetAlpha(curAlpha); o.Show(); o.Bounds = o.Area; }
            KeepOnTop();
        }

        void HideOverlays()
        {
            if (!overlaysShown) return;
            overlaysShown = false;
            foreach (var o in overlays) o.Hide();
        }

        void BuildOverlays()
        {
            foreach (var o in overlays) o.Dispose();
            overlays.Clear();
            foreach (var screen in Screen.AllScreens)
            {
                var o = new Overlay(screen.Bounds, settings.HideFromScreenshots);
                overlays.Add(o);
                if (overlaysShown) { o.SetAlpha(curAlpha); o.Show(); o.Bounds = o.Area; }
            }
        }

        // The taskbar and other always-on-top windows can climb above us; push back up.
        void KeepOnTop()
        {
            if (overlaysShown)
                foreach (var o in overlays) Native.MakeTopmost(o.Handle);
            if (bubble != null && bubble.Visible) Native.MakeTopmost(bubble.Handle);
        }

        void OnDisplayChanged(object sender, EventArgs e)
        {
            sync.BeginInvoke((Action)(() => { BuildOverlays(); bubble.KeepOnScreen(); KeepOnTop(); }));
        }

        Point InitialBubbleSpot()
        {
            var p = new Point(settings.BubbleX, settings.BubbleY);
            var center = new Point(p.X + bubble.Width / 2, p.Y + bubble.Height / 2);
            foreach (var s in Screen.AllScreens)
                if (s.WorkingArea.Contains(center)) return p;
            var wa = Screen.PrimaryScreen.WorkingArea;
            return new Point(wa.Right - bubble.Width - 24, wa.Bottom - bubble.Height - 140);
        }

        // ---------- hotkeys ----------

        const int HkToggle = 1, HkDimmer = 2, HkDimmerPad = 3, HkBrighter = 4, HkBrighterPad = 5, HkBubble = 6;

        void RegisterHotkeys()
        {
            const uint ctrlAlt = Native.MOD_CONTROL | Native.MOD_ALT;
            Native.RegisterHotKey(msg.Handle, HkToggle, ctrlAlt | Native.MOD_NOREPEAT, (uint)Keys.D);
            Native.RegisterHotKey(msg.Handle, HkDimmer, ctrlAlt, (uint)Keys.Oemplus);
            Native.RegisterHotKey(msg.Handle, HkDimmerPad, ctrlAlt, (uint)Keys.Add);
            Native.RegisterHotKey(msg.Handle, HkBrighter, ctrlAlt, (uint)Keys.OemMinus);
            Native.RegisterHotKey(msg.Handle, HkBrighterPad, ctrlAlt, (uint)Keys.Subtract);
            Native.RegisterHotKey(msg.Handle, HkBubble, ctrlAlt | Native.MOD_NOREPEAT, (uint)Keys.B);
        }

        void OnHotkey(int id)
        {
            switch (id)
            {
                case HkToggle: Toggle(); break;
                case HkDimmer: case HkDimmerPad: Step(5); break;
                case HkBrighter: case HkBrighterPad: Step(-5); break;
                case HkBubble: SetBubbleVisible(!settings.Bubble); break;
            }
        }

        // ---------- UI ----------

        ContextMenuStrip BuildMenu()
        {
            var m = new ContextMenuStrip();
            miToggle = new ToolStripMenuItem("Turn off", Theme.Icon("moon"), (s, e) => Toggle());
            miToggle.Font = new Font(miToggle.Font, FontStyle.Bold);
            miLevel = new ToolStripMenuItem("Dim level", Theme.Icon("level"));
            foreach (int v in new[] { 10, 20, 30, 40, 50, 60, 70, 80, 90, 95 })
            {
                int lv = v;
                var it = new ToolStripMenuItem(v + "%", null, (s, e) => SetLevel(lv));
                it.Tag = v;
                miLevel.DropDownItems.Add(it);
            }
            miBubble = new ToolStripMenuItem("Show floating bubble", null, (s, e) => SetBubbleVisible(!settings.Bubble));
            miTuck = new ToolStripMenuItem("Tuck bubble into edge when idle", null, (s, e) => SetAutoTuck(!settings.AutoTuck));
            miStartup = new ToolStripMenuItem("Start with Windows", null, (s, e) => StartWithWindows = !StartWithWindows);
            Theme.MakeSwitch(miBubble);
            Theme.MakeSwitch(miTuck);
            Theme.MakeSwitch(miStartup);

            m.Items.Add(miToggle);
            m.Items.Add(miLevel);
            m.Items.Add(new ToolStripMenuItem("Settings…", Theme.Icon("settings"), (s, e) => ShowPanel()));
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(miBubble);
            m.Items.Add(miTuck);
            m.Items.Add(miStartup);
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(new ToolStripMenuItem("Exit", Theme.Icon("exit"), (s, e) => Exit()));

            m.Opening += (s, e) =>
            {
                miToggle.Text = settings.On ? "Turn off" : "Turn on";
                foreach (ToolStripMenuItem it in miLevel.DropDownItems) it.Checked = (int)it.Tag == settings.Level;
                miBubble.Checked = settings.Bubble;
                miTuck.Checked = settings.AutoTuck;
                miStartup.Checked = StartWithWindows;
            };
            Theme.StyleMenu(m);

            // The bubble never takes focus, so Windows won't auto-close the menu on an outside click.
            // Watch the mouse ourselves while it is open.
            var watch = new Timer { Interval = 40 };
            watch.Tick += (s, e) =>
            {
                if (!Native.AnyMouseButtonDown()) return;
                var p = Cursor.Position;
                if (m.Bounds.Contains(p)) return;
                if (miLevel.DropDown.Visible && miLevel.DropDown.Bounds.Contains(p)) return;
                m.Close(ToolStripDropDownCloseReason.AppClicked);
            };
            m.Opened += (s, e) => watch.Start();
            m.Closed += (s, e) => watch.Stop();
            return m;
        }

        public void ShowPanel()
        {
            if (panel == null || panel.IsDisposed) panel = new ControlPanel(this, bigIcon);
            panel.Sync();
            panel.Show();
            if (panel.WindowState == FormWindowState.Minimized) panel.WindowState = FormWindowState.Normal;
            panel.Activate();
        }

        public void Exit()
        {
            fade.Stop();
            keepTop.Stop();
            saveLater.Stop();
            settings.Save();
            SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
            for (int id = HkToggle; id <= HkBubble; id++) Native.UnregisterHotKey(msg.Handle, id);
            msg.DestroyHandle();
            tray.Visible = false;
            tray.Dispose();
            foreach (var o in overlays) o.Dispose();
            bubble.Dispose();
            if (panel != null) panel.Dispose();
            ExitThread();
        }

        public static Icon LoadIcon(int size)
        {
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("ExtraDim.icon.ico"))
                return s == null ? SystemIcons.Application : new Icon(s, size, size);
        }
    }

    // One click-through black layer per monitor.
    class Overlay : Form
    {
        public readonly Rectangle Area;
        readonly bool hideFromScreenshots;
        byte alpha;

        public Overlay(Rectangle area, bool hideFromScreenshots)
        {
            // One pixel short of the full monitor: a topmost window covering the whole screen makes
            // Windows think a full-screen app is running and it stops revealing an auto-hide taskbar.
            Area = new Rectangle(area.X, area.Y, area.Width, area.Height - 1);
            this.hideFromScreenshots = hideFromScreenshots;
            Text = "Extra Dim Overlay";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.Black;
            TopMost = true;
            Bounds = Area;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT | Native.WS_EX_TOOLWINDOW
                            | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOPMOST;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.SetLayeredWindowAttributes(Handle, 0, alpha, Native.LWA_ALPHA);
            // Keep screenshots and screen-shares at normal brightness (Windows 10 2004+).
            if (hideFromScreenshots)
                try { Native.SetWindowDisplayAffinity(Handle, Native.WDA_EXCLUDEFROMCAPTURE); } catch { }
        }

        public void SetAlpha(int a)
        {
            alpha = (byte)Math.Max(0, Math.Min(255, a));
            if (IsHandleCreated) Native.SetLayeredWindowAttributes(Handle, 0, alpha, Native.LWA_ALPHA);
        }

        protected override void OnPaintBackground(PaintEventArgs e) { e.Graphics.Clear(Color.Black); }
    }

    // The floating round button: click = on/off, drag = move, wheel = level, right-click = menu.
    // Fades out when idle; optionally tucks half into the nearest screen edge until clicked.
    class Bubble : Form
    {
        const int FadeAfterMs = 3000, TuckAfterMs = 8000, FlashMs = 1200;
        const float Awake = 1f, Resting = 0.88f, Faded = 0.35f, TuckedOpacity = 0.30f;

        readonly DimApp app;
        readonly ContextMenuStrip menu;
        readonly ToolTip tip;
        readonly Timer anim;
        readonly int size;
        Bitmap face;
        bool hover, down, dragging, menuOpen, tucked;
        Point downCursor, downLocation, home, slideTarget;
        bool sliding;
        int lastActive = Environment.TickCount;
        float opacity = Resting;
        byte shownAlpha;

        public bool AutoTuck;

        public Bubble(DimApp app, ContextMenuStrip menu)
        {
            this.app = app;
            this.menu = menu;
            size = (int)Math.Round(60 * Native.Dpi / 96f);
            Text = "Extra Dim";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            Size = new Size(size, size);
            tip = HoverCard.Create();
            menu.Opened += (s, e) => { menuOpen = true; };
            menu.Closed += (s, e) => { menuOpen = false; Wake(); };
            anim = new Timer { Interval = 16 };
            anim.Tick += Animate;
            anim.Start();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOPMOST;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Redraw();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { anim.Dispose(); if (face != null) face.Dispose(); }
            base.Dispose(disposing);
        }

        // Re-render the bubble's face (state, level or hover changed) and brighten briefly.
        public void Redraw()
        {
            UpdateTip();
            if (face != null) face.Dispose();
            face = Render();
            Wake();
            Present(true);
        }

        void Wake() { lastActive = Environment.TickCount; }

        void UpdateTip()
        {
            string title = "Extra Dim  \u00B7  " + app.Level + "%  \u00B7  " + (app.On ? "On" : "Off");
            string hint = tucked ? "Click to bring it out  \u00B7  Right-click for menu"
                                 : "Click on/off  \u00B7  Scroll to adjust  \u00B7  Drag to move  \u00B7  Right-click for menu";
            string text = title + "\n" + hint;
            if (text == tip.GetToolTip(this)) return;
            // Changing the text while the card is showing skips our size calculation and clips it.
            tip.Hide(this);
            tip.SetToolTip(this, text);
        }

        void Present(bool force)
        {
            if (!IsHandleCreated || face == null) return;
            byte a = (byte)Math.Round(255 * Math.Max(0f, Math.Min(1f, opacity)));
            if (!force && a == shownAlpha) return;
            shownAlpha = a;
            Native.ApplyBitmap(Handle, face, Location, a);
        }

        void Animate(object sender, EventArgs e)
        {
            if (!Visible) return;
            bool active = hover || down || menuOpen;
            if (active) lastActive = Environment.TickCount;
            int idle = Environment.TickCount - lastActive;

            if (AutoTuck && !tucked && !active && !sliding && idle > TuckAfterMs) Tuck();

            float target;
            if (active) target = Awake;
            else if (idle < FlashMs) target = Awake;
            else if (tucked) target = TuckedOpacity;
            else target = idle > FadeAfterMs ? Faded : Resting;

            // Brighten quickly, fade out slowly.
            float rate = target > opacity ? 0.25f : 0.06f;
            opacity += (target - opacity) * rate;
            if (Math.Abs(target - opacity) < 0.005f) opacity = target;

            bool moved = false;
            if (sliding)
            {
                int dx = slideTarget.X - Left, dy = slideTarget.Y - Top;
                if (Math.Abs(dx) <= 1 && Math.Abs(dy) <= 1) { Location = slideTarget; sliding = false; }
                else Location = new Point(Left + Step(dx), Top + Step(dy));
                moved = true;
            }
            Present(moved);
        }

        void SlideTo(Point p) { slideTarget = p; sliding = true; }

        // Ease toward the target, but always move at least 1px so the slide can't stall short of it
        // (a stalled slide left "sliding" stuck on and the bubble never tucked again).
        static int Step(int d)
        {
            if (d == 0) return 0;
            return Math.Sign(d) * Math.Max(1, (int)Math.Round(Math.Abs(d) * 0.22));
        }

        void Tuck()
        {
            home = Location;
            var wa = Screen.FromPoint(new Point(Left + Width / 2, Top + Height / 2)).WorkingArea;
            bool left = Left + Width / 2 < wa.Left + wa.Width / 2;
            int x = left ? wa.Left - (int)(size * 0.55) : wa.Right - (int)(size * 0.45);
            int y = Math.Max(wa.Top, Math.Min(Top, wa.Bottom - Height));
            tucked = true;
            UpdateTip();
            SlideTo(new Point(x, y));
        }

        void Untuck()
        {
            tucked = false;
            Wake();
            SlideTo(home);
        }

        public void SetAutoTuck(bool on)
        {
            AutoTuck = on;
            if (!on && tucked) Untuck();
            Wake();
        }

        Bitmap Render()
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.Clear(Color.Transparent);

                float pad = size * 0.08f;
                var disc = new RectangleF(pad, pad, size - 2 * pad, size - 2 * pad);
                float cx = size / 2f, cy = size / 2f;

                // drop shadow
                for (int i = 3; i >= 1; i--)
                {
                    var sh = disc;
                    sh.Inflate(i * size * 0.018f, i * size * 0.018f);
                    sh.Offset(0, size * 0.025f);
                    using (var b = new SolidBrush(Color.FromArgb(28, 0, 0, 0))) g.FillEllipse(b, sh);
                }

                bool on = app.On;
                using (var bg = new LinearGradientBrush(disc, on ? Art.OnTop : Art.OffTop, on ? Art.OnBottom : Art.OffBottom, 65f))
                    g.FillEllipse(bg, disc);
                using (var hl = new LinearGradientBrush(new RectangleF(disc.X, disc.Y, disc.Width, disc.Height * 0.55f),
                    Color.FromArgb(50, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f))
                    g.FillEllipse(hl, disc.X + disc.Width * 0.12f, disc.Y + disc.Height * 0.03f, disc.Width * 0.76f, disc.Height * 0.5f);

                float w = disc.Width;
                Art.Ring(g, cx, cy, w * 0.40f, Math.Max(2f, w * 0.07f), app.Level / 100f,
                    Color.FromArgb(50, 255, 255, 255), on ? Art.MoonDeep : Color.FromArgb(150, 170, 176, 188));

                if ((hover || dragging) && !tucked)
                {
                    using (var f = new Font("Segoe UI Semibold", w * 0.23f, FontStyle.Bold, GraphicsUnit.Pixel))
                    using (var b = new SolidBrush(on ? Art.MoonLight : Color.FromArgb(205, 210, 220)))
                    using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                        g.DrawString(app.Level + "%", f, b, new RectangleF(0, size * 0.01f, size, size), sf);
                }
                else
                {
                    float R = w * 0.22f;
                    float mx = cx - w * 0.02f, my = cy + w * 0.01f;
                    if (on)
                    {
                        Art.Glow(g, mx, my, R * 1.7f, Art.MoonDeep, 60);
                        Art.FillMoon(g, mx, my, R, Art.MoonLight, Art.MoonDeep);
                        Art.Sparkle(g, cx + w * 0.10f, cy - w * 0.10f, w * 0.065f, Art.MoonLight);
                    }
                    else
                    {
                        Art.FillMoon(g, mx, my, R, Color.FromArgb(190, 196, 208), Color.FromArgb(130, 136, 150));
                    }
                }
            }
            return bmp;
        }

        public void KeepOnScreen()
        {
            if (tucked || sliding) { tucked = false; sliding = false; Location = home; }
            var wa = Screen.FromPoint(new Point(Left + Width / 2, Top + Height / 2)).WorkingArea;
            int x = Math.Max(wa.Left, Math.Min(Left, wa.Right - Width));
            int y = Math.Max(wa.Top, Math.Min(Top, wa.Bottom - Height));
            if (x != Left || y != Top) Location = new Point(x, y);
            Wake();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            down = true;
            dragging = false;
            downCursor = Cursor.Position;
            downLocation = Location;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!down) return;
            var p = Cursor.Position;
            int dx = p.X - downCursor.X, dy = p.Y - downCursor.Y;
            if (!dragging && dx * dx + dy * dy > 36)
            {
                dragging = true;
                tucked = false;
                sliding = false;
                Redraw();
            }
            if (dragging) Location = new Point(downLocation.X + dx, downLocation.Y + dy);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left && down)
            {
                down = false;
                if (dragging)
                {
                    dragging = false;
                    KeepOnScreen();
                    app.SaveBubbleSpot(Location);
                    Redraw();
                }
                else if (tucked) { Untuck(); Redraw(); }   // first tap only brings it out
                else app.Toggle();
            }
            else if (e.Button == MouseButtons.Right)
            {
                Native.SetForegroundWindow(Handle);
                menu.Show(Cursor.Position);
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            app.Step(e.Delta > 0 ? 5 : -5);
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Redraw(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = false; Redraw(); }
    }

    class ControlPanel : Form
    {
        static readonly Color Bg = Color.FromArgb(0x17, 0x15, 0x2B);
        static readonly Color Card = Color.FromArgb(0x24, 0x20, 0x44);
        static readonly Color Fg = Color.FromArgb(0xEC, 0xE8, 0xFF);
        static readonly Color Muted = Color.FromArgb(0x9C, 0x96, 0xC4);

        readonly DimApp app;
        readonly Label levelLabel;
        readonly TrackBar bar;
        readonly Button toggleBtn;
        readonly CheckBox bubbleChk, startupChk, tuckChk;
        bool syncing;

        public ControlPanel(DimApp app, Icon icon)
        {
            this.app = app;
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = "Extra Dim";
            Icon = icon;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(380, 374);
            BackColor = Bg;
            ForeColor = Fg;
            Font = new Font("Segoe UI", 9.75f);

            var title = new Label { Text = "Extra Dim", Font = new Font("Segoe UI Semibold", 17f), AutoSize = true, Location = new Point(18, 14) };
            var sub = new Label { Text = "Darker than your lowest brightness.", ForeColor = Muted, AutoSize = true, Location = new Point(21, 50) };

            levelLabel = new Label { Font = new Font("Segoe UI Semibold", 11f), AutoSize = true, Location = new Point(20, 86) };
            bar = new TrackBar
            {
                Minimum = 0, Maximum = DimApp.MaxLevel, TickFrequency = 10, SmallChange = 1, LargeChange = 5,
                Location = new Point(14, 112), Size = new Size(352, 45), BackColor = Bg
            };
            bar.ValueChanged += (s, e) => { if (!syncing) app.SetLevel(bar.Value); };
            var ends = new Label { Text = "brighter" + new string(' ', 70) + "darker", ForeColor = Muted, AutoSize = true,
                Font = new Font("Segoe UI", 8.25f), Location = new Point(20, 160) };

            toggleBtn = new Button { Location = new Point(20, 188), Size = new Size(340, 42), FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI Semibold", 10.5f), Cursor = Cursors.Hand };
            toggleBtn.FlatAppearance.BorderSize = 0;
            toggleBtn.Click += (s, e) => app.Toggle();

            bubbleChk = new CheckBox { Text = "Show floating bubble", AutoSize = true, Location = new Point(22, 242) };
            bubbleChk.CheckedChanged += (s, e) => { if (!syncing) app.SetBubbleVisible(bubbleChk.Checked); };
            startupChk = new CheckBox { Text = "Start with Windows", AutoSize = true, Location = new Point(200, 242) };
            startupChk.CheckedChanged += (s, e) => { if (!syncing) app.StartWithWindows = startupChk.Checked; };

            tuckChk = new CheckBox { Text = "Tuck bubble into the screen edge when idle", AutoSize = true, Location = new Point(22, 268) };
            tuckChk.CheckedChanged += (s, e) => { if (!syncing) app.SetAutoTuck(tuckChk.Checked); };

            var keys = new Label
            {
                Text = "Ctrl+Alt+D  on/off     Ctrl+Alt+ =  /  −  darker / brighter\nCtrl+Alt+B  show/hide bubble",
                ForeColor = Muted, Font = new Font("Segoe UI", 8.25f), Location = new Point(20, 304), Size = new Size(345, 40)
            };

            var credit = new CreditLabel { Location = new Point(110, 352), Size = new Size(256, 18) };

            Controls.AddRange(new Control[] { title, sub, levelLabel, bar, ends, toggleBtn, bubbleChk, startupChk, tuckChk, keys, credit });
            ResumeLayout(false);
            PerformLayout();
        }

        public void Sync()
        {
            syncing = true;
            levelLabel.Text = "Dim level:  " + app.Level + "%";
            bar.Value = Math.Max(bar.Minimum, Math.Min(bar.Maximum, app.Level));
            toggleBtn.Text = app.On ? "Turn off" : "Turn on";
            toggleBtn.BackColor = app.On ? Art.MoonDeep : Card;
            toggleBtn.ForeColor = app.On ? Color.FromArgb(0x2A, 0x1A, 0x05) : Fg;
            bubbleChk.Checked = app.BubbleVisible;
            tuckChk.Checked = app.AutoTuck;
            startupChk.Checked = app.StartWithWindows;
            syncing = false;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Closing the window just hides it; the dimmer keeps running in the tray.
            if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }
            base.OnFormClosing(e);
        }
    }

    // "Made by Lawal Goodness": hovering shows the GitHub name, a longer hover or a click shows the
    // link, and clicking the link opens it.
    class CreditLabel : Label
    {
        const string Credit = "Made by Lawal Goodness";
        const string GitHubName = "@LawalGoodness on GitHub";
        const string Link = "github.com/LawalGoodness";
        static readonly Color Quiet = Color.FromArgb(0x5E, 0x58, 0x88);
        static readonly Color Lit = Color.FromArgb(0x9C, 0x96, 0xC4);
        readonly Font plain = new Font("Segoe UI", 7.5f);
        readonly Font underlined = new Font("Segoe UI", 7.5f, FontStyle.Underline);
        readonly Timer linger = new Timer { Interval = 1400 };
        bool showingLink;

        public CreditLabel()
        {
            Text = Credit;
            ForeColor = Quiet;
            Font = plain;
            TextAlign = ContentAlignment.MiddleRight;
            linger.Tick += (s, e) => { linger.Stop(); ShowLink(); };
        }

        void ShowLink()
        {
            showingLink = true;
            Text = Link;
            ForeColor = Art.MoonDeep;
            Font = underlined;
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            Text = GitHubName;
            ForeColor = Lit;
            linger.Start();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            linger.Stop();
            showingLink = false;
            Text = Credit;
            ForeColor = Quiet;
            Font = plain;
            Cursor = Cursors.Default;
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            if (!showingLink) { linger.Stop(); ShowLink(); return; }
            try { System.Diagnostics.Process.Start("https://" + Link); } catch { }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) linger.Dispose();
            base.Dispose(disposing);
        }
    }

    class MsgWindow : NativeWindow
    {
        public static readonly uint WM_SHOWME = Native.RegisterWindowMessage("ExtraDim.ShowMe.7c1e");
        public event Action<int> Hotkey;
        public event Action ShowRequested;

        public MsgWindow() { CreateHandle(new CreateParams { Caption = "ExtraDim.Msg" }); }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY) { if (Hotkey != null) Hotkey(m.WParam.ToInt32()); }
            else if (WM_SHOWME != 0 && (uint)m.Msg == WM_SHOWME) { if (ShowRequested != null) ShowRequested(); }
            base.WndProc(ref m);
        }
    }

    class Settings
    {
        public int Level = 50;
        public bool On = true, Bubble = true, HideFromScreenshots = true, AutoTuck = false;
        public int BubbleX = int.MinValue / 2, BubbleY = int.MinValue / 2;

        static string FilePath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ExtraDim", "settings.ini"); }
        }

        public static Settings Load()
        {
            var s = new Settings();
            try
            {
                if (!File.Exists(FilePath)) return s;
                foreach (var line in File.ReadAllLines(FilePath))
                {
                    int eq = line.IndexOf('=');
                    if (eq < 0) continue;
                    string k = line.Substring(0, eq).Trim(), v = line.Substring(eq + 1).Trim();
                    int n;
                    bool isNum = int.TryParse(v, out n);
                    switch (k)
                    {
                        case "Level": if (isNum) s.Level = Math.Max(0, Math.Min(DimApp.MaxLevel, n)); break;
                        case "On": s.On = v == "1"; break;
                        case "Bubble": s.Bubble = v == "1"; break;
                        case "HideFromScreenshots": s.HideFromScreenshots = v == "1"; break;
                        case "AutoTuck": s.AutoTuck = v == "1"; break;
                        case "BubbleX": if (isNum) s.BubbleX = n; break;
                        case "BubbleY": if (isNum) s.BubbleY = n; break;
                    }
                }
            }
            catch { }
            return s;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllLines(FilePath, new[]
                {
                    "Level=" + Level, "On=" + (On ? 1 : 0), "Bubble=" + (Bubble ? 1 : 0),
                    "HideFromScreenshots=" + (HideFromScreenshots ? 1 : 0), "AutoTuck=" + (AutoTuck ? 1 : 0),
                    "BubbleX=" + BubbleX, "BubbleY=" + BubbleY
                });
            }
            catch { }
        }
    }

    static class Native
    {
        public const int WS_EX_LAYERED = 0x80000, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80,
                         WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOPMOST = 0x8;
        public const int WM_HOTKEY = 0x0312;
        public const uint LWA_ALPHA = 2, WDA_EXCLUDEFROMCAPTURE = 0x11;
        public const uint MOD_ALT = 1, MOD_CONTROL = 2, MOD_NOREPEAT = 0x4000;
        const uint SWP_NOSIZE = 1, SWP_NOMOVE = 2, SWP_NOACTIVATE = 0x10;
        public static readonly IntPtr HWND_BROADCAST = new IntPtr(0xffff);
        static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        static float dpi;
        public static float Dpi
        {
            get
            {
                if (dpi == 0) using (var g = Graphics.FromHwnd(IntPtr.Zero)) dpi = g.DpiX;
                return dpi;
            }
        }

        public static void MakeTopmost(IntPtr h)
        {
            SetWindowPos(h, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        // Paint a per-pixel-alpha bitmap onto a layered window (smooth round edges).
        public static void ApplyBitmap(IntPtr hwnd, Bitmap bmp, Point location, byte opacity)
        {
            IntPtr screenDc = GetDC(IntPtr.Zero);
            IntPtr memDc = CreateCompatibleDC(screenDc);
            IntPtr hBmp = bmp.GetHbitmap(Color.FromArgb(0));
            IntPtr old = SelectObject(memDc, hBmp);
            try
            {
                var size = new SIZE { cx = bmp.Width, cy = bmp.Height };
                var src = new POINT();
                var dst = new POINT { x = location.X, y = location.Y };
                var blend = new BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = opacity, AlphaFormat = 1 };
                UpdateLayeredWindow(hwnd, screenDc, ref dst, ref size, memDc, ref src, 0, ref blend, 2);
            }
            finally
            {
                SelectObject(memDc, old);
                DeleteObject(hBmp);
                DeleteDC(memDc);
                ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        [StructLayout(LayoutKind.Sequential)] struct POINT { public int x, y; }
        [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx, cy; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);

        public static bool AnyMouseButtonDown()
        {
            return (GetAsyncKeyState(0x01) & 0x8000) != 0 || (GetAsyncKeyState(0x02) & 0x8000) != 0
                || (GetAsyncKeyState(0x04) & 0x8000) != 0;
        }
        [DllImport("user32.dll")] public static extern bool SetLayeredWindowAttributes(IntPtr h, uint key, byte alpha, uint flags);
        [DllImport("user32.dll")] public static extern bool SetWindowDisplayAffinity(IntPtr h, uint affinity);
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr h, int id);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern uint RegisterWindowMessage(string name);
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("user32.dll")]
        static extern bool UpdateLayeredWindow(IntPtr h, IntPtr dstDc, ref POINT dst, ref SIZE size, IntPtr srcDc,
            ref POINT src, int key, ref BLENDFUNCTION blend, int flags);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    }
}
