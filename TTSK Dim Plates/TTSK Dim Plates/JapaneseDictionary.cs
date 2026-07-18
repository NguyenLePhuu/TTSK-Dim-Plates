using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace TTSK_AutoDim_Plates
{
    internal enum JapaneseDictionaryStatusKind
    {
        Success,
        Information,
        Warning,
        Error
    }

    internal sealed class JapaneseDictionaryStatusEventArgs : EventArgs
    {
        public JapaneseDictionaryStatusEventArgs(string message, JapaneseDictionaryStatusKind kind)
        {
            Message = message;
            Kind = kind;
        }

        public string Message { get; private set; }
        public JapaneseDictionaryStatusKind Kind { get; private set; }
    }

    internal sealed class JapaneseDictionaryPanel : UserControl
    {
        private const string VietnameseHeader = "Việt";
        private const string JapaneseHeader = "Nhật";

        private readonly string _dataFilePath;
        private readonly DictionaryGrid _grid;
        private readonly Label _emptyLabel;
        private bool _darkMode;
        private int _hoveredCopyRowIndex = -1;
        private Color _copyButtonHoverBackColor;
        private Color _copyButtonBorderColor;
        private Color _copyButtonTextColor;
        private Color _copyButtonHoverTextColor;

        public event EventHandler<JapaneseDictionaryStatusEventArgs> StatusChanged;

        public JapaneseDictionaryPanel(string dataFilePath)
        {
            _dataFilePath = dataFilePath;
            DoubleBuffered = true;
            BackColor = Color.White;

            _grid = new DictionaryGrid();
            _grid.AllowUserToAddRows = false;
            _grid.AllowUserToDeleteRows = false;
            _grid.AllowUserToResizeColumns = false;
            _grid.AllowUserToResizeRows = false;
            _grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCellsExceptHeaders;
            _grid.AutoGenerateColumns = false;
            _grid.BackgroundColor = Color.White;
            _grid.BorderStyle = BorderStyle.None;
            _grid.CellBorderStyle = DataGridViewCellBorderStyle.Single;
            _grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
            _grid.ColumnHeadersHeight = 38;
            _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            _grid.EnableHeadersVisualStyles = false;
            _grid.MultiSelect = false;
            _grid.ReadOnly = true;
            _grid.RowHeadersVisible = false;
            _grid.RowTemplate.Height = 58;
            _grid.RowTemplate.MinimumHeight = 58;
            _grid.ScrollBars = ScrollBars.Vertical;
            _grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
            _grid.CellContentClick += Grid_CellContentClick;
            _grid.CellPainting += Grid_CellPainting;
            _grid.CellMouseEnter += Grid_CellMouseEnter;
            _grid.CellMouseLeave += Grid_CellMouseLeave;
            Controls.Add(_grid);

            DataGridViewTextBoxColumn indexColumn = new DataGridViewTextBoxColumn();
            indexColumn.Name = "STT";
            indexColumn.HeaderText = "STT";
            indexColumn.Width = 38;
            indexColumn.SortMode = DataGridViewColumnSortMode.NotSortable;
            indexColumn.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            _grid.Columns.Add(indexColumn);

            DataGridViewTextBoxColumn vietnameseColumn = new DataGridViewTextBoxColumn();
            vietnameseColumn.Name = "VIETNAMESE";
            vietnameseColumn.HeaderText = "VIỆT";
            vietnameseColumn.Width = 94;
            vietnameseColumn.SortMode = DataGridViewColumnSortMode.NotSortable;
            vietnameseColumn.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            _grid.Columns.Add(vietnameseColumn);

            DataGridViewTextBoxColumn japaneseColumn = new DataGridViewTextBoxColumn();
            japaneseColumn.Name = "JAPANESE";
            japaneseColumn.HeaderText = "NHẬT";
            japaneseColumn.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            japaneseColumn.MinimumWidth = 92;
            japaneseColumn.SortMode = DataGridViewColumnSortMode.NotSortable;
            japaneseColumn.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            _grid.Columns.Add(japaneseColumn);

            DataGridViewButtonColumn copyColumn = new DataGridViewButtonColumn();
            copyColumn.Name = "COPY";
            copyColumn.HeaderText = "COPY";
            copyColumn.Text = "Copy";
            copyColumn.UseColumnTextForButtonValue = true;
            copyColumn.Width = 54;
            copyColumn.FlatStyle = FlatStyle.Flat;
            copyColumn.SortMode = DataGridViewColumnSortMode.NotSortable;
            copyColumn.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            copyColumn.DefaultCellStyle.Padding = new Padding(4, 12, 4, 12);
            _grid.Columns.Add(copyColumn);

            _emptyLabel = new Label();
            _emptyLabel.Text = "Chưa có từ vựng.";
            _emptyLabel.TextAlign = ContentAlignment.MiddleCenter;
            _emptyLabel.Font = new Font("Segoe UI", 9F, FontStyle.Italic);
            _emptyLabel.Visible = false;
            Controls.Add(_emptyLabel);

            ApplyTheme(false);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);

            _grid.Location = new Point(0, 0);
            _grid.Size = ClientSize;
            _emptyLabel.Location = new Point(1, _grid.ColumnHeadersHeight + 1);
            _emptyLabel.Size = new Size(
                Math.Max(0, ClientSize.Width - 2),
                Math.Max(0, ClientSize.Height - _grid.ColumnHeadersHeight - 2));
            _emptyLabel.BringToFront();
        }

        public void ReloadEntries()
        {
            try
            {
                List<JapaneseDictionaryEntry> entries;
                int invalidLineCount;
                LoadEntries(out entries, out invalidLineCount);

                _grid.Rows.Clear();

                for (int i = 0; i < entries.Count; i++)
                {
                    JapaneseDictionaryEntry entry = entries[i];
                    _grid.Rows.Add(i + 1, entry.Vietnamese, entry.Japanese, "Copy");
                }

                _emptyLabel.Visible = entries.Count == 0;
                ApplyTheme(_darkMode);

                if (invalidLineCount > 0)
                {
                    RaiseStatus(
                        "Đã tải " + entries.Count + " từ; bỏ qua " + invalidLineCount + " dòng sai định dạng.",
                        JapaneseDictionaryStatusKind.Warning);
                }
                else
                {
                    RaiseStatus(
                        "Đã tải " + entries.Count + " từ tiếng Nhật.",
                        JapaneseDictionaryStatusKind.Information);
                }
            }
            catch (FileNotFoundException)
            {
                _grid.Rows.Clear();
                _emptyLabel.Text = "Không tìm thấy file từ điển.";
                _emptyLabel.Visible = true;
                RaiseStatus("Không tìm thấy Data\\JapaneseDictionary.tsv.", JapaneseDictionaryStatusKind.Error);
            }
            catch (Exception ex)
            {
                _grid.Rows.Clear();
                _emptyLabel.Text = "Không thể đọc file từ điển.";
                _emptyLabel.Visible = true;
                RaiseStatus("Đọc từ điển lỗi: " + ex.Message, JapaneseDictionaryStatusKind.Error);
            }
        }

        public void ApplyTheme(bool darkMode)
        {
            _darkMode = darkMode;

            Color panelBack = darkMode ? Color.FromArgb(18, 18, 18) : Color.White;
            Color headerBack = darkMode ? Color.FromArgb(24, 24, 24) : Color.FromArgb(248, 250, 252);
            Color rowBack = darkMode ? Color.FromArgb(15, 15, 15) : Color.White;
            Color alternateBack = darkMode ? Color.FromArgb(20, 20, 20) : Color.FromArgb(250, 252, 255);
            Color text = darkMode ? Color.FromArgb(226, 232, 240) : Color.FromArgb(15, 23, 42);
            Color muted = darkMode ? Color.FromArgb(148, 163, 184) : Color.FromArgb(100, 116, 139);
            Color accent = darkMode ? Color.FromArgb(224, 156, 96) : Color.FromArgb(30, 58, 138);
            Color border = darkMode ? Color.FromArgb(73, 56, 43) : Color.FromArgb(220, 226, 235);

            _copyButtonHoverBackColor = darkMode
                ? Color.FromArgb(59, 45, 34)
                : Color.FromArgb(229, 238, 255);
            _copyButtonBorderColor = darkMode
                ? Color.FromArgb(201, 122, 64)
                : Color.FromArgb(30, 58, 138);
            _copyButtonTextColor = accent;
            _copyButtonHoverTextColor = darkMode
                ? Color.FromArgb(229, 171, 120)
                : Color.FromArgb(30, 58, 138);

            BackColor = panelBack;
            _grid.BackgroundColor = rowBack;
            _grid.GridColor = border;
            _grid.SoftOuterBorderColor = border;

            _grid.ColumnHeadersDefaultCellStyle.BackColor = headerBack;
            _grid.ColumnHeadersDefaultCellStyle.ForeColor = accent;
            _grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = headerBack;
            _grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = accent;
            _grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 8.5F, FontStyle.Bold);
            _grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;

            _grid.DefaultCellStyle.BackColor = rowBack;
            _grid.DefaultCellStyle.ForeColor = text;
            _grid.DefaultCellStyle.SelectionBackColor = rowBack;
            _grid.DefaultCellStyle.SelectionForeColor = text;
            _grid.DefaultCellStyle.Font = new Font("Segoe UI", 9F);
            _grid.DefaultCellStyle.WrapMode = DataGridViewTriState.True;

            _grid.AlternatingRowsDefaultCellStyle.BackColor = alternateBack;
            _grid.AlternatingRowsDefaultCellStyle.ForeColor = text;
            _grid.AlternatingRowsDefaultCellStyle.SelectionBackColor = alternateBack;
            _grid.AlternatingRowsDefaultCellStyle.SelectionForeColor = text;

            DataGridViewColumn copyColumn = _grid.Columns["COPY"];
            if (copyColumn != null)
            {
                copyColumn.DefaultCellStyle.BackColor = rowBack;
                copyColumn.DefaultCellStyle.ForeColor = accent;
                copyColumn.DefaultCellStyle.SelectionBackColor = rowBack;
                copyColumn.DefaultCellStyle.SelectionForeColor = accent;
                copyColumn.DefaultCellStyle.Font = new Font("Segoe UI", 8F, FontStyle.Bold);
            }

            foreach (DataGridViewRow row in _grid.Rows)
            {
                DataGridViewCell copyCell = row.Cells["COPY"];
                Color rowBackground = row.Index % 2 == 0 ? rowBack : alternateBack;
                copyCell.Style.BackColor = rowBackground;
                copyCell.Style.ForeColor = accent;
                copyCell.Style.SelectionBackColor = rowBackground;
                copyCell.Style.SelectionForeColor = accent;
                copyCell.Style.Font = new Font("Segoe UI", 8F, FontStyle.Bold);
                copyCell.Style.Padding = new Padding(4, 12, 4, 12);
            }

            _emptyLabel.BackColor = panelBack;
            _emptyLabel.ForeColor = muted;
            _grid.Invalidate();
        }

        private void LoadEntries(out List<JapaneseDictionaryEntry> entries, out int invalidLineCount)
        {
            entries = new List<JapaneseDictionaryEntry>();
            invalidLineCount = 0;

            using (StreamReader reader = new StreamReader(_dataFilePath, Encoding.UTF8, true))
            {
                string line;

                while ((line = reader.ReadLine()) != null)
                {
                    string trimmed = line.Trim();

                    if (trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal))
                        continue;

                    string[] values = line.Split(new char[] { '\t' }, 2);

                    if (values.Length != 2)
                    {
                        invalidLineCount++;
                        continue;
                    }

                    string vietnamese = values[0].Trim();
                    string japanese = values[1].Trim();

                    if (IsHeader(vietnamese, japanese))
                        continue;

                    if (vietnamese.Length == 0 || japanese.Length == 0)
                    {
                        invalidLineCount++;
                        continue;
                    }

                    entries.Add(new JapaneseDictionaryEntry(vietnamese, japanese));
                }
            }
        }

        private static bool IsHeader(string vietnamese, string japanese)
        {
            return string.Equals(vietnamese, VietnameseHeader, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(japanese, JapaneseHeader, StringComparison.OrdinalIgnoreCase);
        }

        private void Grid_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0 || _grid.Columns[e.ColumnIndex].Name != "COPY")
                return;

            object value = _grid.Rows[e.RowIndex].Cells["JAPANESE"].Value;
            string japanese = value == null ? string.Empty : value.ToString();

            if (string.IsNullOrWhiteSpace(japanese))
                return;

            try
            {
                Clipboard.SetText(japanese);
                RaiseStatus("Đã copy: " + japanese, JapaneseDictionaryStatusKind.Success);
            }
            catch (Exception ex)
            {
                RaiseStatus("Copy chữ Nhật lỗi: " + ex.Message, JapaneseDictionaryStatusKind.Error);
            }
        }

        private void Grid_CellPainting(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0 ||
                _grid.Columns[e.ColumnIndex].Name != "COPY")
            {
                return;
            }

            e.PaintBackground(e.CellBounds, false);

            Padding padding = e.CellStyle.Padding;
            Rectangle buttonBounds = new Rectangle(
                e.CellBounds.X + padding.Left,
                e.CellBounds.Y + padding.Top,
                Math.Max(1, e.CellBounds.Width - padding.Horizontal),
                Math.Max(1, e.CellBounds.Height - padding.Vertical));

            bool hovered = e.RowIndex == _hoveredCopyRowIndex;
            Color buttonBack = hovered
                ? _copyButtonHoverBackColor
                : e.CellStyle.BackColor;
            Color buttonText = hovered
                ? _copyButtonHoverTextColor
                : _copyButtonTextColor;

            using (SolidBrush backBrush = new SolidBrush(buttonBack))
                e.Graphics.FillRectangle(backBrush, buttonBounds);

            using (Pen borderPen = new Pen(_copyButtonBorderColor, 1.0f))
                e.Graphics.DrawRectangle(
                    borderPen,
                    buttonBounds.X,
                    buttonBounds.Y,
                    buttonBounds.Width - 1,
                    buttonBounds.Height - 1);

            TextRenderer.DrawText(
                e.Graphics,
                "Copy",
                e.CellStyle.Font ?? _grid.Font,
                buttonBounds,
                buttonText,
                TextFormatFlags.HorizontalCenter |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine |
                TextFormatFlags.NoPadding);

            e.Handled = true;
        }

        private void Grid_CellMouseEnter(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0 && e.ColumnIndex >= 0 &&
                _grid.Columns[e.ColumnIndex].Name == "COPY")
            {
                _grid.Cursor = Cursors.Hand;
                _hoveredCopyRowIndex = e.RowIndex;
                _grid.InvalidateCell(e.ColumnIndex, e.RowIndex);
            }
        }

        private void Grid_CellMouseLeave(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0 && e.ColumnIndex >= 0 &&
                _grid.Columns[e.ColumnIndex].Name == "COPY" &&
                _hoveredCopyRowIndex == e.RowIndex)
            {
                _hoveredCopyRowIndex = -1;
                _grid.InvalidateCell(e.ColumnIndex, e.RowIndex);
            }

            _grid.Cursor = Cursors.Default;
        }

        private void RaiseStatus(string message, JapaneseDictionaryStatusKind kind)
        {
            EventHandler<JapaneseDictionaryStatusEventArgs> handler = StatusChanged;
            if (handler != null)
                handler(this, new JapaneseDictionaryStatusEventArgs(message, kind));
        }

        private sealed class JapaneseDictionaryEntry
        {
            public JapaneseDictionaryEntry(string vietnamese, string japanese)
            {
                Vietnamese = vietnamese;
                Japanese = japanese;
            }

            public string Vietnamese { get; private set; }
            public string Japanese { get; private set; }
        }

        private sealed class DictionaryGrid : DataGridView
        {
            public Color SoftOuterBorderColor { get; set; }

            public DictionaryGrid()
            {
                DoubleBuffered = true;
                SoftOuterBorderColor = Color.FromArgb(160, 165, 170);
                SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
                SetStyle(ControlStyles.AllPaintingInWmPaint, true);
                SetStyle(ControlStyles.ResizeRedraw, true);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);

                if (Width <= 0 || Height <= 0)
                    return;

                using (Pen borderPen = new Pen(SoftOuterBorderColor, 1.0f))
                {
                    e.Graphics.DrawRectangle(
                        borderPen,
                        0,
                        0,
                        Width - 1,
                        Height - 1);
                }
            }
        }
    }
}
