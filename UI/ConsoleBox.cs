using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace LHInstaller
{
    // La console incorporata nella finestra principale: qui scorre, riga per riga,
    // l'output vero di winget e degli installer, mentre girano.
    public class ConsoleBox : RichTextBox
    {
        private const int MaxLines = 5000;
        private readonly StringBuilder _plain = new StringBuilder();
        private bool _autoScroll = true;

        public ConsoleBox()
        {
            ReadOnly = true;
            BorderStyle = BorderStyle.None;
            BackColor = Theme.ConsoleBg;
            ForeColor = Theme.ConsoleText;
            Font = Theme.Mono;
            // Le righe lunghe vanno a capo: un testo nascosto oltre il bordo destro e'
            // peggio di un testo spezzato, e qui si legge cosa sta succedendo.
            WordWrap = true;
            ScrollBars = RichTextBoxScrollBars.Vertical;
            DetectUrls = false;
            HideSelection = false;
            ShortcutsEnabled = true;
            ContextMenuStrip = BuildMenu();
        }

        private ContextMenuStrip BuildMenu()
        {
            ContextMenuStrip m = new ContextMenuStrip();
            m.Renderer = new FlatToolStripRenderer();
            m.Font = Theme.UI;
            ToolStripMenuItem copy = new ToolStripMenuItem(Tr.T("Copia", "Copy"), Icons.Glyph(Icons.Copy, 16, Theme.Text));
            copy.ShortcutKeyDisplayString = "Ctrl+C";
            copy.Click += delegate { if (SelectionLength > 0) Copy(); };
            ToolStripMenuItem all = new ToolStripMenuItem(Tr.T("Seleziona tutto", "Select all"));
            all.ShortcutKeyDisplayString = "Ctrl+A";
            all.Click += delegate { SelectAll(); };
            ToolStripMenuItem clear = new ToolStripMenuItem(Tr.T("Pulisci", "Clear"), Icons.Glyph(Icons.Clear, 16, Theme.Text));
            clear.Click += delegate { ClearAll(); };
            ToolStripMenuItem save = new ToolStripMenuItem(Tr.T("Salva log...", "Save log..."), Icons.Glyph(Icons.Save, 16, Theme.Text));
            save.Click += delegate { SaveLogInteractive(); };
            m.Items.Add(copy);
            m.Items.Add(all);
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(clear);
            m.Items.Add(save);
            return m;
        }

        public bool AutoScrollEnabled
        {
            get { return _autoScroll; }
            set { _autoScroll = value; if (value) ScrollToEnd(); }
        }

        public static Color ColorFor(LineKind kind)
        {
            switch (kind)
            {
                case LineKind.Info: return Theme.ConsoleInfo;
                case LineKind.Good: return Theme.ConsoleGood;
                case LineKind.Warn: return Theme.ConsoleWarn;
                case LineKind.Error: return Theme.ConsoleError;
                default: return Theme.ConsoleText;
            }
        }

        public void Write(string text, LineKind kind)
        {
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action<string, LineKind>(Write), text, kind); }
                catch { }
                return;
            }
            AppendCore(text, kind);
        }

        private void AppendCore(string text, LineKind kind)
        {
            if (text == null) text = "";

            // L'orario solo sulle righe "nostre" (avvisi, esiti, intestazioni): l'output
            // grezzo di winget resta pulito, e si capisce a colpo d'occhio chi parla.
            bool stamp = kind != LineKind.Normal && text.Length > 0;
            string ts = stamp ? DateTime.Now.ToString("HH:mm:ss") + "  " : "";

            _plain.Append(ts).AppendLine(text);
            TrimIfTooLong();

            SelectionStart = TextLength;
            SelectionLength = 0;
            if (stamp)
            {
                SelectionColor = Theme.ConsoleDim;
                AppendText(ts);
            }
            SelectionColor = ColorFor(kind);
            AppendText(text + Environment.NewLine);
            SelectionColor = ForeColor;

            if (_autoScroll) ScrollToEnd();
        }

        private void ScrollToEnd()
        {
            SelectionStart = TextLength;
            SelectionLength = 0;
            ScrollToCaret();
        }

        // Una sessione lunga produce migliaia di righe: taglio le piu' vecchie,
        // altrimenti il controllo diventa lento.
        private void TrimIfTooLong()
        {
            if (Lines.Length <= MaxLines) return;
            int cut = Lines.Length - MaxLines + 500;
            int index = GetFirstCharIndexFromLine(cut);
            if (index <= 0) return;
            Select(0, index);
            SelectedText = "";
        }

        public void Rule(string title)
        {
            string bar = new string('-', Math.Max(4, 72 - title.Length));
            Write("", LineKind.Normal);
            Write("--- " + title + " " + bar, LineKind.Info);
        }

        public void ClearAll()
        {
            Clear();
            _plain.Length = 0;
        }

        public string PlainText()
        {
            return _plain.ToString();
        }

        public string SaveLog()
        {
            string path = Path.Combine(Storage.LogDir(),
                "sessione-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
            File.WriteAllText(path, _plain.ToString(), new UTF8Encoding(false));
            return path;
        }

        public void SaveLogInteractive()
        {
            using (SaveFileDialog d = new SaveFileDialog())
            {
                d.Title = Tr.T("Salva il log della console", "Save the console log");
                d.Filter = Tr.T("File di testo (*.txt)|*.txt", "Text files (*.txt)|*.txt");
                d.FileName = "LHInstaller-log-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".txt";
                d.InitialDirectory = Storage.LogDir();
                if (d.ShowDialog(FindForm()) != DialogResult.OK) return;
                try
                {
                    File.WriteAllText(d.FileName, _plain.ToString(), new UTF8Encoding(false));
                    Write(Tr.F("Log salvato in {0}", "Log saved to {0}", d.FileName), LineKind.Good);
                }
                catch (Exception ex)
                {
                    Write(Tr.F("Non sono riuscito a salvare il log: {0}", "Could not save the log: {0}", ex.Message), LineKind.Error);
                }
            }
        }
    }
}
