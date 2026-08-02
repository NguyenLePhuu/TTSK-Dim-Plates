using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TTSK_AutoDim_Plates
{
    public class ShortcutSettingsForm : Form
    {
        private readonly ShortcutManager _manager;
        private readonly bool _darkMode;
        private readonly bool _autoSectionLocked;
        private readonly Action<bool> _applyAutoSectionSetting;
        private readonly Dictionary<string, Keys> _workingShortcuts;
        private readonly Dictionary<string, ShortcutRow> _rows;

        private ShortcutTextBox txtSearch;
        private Panel rowsPanel;
        private Label lblMessage;
        private Label lblThemeIcon;
        private Label lblAutoSectionState;
        private AutoSectionToggleSwitch headerAutoSectionSwitch;
        private AutoSectionToggleSwitch rowAutoSectionSwitch;
        private string _editingActionId;
        private Keys _editingModifierCandidate;
        private bool _workingAutoSectionEnabled;
        private bool _syncingAutoSectionSwitches;

        private Color _formBack;
        private Color _cardBack;
        private Color _cardBackHover;
        private Color _inputBack;
        private Color _textColor;
        private Color _mutedTextColor;
        private Color _borderColor;
        private Color _accentColor;
        private Color _accentSoftColor;
        private Color _dangerColor;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(
            IntPtr hwnd,
            int attr,
            ref int attrValue,
            int attrSize);

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        public ShortcutSettingsForm(
            ShortcutManager manager,
            bool darkMode,
            bool autoSectionEnabled,
            bool autoSectionLocked,
            Action<bool> applyAutoSectionSetting)
        {
            _manager = manager;
            _darkMode = darkMode;
            _autoSectionLocked = autoSectionLocked;
            _applyAutoSectionSetting = applyAutoSectionSetting;
            _workingAutoSectionEnabled = autoSectionEnabled;
            _rows = new Dictionary<string, ShortcutRow>(StringComparer.OrdinalIgnoreCase);
            _workingShortcuts = new Dictionary<string, Keys>(StringComparer.OrdinalIgnoreCase);

            IDictionary<string, Keys> current = manager.GetShortcutsCopy();
            foreach (KeyValuePair<string, Keys> pair in current)
                _workingShortcuts[pair.Key] = pair.Value;

            BuildPalette();
            BuildUi();
            PopulateRows();
            // Rows must exist before theming. Otherwise their custom Edit buttons
            // keep the default transparent fill until hover, which leaves stale
            // key text visible underneath them.
            ApplyTheme();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyWindowTitleBarTheme();
        }

        private void ApplyWindowTitleBarTheme()
        {
            try
            {
                int useDark = _darkMode ? 1 : 0;
                DwmSetWindowAttribute(
                    Handle,
                    DWMWA_USE_IMMERSIVE_DARK_MODE,
                    ref useDark,
                    sizeof(int));
            }
            catch
            {
            }
        }

        private void BuildPalette()
        {
            if (_darkMode)
            {
                _formBack = Color.FromArgb(10, 10, 10);
                _cardBack = Color.FromArgb(21, 21, 21);
                _cardBackHover = Color.FromArgb(30, 26, 22);
                _inputBack = Color.FromArgb(18, 18, 18);
                _textColor = Color.FromArgb(242, 245, 248);
                _mutedTextColor = Color.FromArgb(154, 163, 175);
                _borderColor = Color.FromArgb(73, 56, 43);
                _accentColor = Color.FromArgb(224, 126, 35);
                _accentSoftColor = Color.FromArgb(42, 28, 18);
                _dangerColor = Color.FromArgb(248, 113, 113);
            }
            else
            {
                _formBack = Color.FromArgb(249, 250, 252);
                _cardBack = Color.FromArgb(255, 255, 255);
                _cardBackHover = Color.FromArgb(239, 246, 255);
                _inputBack = Color.FromArgb(255, 255, 255);
                _textColor = Color.FromArgb(17, 24, 39);
                _mutedTextColor = Color.FromArgb(100, 116, 139);
                _borderColor = Color.FromArgb(203, 213, 225);
                _accentColor = Color.FromArgb(37, 99, 235);
                _accentSoftColor = Color.FromArgb(219, 234, 254);
                _dangerColor = Color.FromArgb(220, 38, 38);
            }
        }

        private void BuildUi()
        {
            Text = "Shortcut Settings";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = true;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(760, 640);
            Font = new Font("Segoe UI", 9F);
            KeyPreview = true;

            try
            {
                string logoPath = Path.Combine(Application.StartupPath, "Resources", "logo.png");
                if (File.Exists(logoPath))
                {
                    using (Bitmap bitmap = new Bitmap(logoPath))
                    {
                        IntPtr hIcon = bitmap.GetHicon();
                        Icon = Icon.FromHandle(hIcon);
                    }
                }
            }
            catch
            {
            }

            PictureBox logo = new PictureBox();
            logo.SizeMode = PictureBoxSizeMode.Zoom;
            logo.Location = new Point(18, 13);
            logo.Size = new Size(30, 30);
            try
            {
                string logoPath = Path.Combine(Application.StartupPath, "Resources", "logo.png");
                if (File.Exists(logoPath))
                    logo.Image = Image.FromFile(logoPath);
            }
            catch
            {
            }
            Controls.Add(logo);

            Label titleBar = new Label();
            titleBar.Text = "Shortcut Settings";
            titleBar.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            titleBar.Location = new Point(56, 16);
            titleBar.Size = new Size(240, 24);
            Controls.Add(titleBar);

            ShortcutRoundPanel headerIcon = new ShortcutRoundPanel();
            headerIcon.BorderRadius = 12;
            headerIcon.Location = new Point(28, 74);
            headerIcon.Size = new Size(80, 74);
            Controls.Add(headerIcon);

            ShortcutCenteredGlyph keyboard = new ShortcutCenteredGlyph();
            keyboard.Text = "⌨";
            keyboard.Font = new Font("Segoe UI", 25F, FontStyle.Bold);
            keyboard.Dock = DockStyle.Fill;
            keyboard.BackColor = Color.Transparent;
            headerIcon.Controls.Add(keyboard);

            Label title = new Label();
            title.Text = "Customize Shortcuts";
            title.Font = new Font("Segoe UI", 16F, FontStyle.Bold);
            title.Location = new Point(126, 78);
            title.Size = new Size(320, 32);
            Controls.Add(title);

            Label subtitle = new Label();
            subtitle.Text = "Set your preferred keyboard shortcuts for AutoDim functions.";
            subtitle.Font = new Font("Segoe UI", 9.3F, FontStyle.Bold);
            subtitle.Location = new Point(128, 112);
            subtitle.Size = new Size(430, 22);
            Controls.Add(subtitle);

            ShortcutRoundPanel autoSectionCard = new ShortcutRoundPanel();
            autoSectionCard.BorderRadius = 12;
            autoSectionCard.Location = new Point(568, 74);
            autoSectionCard.Size = new Size(164, 74);
            Controls.Add(autoSectionCard);

            Label autoSectionTitle = new Label();
            autoSectionTitle.Text = "Auto Section";
            autoSectionTitle.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            autoSectionTitle.Location = new Point(12, 9);
            autoSectionTitle.Size = new Size(100, 20);
            autoSectionTitle.BackColor = Color.Transparent;
            autoSectionCard.Controls.Add(autoSectionTitle);

            lblAutoSectionState = new Label();
            lblAutoSectionState.Font = new Font("Segoe UI", 7.8F, FontStyle.Bold);
            lblAutoSectionState.Location = new Point(12, 36);
            lblAutoSectionState.Size = new Size(92, 20);
            lblAutoSectionState.BackColor = Color.Transparent;
            autoSectionCard.Controls.Add(lblAutoSectionState);

            headerAutoSectionSwitch = new AutoSectionToggleSwitch();
            headerAutoSectionSwitch.Location = new Point(108, 25);
            headerAutoSectionSwitch.Size = new Size(44, 24);
            headerAutoSectionSwitch.Checked = _workingAutoSectionEnabled;
            headerAutoSectionSwitch.Enabled = !_autoSectionLocked;
            headerAutoSectionSwitch.CheckedChanged += delegate
            {
                SetWorkingAutoSectionEnabled(headerAutoSectionSwitch.Checked);
            };
            autoSectionCard.Controls.Add(headerAutoSectionSwitch);
            UpdateAutoSectionStateLabel();

            txtSearch = new ShortcutTextBox();
            txtSearch.Location = new Point(28, 170);
            txtSearch.Size = new Size(555, 32);
            txtSearch.Font = new Font("Segoe UI", 10F);
            txtSearch.TextChanged += delegate { ApplySearchFilter(); };
            Controls.Add(txtSearch);

            Label searchIcon = new Label();
            searchIcon.Text = "";
            searchIcon.Font = new Font("Segoe MDL2 Assets", 10.5F, FontStyle.Regular);
            searchIcon.TextAlign = ContentAlignment.MiddleCenter;
            searchIcon.Location = new Point(43, 176);
            searchIcon.Size = new Size(18, 18);
            searchIcon.BackColor = Color.Transparent;
            searchIcon.Cursor = Cursors.IBeam;
            searchIcon.Click += delegate { txtSearch.Focus(); };
            Controls.Add(searchIcon);
            searchIcon.BringToFront();

            rowsPanel = new ShortcutRowsPanel();
            rowsPanel.Location = new Point(28, 216);
            rowsPanel.Size = new Size(704, 278);
            rowsPanel.AutoScroll = false;
            Controls.Add(rowsPanel);

            ShortcutRoundPanel info = new ShortcutRoundPanel();
            info.BorderRadius = 12;
            info.Location = new Point(28, 506);
            info.Size = new Size(704, 72);
            Controls.Add(info);

            Label infoIcon = new Label();
            infoIcon.Text = "ⓘ";
            infoIcon.Font = new Font("Segoe UI", 17F, FontStyle.Bold);
            infoIcon.TextAlign = ContentAlignment.MiddleCenter;
            infoIcon.Location = new Point(18, 16);
            infoIcon.Size = new Size(34, 34);
            infoIcon.BackColor = Color.Transparent;
            info.Controls.Add(infoIcon);

            Label infoText = new Label();
            infoText.Text = "Shortcuts are active only when the AutoDim window is open.\nClick Edit, then press a shortcut. Backspace clears it; Esc cancels.";
            infoText.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            infoText.Location = new Point(68, 16);
            infoText.Size = new Size(560, 44);
            infoText.BackColor = Color.Transparent;
            info.Controls.Add(infoText);

            lblMessage = new Label();
            lblMessage.Text = "";
            lblMessage.Font = new Font("Segoe UI", 8.5F, FontStyle.Bold);
            lblMessage.TextAlign = ContentAlignment.MiddleLeft;
            lblMessage.Location = new Point(244, 596);
            lblMessage.Size = new Size(220, 24);
            Controls.Add(lblMessage);

            ShortcutFlatButton reset = new ShortcutFlatButton();
            reset.Text = "⟳  Reset to Default";
            reset.Location = new Point(28, 590);
            reset.Size = new Size(200, 38);
            reset.Click += delegate { ResetToDefault(); };
            Controls.Add(reset);

            ShortcutFlatButton save = new ShortcutFlatButton();
            save.Text = "▣  Save";
            save.IsPrimary = true;
            save.Location = new Point(472, 590);
            save.Size = new Size(130, 38);
            save.Click += delegate { SaveAndClose(); };
            Controls.Add(save);

            ShortcutFlatButton cancel = new ShortcutFlatButton();
            cancel.Text = "Cancel";
            cancel.Location = new Point(614, 590);
            cancel.Size = new Size(118, 38);
            cancel.Click += delegate
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };
            Controls.Add(cancel);
        }

        private void PopulateRows()
        {
            rowsPanel.Controls.Clear();
            _rows.Clear();

            IList<ShortcutActionDefinition> defs = _manager.GetDefinitions();

            for (int i = 0; i < defs.Count; i++)
            {
                ShortcutActionDefinition def = defs[i];
                ShortcutRow row = CreateRow(def, i);
                _rows[def.ActionId] = row;
                rowsPanel.Controls.Add(row.Panel);
            }

            ApplySearchFilter();
        }

        private ShortcutRow CreateRow(ShortcutActionDefinition def, int index)
        {
            ShortcutRoundPanel card = new ShortcutRoundPanel();
            card.BorderRadius = 10;
            card.Location = new Point(0, index * 64);
            card.Size = new Size(668, 58);
            card.Cursor = Cursors.Hand;
            card.Tag = def;

            ShortcutBadge badge = new ShortcutBadge();
            badge.Text = def.IconText;
            badge.Location = new Point(18, 13);
            badge.Size = new Size(34, 34);
            badge.BadgeColor = GetBadgeColor(def.ActionId);
            card.Controls.Add(badge);

            Label name = new Label();
            name.Text = def.DisplayName;
            name.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            name.Location = new Point(72, 12);
            name.Size = new Size(270, 22);
            name.BackColor = Color.Transparent;
            card.Controls.Add(name);

            Label desc = new Label();
            desc.Text = def.Description;
            desc.Font = new Font("Segoe UI", 7.9F, FontStyle.Bold);
            desc.Location = new Point(72, 34);
            desc.Size = new Size(270, 16);
            desc.BackColor = Color.Transparent;
            card.Controls.Add(desc);

            if (string.Equals(def.ActionId, ShortcutManager.ActionAutoSection, StringComparison.OrdinalIgnoreCase))
            {
                rowAutoSectionSwitch = new AutoSectionToggleSwitch();
                rowAutoSectionSwitch.Location = new Point(350, 18);
                rowAutoSectionSwitch.Size = new Size(36, 22);
                rowAutoSectionSwitch.Checked = _workingAutoSectionEnabled;
                rowAutoSectionSwitch.Enabled = !_autoSectionLocked;
                rowAutoSectionSwitch.CheckedChanged += delegate
                {
                    SetWorkingAutoSectionEnabled(rowAutoSectionSwitch.Checked);
                };
                card.Controls.Add(rowAutoSectionSwitch);
            }

            ShortcutKeyBox keyBox = new ShortcutKeyBox();
            keyBox.Location = new Point(398, 13);
            keyBox.Size = new Size(154, 34);
            keyBox.Text = ShortcutManager.Format(GetWorkingShortcut(def.ActionId));
            card.Controls.Add(keyBox);

            ShortcutFlatButton edit = new ShortcutFlatButton();
            edit.Text = "Edit";
            edit.UseNeutralStyle = true;
            edit.Location = new Point(572, 12);
            edit.Size = new Size(82, 36);
            edit.Click += delegate { ToggleEdit(def.ActionId); };
            card.Controls.Add(edit);

            card.Click += delegate { BeginEdit(def.ActionId); };
            name.Click += delegate { BeginEdit(def.ActionId); };
            desc.Click += delegate { BeginEdit(def.ActionId); };
            badge.Click += delegate { BeginEdit(def.ActionId); };
            keyBox.Click += delegate { BeginEdit(def.ActionId); };

            ShortcutRow row = new ShortcutRow();
            row.ActionId = def.ActionId;
            row.Panel = card;
            row.NameLabel = name;
            row.DescriptionLabel = desc;
            row.KeyBox = keyBox;
            row.EditButton = edit;
            row.Definition = def;
            return row;
        }

        private Keys GetWorkingShortcut(string actionId)
        {
            if (_workingShortcuts.ContainsKey(actionId))
                return _workingShortcuts[actionId];

            ShortcutActionDefinition def = _manager.FindDefinition(actionId);
            if (def != null)
                return def.DefaultShortcut;

            return Keys.None;
        }

        private void SetWorkingAutoSectionEnabled(bool enabled)
        {
            if (_syncingAutoSectionSwitches)
                return;

            _workingAutoSectionEnabled = enabled;
            _syncingAutoSectionSwitches = true;

            try
            {
                if (headerAutoSectionSwitch != null)
                    headerAutoSectionSwitch.Checked = enabled;
                if (rowAutoSectionSwitch != null)
                    rowAutoSectionSwitch.Checked = enabled;
            }
            finally
            {
                _syncingAutoSectionSwitches = false;
            }

            UpdateAutoSectionStateLabel();
        }

        private void UpdateAutoSectionStateLabel()
        {
            if (lblAutoSectionState == null)
                return;

            string state = _workingAutoSectionEnabled ? "ON" : "OFF";
            lblAutoSectionState.Text = _autoSectionLocked ? state + "  LOCKED" : state;
            lblAutoSectionState.ForeColor = _workingAutoSectionEnabled ? _accentColor : _mutedTextColor;
        }

        private void BeginEdit(string actionId)
        {
            _editingActionId = actionId;
            _editingModifierCandidate = Keys.None;
            ShowMessage("Press new shortcut. Backspace = clear; Esc = cancel.", false);
            RefreshRowsVisualState();
            Focus();
        }

        private void ToggleEdit(string actionId)
        {
            if (string.Equals(_editingActionId, actionId, StringComparison.OrdinalIgnoreCase))
            {
                ShowMessage("Edit cancelled.", false);
                EndEdit();
                return;
            }

            BeginEdit(actionId);
        }

        private void EndEdit()
        {
            _editingActionId = null;
            _editingModifierCandidate = Keys.None;
            RefreshRowsVisualState();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (!string.IsNullOrEmpty(_editingActionId))
            {
                if ((keyData & Keys.KeyCode) == Keys.Back &&
                    (keyData & Keys.Modifiers) == Keys.None)
                {
                    ClearEditingShortcut();
                    return true;
                }

                if ((keyData & Keys.KeyCode) == Keys.Escape)
                {
                    ShowMessage("Edit cancelled.", false);
                    EndEdit();
                    return true;
                }

                Keys normalized = ShortcutManager.NormalizeShortcut(keyData);

                if ((normalized & Keys.Alt) == Keys.Alt)
                {
                    _editingModifierCandidate = Keys.None;
                    return true;
                }

                if (ShortcutManager.IsBareModifier(normalized))
                {
                    if (ShortcutManager.IsAllowedModifierOnlyShortcut(
                        _editingActionId,
                        normalized & Keys.Modifiers))
                        _editingModifierCandidate = normalized & Keys.Modifiers;
                    return true;
                }

                ApplyEditingShortcut(normalized);
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void ClearEditingShortcut()
        {
            if (string.IsNullOrEmpty(_editingActionId))
                return;

            _workingShortcuts[_editingActionId] = Keys.None;
            ShowMessage("Shortcut cleared. Click Save to apply.", false);
            EndEdit();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (!string.IsNullOrEmpty(_editingActionId))
            {
                Keys keyCode = e.KeyCode & Keys.KeyCode;
                if (ShortcutManager.IsBareModifier(keyCode))
                {
                    Keys modifiers = e.Modifiers & Keys.Modifiers;
                    if (ShortcutManager.IsAllowedModifierOnlyShortcut(_editingActionId, modifiers))
                        _editingModifierCandidate = modifiers;
                    else
                        _editingModifierCandidate = Keys.None;

                    e.Handled = true;
                    e.SuppressKeyPress = true;
                }
                else
                {
                    _editingModifierCandidate = Keys.None;
                }
            }

            base.OnKeyDown(e);
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            if (!string.IsNullOrEmpty(_editingActionId) &&
                _editingModifierCandidate != Keys.None &&
                (ModifierKeys & Keys.Modifiers) == Keys.None)
            {
                Keys candidate = _editingModifierCandidate;
                _editingModifierCandidate = Keys.None;
                ApplyEditingShortcut(candidate);
                e.Handled = true;
                e.SuppressKeyPress = true;
            }

            base.OnKeyUp(e);
        }

        private void ApplyEditingShortcut(Keys normalized)
        {
            if (string.IsNullOrEmpty(_editingActionId))
                return;

            normalized = ShortcutManager.NormalizeShortcut(normalized);

            if ((normalized & Keys.Alt) == Keys.Alt)
                return;

            string duplicateActionId = FindDuplicateAction(normalized, _editingActionId);
            if (!string.IsNullOrEmpty(duplicateActionId))
            {
                ShortcutActionDefinition dup = _manager.FindDefinition(duplicateActionId);
                string duplicateName = dup != null ? dup.DisplayName : duplicateActionId;
                ShowMessage(ShortcutManager.Format(normalized) + " already used by " + duplicateName + ".", true);
                return;
            }

            _workingShortcuts[_editingActionId] = normalized;
            ShowMessage("Shortcut updated: " + ShortcutManager.Format(normalized), false);
            EndEdit();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            ShortcutRowsPanel scrollingPanel = rowsPanel as ShortcutRowsPanel;
            if (scrollingPanel != null && scrollingPanel.IsHandleCreated)
            {
                Point cursor = scrollingPanel.PointToClient(Cursor.Position);
                if (scrollingPanel.ClientRectangle.Contains(cursor))
                {
                    scrollingPanel.ScrollByWheelDelta(e.Delta);
                    return;
                }
            }

            base.OnMouseWheel(e);
        }

        private string FindDuplicateAction(Keys keys, string exceptActionId)
        {
            Keys target = ShortcutManager.NormalizeShortcut(keys);

            foreach (KeyValuePair<string, Keys> pair in _workingShortcuts)
            {
                if (string.Equals(pair.Key, exceptActionId, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (ShortcutManager.NormalizeShortcut(pair.Value) == target)
                    return pair.Key;
            }

            return null;
        }

        private void ResetToDefault()
        {
            _workingShortcuts.Clear();

            IDictionary<string, Keys> defaults = _manager.GetDefaultShortcutsCopy();
            foreach (KeyValuePair<string, Keys> pair in defaults)
                _workingShortcuts[pair.Key] = pair.Value;

            SetWorkingAutoSectionEnabled(false);

            ShowMessage("Default shortcuts restored. Click Save to apply.", false);
            EndEdit();
            RefreshRowsVisualState();
        }

        private void SaveAndClose()
        {
            string message;
            if (!_manager.TryValidate(_workingShortcuts, out message))
            {
                ShowMessage(message, true);
                return;
            }

            _manager.SetShortcuts(_workingShortcuts);
            _manager.Save();
            if (_applyAutoSectionSetting != null)
                _applyAutoSectionSetting(_workingAutoSectionEnabled);
            DialogResult = DialogResult.OK;
            Close();
        }

        private void ShowMessage(string text, bool danger)
        {
            if (lblMessage == null)
                return;

            lblMessage.Text = text;
            lblMessage.ForeColor = danger ? _dangerColor : _accentColor;
            lblMessage.BringToFront();
        }

        private void ApplySearchFilter()
        {
            if (rowsPanel == null)
                return;

            string filter = txtSearch == null ? string.Empty : txtSearch.Text.Trim();
            int y = 0;
            ShortcutRowsPanel customRowsPanel = rowsPanel as ShortcutRowsPanel;
            if (customRowsPanel != null)
                customRowsPanel.BeginContentLayout();

            foreach (ShortcutActionDefinition def in _manager.GetDefinitions())
            {
                ShortcutRow row;
                if (!_rows.TryGetValue(def.ActionId, out row))
                    continue;

                bool visible = string.IsNullOrEmpty(filter) ||
                    def.DisplayName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    def.Description.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    ShortcutManager.Format(GetWorkingShortcut(def.ActionId)).IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;

                row.Panel.Visible = visible;

                if (visible)
                {
                    if (customRowsPanel != null)
                        customRowsPanel.SetChildTop(row.Panel, y);
                    else
                        row.Panel.Location = new Point(0, y);
                    y += 64;
                }
            }

            if (customRowsPanel != null)
                customRowsPanel.EndContentLayout(y);

            RefreshRowsVisualState();
        }

        private void RefreshRowsVisualState()
        {
            foreach (ShortcutRow row in _rows.Values)
            {
                bool editing = string.Equals(row.ActionId, _editingActionId, StringComparison.OrdinalIgnoreCase);

                row.KeyBox.Text = editing ? "LISTENING..." : ShortcutManager.Format(GetWorkingShortcut(row.ActionId));
                row.KeyBox.IsListening = editing;
                row.KeyBox.Invalidate();

                row.Panel.BorderColor = editing ? _accentColor : _borderColor;
                row.Panel.BackColor = editing ? _cardBackHover : _cardBack;
                row.Panel.Invalidate();

                row.EditButton.Text = editing ? "Cancel" : "Edit";
                row.EditButton.Invalidate();
            }
        }

        private Color GetBadgeColor(string actionId)
        {
            if (string.Equals(actionId, ShortcutManager.ActionCreateDrawing, StringComparison.OrdinalIgnoreCase))
                return Color.FromArgb(249, 115, 22);
            if (string.Equals(actionId, ShortcutManager.ActionBatchCreate, StringComparison.OrdinalIgnoreCase))
                return Color.FromArgb(244, 114, 112);
            if (string.Equals(actionId, ShortcutManager.ActionCheckScale, StringComparison.OrdinalIgnoreCase))
                return Color.FromArgb(201, 122, 64);
            if (string.Equals(actionId, ShortcutManager.ActionLineDistance, StringComparison.OrdinalIgnoreCase))
                return Color.FromArgb(45, 180, 168);
            if (string.Equals(actionId, ShortcutManager.ActionRepeatLast, StringComparison.OrdinalIgnoreCase))
                return Color.FromArgb(167, 139, 250);
            if (string.Equals(actionId, ShortcutManager.ActionOpenGrid, StringComparison.OrdinalIgnoreCase))
                return Color.FromArgb(245, 183, 77);
            if (string.Equals(actionId, ShortcutManager.ActionFitView, StringComparison.OrdinalIgnoreCase))
                return Color.FromArgb(96, 165, 250);
            if (string.Equals(actionId, ShortcutManager.ActionNeighborGrid, StringComparison.OrdinalIgnoreCase))
                return Color.FromArgb(34, 197, 94);
            if (string.Equals(actionId, ShortcutManager.ActionArrangeView, StringComparison.OrdinalIgnoreCase))
                return Color.FromArgb(124, 58, 237);
            if (string.Equals(actionId, ShortcutManager.ActionAutoSection, StringComparison.OrdinalIgnoreCase))
                return Color.FromArgb(217, 119, 6);
            if (string.Equals(actionId, ShortcutManager.ActionSlot01, StringComparison.OrdinalIgnoreCase))
                return Color.FromArgb(13, 148, 136);
            if (string.Equals(actionId, ShortcutManager.ActionSlot02, StringComparison.OrdinalIgnoreCase))
                return Color.FromArgb(14, 165, 233);
            if (string.Equals(actionId, ShortcutManager.ActionSlot03, StringComparison.OrdinalIgnoreCase))
                return Color.FromArgb(139, 92, 246);
            if (string.Equals(actionId, ShortcutManager.ActionSlot04, StringComparison.OrdinalIgnoreCase))
                return Color.FromArgb(245, 158, 11);
            if (string.Equals(actionId, ShortcutManager.ActionSlot05, StringComparison.OrdinalIgnoreCase))
                return Color.FromArgb(239, 68, 68);
            if (string.Equals(actionId, ShortcutManager.ActionSlot06, StringComparison.OrdinalIgnoreCase))
                return Color.FromArgb(20, 184, 166);

            return _accentColor;
        }

        private void ApplyTheme()
        {
            BackColor = _formBack;
            ForeColor = _textColor;

            foreach (Control control in Controls)
                ApplyThemeToControl(control);

            UpdateAutoSectionStateLabel();
            RefreshRowsVisualState();
            ApplyWindowTitleBarTheme();
        }

        private void ApplyThemeToControl(Control control)
        {
            if (control == null)
                return;

            ShortcutRoundPanel panel = control as ShortcutRoundPanel;
            if (panel != null)
            {
                panel.BackColor = _cardBack;
                panel.BorderColor = _borderColor;
            }
            else if (control is Panel)
            {
                control.BackColor = _formBack;
            }

            ShortcutRowsPanel scrollingPanel = control as ShortcutRowsPanel;
            if (scrollingPanel != null)
            {
                scrollingPanel.ScrollTrackColor = _darkMode
                    ? Color.FromArgb(18, 18, 18)
                    : Color.FromArgb(241, 245, 249);
                scrollingPanel.ScrollThumbColor = _darkMode
                    ? Color.FromArgb(95, 82, 70)
                    : Color.FromArgb(148, 163, 184);
                scrollingPanel.ScrollThumbHoverColor = _darkMode
                    ? Color.FromArgb(128, 105, 82)
                    : Color.FromArgb(100, 116, 139);
            }

            ShortcutTextBox textBox = control as ShortcutTextBox;
            if (textBox != null)
            {
                textBox.BackColor = _inputBack;
                textBox.ForeColor = _textColor;
                textBox.BorderColor = _darkMode ? _borderColor : _accentColor;
                textBox.PlaceholderColor = _mutedTextColor;
                textBox.PlaceholderText = "Search function...";
                textBox.LeftPadding = 48;
                textBox.Invalidate();
            }

            ShortcutFlatButton button = control as ShortcutFlatButton;
            if (button != null)
            {
                button.BackColor = _darkMode ? _formBack : Color.White;
                button.BackColorValue = button.IsPrimary
                    ? _accentColor
                    : (button.UseNeutralStyle
                        ? (_darkMode ? Color.FromArgb(18, 24, 38) : Color.White)
                        : (_darkMode ? Color.FromArgb(18, 18, 18) : Color.White));
                button.BorderColor = button.IsPrimary
                    ? _accentColor
                    : (button.UseNeutralStyle
                        ? (_darkMode ? Color.FromArgb(71, 85, 105) : Color.FromArgb(203, 213, 225))
                        : _accentColor);
                button.TextColor = button.IsPrimary
                    ? Color.White
                    : (button.UseNeutralStyle
                        ? (_darkMode ? Color.FromArgb(226, 232, 240) : Color.FromArgb(51, 65, 85))
                        : _accentColor);
                button.HoverBackColor = button.IsPrimary
                    ? (_darkMode ? Color.FromArgb(238, 139, 50) : Color.FromArgb(29, 78, 216))
                    : (button.UseNeutralStyle
                        ? (_darkMode ? Color.FromArgb(30, 41, 59) : Color.FromArgb(241, 245, 249))
                        : (_darkMode ? Color.FromArgb(37, 26, 18) : _accentSoftColor));
                button.Invalidate();
            }

            ShortcutKeyBox keyBox = control as ShortcutKeyBox;
            if (keyBox != null)
            {
                keyBox.BackColor = _darkMode ? _cardBack : Color.FromArgb(248, 250, 252);
                keyBox.BackColorValue = _darkMode
                    ? Color.FromArgb(15, 23, 42)
                    : Color.FromArgb(248, 250, 252);
                keyBox.BorderColor = _darkMode
                    ? Color.FromArgb(71, 85, 105)
                    : Color.FromArgb(203, 213, 225);
                keyBox.TextColor = _darkMode
                    ? Color.FromArgb(226, 232, 240)
                    : Color.FromArgb(51, 65, 85);
                keyBox.ListeningBackColor = _darkMode
                    ? Color.FromArgb(30, 58, 95)
                    : Color.FromArgb(239, 246, 255);
                keyBox.ListeningBorderColor = _darkMode
                    ? Color.FromArgb(96, 165, 250)
                    : Color.FromArgb(37, 99, 235);
                keyBox.Invalidate();
            }

            ShortcutBadge badge = control as ShortcutBadge;
            if (badge != null)
            {
                badge.TextColor = Color.White;
                badge.Invalidate();
            }

            AutoSectionToggleSwitch autoSectionSwitch = control as AutoSectionToggleSwitch;
            if (autoSectionSwitch != null)
            {
                autoSectionSwitch.OnColor = _accentColor;
                autoSectionSwitch.OffColor = _darkMode
                    ? Color.FromArgb(73, 56, 43)
                    : Color.FromArgb(203, 213, 225);
                autoSectionSwitch.DisabledTrackColor = _darkMode
                    ? Color.FromArgb(47, 43, 40)
                    : Color.FromArgb(226, 232, 240);
                autoSectionSwitch.KnobColor = Color.White;
                autoSectionSwitch.Invalidate();
            }

            ShortcutCenteredGlyph centeredGlyph = control as ShortcutCenteredGlyph;
            if (centeredGlyph != null)
            {
                centeredGlyph.GlyphColor = _textColor;
                centeredGlyph.Invalidate();
            }

            Label label = control as Label;
            if (label != null)
            {
                label.BackColor = Color.Transparent;

                if (label == lblThemeIcon)
                    label.ForeColor = _accentColor;
                else if (label.Font != null && string.Equals(label.Font.FontFamily.Name, "Segoe MDL2 Assets", StringComparison.OrdinalIgnoreCase))
                    label.ForeColor = _mutedTextColor;
                else if (label.Font != null && label.Font.Size <= 8.5F)
                    label.ForeColor = _mutedTextColor;
                else
                    label.ForeColor = _textColor;
            }

            foreach (Control child in control.Controls)
                ApplyThemeToControl(child);
        }

        private sealed class ShortcutRow
        {
            public string ActionId;
            public ShortcutActionDefinition Definition;
            public ShortcutRoundPanel Panel;
            public Label NameLabel;
            public Label DescriptionLabel;
            public ShortcutKeyBox KeyBox;
            public ShortcutFlatButton EditButton;
        }

        private class ShortcutRoundPanel : Panel
        {
            public int BorderRadius { get; set; }
            public Color BorderColor { get; set; }

            public ShortcutRoundPanel()
            {
                BorderRadius = 10;
                BorderColor = Color.FromArgb(226, 232, 240);
                DoubleBuffered = true;
                BorderStyle = BorderStyle.None;
                SetStyle(ControlStyles.UserPaint, true);
                SetStyle(ControlStyles.AllPaintingInWmPaint, true);
                SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
                SetStyle(ControlStyles.ResizeRedraw, true);
            }

            protected override void OnPaintBackground(PaintEventArgs e)
            {
                Color parent = Parent != null ? Parent.BackColor : Color.FromArgb(249, 250, 252);
                using (SolidBrush brush = new SolidBrush(parent))
                    e.Graphics.FillRectangle(brush, ClientRectangle);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;

                RectangleF shadowRect = new RectangleF(3, 4, Width - 7, Height - 8);
                using (GraphicsPath shadowPath = RoundedRectF(shadowRect, BorderRadius))
                using (SolidBrush shadow = new SolidBrush(Color.FromArgb(28, Color.Black)))
                    e.Graphics.FillPath(shadow, shadowPath);

                RectangleF rect = new RectangleF(1, 1, Width - 3, Height - 4);
                using (GraphicsPath path = RoundedRectF(rect, BorderRadius))
                {
                    using (SolidBrush brush = new SolidBrush(BackColor))
                        e.Graphics.FillPath(brush, path);

                    using (Pen pen = new Pen(BorderColor, 1.2f))
                        e.Graphics.DrawPath(pen, path);
                }
            }
        }

        private class AutoSectionToggleSwitch : Control
        {
            private bool _checked;

            public event EventHandler CheckedChanged;

            public Color OnColor { get; set; }
            public Color OffColor { get; set; }
            public Color DisabledTrackColor { get; set; }
            public Color KnobColor { get; set; }

            public bool Checked
            {
                get { return _checked; }
                set
                {
                    if (_checked == value)
                        return;

                    _checked = value;
                    Invalidate();

                    EventHandler handler = CheckedChanged;
                    if (handler != null)
                        handler(this, EventArgs.Empty);
                }
            }

            public AutoSectionToggleSwitch()
            {
                DoubleBuffered = true;
                SetStyle(ControlStyles.UserPaint, true);
                SetStyle(ControlStyles.AllPaintingInWmPaint, true);
                SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
                SetStyle(ControlStyles.ResizeRedraw, true);
                SetStyle(ControlStyles.Selectable, true);
                TabStop = true;
                Cursor = Cursors.Hand;
                OnColor = Color.FromArgb(37, 99, 235);
                OffColor = Color.FromArgb(203, 213, 225);
                DisabledTrackColor = Color.FromArgb(226, 232, 240);
                KnobColor = Color.White;
            }

            protected override void OnClick(EventArgs e)
            {
                if (!Enabled)
                    return;

                Checked = !Checked;
                base.OnClick(e);
            }

            protected override void OnKeyDown(KeyEventArgs e)
            {
                if (Enabled && (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter))
                {
                    OnClick(EventArgs.Empty);
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                }

                base.OnKeyDown(e);
            }

            protected override void OnEnabledChanged(EventArgs e)
            {
                Cursor = Enabled ? Cursors.Hand : Cursors.Default;
                Invalidate();
                base.OnEnabledChanged(e);
            }

            protected override void OnPaintBackground(PaintEventArgs e)
            {
                Color background = Parent != null ? Parent.BackColor : BackColor;
                using (SolidBrush brush = new SolidBrush(background))
                    e.Graphics.FillRectangle(brush, ClientRectangle);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;

                Color trackColor = Enabled
                    ? (Checked ? OnColor : OffColor)
                    : DisabledTrackColor;
                RectangleF trackRect = new RectangleF(1, 1, Width - 2, Height - 2);

                using (GraphicsPath trackPath = RoundedRectF(trackRect, trackRect.Height / 2f))
                {
                    using (SolidBrush trackBrush = new SolidBrush(trackColor))
                        e.Graphics.FillPath(trackBrush, trackPath);

                    using (Pen borderPen = new Pen(Mix(trackColor, Color.Black, 0.12), 1f))
                        e.Graphics.DrawPath(borderPen, trackPath);
                }

                float knobDiameter = Math.Max(8f, Height - 6f);
                float knobX = Checked ? Width - knobDiameter - 3f : 3f;
                RectangleF knobRect = new RectangleF(knobX, 3f, knobDiameter, knobDiameter);
                Color knobColor = Enabled ? KnobColor : Mix(KnobColor, DisabledTrackColor, 0.35);

                using (SolidBrush knobBrush = new SolidBrush(knobColor))
                    e.Graphics.FillEllipse(knobBrush, knobRect);
            }
        }

        private class ShortcutFlatButton : Control
        {
            private bool _hovered;
            private bool _pressed;
            private bool _keyboardPressed;

            public bool IsPrimary { get; set; }
            public bool UseNeutralStyle { get; set; }
            public Color BackColorValue { get; set; }
            public Color HoverBackColor { get; set; }
            public Color BorderColor { get; set; }
            public Color TextColor { get; set; }

            public ShortcutFlatButton()
            {
                DoubleBuffered = true;
                SetStyle(ControlStyles.UserPaint, true);
                SetStyle(ControlStyles.AllPaintingInWmPaint, true);
                SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
                SetStyle(ControlStyles.ResizeRedraw, true);
                SetStyle(ControlStyles.Selectable, true);
                Font = new Font("Segoe UI", 9F, FontStyle.Bold);
                Cursor = Cursors.Hand;
                TabStop = true;
                BackColorValue = Color.Transparent;
                HoverBackColor = Color.FromArgb(255, 237, 213);
                BorderColor = Color.FromArgb(226, 232, 240);
                TextColor = Color.FromArgb(234, 88, 12);
            }

            protected override void OnPaintBackground(PaintEventArgs pevent)
            {
                // A Button keeps its own native background buffer.  With a custom
                // rounded OnPaint that buffer can leak through at the corners and,
                // after scrolling, can even retain text from the previous frame.
                // Always start from the real parent's background instead.
                Color background = Parent != null ? Parent.BackColor : BackColor;
                using (SolidBrush brush = new SolidBrush(background))
                    pevent.Graphics.FillRectangle(brush, ClientRectangle);
            }

            protected override void OnMouseEnter(EventArgs e)
            {
                _hovered = true;
                Invalidate();
                base.OnMouseEnter(e);
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                _hovered = false;
                _pressed = false;
                Invalidate();
                base.OnMouseLeave(e);
            }

            protected override void OnMouseDown(MouseEventArgs mevent)
            {
                if (mevent.Button == MouseButtons.Left)
                {
                    Focus();
                    _pressed = true;
                    Capture = true;
                    Invalidate();
                }
                base.OnMouseDown(mevent);
            }

            protected override void OnMouseUp(MouseEventArgs mevent)
            {
                if (mevent.Button == MouseButtons.Left)
                {
                    _pressed = false;
                    Capture = false;
                    Invalidate();
                }
                base.OnMouseUp(mevent);
            }

            protected override void OnKeyDown(KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter)
                {
                    _keyboardPressed = true;
                    Invalidate();
                    e.Handled = true;
                }
                base.OnKeyDown(e);
            }

            protected override void OnKeyUp(KeyEventArgs e)
            {
                if ((e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) && _keyboardPressed)
                {
                    _keyboardPressed = false;
                    Invalidate();
                    OnClick(EventArgs.Empty);
                    e.Handled = true;
                }
                base.OnKeyUp(e);
            }

            protected override void OnTextChanged(EventArgs e)
            {
                base.OnTextChanged(e);
                Invalidate();
            }

            protected override void OnEnabledChanged(EventArgs e)
            {
                base.OnEnabledChanged(e);
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs pevent)
            {
                pevent.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                pevent.Graphics.PixelOffsetMode = PixelOffsetMode.Half;

                Color fill = BackColorValue;
                if (_hovered)
                    fill = HoverBackColor;
                if (_pressed || _keyboardPressed)
                    fill = Mix(fill, Color.Black, 0.08);

                RectangleF rect = new RectangleF(1, 1, Width - 3, Height - 3);
                using (GraphicsPath path = RoundedRectF(rect, 7))
                {
                    using (SolidBrush brush = new SolidBrush(fill))
                        pevent.Graphics.FillPath(brush, path);

                    using (Pen pen = new Pen(BorderColor, 1.1f))
                        pevent.Graphics.DrawPath(pen, path);

                    GraphicsState state = pevent.Graphics.Save();
                    pevent.Graphics.SetClip(path);
                    TextRenderer.DrawText(
                        pevent.Graphics,
                        Text,
                        Font,
                        Rectangle.Round(rect),
                        Enabled ? TextColor : SystemColors.GrayText,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                        TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                    pevent.Graphics.Restore(state);
                }
            }
        }

        private class ShortcutKeyBox : Control
        {
            public bool IsListening { get; set; }
            public Color BackColorValue { get; set; }
            public Color BorderColor { get; set; }
            public Color TextColor { get; set; }
            public Color ListeningBackColor { get; set; }
            public Color ListeningBorderColor { get; set; }

            public ShortcutKeyBox()
            {
                DoubleBuffered = true;
                SetStyle(ControlStyles.UserPaint, true);
                SetStyle(ControlStyles.AllPaintingInWmPaint, true);
                SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
                Cursor = Cursors.Hand;
                Font = new Font("Segoe UI", 10F, FontStyle.Bold);
                BackColorValue = Color.White;
                BorderColor = Color.FromArgb(226, 232, 240);
                TextColor = Color.FromArgb(234, 88, 12);
                ListeningBackColor = Color.FromArgb(255, 237, 213);
                ListeningBorderColor = Color.FromArgb(234, 88, 12);
            }

            protected override void OnPaintBackground(PaintEventArgs e)
            {
                Color background = Parent != null ? Parent.BackColor : BackColor;
                using (SolidBrush brush = new SolidBrush(background))
                    e.Graphics.FillRectangle(brush, ClientRectangle);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;

                Color fill = IsListening ? ListeningBackColor : BackColorValue;
                Color border = IsListening ? ListeningBorderColor : BorderColor;

                RectangleF shadowRect = new RectangleF(2, 3, Width - 5, Height - 6);
                using (GraphicsPath shadowPath = RoundedRectF(shadowRect, 7))
                using (SolidBrush shadow = new SolidBrush(Color.FromArgb(24, Color.Black)))
                    e.Graphics.FillPath(shadow, shadowPath);

                RectangleF rect = new RectangleF(1, 1, Width - 3, Height - 4);
                using (GraphicsPath path = RoundedRectF(rect, 7))
                {
                    using (SolidBrush brush = new SolidBrush(fill))
                        e.Graphics.FillPath(brush, path);

                    using (Pen pen = new Pen(border, IsListening ? 1.8f : 1.1f))
                        e.Graphics.DrawPath(pen, path);
                }

                string text = Text;
                TextRenderer.DrawText(
                    e.Graphics,
                    text,
                    Font,
                    Rectangle.Round(rect),
                    TextColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            }
        }

        private class ShortcutRowsPanel : Panel
        {
            private readonly Dictionary<Control, int> _logicalTops;
            private int _contentHeight;
            private int _scrollOffset;
            private bool _thumbHovered;
            private bool _draggingThumb;
            private int _dragStartY;
            private int _dragStartOffset;

            public Color ScrollTrackColor { get; set; }
            public Color ScrollThumbColor { get; set; }
            public Color ScrollThumbHoverColor { get; set; }

            public ShortcutRowsPanel()
            {
                _logicalTops = new Dictionary<Control, int>();
                DoubleBuffered = true;
                SetStyle(ControlStyles.UserPaint, true);
                SetStyle(ControlStyles.AllPaintingInWmPaint, true);
                SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
                SetStyle(ControlStyles.ResizeRedraw, true);
                SetStyle(ControlStyles.Selectable, true);
                TabStop = true;
                ScrollTrackColor = Color.FromArgb(18, 18, 18);
                ScrollThumbColor = Color.FromArgb(95, 82, 70);
                ScrollThumbHoverColor = Color.FromArgb(128, 105, 82);
                UpdateStyles();
            }

            public void BeginContentLayout()
            {
                _logicalTops.Clear();
            }

            public void SetChildTop(Control child, int top)
            {
                if (child == null)
                    return;

                _logicalTops[child] = top;
            }

            public void EndContentLayout(int contentHeight)
            {
                _contentHeight = Math.Max(0, contentHeight);
                SetScrollOffset(Math.Min(_scrollOffset, MaximumScrollOffset));
                LayoutContent();
                Invalidate();
            }

            private int MaximumScrollOffset
            {
                get { return Math.Max(0, _contentHeight - ClientSize.Height); }
            }

            private bool NeedsScrollBar
            {
                get { return MaximumScrollOffset > 0; }
            }

            private Rectangle ScrollTrackRectangle
            {
                get { return new Rectangle(Math.Max(0, ClientSize.Width - 16), 0, 16, ClientSize.Height); }
            }

            private Rectangle ThumbRectangle
            {
                get
                {
                    if (!NeedsScrollBar || ClientSize.Height <= 0)
                        return Rectangle.Empty;

                    int thumbHeight = Math.Max(34,
                        (int)Math.Round(ClientSize.Height * (ClientSize.Height / (double)_contentHeight)));
                    thumbHeight = Math.Min(ClientSize.Height - 8, thumbHeight);
                    int travel = Math.Max(1, ClientSize.Height - thumbHeight - 8);
                    int thumbY = 4 + (int)Math.Round(travel * (_scrollOffset / (double)MaximumScrollOffset));
                    return new Rectangle(ClientSize.Width - 11, thumbY, 7, thumbHeight);
                }
            }

            private void SetScrollOffset(int value)
            {
                int next = Math.Max(0, Math.Min(MaximumScrollOffset, value));
                if (_scrollOffset == next)
                    return;

                _scrollOffset = next;
                LayoutContent();
                Invalidate();
            }

            private void LayoutContent()
            {
                foreach (KeyValuePair<Control, int> pair in _logicalTops)
                {
                    if (pair.Key != null && !pair.Key.IsDisposed)
                        pair.Key.Location = new Point(pair.Key.Left, pair.Value - _scrollOffset);
                }
            }

            protected override void OnMouseWheel(MouseEventArgs e)
            {
                ScrollByWheelDelta(e.Delta);
                base.OnMouseWheel(e);
            }

            public void ScrollByWheelDelta(int wheelDelta)
            {
                int lines = SystemInformation.MouseWheelScrollLines;
                if (lines <= 0 || lines == -1)
                    lines = 3;

                int delta = -(wheelDelta / SystemInformation.MouseWheelScrollDelta) * lines * 22;
                SetScrollOffset(_scrollOffset + delta);
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left && NeedsScrollBar)
                {
                    Focus();
                    Rectangle thumb = ThumbRectangle;
                    if (thumb.Contains(e.Location))
                    {
                        _draggingThumb = true;
                        _dragStartY = e.Y;
                        _dragStartOffset = _scrollOffset;
                        Capture = true;
                    }
                    else if (ScrollTrackRectangle.Contains(e.Location))
                    {
                        SetScrollOffset(_scrollOffset + (e.Y < thumb.Top ? -ClientSize.Height : ClientSize.Height));
                    }
                }

                base.OnMouseDown(e);
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                if (_draggingThumb)
                {
                    Rectangle thumb = ThumbRectangle;
                    int travel = Math.Max(1, ClientSize.Height - thumb.Height - 8);
                    int offsetDelta = (int)Math.Round((e.Y - _dragStartY) *
                        (MaximumScrollOffset / (double)travel));
                    SetScrollOffset(_dragStartOffset + offsetDelta);
                }

                bool hovered = NeedsScrollBar && ThumbRectangle.Contains(e.Location);
                if (_thumbHovered != hovered)
                {
                    _thumbHovered = hovered;
                    Invalidate(ScrollTrackRectangle);
                }

                Cursor = hovered || _draggingThumb ? Cursors.Hand : Cursors.Default;
                base.OnMouseMove(e);
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left && _draggingThumb)
                {
                    _draggingThumb = false;
                    Capture = false;
                    Invalidate(ScrollTrackRectangle);
                }

                base.OnMouseUp(e);
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                if (!_draggingThumb)
                {
                    _thumbHovered = false;
                    Cursor = Cursors.Default;
                    Invalidate(ScrollTrackRectangle);
                }

                base.OnMouseLeave(e);
            }

            protected override void OnResize(EventArgs eventargs)
            {
                base.OnResize(eventargs);
                _scrollOffset = Math.Min(_scrollOffset, MaximumScrollOffset);
                LayoutContent();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);

                if (!NeedsScrollBar)
                    return;

                using (SolidBrush trackBrush = new SolidBrush(ScrollTrackColor))
                    e.Graphics.FillRectangle(trackBrush, ScrollTrackRectangle);

                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle thumb = ThumbRectangle;
                using (GraphicsPath path = RoundedRectF(
                    new RectangleF(thumb.X, thumb.Y, thumb.Width, thumb.Height), 3.5f))
                using (SolidBrush thumbBrush = new SolidBrush(
                    _thumbHovered || _draggingThumb ? ScrollThumbHoverColor : ScrollThumbColor))
                    e.Graphics.FillPath(thumbBrush, path);
            }

            protected override bool IsInputKey(Keys keyData)
            {
                Keys keyCode = keyData & Keys.KeyCode;
                if (keyCode == Keys.Up || keyCode == Keys.Down ||
                    keyCode == Keys.PageUp || keyCode == Keys.PageDown ||
                    keyCode == Keys.Home || keyCode == Keys.End)
                    return true;

                return base.IsInputKey(keyData);
            }

            protected override void OnKeyDown(KeyEventArgs e)
            {
                int next = _scrollOffset;
                if (e.KeyCode == Keys.Up) next -= 22;
                else if (e.KeyCode == Keys.Down) next += 22;
                else if (e.KeyCode == Keys.PageUp) next -= ClientSize.Height;
                else if (e.KeyCode == Keys.PageDown) next += ClientSize.Height;
                else if (e.KeyCode == Keys.Home) next = 0;
                else if (e.KeyCode == Keys.End) next = MaximumScrollOffset;
                else
                {
                    base.OnKeyDown(e);
                    return;
                }

                SetScrollOffset(next);
                e.Handled = true;
                base.OnKeyDown(e);
            }
        }

        private class ShortcutCenteredGlyph : Control
        {
            public Color GlyphColor { get; set; }

            public ShortcutCenteredGlyph()
            {
                DoubleBuffered = true;
                GlyphColor = Color.White;
                SetStyle(ControlStyles.SupportsTransparentBackColor, true);
                SetStyle(ControlStyles.UserPaint, true);
                SetStyle(ControlStyles.AllPaintingInWmPaint, true);
                SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
                SetStyle(ControlStyles.ResizeRedraw, true);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
                DrawGlyphCenteredByBounds(
                    e.Graphics,
                    Text,
                    Font,
                    GlyphColor,
                    ClientRectangle);
            }
        }

        private class ShortcutBadge : Control
        {
            public Color BadgeColor { get; set; }
            public Color TextColor { get; set; }

            public ShortcutBadge()
            {
                DoubleBuffered = true;
                Font = new Font("Segoe UI", 12F, FontStyle.Bold);
                BadgeColor = Color.FromArgb(234, 88, 12);
                TextColor = Color.White;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;

                RectangleF rect = new RectangleF(0, 0, Width - 1, Height - 1);
                using (GraphicsPath path = RoundedRectF(rect, 7))
                using (SolidBrush brush = new SolidBrush(BadgeColor))
                    e.Graphics.FillPath(brush, path);

                if (string.Equals(Text, "+", StringComparison.Ordinal))
                {
                    DrawGlyphCenteredByBounds(
                        e.Graphics,
                        Text,
                        Font,
                        TextColor,
                        ClientRectangle);
                }
                else
                {
                    TextRenderer.DrawText(
                        e.Graphics,
                        Text,
                        Font,
                        ClientRectangle,
                        TextColor,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                }
            }
        }

        private class ShortcutTextBox : Control
        {
            private readonly TextBox _editor;
            private readonly SearchPlaceholder _placeholder;
            private Color _borderColor;
            private Color _placeholderColor;
            private string _placeholderText;
            private int _leftPadding;
            private int _borderRadius;

            public Color BorderColor
            {
                get { return _borderColor; }
                set
                {
                    _borderColor = value;
                    Invalidate();
                }
            }

            public Color PlaceholderColor
            {
                get { return _placeholderColor; }
                set
                {
                    _placeholderColor = value;
                    if (_placeholder != null)
                        _placeholder.ForeColor = value;
                }
            }

            public string PlaceholderText
            {
                get { return _placeholderText; }
                set
                {
                    _placeholderText = value ?? string.Empty;
                    if (_placeholder != null)
                        _placeholder.Text = _placeholderText;
                }
            }

            public int LeftPadding
            {
                get { return _leftPadding; }
                set
                {
                    _leftPadding = Math.Max(4, value);
                    LayoutEditor();
                }
            }

            public int BorderRadius
            {
                get { return _borderRadius; }
                set
                {
                    _borderRadius = Math.Max(0, value);
                    Invalidate();
                }
            }

            public override string Text
            {
                get { return _editor == null ? string.Empty : _editor.Text; }
                set
                {
                    if (_editor != null)
                        _editor.Text = value ?? string.Empty;
                }
            }

            public ShortcutTextBox()
            {
                SetStyle(ControlStyles.UserPaint, true);
                SetStyle(ControlStyles.AllPaintingInWmPaint, true);
                SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
                SetStyle(ControlStyles.ResizeRedraw, true);
                DoubleBuffered = true;
                Cursor = Cursors.IBeam;
                TabStop = false;

                _borderColor = Color.FromArgb(226, 232, 240);
                _placeholderColor = Color.FromArgb(100, 116, 139);
                _placeholderText = "Search function...";
                _leftPadding = 12;
                _borderRadius = 8;

                _editor = new TextBox();
                _editor.AutoSize = true;
                _editor.BorderStyle = BorderStyle.None;
                _editor.BackColor = BackColor;
                _editor.ForeColor = ForeColor;
                _editor.Font = Font;
                _editor.TabIndex = 0;
                _editor.TabStop = true;
                _editor.Cursor = Cursors.IBeam;
                _editor.TextChanged += delegate
                {
                    UpdatePlaceholderVisibility();
                    OnTextChanged(EventArgs.Empty);
                };
                _editor.GotFocus += delegate { UpdatePlaceholderVisibility(); };
                _editor.LostFocus += delegate { UpdatePlaceholderVisibility(); };
                _editor.KeyPress += delegate (object sender, KeyPressEventArgs e)
                {
                    if (e.KeyChar == '\r' || e.KeyChar == '\n')
                        e.Handled = true;
                };
                Controls.Add(_editor);

                _placeholder = new SearchPlaceholder();
                _placeholder.Text = _placeholderText;
                _placeholder.ForeColor = _placeholderColor;
                _placeholder.BackColor = BackColor;
                _placeholder.Font = Font;
                _placeholder.Cursor = Cursors.IBeam;
                _placeholder.Click += delegate { _editor.Focus(); };
                Controls.Add(_placeholder);
                _placeholder.BringToFront();

                LayoutEditor();
                UpdatePlaceholderVisibility();
            }

            public new bool Focus()
            {
                return _editor != null && _editor.Focus();
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                if (_editor != null)
                    _editor.Focus();
                base.OnMouseDown(e);
            }

            protected override void OnSizeChanged(EventArgs e)
            {
                base.OnSizeChanged(e);
                LayoutEditor();
            }

            protected override void OnFontChanged(EventArgs e)
            {
                base.OnFontChanged(e);

                if (_editor != null)
                    _editor.Font = Font;
                if (_placeholder != null)
                    _placeholder.Font = Font;

                LayoutEditor();
            }

            protected override void OnBackColorChanged(EventArgs e)
            {
                base.OnBackColorChanged(e);

                if (_editor != null)
                    _editor.BackColor = BackColor;
                if (_placeholder != null)
                    _placeholder.BackColor = BackColor;

                Invalidate();
            }

            protected override void OnForeColorChanged(EventArgs e)
            {
                base.OnForeColorChanged(e);

                if (_editor != null)
                    _editor.ForeColor = ForeColor;
            }

            protected override void OnEnabledChanged(EventArgs e)
            {
                base.OnEnabledChanged(e);

                if (_editor != null)
                    _editor.Enabled = Enabled;
                if (_placeholder != null)
                    _placeholder.Enabled = Enabled;
            }

            protected override void OnPaintBackground(PaintEventArgs e)
            {
                Color outside = Parent != null ? Parent.BackColor : SystemColors.Control;
                using (SolidBrush brush = new SolidBrush(outside))
                    e.Graphics.FillRectangle(brush, ClientRectangle);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

                RectangleF rect = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
                float radius = Math.Min(BorderRadius, rect.Height / 2f);

                using (GraphicsPath path = RoundedRectF(rect, radius))
                using (SolidBrush fill = new SolidBrush(BackColor))
                using (Pen pen = new Pen(BorderColor, 1f))
                {
                    e.Graphics.FillPath(fill, path);
                    e.Graphics.DrawPath(pen, path);
                }
            }

            private void LayoutEditor()
            {
                if (_editor == null || _placeholder == null || Width <= 0 || Height <= 0)
                    return;

                int editorHeight = Math.Min(_editor.PreferredHeight, Math.Max(1, Height - 6));
                int editorTop = Math.Max(3, (Height - editorHeight) / 2 + 1);
                int placeholderTop = Math.Max(2, editorTop - 2);
                int editorWidth = Math.Max(1, Width - LeftPadding - 10);

                _editor.SetBounds(LeftPadding, editorTop, editorWidth, editorHeight);
                _placeholder.SetBounds(LeftPadding, placeholderTop, editorWidth, editorHeight);
            }

            private void UpdatePlaceholderVisibility()
            {
                if (_placeholder == null || _editor == null)
                    return;

                _placeholder.Visible = string.IsNullOrEmpty(_editor.Text) && !_editor.Focused;

                if (_placeholder.Visible)
                    _placeholder.BringToFront();
            }

            private class SearchPlaceholder : Control
            {
                public SearchPlaceholder()
                {
                    SetStyle(ControlStyles.UserPaint, true);
                    SetStyle(ControlStyles.AllPaintingInWmPaint, true);
                    SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
                    DoubleBuffered = true;
                    TabStop = false;
                }

                protected override void OnPaint(PaintEventArgs e)
                {
                    TextRenderer.DrawText(
                        e.Graphics,
                        Text,
                        Font,
                        ClientRectangle,
                        ForeColor,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                        TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding |
                        TextFormatFlags.SingleLine);
                }
            }
        }

        private static Color Mix(Color a, Color b, double amount)
        {
            if (amount < 0.0)
                amount = 0.0;
            if (amount > 1.0)
                amount = 1.0;

            int r = (int)Math.Round(a.R + (b.R - a.R) * amount);
            int g = (int)Math.Round(a.G + (b.G - a.G) * amount);
            int bl = (int)Math.Round(a.B + (b.B - a.B) * amount);
            return Color.FromArgb(r, g, bl);
        }

        private static void DrawGlyphCenteredByBounds(
            Graphics graphics,
            string text,
            Font font,
            Color color,
            Rectangle bounds)
        {
            if (graphics == null || font == null || string.IsNullOrEmpty(text))
                return;

            using (GraphicsPath glyphPath = new GraphicsPath())
            using (StringFormat format = (StringFormat)StringFormat.GenericTypographic.Clone())
            {
                float emSize = font.SizeInPoints * graphics.DpiY / 72f;
                glyphPath.AddString(
                    text,
                    font.FontFamily,
                    (int)font.Style,
                    emSize,
                    PointF.Empty,
                    format);

                RectangleF glyphBounds = glyphPath.GetBounds();
                if (glyphBounds.Width <= 0f || glyphBounds.Height <= 0f)
                    return;

                float targetCenterX = bounds.Left + bounds.Width / 2f;
                float targetCenterY = bounds.Top + bounds.Height / 2f;
                float glyphCenterX = glyphBounds.Left + glyphBounds.Width / 2f;
                float glyphCenterY = glyphBounds.Top + glyphBounds.Height / 2f;

                using (Matrix moveToCenter = new Matrix())
                {
                    moveToCenter.Translate(
                        targetCenterX - glyphCenterX,
                        targetCenterY - glyphCenterY);
                    glyphPath.Transform(moveToCenter);
                }

                using (SolidBrush brush = new SolidBrush(color))
                    graphics.FillPath(brush, glyphPath);
            }
        }

        private static GraphicsPath RoundedRectF(RectangleF bounds, float radius)
        {
            float diameter = radius * 2f;
            GraphicsPath path = new GraphicsPath();

            if (radius <= 0)
            {
                path.AddRectangle(bounds);
                return path;
            }

            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
