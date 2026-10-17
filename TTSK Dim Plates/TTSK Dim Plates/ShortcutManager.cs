using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace TTSK_AutoDim_Plates
{
    public sealed class ShortcutManager
    {
        public const string ActionCreateDrawing = "CreateDrawing";
        public const string ActionBatchCreate = "BatchCreate";
        public const string ActionCheckDim = "CheckDim";
        public const string ActionCheckCombo = "CheckCombo";
        public const string ActionRevision = "Revision";
        public const string ActionCheckScale = "CheckScale";
        public const string ActionLineDistance = "LineDistance";
        public const string ActionRepeatLast = "RepeatLast";
        public const string ActionOpenGrid = "OpenGrid";
        public const string ActionFitView = "FitView";
        public const string ActionNeighborGrid = "NeighborGrid";
        public const string ActionArrangeView = "ArrangeView";
        public const string ActionAutoSection = "AutoSection";
        public const string ActionDimensionSpacing = "DimensionSpacing";
        public const string ActionLineByLength = "LineByLength";
        public const string ActionArrangeReference = "ArrangeReference";
        public const string ActionLoadSelected = "LoadSelected";
        public const string ActionPrintMenu = "PrintMenu";
        public const string ActionPrintPdf = "PrintPdf";
        public const string ActionSnapshot = "Snapshot";
        public const string ActionDictionary = "Dictionary";
        public const string ActionMeetingTranslation = "MeetingTranslation";
        public const string ActionWorkJournal = "WorkJournal";
        public const string ActionShortcutSettings = "ShortcutSettings";
        public const string ActionDrawingTools = "DrawingTools";
        public const string ActionClearLog = "ClearLog";
        public const string ActionSlot01 = "Slot01";
        public const string ActionSlot02 = "Slot02";
        public const string ActionSlot03 = "Slot03";
        public const string ActionSlot04 = "Slot04";
        public const string ActionSlot05 = "Slot05";
        public const string ActionSlot06 = "Slot06";
        public const string ActionSlot07 = "Slot07";
        public const string ActionSlot08 = "Slot08";
        public const string ActionSlot09 = "Slot09";
        public const string ActionSlot10 = "Slot10";
        public const string ActionSlot11 = "Slot11";
        public const string ActionSlot12 = "Slot12";
        public const string ActionSlot13 = "Slot13";
        public const string ActionSlot14 = "Slot14";
        public const string ActionSlot15 = "Slot15";
        public const string ActionSlot16 = "Slot16";
        public const string ActionSlot17 = "Slot17";
        public const string ActionSlot18 = "Slot18";

        private readonly string _filePath;
        private readonly List<ShortcutActionDefinition> _definitions;
        private readonly Dictionary<string, Keys> _shortcuts;

        public ShortcutManager(string baseFolder)
        {
            if (string.IsNullOrEmpty(baseFolder))
                baseFolder = Application.StartupPath;

            _filePath = Path.Combine(baseFolder, "shortcut.cfg");
            _definitions = BuildDefinitions();
            _shortcuts = new Dictionary<string, Keys>(StringComparer.OrdinalIgnoreCase);

            ResetToDefault(false);
        }

        public string FilePath
        {
            get { return _filePath; }
        }

        public IList<ShortcutActionDefinition> GetDefinitions()
        {
            return new List<ShortcutActionDefinition>(_definitions);
        }

        public IDictionary<string, Keys> GetShortcutsCopy()
        {
            Dictionary<string, Keys> copy = new Dictionary<string, Keys>(
                StringComparer.OrdinalIgnoreCase
            );

            foreach (KeyValuePair<string, Keys> pair in _shortcuts)
                copy[pair.Key] = pair.Value;

            return copy;
        }

        public IDictionary<string, Keys> GetDefaultShortcutsCopy()
        {
            Dictionary<string, Keys> copy = new Dictionary<string, Keys>(
                StringComparer.OrdinalIgnoreCase
            );

            foreach (ShortcutActionDefinition def in _definitions)
                copy[def.ActionId] = NormalizeShortcut(def.DefaultShortcut);

            return copy;
        }

        public Keys GetShortcut(string actionId)
        {
            if (string.IsNullOrEmpty(actionId))
                return Keys.None;

            Keys keys;
            if (_shortcuts.TryGetValue(actionId, out keys))
                return keys;

            return Keys.None;
        }

        public void SetShortcut(string actionId, Keys keys)
        {
            if (string.IsNullOrEmpty(actionId))
                return;

            _shortcuts[actionId] = NormalizeShortcut(keys);
        }

        public void SetShortcuts(IDictionary<string, Keys> shortcuts)
        {
            _shortcuts.Clear();

            foreach (ShortcutActionDefinition def in _definitions)
            {
                Keys keys = def.DefaultShortcut;

                if (shortcuts != null && shortcuts.ContainsKey(def.ActionId))
                    keys = shortcuts[def.ActionId];

                _shortcuts[def.ActionId] = NormalizeShortcut(keys);
            }
        }

        public void ResetToDefault(bool save)
        {
            _shortcuts.Clear();

            foreach (ShortcutActionDefinition def in _definitions)
                _shortcuts[def.ActionId] = NormalizeShortcut(def.DefaultShortcut);

            if (save)
                Save();
        }

        public void Load()
        {
            ResetToDefault(false);

            try
            {
                if (!File.Exists(_filePath))
                {
                    Save();
                    return;
                }

                string[] lines = File.ReadAllLines(_filePath, Encoding.UTF8);
                bool currentSchema = Array.Exists(lines, line => line.Trim() == "# ShortcutSchema=2");
                bool journalShortcutCurrent = Array.Exists(lines, line => line.Trim() == "# JournalShortcutVersion=1");
                bool meetingShortcutCurrent = Array.Exists(lines, line => line.Trim() == "# MeetingShortcutVersion=1");
                var loadedActions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                bool hasBatchCreateSetting = false;
                bool hasArrangeViewSetting = false;
                bool hasRevisionSetting = false;
                bool hasSlot07Setting = false;
                bool hasSlot08Setting = false;
                bool hasSlot09Setting = false;
                bool migratedDefaults = false;

                foreach (string rawLine in lines)
                {
                    if (rawLine == null)
                        continue;

                    string line = rawLine.Trim();
                    if (line.Length == 0 || line.StartsWith("#"))
                        continue;

                    int index = line.IndexOf('=');
                    if (index <= 0)
                        continue;

                    string actionId = line.Substring(0, index).Trim();
                    string keyText = line.Substring(index + 1).Trim();

                    if (
                        string.Equals(
                            actionId,
                            ActionBatchCreate,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                        hasBatchCreateSetting = true;

                    if (
                        string.Equals(
                            actionId,
                            ActionArrangeView,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                        hasArrangeViewSetting = true;

                    if (string.Equals(actionId, ActionRevision, StringComparison.OrdinalIgnoreCase))
                        hasRevisionSetting = true;

                    if (string.Equals(actionId, ActionSlot07, StringComparison.OrdinalIgnoreCase))
                        hasSlot07Setting = true;
                    if (string.Equals(actionId, ActionSlot08, StringComparison.OrdinalIgnoreCase))
                        hasSlot08Setting = true;
                    if (string.Equals(actionId, ActionSlot09, StringComparison.OrdinalIgnoreCase))
                        hasSlot09Setting = true;

                    if (!IsKnownAction(actionId))
                        continue;

                    Keys keys;
                    if (TryParse(keyText, out keys))
                    {
                        _shortcuts[actionId] = NormalizeShortcut(keys);
                        loadedActions.Add(actionId);
                    }
                }

                if (
                    !hasBatchCreateSetting
                    && NormalizeShortcut(GetShortcut(ActionCreateDrawing))
                        == NormalizeShortcut(Keys.Control | Keys.D)
                )
                {
                    _shortcuts[ActionCreateDrawing] = Keys.D;
                    _shortcuts[ActionBatchCreate] = Keys.Control | Keys.D;
                    Save();
                }

                if (!hasArrangeViewSetting)
                {
                    if (NormalizeShortcut(GetShortcut(ActionAutoSection)) == Keys.A)
                        _shortcuts[ActionAutoSection] = Keys.None;

                    _shortcuts[ActionArrangeView] = Keys.A;
                    migratedDefaults = true;
                }

                if (!hasRevisionSetting)
                {
                    _shortcuts[ActionRevision] = Keys.R;
                    migratedDefaults = true;
                }

                if (NormalizeShortcut(GetShortcut(ActionOpenGrid)) == Keys.Shift)
                {
                    _shortcuts[ActionOpenGrid] = Keys.Q;
                    migratedDefaults = true;
                }

                if (
                    NormalizeShortcut(GetShortcut(ActionFitView))
                    == NormalizeShortcut(Keys.Control | Keys.Shift)
                )
                {
                    _shortcuts[ActionFitView] = Keys.W;
                    migratedDefaults = true;
                }

                // Existing shortcut.cfg files predate page 2.  Add the new
                // defaults once, then persist them so 7 and 9 work immediately.
                if (!hasSlot07Setting)
                {
                    _shortcuts[ActionSlot07] = Keys.D7;
                    migratedDefaults = true;
                }
                if (!hasSlot08Setting)
                {
                    _shortcuts[ActionSlot08] = Keys.D8;
                    migratedDefaults = true;
                }
                if (!hasSlot09Setting)
                {
                    _shortcuts[ActionSlot09] = Keys.D9;
                    migratedDefaults = true;
                }

                // Preserve custom keys (including deliberately cleared keys after migration).
                // Install missing defaults only when no saved action already owns the chord.
                var pending = new List<ShortcutActionDefinition>();
                foreach (ShortcutActionDefinition def in _definitions)
                {
                    int slot;
                    bool unassignedLegacySlot = !currentSchema && def.ActionId.StartsWith("Slot")
                        && int.TryParse(def.ActionId.Substring(4), out slot) && slot >= 10 && slot <= 18
                        && GetShortcut(def.ActionId) == Keys.None;
                    bool unassignedLegacySection = !currentSchema && def.ActionId == ActionAutoSection
                        && GetShortcut(def.ActionId) == Keys.None;
                    if (!loadedActions.Contains(def.ActionId) || unassignedLegacySlot || unassignedLegacySection)
                    {
                        pending.Add(def);
                        _shortcuts[def.ActionId] = Keys.None;
                    }
                }
                var occupied = new HashSet<Keys>(_shortcuts.Values);
                foreach (ShortcutActionDefinition def in pending)
                {
                    Keys candidate = NormalizeShortcut(def.DefaultShortcut);
                    if (candidate != Keys.None && occupied.Add(candidate))
                        _shortcuts[def.ActionId] = candidate;
                }
                if (!journalShortcutCurrent
                    && GetShortcut(ActionWorkJournal) == (Keys.Control | Keys.Shift | Keys.J)
                    && !_shortcuts.ContainsValue(Keys.Control | Keys.H))
                {
                    _shortcuts[ActionWorkJournal] = Keys.Control | Keys.H;
                    migratedDefaults = true;
                }
                if (!meetingShortcutCurrent && GetShortcut(ActionMeetingTranslation) == Keys.None
                    && !_shortcuts.ContainsValue(Keys.Control | Keys.M))
                    _shortcuts[ActionMeetingTranslation] = Keys.Control | Keys.M;
                if (migratedDefaults || pending.Count > 0 || !currentSchema || !journalShortcutCurrent || !meetingShortcutCurrent)
                    Save();
            }
            catch
            {
                ResetToDefault(false);
            }
        }

        public void Save()
        {
            try
            {
                List<string> lines = new List<string>();
                lines.Add("# TTSK AutoDim shortcut configuration");
                lines.Add("# ShortcutSchema=2");
                lines.Add("# JournalShortcutVersion=1");
                lines.Add("# MeetingShortcutVersion=1");
                lines.Add("# Edit from Shortcut Settings popup when possible.");

                foreach (ShortcutActionDefinition def in _definitions)
                    lines.Add(def.ActionId + "=" + Format(GetShortcut(def.ActionId)));

                File.WriteAllLines(_filePath, lines.ToArray(), Encoding.UTF8);
            }
            catch { }
        }

        public bool TryFindAction(Keys keyData, out string actionId)
        {
            actionId = null;
            Keys target = NormalizeShortcut(keyData);

            if (target == Keys.None || IsBareModifier(target) || (target & Keys.Alt) == Keys.Alt)
                return false;

            foreach (ShortcutActionDefinition def in _definitions)
            {
                Keys keys = GetShortcut(def.ActionId);

                if (keys == Keys.None)
                    continue;

                if (NormalizeShortcut(keys) == target)
                {
                    actionId = def.ActionId;
                    return true;
                }
            }

            return false;
        }

        public bool TryFindModifierOnlyAction(Keys keyData, out string actionId)
        {
            actionId = null;
            Keys target = NormalizeShortcut(keyData) & Keys.Modifiers;

            if (target == Keys.None || (target & Keys.Alt) == Keys.Alt)
                return false;

            foreach (ShortcutActionDefinition def in _definitions)
            {
                Keys keys = NormalizeShortcut(GetShortcut(def.ActionId));
                if (
                    IsAllowedModifierOnlyShortcut(def.ActionId, keys)
                    && (keys & Keys.Modifiers) == target
                )
                {
                    actionId = def.ActionId;
                    return true;
                }
            }

            return false;
        }

        public bool TryValidate(IDictionary<string, Keys> shortcuts, out string message)
        {
            message = string.Empty;

            if (shortcuts == null)
                return true;

            Dictionary<Keys, string> used = new Dictionary<Keys, string>();

            foreach (ShortcutActionDefinition def in _definitions)
            {
                if (!shortcuts.ContainsKey(def.ActionId))
                    continue;

                Keys keys = NormalizeShortcut(shortcuts[def.ActionId]);

                if (keys == Keys.None)
                    continue;

                if (IsBareModifier(keys) && !IsAllowedModifierOnlyShortcut(def.ActionId, keys))
                {
                    message = "Shortcut của " + def.DisplayName + " chưa hợp lệ.";
                    return false;
                }

                if (used.ContainsKey(keys))
                {
                    message =
                        "Shortcut "
                        + Format(keys)
                        + " đang bị trùng giữa "
                        + used[keys]
                        + " và "
                        + def.DisplayName
                        + ".";
                    return false;
                }

                used[keys] = def.DisplayName;
            }

            return true;
        }

        public static bool AllowsModifierOnly(string actionId)
        {
            return !string.IsNullOrEmpty(actionId);
        }

        public static bool IsAllowedModifierOnlyShortcut(string actionId, Keys keyData)
        {
            Keys normalized = NormalizeShortcut(keyData);
            Keys modifiers = normalized & Keys.Modifiers;

            return AllowsModifierOnly(actionId)
                && (normalized & Keys.KeyCode) == Keys.None
                && modifiers != Keys.None
                && (modifiers & Keys.Alt) != Keys.Alt;
        }

        public ShortcutActionDefinition FindDefinition(string actionId)
        {
            foreach (ShortcutActionDefinition def in _definitions)
            {
                if (string.Equals(def.ActionId, actionId, StringComparison.OrdinalIgnoreCase))
                    return def;
            }

            return null;
        }

        public bool IsKnownAction(string actionId)
        {
            return FindDefinition(actionId) != null;
        }

        private static List<ShortcutActionDefinition> BuildDefinitions()
        {
            List<ShortcutActionDefinition> list = new List<ShortcutActionDefinition>();

            list.Add(
                new ShortcutActionDefinition(
                    ActionCreateDrawing,
                    "Create (Run DIM)",
                    "Run Auto Dimension",
                    "+",
                    Keys.D
                )
            );
            list.Add(
                new ShortcutActionDefinition(
                    ActionBatchCreate,
                    "Batch Create",
                    "Batch load selected + create drawings",
                    "B",
                    Keys.Control | Keys.D
                )
            );
            list.Add(
                new ShortcutActionDefinition(
                    ActionCheckDim, "Check DIM", "Active: bản đang mở · Batch: dòng chọn / Document Manager; hỗ trợ 1 hoặc nhiều bản", "C", Keys.C
                )
            );
            list.Add(
                new ShortcutActionDefinition(
                    ActionCheckCombo, "Check bản vẽ · Combo", "Scale → DIM → Góc quay (Assembly) · Active hoặc bản vẽ chọn trong list", "C", Keys.Shift | Keys.C
                )
            );
            list.Add(
                new ShortcutActionDefinition(
                    ActionRevision, "Revision", "Mở hộp thoại Revision cho bản vẽ đang mở", "R", Keys.R
                )
            );
            list.Add(
                new ShortcutActionDefinition(
                    ActionCheckScale,
                    "Check Scale",
                    "Kiểm tra bản đang mở hoặc 1/nhiều bản được chọn",
                    "S",
                    Keys.Tab
                )
            );
            list.Add(
                new ShortcutActionDefinition(
                    ActionLineDistance,
                    "Line · chọn 2 điểm",
                    "Pick 2 points to draw line",
                    "L",
                    Keys.L
                )
            );
            list.Add(
                new ShortcutActionDefinition(
                    ActionRepeatLast,
                    "Repeat Last Command",
                    "Repeat the last repeatable shortcut",
                    "↻",
                    Keys.Space
                )
            );
            list.Add(
                new ShortcutActionDefinition(
                    ActionOpenGrid,
                    "Open Grid",
                    "Open grid view by picked frame",
                    "G",
                    Keys.Q
                )
            );
            list.Add(
                new ShortcutActionDefinition(
                    ActionFitView,
                    "Fit View",
                    "Fit active drawing view",
                    "F",
                    Keys.W
                )
            );
            list.Add(
                new ShortcutActionDefinition(
                    ActionNeighborGrid,
                    "Neighboring Grid",
                    "Open grid + create neighboring grid marks",
                    "E",
                    Keys.E
                )
            );
            list.Add(
                new ShortcutActionDefinition(
                    ActionArrangeView,
                    "Arrange View",
                    "Arrange drawing views",
                    "A",
                    Keys.A
                )
            );
            list.Add(
                new ShortcutActionDefinition(
                    ActionAutoSection,
                    "Auto Section",
                    "Tự động tạo mặt cắt (Section)",
                    "—",
                    Keys.Shift | Keys.S
                )
            );
            list.Add(
                new ShortcutActionDefinition(
                    ActionSlot01,
                    "Slot 01 · Main Part",
                    "Selected Main Part",
                    "1",
                    Keys.D1
                )
            );
            list.Add(
                new ShortcutActionDefinition(
                    ActionSlot02,
                    "Slot 02 · Plate tham chiếu",
                    "AutoDim function 2",
                    "2",
                    Keys.D2
                )
            );
            list.Add(
                new ShortcutActionDefinition(
                    ActionSlot03,
                    "Slot 03 · Mặt cắt plate",
                    "AutoDim function 3",
                    "3",
                    Keys.D3
                )
            );
            list.Add(
                new ShortcutActionDefinition(
                    ActionSlot04,
                    "Slot 04 · Pallet",
                    "Use current Slot04 mode",
                    "4",
                    Keys.D4
                )
            );
            list.Add(
                new ShortcutActionDefinition(
                    ActionSlot05,
                    "Slot 05 · Cạnh / lỗ plate",
                    "Use current Slot05 mode",
                    "5",
                    Keys.D5
                )
            );
            list.Add(
                new ShortcutActionDefinition(
                    ActionSlot06,
                    "Slot 06 · Giằng xéo",
                    "Auto dim 3 thanh L + plate liên kết",
                    "6",
                    Keys.D6
                )
            );
            list.Add(
                new ShortcutActionDefinition(
                    ActionSlot07,
                    "Slot 07 · Data Center",
                    "Chế độ Injai Data Center Dầm - Cột",
                    "7",
                    Keys.D7
                )
            );
            list.Add(
                new ShortcutActionDefinition(
                    ActionSlot08,
                    "Slot 08 · Ẩn neighbor",
                    "Giữ neighbor liên kết trực tiếp",
                    "8",
                    Keys.D8
                )
            );
            list.Add(
                new ShortcutActionDefinition(
                    ActionSlot09,
                    "Slot 09 · Keyplan",
                    "Kiểm tra hướng nhìn và đề xuất góc Local",
                    "9",
                    Keys.D9
                )
            );
            list.Add(
                new ShortcutActionDefinition(
                    ActionSlot10,
                    "Slot 10 · Dự phòng",
                    "Chưa có chức năng được gắn",
                    "10",
                    Keys.Control | Keys.D0
                )
            );
            list.Add(
                new ShortcutActionDefinition(
                    ActionSlot11,
                    "Slot 11 · Dự phòng",
                    "Chưa có chức năng được gắn",
                    "11",
                    Keys.Control | Keys.D1
                )
            );
            list.Add(
                new ShortcutActionDefinition(
                    ActionSlot12,
                    "Slot 12 · Dự phòng",
                    "Chưa có chức năng được gắn",
                    "12",
                    Keys.Control | Keys.D2
                )
            );
            list.Add(
                new ShortcutActionDefinition(
                    ActionSlot13,
                    "Slot 13 · Dự phòng",
                    "Chưa có chức năng được gắn",
                    "13",
                    Keys.Control | Keys.D3
                )
            );
            list.Add(
                new ShortcutActionDefinition(
                    ActionSlot14,
                    "Slot 14 · Dự phòng",
                    "Chưa có chức năng được gắn",
                    "14",
                    Keys.Control | Keys.D4
                )
            );
            list.Add(
                new ShortcutActionDefinition(
                    ActionSlot15,
                    "Slot 15 · Dự phòng",
                    "Chưa có chức năng được gắn",
                    "15",
                    Keys.Control | Keys.D5
                )
            );
            list.Add(
                new ShortcutActionDefinition(
                    ActionSlot16,
                    "Slot 16 · Dự phòng",
                    "Chưa có chức năng được gắn",
                    "16",
                    Keys.Control | Keys.D6
                )
            );
            list.Add(
                new ShortcutActionDefinition(
                    ActionSlot17,
                    "Slot 17 · Dự phòng",
                    "Chưa có chức năng được gắn",
                    "17",
                    Keys.Control | Keys.D7
                )
            );
            list.Add(
                new ShortcutActionDefinition(
                    ActionSlot18,
                    "Slot 18 · Kiểm tra khoảng cách",
                    "Kiểm tra khoảng cách đường kích thước",
                    "18",
                    Keys.Control | Keys.D8
                )
            );

            list.Add(new ShortcutActionDefinition(ActionDimensionSpacing, "Dimension Spacing", "Áp dụng khoảng cách GAP đang đặt", "G", Keys.Shift | Keys.G));
            list.Add(new ShortcutActionDefinition(ActionLineByLength, "Line · theo chiều dài", "Vẽ line theo chiều dài đang đặt", "↔", Keys.Shift | Keys.L));
            list.Add(new ShortcutActionDefinition(ActionArrangeReference, "Arrange · theo bản mẫu", "Căn danh sách theo bản đầu được chọn", "V", Keys.Shift | Keys.A));
            list.Add(new ShortcutActionDefinition(ActionLoadSelected, "Load Selected", "Nạp bản vẽ đang chọn trong Document Manager", "↓", Keys.Control | Keys.L));
            // Keep the saved action ID so customized shortcuts are preserved.
            list.Add(new ShortcutActionDefinition(ActionPrintMenu, "Merge PDF", "Gộp PDF từ bản đang mở hoặc 1/nhiều bản được chọn", "P", Keys.Control | Keys.P));
            list.Add(new ShortcutActionDefinition(ActionPrintPdf, "Print PDF", "In PDF riêng từ bản đang mở hoặc 1/nhiều bản được chọn", "P", Keys.None));
            list.Add(new ShortcutActionDefinition(ActionSnapshot, "Snapshot", "Xuất ảnh bản đang mở hoặc 1/nhiều bản được chọn", "S", Keys.Control | Keys.Shift | Keys.P));
            list.Add(new ShortcutActionDefinition(ActionDictionary, "Từ điển", "Mở / đóng từ điển tiếng Nhật", "字", Keys.Control | Keys.J));
            list.Add(new ShortcutActionDefinition(ActionMeetingTranslation, "Dịch họp Nhật – Việt", "Mở cửa sổ dịch phụ đề Teams trực tiếp Nhật → Việt", "訳", Keys.Control | Keys.M));
            list.Add(new ShortcutActionDefinition(ActionWorkJournal, "Nhật ký làm việc", "Xem nhật ký dự án và giờ làm", "H", Keys.Control | Keys.H));
            list.Add(new ShortcutActionDefinition(ActionShortcutSettings, "Shortcut Settings", "Chỉnh phím tắt", "⌨", Keys.Control | Keys.K));
            list.Add(new ShortcutActionDefinition(ActionDrawingTools, "Drawing Tools", "Mở / đóng bảng công cụ", "T", Keys.Control | Keys.T));
            list.Add(new ShortcutActionDefinition(ActionClearLog, "Clear Log", "Xóa nội dung log đang hiển thị", "×", Keys.Control | Keys.Shift | Keys.Delete));

            var ordered = list.FindAll(def => !def.ActionId.StartsWith("Slot"));
            ordered.AddRange(list.FindAll(def => def.ActionId.StartsWith("Slot")));
            return ordered;
        }

        public static Keys NormalizeShortcut(Keys keyData)
        {
            Keys modifiers = keyData & Keys.Modifiers;
            Keys keyCode = keyData & Keys.KeyCode;

            if (keyCode >= Keys.NumPad0 && keyCode <= Keys.NumPad9)
                keyCode = (Keys)((int)Keys.D0 + ((int)keyCode - (int)Keys.NumPad0));

            return modifiers | keyCode;
        }

        public static bool IsBareModifier(Keys keyData)
        {
            Keys keyCode = keyData & Keys.KeyCode;

            return keyCode == Keys.None
                || keyCode == Keys.ControlKey
                || keyCode == Keys.ShiftKey
                || keyCode == Keys.Menu
                || keyCode == Keys.LControlKey
                || keyCode == Keys.RControlKey
                || keyCode == Keys.LShiftKey
                || keyCode == Keys.RShiftKey
                || keyCode == Keys.LMenu
                || keyCode == Keys.RMenu;
        }

        public static bool HasControlAltShift(Keys keyData)
        {
            return (keyData & Keys.Control) == Keys.Control
                || (keyData & Keys.Alt) == Keys.Alt
                || (keyData & Keys.Shift) == Keys.Shift;
        }

        public static string Format(Keys keyData)
        {
            keyData = NormalizeShortcut(keyData);

            if (keyData == Keys.None)
                return "None";

            List<string> parts = new List<string>();

            if ((keyData & Keys.Control) == Keys.Control)
                parts.Add("Ctrl");
            if ((keyData & Keys.Shift) == Keys.Shift)
                parts.Add("Shift");
            if ((keyData & Keys.Alt) == Keys.Alt)
                parts.Add("Alt");

            Keys keyCode = keyData & Keys.KeyCode;
            string keyText = KeyCodeToText(keyCode);

            if (!string.IsNullOrEmpty(keyText))
                parts.Add(keyText);

            if (parts.Count == 0)
                return "None";

            return string.Join(" + ", parts.ToArray());
        }

        public static bool TryParse(string text, out Keys keys)
        {
            keys = Keys.None;

            if (string.IsNullOrWhiteSpace(text))
                return false;

            string value = text.Trim();
            if (string.Equals(value, "None", StringComparison.OrdinalIgnoreCase))
            {
                keys = Keys.None;
                return true;
            }

            string[] parts = value.Split(new char[] { '+' }, StringSplitOptions.RemoveEmptyEntries);
            Keys result = Keys.None;
            bool hasKeyCode = false;

            foreach (string rawPart in parts)
            {
                string part = rawPart.Trim();
                if (part.Length == 0)
                    continue;

                if (
                    string.Equals(part, "Ctrl", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(part, "Control", StringComparison.OrdinalIgnoreCase)
                )
                {
                    result |= Keys.Control;
                    continue;
                }

                if (string.Equals(part, "Shift", StringComparison.OrdinalIgnoreCase))
                {
                    result |= Keys.Shift;
                    continue;
                }

                if (string.Equals(part, "Alt", StringComparison.OrdinalIgnoreCase))
                {
                    result |= Keys.Alt;
                    continue;
                }

                Keys keyCode;
                if (TryParseKeyCode(part, out keyCode))
                {
                    result |= keyCode;
                    hasKeyCode = true;
                    continue;
                }

                return false;
            }

            keys = NormalizeShortcut(result);
            return hasKeyCode || keys != Keys.None;
        }

        private static bool TryParseKeyCode(string text, out Keys keyCode)
        {
            keyCode = Keys.None;

            if (string.IsNullOrWhiteSpace(text))
                return false;

            string value = text.Trim();

            if (value.Length == 1)
            {
                char ch = value[0];

                if (ch >= '0' && ch <= '9')
                {
                    keyCode = (Keys)((int)Keys.D0 + (ch - '0'));
                    return true;
                }

                if (ch >= 'A' && ch <= 'Z')
                {
                    keyCode = (Keys)((int)Keys.A + (ch - 'A'));
                    return true;
                }

                if (ch >= 'a' && ch <= 'z')
                {
                    keyCode = (Keys)((int)Keys.A + (ch - 'a'));
                    return true;
                }
            }

            if (string.Equals(value, "Tab", StringComparison.OrdinalIgnoreCase))
            {
                keyCode = Keys.Tab;
                return true;
            }

            if (
                string.Equals(value, "Esc", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "Escape", StringComparison.OrdinalIgnoreCase)
            )
            {
                keyCode = Keys.Escape;
                return true;
            }

            if (string.Equals(value, "Space", StringComparison.OrdinalIgnoreCase))
            {
                keyCode = Keys.Space;
                return true;
            }

            Keys parsed;
            try
            {
                parsed = (Keys)Enum.Parse(typeof(Keys), value, true);
                keyCode = parsed & Keys.KeyCode;
                return keyCode != Keys.None;
            }
            catch { }

            return false;
        }

        private static string KeyCodeToText(Keys keyCode)
        {
            if (keyCode == Keys.None)
                return string.Empty;

            if (keyCode >= Keys.D0 && keyCode <= Keys.D9)
                return ((int)keyCode - (int)Keys.D0).ToString();

            if (keyCode >= Keys.A && keyCode <= Keys.Z)
                return keyCode.ToString();

            if (keyCode >= Keys.F1 && keyCode <= Keys.F24)
                return keyCode.ToString();

            if (keyCode == Keys.Tab)
                return "Tab";

            if (keyCode == Keys.Escape)
                return "Esc";

            if (keyCode == Keys.Space)
                return "Space";

            return keyCode.ToString();
        }
    }

    public sealed class ShortcutActionDefinition
    {
        public readonly string ActionId;
        public readonly string DisplayName;
        public readonly string Description;
        public readonly string IconText;
        public readonly Keys DefaultShortcut;

        public ShortcutActionDefinition(
            string actionId,
            string displayName,
            string description,
            string iconText,
            Keys defaultShortcut
        )
        {
            ActionId = actionId;
            DisplayName = displayName;
            Description = description;
            IconText = iconText;
            DefaultShortcut = defaultShortcut;
        }
    }
}
