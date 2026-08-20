using System;
using System.Diagnostics;
using System.Drawing;
using System.Security.Principal;
using System.Windows.Forms;

namespace LHInstaller
{
    // "Come funziona" e "Informazioni": la stessa finestra, due pagine.
    public class HelpForm : Form
    {
        public HelpForm(int page)
        {
            SuspendLayout();
            Text = AppInfo.Name + " - " + Tr.T("Aiuto", "Help");
            Icon = Icons.AppIcon();
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(740, 580);
            MinimumSize = new Size(560, 440);
            Font = Theme.UI;
            BackColor = Theme.WindowBg;
            ShowInTaskbar = false;

            TabControl tabs = new TabControl();
            tabs.Dock = DockStyle.Fill;
            tabs.Font = Theme.UI;
            tabs.Padding = new Point(14, 6);

            TabPage how = new TabPage(Tr.T("Come funziona", "How it works"));
            how.BackColor = Theme.CardBg;
            how.Padding = new Padding(0);
            how.Controls.Add(HowText());

            TabPage about = new TabPage(Tr.T("Informazioni", "About"));
            about.BackColor = Theme.CardBg;
            about.Controls.Add(AboutText());

            tabs.TabPages.Add(how);
            tabs.TabPages.Add(about);
            tabs.SelectedIndex = page == 1 ? 1 : 0;

            Panel wrap = new Panel();
            wrap.Dock = DockStyle.Fill;
            wrap.Padding = new Padding(12, 12, 12, 0);
            wrap.Controls.Add(tabs);

            Panel foot = new Panel();
            foot.Dock = DockStyle.Bottom;
            foot.Height = 54;
            foot.Padding = new Padding(12, 10, 12, 12);

            Button close = Theme.PrimaryButton(Tr.T("Chiudi", "Close"), null);
            close.Size = new Size(110, 32);
            close.Dock = DockStyle.Right;
            close.DialogResult = DialogResult.OK;
            foot.Controls.Add(close);

            Button openDir = Theme.FlatButton(Tr.T("Apri la cartella dei dati", "Open the data folder"), Icons.FolderOpen);
            openDir.Size = new Size(220, 32);
            openDir.Dock = DockStyle.Left;
            openDir.Click += delegate
            {
                try { Process.Start("explorer.exe", "\"" + Storage.DataDir() + "\""); }
                catch { }
            };
            foot.Controls.Add(openDir);

            Controls.Add(wrap);
            Controls.Add(foot);
            AcceptButton = close;
            CancelButton = close;

            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ResumeLayout(false);
        }

        private static RichTextBox Box()
        {
            RichTextBox r = new RichTextBox();
            r.Dock = DockStyle.Fill;
            r.ReadOnly = true;
            r.BorderStyle = BorderStyle.None;
            r.BackColor = Theme.CardBg;
            r.ForeColor = Theme.Text;
            r.Font = new Font("Segoe UI", 9.75f);
            r.DetectUrls = false;
            r.Margin = new Padding(0);
            // Un po' di margine interno: il testo attaccato al bordo si legge male.
            r.SelectionIndent = 16;
            r.SelectionRightIndent = 16;
            return r;
        }

        private static void H(RichTextBox r, string text)
        {
            r.SelectionFont = new Font("Segoe UI Semibold", 11.5f, FontStyle.Regular);
            r.SelectionColor = Theme.Text;
            r.AppendText(text + Environment.NewLine);
        }

        private static void P(RichTextBox r, string text)
        {
            r.SelectionFont = new Font("Segoe UI", 9.75f);
            r.SelectionColor = Theme.Text;
            r.AppendText(text + Environment.NewLine);
        }

        private static void Step(RichTextBox r, string n, string title, string body)
        {
            r.SelectionFont = new Font("Segoe UI Semibold", 9.75f);
            r.SelectionColor = Theme.Accent;
            r.AppendText(n + "  ");
            r.SelectionFont = new Font("Segoe UI Semibold", 9.75f);
            r.SelectionColor = Theme.Text;
            r.AppendText(title + Environment.NewLine);
            r.SelectionFont = new Font("Segoe UI", 9.75f);
            r.SelectionColor = Theme.TextSecondary;
            r.SelectionIndent = 36;
            r.AppendText(body + Environment.NewLine + Environment.NewLine);
            r.SelectionIndent = 16;
        }

        private static void Note(RichTextBox r, string text)
        {
            r.SelectionFont = new Font("Segoe UI", 9.25f);
            r.SelectionColor = Theme.TextSecondary;
            r.AppendText(text + Environment.NewLine);
        }

        private static RichTextBox HowText()
        {
            RichTextBox r = Box();
            r.AppendText(Environment.NewLine);

            H(r, Tr.T("Prima di formattare", "Before you format"));
            P(r, "");
            Step(r, "1", Tr.T("Riempi la lista", "Fill the list"),
                Tr.T("\"Leggi da questo PC\" ha due schede: i programmi che winget sa reinstallare (li spunti e basta) e quelli "
                   + "che NON sa reinstallare, perche' presi da un sito o con licenza. Per questi ultimi puoi cercare una "
                   + "corrispondenza a catalogo, aggiungere l'indirizzo dell'installer, o metterli in lista come promemoria: "
                   + "finiscono nel gruppo \"Da completare\" con stato \"manca l'indirizzo\" finche' non glielo dai. "
                   + "\"Cerca nel catalogo\" e \"Aggiungi indirizzo\" servono per aggiungere a mano.",
                     "\"Read from this PC\" has two tabs: the programs winget can reinstall (just tick them) and those it "
                   + "CANNOT, because they came from a website or carry a licence. For the latter you can look for a "
                   + "catalog match, add the installer address, or put them on the list as reminders: they land in the "
                   + "\"To complete\" group with the status \"address missing\" until you give them one. "
                   + "\"Search the catalog\" and \"Add address\" are there to add entries by hand."));
            Step(r, "2", Tr.T("Organizza e spunta", "Organise and tick"),
                Tr.T("I gruppi a sinistra servono a fare ordine: la casella di un gruppo accende o spegne tutto quello che contiene. "
                   + "Si installa solo cio' che ha la spunta.",
                     "The groups on the left keep things tidy: a group's checkbox turns everything inside it on or off. "
                   + "Only ticked entries get installed."));
            Step(r, "3", Tr.T("Copia la cartella su una chiavetta", "Copy the folder to a USB stick"),
                Tr.T("Bastano LHInstaller.exe e LHInstaller.json, che sta accanto all'eseguibile e si salva da solo. "
                   + "Nessun installer viene archiviato: si scarica tutto al momento.",
                     "LHInstaller.exe and LHInstaller.json are enough; the latter sits next to the executable and saves itself. "
                   + "No installer is stored: everything is downloaded when the time comes."));

            H(r, Tr.T("Dopo aver formattato", "After you format"));
            P(r, "");
            Step(r, "4", Tr.T("Copia la cartella sul PC e apri LHInstaller", "Copy the folder to the PC and open LHInstaller"),
                Tr.T("Non serve installare niente: Windows 10 e 11 hanno gia' tutto quello che serve (.NET Framework e winget).",
                     "Nothing to install first: Windows 10 and 11 already ship with what is needed (.NET Framework and winget)."));
            Step(r, "5", Tr.T("Premi \"Avvia installazioni\"", "Press \"Start installs\""),
                Tr.T("Chiede i permessi di amministratore una volta sola, poi scarica e installa in fila. "
                   + "Nella console in basso scorre l'output vero di winget; nella colonna Stato vedi a che punto e' ogni programma.",
                     "It asks for administrator rights once, then downloads and installs one after the other. "
                   + "The console at the bottom streams winget's real output; the Status column shows where each program is."));
            Step(r, "6", Tr.T("Se qualcosa non riesce", "If something fails"),
                Tr.T("Il riepilogo elenca i falliti. Premi di nuovo Avvia: quelli gia' installati vengono saltati. "
                   + "Se un installer fallisce in silenzio, accendi \"Mostra le finestre degli installer\" e riprova.",
                     "The summary lists the failures. Press Start again: whatever is already installed gets skipped. "
                   + "If an installer fails silently, turn on \"Show installer windows\" and try again."));

            H(r, Tr.T("Perche' chiede l'amministratore solo all'avvio",
                      "Why it asks for administrator only when you start"));
            P(r, "");
            Note(r, Tr.F("Per preparare la lista non servono permessi speciali, e sarebbe fastidioso averli chiesti a ogni apertura. "
                       + "Installare software invece li richiede: quando premi Avvia, {0} si riavvia come amministratore "
                       + "(Windows lo chiede una volta) e riprende da solo. Lo scudo sul pulsante e' la convenzione di Windows per dirlo.",
                         "Building the list needs no special rights, and being asked at every launch would be annoying. "
                       + "Installing software does need them: when you press Start, {0} restarts as administrator "
                       + "(Windows asks once) and carries on by itself. The shield on the button is the Windows convention for that.",
                         AppInfo.Name));
            P(r, "");

            H(r, Tr.T("Cosa copre e cosa no", "What it covers and what it does not"));
            P(r, "");
            Note(r, Tr.T("Il catalogo winget copre i programmi scaricabili dal web: browser, Discord, Spotify, VLC, 7-Zip, Steam, "
                       + "Blender, Git, Python, Visual Studio Code, OBS e cosi' via. Non copre i programmi con licenza personale "
                       + "o account (plugin audio, Ableton, FL Studio, DaVinci Resolve, Native Access): la scheda "
                       + "\"Non reinstallabili\" di \"Leggi da questo PC\" te li elenca, cosi' decidi tu per ognuno: indirizzo "
                       + "dell'installer, promemoria, o portale del produttore come hai sempre fatto.",
                         "The winget catalog covers programs you download from the web: browsers, Discord, Spotify, VLC, 7-Zip, "
                       + "Steam, Blender, Git, Python, Visual Studio Code, OBS and so on. It does not cover programs with a "
                       + "personal licence or an account (audio plug-ins, Ableton, FL Studio, DaVinci Resolve, Native Access): "
                       + "the \"Not reinstallable\" tab of \"Read from this PC\" lists them, so you decide for each one: "
                       + "installer address, reminder, or the maker's portal as you have always done."));
            P(r, "");

            H(r, Tr.T("Lingua e aggiornamenti", "Language and updates"));
            P(r, "");
            Note(r, Tr.F("La lingua si cambia dal menu \"Lingua\" nella barra in alto: italiano, inglese, oppure automatica, "
                       + "che segue Windows. La scelta si salva nel profilo, e {0} si riapre da solo nella lingua nuova.",
                         "The language is changed from the \"Language\" menu in the top bar: Italian, English, or automatic, "
                       + "which follows Windows. The choice is saved in the profile, and {0} reopens itself in the new language.",
                         AppInfo.Name));
            P(r, "");
            Note(r, Tr.F("All'avvio {0} controlla se ne esiste una versione piu' recente e, se c'e', lo dice con una striscia "
                       + "in cima alla finestra. Il controllo si puo' spegnere dal menu Aiuto, e se non c'e' connessione "
                       + "fallisce in silenzio, senza messaggi.",
                         "At startup {0} checks whether a newer version exists and, if so, says it with a strip at the top of "
                       + "the window. The check can be switched off from the Help menu, and if there is no connection it "
                       + "fails silently, with no messages.",
                         AppInfo.Name));

            r.SelectionStart = 0;
            r.ScrollToCaret();
            return r;
        }

        private static RichTextBox AboutText()
        {
            RichTextBox r = Box();
            r.AppendText(Environment.NewLine);
            H(r, AppInfo.Name + " " + AppInfo.Version);
            P(r, "");
            P(r, Tr.T("Rimette in piedi i programmi di un PC appena formattato. Portatile, senza installazione, "
                    + "senza dipendenze oltre a quelle che Windows ha gia'.",
                      "Puts the programs of a freshly formatted PC back in place. Portable, no installation, "
                    + "no dependencies beyond what Windows already ships with."));
            P(r, "");

            bool elevated = false;
            try
            {
                using (WindowsIdentity id = WindowsIdentity.GetCurrent())
                    elevated = new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { }

            string wexe = Winget.ExePath();
            string wver = wexe == null ? null : Winget.Version();

            H(r, Tr.T("Questa istanza", "This instance"));
            P(r, "");
            KV(r, Tr.T("Eseguibile", "Executable"), Application.ExecutablePath);
            KV(r, Tr.T("Cartella dati", "Data folder"), Storage.DataDir()
                + (Storage.DataDirIsPortable() ? Tr.T("  (portatile)", "  (portable)")
                                               : Tr.T("  (AppData: l'eseguibile e' in sola lettura)",
                                                      "  (AppData: the executable is read-only)")));
            KV(r, Tr.T("Profilo", "Profile"), Storage.ProfilePath());
            KV(r, Tr.T("Lingua", "Language"), Tr.CurrentName() + "  ("
                + (Tr.Choice == Tr.Auto ? Tr.T("automatica", "automatic") : Tr.T("scelta a mano", "chosen by hand")) + ")");
            KV(r, Tr.T("Amministratore", "Administrator"), elevated
                ? Tr.T("si'", "yes")
                : Tr.T("no, viene chiesto quando premi Avvia", "no, asked when you press Start"));
            KV(r, "winget", wexe == null ? Tr.T("non disponibile", "not available") : (wver ?? "") + "  -  " + wexe);
            KV(r, ".NET Framework", Environment.Version.ToString());
            KV(r, "Windows", Environment.OSVersion.VersionString);
            KV(r, Tr.T("Aggiornamenti", "Updates"), UpdateCheck.ReleasesPage);
            P(r, "");

            H(r, Tr.T("Com'e' fatto", "How it is built"));
            P(r, "");
            Note(r, Tr.T("C# / Windows Forms su .NET Framework 4.8, compilato con il compilatore che sta gia' dentro Windows "
                       + "(C:\\Windows\\Microsoft.NET). Icone: Segoe Fluent Icons / Segoe MDL2 Assets, le stesse delle app di "
                       + "sistema. Le installazioni passano da winget, il gestore pacchetti di Microsoft; gli indirizzi diretti "
                       + "vengono scaricati e l'installer riconosciuto dalla sua firma (Inno Setup, NSIS, MSI, WiX, InstallShield).",
                         "C# / Windows Forms on .NET Framework 4.8, built with the compiler already inside Windows "
                       + "(C:\\Windows\\Microsoft.NET). Icons: Segoe Fluent Icons / Segoe MDL2 Assets, the same ones the system "
                       + "apps use. Installs go through winget, Microsoft's package manager; direct addresses are downloaded "
                       + "and the installer is recognised by its signature (Inno Setup, NSIS, MSI, WiX, InstallShield)."));
            P(r, "");
            Note(r, Tr.T("I file scaricati dagli indirizzi finiscono in Download\\, i resoconti di ogni sessione in Log\\, "
                       + "entrambe accanto all'eseguibile.",
                         "Files downloaded from addresses end up in Download\\, the report of each session in Log\\, "
                       + "both next to the executable."));

            r.SelectionStart = 0;
            r.ScrollToCaret();
            return r;
        }

        private static void KV(RichTextBox r, string k, string v)
        {
            // Una tabulazione a larghezza fissa: con un carattere proporzionale gli spazi
            // non allineano niente.
            r.SelectionTabs = new int[] { 130 };
            r.SelectionFont = new Font("Segoe UI Semibold", 9.75f);
            r.SelectionColor = Theme.TextSecondary;
            r.AppendText(k + "\t");
            r.SelectionFont = new Font("Segoe UI", 9.75f);
            r.SelectionColor = Theme.Text;
            r.AppendText(v + Environment.NewLine);
        }
    }
}
