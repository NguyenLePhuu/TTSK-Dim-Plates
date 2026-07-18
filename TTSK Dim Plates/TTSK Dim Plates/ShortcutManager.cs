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
        public const string ActionCheckScale = "CheckScale";
        public const string ActionLineDistance = "LineDistance";
        public const string ActionRepeatLast = "RepeatLast";
        public const string ActionOpenGrid = "OpenGrid";
        public const string ActionFitView = "FitView";
        public const string ActionNeighborGrid = "NeighborGrid";
        public const string ActionAutoSection = "AutoSection";
        public const string ActionSlot01 = "Slot01";
        public const string ActionSlot02 = "Slot02";
        public const string ActionSlot03 = "Slot03";
        public const string ActionSlot04 = "Slot04";
        public const string ActionSlot05 = "Slot05";
        public const string ActionSlot06 = "Slot06";

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
            Dictionary<string, Keys> copy = new Dictionary<string, Keys>(StringComparer.OrdinalIgnoreCase);

            foreach (KeyValuePair<string, Keys> pair in _shortcuts)
                copy[pair.Key] = pair.Value;

            return copy;
        }

        public IDictionary<string, Keys> GetDefaultShortcutsCopy()
        {
            Dictionary<string, Keys> copy = new Dictionary<string, Keys>(StringComparer.OrdinalIgnoreCase);

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
                bool hasBatchCreateSetting = false;
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

                    if (string.Equals(actionId, ActionBatchCreate, StringComparison.OrdinalIgnoreCase))
                        hasBatchCreateSetting = true;

                    if (!IsKnownAction(actionId))
                        continue;

                    Keys keys;
                    if (TryParse(keyText, out keys))
                        _shortcuts[actionId] = NormalizeShortcut(keys);
                }

                if (!hasBatchCreateSetting &&
                    NormalizeShortcut(GetShortcut(ActionCreateDrawing)) ==
                    NormalizeShortcut(Keys.Control | Keys.D))
                {
                    _shortcuts[ActionCreateDrawing] = Keys.D;
                    _shortcuts[ActionBatchCreate] = Keys.Control | Keys.D;
                    Save();
                }

                if (NormalizeShortcut(GetShortcut(ActionOpenGrid)) == Keys.Shift)
                {
                    _shortcuts[ActionOpenGrid] = Keys.Q;
                    migratedDefaults = true;
                }

                if (NormalizeShortcut(GetShortcut(ActionFitView)) ==
                    NormalizeShortcut(Keys.Control | Keys.Shift))
                {
                    _shortcuts[ActionFitView] = Keys.W;
                    migratedDefaults = true;
                }

                if (migratedDefaults)
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
                lines.Add("# Edit from Shortcut Settings popup when possible.");

                foreach (ShortcutActionDefinition def in _definitions)
                    lines.Add(def.ActionId + "=" + Format(GetShortcut(def.ActionId)));

                File.WriteAllLines(_filePath, lines.ToArray(), Encoding.UTF8);
            }
            catch
            {
            }
        }

        public bool TryFindAction(Keys keyData, out string actionId)
        {
            actionId = null;
            Keys target = NormalizeShortcut(keyData);

            if (target == Keys.None ||
                IsBareModifier(target) ||
                (target & Keys.Alt) == Keys.Alt)
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
                if (IsAllowedModifierOnlyShortcut(def.ActionId, keys) &&
                    (keys & Keys.Modifiers) == target)
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
                    message = "Shortcut " + Format(keys) + " đang bị trùng giữa " + used[keys] + " và " + def.DisplayName + ".";
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

            return AllowsModifierOnly(actionId) &&
                   (normalized & Keys.KeyCode) == Keys.None &&
                   modifiers != Keys.None &&
                   (modifiers & Keys.Alt) != Keys.Alt;
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

            list.Add(new ShortcutActionDefinition(ActionCreateDrawing, "Create (Run DIM)", "Run Auto Dimension", "+", Keys.D));
            list.Add(new ShortcutActionDefinition(ActionBatchCreate, "Batch Create", "Batch load selected + create drawings", "B", Keys.Control | Keys.D));
            list.Add(new ShortcutActionDefinition(ActionCheckScale, "Check Scale", "Batch load selected + check scale", "S", Keys.Tab));
            list.Add(new ShortcutActionDefinition(ActionLineDistance, "Line Distance", "Pick 2 points to draw line", "L", Keys.L));
            list.Add(new ShortcutActionDefinition(ActionRepeatLast, "Repeat Last Command", "Repeat the last repeatable shortcut", "↻", Keys.Space));
            list.Add(new ShortcutActionDefinition(ActionOpenGrid, "Open Grid", "Open grid view by picked frame", "G", Keys.Q));
            list.Add(new ShortcutActionDefinition(ActionFitView, "Fit View", "Fit active drawing view", "F", Keys.W));
            list.Add(new ShortcutActionDefinition(ActionNeighborGrid, "Neighboring Grid", "Open grid + create neighboring grid marks", "E", Keys.E));
            list.Add(new ShortcutActionDefinition(ActionAutoSection, "Auto Section", "Tự động tạo mặt cắt (Section)", "A", Keys.A));
            list.Add(new ShortcutActionDefinition(ActionSlot01, "Slot01 (Function 1)", "Selected Main Part", "1", Keys.D1));
            list.Add(new ShortcutActionDefinition(ActionSlot02, "Slot02 (Function 2)", "AutoDim function 2", "2", Keys.D2));
            list.Add(new ShortcutActionDefinition(ActionSlot03, "Slot03 (Function 3)", "AutoDim function 3", "3", Keys.D3));
            list.Add(new ShortcutActionDefinition(ActionSlot04, "Slot04 (Function 4)", "Use current Slot04 mode", "4", Keys.D4));
            list.Add(new ShortcutActionDefinition(ActionSlot05, "Slot05 (Function 5)", "Use current Slot05 mode", "5", Keys.D5));
            list.Add(new ShortcutActionDefinition(ActionSlot06, "Slot06 (Function 6)", "AutoDim function 6", "6", Keys.D6));

            return list;
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

            return keyCode == Keys.None ||
                   keyCode == Keys.ControlKey ||
                   keyCode == Keys.ShiftKey ||
                   keyCode == Keys.Menu ||
                   keyCode == Keys.LControlKey ||
                   keyCode == Keys.RControlKey ||
                   keyCode == Keys.LShiftKey ||
                   keyCode == Keys.RShiftKey ||
                   keyCode == Keys.LMenu ||
                   keyCode == Keys.RMenu;
        }

        public static bool HasControlAltShift(Keys keyData)
        {
            return (keyData & Keys.Control) == Keys.Control ||
                   (keyData & Keys.Alt) == Keys.Alt ||
                   (keyData & Keys.Shift) == Keys.Shift;
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

                if (string.Equals(part, "Ctrl", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(part, "Control", StringComparison.OrdinalIgnoreCase))
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

            if (string.Equals(value, "Esc", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "Escape", StringComparison.OrdinalIgnoreCase))
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
            catch
            {
            }

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
            Keys defaultShortcut)
        {
            ActionId = actionId;
            DisplayName = displayName;
            Description = description;
            IconText = iconText;
            DefaultShortcut = defaultShortcut;
        }
    }
}
