using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace LHInstaller
{
    // "Leggi da questo PC": due schede.
    //  - Reinstallabili da winget: quelli che si rimettono da soli; li spunti e vanno in lista.
    //  - Non reinstallabili: quelli che winget vede sul PC ma non sa rimettere (installati da
    //    un sito, con licenza...). Per ognuno puoi cercare una corrispondenza a catalogo,
    //    aggiungerlo con un indirizzo, aprire una ricerca nel browser, o metterlo in lista
    //    come promemoria "manca l'indirizzo", cosi' non te ne dimentichi.
    public class ImportForm : Form
    {
        private readonly HashSet<string> _haveIds;
        private readonly HashSet<string> _haveNames;
        private readonly List<string> _groups;
        private readonly string _defaultGroup;

        private TabControl _tabs;
        private TabPage _tabOk, _tabNo;

        // scheda 1
        private readonly ListView _list = new ListView();
        private readonly TextBox _filter = new TextBox();
        private readonly CheckBox _hideSystem = new CheckBox();
        private readonly CheckBox _hideHave = new CheckBox();
        private readonly Label _status1 = new Label();
        private readonly Dictionary<string, bool> _checks = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private bool _suspend;

        // scheda 2
        private readonly ListView _list2 = new ListView();
        private readonly TextBox _filter2 = new TextBox();
        private readonly CheckBox _showAll2 = new CheckBox();
        private readonly Label _status2 = new Label();
        private Button _btnMatch, _btnFromCatalog, _btnWithUrl, _btnWeb, _btnTodo;
        private readonly Dictionary<string, string> _matches = new Dictionary<string, string>();   // id riga -> PackageId
        private readonly Dictionary<string, string> _matchNames = new Dictionary<string, string>();
        private readonly Dictionary<string, string> _matchVersions = new Dictionary<string, string>();
        private readonly Dictionary<string, int> _matchStrength = new Dictionary<string, int>();
        private readonly HashSet<string> _done2 = new HashSet<string>();    // id riga gia' aggiunti
        private volatile bool _matching;
        private volatile bool _cancelMatch;

        // comune
        private readonly Label _statusAll = new Label();
        private Button _ok, _cancel;

        private List<Winget.FoundProgram> _all = new List<Winget.FoundProgram>();
        private readonly List<AppItem> _reinstallable = new List<AppItem>();
        private readonly List<Winget.FoundProgram> _others = new List<Winget.FoundProgram>();

        public List<AppItem> Chosen = new List<AppItem>();
        public int NotReinstallableCount;

        public ImportForm(HashSet<string> haveIds, HashSet<string> haveNames, List<string> groups, string defaultGroup)
        {
            _haveIds = haveIds ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _haveNames = haveNames ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _groups = groups ?? new List<string>();
            _defaultGroup = string.IsNullOrEmpty(defaultGroup) ? Groups.General : defaultGroup;

            SuspendLayout();
            Text = Tr.T("Leggi i programmi da questo PC", "Read the programs from this PC");
            Icon = Icons.AppIcon();
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(960, 640);
            MinimumSize = new Size(760, 500);
            Font = Theme.UI;
            BackColor = Theme.WindowBg;
            ShowInTaskbar = false;

            _tabs = new TabControl();
            _tabs.Font = Theme.UI;
            _tabs.Padding = new Point(14, 6);
            _tabs.SetBounds(16, 14, 928, 556);
            _tabs.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

            // Le pagine vanno dimensionate PRIMA di riempirle: gli ancoraggi dei figli si
            // calcolano sulla dimensione del momento, e una pagina appena creata e' 200x100.
            Size pageSize = new Size(920, 524);

            _tabOk = new TabPage(Tr.T("Reinstallabili da winget", "Reinstallable by winget"));
            _tabOk.BackColor = Theme.CardBg;
            _tabOk.Padding = new Padding(0);
            _tabOk.Size = pageSize;
            BuildTab1(_tabOk);

            _tabNo = new TabPage(Tr.T("Non reinstallabili da winget", "Not reinstallable by winget"));
            _tabNo.BackColor = Theme.CardBg;
            _tabNo.Padding = new Padding(0);
            _tabNo.Size = pageSize;
            BuildTab2(_tabNo);

            _tabs.TabPages.Add(_tabOk);
            _tabs.TabPages.Add(_tabNo);
            _tabs.SelectedIndexChanged += delegate { UpdateStatus(); };

            _statusAll.SetBounds(16, 590, 560, 22);
            _statusAll.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            _statusAll.ForeColor = Theme.TextSecondary;
            _statusAll.Text = Tr.T("Leggo l'elenco dei programmi installati...", "Reading the list of installed programs...");

            _cancel = Theme.FlatButton(Tr.T("Annulla", "Cancel"), null);
            _cancel.SetBounds(844, 584, 100, 34);
            _cancel.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            _cancel.DialogResult = DialogResult.Cancel;

            _ok = Theme.PrimaryButton(Tr.T("Aggiungi al profilo", "Add to the profile"), Icons.AddTo);
            _ok.SetBounds(590, 584, 246, 34);
            _ok.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            _ok.Enabled = false;
            _ok.Click += delegate { Commit(); };

            Controls.AddRange(new Control[] { _tabs, _statusAll, _ok, _cancel });
            CancelButton = _cancel;

            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ResumeLayout(false);

            Shown += delegate { StartScan(); };
            FormClosing += delegate { _cancelMatch = true; };
        }

        // ------------------------------------------------------------ scheda 1

        private void BuildTab1(TabPage page)
        {
            Label intro = new Label();
            intro.SetBounds(12, 10, 890, 34);
            intro.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            intro.Text = Tr.T("Questi winget li sa reinstallare da solo: spunta quelli che vuoi ritrovare dopo il "
                            + "formattaggio. Quello che winget non sa rimettere e' nell'altra scheda.",
                              "winget can reinstall these on its own: tick the ones you want back after formatting. "
                            + "What winget cannot put back is in the other tab.");
            intro.ForeColor = Theme.TextSecondary;

            Label lf = new Label();
            lf.Text = Tr.T("Filtra:", "Filter:");
            lf.AutoSize = true;
            lf.Location = new Point(12, 55);
            _filter.SetBounds(58, 51, 240, 26);
            _filter.TextChanged += delegate { Refill1(); };

            _hideSystem.Text = Tr.T("Nascondi i componenti di sistema", "Hide system components");
            _hideSystem.AutoSize = true;
            _hideSystem.Checked = true;
            _hideSystem.Location = new Point(312, 54);
            _hideSystem.CheckedChanged += delegate { Refill1(); };

            _hideHave.Text = Tr.T("Nascondi quelli gia' in lista", "Hide those already listed");
            _hideHave.AutoSize = true;
            _hideHave.Checked = true;
            _hideHave.Location = new Point(536, 54);
            _hideHave.CheckedChanged += delegate { Refill1(); };

            Panel card = Theme.Card();
            card.SetBounds(12, 86, 896, 384);
            card.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

            _list.Dock = DockStyle.Fill;
            _list.View = View.Details;
            _list.CheckBoxes = true;
            _list.FullRowSelect = true;
            _list.HideSelection = false;
            _list.BorderStyle = BorderStyle.None;
            _list.Font = Theme.UI;
            _list.Columns.Add(Tr.T("Nome", "Name"), 310);
            _list.Columns.Add(Tr.T("Identificativo", "Identifier"), 300);
            _list.Columns.Add(Tr.T("Versione", "Version"), 140);
            _list.Columns.Add(Tr.T("Note", "Notes"), 110);
            _list.ItemChecked += delegate(object s, ItemCheckedEventArgs e)
            {
                if (_suspend) return;
                AppItem it = e.Item.Tag as AppItem;
                if (it != null) _checks[it.PackageId] = e.Item.Checked;
                UpdateStatus();
            };
            _list.HandleCreated += delegate { Theme.ExplorerStyle(_list); };
            Theme.CardBody(card).Controls.Add(_list);

            Button all = Theme.FlatButton(Tr.T("Spunta tutti", "Tick all"), Icons.Multiselect);
            all.SetBounds(12, 480, 130, 30);
            all.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            all.Click += delegate { SetAll1(true); };

            Button none = Theme.FlatButton(Tr.T("Togli tutti", "Untick all"), Icons.ClearSelection);
            none.SetBounds(150, 480, 120, 30);
            none.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            none.Click += delegate { SetAll1(false); };

            _status1.SetBounds(286, 485, 620, 22);
            _status1.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            _status1.ForeColor = Theme.TextSecondary;

            page.Controls.AddRange(new Control[] { intro, lf, _filter, _hideSystem, _hideHave, card, all, none, _status1 });
        }

        private void Refill1()
        {
            string q = _filter.Text.Trim().ToLowerInvariant();
            _suspend = true;
            _list.BeginUpdate();
            _list.Items.Clear();
            _list.Groups.Clear();

            Dictionary<string, ListViewGroup> groups = new Dictionary<string, ListViewGroup>();
            foreach (AppItem it in _reinstallable)
            {
                bool have = _haveIds.Contains(it.PackageId);
                if (_hideSystem.Checked && it.Group == Winget.SystemGroup) continue;
                if (_hideHave.Checked && have) continue;
                if (q.Length > 0
                    && it.Name.ToLowerInvariant().IndexOf(q, StringComparison.Ordinal) < 0
                    && it.PackageId.ToLowerInvariant().IndexOf(q, StringComparison.Ordinal) < 0)
                    continue;

                ListViewGroup g;
                if (!groups.TryGetValue(it.Group, out g))
                {
                    g = new ListViewGroup(Groups.Show(it.Group), HorizontalAlignment.Left);
                    groups[it.Group] = g;
                    _list.Groups.Add(g);
                }

                ListViewItem lvi = new ListViewItem(it.Name);
                lvi.SubItems.Add(it.PackageId);
                lvi.SubItems.Add(it.Version);
                lvi.SubItems.Add(have ? Tr.T("gia' in lista", "already listed") : "");
                lvi.UseItemStyleForSubItems = false;
                lvi.Tag = it;
                bool c;
                lvi.Checked = _checks.TryGetValue(it.PackageId, out c) && c;
                lvi.Group = g;
                if (have)
                {
                    lvi.ForeColor = Theme.TextDisabled;
                    foreach (ListViewItem.ListViewSubItem si in lvi.SubItems) si.ForeColor = Theme.TextDisabled;
                }
                _list.Items.Add(lvi);
            }
            _list.EndUpdate();
            _suspend = false;
            UpdateStatus();
        }

        private void SetAll1(bool value)
        {
            _suspend = true;
            foreach (ListViewItem lvi in _list.Items)
            {
                lvi.Checked = value;
                AppItem it = lvi.Tag as AppItem;
                if (it != null) _checks[it.PackageId] = value;
            }
            _suspend = false;
            UpdateStatus();
        }

        // ------------------------------------------------------------ scheda 2

        private void BuildTab2(TabPage page)
        {
            Label intro = new Label();
            intro.SetBounds(12, 10, 890, 34);
            intro.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            intro.Text = Tr.T("Questi winget li vede sul PC ma non sa reinstallarli (presi da un sito, con licenza, dal "
                            + "loro portale). Per non perderli: cerca una corrispondenza a catalogo, aggiungi l'indirizzo "
                            + "dell'installer, oppure mettili in lista come promemoria.",
                              "winget sees these on the PC but cannot reinstall them (taken from a website, licensed, from "
                            + "the maker's portal). So you do not lose them: look for a catalog match, add the installer "
                            + "address, or put them on the list as reminders.");
            intro.ForeColor = Theme.TextSecondary;

            Label lf = new Label();
            lf.Text = Tr.T("Filtra:", "Filter:");
            lf.AutoSize = true;
            lf.Location = new Point(12, 55);
            _filter2.SetBounds(58, 51, 240, 26);
            _filter2.TextChanged += delegate { Refill2(); };

            _showAll2.Text = Tr.T("Mostra anche driver, componenti, app di Windows e giochi Steam",
                                  "Also show drivers, components, Windows apps and Steam games");
            _showAll2.AutoSize = true;
            _showAll2.Location = new Point(312, 54);
            _showAll2.CheckedChanged += delegate { Refill2(); };

            Panel card = Theme.Card();
            card.SetBounds(12, 86, 896, 346);
            card.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

            _list2.Dock = DockStyle.Fill;
            _list2.View = View.Details;
            _list2.FullRowSelect = true;
            _list2.MultiSelect = true;
            _list2.HideSelection = false;
            _list2.BorderStyle = BorderStyle.None;
            _list2.Font = Theme.UI;
            _list2.Columns.Add(Tr.T("Nome", "Name"), 300);
            _list2.Columns.Add(Tr.T("Versione", "Version"), 120);
            _list2.Columns.Add(Tr.T("Tipo", "Kind"), 190);
            _list2.Columns.Add(Tr.T("A catalogo", "In catalog"), 180);
            _list2.Columns.Add(Tr.T("Esito", "Outcome"), 90);
            _list2.SelectedIndexChanged += delegate { UpdateButtons2(); };
            _list2.HandleCreated += delegate { Theme.ExplorerStyle(_list2); };
            _list2.DoubleClick += delegate { if (_btnWithUrl.Enabled) AddWithUrl(); };
            Theme.CardBody(card).Controls.Add(_list2);

            // riga di azioni
            _btnMatch = Theme.FlatButton(Tr.T("Cerca corrispondenze nel catalogo", "Look for catalog matches"), Icons.Search);
            _btnMatch.SetBounds(12, 442, 250, 30);
            _btnMatch.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            _btnMatch.Click += delegate { if (_matching) _cancelMatch = true; else StartMatch(); };

            _status2.SetBounds(270, 447, 630, 22);
            _status2.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            _status2.ForeColor = Theme.TextSecondary;

            _btnFromCatalog = Theme.FlatButton(Tr.T("Aggiungi dal catalogo", "Add from catalog"), Icons.Package);
            _btnFromCatalog.SetBounds(12, 480, 190, 30);
            _btnFromCatalog.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            _btnFromCatalog.Click += delegate { AddFromCatalog(); };

            _btnWithUrl = Theme.FlatButton(Tr.T("Aggiungi con indirizzo...", "Add with address..."), Icons.Link);
            _btnWithUrl.SetBounds(210, 480, 200, 30);
            _btnWithUrl.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            _btnWithUrl.Click += delegate { AddWithUrl(); };

            _btnWeb = Theme.FlatButton(Tr.T("Cerca il sito nel browser", "Search the web for it"), Icons.Globe);
            _btnWeb.SetBounds(418, 480, 200, 30);
            _btnWeb.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            _btnWeb.Click += delegate { OpenWebSearch(); };

            _btnTodo = Theme.FlatButton(Tr.T("Aggiungi come promemoria", "Add as a reminder"), Icons.Tag);
            _btnTodo.SetBounds(626, 480, 220, 30);
            _btnTodo.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            _btnTodo.Click += delegate { AddAsTodo(); };

            page.Controls.AddRange(new Control[] { intro, lf, _filter2, _showAll2, card,
                                                   _btnMatch, _status2, _btnFromCatalog, _btnWithUrl, _btnWeb, _btnTodo });
            UpdateButtons2();
        }

        private void Refill2()
        {
            string q = _filter2.Text.Trim().ToLowerInvariant();
            _list2.BeginUpdate();
            _list2.Items.Clear();
            _list2.Groups.Clear();

            ListViewGroup gSetup = new ListViewGroup(Tr.T("Installati da un setup o da un portale",
                                                          "Installed by a setup or a portal"), HorizontalAlignment.Left);
            ListViewGroup gOther = new ListViewGroup(Tr.T("Driver, componenti, app di Windows, giochi Steam",
                                                          "Drivers, components, Windows apps, Steam games"), HorizontalAlignment.Left);
            _list2.Groups.Add(gSetup);
            if (_showAll2.Checked) _list2.Groups.Add(gOther);

            foreach (Winget.FoundProgram f in _others)
            {
                bool main = f.Kind == "setup" || f.Kind == "store";
                if (!main && !_showAll2.Checked) continue;
                if (q.Length > 0 && f.Name.ToLowerInvariant().IndexOf(q, StringComparison.Ordinal) < 0) continue;

                ListViewItem lvi = new ListViewItem(f.Name);
                lvi.SubItems.Add(f.Version);
                lvi.SubItems.Add(f.KindLabel());
                string m;
                lvi.SubItems.Add(_matches.TryGetValue(f.Id, out m) ? MatchLabel(f.Id, m) : "");
                lvi.SubItems.Add(_done2.Contains(f.Id) ? Tr.T("aggiunto", "added")
                                                       : (_haveNames.Contains(f.Name) ? Tr.T("gia' in lista", "already listed") : ""));
                lvi.UseItemStyleForSubItems = false;
                lvi.Tag = f;
                lvi.Group = main ? gSetup : gOther;
                if (_done2.Contains(f.Id) || _haveNames.Contains(f.Name))
                {
                    lvi.ForeColor = Theme.TextDisabled;
                    foreach (ListViewItem.ListViewSubItem si in lvi.SubItems) si.ForeColor = Theme.TextDisabled;
                }
                else if (_matches.ContainsKey(f.Id))
                {
                    lvi.SubItems[3].ForeColor = MatchColor(f.Id);
                }
                _list2.Items.Add(lvi);
            }
            _list2.EndUpdate();
            UpdateButtons2();
            UpdateStatus();
        }

        private string MatchLabel(string rowId, string packageId)
        {
            int st;
            return (_matchStrength.TryGetValue(rowId, out st) && st < 2)
                ? Tr.F("forse: {0}", "maybe: {0}", packageId) : packageId;
        }

        private Color MatchColor(string rowId)
        {
            int st;
            return (_matchStrength.TryGetValue(rowId, out st) && st < 2) ? Theme.Warning : Theme.Success;
        }

        private List<Winget.FoundProgram> Selected2()
        {
            List<Winget.FoundProgram> l = new List<Winget.FoundProgram>();
            foreach (ListViewItem lvi in _list2.SelectedItems)
            {
                Winget.FoundProgram f = lvi.Tag as Winget.FoundProgram;
                if (f != null && !_done2.Contains(f.Id)) l.Add(f);
            }
            return l;
        }

        private void UpdateButtons2()
        {
            List<Winget.FoundProgram> sel = Selected2();
            bool any = sel.Count > 0;
            bool matched = false;
            foreach (Winget.FoundProgram f in sel) if (_matches.ContainsKey(f.Id)) { matched = true; break; }
            _btnFromCatalog.Enabled = matched;
            _btnWithUrl.Enabled = sel.Count == 1;
            _btnWeb.Enabled = sel.Count == 1;
            _btnTodo.Enabled = any;
        }

        // Per ogni riga visibile interroga il catalogo con il nome: se il risultato in cima
        // ha lo stesso nome (al netto di versione e fronzoli), lo propone. Lento, perche' e'
        // una chiamata a winget per programma: per questo e' un pulsante, e si puo' fermare.
        private void StartMatch()
        {
            List<Winget.FoundProgram> todo = new List<Winget.FoundProgram>();
            foreach (ListViewItem lvi in _list2.Items)
            {
                Winget.FoundProgram f = lvi.Tag as Winget.FoundProgram;
                if (f == null || _matches.ContainsKey(f.Id) || _done2.Contains(f.Id)) continue;
                if (f.Kind != "setup" && f.Kind != "store") continue;
                todo.Add(f);
            }
            if (todo.Count == 0) { _status2.Text = Tr.T("Niente da cercare: tutte le righe visibili sono gia' state controllate.",
                                                        "Nothing to look up: every visible row has already been checked."); return; }

            _matching = true;
            _cancelMatch = false;
            _btnMatch.Text = Tr.T("Ferma la ricerca", "Stop the search");
            _status2.ForeColor = Theme.TextSecondary;
            UseWaitCursor = true;

            Thread t = new Thread(delegate()
            {
                int found = 0, done = 0;
                foreach (Winget.FoundProgram f in todo)
                {
                    if (_cancelMatch) break;
                    done++;
                    int d = done, fnd = found;
                    Winget.FoundProgram cur = f;
                    Safe(delegate { _status2.Text = Tr.F("Cerco {0} di {1}:  {2}   ({3} trovati)",
                                                          "Looking up {0} of {1}:  {2}   ({3} found)", d, todo.Count, cur.Name, fnd); });
                    try
                    {
                        List<SearchResult> res = Winget.Search(Winget.NormalizeName(f.Name), null);
                        SearchResult hit = null;
                        int best = 0;
                        foreach (SearchResult r in res)
                        {
                            int st = Winget.MatchStrength(f.Name, r.Name, r.Id);
                            if (st > best) { best = st; hit = r; }
                            if (best == 2) break;
                        }
                        if (hit != null)
                        {
                            found++;
                            SearchResult h = hit;
                            int strength = best;
                            Safe(delegate
                            {
                                _matches[cur.Id] = h.Id;
                                _matchNames[cur.Id] = h.Name;
                                _matchVersions[cur.Id] = h.Version;
                                _matchStrength[cur.Id] = strength;
                                foreach (ListViewItem lvi in _list2.Items)
                                {
                                    if (lvi.Tag == cur)
                                    {
                                        lvi.SubItems[3].Text = MatchLabel(cur.Id, h.Id);
                                        lvi.SubItems[3].ForeColor = MatchColor(cur.Id);
                                    }
                                }
                                UpdateButtons2();
                            });
                        }
                    }
                    catch { }
                }
                int total = found;
                bool stopped = _cancelMatch;
                Safe(delegate
                {
                    _matching = false;
                    UseWaitCursor = false;
                    _btnMatch.Text = Tr.T("Cerca corrispondenze nel catalogo", "Look for catalog matches");
                    _status2.ForeColor = total > 0 ? Theme.Success : Theme.TextSecondary;
                    _status2.Text = (stopped ? Tr.T("Fermato. ", "Stopped. ") : Tr.T("Finito. ", "Done. "))
                                  + Tr.F("{0} corrispondenze: in verde quelle sicure, in arancione (\"forse\") gli omonimi "
                                       + "da controllare. Selezionale e premi \"Aggiungi dal catalogo\".",
                                         "{0} matches: sure ones in green, namesakes to check in orange (\"maybe\"). "
                                       + "Select them and press \"Add from catalog\".", total);
                });
            });
            t.IsBackground = true;
            t.Start();
        }

        private void Safe(Action a)
        {
            if (IsDisposed) return;
            try { BeginInvoke(a); }
            catch { }
        }

        private void AddFromCatalog()
        {
            List<Winget.FoundProgram> sel = Selected2();
            if (sel.Count > 10 && MessageBox.Show(this,
                    Tr.F("Aggiungo {0} voci dal catalogo?", "Add {0} entries from the catalog?", sel.Count),
                    AppInfo.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            int n = 0;
            foreach (Winget.FoundProgram f in sel)
            {
                string id;
                if (!_matches.TryGetValue(f.Id, out id)) continue;
                if (_haveIds.Contains(id)) { MarkDone(f, Tr.T("gia' in lista", "already listed")); continue; }
                AppItem it = new AppItem();
                it.Kind = AppItem.KindWinget;
                it.PackageId = id;
                it.Name = _matchNames.ContainsKey(f.Id) ? _matchNames[f.Id] : f.Name;
                it.Version = _matchVersions.ContainsKey(f.Id) ? _matchVersions[f.Id] : "";
                it.Group = Winget.GuessGroup(it);
                Chosen.Add(it);
                _haveIds.Add(id);
                MarkDone(f, Tr.T("aggiunto", "added"));
                n++;
            }
            _status2.ForeColor = Theme.Success;
            _status2.Text = Tr.F("{0} voci aggiunte dal catalogo (verranno salvate con \"Aggiungi al profilo\").",
                                 "{0} entries added from the catalog (they are saved with \"Add to the profile\").", n);
            UpdateStatus();
        }

        private void AddWithUrl()
        {
            List<Winget.FoundProgram> sel = Selected2();
            if (sel.Count != 1) return;
            Winget.FoundProgram f = sel[0];

            AppItem seed = new AppItem();
            seed.Kind = AppItem.KindUrl;
            seed.Name = CleanName(f.Name);
            seed.Group = _defaultGroup;
            seed.SilentArgs = DirectUrl.SilentAuto;
            using (UrlForm uf = new UrlForm(_groups, seed, _defaultGroup))
            {
                uf.Text = Tr.F("Aggiungi l'indirizzo di {0}", "Add the address of {0}", seed.Name);
                if (uf.ShowDialog(this) != DialogResult.OK) return;
                Chosen.Add(uf.Item);
                _haveNames.Add(f.Name);
                MarkDone(f, Tr.T("aggiunto", "added"));
                _status2.ForeColor = Theme.Success;
                _status2.Text = Tr.F("\"{0}\" aggiunto con il suo indirizzo.",
                                     "\"{0}\" added with its address.", uf.Item.Name);
                UpdateStatus();
            }
        }

        private void OpenWebSearch()
        {
            List<Winget.FoundProgram> sel = Selected2();
            if (sel.Count != 1) return;
            string q = Uri.EscapeDataString(CleanName(sel[0].Name) + " download windows");
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("https://www.bing.com/search?q=" + q);
                psi.UseShellExecute = true;
                Process.Start(psi);
                _status2.ForeColor = Theme.TextSecondary;
                _status2.Text = Tr.T("Ricerca aperta nel browser. Copia il link del file di installazione e usa "
                                   + "\"Aggiungi con indirizzo...\".",
                                     "Search opened in the browser. Copy the installer link and use \"Add with address...\".");
            }
            catch (Exception ex)
            {
                _status2.ForeColor = Theme.Danger;
                _status2.Text = Tr.F("Non riesco ad aprire il browser: {0}", "Cannot open the browser: {0}", ex.Message);
            }
        }

        private void AddAsTodo()
        {
            List<Winget.FoundProgram> sel = Selected2();
            if (sel.Count > 10 && MessageBox.Show(this,
                    Tr.F("Aggiungo {0} promemoria al gruppo \"{1}\"?",
                         "Add {0} reminders to the \"{1}\" group?", sel.Count, Groups.Show(Groups.Todo)),
                    AppInfo.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            int n = 0;
            foreach (Winget.FoundProgram f in sel)
            {
                AppItem it = new AppItem();
                it.Kind = AppItem.KindUrl;
                it.Name = CleanName(f.Name);
                it.Url = "";
                it.SilentArgs = DirectUrl.SilentAuto;
                it.Group = Groups.Todo;
                it.Note = Tr.F("letto dal PC il {0}{1}", "read from the PC on {0}{1}",
                               DateTime.Now.ToString("dd/MM/yyyy"),
                               f.Version.Length > 0 ? Tr.F(", versione {0}", ", version {0}", f.Version) : "");
                Chosen.Add(it);
                _haveNames.Add(f.Name);
                MarkDone(f, Tr.T("promemoria", "reminder"));
                n++;
            }
            _status2.ForeColor = Theme.Success;
            _status2.Text = Tr.F("{0} promemoria nel gruppo \"{1}\": in lista avranno stato \"manca l'indirizzo\" "
                               + "finche' non lo aggiungi.",
                                 "{0} reminders in the \"{1}\" group: on the list they show \"address missing\" "
                               + "until you add one.", n, Groups.Show(Groups.Todo));
            UpdateStatus();
        }

        private void MarkDone(Winget.FoundProgram f, string label)
        {
            _done2.Add(f.Id);
            foreach (ListViewItem lvi in _list2.Items)
            {
                if (lvi.Tag != f) continue;
                lvi.SubItems[4].Text = label;
                lvi.ForeColor = Theme.TextDisabled;
                foreach (ListViewItem.ListViewSubItem si in lvi.SubItems) si.ForeColor = Theme.TextDisabled;
                lvi.Selected = false;
            }
            UpdateButtons2();
        }

        // "Analog Lab V 5.12.4" -> "Analog Lab V": il numero di versione in coda non e' parte del nome.
        private static string CleanName(string name)
        {
            string n = (name ?? "").Trim();
            string[] words = n.Split(' ');
            int end = words.Length;
            while (end > 1)
            {
                string w = words[end - 1].Trim('(', ')');
                bool versiony = w.Length > 0 && (char.IsDigit(w[0]) || (w.Length > 1 && (w[0] == 'v' || w[0] == 'V') && char.IsDigit(w[1])));
                if (versiony || string.Equals(w, "version", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(w, "versione", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(w, "x64", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(w, "x86", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(w, "64-bit", StringComparison.OrdinalIgnoreCase))
                    end--;
                else break;
            }
            return string.Join(" ", words, 0, end).Trim();
        }

        // ------------------------------------------------------------ comune

        private void StartScan()
        {
            UseWaitCursor = true;
            Thread t = new Thread(delegate()
            {
                List<Winget.FoundProgram> found = new List<Winget.FoundProgram>();
                string error = null;
                try { found = Winget.ListAll(null); }
                catch (Exception ex) { error = ex.Message; }
                List<Winget.FoundProgram> f = found;
                string err = error;
                Safe(delegate { ScanDone(f, err); });
            });
            t.IsBackground = true;
            t.Start();
        }

        private void ScanDone(List<Winget.FoundProgram> found, string error)
        {
            UseWaitCursor = false;
            if (error != null)
            {
                _statusAll.ForeColor = Theme.Danger;
                _statusAll.Text = Tr.F("Errore nella lettura: {0}", "Error while reading: {0}", error);
                return;
            }
            _all = found;
            _reinstallable.Clear();
            _others.Clear();
            foreach (Winget.FoundProgram f in _all)
            {
                if (f.Reinstallable && f.Id.Length > 0) _reinstallable.Add(Winget.ToItem(f));
                else if (!f.Reinstallable) _others.Add(f);
            }
            // Spuntati di partenza: tutto tranne i componenti di sistema e quelli gia' in lista.
            foreach (AppItem it in _reinstallable)
                _checks[it.PackageId] = it.Group != Winget.SystemGroup && !_haveIds.Contains(it.PackageId);

            int mainOthers = 0;
            foreach (Winget.FoundProgram f in _others) if (f.Kind == "setup" || f.Kind == "store") mainOthers++;
            NotReinstallableCount = mainOthers;
            _tabOk.Text = Tr.F("Reinstallabili da winget ({0})", "Reinstallable by winget ({0})", _reinstallable.Count);
            _tabNo.Text = Tr.F("Non reinstallabili da winget ({0})", "Not reinstallable by winget ({0})", mainOthers);

            _ok.Enabled = true;
            Refill1();
            Refill2();
        }

        private void UpdateStatus()
        {
            int n = 0;
            foreach (KeyValuePair<string, bool> kv in _checks) if (kv.Value) n++;
            int extra = Chosen.Count;
            _status1.ForeColor = Theme.TextSecondary;
            _status1.Text = Tr.F("{0} reinstallabili, {1} mostrati, {2} spuntati.",
                                 "{0} reinstallable, {1} shown, {2} ticked.", _reinstallable.Count, _list.Items.Count, n);
            int total = n + extra;
            _ok.Text = total > 0 ? Tr.F("Aggiungi al profilo ({0})", "Add to the profile ({0})", total)
                                 : Tr.T("Aggiungi al profilo", "Add to the profile");
            string s = Tr.F("{0} spuntati nella prima scheda", "{0} ticked in the first tab", n);
            if (extra > 0) s += Tr.F(", {0} dalla seconda", ", {0} from the second", extra);
            _statusAll.ForeColor = Theme.TextSecondary;
            _statusAll.Text = _all.Count == 0
                ? Tr.T("Leggo l'elenco dei programmi installati...", "Reading the list of installed programs...")
                : s + ".";
        }

        private void Commit()
        {
            List<AppItem> result = new List<AppItem>();
            foreach (AppItem it in _reinstallable)
            {
                bool c;
                if (_checks.TryGetValue(it.PackageId, out c) && c) result.Add(it.Clone());
            }
            result.AddRange(Chosen);
            Chosen = result;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
