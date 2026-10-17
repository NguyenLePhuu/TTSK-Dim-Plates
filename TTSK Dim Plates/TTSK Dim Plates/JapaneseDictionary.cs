using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Linq;

namespace TTSK_AutoDim_Plates
{
    internal enum JapaneseDictionaryStatusKind { Success, Information, Warning, Error }
    internal sealed class JapaneseDictionaryStatusEventArgs : EventArgs
    {
        public JapaneseDictionaryStatusEventArgs(string message, JapaneseDictionaryStatusKind kind) { Message = message; Kind = kind; }
        public string Message { get; private set; }
        public JapaneseDictionaryStatusKind Kind { get; private set; }
    }
    internal sealed class JapaneseDictionaryEntry
    {
        internal JapaneseDictionaryEntry(string id, string vietnamese, string japanese) { Id = id; Vietnamese = vietnamese; Japanese = japanese; }
        internal string Id { get; private set; }
        internal string Vietnamese { get; private set; }
        internal string Japanese { get; private set; }
    }
    internal sealed class JapaneseDictionarySnapshot
    {
        internal readonly List<JapaneseDictionaryEntry> Entries = new List<JapaneseDictionaryEntry>();
        internal string Warning = "";
    }
    // Bộ gốc chỉ đọc. Thay đổi riêng ở LocalAppData, độc lập EXE/update và model Tekla.
    internal sealed class JapaneseDictionaryStore
    {
        internal const int MaxTextLength = 500; // Ký tự mỗi vế; tránh paste nhầm cả tài liệu vào một từ.
        private readonly string defaultsPath;
        internal string UserFilePath { get; private set; }
        private sealed class Record { internal JapaneseDictionaryEntry Entry; internal bool Deleted; }
        internal JapaneseDictionaryStore(string defaultsPath, string userFilePath = null)
        {
            this.defaultsPath = defaultsPath;
            UserFilePath = userFilePath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TTSK", "Dictionary", "entries.xml");
        }
        internal static string Normalize(string value)
        { return Regex.Replace((value ?? "").Normalize(NormalizationForm.FormC), @"\s+", " ").Trim(); }
        internal static string Validate(ref string vietnamese, ref string japanese)
        {
            vietnamese = Normalize(vietnamese);
            japanese = JapaneseDictionaryPanel.NormalizeJapaneseForDrawing(Normalize(japanese));
            if (vietnamese.Length == 0) return "Nhập nghĩa tiếng Việt trước khi lưu.";
            if (japanese.Length == 0) return "Nhập từ hoặc câu tiếng Nhật trước khi lưu.";
            if (vietnamese.Length > MaxTextLength || japanese.Length > MaxTextLength) return "Mỗi ô nhập tối đa 500 ký tự.";
            if (vietnamese.Any(char.IsControl) || japanese.Any(char.IsControl)) return "Nội dung chứa ký tự điều khiển không hợp lệ.";
            try { XmlConvert.VerifyXmlChars(vietnamese); XmlConvert.VerifyXmlChars(japanese); }
            catch (XmlException) { return "Nội dung có ký tự không hợp lệ. Hãy nhập lại phần vừa dán."; }
            return null;
        }
        internal JapaneseDictionarySnapshot Load()
        { var snapshot = LoadDefaults(); Merge(snapshot, ReadRecords()); return snapshot; }
        internal JapaneseDictionarySnapshot LoadDefaults()
        {
            var snapshot = new JapaneseDictionarySnapshot();
            try
            {
                int invalid = 0; var ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (string line in File.ReadAllLines(defaultsPath, Encoding.UTF8))
                {
                    if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#", StringComparison.Ordinal)) continue;
                    string[] parts = line.Split(new[] { '\t' }, 2);
                    if (parts.Length != 2) { invalid++; continue; }
                    string vi = parts[0], ja = parts[1];
                    if (Normalize(vi).Equals("Việt", StringComparison.OrdinalIgnoreCase) && Normalize(ja).Equals("Nhật", StringComparison.OrdinalIgnoreCase)) continue;
                    if (Validate(ref vi, ref ja) != null) { invalid++; continue; }
                    string id;
                    using (var hash = SHA256.Create()) id = "base-" + BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(vi + "\n" + ja))).Replace("-", "");
                    if (ids.Add(id)) snapshot.Entries.Add(new JapaneseDictionaryEntry(id, vi, ja));
                }
                if (invalid > 0) snapshot.Warning = "Bộ từ có sẵn có " + invalid + " dòng không hợp lệ.";
            }
            catch (IOException) { snapshot.Warning = "Chưa đọc được bộ từ có sẵn; từ riêng vẫn sử dụng được."; }
            catch (UnauthorizedAccessException) { snapshot.Warning = "Không có quyền đọc bộ từ có sẵn; từ riêng vẫn sử dụng được."; }
            return snapshot;
        }
        private List<Record> ReadRecords()
        {
            XDocument document;
            try
            {
                using (var stream = new FileStream(UserFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 8 * 1024 * 1024 })) document = XDocument.Load(reader);
            }
            catch (FileNotFoundException) { return new List<Record>(); }
            catch (DirectoryNotFoundException) { return new List<Record>(); }
            if (document.Root == null || document.Root.Name != "dictionary" || (string)document.Root.Attribute("version") != "1")
                throw new InvalidDataException("Định dạng từ điển riêng chưa được hỗ trợ; dữ liệu chưa bị thay đổi.");
            var records = new List<Record>(); var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var node in document.Root.Elements())
            {
                string id = (string)node.Attribute("id"), deleted = (string)node.Attribute("deleted");
                string vi = (string)node.Element("vi"), ja = (string)node.Element("ja");
                if (node.Name != "entry" || string.IsNullOrEmpty(id) || id.Length > 100 || !ids.Add(id)
                    || (deleted != "true" && deleted != "false") || Validate(ref vi, ref ja) != null || records.Count >= 5000)
                    throw new InvalidDataException("Dữ liệu từ riêng không hợp lệ; giữ nguyên file để tránh mất từ.");
                records.Add(new Record { Entry = new JapaneseDictionaryEntry(id, vi, ja), Deleted = deleted == "true" });
            }
            return records;
        }
        private static void Merge(JapaneseDictionarySnapshot snapshot, List<Record> records)
        {
            foreach (var record in records)
            {
                int index = snapshot.Entries.FindIndex(e => e.Id == record.Entry.Id);
                if (record.Deleted) { if (index >= 0) snapshot.Entries.RemoveAt(index); }
                else if (index >= 0) snapshot.Entries[index] = record.Entry;
                else snapshot.Entries.Add(record.Entry);
            }
        }
        internal JapaneseDictionarySnapshot Save(JapaneseDictionaryEntry expected, string vietnamese, string japanese, bool delete, out string savedId)
        {
            savedId = null; string error = Validate(ref vietnamese, ref japanese);
            if (error != null) throw new InvalidDataException(error);
            Directory.CreateDirectory(Path.GetDirectoryName(UserFilePath));
            // Khóa theo file, đọc lại trong khóa: nhiều cửa sổ không ghi đè từ của nhau.
            using (var gate = new FileStream(UserFilePath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                var snapshot = LoadDefaults(); var records = ReadRecords(); Merge(snapshot, records);
                if (expected != null)
                {
                    var current = snapshot.Entries.FirstOrDefault(e => e.Id == expected.Id);
                    if (current == null || current.Vietnamese != expected.Vietnamese || current.Japanese != expected.Japanese)
                        throw new InvalidOperationException("Từ này vừa thay đổi ở cửa sổ khác. Hủy để tải lại rồi sửa tiếp.");
                }
                else if (delete) throw new InvalidOperationException("Chọn từ cần xóa trước.");
                if (!delete && snapshot.Entries.Any(e => (expected == null || e.Id != expected.Id)
                    && string.Equals(e.Vietnamese, vietnamese, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(e.Japanese, japanese, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("Cặp từ Việt–Nhật này đã có trong từ điển.");
                string id = expected == null ? "user-" + Guid.NewGuid().ToString("N") : expected.Id;
                var next = new Record { Entry = new JapaneseDictionaryEntry(id, vietnamese, japanese), Deleted = delete };
                int recordIndex = records.FindIndex(r => r.Entry.Id == id);
                // Từ gốc cần dấu xóa để không hiện lại khi mở app; từ thêm mới có thể bỏ hẳn.
                if (delete && !id.StartsWith("base-", StringComparison.Ordinal)) records.RemoveAll(r => r.Entry.Id == id);
                else if (recordIndex >= 0) records[recordIndex] = next;
                else records.Add(next);
                if (records.Count > 5000) throw new InvalidOperationException("Từ điển đã đạt 5.000 mục thay đổi riêng.");
                WriteRecords(records);
                int index = snapshot.Entries.FindIndex(e => e.Id == id);
                if (delete) snapshot.Entries.RemoveAll(e => e.Id == id);
                else if (index >= 0) snapshot.Entries[index] = next.Entry;
                else snapshot.Entries.Add(next.Entry);
                savedId = id; return snapshot;
            }
        }
        private void WriteRecords(List<Record> records)
        {
            var document = new XDocument(new XElement("dictionary", new XAttribute("version", "1"),
                records.Select(r => new XElement("entry", new XAttribute("id", r.Entry.Id), new XAttribute("deleted", r.Deleted),
                    new XElement("vi", r.Entry.Vietnamese), new XElement("ja", r.Entry.Japanese)))));
            string temp = UserFilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    using (var writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true, CloseOutput = false })) document.Save(writer);
                    stream.Flush(true);
                }
                if (File.Exists(UserFilePath)) File.Replace(temp, UserFilePath, UserFilePath + ".bak"); else File.Move(temp, UserFilePath);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
    internal sealed class JapaneseDictionaryPanel : UserControl
    {
        // Nội dung 10pt Regular: tiếng Việt/ô tìm dùng Segoe UI, tiếng Nhật dùng MS UI Gothic.
        internal const string DictionaryFontName = "MS UI Gothic";
        private readonly Font bodyFont = new Font("Segoe UI", 10F), strongFont = new Font("Segoe UI", 10F, FontStyle.Bold), japaneseFont = new Font(DictionaryFontName, 10F);
        private readonly Font titleFont = new Font("Segoe UI", 12.5F, FontStyle.Bold), captionFont = new Font("Segoe UI", 9F);
        private readonly JapaneseDictionaryStore store;
        private readonly TableLayoutPanel browser, editor;
        private readonly DictionaryGrid grid;
        private readonly DictionaryScroll scroll;
        private readonly TextBox search, vietnamese, japanese;
        private readonly Label count, language, headerCaption, deletePrompt, editorTitle, editorMessage, searchHint, empty;
        private readonly DictionaryMark dictionaryMark;
        private readonly DictionaryButton add, edit, remove, save, cancel, confirmDelete, cancelDelete, clearSearch;
        private readonly Panel listHost, deleteHost, searchHost;
        private List<JapaneseDictionaryEntry> entries = new List<JapaneseDictionaryEntry>();
        private JapaneseDictionaryEntry editing, deleting;
        private int hoveredCopyRow = -1;
        private bool darkMode, ready, refreshing, hasLoaded, editorHasError;
        private Color surface, fieldSurface, ink, muted, border, separator, accent, selection, errorInk;
        public event EventHandler<JapaneseDictionaryStatusEventArgs> StatusChanged;
        public JapaneseDictionaryPanel(string dataFilePath) : this(new JapaneseDictionaryStore(dataFilePath)) { }
        internal JapaneseDictionaryPanel(JapaneseDictionaryStore store)
        {
            this.store = store; SuspendLayout(); DoubleBuffered = true; AutoScaleMode = AutoScaleMode.None; Font = bodyFont; Size = new Size(314, 472);
            browser = CreateLayout(40, 40, 28, 0, -1); // px tại 100%; đầu panel vẫn 108px để giữ diện tích list.
            var heading = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            heading.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); heading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108));
            var identity = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            identity.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            identity.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42)); identity.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            dictionaryMark = new DictionaryMark { Dock = DockStyle.Fill, Font = japaneseFont, Margin = new Padding(0, 3, 7, 3), AccessibleName = "Từ điển Việt–Nhật" };
            var naming = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            headerCaption = Label("TỪ ĐIỂN", captionFont); headerCaption.Dock = DockStyle.Top; headerCaption.Height = 16;
            language = Label("Việt · Nhật", titleFont); language.Dock = DockStyle.Fill;
            naming.Controls.Add(language); naming.Controls.Add(headerCaption);
            identity.Controls.Add(dictionaryMark, 0, 0); identity.Controls.Add(naming, 1, 0); heading.Controls.Add(identity, 0, 0);
            add = Button("Thêm từ", 108, true); add.Glyph = DictionaryGlyph.Add; add.Capsule = add.GlyphBadge = true;
            add.Dock = DockStyle.Fill; add.Margin = new Padding(0, 2, 0, 4);
            heading.Controls.Add(add, 1, 0); browser.Controls.Add(heading, 0, 0);
            searchHost = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 6) };
            search = new TextBox { BorderStyle = BorderStyle.None, Font = bodyFont, AccessibleName = "Tìm từ tiếng Việt hoặc Nhật", TabIndex = 0 };
            searchHint = Label("Tìm từ Việt hoặc Nhật", bodyFont); searchHint.Dock = DockStyle.None;
            searchHint.TextAlign = ContentAlignment.MiddleLeft; searchHint.Cursor = Cursors.IBeam;
            clearSearch = Button("", 26); clearSearch.Glyph = DictionaryGlyph.Close; clearSearch.Quiet = true;
            clearSearch.AccessibleName = "Xóa nội dung tìm kiếm"; clearSearch.TabIndex = 1; clearSearch.Visible = false;
            searchHost.Controls.AddRange(new Control[] { search, searchHint, clearSearch }); browser.Controls.Add(searchHost, 0, 1);
            searchHost.Paint += PaintSearch; searchHost.Resize += (s, e) => LayoutSearch();
            search.GotFocus += (s, e) => UpdateSearchChrome(); search.LostFocus += (s, e) => UpdateSearchChrome();
            searchHint.Click += (s, e) => FocusSearch();
            searchHost.MouseDown += (s, e) => search.Focus();
            clearSearch.Click += (s, e) => { search.Clear(); search.Focus(); };
            search.TextChanged += (s, e) => { UpdateSearchChrome(); RenderRows(); };
            var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty };
            actions.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 66)); actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 66));
            count = new DictionaryBadge { Text = "0 từ", Font = captionFont, Dock = DockStyle.Fill, Margin = new Padding(0, 2, 4, 4), AccessibleName = "Số từ" };
            edit = Button("Sửa", 66); remove = Button("Xóa", 66);
            edit.Glyph = DictionaryGlyph.Edit; remove.Glyph = DictionaryGlyph.Delete; remove.Danger = true;
            foreach (var button in new[] { edit, remove }) { button.Quiet = true; button.Font = captionFont; button.Dock = DockStyle.Fill; button.Margin = new Padding(2, 0, 0, 2); }
            actions.Controls.Add(count, 0, 0); actions.Controls.Add(edit, 1, 0); actions.Controls.Add(remove, 2, 0); browser.Controls.Add(actions, 0, 2);
            grid = new DictionaryGrid { Dock = DockStyle.Fill, Font = bodyFont, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false, AllowUserToResizeColumns = false, AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None,
                AutoGenerateColumns = false, BorderStyle = BorderStyle.None, CellBorderStyle = DataGridViewCellBorderStyle.None,
                ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None, ColumnHeadersHeight = 32,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing, EnableHeadersVisualStyles = false,
                MultiSelect = false, RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, ScrollBars = ScrollBars.None, ShowCellToolTips = false,
                AccessibleName = "Danh sách từ Việt–Nhật" };
            grid.RowTemplate.Height = grid.RowTemplate.MinimumHeight = 46; // px tại 100%; vừa 7 từ gốc trong panel chuẩn.
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "VIETNAMESE", HeaderText = "Tiếng Việt", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 47 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "JAPANESE", HeaderText = "Tiếng Nhật", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 53 });
            // Cột thường, tự vẽ icon: không để nút native vẽ viền hover/pressed chồng lên icon.
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "COPY", HeaderText = "", Width = 34 });
            foreach (DataGridViewColumn col in grid.Columns) col.SortMode = DataGridViewColumnSortMode.NotSortable;
            grid.DefaultCellStyle.Font = grid.Columns["VIETNAMESE"].DefaultCellStyle.Font = bodyFont;
            grid.Columns["JAPANESE"].DefaultCellStyle.Font = japaneseFont;
            grid.DefaultCellStyle.Padding = new Padding(7, 6, 5, 6); grid.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(7, 0, 0, 0);
            grid.SelectionChanged += (s, e) => { UpdateActions(); grid.Invalidate(); };
            grid.CellClick += (s, e) => { if (e.RowIndex >= 0 && e.ColumnIndex == 2) CopyEntry(grid.Rows[e.RowIndex].Tag as JapaneseDictionaryEntry); };
            grid.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0 && e.ColumnIndex != 2) BeginEdit(SelectedEntry()); }; grid.CellPainting += PaintDictionaryCell;
            grid.CellMouseEnter += (s, e) => SetCopyHover(e.ColumnIndex == 2 ? e.RowIndex : -1);
            grid.MouseLeave += (s, e) => SetCopyHover(-1);
            listHost = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty }; listHost.Controls.Add(grid);
            scroll = new DictionaryScroll(grid) { Dock = DockStyle.Right, Width = 10 }; listHost.Controls.Add(scroll);
            empty = Label("Chưa có từ vựng.", bodyFont); empty.TextAlign = ContentAlignment.MiddleCenter; empty.Visible = false;
            listHost.Controls.Add(empty); browser.Controls.Add(listHost, 0, 3);
            deleteHost = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, Visible = false };
            deletePrompt = Label("", bodyFont); deletePrompt.Dock = DockStyle.Top; deletePrompt.Height = 40;
            deletePrompt.Padding = new Padding(0, 6, 0, 0);
            confirmDelete = Button("Xóa từ này", 150, true); confirmDelete.Danger = true; cancelDelete = Button("Giữ lại", 120);
            confirmDelete.Location = new Point(0, 44); cancelDelete.Location = new Point(158, 44);
            deleteHost.Controls.AddRange(new Control[] { confirmDelete, cancelDelete, deletePrompt }); browser.Controls.Add(deleteHost, 0, 4);
            editor = CreateLayout(32, 24, 68, 28, 94, 0, 44); editor.Visible = false;
            editorTitle = Label("Thêm từ", titleFont); editor.Controls.Add(editorTitle, 0, 0); editor.Controls.Add(Label("Tiếng Việt", strongFont), 0, 1);
            vietnamese = Input("Nghĩa tiếng Việt", bodyFont); editor.Controls.Add(InputFrame(vietnamese), 0, 2);
            editor.Controls.Add(Label("Tiếng Nhật", strongFont), 0, 3); japanese = Input("Từ hoặc câu tiếng Nhật", japaneseFont); editor.Controls.Add(InputFrame(japanese), 0, 4);
            editorMessage = Label("Lưu để dùng ngay cho những lần sau.", bodyFont); editorMessage.Padding = new Padding(0, 8, 0, 0); editor.Controls.Add(editorMessage, 0, 5);
            var finish = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            finish.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60)); finish.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
            save = Button("Lưu từ", 150, true); cancel = Button("Hủy", 100); save.Dock = cancel.Dock = DockStyle.Fill;
            finish.Controls.Add(save, 0, 0); finish.Controls.Add(cancel, 1, 0); editor.Controls.Add(finish, 0, 6);
            Controls.Add(browser); Controls.Add(editor);
            add.Click += (s, e) => BeginEdit(null); edit.Click += (s, e) => BeginEdit(SelectedEntry()); remove.Click += (s, e) => AskDelete();
            save.Click += (s, e) => SaveEntry(); cancel.Click += (s, e) => CancelEdit(); confirmDelete.Click += (s, e) => DeleteEntry(); cancelDelete.Click += (s, e) => CancelDelete();
            ready = true; ApplyTheme(false); UpdateActions(); ResumeLayout(true); LayoutSearch();
        }
        private TableLayoutPanel CreateLayout(params int[] heights)
        {
            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = heights.Length, Margin = Padding.Empty };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            foreach (int height in heights) panel.RowStyles.Add(new RowStyle(height == 0 ? SizeType.Percent : SizeType.Absolute, height == 0 ? 100 : Math.Max(0, height)));
            return panel;
        }
        private static Label Label(string text, Font font) { return new Label { Text = text, Font = font, Dock = DockStyle.Fill, Margin = Padding.Empty }; }
        private DictionaryButton Button(string text, int width, bool primary = false)
        { return new DictionaryButton { Text = text, Width = width, Height = 34, Font = strongFont, Primary = primary, Margin = new Padding(0, 0, 6, 3), Cursor = Cursors.Hand, UseVisualStyleBackColor = false }; }
        private static TextBox Input(string name, Font font)
        { return new TextBox { BorderStyle = BorderStyle.None, Multiline = true, Dock = DockStyle.Fill, Font = font, MaxLength = JapaneseDictionaryStore.MaxTextLength, ScrollBars = ScrollBars.None, AccessibleName = name, AcceptsReturn = true }; }
        private Panel InputFrame(TextBox input)
        {
            var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 8, 10, 8), Margin = new Padding(0, 0, 0, 5) };
            host.Controls.Add(input); host.Paint += (s, e) => PaintInputSurface(e.Graphics, host.ClientRectangle, input.Focused);
            input.GotFocus += (s, e) => host.Invalidate(); input.LostFocus += (s, e) => host.Invalidate(); return host;
        }
        private void LayoutSearch()
        {
            if (search == null || clearSearch == null) return;
            // Theo chiều cao control sau scale của form, không nhân DPI thêm lần nữa.
            float scale = searchHost.Height / 34F;
            int left = (int)Math.Round(34 * scale), right = (int)Math.Round(32 * scale), buttonSize = (int)Math.Round(26 * scale);
            search.SetBounds(left, Math.Max(0, (searchHost.Height - search.PreferredHeight) / 2), Math.Max(1, searchHost.Width - left - right), search.PreferredHeight);
            searchHint.Bounds = search.Bounds;
            clearSearch.SetBounds(searchHost.Width - right, (searchHost.Height - buttonSize) / 2, buttonSize, buttonSize);
        }
        private void UpdateSearchChrome()
        {
            searchHint.Visible = search.TextLength == 0 && !search.Focused;
            clearSearch.Visible = search.TextLength > 0; searchHint.BringToFront(); clearSearch.BringToFront(); searchHost.Invalidate();
        }
        private void PaintSearch(object sender, PaintEventArgs e)
        {
            PaintInputSurface(e.Graphics, searchHost.ClientRectangle, search.Focused);
            float scale = searchHost.Height / 34F;
            var state = e.Graphics.Save();
            try
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var pen = new Pen(search.Focused ? accent : muted, 1.5F * scale))
                {
                    pen.StartCap = pen.EndCap = LineCap.Round;
                    float x = 12 * scale, y = searchHost.Height / 2F - 5 * scale;
                    e.Graphics.DrawEllipse(pen, x, y, 8 * scale, 8 * scale);
                    e.Graphics.DrawLine(pen, x + 7 * scale, y + 7 * scale, x + 11 * scale, y + 11 * scale);
                }
            }
            finally { e.Graphics.Restore(state); }
        }
        private void PaintInputSurface(Graphics graphics, Rectangle bounds, bool focused)
        {
            if (bounds.Width < 2 || bounds.Height < 2) return;
            var state = graphics.Save();
            try
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var path = Rounded(new RectangleF(1, 1, bounds.Width - 2, bounds.Height - 2), 9F))
                {
                    using (var fill = new SolidBrush(fieldSurface)) graphics.FillPath(fill, path);
                    using (var pen = new Pen(focused ? accent : border, focused ? 1.5F : 1F)) graphics.DrawPath(pen, path);
                }
            }
            finally { graphics.Restore(state); }
        }
        public void ReloadEntries()
        {
            if (!ready || IsDisposed || Disposing) return;
            try
            {
                var snapshot = store.Load(); entries = snapshot.Entries; hasLoaded = true; RenderRows();
                ShowStatus(snapshot.Warning.Length == 0 ? entries.Count + " từ · Đã sẵn sàng" : snapshot.Warning,
                    snapshot.Warning.Length == 0 ? JapaneseDictionaryStatusKind.Information : JapaneseDictionaryStatusKind.Warning);
            }
            catch (Exception ex)
            {
                if (!hasLoaded) { entries = store.LoadDefaults().Entries; RenderRows(); }
                ShowStatus("Không đọc được từ riêng. " + StorageError(ex), JapaneseDictionaryStatusKind.Error);
            }
        }
        private static string SearchKey(string value)
        {
            var text = new StringBuilder();
            foreach (char c in JapaneseDictionaryStore.Normalize(value).Normalize(NormalizationForm.FormD))
                if (c < '\u0300' || c > '\u036f') text.Append(c == 'đ' || c == 'Đ' ? 'd' : char.ToLowerInvariant(c));
            return text.ToString().Normalize(NormalizationForm.FormC);
        }
        private void RenderRows(string selectedId = null)
        {
            if (!ready || IsDisposed || Disposing) return;
            selectedId = selectedId ?? (SelectedEntry() == null ? null : SelectedEntry().Id); string query = SearchKey(search.Text); refreshing = true;
            try
            {
                hoveredCopyRow = -1; grid.Cursor = Cursors.Default; grid.Rows.Clear();
                foreach (var entry in entries)
                {
                    if (!SearchKey(entry.Vietnamese).Contains(query) && !SearchKey(entry.Japanese).Contains(query)) continue;
                    int index = grid.Rows.Add(entry.Vietnamese, entry.Japanese, "Copy"); grid.Rows[index].Tag = entry;
                }
                grid.ClearSelection(); grid.CurrentCell = null;
                foreach (DataGridViewRow row in grid.Rows)
                    if (((JapaneseDictionaryEntry)row.Tag).Id == selectedId) { grid.CurrentCell = row.Cells[0]; row.Selected = true; break; }
                count.Text = (query.Length == 0 ? entries.Count.ToString() : grid.Rows.Count + " / " + entries.Count) + " từ";
                empty.Text = entries.Count == 0 ? "Chưa có từ vựng.\r\nBấm + Thêm từ để bắt đầu." : "Không tìm thấy từ phù hợp.";
                empty.Visible = grid.Rows.Count == 0; if (empty.Visible) empty.BringToFront();
            }
            finally { refreshing = false; }
            UpdateActions();
        }
        private JapaneseDictionaryEntry SelectedEntry() { return grid == null || grid.SelectedRows.Count == 0 ? null : grid.SelectedRows[0].Tag as JapaneseDictionaryEntry; }
        private void UpdateActions()
        {
            if (!ready || refreshing || IsDisposed || Disposing) return;
            edit.Enabled = remove.Enabled = SelectedEntry() != null && deleting == null; add.Enabled = deleting == null; search.Enabled = deleting == null; grid.Enabled = deleting == null;
            clearSearch.Enabled = deleting == null;
        }
        private void BeginEdit(JapaneseDictionaryEntry entry)
        {
            if (deleting != null) return;
            editing = entry; editorTitle.Text = entry == null ? "Thêm từ mới" : "Sửa từ";
            vietnamese.Text = entry == null ? "" : entry.Vietnamese; japanese.Text = entry == null ? "" : entry.Japanese;
            editorHasError = false; editorMessage.Text = "Lưu để dùng ngay cho những lần sau."; editorMessage.ForeColor = muted;
            browser.Visible = false; editor.Visible = true; editor.BringToFront(); vietnamese.Focus();
        }
        private void CancelEdit() { editor.Visible = false; browser.Visible = true; editing = null; ReloadEntries(); grid.Focus(); }
        private void SaveEntry()
        {
            save.Enabled = false;
            try
            {
                string vi = vietnamese.Text, ja = japanese.Text;
                string validation = JapaneseDictionaryStore.Validate(ref vi, ref ja);
                if (validation != null) { EditorError(validation); return; }
                string id; var snapshot = store.Save(editing, vi, ja, false, out id);
                entries = snapshot.Entries; hasLoaded = true; editing = null;
                editor.Visible = false; browser.Visible = true; search.Clear(); RenderRows(id); grid.Focus();
                ShowStatus("Đã lưu từ: " + vi, JapaneseDictionaryStatusKind.Success);
            }
            catch (Exception ex) { EditorError("Chưa lưu được. " + StorageError(ex)); }
            finally { save.Enabled = true; }
        }
        private void EditorError(string value)
        { editorHasError = true; editorMessage.Text = value; editorMessage.ForeColor = errorInk; RaiseStatus(value, JapaneseDictionaryStatusKind.Error); }
        private void AskDelete()
        {
            deleting = SelectedEntry(); if (deleting == null) return;
            deletePrompt.Text = "Xóa từ “" + deleting.Vietnamese + "”?";
            ShowStatus(deletePrompt.Text, JapaneseDictionaryStatusKind.Warning);
            browser.RowStyles[4].Height = confirmDelete.Bottom + 6; deleteHost.Visible = true; UpdateActions();
        }
        private void CancelDelete() { deleting = null; deleteHost.Visible = false; browser.RowStyles[4].Height = 0; UpdateActions(); }
        private void DeleteEntry()
        {
            if (deleting == null) return;
            try
            {
                string id; var snapshot = store.Save(deleting, deleting.Vietnamese, deleting.Japanese, true, out id);
                entries = snapshot.Entries; CancelDelete(); RenderRows(); ShowStatus("Đã xóa từ khỏi từ điển của bạn.", JapaneseDictionaryStatusKind.Success);
            }
            catch (Exception ex) { CancelDelete(); ShowStatus("Chưa xóa được. " + StorageError(ex), JapaneseDictionaryStatusKind.Error); }
        }
        private static string StorageError(Exception error)
        {
            if (error is InvalidDataException || error is InvalidOperationException) return error.Message;
            if (error is UnauthorizedAccessException) return "Không có quyền truy cập thư mục từ riêng. Kiểm tra quyền rồi thử lại.";
            if (error is XmlException) return "File từ riêng bị lỗi định dạng; file vẫn được giữ nguyên.";
            if (error is IOException) return "File đang được sử dụng hoặc ổ đĩa chưa sẵn sàng. Nội dung được giữ lại để thử lại.";
            return error.Message;
        }
        internal static string NormalizeJapaneseForDrawing(string text) { return (text ?? "").Replace('\u2300', '\u03c6'); }
        private void CopyEntry(JapaneseDictionaryEntry entry)
        {
            if (entry == null) return;
            try { Clipboard.SetText(entry.Japanese, TextDataFormat.UnicodeText); ShowStatus("Đã copy: " + entry.Japanese, JapaneseDictionaryStatusKind.Success); }
            catch (Exception ex) { ShowStatus("Chưa copy được. " + ex.Message, JapaneseDictionaryStatusKind.Error); }
        }
        private void FocusSearch() { search.Focus(); search.SelectAll(); }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (editor.Visible)
            {
                if (keyData == (Keys.Control | Keys.S)) { SaveEntry(); return true; }
                // Enter để IME chốt chữ Nhật; không tự lưu khi đang gõ.
                return base.ProcessCmdKey(ref msg, keyData);
            }
            if (deleting != null) { if (keyData == Keys.Escape) { CancelDelete(); return true; } return base.ProcessCmdKey(ref msg, keyData); }
            if (keyData == Keys.Escape && searchHost.ContainsFocus && search.TextLength > 0) { search.Clear(); search.Focus(); return true; }
            if (keyData == (Keys.Control | Keys.N)) { BeginEdit(null); return true; }
            if (keyData == (Keys.Control | Keys.F)) { FocusSearch(); return true; }
            if (grid.ContainsFocus)
            {
                if (keyData == Keys.F2 && SelectedEntry() != null) { BeginEdit(SelectedEntry()); return true; }
                if (keyData == Keys.Delete) { AskDelete(); return true; }
                if (keyData == Keys.Enter || keyData == (Keys.Control | Keys.C)) { CopyEntry(SelectedEntry()); return true; }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
        private void ShowStatus(string value, JapaneseDictionaryStatusKind kind)
        { RaiseStatus(value, kind); } // Status chung ở MainForm; lỗi nhập liệu vẫn hiện ngay trong editor.
        private void RaiseStatus(string value, JapaneseDictionaryStatusKind kind) { var handler = StatusChanged; if (handler != null) handler(this, new JapaneseDictionaryStatusEventArgs(value, kind)); }
        public void ApplyTheme(bool dark)
        {
            ApplyTheme(dark, dark ? Color.FromArgb(16, 16, 16) : Color.FromArgb(247, 249, 252),
                dark ? Color.FromArgb(201, 122, 64) : Color.FromArgb(30, 58, 138));
        }
        internal void ApplyTheme(bool dark, Color background, Color primary)
        {
            if (!ready || IsDisposed || Disposing) return;
            // Nền list theo khung ngoài; ô nhập tách nhẹ, màu nhấn lấy từ MainForm.
            darkMode = dark; surface = background; accent = primary;
            fieldSurface = dark ? Color.FromArgb(26, 27, 30) : Color.White;
            ink = dark ? Color.FromArgb(226, 232, 240) : Color.FromArgb(38, 48, 62);
            muted = dark ? Color.FromArgb(160, 170, 184) : Color.FromArgb(110, 121, 137);
            border = dark ? Color.FromArgb(55, 55, 55) : Color.FromArgb(216, 224, 235);
            separator = dark ? Color.FromArgb(38, 38, 38) : Color.FromArgb(229, 234, 242);
            selection = dark ? Color.FromArgb(48, 38, 30) : Color.FromArgb(233, 239, 250);
            errorInk = dark ? Color.FromArgb(255, 150, 150) : Color.FromArgb(183, 39, 50);
            ThemeChildren(this);
            // Thumb tối pha từ nền và accent để cùng tông với list.
            scroll.ThumbColor = dark ? Blend(surface, accent, .65F) : Color.FromArgb(181, 194, 211);
            headerCaption.ForeColor = searchHint.ForeColor = empty.ForeColor = muted; editorMessage.ForeColor = editorHasError ? errorInk : muted;
            dictionaryMark.AccentTone = accent; dictionaryMark.CardTone = fieldSurface; dictionaryMark.LetterTone = dark ? Color.FromArgb(30, 25, 21) : Color.White;
            searchHint.BackColor = fieldSurface; clearSearch.CanvasTone = fieldSurface;
            count.ForeColor = accent; ((DictionaryBadge)count).FillTone = selection;
            grid.BackgroundColor = surface; grid.GridColor = border; grid.DefaultCellStyle.BackColor = surface; grid.DefaultCellStyle.ForeColor = ink;
            grid.DefaultCellStyle.SelectionBackColor = selection; grid.DefaultCellStyle.SelectionForeColor = ink;
            grid.ColumnHeadersDefaultCellStyle.BackColor = surface;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = muted; grid.ColumnHeadersDefaultCellStyle.Font = strongFont;
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = grid.ColumnHeadersDefaultCellStyle.BackColor; grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = muted; Invalidate(true);
        }
        private void ThemeChildren(Control control)
        {
            control.BackColor = control is TextBox ? fieldSurface : surface; control.ForeColor = ink; foreach (Control child in control.Controls) ThemeChildren(child);
            var button = control as DictionaryButton;
            if (button != null)
            {
                Color tone = button.Danger ? errorInk : accent;
                button.FlatStyle = FlatStyle.Flat; button.FlatAppearance.BorderSize = 0; button.BackColor = button.Primary ? tone : surface;
                button.ForeColor = button.Primary ? (darkMode ? Color.FromArgb(30, 25, 21) : Color.White) : button.Danger ? errorInk : ink;
                button.CanvasTone = surface; button.BorderTone = border; button.FocusTone = tone;
                button.HoverTone = button.Danger ? Blend(surface, errorInk, .10F) : selection; button.DisabledInk = muted;
            }
        }
        private void SetCopyHover(int row)
        {
            int previous = hoveredCopyRow; hoveredCopyRow = row;
            grid.Cursor = row >= 0 ? Cursors.Hand : Cursors.Default;
            if (previous >= 0 && previous < grid.Rows.Count) grid.InvalidateCell(2, previous);
            if (row >= 0 && row < grid.Rows.Count) grid.InvalidateCell(2, row);
        }
        private void PaintDictionaryCell(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (!ready || e.RowIndex < 0 || e.ColumnIndex < 0) return;
            // WM_PAINT có thể chỉ cập nhật một phần ô khi cuộn/hover. Không vẽ ra ngoài clip,
            // và phải trả Graphics về trạng thái cũ để ô kế tiếp không nhận AntiAlias của icon.
            var state = e.Graphics.Save();
            try
            {
                var clip = Rectangle.Intersect(e.CellBounds, e.ClipBounds);
                clip.Intersect(new Rectangle(0, grid.ColumnHeadersHeight, grid.Width, Math.Max(0, grid.Height - grid.ColumnHeadersHeight)));
                e.Graphics.SetClip(clip, CombineMode.Intersect);
                e.Graphics.SmoothingMode = SmoothingMode.None;
                bool selected = (e.State & DataGridViewElementStates.Selected) != 0;
                using (var fill = new SolidBrush(selected ? selection : surface)) e.Graphics.FillRectangle(fill, e.CellBounds);
                if (selected && e.ColumnIndex == 0)
                    using (var fill = new SolidBrush(accent)) e.Graphics.FillRectangle(fill, e.CellBounds.Left, e.CellBounds.Top + 12, 2, Math.Max(1, e.CellBounds.Height - 24));
                using (var pen = new Pen(separator)) e.Graphics.DrawLine(pen, e.CellBounds.Left, e.CellBounds.Bottom - 1, e.CellBounds.Right, e.CellBounds.Bottom - 1);
                if (e.ColumnIndex == 2)
                {
                    float x = e.CellBounds.Left + (e.CellBounds.Width - 14F) / 2, y = e.CellBounds.Top + (e.CellBounds.Height - 15F) / 2;
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    if (e.RowIndex == hoveredCopyRow)
                        using (var path = Rounded(new RectangleF(x - 5, y - 6, 24, 27), 5F))
                        using (var fill = new SolidBrush(selection)) e.Graphics.FillPath(fill, path);
                    using (var pen = new Pen(accent, 1.3F))
                    {
                        e.Graphics.DrawRectangle(pen, x + 4, y + 4, 10, 11);
                        e.Graphics.DrawLines(pen, new[] { new PointF(x + 10, y), new PointF(x, y), new PointF(x, y + 11) });
                    }
                }
                else
                {
                    var p = e.CellStyle.Padding;
                    var textBounds = new Rectangle(e.CellBounds.Left + p.Left, e.CellBounds.Top + p.Top,
                        Math.Max(0, e.CellBounds.Width - p.Horizontal), Math.Max(0, e.CellBounds.Height - p.Vertical));
                    TextRenderer.DrawText(e.Graphics, Convert.ToString(e.FormattedValue), e.CellStyle.Font ?? grid.Font,
                        textBounds, ink, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak
                        | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.PreserveGraphicsClipping | TextFormatFlags.PreserveGraphicsTranslateTransform);
                }
            }
            finally { e.Graphics.Restore(state); }
            e.Handled = true;
        }
        private static GraphicsPath Rounded(RectangleF r, float radius)
        {
            var p = new GraphicsPath(); float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height)); if (d <= 0) { p.AddRectangle(r); return p; }
            p.AddArc(r.Left, r.Top, d, d, 180, 90); p.AddArc(r.Right - d, r.Top, d, d, 270, 90); p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90); p.CloseFigure(); return p;
        }
        private static Color Blend(Color from, Color to, float amount)
        { return Color.FromArgb((int)(from.R + (to.R - from.R) * amount), (int)(from.G + (to.G - from.G) * amount), (int)(from.B + (to.B - from.B) * amount)); }
        private enum DictionaryGlyph { None, Add, Edit, Delete, Close }
        private sealed class DictionaryMark : Control
        {
            internal Color AccentTone, CardTone, LetterTone;
            internal DictionaryMark() { DoubleBuffered = true; ResizeRedraw = true; TabStop = false; }
            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.Clear(BackColor); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                float scale = Math.Min(Width / 35F, Height / 34F);
                var back = new RectangleF(1 * scale, 2 * scale, 23 * scale, 24 * scale);
                var front = new RectangleF(12 * scale, 12 * scale, 22 * scale, 21 * scale);
                using (var path = Rounded(back, 6 * scale))
                using (var brush = new SolidBrush(AccentTone)) e.Graphics.FillPath(brush, path);
                TextRenderer.DrawText(e.Graphics, "A", Font, Rectangle.Round(new RectangleF(1 * scale, 3 * scale, 19 * scale, 19 * scale)), LetterTone,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                using (var path = Rounded(front, 6 * scale))
                {
                    using (var brush = new SolidBrush(CardTone)) e.Graphics.FillPath(brush, path);
                    using (var pen = new Pen(AccentTone, 1.2F * scale)) e.Graphics.DrawPath(pen, path);
                }
                TextRenderer.DrawText(e.Graphics, "文", Font, Rectangle.Round(front), AccentTone,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }
        private sealed class DictionaryBadge : Label
        {
            internal Color FillTone;
            internal DictionaryBadge() { DoubleBuffered = true; ResizeRedraw = true; }
            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.Clear(BackColor); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                int width = Math.Min(Width, TextRenderer.MeasureText(Text, Font).Width + 14);
                using (var path = Rounded(new RectangleF(0, 0, Math.Max(1, width), Math.Max(1, Height - 1)), 8F))
                using (var brush = new SolidBrush(FillTone)) e.Graphics.FillPath(brush, path);
                TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(0, 0, width, Height), ForeColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            }
        }
        private sealed class DictionaryGrid : DataGridView
        {
            internal DictionaryGrid() { DoubleBuffered = true; }
            internal int MaximumFirst { get { return Math.Max(0, Rows.Count - Math.Max(1, DisplayedRowCount(false))); } }
            internal void ScrollTo(int first) { if (Rows.Count > 0) FirstDisplayedScrollingRowIndex = Math.Max(0, Math.Min(MaximumFirst, first)); }
            protected override void OnMouseWheel(MouseEventArgs e)
            { if (e.Delta != 0) ScrollTo(Math.Max(0, FirstDisplayedScrollingRowIndex) + (e.Delta < 0 ? 3 : -3)); }
            protected override void OnScroll(ScrollEventArgs e) { base.OnScroll(e); Invalidate(); }
        }
        private sealed class DictionaryScroll : Control
        {
            private readonly DictionaryGrid grid;
            private int grab = -1;
            internal Color ThumbColor;
            internal DictionaryScroll(DictionaryGrid grid)
            {
                this.grid = grid; DoubleBuffered = true; ResizeRedraw = true; TabStop = false;
                grid.Scroll += (s, e) => Invalidate(); grid.RowsAdded += (s, e) => Invalidate();
                grid.RowsRemoved += (s, e) => Invalidate(); grid.SizeChanged += (s, e) => Invalidate();
            }
            private Rectangle Thumb()
            {
                int track = Math.Max(1, Height - grid.ColumnHeadersHeight - 4);
                int h = Math.Min(track, Math.Max(28, track * Math.Max(1, grid.DisplayedRowCount(false)) / Math.Max(1, grid.RowCount)));
                int y = grid.ColumnHeadersHeight + 2 + (track - h) * Math.Max(0, grid.FirstDisplayedScrollingRowIndex) / Math.Max(1, grid.MaximumFirst);
                return new Rectangle(2, y, Math.Max(1, Width - 4), h);
            }
            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.Clear(BackColor); if (grid.MaximumFirst <= 0) return;
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var path = Rounded(Thumb(), 3F)) using (var brush = new SolidBrush(ThumbColor)) e.Graphics.FillPath(brush, path);
            }
            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e); if (e.Button != MouseButtons.Left || grid.MaximumFirst <= 0) return;
                var thumb = Thumb(); grab = thumb.Contains(e.Location) ? e.Y - thumb.Top : thumb.Height / 2; Capture = true; MoveThumb(e.Y);
            }
            private void MoveThumb(int y)
            {
                int available = Height - grid.ColumnHeadersHeight - 4 - Thumb().Height;
                if (available > 0) grid.ScrollTo((int)Math.Round((y - grab - grid.ColumnHeadersHeight - 2) * (double)grid.MaximumFirst / available));
            }
            protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (grab >= 0 && Capture) MoveThumb(e.Y); }
            protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); grab = -1; Capture = false; }
            protected override void OnMouseCaptureChanged(EventArgs e) { base.OnMouseCaptureChanged(e); if (!Capture) grab = -1; }
            protected override void OnMouseWheel(MouseEventArgs e) { if (e.Delta != 0) grid.ScrollTo(Math.Max(0, grid.FirstDisplayedScrollingRowIndex) + (e.Delta < 0 ? 3 : -3)); }
        }
        private sealed class DictionaryButton : Button
        {
            internal bool Primary, Quiet, Danger, Capsule, GlyphBadge;
            internal DictionaryGlyph Glyph;
            internal Color BorderTone, HoverTone, DisabledInk, CanvasTone, FocusTone;
            private bool hover, pressed;
            internal DictionaryButton() { DoubleBuffered = true; ResizeRedraw = true; }
            protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
            protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) pressed = true; Invalidate(); base.OnMouseDown(e); }
            protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
            protected override void OnMouseCaptureChanged(EventArgs e) { if (!Capture) pressed = false; Invalidate(); base.OnMouseCaptureChanged(e); }
            protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space) { pressed = true; Invalidate(); } base.OnKeyDown(e); }
            protected override void OnKeyUp(KeyEventArgs e) { pressed = false; Invalidate(); base.OnKeyUp(e); }
            protected override void OnLostFocus(EventArgs e) { pressed = false; Invalidate(); base.OnLostFocus(e); }
            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.Clear(CanvasTone); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Color fill = Enabled && Primary ? BackColor : hover && Enabled ? HoverTone : CanvasTone;
                if (Enabled && ((Primary && hover) || pressed)) fill = Blend(fill, ForeColor, pressed ? .18F : .10F);
                using (var path = Rounded(new RectangleF(.5F, .5F, Math.Max(1, Width - 1F), Math.Max(1, Height - 1F)), Capsule ? Height / 2F : 8F))
                {
                    using (var brush = new SolidBrush(fill)) e.Graphics.FillPath(brush, path);
                    if (!Quiet && !Primary) using (var pen = new Pen(BorderTone)) e.Graphics.DrawPath(pen, path);
                }
                Color textInk = Enabled ? ForeColor : DisabledInk;
                var textBounds = ClientRectangle;
                if (Glyph != DictionaryGlyph.None)
                {
                    float scale = Font.SizeInPoints / 10F;
                    int glyphWidth = (int)Math.Round((GlyphBadge ? 22 : 14) * scale), gap = Text.Length == 0 ? 0 : (int)Math.Round(5 * scale);
                    int textWidth = Text.Length == 0 ? 0 : TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding).Width;
                    int x = Math.Max(4, (Width - glyphWidth - gap - textWidth) / 2);
                    if (GlyphBadge)
                    {
                        float y = (Height - glyphWidth) / 2F;
                        using (var brush = new SolidBrush(Blend(fill, textInk, .16F))) e.Graphics.FillEllipse(brush, x, y, glyphWidth, glyphWidth);
                        PaintGlyph(e.Graphics, Glyph, x + 4 * scale, y + 4 * scale, scale, textInk);
                    }
                    else PaintGlyph(e.Graphics, Glyph, x, (Height - glyphWidth) / 2F, glyphWidth / 14F, textInk);
                    textBounds = new Rectangle(x + glyphWidth + gap, 0, Math.Max(1, Width - x - glyphWidth - gap - 4), Height);
                }
                if (Text.Length > 0) TextRenderer.DrawText(e.Graphics, Text, Font, textBounds, textInk,
                    (Glyph == DictionaryGlyph.None ? TextFormatFlags.HorizontalCenter : TextFormatFlags.Left) | TextFormatFlags.VerticalCenter
                    | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                if (Focused && ShowFocusCues)
                    using (var path = Rounded(new RectangleF(3, 3, Math.Max(1, Width - 6), Math.Max(1, Height - 6)), Capsule ? Height / 2F - 3 : 5F))
                    using (var pen = new Pen(Primary ? ForeColor : FocusTone)) e.Graphics.DrawPath(pen, path);
            }
            private static void PaintGlyph(Graphics g, DictionaryGlyph glyph, float x, float y, float scale, Color color)
            {
                var state = g.Save();
                try
                {
                    g.TranslateTransform(x, y); g.ScaleTransform(scale, scale);
                    using (var pen = new Pen(color, 1.4F) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                    {
                        if (glyph == DictionaryGlyph.Add) { g.DrawLine(pen, 2, 7, 12, 7); g.DrawLine(pen, 7, 2, 7, 12); }
                        else if (glyph == DictionaryGlyph.Close) { g.DrawLine(pen, 3, 3, 11, 11); g.DrawLine(pen, 11, 3, 3, 11); }
                        else if (glyph == DictionaryGlyph.Edit)
                        {
                            g.DrawPolygon(pen, new[] { new PointF(2, 9), new PointF(9, 2), new PointF(12, 5), new PointF(5, 12), new PointF(1, 13) });
                            g.DrawLine(pen, 7.5F, 3.5F, 10.5F, 6.5F);
                        }
                        else if (glyph == DictionaryGlyph.Delete)
                        {
                            g.DrawLine(pen, 2, 4, 12, 4); g.DrawLine(pen, 5, 1.5F, 9, 1.5F);
                            g.DrawLines(pen, new[] { new PointF(3, 4), new PointF(4, 12), new PointF(10, 12), new PointF(11, 4) });
                            g.DrawLine(pen, 6, 6.5F, 6, 9.5F); g.DrawLine(pen, 8, 6.5F, 8, 9.5F);
                        }
                    }
                }
                finally { g.Restore(state); }
            }
        }
        protected override void Dispose(bool disposing) { ready = false; base.Dispose(disposing); if (disposing) { bodyFont.Dispose(); strongFont.Dispose(); japaneseFont.Dispose(); titleFont.Dispose(); captionFont.Dispose(); } }
    }
}
