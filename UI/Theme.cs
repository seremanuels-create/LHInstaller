using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace LHInstaller
{
    // Tavolozza, caratteri e icone: un posto solo, cosi' tutte le finestre si
    // somigliano. I colori sono quelli delle impostazioni di Windows 11 in tema
    // chiaro: l'app deve sembrare parte del sistema, non un corpo estraneo.
    public static class Theme
    {
        public static readonly Color WindowBg = Color.FromArgb(243, 243, 243);
        public static readonly Color CardBg = Color.White;
        public static readonly Color Border = Color.FromArgb(229, 229, 229);
        public static readonly Color BorderStrong = Color.FromArgb(204, 204, 204);
        public static readonly Color HeaderBg = Color.FromArgb(250, 250, 250);
        public static readonly Color Text = Color.FromArgb(27, 27, 27);
        public static readonly Color TextSecondary = Color.FromArgb(96, 96, 96);
        public static readonly Color TextDisabled = Color.FromArgb(160, 160, 160);
        public static readonly Color HoverBg = Color.FromArgb(234, 234, 234);
        public static readonly Color PressedBg = Color.FromArgb(222, 222, 222);
        public static readonly Color SelectedBg = Color.FromArgb(225, 237, 251);
        public static readonly Color Accent = Color.FromArgb(0, 103, 192);
        public static readonly Color AccentHover = Color.FromArgb(25, 117, 197);
        public static readonly Color AccentPressed = Color.FromArgb(49, 131, 202);
        public static readonly Color Success = Color.FromArgb(15, 123, 15);
        public static readonly Color Warning = Color.FromArgb(157, 93, 0);
        public static readonly Color Danger = Color.FromArgb(196, 43, 28);

        // La console e' scura di proposito: e' il pezzo che "fa terminale". Ma non nera
        // pece, e incorniciata come gli altri riquadri, cosi' non stona.
        public static readonly Color ConsoleBg = Color.FromArgb(30, 30, 30);
        public static readonly Color ConsoleHeaderBg = Color.FromArgb(43, 43, 43);
        public static readonly Color ConsoleHeaderText = Color.FromArgb(220, 220, 220);
        public static readonly Color ConsoleText = Color.FromArgb(204, 204, 204);
        public static readonly Color ConsoleDim = Color.FromArgb(128, 128, 128);
        public static readonly Color ConsoleInfo = Color.FromArgb(86, 156, 214);
        public static readonly Color ConsoleGood = Color.FromArgb(106, 190, 120);
        public static readonly Color ConsoleWarn = Color.FromArgb(220, 190, 100);
        public static readonly Color ConsoleError = Color.FromArgb(241, 96, 96);

        public static readonly Font UI = new Font("Segoe UI", 9f);
        public static readonly Font UISmall = new Font("Segoe UI", 8.25f);
        public static readonly Font UIBold = new Font("Segoe UI", 9f, FontStyle.Bold);
        public static readonly Font Header = SemiBold(9.5f);
        public static readonly Font Title = SemiBold(13f);
        public static readonly Font Mono = PickMono();

        private static Font SemiBold(float size)
        {
            // "Segoe UI Semibold" e' una famiglia a parte; se manca, il grassetto normale.
            try { return new Font("Segoe UI Semibold", size, FontStyle.Regular); }
            catch { return new Font("Segoe UI", size, FontStyle.Bold); }
        }

        private static Font PickMono()
        {
            foreach (string name in new string[] { "Cascadia Mono", "Consolas", "Lucida Console" })
            {
                if (FontExists(name)) return new Font(name, 9.25f);
            }
            return new Font(FontFamily.GenericMonospace, 9.25f);
        }

        public static bool FontExists(string name)
        {
            using (InstalledFontCollection c = new InstalledFontCollection())
            {
                foreach (FontFamily f in c.Families)
                    if (string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        // ---------- aspetto nativo per liste e alberi ----------

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hWnd, string appName, string idList);

        // Con il tema "Explorer" ListView e TreeView prendono l'aspetto di Esplora file:
        // selezione tenue, passaggio del mouse evidenziato, intestazioni pulite.
        public static void ExplorerStyle(Control c)
        {
            try { SetWindowTheme(c.Handle, "Explorer", null); }
            catch { }
        }

        // ---------- pezzi di interfaccia ricorrenti ----------

        // Un riquadro bianco con bordo sottile: la "scheda" su cui poggia ogni zona.
        public static Panel Card()
        {
            Panel outer = new Panel();
            outer.BackColor = Border;
            outer.Padding = new Padding(1);
            Panel inner = new Panel();
            inner.BackColor = CardBg;
            inner.Dock = DockStyle.Fill;
            outer.Controls.Add(inner);
            return outer;
        }

        public static Panel CardBody(Panel card)
        {
            return (Panel)card.Controls[0];
        }

        // Intestazione di una scheda: titolo a sinistra, eventuale sottotitolo in grigio.
        public static Panel CardHeader(string title, out Label titleLabel, out Label subLabel)
        {
            Panel p = new Panel();
            p.Dock = DockStyle.Top;
            p.Height = 34;
            p.BackColor = HeaderBg;
            p.Padding = new Padding(10, 0, 8, 0);

            Label t = new Label();
            t.Text = title;
            t.Font = Header;
            t.ForeColor = Text;
            t.AutoSize = true;
            t.Location = new Point(10, 9);
            p.Controls.Add(t);

            Label s = new Label();
            s.Font = UI;
            s.ForeColor = TextSecondary;
            s.AutoSize = true;
            s.Location = new Point(t.Right + 6, 10);
            p.Controls.Add(s);

            // Riga di separazione in fondo all'intestazione.
            Panel line = new Panel();
            line.Dock = DockStyle.Bottom;
            line.Height = 1;
            line.BackColor = Border;
            p.Controls.Add(line);

            titleLabel = t;
            subLabel = s;
            return p;
        }

        public static Button FlatButton(string text, string glyph)
        {
            Button b = new Button();
            b.Text = text;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 1;
            b.FlatAppearance.BorderColor = BorderStrong;
            b.FlatAppearance.MouseOverBackColor = HoverBg;
            b.FlatAppearance.MouseDownBackColor = PressedBg;
            b.BackColor = CardBg;
            b.ForeColor = Text;
            b.Font = UI;
            b.UseVisualStyleBackColor = false;
            b.Height = 30;
            b.Cursor = Cursors.Hand;
            if (glyph != null)
            {
                b.Image = Icons.Glyph(glyph, 16, Text);
                b.ImageAlign = ContentAlignment.MiddleLeft;
                b.TextImageRelation = TextImageRelation.ImageBeforeText;
                b.TextAlign = ContentAlignment.MiddleLeft;
                b.Padding = new Padding(8, 0, 8, 0);
            }
            return b;
        }

        // Il pulsante dell'azione principale: uno solo per finestra, in colore accento.
        public static Button PrimaryButton(string text, string glyph)
        {
            Button b = new Button();
            b.Text = text;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = AccentHover;
            b.FlatAppearance.MouseDownBackColor = AccentPressed;
            b.BackColor = Accent;
            b.ForeColor = Color.White;
            b.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            b.UseVisualStyleBackColor = false;
            b.Cursor = Cursors.Hand;
            if (glyph != null)
            {
                b.Image = Icons.Glyph(glyph, 16, Color.White);
                b.ImageAlign = ContentAlignment.MiddleLeft;
                b.TextImageRelation = TextImageRelation.ImageBeforeText;
                b.TextAlign = ContentAlignment.MiddleCenter;
                b.Padding = new Padding(12, 0, 12, 0);
            }
            return b;
        }

        // Pulsante piccolo e piatto, senza bordo, per le intestazioni.
        public static Button LinkButton(string text, string glyph, bool onDark)
        {
            Button b = new Button();
            b.Text = text;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = onDark ? Color.FromArgb(70, 70, 70) : HoverBg;
            b.FlatAppearance.MouseDownBackColor = onDark ? Color.FromArgb(90, 90, 90) : PressedBg;
            b.BackColor = onDark ? ConsoleHeaderBg : HeaderBg;
            b.ForeColor = onDark ? ConsoleHeaderText : Text;
            b.Font = UI;
            b.UseVisualStyleBackColor = false;
            b.Cursor = Cursors.Hand;
            b.AutoSize = true;
            b.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            b.Padding = new Padding(6, 2, 6, 2);
            if (glyph != null)
            {
                b.Image = Icons.Glyph(glyph, 14, onDark ? ConsoleHeaderText : Text);
                b.ImageAlign = ContentAlignment.MiddleLeft;
                b.TextImageRelation = TextImageRelation.ImageBeforeText;
            }
            return b;
        }

        public static void Tip(ToolTip tip, Control c, string text)
        {
            tip.SetToolTip(c, text);
        }
    }

    // Le icone sono i glifi di "Segoe Fluent Icons" (Windows 11) o "Segoe MDL2 Assets"
    // (Windows 10): gli stessi delle app di sistema, nitidi a ogni risoluzione, e senza
    // dover portarsi dietro un solo file immagine. Se mancano entrambi i caratteri,
    // i pulsanti restano di solo testo e l'app funziona lo stesso.
    public static class Icons
    {
        public const string Search = "\uE721";
        public const string Pc = "\uE977";
        public const string Link = "\uE71B";
        public const string NewFolder = "\uE8F4";
        public const string Folder = "\uE8B7";
        public const string FolderOpen = "\uE838";
        public const string Delete = "\uE74D";
        public const string Refresh = "\uE72C";
        public const string Save = "\uE74E";
        public const string SaveAs = "\uE792";
        public const string Play = "\uE768";
        public const string Stop = "\uE71A";
        public const string Shield = "\uEA18";
        public const string Help = "\uE897";
        public const string Info = "\uE946";
        public const string Copy = "\uE8C8";
        public const string Clear = "\uE894";
        public const string Download = "\uE896";
        public const string OpenFile = "\uE8E5";
        public const string Shop = "\uE719";
        public const string Package = "\uE7B8";
        public const string Filter = "\uE71C";
        public const string More = "\uE712";
        public const string Import = "\uE8B5";
        public const string Export = "\uEDE1";
        public const string Check = "\uE73E";
        public const string Cancel = "\uE711";
        public const string Error = "\uE783";
        public const string Warning = "\uE7BA";
        public const string Completed = "\uE930";
        public const string History = "\uE81C";
        public const string Sync = "\uE895";
        public const string Add = "\uE710";
        public const string Accept = "\uE8FB";
        public const string Settings = "\uE713";
        public const string CheckList = "\uE9D5";
        public const string Globe = "\uE774";
        public const string List = "\uEA37";
        public const string Rename = "\uE8AC";
        public const string OpenNew = "\uE8A7";
        public const string Multiselect = "\uE762";
        public const string ClearSelection = "\uE9A8";
        public const string AddTo = "\uECC8";
        public const string RemoveFrom = "\uECC9";
        public const string ViewAll = "\uE8A9";
        public const string Tag = "\uE8EC";
        public const string Page = "\uE7C3";

        private static string _family;
        private static bool _resolved;
        private static readonly Dictionary<string, Bitmap> _cache = new Dictionary<string, Bitmap>();

        public static bool Available
        {
            get { Resolve(); return _family != null; }
        }

        private static void Resolve()
        {
            if (_resolved) return;
            _resolved = true;
            if (Theme.FontExists("Segoe Fluent Icons")) _family = "Segoe Fluent Icons";
            else if (Theme.FontExists("Segoe MDL2 Assets")) _family = "Segoe MDL2 Assets";
            else _family = null;
        }

        public static Bitmap Glyph(string glyph, int size, Color color)
        {
            Resolve();
            if (_family == null || string.IsNullOrEmpty(glyph)) return null;

            string key = glyph + "|" + size + "|" + color.ToArgb();
            Bitmap cached;
            if (_cache.TryGetValue(key, out cached)) return cached;

            Bitmap bmp = new Bitmap(size, size);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                // Il glifo e' disegnato in una cella quadrata pari al corpo del carattere:
                // la dimensione in punti va riportata in pixel rispetto ai 96 DPI nominali.
                float pt = size * 72f / 96f * 0.92f;
                using (Font f = new Font(_family, pt, FontStyle.Regular, GraphicsUnit.Point))
                using (SolidBrush b = new SolidBrush(color))
                using (StringFormat sf = new StringFormat())
                {
                    sf.Alignment = StringAlignment.Center;
                    sf.LineAlignment = StringAlignment.Center;
                    sf.FormatFlags = StringFormatFlags.NoClip;
                    g.DrawString(glyph, f, b, new RectangleF(0, 0, size, size), sf);
                }
            }
            _cache[key] = bmp;
            return bmp;
        }

        // L'icona della finestra: un rettangolo in colore accento con la freccia di
        // scaricamento. Sta anche nell'eseguibile (la genera build.ps1 con lo stesso
        // disegno), cosi' in Esplora file l'app si riconosce a colpo d'occhio.
        public static Icon AppIcon()
        {
            try
            {
                Icon fromExe = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (fromExe != null) return fromExe;
            }
            catch { }
            return Icon.FromHandle(DrawAppBitmap(32).GetHicon());
        }

        public static Bitmap DrawAppBitmap(int size)
        {
            Bitmap bmp = new Bitmap(size, size);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                float r = size * 0.22f;
                using (GraphicsPath p = RoundedRect(new RectangleF(0, 0, size - 1, size - 1), r))
                using (SolidBrush bg = new SolidBrush(Theme.Accent))
                    g.FillPath(bg, p);

                float w = size;
                using (Pen pen = new Pen(Color.White, Math.Max(1.5f, size * 0.1f)))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    pen.LineJoin = LineJoin.Round;
                    // asta della freccia
                    g.DrawLine(pen, w * 0.5f, w * 0.22f, w * 0.5f, w * 0.6f);
                    // punta
                    g.DrawLine(pen, w * 0.32f, w * 0.44f, w * 0.5f, w * 0.62f);
                    g.DrawLine(pen, w * 0.68f, w * 0.44f, w * 0.5f, w * 0.62f);
                    // vassoio
                    g.DrawLine(pen, w * 0.24f, w * 0.66f, w * 0.24f, w * 0.78f);
                    g.DrawLine(pen, w * 0.24f, w * 0.78f, w * 0.76f, w * 0.78f);
                    g.DrawLine(pen, w * 0.76f, w * 0.78f, w * 0.76f, w * 0.66f);
                }
            }
            return bmp;
        }

        private static GraphicsPath RoundedRect(RectangleF r, float radius)
        {
            GraphicsPath p = new GraphicsPath();
            float d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    // Barra dei comandi piatta: niente sfumature, niente bordi in rilievo. Il passaggio
    // del mouse e' un rettangolo tenue, come nelle app di sistema.
    public class FlatToolStripRenderer : ToolStripProfessionalRenderer
    {
        public FlatToolStripRenderer() : base(new FlatColorTable())
        {
            RoundedEdges = false;
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using (SolidBrush b = new SolidBrush(Theme.WindowBg))
                e.Graphics.FillRectangle(b, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            // Solo una riga sottile in basso, che stacca la barra dal resto.
            using (Pen p = new Pen(Theme.Border))
                e.Graphics.DrawLine(p, 0, e.ToolStrip.Height - 1, e.ToolStrip.Width, e.ToolStrip.Height - 1);
        }

        protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
        {
            ToolStripButton b = e.Item as ToolStripButton;
            if (b == null) { base.OnRenderButtonBackground(e); return; }
            if (!b.Enabled) return;
            if (b.Pressed) Fill(e, Theme.PressedBg);
            else if (b.Selected || b.Checked) Fill(e, Theme.HoverBg);
        }

        protected override void OnRenderDropDownButtonBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Enabled) return;
            if (e.Item.Pressed) Fill(e, Theme.PressedBg);
            else if (e.Item.Selected) Fill(e, Theme.HoverBg);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            if (e.Vertical)
            {
                int x = e.Item.Width / 2;
                using (Pen p = new Pen(Theme.BorderStrong))
                    e.Graphics.DrawLine(p, x, 6, x, e.Item.Height - 6);
            }
            else base.OnRenderSeparator(e);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Theme.Text : Theme.TextDisabled;
            base.OnRenderItemText(e);
        }

        private static void Fill(ToolStripItemRenderEventArgs e, Color c)
        {
            Rectangle r = new Rectangle(Point.Empty, e.Item.Size);
            r.Inflate(-1, -2);
            using (SolidBrush b = new SolidBrush(c))
                e.Graphics.FillRectangle(b, r);
        }

        private class FlatColorTable : ProfessionalColorTable
        {
            public override Color MenuBorder { get { return Theme.BorderStrong; } }
            public override Color MenuItemBorder { get { return Color.Transparent; } }
            public override Color MenuItemSelected { get { return Theme.HoverBg; } }
            public override Color MenuItemSelectedGradientBegin { get { return Theme.HoverBg; } }
            public override Color MenuItemSelectedGradientEnd { get { return Theme.HoverBg; } }
            public override Color MenuItemPressedGradientBegin { get { return Theme.PressedBg; } }
            public override Color MenuItemPressedGradientEnd { get { return Theme.PressedBg; } }
            public override Color ToolStripDropDownBackground { get { return Theme.CardBg; } }
            public override Color ImageMarginGradientBegin { get { return Theme.CardBg; } }
            public override Color ImageMarginGradientMiddle { get { return Theme.CardBg; } }
            public override Color ImageMarginGradientEnd { get { return Theme.CardBg; } }
            public override Color SeparatorDark { get { return Theme.Border; } }
            public override Color SeparatorLight { get { return Theme.CardBg; } }
        }
    }
}
