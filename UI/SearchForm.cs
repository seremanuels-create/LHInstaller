using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace LHInstaller
{
    // Ricerca nel catalogo winget. Il pacchetto principale e' sempre in cima,
    // marcato come consigliato; beta, nightly e varianti scendono in fondo.
    public class SearchForm : Form
    {
        private readonly TextBox _query = new TextBox();
        private readonly Button _search;
        private readonly ListView _list = new ListView();
        private readonly ComboBox _group = new ComboBox();
        private readonly Button _add;
        private readonly Button _close;
        private readonly Label _status = new Label();
        private readonly HashSet<string> _have;
        private readonly List<string> _allGroups;

        private volatile bool _busy;

        public List<AppItem> Chosen = new List<AppItem>();

        public SearchForm(List<string> groups, string currentGroup, HashSet<string> alreadyInList)
        {
            _have = alreadyInList ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _allGroups = groups ?? new List<string>();

            SuspendLayout();
            Text = Tr.T("Cerca nel catalogo winget", "Search the winget catalog");
            Icon = Icons.AppIcon();
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(900, 560);
            MinimumSize = new Size(720, 440);
            Font = Theme.UI;
            BackColor = Theme.WindowBg;
            ShowInTaskbar = false;

            // --- riga di ricerca
            Label lq = new Label();
            lq.Text = Tr.T("Cerca:", "Search:");
            lq.AutoSize = true;
            lq.Location = new Point(16, 19);

            _query.SetBounds(64, 15, 700, 26);
            _query.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _query.Font = new Font("Segoe UI", 10f);

            _search = Theme.PrimaryButton(Tr.T("Cerca", "Search"), Icons.Search);
            _search.SetBounds(774, 13, 110, 30);
            _search.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _search.Click += delegate { StartSearch(); };

            Label hint = new Label();
            hint.Text = Tr.T("Scrivi il nome del programma e premi Invio. La riga segnata \"Consigliata\" e' la versione "
                           + "stabile piu' recente; beta, nightly e varianti stanno piu' in basso.",
                             "Type the program name and press Enter. The row marked \"Recommended\" is the latest stable "
                           + "version; beta, nightly and variants sit further down.");
            hint.ForeColor = Theme.TextSecondary;
            hint.AutoSize = false;
            hint.SetBounds(16, 46, 868, 18);
            hint.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            // --- risultati
            Panel card = Theme.Card();
            card.SetBounds(16, 70, 868, 400);
            card.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

            _list.Dock = DockStyle.Fill;
            _list.View = View.Details;
            _list.FullRowSelect = true;
            _list.MultiSelect = true;
            _list.HideSelection = false;
            _list.BorderStyle = BorderStyle.None;
            _list.Font = Theme.UI;
            _list.Columns.Add("", 110);
            _list.Columns.Add(Tr.T("Nome", "Name"), 300);
            _list.Columns.Add(Tr.T("Identificativo", "Identifier"), 290);
            _list.Columns.Add(Tr.T("Versione", "Version"), 140);
            _list.DoubleClick += delegate { AddSelected(false); };
            _list.HandleCreated += delegate { Theme.ExplorerStyle(_list); };
            Theme.CardBody(card).Controls.Add(_list);

            // --- piede
            Label lg = new Label();
            lg.Text = Tr.T("Aggiungi al gruppo:", "Add to group:");
            lg.AutoSize = true;
            lg.Location = new Point(16, 487);
            lg.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;

            _group.SetBounds(140, 483, 240, 26);
            _group.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            _group.DropDownStyle = ComboBoxStyle.DropDown;
            foreach (string g in groups) _group.Items.Add(Groups.Show(g));
            _group.Text = Groups.Show(string.IsNullOrEmpty(currentGroup) ? Groups.General : currentGroup);

            _add = Theme.FlatButton(Tr.T("Aggiungi alla lista", "Add to the list"), Icons.AddTo);
            _add.SetBounds(580, 480, 190, 32);
            _add.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            _add.Click += delegate { AddSelected(false); };

            _close = Theme.FlatButton(Tr.T("Chiudi", "Close"), null);
            _close.SetBounds(780, 480, 104, 32);
            _close.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            _close.Click += delegate { DialogResult = DialogResult.OK; Close(); };

            _status.SetBounds(16, 524, 868, 22);
            _status.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            _status.ForeColor = Theme.TextSecondary;
            _status.Text = Tr.T("Esempi: brave, discord, vlc, 7zip, spotify, steam, obs.",
                                "Examples: brave, discord, vlc, 7zip, spotify, steam, obs.");

            Controls.AddRange(new Control[] { lq, _query, _search, hint, card, lg, _group, _add, _close, _status });
            AcceptButton = _search;

            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ResumeLayout(false);

            Shown += delegate { _query.Focus(); };
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { DialogResult = DialogResult.OK; Close(); return true; }
            if (keyData == (Keys.Control | Keys.Enter)) { AddSelected(false); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void StartSearch()
        {
            if (_busy) return;
            string q = _query.Text.Trim();
            if (q.Length == 0) return;

            _busy = true;
            _search.Enabled = false;
            _list.Items.Clear();
            _status.ForeColor = Theme.TextSecondary;
            _status.Text = Tr.T("Interrogo il catalogo...", "Querying the catalog...");
            UseWaitCursor = true;

            Thread t = new Thread(delegate()
            {
                List<SearchResult> found = new List<SearchResult>();
                string error = null;
                try { found = Winget.Search(q, null); }
                catch (Exception ex) { error = ex.Message; }

                string err = error;
                List<SearchResult> res = found;
                try { BeginInvoke(new Action(delegate { ShowResults(res, err); })); }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        private void ShowResults(List<SearchResult> results, string error)
        {
            _busy = false;
            _search.Enabled = true;
            UseWaitCursor = false;
            _list.BeginUpdate();
            _list.Items.Clear();

            if (error != null)
            {
                _list.EndUpdate();
                _status.ForeColor = Theme.Danger;
                _status.Text = Tr.F("Errore: {0}", "Error: {0}", error);
                return;
            }
            if (results.Count == 0)
            {
                _list.EndUpdate();
                _status.ForeColor = Theme.Warning;
                _status.Text = Tr.T("Nessun pacchetto trovato. Se il programma non e' a catalogo, usa \"Aggiungi indirizzo\" "
                                  + "dalla finestra principale.",
                                    "No package found. If the program is not in the catalog, use \"Add address\" "
                                  + "from the main window.");
                return;
            }

            foreach (SearchResult r in results)
            {
                bool have = _have.Contains(r.Id);
                ListViewItem lvi = new ListViewItem(have ? Tr.T("gia' in lista", "already listed")
                                                        : (r.Recommended ? Tr.T("Consigliata", "Recommended") : ""));
                lvi.SubItems.Add(r.Name);
                lvi.SubItems.Add(r.Id);
                lvi.SubItems.Add(r.Version);
                lvi.Tag = r;
                lvi.UseItemStyleForSubItems = false;
                if (have)
                {
                    lvi.ForeColor = Theme.TextDisabled;
                    foreach (ListViewItem.ListViewSubItem s in lvi.SubItems) s.ForeColor = Theme.TextDisabled;
                }
                else if (r.Recommended)
                {
                    lvi.ForeColor = Theme.Success;
                    lvi.Font = Theme.UIBold;
                    for (int i = 1; i < lvi.SubItems.Count; i++) { lvi.SubItems[i].Font = Theme.UIBold; lvi.SubItems[i].ForeColor = Theme.Text; }
                }
                _list.Items.Add(lvi);
            }
            _list.EndUpdate();

            // Preseleziono la prima riga non ancora in lista: un Invio in piu' e l'hai aggiunta.
            foreach (ListViewItem lvi in _list.Items)
            {
                if (!_have.Contains(((SearchResult)lvi.Tag).Id)) { lvi.Selected = true; lvi.Focused = true; break; }
            }
            _status.ForeColor = Theme.TextSecondary;
            _status.Text = Tr.F("{0} risultati. Doppio clic, o seleziona e premi \"Aggiungi alla lista\" (Ctrl+Invio).",
                                "{0} results. Double-click, or select and press \"Add to the list\" (Ctrl+Enter).", results.Count);
        }

        // La casella mostra i nomi tradotti; nel profilo deve tornare il nome canonico.
        private string GroupFromBox()
        {
            string shown = _group.Text.Trim();
            if (shown.Length == 0) return Groups.General;
            foreach (string g in _allGroups)
                if (string.Equals(Groups.Show(g), shown, StringComparison.OrdinalIgnoreCase)) return g;
            return shown;
        }

        private void AddSelected(bool close)
        {
            if (_list.SelectedItems.Count == 0) return;
            string group = GroupFromBox();

            int added = 0, skipped = 0;
            foreach (ListViewItem lvi in _list.SelectedItems)
            {
                SearchResult r = lvi.Tag as SearchResult;
                if (r == null) continue;
                if (_have.Contains(r.Id)) { skipped++; continue; }
                AppItem it = new AppItem();
                it.Kind = AppItem.KindWinget;
                it.Name = r.Name;
                it.PackageId = r.Id;
                it.Version = r.Version;
                it.Group = group;
                it.Enabled = true;
                Chosen.Add(it);
                _have.Add(r.Id);
                lvi.Text = Tr.T("aggiunta", "added");
                lvi.ForeColor = Theme.TextDisabled;
                lvi.Font = Theme.UI;
                foreach (ListViewItem.ListViewSubItem s in lvi.SubItems) { s.ForeColor = Theme.TextDisabled; s.Font = Theme.UI; }
                added++;
            }
            _status.ForeColor = added > 0 ? Theme.Success : Theme.Warning;
            _status.Text = Tr.F("{0} voci aggiunte al gruppo \"{1}\"{2}. Puoi cercare ancora, oppure chiudere.",
                                "{0} entries added to group \"{1}\"{2}. You can search again, or close.",
                                added, Groups.Show(group),
                                skipped > 0 ? Tr.F(", {0} gia' presenti", ", {0} already there", skipped) : "");
            _query.Focus();
            _query.SelectAll();
            if (close) { DialogResult = DialogResult.OK; Close(); }
        }
    }
}
