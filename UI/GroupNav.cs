using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using System.Windows.Forms.VisualStyles;

namespace LHInstaller
{
    public class GroupInfo
    {
        public string Name;
        public int Total;
        public int Checked;
    }

    // Il pannello dei gruppi, a sinistra. Ogni riga ha una casella a tre stati
    // (tutto spuntato, niente, in parte) che accende o spegne l'intero gruppo, il
    // nome e quante voci contiene. Cliccando sul nome si filtra la lista a quel
    // gruppo; la prima riga, "Tutti", mostra tutto.
    public class GroupNav : Panel
    {
        public event Action<string, bool> GroupToggled;          // null = tutti
        public event Action<string> SelectionChanged;            // null = tutti
        public event Action NewGroupRequested;
        public event Action<string> RenameRequested;
        public event Action<string> DeleteRequested;

        private readonly List<GroupRow> _rows = new List<GroupRow>();
        private readonly LinkRow _newRow;
        private string _selected;    // null = tutti
        private int _allTotal, _allChecked;

        public GroupNav()
        {
            BackColor = Theme.CardBg;
            AutoScroll = true;
            Padding = new Padding(0, 4, 0, 4);

            _newRow = new LinkRow(Tr.T("Nuovo gruppo", "New group"), Icons.Add);
            _newRow.Click += delegate { if (NewGroupRequested != null) NewGroupRequested(); };
            Controls.Add(_newRow);
        }

        public string Selected
        {
            get { return _selected; }
        }

        public void SetGroups(List<GroupInfo> groups, int allTotal, int allChecked)
        {
            _allTotal = allTotal;
            _allChecked = allChecked;

            // Se il gruppo selezionato non esiste piu', torno a "Tutti".
            if (_selected != null)
            {
                bool still = false;
                foreach (GroupInfo g in groups) if (g.Name == _selected) { still = true; break; }
                if (!still) _selected = null;
            }

            SuspendLayout();
            foreach (GroupRow r in _rows) Controls.Remove(r);
            _rows.Clear();

            GroupRow all = new GroupRow(null, Tr.T("Tutti i gruppi", "All groups"), allTotal, allChecked, true);
            Wire(all);
            _rows.Add(all);

            foreach (GroupInfo g in groups)
            {
                GroupRow r = new GroupRow(g.Name, Groups.Show(g.Name), g.Total, g.Checked, false);
                Wire(r);
                _rows.Add(r);
            }

            int y = Padding.Top - VerticalScroll.Value;
            foreach (GroupRow r in _rows)
            {
                r.Selected = (r.Group == _selected);
                r.Location = new Point(0, y);
                r.Width = ClientSize.Width;
                r.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
                Controls.Add(r);
                y += r.Height;
            }
            _newRow.Location = new Point(0, y + 6);
            _newRow.Width = ClientSize.Width;
            _newRow.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _newRow.BringToFront();
            ResumeLayout(true);
        }

        private void Wire(GroupRow r)
        {
            r.Toggled += delegate(bool on)
            {
                if (GroupToggled != null) GroupToggled(r.Group, on);
            };
            r.Picked += delegate
            {
                Select(r.Group);
            };
            r.MenuRequested += delegate(Point screen)
            {
                ShowMenu(r, screen);
            };
        }

        public void Select(string group)
        {
            _selected = group;
            foreach (GroupRow r in _rows) r.Selected = (r.Group == group);
            if (SelectionChanged != null) SelectionChanged(group);
        }

        private void ShowMenu(GroupRow r, Point screen)
        {
            ContextMenuStrip m = new ContextMenuStrip();
            m.Renderer = new FlatToolStripRenderer();
            m.Font = Theme.UI;

            ToolStripMenuItem on = new ToolStripMenuItem(Tr.T("Spunta tutto il gruppo", "Check the whole group"), Icons.Glyph(Icons.Multiselect, 16, Theme.Text));
            on.Click += delegate { if (GroupToggled != null) GroupToggled(r.Group, true); };
            ToolStripMenuItem off = new ToolStripMenuItem(Tr.T("Togli la spunta al gruppo", "Uncheck the whole group"), Icons.Glyph(Icons.ClearSelection, 16, Theme.Text));
            off.Click += delegate { if (GroupToggled != null) GroupToggled(r.Group, false); };
            m.Items.Add(on);
            m.Items.Add(off);

            if (r.Group != null)
            {
                m.Items.Add(new ToolStripSeparator());
                ToolStripMenuItem ren = new ToolStripMenuItem(Tr.T("Rinomina...", "Rename..."), Icons.Glyph(Icons.Rename, 16, Theme.Text));
                ren.Click += delegate { if (RenameRequested != null) RenameRequested(r.Group); };
                ToolStripMenuItem del = new ToolStripMenuItem(Tr.T("Elimina gruppo...", "Delete group..."), Icons.Glyph(Icons.Delete, 16, Theme.Text));
                del.Click += delegate { if (DeleteRequested != null) DeleteRequested(r.Group); };
                m.Items.Add(ren);
                m.Items.Add(del);
            }
            m.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem nw = new ToolStripMenuItem(Tr.T("Nuovo gruppo...", "New group..."), Icons.Glyph(Icons.NewFolder, 16, Theme.Text));
            nw.Click += delegate { if (NewGroupRequested != null) NewGroupRequested(); };
            m.Items.Add(nw);

            m.Show(screen);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            foreach (GroupRow r in _rows) r.Width = ClientSize.Width;
            _newRow.Width = ClientSize.Width;
        }

        // ---------- una riga ----------

        private class GroupRow : Control
        {
            public readonly string Group;
            public event Action<bool> Toggled;
            public event Action Picked;
            public event Action<Point> MenuRequested;

            private readonly string _label;
            private readonly int _total, _checked;
            private readonly bool _isAll;
            private bool _hover, _selected;
            private bool _hoverBox;

            public GroupRow(string group, string label, int total, int checkedCount, bool isAll)
            {
                Group = group;
                _label = label;
                _total = total;
                _checked = checkedCount;
                _isAll = isAll;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                       | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
                Font = Theme.UI;
                Height = Font.Height + 16;
                Cursor = Cursors.Hand;
            }

            public bool Selected
            {
                get { return _selected; }
                set { _selected = value; Invalidate(); }
            }

            private Rectangle BoxRect()
            {
                int s = 16;
                return new Rectangle(12, (Height - s) / 2, s, s);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.Clear(Theme.CardBg);

                if (_selected)
                {
                    using (SolidBrush b = new SolidBrush(Theme.SelectedBg))
                        g.FillRectangle(b, new Rectangle(4, 1, Width - 8, Height - 2));
                    using (SolidBrush b = new SolidBrush(Theme.Accent))
                        g.FillRectangle(b, new Rectangle(4, 5, 3, Height - 10));
                }
                else if (_hover)
                {
                    using (SolidBrush b = new SolidBrush(Theme.HoverBg))
                        g.FillRectangle(b, new Rectangle(4, 1, Width - 8, Height - 2));
                }

                // La casella: tre stati, disegnata dal tema di Windows.
                CheckBoxState state;
                if (_total == 0 || _checked == 0) state = _hoverBox ? CheckBoxState.UncheckedHot : CheckBoxState.UncheckedNormal;
                else if (_checked >= _total) state = _hoverBox ? CheckBoxState.CheckedHot : CheckBoxState.CheckedNormal;
                else state = _hoverBox ? CheckBoxState.MixedHot : CheckBoxState.MixedNormal;
                Rectangle box = BoxRect();
                if (Application.RenderWithVisualStyles)
                    CheckBoxRenderer.DrawCheckBox(g, box.Location, state);
                else
                    ControlPaint.DrawCheckBox(g, box, _checked > 0 ? ButtonState.Checked : ButtonState.Normal);

                // Il conteggio a destra, in grigio.
                string count = _total.ToString();
                Font countFont = Theme.UISmall;
                SizeF cs = g.MeasureString(count, countFont);
                int countW = (int)Math.Ceiling(cs.Width) + 10;
                Rectangle countRect = new Rectangle(Width - countW - 10, 0, countW, Height);
                TextRenderer.DrawText(g, count, countFont, countRect,
                    _total == 0 ? Theme.TextDisabled : Theme.TextSecondary,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

                // Il nome.
                Font f = _isAll ? Theme.Header : Theme.UI;
                Rectangle textRect = new Rectangle(box.Right + 10, 0, countRect.Left - box.Right - 14, Height);
                TextRenderer.DrawText(g, _label, f, textRect,
                    _total == 0 && !_isAll ? Theme.TextSecondary : Theme.Text,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            }

            protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); }
            protected override void OnMouseLeave(EventArgs e) { _hover = false; _hoverBox = false; Invalidate(); }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                Rectangle hit = BoxRect();
                hit.Inflate(6, 6);
                bool over = hit.Contains(e.Location);
                if (over != _hoverBox) { _hoverBox = over; Invalidate(); }
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                Focus();
                if (e.Button == MouseButtons.Right)
                {
                    if (MenuRequested != null) MenuRequested(PointToScreen(e.Location));
                    return;
                }
                if (e.Button != MouseButtons.Left) return;

                Rectangle hit = BoxRect();
                hit.Inflate(6, 6);
                if (hit.Contains(e.Location))
                {
                    // Da "tutto spuntato" si passa a niente; da ogni altro stato a tutto.
                    bool on = !(_total > 0 && _checked >= _total);
                    if (Toggled != null) Toggled(on);
                }
                else
                {
                    if (Picked != null) Picked();
                }
            }

            protected override void OnKeyDown(KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Space)
                {
                    bool on = !(_total > 0 && _checked >= _total);
                    if (Toggled != null) Toggled(on);
                    e.Handled = true;
                }
            }
        }

        // La riga "+ Nuovo gruppo" in fondo, in stile collegamento.
        private class LinkRow : Control
        {
            private readonly string _text;
            private readonly string _glyph;
            private bool _hover;

            public LinkRow(string text, string glyph)
            {
                _text = text;
                _glyph = glyph;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                       | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
                Font = Theme.UI;
                Height = Font.Height + 14;
                Cursor = Cursors.Hand;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.Clear(Theme.CardBg);
                Color c = _hover ? Theme.AccentHover : Theme.Accent;
                Bitmap ic = Icons.Glyph(_glyph, 14, c);
                int x = 14;
                if (ic != null)
                {
                    g.DrawImage(ic, x, (Height - 14) / 2, 14, 14);
                    x += 20;
                }
                TextRenderer.DrawText(g, _text, Font, new Rectangle(x, 0, Width - x, Height), c,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }

            protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); }
            protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); }
        }
    }
}
