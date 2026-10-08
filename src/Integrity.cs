using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

namespace ExtraDim
{
    // Checks that this exe is byte-for-byte the copy that was signed at build time.
    // Catches infected, patched or corrupted files. It cannot stop someone who strips this check out,
    // which is why the warning points people to the one official download.
    static class Integrity
    {
        const string Magic = "EXDMSIG1";

        public static bool IsOriginal()
        {
            try
            {
                byte[] all = File.ReadAllBytes(Application.ExecutablePath);
                int tail = 4 + Magic.Length;
                if (all.Length < tail + 64) return false;
                if (Encoding.ASCII.GetString(all, all.Length - Magic.Length, Magic.Length) != Magic) return false;

                int sigLen = BitConverter.ToInt32(all, all.Length - tail);
                int dataLen = all.Length - tail - sigLen;
                if (sigLen < 64 || sigLen > 1024 || dataLen <= 0) return false;

                var sig = new byte[sigLen];
                Buffer.BlockCopy(all, dataLen, sig, 0, sigLen);
                var data = new byte[dataLen];
                Buffer.BlockCopy(all, 0, data, 0, dataLen);

                string publicKey;
                using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("ExtraDim.publickey.xml"))
                {
                    if (s == null) return false;
                    using (var r = new StreamReader(s)) publicKey = r.ReadToEnd();
                }
                using (var rsa = new RSACryptoServiceProvider())
                {
                    rsa.PersistKeyInCsp = false;
                    rsa.FromXmlString(publicKey);
                    return rsa.VerifyData(data, new SHA256CryptoServiceProvider(), sig);
                }
            }
            catch { return false; }
        }
    }

    class TamperWarning : Form
    {
        static readonly Color Bg = Color.FromArgb(0x17, 0x15, 0x2B);
        static readonly Color Fg = Color.FromArgb(0xEC, 0xE8, 0xFF);
        static readonly Color Muted = Color.FromArgb(0x9C, 0x96, 0xC4);
        static readonly Color Alarm = Color.FromArgb(0xFF, 0x6B, 0x6B);

        public TamperWarning(Icon icon)
        {
            SuspendLayout();
            AutoScaleMode = AutoScaleMode.None;   // laid out by hand below, scaled for the screen's DPI
            Text = "Extra Dim — warning";
            Icon = icon;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            ClientSize = new Size(Theme.Px(440), Theme.Px(340));
            BackColor = Bg;
            ForeColor = Fg;
            Font = new Font("Segoe UI", 9.75f);

            var badge = new Panel { Location = new Point(Theme.Px(22), Theme.Px(22)), Size = new Size(Theme.Px(48), Theme.Px(48)), BackColor = Bg };
            badge.Paint += (s, e) => DrawShield(e.Graphics, badge.ClientRectangle);

            var title = new Label
            {
                Text = "This copy of Extra Dim isn't safe to run",
                Font = new Font("Segoe UI Semibold", 13f), ForeColor = Alarm,
                Location = new Point(Theme.Px(84), Theme.Px(22)), Size = new Size(Theme.Px(340), Theme.Px(52))
            };
            var body = new Label
            {
                Text = "It may have been infected by a virus, damaged, or modified by someone else, so it "
                     + "no longer matches the original made by Lawal Goodness. To keep you safe, it won't run.\n\n"
                     + "Delete this copy and download the original from the official page below.",
                ForeColor = Muted, Location = new Point(Theme.Px(22), Theme.Px(88)), Size = new Size(Theme.Px(400), Theme.Px(120))
            };
            var get = new Button
            {
                Text = "Get the original", Location = new Point(Theme.Px(22), Theme.Px(228)), Size = new Size(Theme.Px(250), Theme.Px(44)),
                FlatStyle = FlatStyle.Flat, BackColor = Art.MoonDeep, ForeColor = Color.FromArgb(0x2A, 0x1A, 0x05),
                Font = new Font("Segoe UI Semibold", 10.5f), Cursor = Cursors.Hand
            };
            get.FlatAppearance.BorderSize = 0;
            get.Click += (s, e) => { try { System.Diagnostics.Process.Start(BuildInfo.DownloadUrl); } catch { } };
            var close = new Button
            {
                Text = "Close", Location = new Point(Theme.Px(284), Theme.Px(228)), Size = new Size(Theme.Px(134), Theme.Px(44)),
                FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0x24, 0x20, 0x44), ForeColor = Fg,
                Font = new Font("Segoe UI Semibold", 10.5f), Cursor = Cursors.Hand
            };
            close.FlatAppearance.BorderSize = 0;
            close.Click += (s, e) => Close();

            var tip = new Label
            {
                Text = "If this keeps happening, your computer may have a virus that is infecting apps. "
                     + "Run a full scan with Windows Security.",
                ForeColor = Color.FromArgb(0x6E, 0x68, 0x98), Font = new Font("Segoe UI", 7.5f),
                Location = new Point(Theme.Px(22), Theme.Px(290)), Size = new Size(Theme.Px(400), Theme.Px(36))
            };

            Controls.AddRange(new Control[] { badge, title, body, get, close, tip });
            AcceptButton = get;
            CancelButton = close;
            ResumeLayout(false);
            PerformLayout();
        }

        static void DrawShield(Graphics g, Rectangle r)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float w = r.Width, h = r.Height;
            using (var p = new GraphicsPath())
            {
                p.AddLine(w * 0.5f, h * 0.04f, w * 0.92f, h * 0.2f);
                p.AddBezier(w * 0.92f, h * 0.2f, w * 0.92f, h * 0.62f, w * 0.72f, h * 0.84f, w * 0.5f, h * 0.97f);
                p.AddBezier(w * 0.5f, h * 0.97f, w * 0.28f, h * 0.84f, w * 0.08f, h * 0.62f, w * 0.08f, h * 0.2f);
                p.CloseFigure();
                using (var b = new LinearGradientBrush(r, Color.FromArgb(0xFF, 0x8E, 0x8E), Color.FromArgb(0xC9, 0x3A, 0x4B), 90f))
                    g.FillPath(b, p);
            }
            using (var pen = new Pen(Color.White, w * 0.09f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawLine(pen, w * 0.5f, h * 0.28f, w * 0.5f, h * 0.58f);
            using (var b = new SolidBrush(Color.White))
                g.FillEllipse(b, w * 0.455f, h * 0.7f, w * 0.09f, w * 0.09f);
        }
    }
}
