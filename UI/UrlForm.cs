using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace LHInstaller
{
    // Aggiunta o modifica di una voce scaricata da un indirizzo diretto, per tutto
    // quello che nel catalogo winget non c'e'.
    public class UrlForm : Form
    {
        private readonly TextBox _name = new TextBox();
        private readonly TextBox _url = new TextBox();
        private readonly ComboBox _group = new ComboBox();
        private readonly ComboBox _silent = new ComboBox();
        private readonly TextBox _note = new TextBox();
        private readonly Button _probe;
        private readonly Label _info = new Label();
        private readonly Button _ok;
        private readonly Button _cancel;

        private volatile bool _busy;
        private readonly List<string> _allGroups;

        public AppItem Item;

        public UrlForm(List<string> groups, AppItem existing, string defaultGroup)
        {
            _allGroups = groups ?? new List<string>();
            Item = existing != null ? existing.Clone() : new AppItem();
            Item.Kind = AppItem.KindUrl;

            SuspendLayout();
            Text = existing == null ? Tr.T("Aggiungi un indirizzo", "Add an address")
                                    : Tr.T("Modifica indirizzo", "Edit address");
            Icon = Icons.AppIcon();
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(720, 424);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Font = Theme.UI;
            BackColor = Theme.WindowBg;

            int left = 150, w = 550, y = 18;

            Label intro = new Label();
            intro.Text = Tr.F("Per i programmi che nel catalogo non ci sono: incolla il link diretto al file di "
                            + "installazione (.exe o .msi). Al momento giusto {0} lo scarica e lo esegue.",
                              "For programs that are not in the catalog: paste the direct link to the installer "
                            + "file (.exe or .msi). When the time comes, {0} downloads and runs it.", AppInfo.Name);
            intro.ForeColor = Theme.TextSecondary;
            intro.SetBounds(16, y, 688, 34);
            Controls.Add(intro);
            y += 46;

            Controls.Add(MakeLabel(Tr.T("Indirizzo", "Address"), 16, y + 4));
            _url.SetBounds(left, y, w - 118, 26);
            Controls.Add(_url);
            _probe = Theme.FlatButton(Tr.T("Verifica", "Check"), Icons.Sync);
            _probe.SetBounds(left + w - 110, y - 1, 110, 28);
            _probe.Click += delegate { StartProbe(); };
            Controls.Add(_probe);
            y += 30;

            _info.SetBounds(left, y, w, 34);
            _info.ForeColor = Theme.TextSecondary;
            _info.Text = Tr.T("Premi \"Verifica\" per controllare che l'indirizzo risponda e leggere nome e peso del file.",
                              "Press \"Check\" to see whether the address answers, and read the file name and size.");
            Controls.Add(_info);
            y += 42;

            Controls.Add(MakeLabel(Tr.T("Nome", "Name"), 16, y + 4));
            _name.SetBounds(left, y, w, 26);
            Controls.Add(_name);
            y += 38;

            Controls.Add(MakeLabel(Tr.T("Gruppo", "Group"), 16, y + 4));
            _group.SetBounds(left, y, 260, 26);
            _group.DropDownStyle = ComboBoxStyle.DropDown;
            foreach (string g in groups) _group.Items.Add(Groups.Show(g));
            Controls.Add(_group);
            y += 38;

            Controls.Add(MakeLabel(Tr.T("Installazione muta", "Silent install"), 16, y + 4));
            _silent.SetBounds(left, y, w, 26);
            _silent.DropDownStyle = ComboBoxStyle.DropDown;
            _silent.Items.Add(DirectUrl.SilentAutoLabel);
            _silent.Items.Add(DirectUrl.SilentNoneLabel);
            _silent.Items.Add("/S");
            _silent.Items.Add("/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-");
            _silent.Items.Add("/qn /norestart");
            _silent.Items.Add("/quiet /norestart");
            _silent.Items.Add("/s /v\"/qn\"");
            _silent.Items.Add("--silent");
            Controls.Add(_silent);
            y += 30;

            Label hint = new Label();
            hint.SetBounds(left, y, w, 48);
            hint.ForeColor = Theme.TextSecondary;
            hint.Text = Tr.F("Con \"{0}\" l'app riconosce da sola la famiglia dell'installer dopo averlo scaricato "
                           + "(Inno Setup, NSIS, MSI, WiX, InstallShield) e usa l'argomento giusto. Se non la "
                           + "riconosce, apre la finestra dell'installer e te lo dice.",
                             "With \"{0}\" the app works out the installer family by itself after downloading it "
                           + "(Inno Setup, NSIS, MSI, WiX, InstallShield) and uses the right argument. If it "
                           + "cannot tell, it opens the installer window and says so.", DirectUrl.SilentAutoLabel);
            Controls.Add(hint);
            y += 56;

            Controls.Add(MakeLabel(Tr.T("Note", "Notes"), 16, y + 4));
            _note.SetBounds(left, y, w, 26);
            Controls.Add(_note);
            y += 44;

            _ok = Theme.PrimaryButton(Tr.T("Salva", "Save"), Icons.Accept);
            _ok.SetBounds(left + w - 226, y, 110, 32);
            _ok.Click += delegate { Commit(); };
            Controls.Add(_ok);

            _cancel = Theme.FlatButton(Tr.T("Annulla", "Cancel"), null);
            _cancel.SetBounds(left + w - 108, y, 108, 32);
            _cancel.DialogResult = DialogResult.Cancel;
            Controls.Add(_cancel);

            AcceptButton = _ok;
            CancelButton = _cancel;

            _name.Text = Item.Name;
            _url.Text = Item.Url;
            _group.Text = Groups.Show(!string.IsNullOrEmpty(Item.Group) && existing != null ? Item.Group
                        : (string.IsNullOrEmpty(defaultGroup) ? Groups.General : defaultGroup));
            if (existing == null) _silent.Text = DirectUrl.SilentAutoLabel;
            else if (string.IsNullOrEmpty(Item.SilentArgs)) _silent.Text = DirectUrl.SilentNoneLabel;
            else if (Item.SilentArgs == DirectUrl.SilentAuto) _silent.Text = DirectUrl.SilentAutoLabel;
            else _silent.Text = Item.SilentArgs;
            _note.Text = Item.Note;

            if (Item.ContentLength > 0)
                _info.Text = Tr.F("Firma registrata: {0}{1}. Premi \"Verifica\" per confrontarla con il file di adesso.",
                                  "Recorded signature: {0}{1}. Press \"Check\" to compare it with the file as it is now.",
                                  DirectUrl.Human(Item.ContentLength),
                                  string.IsNullOrEmpty(Item.LastChecked) ? ""
                                      : Tr.F(", vista il {0}", ", seen on {0}", Item.LastChecked));

            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ResumeLayout(false);

            Shown += delegate { if (_url.Text.Length == 0) _url.Focus(); else _name.Focus(); };
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

        private static Label MakeLabel(string text, int x, int y)
        {
            Label l = new Label();
            l.Text = text;
            l.AutoSize = true;
            l.Location = new Point(x, y);
            l.ForeColor = Theme.Text;
            return l;
        }

        private void StartProbe()
        {
            if (_busy) return;
            string url = _url.Text.Trim();
            if (url.Length == 0) return;
            if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                _info.ForeColor = Theme.Danger;
                _info.Text = Tr.T("L'indirizzo deve cominciare con http:// o https://",
                                  "The address must start with http:// or https://");
                return;
            }

            _busy = true;
            _probe.Enabled = false;
            UseWaitCursor = true;
            _info.ForeColor = Theme.TextSecondary;
            _info.Text = Tr.T("Contatto il server...", "Contacting the server...");

            Thread t = new Thread(delegate()
            {
                DirectUrl.RemoteInfo info = DirectUrl.Probe(url);
                try { BeginInvoke(new Action(delegate { ShowProbe(info); })); }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        private void ShowProbe(DirectUrl.RemoteInfo info)
        {
            _busy = false;
            _probe.Enabled = true;
            UseWaitCursor = false;

            if (!info.Ok)
            {
                _info.ForeColor = Theme.Danger;
                _info.Text = Tr.F("Non raggiungibile: {0}", "Not reachable: {0}", info.Error);
                return;
            }

            bool changed = info.DiffersFrom(Item);

            Item.ETag = info.ETag;
            Item.LastModified = info.LastModified;
            Item.ContentLength = info.ContentLength;
            Item.LastChecked = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            Item.UpdateAvailable = false;

            string msg = Tr.F("Raggiungibile. File: {0}", "Reachable. File: {0}",
                              info.FileName.Length > 0 ? info.FileName : Tr.T("senza nome", "unnamed"));
            if (info.ContentLength > 0) msg += ", " + DirectUrl.Human(info.ContentLength);
            if (!string.IsNullOrEmpty(info.LastModified))
                msg += Tr.F(", del {0}", ", dated {0}", info.LastModified);
            if (changed) msg += Tr.T("  --  diverso da quello registrato prima.",
                                     "  --  different from the one recorded before.");

            _info.ForeColor = changed ? Theme.Warning : Theme.Success;
            _info.Text = msg;

            if (_name.Text.Trim().Length == 0 && info.FileName.Length > 0)
                _name.Text = System.IO.Path.GetFileNameWithoutExtension(info.FileName);
        }

        private void Commit()
        {
            string url = _url.Text.Trim();
            if (url.Length == 0 || !url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(this, Tr.T("Serve un indirizzo che cominci con http:// o https://",
                                           "An address starting with http:// or https:// is required"),
                    Tr.T("Indirizzo mancante", "Address missing"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _url.Focus();
                return;
            }

            Item.Url = url;
            Item.Name = _name.Text.Trim().Length > 0 ? _name.Text.Trim() : url;
            Item.Group = GroupFromBox();
            Item.Note = _note.Text.Trim();

            // Nel profilo finiscono i valori canonici, non le etichette tradotte.
            string s = _silent.Text.Trim();
            if (s == DirectUrl.SilentNoneLabel || s == DirectUrl.SilentNone) Item.SilentArgs = "";
            else if (s == DirectUrl.SilentAutoLabel) Item.SilentArgs = DirectUrl.SilentAuto;
            else Item.SilentArgs = s;

            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
