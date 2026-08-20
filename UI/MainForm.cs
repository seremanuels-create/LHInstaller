using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;

namespace LHInstaller
{
    public class MainForm : Form
    {
        private Profile _profile = Profile.CreateEmpty();
        private readonly bool _autoStart;
        private readonly ToolTip _tip = new ToolTip();

        // barra dei comandi
        private ToolStrip _tools;
        private ToolStripButton _tbSearch, _tbScan, _tbUrl, _tbNewGroup, _tbRemove, _tbCheck, _tbSave;
        private ToolStripDropDownButton _tbProfile, _tbHelp, _tbLang;
        private ToolStripMenuItem _miAutoUpdate;

        // striscia dell'aggiornamento dell'app
        private Panel _updateBar;
        private Label _updateText;
        private UpdateCheck.Result _update;

        // zona liste
        private SplitContainer _vsplit, _hsplit;
        private GroupNav _nav;
        private ListView _list;
        private Label _listTitle, _listSub;
        private TextBox _filter;
        private Panel _empty;
        private readonly Dictionary<AppItem, ListViewItem> _rows = new Dictionary<AppItem, ListViewItem>();
        private ImageList _icons;

        // console
        private ConsoleBox _console;
        private Label _consoleState;
        private CheckBox _autoScroll;

        // barra delle azioni e di stato
        private CheckBox _optSkip, _optContinue, _optWindows;
        private ProgressBar _bar;
        private Label _progressText;
        private Button _run, _stop;
        private StatusStrip _status;
        private ToolStripStatusLabel _stWinget, _stAdmin, _stProfile, _stCount;

        private bool _suspendCheck;
        private volatile bool _running;
        private volatile bool _cancel;
        private volatile bool _scanning;
        private ProcessRunner _current;
        private readonly System.Windows.Forms.Timer _saveTimer = new System.Windows.Forms.Timer();
        private bool _elevated;

        private readonly string _openAtStart;

        public MainForm(bool autoStart, string openAtStart)
        {
            _autoStart = autoStart;
            _openAtStart = openAtStart;
            _elevated = IsElevated();
            BuildUi();
            Load += delegate { OnReady(); };
            Shown += delegate { OpenRequestedDialog(); };
        }

        private void OpenRequestedDialog()
        {
            if (string.IsNullOrEmpty(_openAtStart)) return;
            string what = _openAtStart.ToLowerInvariant();
            BeginInvoke(new Action(delegate
            {
                if (what == "cerca") DoSearch();
                else if (what == "pc") DoScan();
                else if (what == "indirizzo") DoAddUrl();
                else if (what == "aiuto") new HelpForm(0).ShowDialog(this);
                else if (what == "info") new HelpForm(1).ShowDialog(this);
            }));
        }

        // =====================================================================
        //  costruzione dell'interfaccia
        // =====================================================================

        private void BuildUi()
        {
            SuspendLayout();

            Text = AppInfo.Name;
            Icon = Icons.AppIcon();
            ClientSize = new Size(1180, 760);
            MinimumSize = new Size(940, 620);
            StartPosition = FormStartPosition.CenterScreen;
            Font = Theme.UI;
            BackColor = Theme.WindowBg;
            KeyPreview = true;

            _tip.AutoPopDelay = 12000;
            _tip.InitialDelay = 500;

            BuildToolbar();
            BuildStatusBar();
            Panel action = BuildActionBar();
            Control main = BuildMain();

            Controls.Add(main);
            Controls.Add(BuildUpdateBar());
            Controls.Add(action);
            Controls.Add(_tools);
            Controls.Add(_status);

            _saveTimer.Interval = 700;
            _saveTimer.Tick += delegate { _saveTimer.Stop(); DoSave(false); };

            KeyDown += MainForm_KeyDown;
            FormClosing += MainForm_FormClosing;

            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ResumeLayout(false);
            PerformLayout();
        }

        private void BuildToolbar()
        {
            _tools = new ToolStrip();
            _tools.GripStyle = ToolStripGripStyle.Hidden;
            _tools.Renderer = new FlatToolStripRenderer();
            _tools.ImageScalingSize = new Size(16, 16);
            _tools.Padding = new Padding(8, 4, 8, 4);
            _tools.Font = Theme.UI;
            _tools.AutoSize = false;
            _tools.Height = 40;
            _tools.Dock = DockStyle.Top;

            _tbSearch = TbButton(Tr.T("Cerca nel catalogo", "Search the catalog"), Icons.Search,
                Tr.T("Cerca un programma nel catalogo winget e aggiungilo alla lista (Ctrl+K)",
                     "Search for a program in the winget catalog and add it to the list (Ctrl+K)"), delegate { DoSearch(); });
            _tbScan = TbButton(Tr.T("Leggi da questo PC", "Read from this PC"), Icons.Pc,
                Tr.T("Riempi la lista con i programmi gia' installati su questo PC",
                     "Fill the list with the programs already installed on this PC"), delegate { DoScan(); });
            _tbUrl = TbButton(Tr.T("Aggiungi indirizzo", "Add address"), Icons.Link,
                Tr.T("Aggiungi un programma che nel catalogo non c'e', dal link del suo installer",
                     "Add a program that is not in the catalog, from its installer link"), delegate { DoAddUrl(); });
            _tools.Items.Add(new ToolStripSeparator());
            _tbNewGroup = TbButton(Tr.T("Nuovo gruppo", "New group"), Icons.NewFolder,
                Tr.T("Crea un gruppo per organizzare la lista", "Create a group to organise the list"), delegate { DoNewGroup(); });
            _tbRemove = TbButton(Tr.T("Rimuovi", "Remove"), Icons.Delete,
                Tr.T("Togli dalla lista le voci selezionate (Canc)", "Take the selected entries off the list (Del)"),
                delegate { DoRemoveSelected(); });
            _tools.Items.Add(new ToolStripSeparator());
            _tbCheck = TbButton(Tr.T("Controlla aggiornamenti", "Check for updates"), Icons.Refresh,
                Tr.T("Verifica se a catalogo c'e' una versione nuova, o se il file a un indirizzo e' cambiato (F5)",
                     "Check whether the catalog has a newer version, or the file at an address has changed (F5)"),
                delegate { DoCheckUpdates(); });
            _tools.Items.Add(new ToolStripSeparator());
            _tbSave = TbButton(Tr.T("Salva", "Save"), Icons.Save,
                Tr.T("Salva il profilo (Ctrl+S)", "Save the profile (Ctrl+S)"), delegate { DoSave(true); });

            _tbProfile = new ToolStripDropDownButton(Tr.T("Profilo", "Profile"));
            _tbProfile.Image = Icons.Glyph(Icons.Folder, 16, Theme.Text);
            _tbProfile.DisplayStyle = Icons.Available ? ToolStripItemDisplayStyle.ImageAndText : ToolStripItemDisplayStyle.Text;
            _tbProfile.ToolTipText = Tr.T("Carica, salva altrove, backup completo, scambio con winget",
                                          "Load, save elsewhere, full backup, exchange with winget");
            _tbProfile.DropDown.Renderer = new FlatToolStripRenderer();
            _tbProfile.DropDown.Font = Theme.UI;
            MenuEntry(_tbProfile, Tr.T("Carica profilo...", "Load profile..."), Icons.OpenFile, delegate { DoLoadFrom(); });
            MenuEntry(_tbProfile, Tr.T("Salva profilo con nome...", "Save profile as..."), Icons.SaveAs, delegate { DoSaveAs(); });
            _tbProfile.DropDownItems.Add(new ToolStripSeparator());
            MenuEntry(_tbProfile, Tr.T("Backup completo...", "Full backup..."), Icons.Export, delegate { DoBackup(); });
            MenuEntry(_tbProfile, Tr.T("Ripristina backup...", "Restore backup..."), Icons.Import, delegate { DoRestore(); });
            _tbProfile.DropDownItems.Add(new ToolStripSeparator());
            MenuEntry(_tbProfile, Tr.T("Importa da file winget...", "Import from a winget file..."), Icons.Download, delegate { DoImportWinget(); });
            MenuEntry(_tbProfile, Tr.T("Esporta in formato winget...", "Export in winget format..."), Icons.Package, delegate { DoExportWinget(); });
            _tbProfile.DropDownItems.Add(new ToolStripSeparator());
            MenuEntry(_tbProfile, Tr.T("Apri la cartella dei dati", "Open the data folder"), Icons.FolderOpen, delegate { OpenDataDir(); });
            _tbProfile.DropDownItems.Add(new ToolStripSeparator());
            MenuEntry(_tbProfile, Tr.T("Svuota la lista...", "Empty the list..."), Icons.Clear, delegate { DoClearAll(); });
            _tools.Items.Add(_tbProfile);

            BuildLanguageMenu();

            _tbHelp = new ToolStripDropDownButton(Tr.T("Aiuto", "Help"));
            _tbHelp.Image = Icons.Glyph(Icons.Help, 16, Theme.Text);
            _tbHelp.DisplayStyle = Icons.Available ? ToolStripItemDisplayStyle.ImageAndText : ToolStripItemDisplayStyle.Text;
            _tbHelp.Alignment = ToolStripItemAlignment.Right;
            _tbHelp.DropDown.Renderer = new FlatToolStripRenderer();
            _tbHelp.DropDown.Font = Theme.UI;
            MenuEntry(_tbHelp, Tr.T("Come funziona", "How it works"), Icons.Page, delegate { new HelpForm(0).ShowDialog(this); });
            MenuEntry(_tbHelp, Tr.T("Informazioni", "About"), Icons.Info, delegate { new HelpForm(1).ShowDialog(this); });
            _tbHelp.DropDownItems.Add(new ToolStripSeparator());
            MenuEntry(_tbHelp, Tr.F("Cerca aggiornamenti di {0}", "Check for {0} updates", AppInfo.Name),
                      Icons.Sync, delegate { CheckAppUpdate(false); });
            _miAutoUpdate = MenuEntry(_tbHelp, Tr.T("Controlla all'avvio", "Check at startup"), null, delegate
            {
                _profile.CheckUpdatesOnStart = !_profile.CheckUpdatesOnStart;
                _miAutoUpdate.Checked = _profile.CheckUpdatesOnStart;
                DoSave(false);
            });
            _miAutoUpdate.CheckOnClick = false;
            _tools.Items.Add(_tbHelp);
        }

        private ToolStripButton TbButton(string text, string glyph, string tip, EventHandler onClick)
        {
            ToolStripButton b = new ToolStripButton(text);
            b.Image = Icons.Glyph(glyph, 16, Theme.Text);
            b.DisplayStyle = Icons.Available ? ToolStripItemDisplayStyle.ImageAndText : ToolStripItemDisplayStyle.Text;
            b.ToolTipText = tip;
            b.Padding = new Padding(4, 0, 4, 0);
            b.Click += onClick;
            _tools.Items.Add(b);
            return b;
        }

        private static ToolStripMenuItem MenuEntry(ToolStripDropDownItem parent, string text, string glyph, EventHandler onClick)
        {
            ToolStripMenuItem m = new ToolStripMenuItem(text);
            m.Image = Icons.Glyph(glyph, 16, Theme.Text);
            m.Click += onClick;
            parent.DropDownItems.Add(m);
            return m;
        }

        // Il menu della lingua. I nomi delle lingue restano nella lingua stessa
        // ("Italiano", "English"): chi apre l'app in una lingua che non capisce deve
        // comunque riconoscere la propria.
        private void BuildLanguageMenu()
        {
            _tbLang = new ToolStripDropDownButton(Tr.T("Lingua", "Language"));
            _tbLang.Image = Icons.Glyph(Icons.Globe, 16, Theme.Text);
            _tbLang.DisplayStyle = Icons.Available ? ToolStripItemDisplayStyle.ImageAndText : ToolStripItemDisplayStyle.Text;
            _tbLang.ToolTipText = Tr.T("Cambia la lingua dell'interfaccia", "Change the interface language");
            _tbLang.DropDown.Renderer = new FlatToolStripRenderer();
            _tbLang.DropDown.Font = Theme.UI;

            AddLanguageItem(Tr.Auto, Tr.NameOf(Tr.Auto));
            _tbLang.DropDownItems.Add(new ToolStripSeparator());
            AddLanguageItem(Tr.Italian, "Italiano");
            AddLanguageItem(Tr.English, "English");

            _tools.Items.Add(_tbLang);
        }

        private void AddLanguageItem(string code, string label)
        {
            ToolStripMenuItem mi = new ToolStripMenuItem(label);
            mi.Checked = Tr.Choice == code;
            mi.Click += delegate { ChangeLanguage(code); };
            _tbLang.DropDownItems.Add(mi);
        }

        // Cambiare lingua a finestra gia' costruita vorrebbe dire riscrivere il testo di
        // ogni controllo, uno per uno, e sperare di non dimenticarne nessuno. Molto piu'
        // onesto salvare e riaprire: e' un attimo, e la finestra torna com'era.
        private void ChangeLanguage(string code)
        {
            if (Tr.Choice == code) return;
            if (_running) return;

            _profile.Language = code;
            StoreWindowPrefs();
            try { Storage.Save(_profile); }
            catch (Exception ex)
            {
                Log(Tr.F("Non sono riuscito a salvare la lingua: {0}", "Could not save the language: {0}", ex.Message), LineKind.Error);
                return;
            }

            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(Application.ExecutablePath);
                psi.UseShellExecute = true;
                psi.WorkingDirectory = Storage.AppDir();
                Process.Start(psi);
                _closingForLanguage = true;
                Application.Exit();
            }
            catch (Exception ex)
            {
                Log(Tr.F("Riavvio non riuscito: {0}. La lingua e' salvata: si applica alla prossima apertura.",
                         "Restart failed: {0}. The language is saved: it applies next time you open the app.",
                         ex.Message), LineKind.Warn);
            }
        }

        private bool _closingForLanguage;

        // ---------------- striscia dell'aggiornamento ----------------

        private Panel BuildUpdateBar()
        {
            _updateBar = new Panel();
            _updateBar.Dock = DockStyle.Top;
            _updateBar.Height = 42;
            _updateBar.BackColor = Color.FromArgb(255, 244, 206);
            _updateBar.Padding = new Padding(12, 0, 8, 0);
            _updateBar.Visible = false;

            Panel line = new Panel();
            line.Dock = DockStyle.Bottom;
            line.Height = 1;
            line.BackColor = Color.FromArgb(228, 202, 128);
            _updateBar.Controls.Add(line);

            PictureBox icon = new PictureBox();
            icon.Image = Icons.Glyph(Icons.Download, 16, Theme.Warning);
            icon.Size = new Size(16, 16);
            icon.Location = new Point(12, 13);
            _updateBar.Controls.Add(icon);

            _updateText = new Label();
            _updateText.AutoSize = true;
            _updateText.ForeColor = Color.FromArgb(96, 62, 0);
            _updateText.Font = Theme.UI;
            _updateText.Location = new Point(36, 13);
            _updateBar.Controls.Add(_updateText);

            FlowLayoutPanel acts = new FlowLayoutPanel();
            acts.Dock = DockStyle.Right;
            acts.FlowDirection = FlowDirection.RightToLeft;
            acts.AutoSize = true;
            acts.WrapContents = false;
            acts.BackColor = _updateBar.BackColor;
            acts.Padding = new Padding(0, 6, 4, 0);

            Button close = Theme.LinkButton("", Icons.Cancel, false);
            close.BackColor = _updateBar.BackColor;
            close.FlatAppearance.MouseOverBackColor = Color.FromArgb(244, 232, 190);
            close.Click += delegate { _updateBar.Visible = false; };
            _tip.SetToolTip(close, Tr.T("Nascondi per ora", "Hide for now"));

            Button skip = Theme.LinkButton(Tr.T("Ignora questa versione", "Skip this version"), null, false);
            skip.BackColor = _updateBar.BackColor;
            skip.FlatAppearance.MouseOverBackColor = Color.FromArgb(244, 232, 190);
            skip.Click += delegate
            {
                if (_update == null) return;
                _profile.SkipUpdateVersion = _update.Version;
                DoSave(false);
                _updateBar.Visible = false;
                Log(Tr.F("Versione {0} ignorata: non te la segnalo piu'.",
                         "Version {0} skipped: I will not mention it again.", _update.Version), LineKind.Normal);
            };

            Button notes = Theme.LinkButton(Tr.T("Novita'", "What's new"), Icons.Page, false);
            notes.BackColor = _updateBar.BackColor;
            notes.FlatAppearance.MouseOverBackColor = Color.FromArgb(244, 232, 190);
            notes.Click += delegate { ShowUpdateNotes(); };

            Button get = Theme.PrimaryButton(Tr.T("Scarica", "Download"), Icons.OpenNew);
            get.Size = new Size(120, 28);
            get.Margin = new Padding(8, 0, 8, 0);
            get.Click += delegate
            {
                if (_update != null) OpenUrl(_update.PageUrl);
            };

            acts.Controls.Add(close);
            acts.Controls.Add(get);
            acts.Controls.Add(notes);
            acts.Controls.Add(skip);
            _updateBar.Controls.Add(acts);
            return _updateBar;
        }

        private void ShowUpdateNotes()
        {
            if (_update == null) return;
            string notes = UpdateCheck.PlainNotes(_update.Notes, 40);
            if (notes.Length == 0)
                notes = Tr.T("Questa release non porta note.", "This release carries no notes.");

            using (Form f = new Form())
            {
                f.Text = Tr.F("{0} {1}", "{0} {1}", AppInfo.Name, _update.Version)
                       + (_update.Title.Length > 0 ? "  -  " + _update.Title : "");
                f.Icon = Icons.AppIcon();
                f.StartPosition = FormStartPosition.CenterParent;
                f.ClientSize = new Size(620, 420);
                f.MinimumSize = new Size(460, 320);
                f.Font = Theme.UI;
                f.BackColor = Theme.WindowBg;
                f.ShowInTaskbar = false;

                Panel card = Theme.Card();
                card.Dock = DockStyle.Fill;
                RichTextBox box = new RichTextBox();
                box.Dock = DockStyle.Fill;
                box.ReadOnly = true;
                box.BorderStyle = BorderStyle.None;
                box.BackColor = Theme.CardBg;
                box.Font = new Font("Segoe UI", 9.75f);
                box.DetectUrls = false;
                box.Text = notes;
                Theme.CardBody(card).Padding = new Padding(12, 10, 12, 10);
                Theme.CardBody(card).Controls.Add(box);

                Panel wrap = new Panel();
                wrap.Dock = DockStyle.Fill;
                wrap.Padding = new Padding(12, 12, 12, 0);
                wrap.Controls.Add(card);

                Panel foot = new Panel();
                foot.Dock = DockStyle.Bottom;
                foot.Height = 54;
                foot.Padding = new Padding(12, 10, 12, 12);
                Button ok = Theme.PrimaryButton(Tr.T("Chiudi", "Close"), null);
                ok.Size = new Size(110, 32);
                ok.Dock = DockStyle.Right;
                ok.DialogResult = DialogResult.OK;
                Button page = Theme.FlatButton(Tr.T("Apri la pagina della release", "Open the release page"), Icons.OpenNew);
                page.Size = new Size(240, 32);
                page.Dock = DockStyle.Left;
                page.Click += delegate { OpenUrl(_update.PageUrl); };
                foot.Controls.Add(ok);
                foot.Controls.Add(page);

                f.Controls.Add(wrap);
                f.Controls.Add(foot);
                f.AcceptButton = ok;
                f.CancelButton = ok;
                f.AutoScaleDimensions = new SizeF(96F, 96F);
                f.AutoScaleMode = AutoScaleMode.Dpi;
                f.ShowDialog(this);
            }
        }

        // manual = l'utente l'ha chiesto dal menu, quindi merita una risposta anche
        // quando non c'e' niente di nuovo. All'avvio invece si tace.
        private void CheckAppUpdate(bool atStartup)
        {
            Thread t = new Thread(delegate()
            {
                UpdateCheck.Result r = UpdateCheck.Check();
                UI(delegate { UpdateCheckDone(r, atStartup); });
            });
            t.IsBackground = true;
            t.Start();
        }

        private void UpdateCheckDone(UpdateCheck.Result r, bool atStartup)
        {
            _update = r;
            _profile.LastUpdateCheckOn = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

            if (!r.Ok)
            {
                // All'avvio, senza rete o senza release, non si dice niente: non e' un
                // problema dell'utente e non c'e' nulla da fare.
                if (atStartup) return;
                string msg = r.NoReleases
                    ? Tr.F("Non c'e' ancora nessuna versione pubblicata." + Environment.NewLine + Environment.NewLine
                         + "Quando ce ne sara' una, {0} te lo dira' da solo all'avvio." + Environment.NewLine
                         + "Pagina delle versioni: {1}",
                           "No version has been published yet." + Environment.NewLine + Environment.NewLine
                         + "When one appears, {0} will tell you by itself at startup." + Environment.NewLine
                         + "Releases page: {1}", AppInfo.Name, UpdateCheck.ReleasesPage)
                    : Tr.F("Non sono riuscito a controllare: {0}." + Environment.NewLine + Environment.NewLine
                         + "Pagina delle versioni: {1}",
                           "Could not check: {0}." + Environment.NewLine + Environment.NewLine
                         + "Releases page: {1}", r.Error, UpdateCheck.ReleasesPage);
                MessageBox.Show(this, msg, AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (!r.Newer)
            {
                if (atStartup) return;
                MessageBox.Show(this,
                    Tr.F("{0} {1} e' la versione piu' recente.", "{0} {1} is the latest version.",
                         AppInfo.Name, AppInfo.Version),
                    AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (atStartup && string.Equals(r.Version, _profile.SkipUpdateVersion, StringComparison.OrdinalIgnoreCase))
                return;

            _updateText.Text = Tr.F("E' disponibile {0} {1}{2}.  Questa e' la {3}.",
                                    "{0} {1}{2} is available.  This is {3}.",
                                    AppInfo.Name, r.Version,
                                    r.PublishedOn.Length > 0
                                        ? Tr.F(", del {0}", ", released {0}", r.PublishedOn) : "",
                                    AppInfo.Version);
            _updateBar.Visible = true;
            Log(Tr.F("Aggiornamento disponibile: {0} {1}  ->  {2}", "Update available: {0} {1}  ->  {2}",
                     AppInfo.Name, AppInfo.Version, r.Version), LineKind.Warn);
            DoSave(false);
        }

        private Control BuildMain()
        {
            // --- pannello gruppi
            _nav = new GroupNav();
            _nav.Dock = DockStyle.Fill;
            _nav.GroupToggled += Nav_GroupToggled;
            _nav.SelectionChanged += delegate { RefillList(); };
            _nav.NewGroupRequested += delegate { DoNewGroup(); };
            _nav.RenameRequested += DoRenameGroup;
            _nav.DeleteRequested += DoDeleteGroup;

            Label navTitle, navSub;
            Panel navCard = Theme.Card();
            Panel navHeader = Theme.CardHeader(Tr.T("Gruppi", "Groups"), out navTitle, out navSub);
            Theme.CardBody(navCard).Controls.Add(_nav);
            Theme.CardBody(navCard).Controls.Add(navHeader);

            // --- elenco dei programmi
            _icons = new ImageList();
            _icons.ColorDepth = ColorDepth.Depth32Bit;
            _icons.ImageSize = new Size(16, 16);
            _icons.Images.Add("winget", Icons.Glyph(Icons.Package, 16, Theme.TextSecondary) ?? new Bitmap(16, 16));
            _icons.Images.Add("url", Icons.Glyph(Icons.Globe, 16, Theme.TextSecondary) ?? new Bitmap(16, 16));

            _list = new ListView();
            _list.Dock = DockStyle.Fill;
            _list.View = View.Details;
            _list.CheckBoxes = true;
            _list.FullRowSelect = true;
            _list.HideSelection = false;
            _list.MultiSelect = true;
            _list.BorderStyle = BorderStyle.None;
            _list.ShowItemToolTips = true;
            _list.SmallImageList = _icons;
            _list.Font = Theme.UI;
            _list.Columns.Add(Tr.T("Nome", "Name"), 240);
            _list.Columns.Add(Tr.T("Origine", "Source"), 84);
            _list.Columns.Add(Tr.T("Dettaglio", "Detail"), 224);
            _list.Columns.Add(Tr.T("Versione", "Version"), 104);
            _list.Columns.Add(Tr.T("Stato", "Status"), 240);
            _list.ItemChecked += List_ItemChecked;
            _list.DoubleClick += delegate { EditSelected(); };
            _list.KeyDown += List_KeyDown;
            _list.ContextMenuStrip = BuildListMenu();
            _list.HandleCreated += delegate { Theme.ExplorerStyle(_list); };
            _list.Resize += delegate { FitColumns(); };

            Panel listCard = Theme.Card();
            Panel listHeader = Theme.CardHeader(Tr.T("Programmi", "Programs"), out _listTitle, out _listSub);

            _filter = new TextBox();
            _filter.Width = 240;
            _filter.Font = Theme.UI;
            _filter.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _filter.TextChanged += delegate { RefillList(); };
            _filter.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape) { _filter.Text = ""; e.Handled = true; e.SuppressKeyPress = true; }
            };
            SetCueBanner(_filter, Tr.T("Filtra la lista  (Ctrl+F)", "Filter the list  (Ctrl+F)"));
            _tip.SetToolTip(_filter, Tr.T("Mostra solo le voci il cui nome, identificativo, sito o gruppo contiene il testo. Esc per svuotare.",
                                          "Show only entries whose name, identifier, site or group contains the text. Esc clears it."));
            _filter.Location = new Point(listHeader.Width - _filter.Width - 10, 5);
            listHeader.Controls.Add(_filter);
            listHeader.Resize += delegate { _filter.Left = listHeader.ClientSize.Width - _filter.Width - 10; };

            _empty = BuildEmptyState();
            _empty.Dock = DockStyle.Fill;

            Theme.CardBody(listCard).Controls.Add(_list);
            Theme.CardBody(listCard).Controls.Add(_empty);
            Theme.CardBody(listCard).Controls.Add(listHeader);

            // --- console
            _console = new ConsoleBox();
            _console.Dock = DockStyle.Fill;

            Panel consoleCard = Theme.Card();
            consoleCard.BackColor = Theme.BorderStrong;
            Panel consoleHeader = new Panel();
            consoleHeader.Dock = DockStyle.Top;
            consoleHeader.Height = 34;
            consoleHeader.BackColor = Theme.ConsoleHeaderBg;

            Label consoleTitle = new Label();
            consoleTitle.Text = "Console";
            consoleTitle.Font = Theme.Header;
            consoleTitle.ForeColor = Theme.ConsoleHeaderText;
            consoleTitle.AutoSize = true;
            consoleTitle.Location = new Point(10, 9);
            consoleHeader.Controls.Add(consoleTitle);

            _consoleState = new Label();
            _consoleState.Text = Tr.T("output di winget e degli installer, riga per riga",
                                      "output from winget and the installers, line by line");
            _consoleState.Font = Theme.UI;
            _consoleState.ForeColor = Theme.ConsoleDim;
            _consoleState.AutoSize = true;
            _consoleState.Location = new Point(consoleTitle.Right + 10, 10);
            consoleHeader.Controls.Add(_consoleState);

            FlowLayoutPanel consoleTools = new FlowLayoutPanel();
            consoleTools.FlowDirection = FlowDirection.RightToLeft;
            consoleTools.Dock = DockStyle.Right;
            consoleTools.AutoSize = true;
            consoleTools.WrapContents = false;
            consoleTools.BackColor = Theme.ConsoleHeaderBg;
            consoleTools.Padding = new Padding(0, 3, 4, 0);

            Button saveLog = Theme.LinkButton(Tr.T("Salva log", "Save log"), Icons.Save, true);
            saveLog.Click += delegate { _console.SaveLogInteractive(); };
            Button clearLog = Theme.LinkButton(Tr.T("Pulisci", "Clear"), Icons.Clear, true);
            clearLog.Click += delegate { _console.ClearAll(); };
            _autoScroll = new CheckBox();
            _autoScroll.Text = Tr.T("Scorri in automatico", "Auto-scroll");
            _autoScroll.Checked = true;
            _autoScroll.ForeColor = Theme.ConsoleHeaderText;
            _autoScroll.BackColor = Theme.ConsoleHeaderBg;
            _autoScroll.AutoSize = true;
            _autoScroll.Margin = new Padding(6, 5, 10, 0);
            _autoScroll.CheckedChanged += delegate { _console.AutoScrollEnabled = _autoScroll.Checked; };
            consoleTools.Controls.Add(saveLog);
            consoleTools.Controls.Add(clearLog);
            consoleTools.Controls.Add(_autoScroll);
            consoleHeader.Controls.Add(consoleTools);

            Theme.CardBody(consoleCard).BackColor = Theme.ConsoleBg;
            Theme.CardBody(consoleCard).Controls.Add(_console);
            Theme.CardBody(consoleCard).Controls.Add(consoleHeader);

            // --- divisori
            _hsplit = new SplitContainer();
            _hsplit.Orientation = Orientation.Vertical;
            _hsplit.SplitterWidth = 8;
            _hsplit.BackColor = Theme.WindowBg;
            _hsplit.Size = new Size(1160, 400);
            _hsplit.Panel1MinSize = 170;
            _hsplit.Panel2MinSize = 420;
            _hsplit.SplitterDistance = 230;
            _hsplit.FixedPanel = FixedPanel.Panel1;
            _hsplit.Dock = DockStyle.Fill;
            navCard.Dock = DockStyle.Fill;
            listCard.Dock = DockStyle.Fill;
            _hsplit.Panel1.Controls.Add(navCard);
            _hsplit.Panel2.Controls.Add(listCard);

            _vsplit = new SplitContainer();
            _vsplit.Orientation = Orientation.Horizontal;
            _vsplit.SplitterWidth = 8;
            _vsplit.BackColor = Theme.WindowBg;
            _vsplit.Size = new Size(1160, 620);
            _vsplit.Panel1MinSize = 180;
            _vsplit.Panel2MinSize = 120;
            _vsplit.SplitterDistance = 380;
            _vsplit.Dock = DockStyle.Fill;
            consoleCard.Dock = DockStyle.Fill;
            _vsplit.Panel1.Controls.Add(_hsplit);
            _vsplit.Panel2.Controls.Add(consoleCard);

            Panel wrap = new Panel();
            wrap.Dock = DockStyle.Fill;
            wrap.Padding = new Padding(10, 10, 10, 4);
            wrap.BackColor = Theme.WindowBg;
            wrap.Controls.Add(_vsplit);
            return wrap;
        }

        // Quello che si vede quando la lista e' vuota: i tre modi per cominciare,
        // spiegati in una riga ciascuno, al posto di un riquadro bianco muto.
        private Panel BuildEmptyState()
        {
            Panel p = new Panel();
            p.BackColor = Theme.CardBg;

            TableLayoutPanel t = new TableLayoutPanel();
            t.ColumnCount = 1;
            t.RowCount = 6;
            t.AutoSize = true;
            t.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            t.Anchor = AnchorStyles.None;
            t.BackColor = Theme.CardBg;
            t.Padding = new Padding(0);

            PictureBox pic = new PictureBox();
            pic.Image = Icons.Glyph(Icons.Download, 48, Theme.BorderStrong);
            pic.Size = new Size(48, 48);
            pic.Margin = new Padding(0, 0, 0, 10);
            pic.Anchor = AnchorStyles.None;

            Label title = new Label();
            title.Text = Tr.T("La lista e' vuota", "The list is empty");
            title.Font = Theme.Title;
            title.ForeColor = Theme.Text;
            title.AutoSize = true;
            title.Anchor = AnchorStyles.None;

            Label sub = new Label();
            sub.Text = Tr.T("Mettici i programmi che vuoi ritrovare dopo il formattaggio. Tre modi:",
                            "Put in the programs you want back after formatting. Three ways:");
            sub.ForeColor = Theme.TextSecondary;
            sub.AutoSize = true;
            sub.Anchor = AnchorStyles.None;
            sub.Margin = new Padding(0, 4, 0, 16);

            Control b1 = EmptyAction(Icons.Pc, Tr.T("Leggi da questo PC", "Read from this PC"),
                Tr.T("il modo piu' rapido: prende tutto cio' che hai gia' e che winget sa reinstallare",
                     "the quickest way: takes everything you already have that winget can reinstall"),
                delegate { DoScan(); });
            Control b2 = EmptyAction(Icons.Search, Tr.T("Cerca nel catalogo", "Search the catalog"),
                Tr.T("scrivi un nome, scegli la riga consigliata, aggiungi",
                     "type a name, pick the recommended row, add"),
                delegate { DoSearch(); });
            Control b3 = EmptyAction(Icons.Link, Tr.T("Aggiungi indirizzo", "Add address"),
                Tr.T("per cio' che nel catalogo non c'e': incolli il link dell'installer",
                     "for what the catalog does not have: paste the installer link"),
                delegate { DoAddUrl(); });

            t.Controls.Add(pic, 0, 0);
            t.Controls.Add(title, 0, 1);
            t.Controls.Add(sub, 0, 2);
            t.Controls.Add(b1, 0, 3);
            t.Controls.Add(b2, 0, 4);
            t.Controls.Add(b3, 0, 5);

            p.Controls.Add(t);
            p.Resize += delegate
            {
                t.Location = new Point((p.ClientSize.Width - t.Width) / 2,
                                       Math.Max(20, (p.ClientSize.Height - t.Height) / 2 - 20));
            };
            t.SizeChanged += delegate
            {
                t.Location = new Point((p.ClientSize.Width - t.Width) / 2,
                                       Math.Max(20, (p.ClientSize.Height - t.Height) / 2 - 20));
            };
            return p;
        }

        private Control EmptyAction(string glyph, string title, string desc, EventHandler onClick)
        {
            Panel row = new Panel();
            row.Size = new Size(520, 44);
            row.Margin = new Padding(0, 3, 0, 3);
            row.Anchor = AnchorStyles.None;

            Button b = Theme.FlatButton(title, glyph);
            b.Size = new Size(200, 34);
            b.Location = new Point(0, 4);
            b.Click += onClick;

            Label l = new Label();
            l.Text = desc;
            l.ForeColor = Theme.TextSecondary;
            l.AutoSize = false;
            l.Size = new Size(312, 34);
            l.Location = new Point(208, 4);
            l.TextAlign = ContentAlignment.MiddleLeft;

            row.Controls.Add(b);
            row.Controls.Add(l);
            return row;
        }

        private ContextMenuStrip BuildListMenu()
        {
            ContextMenuStrip m = new ContextMenuStrip();
            m.Renderer = new FlatToolStripRenderer();
            m.Font = Theme.UI;
            m.Opening += delegate(object s, System.ComponentModel.CancelEventArgs e)
            {
                m.Items.Clear();
                AppItem it = SelectedItem();
                int n = _list.SelectedItems.Count;
                if (n == 0) { e.Cancel = true; return; }

                if (n == 1)
                {
                    MenuItem(m, it.IsPlaceholder() ? Tr.T("Aggiungi l'indirizzo...", "Add the address...")
                                                   : Tr.T("Modifica...", "Edit..."), Icons.Rename, delegate { EditSelected(); });
                    if (!it.IsWinget() && !it.IsPlaceholder())
                        MenuItem(m, Tr.T("Apri l'indirizzo nel browser", "Open the address in the browser"),
                                 Icons.OpenNew, delegate { OpenUrl(it.Url); });
                    if (!it.IsWinget())
                        MenuItem(m, Tr.T("Cerca il sito nel browser", "Search the web for it"), Icons.Globe, delegate
                        {
                            OpenUrl("https://www.bing.com/search?q=" + Uri.EscapeDataString(it.Name + " download windows"));
                        });
                    if (!it.IsPlaceholder())
                        MenuItem(m, it.IsWinget() ? Tr.T("Copia identificativo", "Copy identifier")
                                                  : Tr.T("Copia indirizzo", "Copy address"), Icons.Copy, delegate
                        {
                            try { Clipboard.SetText(it.IsWinget() ? it.PackageId : it.Url); } catch { }
                        });
                    m.Items.Add(new ToolStripSeparator());
                }

                ToolStripMenuItem move = new ToolStripMenuItem(Tr.T("Sposta nel gruppo", "Move to group"));
                move.Image = Icons.Glyph(Icons.Folder, 16, Theme.Text);
                foreach (string g in _profile.Groups)
                {
                    string gg = g;
                    ToolStripMenuItem gi = new ToolStripMenuItem(Groups.Show(g));
                    if (n == 1 && it.Group == g) { gi.Checked = true; }
                    gi.Click += delegate { MoveSelectedTo(gg); };
                    move.DropDownItems.Add(gi);
                }
                move.DropDownItems.Add(new ToolStripSeparator());
                ToolStripMenuItem newG = new ToolStripMenuItem(Tr.T("Nuovo gruppo...", "New group..."));
                newG.Click += delegate
                {
                    string name = Prompt(Tr.T("Nuovo gruppo", "New group"), Tr.T("Nome del gruppo:", "Group name:"), "");
                    if (!string.IsNullOrEmpty(name)) { _profile.EnsureGroup(name); MoveSelectedTo(_profile.CanonicalGroup(name)); }
                };
                move.DropDownItems.Add(newG);
                m.Items.Add(move);

                MenuItem(m, Tr.T("Spunta le voci selezionate", "Tick the selected entries"), Icons.Multiselect, delegate { SetSelectedChecked(true); });
                MenuItem(m, Tr.T("Togli la spunta alle selezionate", "Untick the selected entries"), Icons.ClearSelection, delegate { SetSelectedChecked(false); });
                m.Items.Add(new ToolStripSeparator());
                ToolStripMenuItem one = MenuItem(m, n == 1 ? Tr.T("Installa solo questo", "Install only this one")
                                                           : Tr.T("Installa solo le selezionate", "Install only the selected ones"),
                    Icons.Play, delegate { StartRun(SelectedItems()); });
                one.Enabled = !_running;
                m.Items.Add(new ToolStripSeparator());
                MenuItem(m, n == 1 ? Tr.T("Rimuovi dalla lista", "Remove from the list")
                                   : Tr.T("Rimuovi le selezionate", "Remove the selected ones"),
                         Icons.Delete, delegate { DoRemoveSelected(); });
            };
            return m;
        }

        private static ToolStripMenuItem MenuItem(ContextMenuStrip m, string text, string glyph, EventHandler onClick)
        {
            ToolStripMenuItem mi = new ToolStripMenuItem(text);
            mi.Image = Icons.Glyph(glyph, 16, Theme.Text);
            mi.Click += onClick;
            m.Items.Add(mi);
            return mi;
        }

        private Panel BuildActionBar()
        {
            Panel bar = new Panel();
            bar.Dock = DockStyle.Bottom;
            bar.Height = 78;
            bar.BackColor = Theme.WindowBg;
            bar.Padding = new Padding(10, 2, 10, 6);

            TableLayoutPanel t = new TableLayoutPanel();
            t.Dock = DockStyle.Fill;
            t.ColumnCount = 3;
            t.RowCount = 1;
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            t.BackColor = Theme.WindowBg;

            // opzioni: due righe, in colonne
            FlowLayoutPanel opts = new FlowLayoutPanel();
            opts.FlowDirection = FlowDirection.TopDown;
            opts.WrapContents = true;
            opts.AutoSize = true;
            opts.MaximumSize = new Size(0, 48);
            opts.Margin = new Padding(0);
            opts.Anchor = AnchorStyles.Left;
            opts.BackColor = Theme.WindowBg;

            _optSkip = Option(Tr.T("Salta i programmi gia' installati", "Skip programs already installed"),
                Tr.T("Prima di ogni pacchetto del catalogo controlla se c'e' gia': se si', passa oltre senza toccarlo.",
                     "Before each catalog package it checks whether it is already there: if so, it moves on without touching it."));
            _optContinue = Option(Tr.T("Se uno fallisce, vai avanti con gli altri", "If one fails, carry on with the rest"),
                Tr.T("Spento: alla prima installazione non riuscita la sequenza si ferma.",
                     "Off: the sequence stops at the first failed install."));
            _optWindows = Option(Tr.T("Mostra le finestre degli installer", "Show installer windows"),
                Tr.T("Di norma tutto e' silenzioso. Accendilo se un installer fallisce in silenzio: vedrai e guiderai tu la sua finestra.",
                     "Normally everything is silent. Turn it on if an installer fails quietly: you will see and drive its window."));
            opts.Controls.Add(_optSkip);
            opts.Controls.Add(_optContinue);
            opts.Controls.Add(_optWindows);
            _optSkip.CheckedChanged += delegate { _profile.SkipInstalled = _optSkip.Checked; QueueSave(); };
            _optContinue.CheckedChanged += delegate { _profile.ContinueOnError = _optContinue.Checked; QueueSave(); };
            _optWindows.CheckedChanged += delegate { _profile.ShowInstallerWindows = _optWindows.Checked; QueueSave(); };

            // avanzamento
            Panel prog = new Panel();
            prog.Dock = DockStyle.Fill;
            prog.Margin = new Padding(24, 0, 24, 0);
            prog.BackColor = Theme.WindowBg;
            _bar = new ProgressBar();
            _bar.Style = ProgressBarStyle.Continuous;
            _bar.Height = 6;
            _bar.Dock = DockStyle.Top;
            _progressText = new Label();
            _progressText.Dock = DockStyle.Fill;
            _progressText.ForeColor = Theme.TextSecondary;
            _progressText.TextAlign = ContentAlignment.TopLeft;
            _progressText.Padding = new Padding(0, 6, 0, 0);
            _progressText.Text = Tr.T("Pronto.", "Ready.");
            Panel progInner = new Panel();
            progInner.Dock = DockStyle.Fill;
            progInner.Padding = new Padding(0, 20, 0, 0);
            progInner.Controls.Add(_progressText);
            progInner.Controls.Add(_bar);
            prog.Controls.Add(progInner);

            // pulsanti
            FlowLayoutPanel btns = new FlowLayoutPanel();
            btns.FlowDirection = FlowDirection.LeftToRight;
            btns.AutoSize = true;
            btns.WrapContents = false;
            btns.Margin = new Padding(0);
            btns.BackColor = Theme.WindowBg;
            btns.Anchor = AnchorStyles.Right;

            _stop = Theme.FlatButton(Tr.T("Interrompi", "Stop"), Icons.Stop);
            _stop.Size = new Size(120, 40);
            _stop.Margin = new Padding(0, 0, 8, 0);
            _stop.Enabled = false;
            _stop.Click += delegate { DoCancel(); };
            _tip.SetToolTip(_stop, Tr.T("Ferma la sequenza appena finisce il passo in corso",
                                        "Stops the sequence as soon as the current step ends"));

            _run = Theme.PrimaryButton(Tr.T("Avvia installazioni", "Start installs"), _elevated ? Icons.Play : Icons.Shield);
            _run.Size = new Size(290, 40);
            _run.Margin = new Padding(0);
            _run.Click += delegate { StartRun(null); };
            _tip.SetToolTip(_run, _elevated
                ? Tr.T("Installa, in fila, tutti i programmi spuntati", "Installs, one after the other, every ticked program")
                : Tr.T("Installa, in fila, tutti i programmi spuntati. Chiede i permessi di amministratore una volta sola.",
                       "Installs, one after the other, every ticked program. Asks for administrator rights once."));

            btns.Controls.Add(_stop);
            btns.Controls.Add(_run);

            t.Controls.Add(opts, 0, 0);
            t.Controls.Add(prog, 1, 0);
            t.Controls.Add(btns, 2, 0);
            bar.Controls.Add(t);
            return bar;
        }

        private CheckBox Option(string text, string tip)
        {
            CheckBox c = new CheckBox();
            c.Text = text;
            c.AutoSize = true;
            c.Margin = new Padding(0, 2, 12, 0);
            c.ForeColor = Theme.Text;
            _tip.SetToolTip(c, tip);
            return c;
        }

        private void BuildStatusBar()
        {
            _status = new StatusStrip();
            _status.SizingGrip = true;
            _status.BackColor = Theme.WindowBg;
            _status.Renderer = new FlatToolStripRenderer();
            _status.Font = Theme.UISmall;
            _status.Padding = new Padding(10, 0, 14, 0);

            _stWinget = StatusCell(Tr.T("winget: controllo...", "winget: checking..."), Icons.Package);
            _stAdmin = StatusCell("", Icons.Shield);
            _stProfile = StatusCell("", Icons.Folder);
            _stProfile.Spring = true;
            _stProfile.TextAlign = ContentAlignment.MiddleLeft;
            _stCount = StatusCell("", Icons.CheckList);

            _status.Items.Add(_stWinget);
            _status.Items.Add(Sep());
            _status.Items.Add(_stAdmin);
            _status.Items.Add(Sep());
            _status.Items.Add(_stProfile);
            _status.Items.Add(_stCount);
        }

        private static ToolStripStatusLabel StatusCell(string text, string glyph)
        {
            ToolStripStatusLabel l = new ToolStripStatusLabel(text);
            l.Image = Icons.Glyph(glyph, 14, Theme.TextSecondary);
            l.ForeColor = Theme.TextSecondary;
            l.Padding = new Padding(2, 0, 6, 0);
            return l;
        }

        private static ToolStripStatusLabel Sep()
        {
            ToolStripStatusLabel l = new ToolStripStatusLabel("|");
            l.ForeColor = Theme.BorderStrong;
            return l;
        }

        // =====================================================================
        //  avvio
        // =====================================================================

        private void OnReady()
        {
            _console.Rule(AppInfo.Name + " " + AppInfo.Version);
            Log(Tr.F("Cartella dati: {0}{1}", "Data folder: {0}{1}", Storage.DataDir(),
                Storage.DataDirIsPortable()
                    ? Tr.T("  (portatile, accanto all'eseguibile)", "  (portable, next to the executable)")
                    : Tr.T("  (l'eseguibile e' in sola lettura: uso AppData)",
                           "  (the executable is read-only: using AppData)")), LineKind.Normal);

            if (_elevated)
            {
                _stAdmin.Text = Tr.T("Amministratore: si'", "Administrator: yes");
                _stAdmin.Image = Icons.Glyph(Icons.Shield, 14, Theme.Success);
                _stAdmin.ToolTipText = Tr.T("Questa istanza ha i permessi di amministratore: puo' installare senza altre richieste.",
                                            "This instance has administrator rights: it can install without asking again.");
                Log(Tr.T("In esecuzione come amministratore.", "Running as administrator."), LineKind.Normal);
            }
            else
            {
                _stAdmin.Text = Tr.T("Amministratore: no, verra' chiesto all'avvio",
                                     "Administrator: no, it will be asked at start");
                _stAdmin.ToolTipText = Tr.F("Per preparare la lista non servono permessi. Per installare si': quando premi "
                                          + "Avvia, {0} si riavvia come amministratore (una sola richiesta) e riprende da solo.",
                                            "Building the list needs no rights. Installing does: when you press Start, {0} "
                                          + "restarts as administrator (one prompt) and carries on by itself.", AppInfo.Name);
            }

            try
            {
                _profile = Storage.Load();
            }
            catch (Exception ex)
            {
                Log(ex.Message, LineKind.Warn);
                _profile = Profile.CreateEmpty();
            }

            ApplyWindowPrefs();
            _optSkip.Checked = _profile.SkipInstalled;
            _optContinue.Checked = _profile.ContinueOnError;
            _optWindows.Checked = _profile.ShowInstallerWindows;
            _stProfile.Text = Storage.ProfilePath();
            _stProfile.ToolTipText = Tr.T("Il file con la tua lista. Portalo sulla chiavetta insieme all'eseguibile.",
                                          "The file with your list. Take it to the USB stick along with the executable.");

            RefreshAll();

            if (_profile.Items.Count > 0)
                Log(Tr.F("{0} voci caricate dal profilo.", "{0} entries loaded from the profile.", _profile.Items.Count), LineKind.Good);

            _miAutoUpdate.Checked = _profile.CheckUpdatesOnStart;
            if (_profile.CheckUpdatesOnStart) CheckAppUpdate(true);

            // winget in un thread: su un PC appena acceso la prima chiamata puo' metterci qualche secondo.
            Thread t = new Thread(delegate()
            {
                string exe = Winget.ExePath();
                string v = exe == null ? null : Winget.Version();
                UI(delegate
                {
                    if (exe == null)
                    {
                        _stWinget.Text = Tr.T("winget: non disponibile", "winget: not available");
                        _stWinget.Image = Icons.Glyph(Icons.Error, 14, Theme.Danger);
                        _stWinget.ToolTipText = Tr.T("Installa \"Programma di installazione app\" dal Microsoft Store e riapri.",
                                                     "Install \"App Installer\" from the Microsoft Store and reopen.");
                        Log(Tr.T("winget NON e' disponibile su questo PC.", "winget is NOT available on this PC."), LineKind.Error);
                        Log(Tr.F("Serve \"Programma di installazione app\" dal Microsoft Store. Su Windows 11 c'e' gia'; "
                               + "se manca, installalo e riapri {0}.",
                                 "\"App Installer\" from the Microsoft Store is required. Windows 11 already has it; "
                               + "if it is missing, install it and reopen {0}.", AppInfo.Name), LineKind.Warn);
                        _run.Enabled = false;
                        _tbSearch.Enabled = false;
                        _tbScan.Enabled = false;
                    }
                    else
                    {
                        _stWinget.Text = "winget " + (v ?? "") ;
                        _stWinget.Image = Icons.Glyph(Icons.Package, 14, Theme.Success);
                        _stWinget.ToolTipText = exe;
                        Log(Tr.F("winget trovato: {0}{1}", "winget found: {0}{1}", exe,
                                 v != null ? "  (" + v + ")" : ""), LineKind.Normal);
                        if (_autoStart && _run.Enabled)
                        {
                            Log("", LineKind.Normal);
                            Log(Tr.T("Riavviato come amministratore: riprendo da dove eravamo.",
                                     "Restarted as administrator: picking up where we left off."), LineKind.Info);
                            StartRun(null);
                        }
                        else
                        {
                            ScanInstalledAsync();
                        }
                    }
                });
            });
            t.IsBackground = true;
            t.Start();
        }

        private void ApplyWindowPrefs()
        {
            try
            {
                if (_profile.WindowWidth >= MinimumSize.Width && _profile.WindowHeight >= MinimumSize.Height)
                {
                    Rectangle screen = Screen.FromControl(this).WorkingArea;
                    int w = Math.Min(_profile.WindowWidth, screen.Width);
                    int h = Math.Min(_profile.WindowHeight, screen.Height);
                    Size = new Size(w, h);
                    CenterToScreen();
                }
                if (_profile.WindowMaximized) WindowState = FormWindowState.Maximized;
                if (_profile.SplitTop > _vsplit.Panel1MinSize && _profile.SplitTop < _vsplit.Height - _vsplit.Panel2MinSize)
                    _vsplit.SplitterDistance = _profile.SplitTop;
                if (_profile.SplitLeft > _hsplit.Panel1MinSize && _profile.SplitLeft < _hsplit.Width - _hsplit.Panel2MinSize)
                    _hsplit.SplitterDistance = _profile.SplitLeft;
            }
            catch { }
        }

        private void StoreWindowPrefs()
        {
            try
            {
                _profile.WindowMaximized = WindowState == FormWindowState.Maximized;
                Size s = WindowState == FormWindowState.Normal ? Size : RestoreBounds.Size;
                _profile.WindowWidth = s.Width;
                _profile.WindowHeight = s.Height;
                _profile.SplitTop = _vsplit.SplitterDistance;
                _profile.SplitLeft = _hsplit.SplitterDistance;
            }
            catch { }
        }

        // =====================================================================
        //  lista e gruppi
        // =====================================================================

        private void RefreshAll()
        {
            RefreshNav();
            RefillList();
            UpdateCounts();
        }

        private void RefreshNav()
        {
            List<GroupInfo> groups = new List<GroupInfo>();
            foreach (string g in _profile.Groups)
            {
                GroupInfo gi = new GroupInfo();
                gi.Name = g;
                foreach (AppItem it in _profile.ItemsIn(g))
                {
                    gi.Total++;
                    if (it.Enabled) gi.Checked++;
                }
                groups.Add(gi);
            }
            _nav.SetGroups(groups, _profile.Items.Count, _profile.CountEnabled());
        }

        private bool PassesFilter(AppItem it)
        {
            string sel = _nav.Selected;
            if (sel != null && !string.Equals(it.Group, sel, StringComparison.OrdinalIgnoreCase)) return false;
            string q = _filter.Text.Trim().ToLowerInvariant();
            if (q.Length == 0) return true;
            if (it.Name.ToLowerInvariant().IndexOf(q, StringComparison.Ordinal) >= 0) return true;
            if (it.PackageId.ToLowerInvariant().IndexOf(q, StringComparison.Ordinal) >= 0) return true;
            if (it.Url.ToLowerInvariant().IndexOf(q, StringComparison.Ordinal) >= 0) return true;
            if (it.Group.ToLowerInvariant().IndexOf(q, StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        private void RefillList()
        {
            _suspendCheck = true;
            _list.BeginUpdate();
            _list.Items.Clear();
            _list.Groups.Clear();
            _rows.Clear();

            int shown = 0;
            foreach (string g in _profile.Groups)
            {
                List<AppItem> inGroup = _profile.ItemsIn(g);
                inGroup.Sort(delegate(AppItem a, AppItem b)
                {
                    return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                });

                ListViewGroup lvg = null;
                foreach (AppItem it in inGroup)
                {
                    if (!PassesFilter(it)) continue;
                    if (lvg == null)
                    {
                        lvg = new ListViewGroup(Groups.Show(g), HorizontalAlignment.Left);
                        _list.Groups.Add(lvg);
                    }
                    ListViewItem lvi = new ListViewItem(it.Name);
                    lvi.ImageKey = it.IsWinget() ? "winget" : "url";
                    lvi.SubItems.Add(it.SourceLabel());
                    lvi.SubItems.Add(it.DetailLabel());
                    lvi.SubItems.Add(it.VersionLabel());
                    lvi.SubItems.Add("");
                    lvi.UseItemStyleForSubItems = false;
                    lvi.Tag = it;
                    lvi.Checked = it.Enabled;
                    lvi.Group = lvg;
                    lvi.ToolTipText = TipFor(it);
                    _list.Items.Add(lvi);
                    _rows[it] = lvi;
                    ApplyStatus(it);
                    shown++;
                }
            }

            _list.EndUpdate();
            _suspendCheck = false;

            bool empty = _profile.Items.Count == 0;
            _empty.Visible = empty;
            _list.Visible = !empty;
            if (empty) _empty.BringToFront(); else _list.BringToFront();

            string sel = _nav.Selected;
            _listTitle.Text = sel == null ? Tr.T("Programmi", "Programs") : Groups.Show(sel);
            _listSub.Left = _listTitle.Right + 6;
            if (_filter.Text.Trim().Length > 0)
                _listSub.Text = Tr.F("{0} corrispondono al filtro", "{0} match the filter", shown);
            else
                _listSub.Text = shown == 0 ? "" : Tr.F("{0} voci", "{0} entries", shown);

            FitColumns();
        }

        // La colonna "Stato" prende lo spazio che resta: e' quella che cambia e si legge di piu'.
        private void FitColumns()
        {
            if (_list.Columns.Count < 5) return;
            int total = _list.ClientSize.Width - 4;
            int fixedW = _list.Columns[1].Width + _list.Columns[2].Width + _list.Columns[3].Width;
            int name = Math.Max(170, (int)(total * 0.25));
            int state = total - fixedW - name;
            if (state < 180) { state = 180; name = Math.Max(140, total - fixedW - state); }
            _list.Columns[0].Width = name;
            _list.Columns[4].Width = state;
        }

        private static string TipFor(AppItem it)
        {
            string s = it.Name + Environment.NewLine;
            if (it.IsPlaceholder())
                s += Tr.T("Promemoria: winget non sa reinstallarlo e l'indirizzo dell'installer non c'e' ancora."
                        + " Doppio clic per aggiungerlo; tasto destro per cercare il sito nel browser.",
                          "Reminder: winget cannot reinstall it and the installer address is not there yet."
                        + " Double-click to add it; right-click to search the web for it.");
            else
                s += it.IsWinget() ? Tr.F("Identificativo: {0}", "Identifier: {0}", it.PackageId)
                                   : Tr.F("Indirizzo: {0}", "Address: {0}", it.Url);
            if (!it.IsWinget())
            {
                s += Environment.NewLine + Tr.F("Installazione muta: {0}", "Silent install: {0}",
                     string.IsNullOrEmpty(it.SilentArgs) ? DirectUrl.SilentNoneLabel : it.SilentArgs);
            }
            if (!string.IsNullOrEmpty(it.Note))
                s += Environment.NewLine + Tr.F("Note: {0}", "Notes: {0}", it.Note);
            if (!string.IsNullOrEmpty(it.LastRunOn))
                s += Environment.NewLine + Tr.F("Ultima esecuzione: {0} - {1}", "Last run: {0} - {1}",
                                                it.LastRunOn, OutcomeLabel(it.LastOutcome));
            if (!it.IsWinget() && it.UpdateAvailable)
                s += Environment.NewLine + Tr.T("Il file all'indirizzo e' cambiato rispetto all'ultima verifica",
                                                "The file at the address changed since the last check");
            if (it.IsWinget() && it.Version.Length > 0)
                s += Environment.NewLine + Tr.F("Versione a catalogo: {0}", "Catalog version: {0}", it.Version);
            if (it.IsWinget() && it.InstalledKnown && it.Installed && it.InstalledVersion.Length > 0)
                s += Environment.NewLine + Tr.F("Installata su questo PC: v{0}{1}",
                                                "Installed on this PC: v{0}{1}", it.InstalledVersion,
                     it.Upgradable() ? Tr.T("  (a catalogo ce n'e' una piu' nuova)", "  (the catalog has a newer one)") : "");
            return s;
        }

        // Il testo della colonna Stato e il suo colore: un solo posto che decide cosa
        // si dice di una voce, cosi' lista e console raccontano la stessa storia.
        private void ApplyStatus(AppItem it)
        {
            ListViewItem lvi;
            if (!_rows.TryGetValue(it, out lvi)) return;

            string text;
            Color color;

            if (!string.IsNullOrEmpty(it.LiveStatus))
            {
                text = it.LiveStatus;
                color = Theme.Accent;
            }
            else if (it.IsWinget())
            {
                if (it.InstalledKnown)
                {
                    if (it.Installed && it.Upgradable())
                    {
                        text = Tr.F("\u2713 installato  \u00B7  \u2191 v{0} disponibile",
                                    "\u2713 installed  \u00B7  \u2191 v{0} available", it.Version);
                        color = Theme.Warning;
                    }
                    else if (it.Installed)
                    {
                        text = Tr.T("\u2713 installato", "\u2713 installed")
                             + (it.InstalledVersion.Length > 0 ? "  v" + it.InstalledVersion : "");
                        color = Theme.Success;
                    }
                    else
                    {
                        text = Tr.T("da installare", "to install");
                        color = Theme.TextSecondary;
                    }
                }
                else if (it.LastOutcome == "ERRORE")
                {
                    text = Tr.F("\u2717 errore il {0}", "\u2717 failed on {0}", it.LastRunOn);
                    color = Theme.Danger;
                }
                else
                {
                    text = _scanning ? Tr.T("controllo...", "checking...") : "";
                    color = Theme.TextDisabled;
                }
            }
            else if (it.IsPlaceholder())
            {
                text = Tr.T("\u26A0 manca l'indirizzo: doppio clic per aggiungerlo",
                            "\u26A0 address missing: double-click to add it");
                color = Theme.Warning;
            }
            else
            {
                if (it.LastOutcome == "OK")
                {
                    text = Tr.F("\u2713 installato il {0}", "\u2713 installed on {0}", it.LastRunOn);
                    color = Theme.Success;
                }
                else if (it.LastOutcome == "ERRORE")
                {
                    text = Tr.F("\u2717 errore il {0}", "\u2717 failed on {0}", it.LastRunOn);
                    color = Theme.Danger;
                }
                else
                {
                    text = Tr.T("da scaricare", "to download");
                    color = Theme.TextSecondary;
                }
                if (it.UpdateAvailable)
                {
                    text += Tr.T("  \u00B7  \u2191 file nuovo", "  \u00B7  \u2191 new file");
                    color = Theme.Warning;
                }
            }

            lvi.SubItems[4].Text = text;
            lvi.SubItems[4].ForeColor = color;
            lvi.SubItems[3].Text = it.VersionLabel();
            lvi.ToolTipText = TipFor(it);
        }

        private void List_ItemChecked(object sender, ItemCheckedEventArgs e)
        {
            if (_suspendCheck) return;
            AppItem it = e.Item.Tag as AppItem;
            if (it == null) return;
            it.Enabled = e.Item.Checked;
            RefreshNav();
            UpdateCounts();
            QueueSave();
        }

        private void Nav_GroupToggled(string group, bool on)
        {
            foreach (AppItem it in _profile.Items)
            {
                if (group != null && !string.Equals(it.Group, group, StringComparison.OrdinalIgnoreCase)) continue;
                it.Enabled = on;
            }
            _suspendCheck = true;
            foreach (KeyValuePair<AppItem, ListViewItem> kv in _rows) kv.Value.Checked = kv.Key.Enabled;
            _suspendCheck = false;
            RefreshNav();
            UpdateCounts();
            QueueSave();
        }

        private void SetSelectedChecked(bool on)
        {
            _suspendCheck = true;
            foreach (ListViewItem lvi in _list.SelectedItems)
            {
                AppItem it = lvi.Tag as AppItem;
                if (it == null) continue;
                it.Enabled = on;
                lvi.Checked = on;
            }
            _suspendCheck = false;
            RefreshNav();
            UpdateCounts();
            QueueSave();
        }

        private void UpdateCounts()
        {
            int n = 0, todo = 0;
            foreach (AppItem it in _profile.Items)
            {
                if (!it.Enabled) continue;
                if (it.IsPlaceholder()) todo++; else n++;
            }
            _run.Text = n == 0 ? Tr.T("Avvia installazioni", "Start installs")
                               : Tr.F("Avvia installazioni  ({0})", "Start installs  ({0})", n);
            _stCount.Text = Tr.F("{0} in lista, {1} da installare", "{0} listed, {1} to install",
                                 _profile.Items.Count, n)
                          + (todo > 0 ? Tr.F(", {0} senza indirizzo", ", {0} without an address", todo) : "");
        }

        private AppItem SelectedItem()
        {
            if (_list.SelectedItems.Count == 0) return null;
            return _list.SelectedItems[0].Tag as AppItem;
        }

        private List<AppItem> SelectedItems()
        {
            List<AppItem> l = new List<AppItem>();
            foreach (ListViewItem lvi in _list.SelectedItems)
            {
                AppItem it = lvi.Tag as AppItem;
                if (it != null) l.Add(it);
            }
            return l;
        }

        private string CurrentGroupForAdd()
        {
            if (_nav.Selected != null) return _nav.Selected;
            AppItem it = SelectedItem();
            if (it != null) return it.Group;
            return Groups.General;
        }

        // =====================================================================
        //  comandi
        // =====================================================================

        private void DoSearch()
        {
            if (!Winget.IsAvailable()) { NoWinget(); return; }
            HashSet<string> have = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (AppItem it in _profile.Items) if (it.IsWinget()) have.Add(it.PackageId);

            using (SearchForm f = new SearchForm(_profile.Groups, CurrentGroupForAdd(), have))
            {
                f.ShowDialog(this);
                AddItems(f.Chosen, Tr.T("dal catalogo", "from the catalog"));
            }
        }

        private void DoScan()
        {
            if (!Winget.IsAvailable()) { NoWinget(); return; }
            HashSet<string> haveIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> haveNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (AppItem it in _profile.Items)
            {
                if (it.IsWinget()) haveIds.Add(it.PackageId);
                else haveNames.Add(it.Name);
            }

            using (ImportForm f = new ImportForm(haveIds, haveNames, _profile.Groups, CurrentGroupForAdd()))
            {
                DialogResult r = f.ShowDialog(this);
                if (f.NotReinstallableCount > 0)
                    Log(Tr.F("{0} programmi sul PC non sono reinstallabili da winget: li trovi nella scheda "
                           + "\"Non reinstallabili\" di \"Leggi da questo PC\", per aggiungere un indirizzo o un promemoria.",
                             "{0} programs on this PC cannot be reinstalled by winget: you find them in the "
                           + "\"Not reinstallable\" tab of \"Read from this PC\", to add an address or a reminder.",
                             f.NotReinstallableCount), LineKind.Normal);
                if (r != DialogResult.OK) return;
                AddItems(f.Chosen, Tr.T("dalla lettura del PC", "from reading the PC"));
            }
        }

        private void DoAddUrl()
        {
            using (UrlForm f = new UrlForm(_profile.Groups, null, CurrentGroupForAdd()))
            {
                if (f.ShowDialog(this) != DialogResult.OK) return;
                List<AppItem> one = new List<AppItem>();
                one.Add(f.Item);
                AddItems(one, Tr.T("da indirizzo", "from an address"));
            }
        }

        private void AddItems(List<AppItem> items, string how)
        {
            int added = 0, dup = 0;
            foreach (AppItem it in items)
            {
                if (_profile.Contains(it)) { dup++; continue; }
                it.Group = _profile.CanonicalGroup(it.Group);
                _profile.EnsureGroup(it.Group);
                _profile.Items.Add(it);
                added++;
                if (it.IsPlaceholder())
                    Log(Tr.F("promemoria {0}: {1}  (manca l'indirizzo: doppio clic sulla voce per aggiungerlo)",
                             "reminder {0}: {1}  (address missing: double-click the entry to add it)", how, it.Name), LineKind.Warn);
                else
                    Log(Tr.F("aggiunto {0}: {1}  [{2}]", "added {0}: {1}  [{2}]", how, it.Name,
                             it.IsWinget() ? it.PackageId : it.Url), LineKind.Good);
            }
            if (dup > 0) Log(Tr.F("{0} gia' in lista, non duplicate.", "{0} already listed, not duplicated.", dup), LineKind.Warn);
            if (added > 0)
            {
                RefreshAll();
                DoSave(false);
                ScanInstalledAsync();
            }
        }

        private void EditSelected()
        {
            AppItem item = SelectedItem();
            if (item == null) return;

            if (!item.IsWinget())
            {
                using (UrlForm f = new UrlForm(_profile.Groups, item, item.Group))
                {
                    if (item.IsPlaceholder()) f.Text = Tr.F("Aggiungi l'indirizzo di {0}", "Add the address of {0}", item.Name);
                    if (f.ShowDialog(this) != DialogResult.OK) return;
                    int idx = _profile.Items.IndexOf(item);
                    f.Item.Group = _profile.CanonicalGroup(f.Item.Group);
                    if (idx >= 0) _profile.Items[idx] = f.Item;
                    _profile.EnsureGroup(f.Item.Group);
                    if (item.IsPlaceholder() && !f.Item.IsPlaceholder())
                        Log(Tr.F("{0}: indirizzo aggiunto, ora si puo' installare{1}.",
                                 "{0}: address added, it can be installed now{1}.", f.Item.Name,
                                 f.Item.Group == Groups.Todo
                                     ? Tr.F(" (e' ancora nel gruppo \"{0}\": spostalo se vuoi)",
                                            " (still in the \"{0}\" group: move it if you like)", Groups.Show(Groups.Todo))
                                     : ""), LineKind.Good);
                    RefreshAll();
                    DoSave(false);
                }
            }
            else
            {
                string newName = Prompt(Tr.T("Modifica voce", "Edit entry"),
                                        Tr.F("Nome mostrato per {0}:", "Name shown for {0}:", item.PackageId), item.Name);
                if (!string.IsNullOrEmpty(newName)) { item.Name = newName; RefreshAll(); DoSave(false); }
            }
        }

        private void DoNewGroup()
        {
            string name = Prompt(Tr.T("Nuovo gruppo", "New group"), Tr.T("Nome del gruppo:", "Group name:"), "");
            if (string.IsNullOrEmpty(name)) return;
            _profile.EnsureGroup(name);
            RefreshAll();
            DoSave(false);
            _nav.Select(_profile.CanonicalGroup(name));
        }

        private void DoRenameGroup(string oldName)
        {
            string newName = Prompt(Tr.T("Rinomina il gruppo", "Rename the group"), Tr.T("Nuovo nome:", "New name:"), Groups.Show(oldName));
            if (string.IsNullOrEmpty(newName) || newName == oldName) return;
            for (int i = 0; i < _profile.Groups.Count; i++)
                if (_profile.Groups[i] == oldName) _profile.Groups[i] = newName;
            foreach (AppItem it in _profile.Items)
                if (it.Group == oldName) it.Group = newName;
            RefreshAll();
            DoSave(false);
        }

        private void DoDeleteGroup(string g)
        {
            int count = _profile.ItemsIn(g).Count;
            string msg = count == 0
                ? Tr.F("Elimino il gruppo \"{0}\"?", "Delete the \"{0}\" group?", Groups.Show(g))
                : Tr.F("Il gruppo \"{0}\" contiene {1} voci." + Environment.NewLine
                     + "Le elimino insieme al gruppo? (No = sposto le voci in \"{2}\")",
                       "The \"{0}\" group holds {1} entries." + Environment.NewLine
                     + "Delete them along with the group? (No = move them to \"{2}\")",
                       Groups.Show(g), count, Groups.Show(Groups.General));
            DialogResult r = MessageBox.Show(this, msg, AppInfo.Name,
                count == 0 ? MessageBoxButtons.YesNo : MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (r == DialogResult.Cancel) return;
            if (r == DialogResult.No && count == 0) return;

            if (r == DialogResult.Yes)
            {
                _profile.Items.RemoveAll(delegate(AppItem it)
                {
                    return string.Equals(it.Group, g, StringComparison.OrdinalIgnoreCase);
                });
            }
            else
            {
                _profile.EnsureGroup(Groups.General);
                foreach (AppItem it in _profile.Items)
                    if (string.Equals(it.Group, g, StringComparison.OrdinalIgnoreCase)) it.Group = Groups.General;
            }
            _profile.Groups.Remove(g);
            RefreshAll();
            DoSave(false);
        }

        private void DoRemoveSelected()
        {
            List<AppItem> sel = SelectedItems();
            if (sel.Count == 0) return;
            string msg = sel.Count == 1
                ? Tr.F("Tolgo \"{0}\" dalla lista?", "Remove \"{0}\" from the list?", sel[0].Name)
                : Tr.F("Tolgo {0} voci dalla lista?", "Remove {0} entries from the list?", sel.Count);
            if (MessageBox.Show(this, msg, AppInfo.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            foreach (AppItem it in sel) _profile.Items.Remove(it);
            RefreshAll();
            DoSave(false);
        }

        private void MoveSelectedTo(string group)
        {
            foreach (AppItem it in SelectedItems()) it.Group = group;
            RefreshAll();
            DoSave(false);
        }

        private void DoClearAll()
        {
            if (_profile.Items.Count == 0) return;
            if (MessageBox.Show(this, Tr.F("Svuoto tutta la lista ({0} voci)? I gruppi restano.",
                                           "Empty the whole list ({0} entries)? The groups stay.", _profile.Items.Count),
                    AppInfo.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            _profile.Items.Clear();
            RefreshAll();
            DoSave(false);
            Log(Tr.T("Lista svuotata.", "List emptied."), LineKind.Warn);
        }

        private void QueueSave()
        {
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        private void DoSave(bool loud)
        {
            StoreWindowPrefs();
            try
            {
                Storage.Save(_profile);
                if (loud) Log(Tr.F("Profilo salvato in {0}", "Profile saved to {0}", Storage.ProfilePath()), LineKind.Good);
                _stProfile.Text = Storage.ProfilePath() + Tr.F("   (salvato {0})", "   (saved {0})",
                                                               DateTime.Now.ToString("HH:mm"));
            }
            catch (Exception ex)
            {
                Log(Tr.F("Non sono riuscito a salvare il profilo: {0}", "Could not save the profile: {0}", ex.Message), LineKind.Error);
                if (loud)
                    MessageBox.Show(this, ex.Message, Tr.T("Salvataggio non riuscito", "Save failed"),
                                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void DoSaveAs()
        {
            using (SaveFileDialog d = new SaveFileDialog())
            {
                d.Title = Tr.T("Salva il profilo con nome", "Save profile as");
                d.Filter = ProfileFilter(false);
                d.FileName = "LHInstaller-" + Environment.MachineName + ".json";
                d.InitialDirectory = Storage.DataDir();
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    Storage.Save(_profile, d.FileName);
                    Log(Tr.F("Profilo salvato in {0}", "Profile saved to {0}", d.FileName), LineKind.Good);
                }
                catch (Exception ex) { Log(Tr.F("Salvataggio non riuscito: {0}", "Save failed: {0}", ex.Message), LineKind.Error); }
            }
        }

        private void DoLoadFrom()
        {
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Title = Tr.T("Carica un profilo", "Load a profile");
                d.Filter = ProfileFilter(true);
                d.InitialDirectory = Storage.DataDir();
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    _profile = Storage.Import(d.FileName);
                    _optSkip.Checked = _profile.SkipInstalled;
                    _optContinue.Checked = _profile.ContinueOnError;
                    _optWindows.Checked = _profile.ShowInstallerWindows;
                    RefreshAll();
                    DoSave(false);
                    Log(Tr.F("Profilo caricato da {0} ({1} voci).", "Profile loaded from {0} ({1} entries).",
                             d.FileName, _profile.Items.Count), LineKind.Good);
                    ScanInstalledAsync();
                }
                catch (Exception ex) { Log(Tr.F("Profilo non leggibile: {0}", "Profile not readable: {0}", ex.Message), LineKind.Error); }
            }
        }

        private void DoBackup()
        {
            using (SaveFileDialog d = new SaveFileDialog())
            {
                d.Title = Tr.T("Backup completo", "Full backup");
                d.Filter = ProfileFilter(false);
                d.FileName = Storage.DefaultBackupName();
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    StoreWindowPrefs();
                    Storage.Save(_profile, d.FileName);
                    Log(Tr.F("Backup completo salvato: {0}", "Full backup saved: {0}", d.FileName), LineKind.Good);
                    Log("  " + Tr.T("contiene gruppi, programmi, indirizzi, argomenti e preferenze.",
                                    "it holds groups, programs, addresses, arguments and preferences."), LineKind.Normal);
                }
                catch (Exception ex) { Log(Tr.F("Backup non riuscito: {0}", "Backup failed: {0}", ex.Message), LineKind.Error); }
            }
        }

        private void DoRestore()
        {
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Title = Tr.T("Ripristina un backup completo", "Restore a full backup");
                d.Filter = ProfileFilter(true);
                if (d.ShowDialog(this) != DialogResult.OK) return;

                DialogResult r = MessageBox.Show(this,
                    Tr.T("Come vuoi ripristinare?" + Environment.NewLine + Environment.NewLine +
                         "Si'  =  unisci al profilo attuale (tiene quello che c'e' gia')" + Environment.NewLine +
                         "No  =  sostituisci tutto con il backup" + Environment.NewLine +
                         "Annulla  =  lascia stare",
                         "How do you want to restore?" + Environment.NewLine + Environment.NewLine +
                         "Yes  =  merge into the current profile (keeps what is already there)" + Environment.NewLine +
                         "No  =  replace everything with the backup" + Environment.NewLine +
                         "Cancel  =  leave it alone"),
                    AppInfo.Name, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (r == DialogResult.Cancel) return;

                try
                {
                    Profile loaded = Storage.Import(d.FileName);
                    if (r == DialogResult.No)
                    {
                        _profile = loaded;
                        Log(Tr.F("Profilo sostituito con il backup ({0} voci).",
                                 "Profile replaced with the backup ({0} entries).", loaded.Items.Count), LineKind.Good);
                    }
                    else
                    {
                        int added = 0;
                        foreach (string g in loaded.Groups) _profile.EnsureGroup(g);
                        foreach (AppItem it in loaded.Items)
                        {
                            if (_profile.Contains(it)) continue;
                            it.Group = _profile.CanonicalGroup(it.Group);
                            _profile.EnsureGroup(it.Group);
                            _profile.Items.Add(it);
                            added++;
                        }
                        Log(Tr.F("Backup unito: {0} voci nuove.", "Backup merged: {0} new entries.", added), LineKind.Good);
                    }
                    _optSkip.Checked = _profile.SkipInstalled;
                    _optContinue.Checked = _profile.ContinueOnError;
                    _optWindows.Checked = _profile.ShowInstallerWindows;
                    RefreshAll();
                    DoSave(false);
                    ScanInstalledAsync();
                }
                catch (Exception ex) { Log(Tr.F("Ripristino non riuscito: {0}", "Restore failed: {0}", ex.Message), LineKind.Error); }
            }
        }

        private void DoImportWinget()
        {
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Title = Tr.T("Importa un file creato con \"winget export\"", "Import a file made with \"winget export\"");
                d.Filter = Tr.T("File winget (*.json)|*.json|Tutti i file (*.*)|*.*",
                                "winget files (*.json)|*.json|All files (*.*)|*.*");
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    List<AppItem> items = WingetFormat.Import(d.FileName);
                    if (items.Count == 0)
                    {
                        Log(Tr.T("Nel file non c'e' nessun pacchetto del catalogo winget.",
                                 "The file holds no package from the winget catalog."), LineKind.Warn);
                        return;
                    }
                    AddItems(items, Tr.T("da file winget", "from a winget file"));
                    Log(Tr.T("I nomi sono ricavati dall'identificativo: \"Controlla aggiornamenti\" li completa con la versione.",
                             "Names come from the identifier: \"Check for updates\" fills in the version."), LineKind.Normal);
                }
                catch (Exception ex) { Log(Tr.F("Importazione non riuscita: {0}", "Import failed: {0}", ex.Message), LineKind.Error); }
            }
        }

        private void DoExportWinget()
        {
            int n = 0;
            foreach (AppItem it in _profile.Items) if (it.IsWinget()) n++;
            if (n == 0) { Log(Tr.T("Non ci sono pacchetti del catalogo da esportare.",
                                   "There are no catalog packages to export."), LineKind.Warn); return; }

            using (SaveFileDialog d = new SaveFileDialog())
            {
                d.Title = Tr.T("Esporta in formato winget", "Export in winget format");
                d.Filter = Tr.T("File winget (*.json)|*.json", "winget files (*.json)|*.json");
                d.FileName = "winget-packages-" + Environment.MachineName + ".json";
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    WingetFormat.Export(_profile.Items, d.FileName);
                    Log(Tr.F("{0} pacchetti esportati in {1}", "{0} packages exported to {1}", n, d.FileName), LineKind.Good);
                    Log("  " + Tr.F("si reinstallano anche da terminale:  winget import -i \"{0}\"",
                                    "they also reinstall from a terminal:  winget import -i \"{0}\"", d.FileName), LineKind.Normal);
                }
                catch (Exception ex) { Log(Tr.F("Esportazione non riuscita: {0}", "Export failed: {0}", ex.Message), LineKind.Error); }
            }
        }

        private void OpenDataDir()
        {
            try { Process.Start("explorer.exe", "\"" + Storage.DataDir() + "\""); }
            catch (Exception ex) { Log(Tr.F("Non riesco ad aprire la cartella: {0}", "Cannot open the folder: {0}", ex.Message), LineKind.Error); }
        }

        private void OpenUrl(string url)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(url);
                psi.UseShellExecute = true;
                Process.Start(psi);
            }
            catch (Exception ex) { Log(Tr.F("Non riesco ad aprire l'indirizzo: {0}", "Cannot open the address: {0}", ex.Message), LineKind.Error); }
        }

        private void NoWinget()
        {
            MessageBox.Show(this, Tr.F("winget non e' disponibile su questo PC: il catalogo non e' raggiungibile."
                    + Environment.NewLine + Environment.NewLine
                    + "Installa \"Programma di installazione app\" dal Microsoft Store e riapri {0}.",
                      "winget is not available on this PC: the catalog cannot be reached."
                    + Environment.NewLine + Environment.NewLine
                    + "Install \"App Installer\" from the Microsoft Store and reopen {0}.", AppInfo.Name),
                AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        // =====================================================================
        //  stato installato (colonna "Stato")
        // =====================================================================

        private void ScanInstalledAsync()
        {
            if (_scanning || _running || !Winget.IsAvailable()) return;
            bool any = false;
            foreach (AppItem it in _profile.Items) if (it.IsWinget()) { any = true; break; }
            if (!any) return;

            _scanning = true;
            foreach (AppItem it in _profile.Items) if (it.IsWinget() && !it.InstalledKnown) ApplyStatus(it);
            SetProgressText(Tr.T("Leggo lo stato dei programmi installati...", "Reading the state of installed programs..."));

            Thread t = new Thread(delegate()
            {
                Dictionary<string, string> installed = null;
                try { installed = Winget.ListInstalled(); }
                catch { }
                UI(delegate
                {
                    _scanning = false;
                    if (installed != null)
                    {
                        foreach (AppItem it in _profile.Items)
                        {
                            if (!it.IsWinget()) continue;
                            string v;
                            it.InstalledKnown = true;
                            it.Installed = installed.TryGetValue(it.PackageId, out v);
                            it.InstalledVersion = it.Installed ? (v ?? "") : "";
                        }
                    }
                    foreach (AppItem it in _profile.Items) ApplyStatus(it);
                    if (!_running) SetProgressText(Tr.T("Pronto.", "Ready."));
                });
            });
            t.IsBackground = true;
            t.Start();
        }

        // =====================================================================
        //  controllo aggiornamenti
        // =====================================================================

        private void DoCheckUpdates()
        {
            if (_running) return;
            List<AppItem> items = new List<AppItem>();
            foreach (AppItem it in _profile.Items) if (!it.IsPlaceholder()) items.Add(it);
            if (items.Count == 0) return;

            SetBusy(true, Tr.T("Controllo aggiornamenti", "Checking for updates"));
            _console.Rule(Tr.T("Controllo aggiornamenti", "Checking for updates"));
            _bar.Value = 0;
            _bar.Maximum = Math.Max(1, items.Count);

            Thread t = new Thread(delegate()
            {
                int changed = 0;
                for (int i = 0; i < items.Count; i++)
                {
                    if (_cancel) break;
                    AppItem it = items[i];
                    Step(i, items.Count, Tr.F("Controllo {0} di {1}  \u00B7  {2}",
                                              "Checking {0} of {1}  \u00B7  {2}", i + 1, items.Count, it.Name));
                    try
                    {
                        if (it.IsWinget())
                        {
                            string latest = Winget.LatestVersion(it.PackageId);
                            if (!string.IsNullOrEmpty(latest))
                            {
                                // La versione in lista segue il catalogo: e' quella che winget
                                // installerebbe oggi. Il confronto utile e' con quella installata.
                                string before = it.Version;
                                it.Version = latest;
                                it.LatestSeen = latest;
                                it.UpdateAvailable = false;
                                if (!string.IsNullOrEmpty(before) && !string.Equals(before, latest, StringComparison.OrdinalIgnoreCase))
                                {
                                    changed++;
                                    Log(Tr.F("{0}: a catalogo ora c'e' la {1} (in lista avevi la {2})",
                                             "{0}: the catalog now has {1} (your list had {2})", it.Name, latest, before), LineKind.Normal);
                                }
                                if (it.Upgradable())
                                    Log(Tr.F("{0}: installata la {1}, a catalogo c'e' la {2}",
                                             "{0}: {1} installed, the catalog has {2}", it.Name, it.InstalledVersion, latest), LineKind.Warn);
                            }
                            else
                            {
                                Log(Tr.F("{0}: non trovato a catalogo con l'identificativo {1}",
                                         "{0}: not found in the catalog under the identifier {1}", it.Name, it.PackageId), LineKind.Error);
                            }
                        }
                        else
                        {
                            DirectUrl.RemoteInfo info = DirectUrl.Probe(it.Url);
                            if (!info.Ok)
                            {
                                Log(Tr.F("{0}: indirizzo non raggiungibile ({1})",
                                         "{0}: address not reachable ({1})", it.Name, info.Error), LineKind.Error);
                            }
                            else if (info.DiffersFrom(it))
                            {
                                it.UpdateAvailable = true;
                                it.LatestSeen = DirectUrl.Human(info.ContentLength);
                                changed++;
                                Log(Tr.F("{0}: il file all'indirizzo e' cambiato ({1}{2})",
                                         "{0}: the file at the address changed ({1}{2})", it.Name,
                                         DirectUrl.Human(info.ContentLength),
                                         string.IsNullOrEmpty(info.LastModified) ? ""
                                             : Tr.F(", del {0}", ", dated {0}", info.LastModified)), LineKind.Warn);
                                it.ETag = info.ETag;
                                it.LastModified = info.LastModified;
                                it.ContentLength = info.ContentLength;
                            }
                            else
                            {
                                it.UpdateAvailable = false;
                                if (it.ContentLength == 0)
                                {
                                    it.ETag = info.ETag;
                                    it.LastModified = info.LastModified;
                                    it.ContentLength = info.ContentLength;
                                }
                            }
                            it.LastChecked = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log(Tr.F("{0}: controllo non riuscito ({1})", "{0}: check failed ({1})", it.Name, ex.Message), LineKind.Error);
                    }
                    AppItem done = it;
                    UI(delegate { ApplyStatus(done); });
                    Step(i + 1, items.Count, null);
                }

                int c = changed;
                UI(delegate
                {
                    Log(c == 0 ? Tr.T("Nessuna novita': e' tutto come l'avevi registrato.",
                                      "Nothing new: everything is as you recorded it.")
                               : Tr.F("{0} voci aggiornate (vedi Versione e Stato).",
                                      "{0} entries updated (see Version and Status).", c),
                        c == 0 ? LineKind.Good : LineKind.Warn);
                    RefreshAll();
                    DoSave(false);
                    SetBusy(false, null);
                    SetProgressText(c == 0 ? Tr.T("Nessun aggiornamento.", "No updates.")
                                           : Tr.F("{0} voci con novita'.", "{0} entries with news.", c));
                });
            });
            t.IsBackground = true;
            t.Start();
        }

        // =====================================================================
        //  installazione
        // =====================================================================

        private void StartRun(List<AppItem> only)
        {
            if (_running) return;

            List<AppItem> items = new List<AppItem>();
            int todo = 0;
            List<AppItem> source = only != null ? only : _profile.Items;
            foreach (AppItem it in source)
            {
                if (only == null && !it.Enabled) continue;
                if (it.IsPlaceholder()) { todo++; continue; }
                items.Add(it);
            }

            if (items.Count == 0)
            {
                MessageBox.Show(this, todo > 0
                    ? Tr.T("Le voci spuntate sono solo promemoria senza indirizzo: non c'e' niente da installare."
                         + Environment.NewLine + "Fai doppio clic su ognuna per aggiungere l'indirizzo dell'installer.",
                           "The ticked entries are only reminders without an address: there is nothing to install."
                         + Environment.NewLine + "Double-click each one to add the installer address.")
                    : Tr.T("Non c'e' nessun programma spuntato." + Environment.NewLine
                         + "Metti la spunta a quelli da installare, oppure a un intero gruppo nel pannello a sinistra.",
                           "No program is ticked." + Environment.NewLine
                         + "Tick the ones to install, or a whole group in the left panel."),
                    AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (todo > 0)
                Log(Tr.F("{0} promemoria senza indirizzo: li salto.",
                         "{0} reminders without an address: skipping them.", todo), LineKind.Warn);

            bool needsWinget = false;
            foreach (AppItem it in items) if (it.IsWinget()) { needsWinget = true; break; }
            if (needsWinget && !Winget.IsAvailable()) { NoWinget(); return; }

            // Installare software richiede i permessi di amministratore. Mi riavvio una
            // volta sola all'inizio, cosi' non compare una richiesta per ogni programma.
            if (!_elevated)
            {
                DoSave(false);
                if (MessageBox.Show(this,
                        Tr.F("Per installare servono i permessi di amministratore." + Environment.NewLine + Environment.NewLine
                           + "{0} si riavvia come amministratore e riparte da solo con l'installazione."
                           + Environment.NewLine + "Windows lo chiede una volta sola, adesso.",
                             "Administrator rights are needed to install." + Environment.NewLine + Environment.NewLine
                           + "{0} restarts as administrator and resumes the installation by itself."
                           + Environment.NewLine + "Windows asks once, now.", AppInfo.Name),
                        AppInfo.Name, MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK) return;

                if (only != null)
                {
                    // Una selezione parziale non sopravvive al riavvio: la traduco in spunte.
                    foreach (AppItem it in _profile.Items) it.Enabled = false;
                    foreach (AppItem it in only) it.Enabled = true;
                    DoSave(false);
                }

                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo(Application.ExecutablePath, "--avvia");
                    psi.UseShellExecute = true;
                    psi.Verb = "runas";
                    psi.WorkingDirectory = Storage.AppDir();
                    Process.Start(psi);
                    Application.Exit();
                }
                catch (Exception ex)
                {
                    Log(Tr.F("Riavvio come amministratore non riuscito: {0}",
                             "Restart as administrator failed: {0}", ex.Message), LineKind.Error);
                    Log(Tr.F("Puoi chiudere e riaprire {0} con il tasto destro, \"Esegui come amministratore\".",
                             "You can close and reopen {0} with right-click, \"Run as administrator\".", AppInfo.Name), LineKind.Warn);
                }
                return;
            }

            DoSave(false);
            SetBusy(true, Tr.T("Installazione in corso", "Installing"));
            _bar.Value = 0;
            _bar.Maximum = items.Count;
            _console.Rule(Tr.F("Installazione di {0} programmi", "Installing {0} programs", items.Count));
            Log(Tr.F("opzioni: {0}, {1}, {2}", "options: {0}, {1}, {2}",
                _profile.SkipInstalled ? Tr.T("salto gli installati", "skipping the installed ones")
                                       : Tr.T("reinstallo anche gli installati", "reinstalling even the installed ones"),
                _profile.ContinueOnError ? Tr.T("continuo dopo un errore", "carrying on after an error")
                                         : Tr.T("mi fermo al primo errore", "stopping at the first error"),
                _profile.ShowInstallerWindows ? Tr.T("finestre visibili", "windows visible")
                                              : Tr.T("silenzioso", "silent")), LineKind.Normal);

            foreach (AppItem it in items) { it.LiveStatus = Waiting; ApplyStatus(it); }

            Thread t = new Thread(delegate() { RunWorker(items); });
            t.IsBackground = true;
            t.Start();
        }

        private void RunWorker(List<AppItem> items)
        {
            List<InstallOutcome> outcomes = new List<InstallOutcome>();
            bool skipInstalled = _profile.SkipInstalled;
            bool keepGoing = _profile.ContinueOnError;
            bool interactive = _profile.ShowInstallerWindows;

            for (int i = 0; i < items.Count; i++)
            {
                if (_cancel) break;
                AppItem it = items[i];

                Log("", LineKind.Normal);
                Log("[" + (i + 1) + "/" + items.Count + "] " + it.Name, LineKind.Info);
                Step(i, items.Count, Tr.F("{0} di {1}  \u00B7  {2}", "{0} of {1}  \u00B7  {2}", i + 1, items.Count, it.Name));
                SetLive(it, Tr.T("\u25CF in corso...", "\u25CF running..."));

                InstallOutcome o;
                try
                {
                    o = it.IsWinget() ? RunWingetItem(it, skipInstalled, interactive) : RunUrlItem(it, interactive);
                }
                catch (Exception ex)
                {
                    o = new InstallOutcome();
                    o.Item = it;
                    o.Status = "ERRORE";
                    o.Detail = ex.Message;
                    Log("  errore: " + ex.Message, LineKind.Error);
                }
                outcomes.Add(o);

                it.LastOutcome = o.Status;
                it.LastRunOn = DateTime.Now.ToString("dd/MM HH:mm");
                if (o.Status == "OK" || o.Status == "GIA' PRESENTE")
                {
                    it.InstalledKnown = true;
                    it.Installed = true;
                    it.UpdateAvailable = false;
                    if (o.Status == "OK" && it.IsWinget() && !string.IsNullOrEmpty(it.LatestSeen)) it.Version = it.LatestSeen;
                }
                SetLive(it, null);
                Step(i + 1, items.Count, null);

                if (!o.Success && !keepGoing && !_cancel)
                {
                    Log("", LineKind.Normal);
                    Log(Tr.T("Mi fermo qui: hai chiesto di non proseguire dopo un errore.",
                             "Stopping here: you asked not to carry on after an error."), LineKind.Warn);
                    break;
                }
            }

            foreach (AppItem it in items) if (it.LiveStatus == Waiting) it.LiveStatus = "";

            List<InstallOutcome> final = outcomes;
            UI(delegate { RunDone(final, items.Count); });
        }

        private void SetLive(AppItem it, string status)
        {
            it.LiveStatus = status ?? "";
            UI(delegate { ApplyStatus(it); });
        }

        private InstallOutcome RunWingetItem(AppItem it, bool skipInstalled, bool interactive)
        {
            InstallOutcome o = new InstallOutcome();
            o.Item = it;

            if (skipInstalled)
            {
                SetLive(it, Tr.T("\u25CF controllo se c'e' gia'...", "\u25CF checking whether it is there..."));
                if (Winget.IsInstalled(it.PackageId))
                {
                    Log("  " + Tr.T("gia' presente sul PC, salto.", "already on the PC, skipping."), LineKind.Good);
                    o.Status = "GIA' PRESENTE";
                    o.Success = true;
                    return o;
                }
            }

            string args = Winget.InstallArgs(it.PackageId, interactive);
            Log("  winget " + args, LineKind.Info);
            SetLive(it, Tr.T("\u25CF scarico e installo...", "\u25CF downloading and installing..."));

            _current = new ProcessRunner();
            int code = _current.Run(Winget.ExePath(), args, delegate(string line, LineKind k)
            {
                Log("  " + line, k);
            });
            _current = null;

            if (_cancel)
            {
                o.Status = "INTERROTTO";
                o.Detail = Tr.T("interrotto da te", "stopped by you");
                Log("  " + Tr.T("interrotto.", "stopped."), LineKind.Warn);
                return o;
            }

            o.Success = Winget.IsAcceptable(code);
            o.Detail = Winget.DescribeExitCode(code);
            o.Status = o.Success ? "OK" : "ERRORE";
            Log("  " + Tr.F("esito: {0}", "outcome: {0}", o.Detail), o.Success ? LineKind.Good : LineKind.Error);
            if (Winget.NeedsReboot(code))
                Log("  " + Tr.T("(ricordati di riavviare il PC alla fine)", "(remember to restart the PC at the end)"), LineKind.Warn);
            return o;
        }

        private InstallOutcome RunUrlItem(AppItem it, bool interactive)
        {
            InstallOutcome o = new InstallOutcome();
            o.Item = it;

            Log("  " + Tr.F("scarico da {0}", "downloading from {0}", it.Url), LineKind.Info);
            SetLive(it, Tr.T("\u25CF scarico...", "\u25CF downloading..."));

            DateTime last = DateTime.MinValue;
            string path = DirectUrl.Download(it.Url, Storage.DownloadDir(),
                delegate(long done, long total)
                {
                    // Una riga di avanzamento al secondo: di piu' allagherebbe la console.
                    DateTime now = DateTime.Now;
                    if ((now - last).TotalMilliseconds < 1000) return;
                    last = now;
                    string p = total > 0
                        ? Tr.F("{0} di {1}  ({2}%)", "{0} of {1}  ({2}%)",
                               DirectUrl.Human(done), DirectUrl.Human(total), done * 100 / total)
                        : Tr.F("{0} scaricati", "{0} downloaded", DirectUrl.Human(done));
                    SetProgressText(Tr.F("Scarico {0}  \u00B7  {1}", "Downloading {0}  \u00B7  {1}", it.Name, p));
                    SetLive(it, Tr.F("\u25CF scarico {0}", "\u25CF downloading {0}",
                                     total > 0 ? (done * 100 / total) + "%" : DirectUrl.Human(done)));
                },
                delegate(string l, LineKind k) { Log(l, k); },
                delegate() { return _cancel; });

            if (path == null)
            {
                o.Status = "INTERROTTO";
                o.Detail = Tr.T("download interrotto", "download stopped");
                Log("  " + Tr.T("download interrotto.", "download stopped."), LineKind.Warn);
                return o;
            }

            string silent = it.SilentArgs;
            if (interactive)
            {
                silent = "";
                Log("  " + Tr.T("hai chiesto le finestre visibili: apro l'installer senza argomenti.",
                                "you asked for visible windows: opening the installer with no arguments."), LineKind.Normal);
            }
            else if (silent == DirectUrl.SilentAuto)
            {
                DirectUrl.InstallerKind kind = DirectUrl.Detect(path);
                if (kind.Known)
                {
                    silent = kind.Args;
                    Log("  " + Tr.F("riconosciuto: {0}  ->  {1}", "recognised: {0}  ->  {1}", kind.Label, kind.Args), LineKind.Good);
                }
                else
                {
                    silent = "";
                    Log("  " + Tr.T("famiglia dell'installer non riconosciuta: apro la finestra, l'installazione la concludi tu.",
                                    "installer family not recognised: opening the window, you finish the install."), LineKind.Warn);
                }
            }

            SetLive(it, Tr.T("\u25CF installo...", "\u25CF installing..."));
            int code = DirectUrl.RunInstaller(path, silent, delegate(string l, LineKind k) { Log(l, k); });

            o.Success = code == 0 || code == 3010 || code == 1641;
            o.Detail = code == 0 ? Tr.T("completato", "completed")
                     : (code == 3010 || code == 1641) ? Tr.T("completato, serve il riavvio", "completed, a restart is needed")
                     : code == 1602 ? Tr.T("annullato dall'utente", "cancelled by the user")
                     : Tr.F("l'installer ha restituito il codice {0}", "the installer returned code {0}", code);
            o.Status = o.Success ? "OK" : "ERRORE";
            Log("  " + Tr.F("esito: {0}", "outcome: {0}", o.Detail), o.Success ? LineKind.Good : LineKind.Error);
            return o;
        }

        private void RunDone(List<InstallOutcome> outcomes, int planned)
        {
            int ok = 0, already = 0, failed = 0, stopped = 0;
            foreach (InstallOutcome o in outcomes)
            {
                if (o.Status == "OK") ok++;
                else if (o.Status == "GIA' PRESENTE") already++;
                else if (o.Status == "INTERROTTO") stopped++;
                else failed++;
            }

            _console.Rule(Tr.T("Riepilogo", "Summary"));
            Log(Tr.F("installati:        {0}", "installed:        {0}", ok), LineKind.Good);
            Log(Tr.F("gia' presenti:     {0}", "already there:    {0}", already), LineKind.Normal);
            if (stopped > 0) Log(Tr.F("interrotti:        {0}", "stopped:          {0}", stopped), LineKind.Warn);
            if (failed > 0) Log(Tr.F("non riusciti:      {0}", "failed:           {0}", failed), LineKind.Error);
            int notTried = planned - outcomes.Count;
            if (notTried > 0) Log(Tr.F("non tentati:       {0}", "not attempted:    {0}", notTried), LineKind.Warn);

            if (failed > 0)
            {
                Log("", LineKind.Normal);
                Log(Tr.T("Non riusciti, uno per uno:", "Failures, one by one:"), LineKind.Error);
                foreach (InstallOutcome o in outcomes)
                    if (o.Status == "ERRORE")
                        Log("  - " + o.Item.Name + ": " + o.Detail, LineKind.Error);
                Log(Tr.T("Puoi premere di nuovo Avvia: quelli gia' installati vengono saltati. "
                       + "Se un installer fallisce in silenzio, prova con \"Mostra le finestre degli installer\".",
                         "You can press Start again: whatever is installed gets skipped. "
                       + "If an installer fails quietly, try \"Show installer windows\"."), LineKind.Normal);
            }

            try
            {
                string logPath = _console.SaveLog();
                Log("", LineKind.Normal);
                Log(Tr.F("Log della sessione: {0}", "Session log: {0}", logPath), LineKind.Normal);
            }
            catch { }

            foreach (AppItem it in _profile.Items) ApplyStatus(it);
            DoSave(false);
            SetBusy(false, null);
            _bar.Value = _bar.Maximum;

            string summary = Tr.F("Finito: {0} installati, {1} gia' presenti",
                                  "Done: {0} installed, {1} already there", ok, already);
            if (failed > 0) summary += Tr.F(", {0} non riusciti", ", {0} failed", failed);
            if (stopped > 0 || notTried > 0)
                summary += Tr.F(", {0} non completati", ", {0} not completed", stopped + notTried);
            SetProgressText(summary + ".");

            ScanInstalledAsync();
        }

        private void DoCancel()
        {
            _cancel = true;
            ProcessRunner r = _current;
            if (r != null) r.Kill();
            Log(Tr.T("Interruzione richiesta: mi fermo appena finisce il passo in corso.",
                     "Stop requested: I halt as soon as the current step ends."), LineKind.Warn);
            SetProgressText(Tr.T("Interrompo...", "Stopping..."));
        }

        // =====================================================================
        //  utilita'
        // =====================================================================

        private void SetBusy(bool busy, string what)
        {
            if (InvokeRequired) { BeginInvoke(new Action<bool, string>(SetBusy), busy, what); return; }

            _running = busy;
            if (busy) _cancel = false;

            _run.Enabled = !busy && (Winget.IsAvailable() || HasUrlItems());
            _stop.Enabled = busy;
            foreach (ToolStripItem item in _tools.Items)
                if (item != _tbHelp) item.Enabled = !busy;
            _optSkip.Enabled = !busy;
            _optContinue.Enabled = !busy;
            _optWindows.Enabled = !busy;
            _nav.Enabled = !busy;
            UseWaitCursor = busy;

            _consoleState.Text = busy
                ? "\u25CF " + what
                : Tr.T("output di winget e degli installer, riga per riga",
                       "output from winget and the installers, line by line");
            _consoleState.ForeColor = busy ? Theme.ConsoleGood : Theme.ConsoleDim;

            if (!busy) UpdateCounts();
        }

        private bool HasUrlItems()
        {
            foreach (AppItem it in _profile.Items) if (!it.IsWinget()) return true;
            return false;
        }

        private void Step(int done, int total, string text)
        {
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action<int, int, string>(Step), done, total, text); }
                catch { }
                return;
            }
            if (total > 0)
            {
                _bar.Maximum = total;
                _bar.Value = Math.Max(0, Math.Min(total, done));
            }
            if (text != null) _progressText.Text = text;
        }

        private void SetProgressText(string text)
        {
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action<string>(SetProgressText), text); }
                catch { }
                return;
            }
            _progressText.Text = text;
        }

        private void UI(Action a)
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                try { BeginInvoke(a); }
                catch { }
                return;
            }
            a();
        }

        private void Log(string text, LineKind kind)
        {
            _console.Write(text, kind);
        }

        // Lo stato "in attesa" e' un valore interno, non testo da mostrare: serve a
        // riconoscere le righe da ripulire a fine sequenza.
        private const string Waiting = "\u25CF ...";

        // Gli esiti sono salvati nel profilo in italiano (OK, ERRORE, INTERROTTO,
        // GIA' PRESENTE) e non vanno tradotti sul disco: si traducono qui, allo schermo.
        private static string OutcomeLabel(string outcome)
        {
            switch (outcome)
            {
                case "OK": return Tr.T("OK", "OK");
                case "ERRORE": return Tr.T("ERRORE", "FAILED");
                case "INTERROTTO": return Tr.T("INTERROTTO", "STOPPED");
                case "GIA' PRESENTE": return Tr.T("GIA' PRESENTE", "ALREADY THERE");
                default: return outcome;
            }
        }

        private static string ProfileFilter(bool withAll)
        {
            string p = Tr.F("Profili {0} (*.json)|*.json", "{0} profiles (*.json)|*.json", AppInfo.Name);
            return withAll ? p + Tr.T("|Tutti i file (*.*)|*.*", "|All files (*.*)|*.*") : p;
        }

        private static bool IsElevated()
        {
            try
            {
                using (WindowsIdentity id = WindowsIdentity.GetCurrent())
                    return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        private void MainForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.F) { _filter.Focus(); _filter.SelectAll(); e.Handled = true; }
            else if (e.Control && e.KeyCode == Keys.K) { if (!_running) DoSearch(); e.Handled = true; }
            else if (e.Control && e.KeyCode == Keys.S) { DoSave(true); e.Handled = true; }
            else if (e.KeyCode == Keys.F5) { if (!_running) DoCheckUpdates(); e.Handled = true; }
            else if (e.KeyCode == Keys.F1) { new HelpForm(0).ShowDialog(this); e.Handled = true; }
        }

        private void List_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Delete) { DoRemoveSelected(); e.Handled = true; }
            else if (e.KeyCode == Keys.Enter) { EditSelected(); e.Handled = true; }
            else if (e.Control && e.KeyCode == Keys.A) { foreach (ListViewItem lvi in _list.Items) lvi.Selected = true; e.Handled = true; }
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (_running)
            {
                DialogResult r = MessageBox.Show(this,
                    Tr.T("C'e' un'installazione in corso. Vuoi interromperla e uscire?",
                         "An installation is running. Stop it and quit?"),
                    AppInfo.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (r != DialogResult.Yes) { e.Cancel = true; return; }
                DoCancel();
            }
            _saveTimer.Stop();
            if (_closingForLanguage) return;   // gia' salvato prima di riaprire
            StoreWindowPrefs();
            try { Storage.Save(_profile); }
            catch { }
        }

        private string Prompt(string title, string label, string initial)
        {
            using (Form f = new Form())
            {
                f.Text = title;
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.StartPosition = FormStartPosition.CenterParent;
                f.MinimizeBox = false;
                f.MaximizeBox = false;
                f.ShowInTaskbar = false;
                f.ClientSize = new Size(440, 124);
                f.Font = Theme.UI;
                f.BackColor = Theme.WindowBg;

                Label l = new Label();
                l.Text = label;
                l.AutoSize = true;
                l.Location = new Point(16, 16);

                TextBox t = new TextBox();
                t.SetBounds(16, 42, 408, 26);
                t.Text = initial;
                t.SelectAll();

                Button ok = Theme.PrimaryButton("OK", null);
                ok.SetBounds(236, 82, 90, 30);
                ok.DialogResult = DialogResult.OK;

                Button cancel = Theme.FlatButton(Tr.T("Annulla", "Cancel"), null);
                cancel.SetBounds(334, 82, 90, 30);
                cancel.DialogResult = DialogResult.Cancel;

                f.Controls.AddRange(new Control[] { l, t, ok, cancel });
                f.AcceptButton = ok;
                f.CancelButton = cancel;
                f.AutoScaleDimensions = new SizeF(96F, 96F);
                f.AutoScaleMode = AutoScaleMode.Dpi;

                if (f.ShowDialog(this) != DialogResult.OK) return null;
                return t.Text.Trim();
            }
        }

        // Testo grigio di suggerimento dentro una casella vuota (EM_SETCUEBANNER).
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        private static void SetCueBanner(TextBox box, string text)
        {
            box.HandleCreated += delegate
            {
                try { SendMessage(box.Handle, 0x1501, (IntPtr)1, text); }
                catch { }
            };
        }
    }
}
