using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Tekla.Structures.Drawing;
using Tekla.Structures.Model;

namespace TTSK_AutoDim_Plates
{
    public class MainForm : Form
    {
        private const string GridVisibilityMacroFileName = "Phu_Macro_GridVisibility.cs";
        private const string GridVisibilityCommandFileName = "TTSK_GridVisibility.command";

        private readonly List<Drawing> _selectedDrawings = new List<Drawing>();

        private bool _isBatchRunning = false;
        private bool _stopRequested = false;
        private int _resumeIndex = 0;
        private bool _gridVisibilityMacroRunning = false;
        private const bool DEFAULT_AUTO_SECTION_ENABLED = false;
        private bool _autoSectionEnabled = DEFAULT_AUTO_SECTION_ENABLED;
        private bool? _batchAutoSectionEnabledSnapshot = null;
        private bool _syncingAutoSectionSwitch = false;

        private RadioButton rbActive;
        private RadioButton rbBatch;
        private Panel btnModeActive;
        private Panel btnModeBatch;
        private SafeRoundedButton btnLoad;
        private SafeRoundedButton btnCheckScale;
        private SafeRoundedButton btnRun;
        private SafeRoundedButton btnClear;
        private SafeRoundedButton btnDictionary;
        private Label lblCount;
        private Label lblStatus;
        private DataGridView dgvDrawings;
        private ThemeSwitch themeSwitch;
        private AutoSectionSwitch autoSectionSwitch;
        private ToolTip autoSectionToolTip;
        private bool _darkMode = false;
        private ShortcutManager _shortcutManager;
        private string _lastRepeatableShortcutActionId;
        private Keys _modifierShortcutCandidate;
        private bool _modifierShortcutCancelled;
        private bool _tabShortcutCandidate;
        private bool _tabShortcutCancelled;

        // ===== PHU SLIDE TOOLS PANEL =====
        private const int MainBaseWidth = 966;
        private const int MainBaseHeight = 640;
        private const int SlideHandleWidth = 8;
        private const int SlideToolsWidth = 350;
        private const int SlidePanelGap = 2;
        private const int SlideRightMargin = 10;
        private const int SlideDetailWidth = SlideToolsWidth;

        private Panel slideHandle;
        private Panel slideToolsPanel;
        private Panel mainFooter;
        private Panel slideDimTool;
        private Panel slideLineTool;
        private Panel slideGridTool;
        private Panel slideArrangeTool;
        private Panel slideAutoDimTool;
        private Panel slideDimPanel;
        private Panel slideLinePanel;
        private Panel slideGridPanel;
        private Panel slideMarkOffsetsPanel;
        private Panel slideArrangePanel;
        private Panel slideAutoDimPanel;
        private JapaneseDictionaryPanel japaneseDictionaryPanel;
        private Label slideHandleLabel;
        private Label slideTitleLabel;
        private Label dimResultLabel;
        private Label lineResultLabel;
        private Label gridResultLabel;
        private Label arrangeResultLabel;
        private Label autoDimResultLabel;
        private NumericUpDown nudDimSpacing;
        private NumericUpDown nudLineDistance;
        private NumericUpDown nudNeighborGridX;
        private NumericUpDown nudNeighborGridY;
        private NumericUpDown nudArrangeGap;
        private ComboBox cboDimScope;
        private Panel arrangeMainHorizontalBox;
        private Panel arrangeMainVerticalBox;
        private Panel arrangeSectionHorizontalBox;
        private Panel arrangeSectionVerticalBox;
        private Panel arrangeVerticalOrderBox;
        private ArrangeOrderSwitch arrangeVerticalOrderSwitch;
        private Label arrangeVerticalOrderIcon;
        private Slot04TargetSwitch slot04TargetSwitch;
        private ThemeButton btnSlot04Auto;
        private bool slot04AutoMode = true;
        private Slot05ModeSwitch slot05ModeSwitch;
        private bool arrangeVerticalBottomUp = false;
        private bool arrangeMainHorizontal = true;
        private bool arrangeSectionHorizontal = true;
        private bool slideToolsOpen = false;
        private bool slideDimOpen = false;
        private bool slideLineOpen = false;
        private bool slideGridOpen = false;
        private bool slideArrangeOpen = false;
        private bool slideAutoDimOpen = false;
        private bool slideDictionaryOpen = false;

        private System.Windows.Forms.Timer slideTimer;
        private int slideTargetWidth = MainBaseWidth + SlideHandleWidth;
        private const int SlideAnimationStep = 18;

        private readonly Color Blue = Color.FromArgb(30, 58, 138);
        private readonly Color BrightBlue = Color.FromArgb(37, 99, 235);
        private readonly Color SoftBg = Color.FromArgb(248, 250, 252);
        private readonly Color PanelBorder = Color.FromArgb(220, 226, 235);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(
            IntPtr hwnd,
            int attr,
            ref int attrValue,
            int attrSize);

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        public MainForm()
        {
            LoadTheme();
            LoadAutoSectionSetting();
            _shortcutManager = new ShortcutManager(Application.StartupPath);
            _shortcutManager.Load();
            BuildUi();
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

        private string ThemeFile
        {
            get
            {
                return System.IO.Path.Combine(
                    Application.StartupPath,
                    "theme.cfg");
            }
        }

        private void SaveTheme()
        {
            try
            {
                System.IO.File.WriteAllText(
                    ThemeFile,
                    _darkMode ? "DARK" : "LIGHT");
            }
            catch
            {
            }
        }

        private void LoadTheme()
        {
            try
            {
                if (!System.IO.File.Exists(ThemeFile))
                    return;

                string text =
                    System.IO.File.ReadAllText(ThemeFile).Trim();

                _darkMode =
                    string.Equals(
                        text,
                        "DARK",
                        StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
            }
        }

        private void SetFormIconFromLogo()
        {
            try
            {
                string logoPath = Application.StartupPath + @"\Resources\logo.png";

                if (!System.IO.File.Exists(logoPath))
                    return;

                using (Bitmap bitmap = new Bitmap(logoPath))
                {
                    IntPtr hIcon = bitmap.GetHicon();
                    Icon = Icon.FromHandle(hIcon);
                }
            }
            catch
            {
            }
        }

        private void BuildUi()
        {
            Text = "TTSK AutoDim Auto Dimension";
            SetFormIconFromLogo();
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = true;
            ClientSize = new System.Drawing.Size(MainBaseWidth + SlideHandleWidth, MainBaseHeight);
            BackColor = SoftBg;
            Font = new Font("Segoe UI", 9F);
            KeyPreview = true;

            Panel header = MakePanel(18, 14, 944, 105);
            Controls.Add(header);

            PictureBox logo = new PictureBox();
            logo.Image = System.Drawing.Image.FromFile(
                Application.StartupPath + @"\Resources\logo.png");
            logo.Location = new Point(18, 18);
            logo.Size = new System.Drawing.Size(70, 70);
            logo.SizeMode = PictureBoxSizeMode.Zoom;
            header.Controls.Add(logo);

            Label title = new Label();
            title.Text = "TTSK VN Auto Dimension";
            title.Font = new Font("Segoe UI", 25F, FontStyle.Bold);
            title.ForeColor = Color.FromArgb(15, 23, 42);
            title.Location = new Point(86, 20);
            title.Size = new System.Drawing.Size(520, 42);
            header.Controls.Add(title);


            Label sub = new Label();
            sub.Text = "Plate Auto Dimension for Tekla Structures 2025 SP7";
            sub.Font = new Font("Segoe UI", 12F);
            sub.ForeColor = Color.FromArgb(75, 85, 99);
            sub.Location = new Point(90, 66);
            sub.Size = new System.Drawing.Size(560, 28);
            header.Controls.Add(sub);

            Label ver = new Label();
            ver.Text = "v1.0.0";
            ver.TextAlign = ContentAlignment.MiddleCenter;
            ver.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
            ver.ForeColor = BrightBlue;
            ver.BackColor = Color.FromArgb(235, 242, 255);
            ver.Location = new Point(838, 34);
            ver.Size = new System.Drawing.Size(70, 38);
            header.Controls.Add(ver);

            themeSwitch = new ThemeSwitch();
            themeSwitch.Location = new Point(748, 37);
            themeSwitch.Size = new System.Drawing.Size(74, 32);
            themeSwitch.CheckedChanged += delegate
            {
                _darkMode = themeSwitch.Checked;
                ApplyTheme();
                SaveTheme();
            };
            header.Controls.Add(themeSwitch);

            // Hidden radio buttons are kept only to preserve the current run logic.
            rbActive = new RadioButton();
            rbBatch = new RadioButton();
            rbActive.Checked = true;
            rbActive.Visible = false;
            rbBatch.Visible = false;
            rbActive.CheckedChanged += delegate { UpdateModeUi(); };
            rbBatch.CheckedChanged += delegate { UpdateModeUi(); };

            Panel modePanel = MakePanel(18, 132, 944, 90);
            Controls.Add(modePanel);

            btnModeActive = MakeModeButton("🗎  ACTIVE", "Bản vẽ hiện hành", 95, 20, 350, 50);
            EventHandler activeModeClick = delegate
            {
                rbActive.Checked = true;
                rbBatch.Checked = false;
                UpdateModeUi();
            };
            WireClickToAll(btnModeActive, activeModeClick);
            modePanel.Controls.Add(btnModeActive);

            btnModeBatch = MakeModeButton("☰  BATCH", "Document Manager", 499, 20, 350, 50);
            EventHandler batchModeClick = delegate
            {
                rbBatch.Checked = true;
                rbActive.Checked = false;
                UpdateModeUi();
            };
            WireClickToAll(btnModeBatch, batchModeClick);
            modePanel.Controls.Add(btnModeBatch);

            Panel listPanel = MakePanel(18, 226, 944, 264);
            Controls.Add(listPanel);

            listPanel.Controls.Add(SectionTitle("▤  DANH SÁCH BẢN VẼ", 20, 20));

            lblCount = new Label();
            lblCount.Text = "Tổng số bản vẽ:  0";
            lblCount.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
            lblCount.ForeColor = Color.FromArgb(15, 23, 42);
            lblCount.TextAlign = ContentAlignment.MiddleRight;
            lblCount.Location = new Point(385, 20);
            lblCount.Size = new System.Drawing.Size(240, 28);
            listPanel.Controls.Add(lblCount);

            btnCheckScale = new SafeRoundedButton();
            btnCheckScale.Text = "⌕  Check Scale";
            btnCheckScale.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            btnCheckScale.Location = new Point(642, 18);
            btnCheckScale.Size = new System.Drawing.Size(124, 32);
            btnCheckScale.Click += btnCheckScale_Click;
            listPanel.Controls.Add(btnCheckScale);

            btnLoad = new SafeRoundedButton();
            btnLoad.Text = "📁  Load Selected";
            btnLoad.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            btnLoad.Location = new Point(786, 18);
            btnLoad.Size = new System.Drawing.Size(144, 32);
            btnLoad.Click += btnLoad_Click;
            listPanel.Controls.Add(btnLoad);

            dgvDrawings = new CleanDataGridView();
            dgvDrawings.Location = new Point(20, 72);
            dgvDrawings.Size = new System.Drawing.Size(904, 170);
            dgvDrawings.AllowUserToAddRows = false;
            dgvDrawings.AllowUserToDeleteRows = false;
            dgvDrawings.AllowUserToResizeRows = false;
            dgvDrawings.AllowUserToResizeColumns = false;
            dgvDrawings.ReadOnly = true;
            dgvDrawings.RowHeadersVisible = false;
            dgvDrawings.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            dgvDrawings.MultiSelect = false;
            dgvDrawings.BackgroundColor = Color.White;
            dgvDrawings.BorderStyle = BorderStyle.None;
            dgvDrawings.GridColor = Color.FromArgb(226, 232, 240);
            dgvDrawings.Font = new Font("Segoe UI", 9F);
            dgvDrawings.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            dgvDrawings.EnableHeadersVisualStyles = false;

            dgvDrawings.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(248, 250, 252);
            dgvDrawings.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(15, 23, 42);
            dgvDrawings.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            dgvDrawings.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(248, 250, 252);
            dgvDrawings.ColumnHeadersDefaultCellStyle.SelectionForeColor = Color.FromArgb(15, 23, 42);
            dgvDrawings.ColumnHeadersHeight = 28;
            dgvDrawings.ColumnHeadersDefaultCellStyle.Alignment =
                DataGridViewContentAlignment.MiddleCenter;

            dgvDrawings.DefaultCellStyle.BackColor = Color.White;
            dgvDrawings.DefaultCellStyle.ForeColor = Color.FromArgb(15, 23, 42);
            dgvDrawings.DefaultCellStyle.SelectionBackColor = Color.FromArgb(239, 246, 255);
            dgvDrawings.DefaultCellStyle.SelectionForeColor = Color.FromArgb(15, 23, 42);
            dgvDrawings.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(250, 252, 255);
            dgvDrawings.RowTemplate.Height = 25;

            dgvDrawings.Columns.Add("STT", "STT");
            dgvDrawings.Columns.Add("MARK", "MARK");
            dgvDrawings.Columns.Add("REV", "REV");
            dgvDrawings.Columns.Add("CHANGES", "CHANGES");
            dgvDrawings.Columns.Add("STATUS", "STATUS");
            dgvDrawings.Columns.Add("RESULT", "RESULT");

            foreach (DataGridViewColumn col in dgvDrawings.Columns)
            {
                col.HeaderCell.Style.Alignment =
                    DataGridViewContentAlignment.MiddleCenter;
            }
            foreach (DataGridViewColumn col in dgvDrawings.Columns)
            {
                col.SortMode = DataGridViewColumnSortMode.NotSortable;
            }

            // Cân lại chiều rộng các cột cho hài hòa.
            // Tổng grid hiện tại 904px: CHANGES vừa đủ đọc ngày giờ, STATUS / RESULT không bị quá nhỏ.
            dgvDrawings.Columns["STT"].Width = 60;
            dgvDrawings.Columns["MARK"].Width = 190;
            dgvDrawings.Columns["REV"].Width = 70;
            dgvDrawings.Columns["CHANGES"].Width = 240;
            dgvDrawings.Columns["CHANGES"].MinimumWidth = 220;
            dgvDrawings.Columns["CHANGES"].AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            dgvDrawings.Columns["STATUS"].Width = 160;
            dgvDrawings.Columns["RESULT"].Width = 170;
            dgvDrawings.Columns["RESULT"].MinimumWidth = 150;
            dgvDrawings.Columns["RESULT"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

            dgvDrawings.Columns["STT"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            dgvDrawings.Columns["REV"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            dgvDrawings.Columns["CHANGES"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            dgvDrawings.Columns["STATUS"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            dgvDrawings.Columns["RESULT"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            dgvDrawings.Columns["MARK"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;

            dgvDrawings.ClearSelection();
            dgvDrawings.CellDoubleClick += dgvDrawings_CellDoubleClick;
            listPanel.Controls.Add(dgvDrawings);

            Panel runPanel = MakePanel(18, 497, 944, 64);
            Controls.Add(runPanel);

            btnRun = new SafeRoundedButton();
            btnRun.Text = "▶  CREATE DRAWING";
            btnRun.Font = new Font("Segoe UI", 16F, FontStyle.Bold);
            btnRun.FillColor = Blue;
            btnRun.BorderColor = Blue;
            btnRun.TextColor = Color.White;
            btnRun.BorderRadius = 8;
            btnRun.Location = new Point(20, 10);
            btnRun.Size = new System.Drawing.Size(904, 40);
            btnRun.Click += btnRun_Click;
            runPanel.Controls.Add(btnRun);

            Label hint = new Label();
            hint.Text = "Nhấn nút để bắt đầu chạy Auto Dimension";
            hint.TextAlign = ContentAlignment.MiddleCenter;
            hint.ForeColor = Color.FromArgb(100, 116, 139);
            hint.Font = new Font("Segoe UI", 9F);
            hint.Location = new Point(20, 48);
            hint.Size = new System.Drawing.Size(904, 18);
            runPanel.Controls.Add(hint);

            Panel status = MakePanel(18, 566, 944, 30);
            Controls.Add(status);

            lblStatus = new Label();
            lblStatus.Text = "✓  Ready    |    0 bản vẽ";
            lblStatus.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            lblStatus.ForeColor = Color.FromArgb(22, 163, 74);
            lblStatus.Location = new Point(20, 6);
            lblStatus.Size = new System.Drawing.Size(630, 20);
            status.Controls.Add(lblStatus);

            autoSectionSwitch = new AutoSectionSwitch();
            autoSectionSwitch.Location = new Point(662, 4);
            autoSectionSwitch.Size = new System.Drawing.Size(48, 22);
            autoSectionSwitch.Checked = _autoSectionEnabled;
            autoSectionSwitch.CheckedChanged += delegate
            {
                if (_syncingAutoSectionSwitch)
                    return;

                SetAutoSectionEnabled(autoSectionSwitch.Checked, true);
            };
            autoSectionToolTip = new ToolTip();
            autoSectionToolTip.InitialDelay = 0;
            autoSectionToolTip.ReshowDelay = 0;
            autoSectionToolTip.AutoPopDelay = 5000;
            autoSectionToolTip.ShowAlways = true;
            autoSectionToolTip.SetToolTip(
                autoSectionSwitch,
                "Auto Section (A): tu dong tao mat cat ON/OFF");
            status.Controls.Add(autoSectionSwitch);

            btnDictionary = new SafeRoundedButton();
            btnDictionary.Text = "Dict";
            btnDictionary.Font = new Font("Segoe UI", 8.5F, FontStyle.Bold);
            btnDictionary.Location = new Point(720, 2);
            btnDictionary.Size = new System.Drawing.Size(95, 25);
            btnDictionary.Click += delegate { ToggleDictionaryPanel(); };
            status.Controls.Add(btnDictionary);

            btnClear = new SafeRoundedButton();
            btnClear.Text = "Clear Log";
            btnClear.Font = new Font("Segoe UI", 8.5F);
            btnClear.Location = new Point(825, 2);
            btnClear.Size = new System.Drawing.Size(95, 25);
            btnClear.Click += delegate
            {
                _resumeIndex = 0;
                _stopRequested = false;
                _selectedDrawings.Clear();
                dgvDrawings.Rows.Clear();
                lblCount.Text = "Tổng số bản vẽ:  0";
                lblStatus.Text = "✓  Ready    |    0 bản vẽ";
                lblStatus.ForeColor = Color.FromArgb(22, 163, 74);
            };
            status.Controls.Add(btnClear);

            mainFooter = new Panel();
            mainFooter.BackColor = Blue;
            mainFooter.Location = new Point(0, 600);
            mainFooter.Size = new System.Drawing.Size(
                MainBaseWidth + SlideHandleWidth,
                40);
            Controls.Add(mainFooter);

            Label foot = new Label();
            foot.Text = "ⓘ  Optimized for Tekla Structures 2025 SP7                                      ♥  Developed by TTSK VN BIM TEAM                                                    ⌬  富";
            foot.ForeColor = Color.White;
            foot.Font = new Font("Segoe UI", 10F);
            foot.Location = new Point(30, 9);
            foot.Size = new System.Drawing.Size(900, 24);
            mainFooter.Controls.Add(foot);

            BuildSlideToolsPanel();

            UpdateModeUi();
            ApplyTheme();
        }

        // ============================================================
        // PHU SLIDE PANEL - DRAWING TOOLS
        // ============================================================
        private void BuildSlideToolsPanel()
        {
            slideHandle = new RoundedPanel();
            ((RoundedPanel)slideHandle).BorderRadius = 14;
            slideHandle.Location = new Point(MainBaseWidth - 8, 292);
            slideHandle.Size = new System.Drawing.Size(20, 62);
            slideHandle.Cursor = Cursors.Hand;
            slideHandle.Click += delegate { ToggleSlideTools(); };
            Controls.Add(slideHandle);

            slideHandleLabel = new Label();
            slideHandleLabel.Text = "›";
            slideHandleLabel.TextAlign = ContentAlignment.MiddleCenter;
            slideHandleLabel.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
            slideHandleLabel.Dock = DockStyle.Fill;
            slideHandleLabel.Cursor = Cursors.Hand;
            slideHandleLabel.Click += delegate { ToggleSlideTools(); };
            slideHandle.Controls.Add(slideHandleLabel);

            slideToolsPanel = new RoundedPanel();
            slideToolsPanel.Location = new Point(MainBaseWidth + SlidePanelGap, 14);
            slideToolsPanel.Size = new System.Drawing.Size(SlideToolsWidth, MainBaseHeight - 28);
            ((RoundedPanel)slideToolsPanel).BorderRadius = 14;
            slideToolsPanel.Visible = false;
            Controls.Add(slideToolsPanel);

            slideTitleLabel = new Label();
            slideTitleLabel.Text = "DRAWING TOOLS";
            slideTitleLabel.Font = new Font("Segoe UI", 12F, FontStyle.Bold);
            slideTitleLabel.Location = new Point(18, 18);
            slideTitleLabel.Size = new System.Drawing.Size(270, 28);
            slideToolsPanel.Controls.Add(slideTitleLabel);

            Label closeTools = new Label();
            closeTools.Text = "×";
            closeTools.TextAlign = ContentAlignment.MiddleCenter;
            closeTools.Font = new Font("Segoe UI", 15F, FontStyle.Bold);
            closeTools.Location = new Point(SlideToolsWidth - 42, 16);
            closeTools.Size = new System.Drawing.Size(26, 28);
            closeTools.Cursor = Cursors.Hand;
            closeTools.Click += delegate { CloseSlideTools(); };
            slideToolsPanel.Controls.Add(closeTools);

            slideAutoDimTool = MakeSlideToolButton(
                "◎",
                "Auto Dimension",
                "By Selected Part",
                18,
                62);
            WireClickToAll(slideAutoDimTool, delegate
            {
                if (slideAutoDimOpen)
                    CloseAutoDimensionPanel();
                else
                    OpenAutoDimensionPanel();
            });
            slideToolsPanel.Controls.Add(slideAutoDimTool);

            slideDimTool = MakeSlideToolButton(
                "↔",
                "Dimension Spacing",
                "",
                18,
                62);
            WireClickToAll(slideDimTool, delegate
            {
                if (slideDimOpen)
                    CloseDimSpacingPanel();
                else
                    OpenDimSpacingPanel();
            });
            slideToolsPanel.Controls.Add(slideDimTool);

            slideLineTool = MakeSlideToolButton(
                "─",
                "Line Distance",
                "",
                18,
                124);
            WireClickToAll(slideLineTool, delegate
            {
                if (slideLineOpen)
                    CloseLineDistancePanel();
                else
                    OpenLineDistancePanel();
            });
            slideToolsPanel.Controls.Add(slideLineTool);

            slideGridTool = MakeSlideToolButton(
                "⌗",
                "Open Grid View",
                "",
                18,
                186);
            WireClickToAll(slideGridTool, delegate
            {
                if (slideGridOpen)
                    CloseOpenGridPanel();
                else
                    OpenOpenGridPanel();
            });
            slideToolsPanel.Controls.Add(slideGridTool);

            slideArrangeTool = MakeSlideToolButton(
                "◫",
                "Arrange View",
                "",
                18,
                248);
            WireClickToAll(slideArrangeTool, delegate
            {
                if (slideArrangeOpen)
                    CloseArrangeViewPanel();
                else
                    OpenArrangeViewPanel();
            });
            slideToolsPanel.Controls.Add(slideArrangeTool);


            SafeRoundedButton shortcutSettings = new SafeRoundedButton();
            shortcutSettings.Text = "⌨  Shortcut Settings";
            shortcutSettings.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            shortcutSettings.FillColor = Blue;
            shortcutSettings.BorderColor = Blue;
            shortcutSettings.TextColor = Color.White;
            shortcutSettings.BorderRadius = 8;
            shortcutSettings.Location = new Point(18, MainBaseHeight - 92);
            shortcutSettings.Size = new System.Drawing.Size(SlideToolsWidth - 36, 38);
            shortcutSettings.Click += delegate
            {
                OpenShortcutSettingsDialog();
            };
            slideToolsPanel.Controls.Add(shortcutSettings);

            slideDimPanel = new RoundedPanel();
            slideDimPanel.Location = new Point(18, 188);
            slideDimPanel.Size = new System.Drawing.Size(SlideToolsWidth - 36, 250);
            ((RoundedPanel)slideDimPanel).BorderRadius = 12;
            slideDimPanel.Visible = false;
            slideToolsPanel.Controls.Add(slideDimPanel);

            slideLinePanel = new RoundedPanel();
            slideLinePanel.Location = new Point(18, 188);
            slideLinePanel.Size = new System.Drawing.Size(SlideToolsWidth - 36, 190);
            ((RoundedPanel)slideLinePanel).BorderRadius = 12;
            slideLinePanel.Visible = false;
            slideToolsPanel.Controls.Add(slideLinePanel);

            slideGridPanel = new RoundedPanel();
            slideGridPanel.Location = new Point(18, 188);
            slideGridPanel.Size = new System.Drawing.Size(SlideToolsWidth - 36, 155);
            ((RoundedPanel)slideGridPanel).BorderRadius = 12;
            slideGridPanel.Visible = false;
            slideToolsPanel.Controls.Add(slideGridPanel);

            slideMarkOffsetsPanel = new RoundedPanel();
            slideMarkOffsetsPanel.Location = new Point(18, 355);
            slideMarkOffsetsPanel.Size = new System.Drawing.Size(SlideToolsWidth - 36, 226);
            ((RoundedPanel)slideMarkOffsetsPanel).BorderRadius = 12;
            slideMarkOffsetsPanel.Visible = false;
            slideToolsPanel.Controls.Add(slideMarkOffsetsPanel);

            slideArrangePanel = new RoundedPanel();
            slideArrangePanel.Location = new Point(18, 188);
            slideArrangePanel.Size = new System.Drawing.Size(SlideToolsWidth - 36, 295);
            ((RoundedPanel)slideArrangePanel).BorderRadius = 12;
            slideArrangePanel.Visible = false;
            slideToolsPanel.Controls.Add(slideArrangePanel);


            slideAutoDimPanel = new RoundedPanel();
            slideAutoDimPanel.Location = new Point(18, 130);
            slideAutoDimPanel.Size = new System.Drawing.Size(SlideToolsWidth - 36, 410);
            ((RoundedPanel)slideAutoDimPanel).BorderRadius = 12;
            slideAutoDimPanel.Visible = false;
            slideToolsPanel.Controls.Add(slideAutoDimPanel);

            japaneseDictionaryPanel = new JapaneseDictionaryPanel(
                System.IO.Path.Combine(Application.StartupPath, "Data", "JapaneseDictionary.tsv"));
            japaneseDictionaryPanel.Location = new Point(18, 62);
            japaneseDictionaryPanel.Size = new System.Drawing.Size(SlideToolsWidth - 36, MainBaseHeight - 168);
            japaneseDictionaryPanel.Visible = false;
            japaneseDictionaryPanel.StatusChanged += JapaneseDictionaryPanel_StatusChanged;
            slideToolsPanel.Controls.Add(japaneseDictionaryPanel);

            BuildAutoDimensionDetailPanel();
            BuildDimSpacingDetailPanel();
            BuildLineDistanceDetailPanel();
            BuildOpenGridDetailPanel();
            BuildMarkOffsetsDetailPanel();
            BuildArrangeViewDetailPanel();

            slideTimer = new System.Windows.Forms.Timer();
            slideTimer.Interval = 8;
            slideTimer.Tick += SlideTimer_Tick;

            LayoutSlidePanels();
        }

        private Panel MakeSlideToolButton(string icon, string title, string desc, int x, int y)
        {
            RoundedPanel p = new RoundedPanel();
            p.Location = new Point(x, y);
            p.Size = new System.Drawing.Size(SlideToolsWidth - 36, 56);
            p.BorderRadius = 12;
            p.Cursor = Cursors.Hand;

            Label ico = new Label();
            ico.Text = icon;
            ico.TextAlign = ContentAlignment.MiddleCenter;
            ico.Font = new Font("Segoe UI", 16F, FontStyle.Bold);
            ico.Location = new Point(10, 5);
            ico.Size = new System.Drawing.Size(42, 46);
            ico.BackColor = Color.Transparent;
            p.Controls.Add(ico);

            Label t = new Label();
            t.Text = title;
            t.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
            t.Location = new Point(58, 16);
            t.Size = new System.Drawing.Size(SlideToolsWidth - 100, 24);
            t.BackColor = Color.Transparent;
            p.Controls.Add(t);


            return p;
        }


        private void BuildAutoDimensionDetailPanel()
        {
            if (slideAutoDimPanel == null)
                return;

            int innerMargin = 14;
            int innerWidth = slideAutoDimPanel.Width - (innerMargin * 2);
            int gap = 12;
            int titleH = 42;
            int boxW = (innerWidth - gap) / 2;
            int boxH = 86;
            int startY = 52;

            Label title = new Label();
            title.Text = "AUTO DIMENSION";
            title.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
            title.Location = new Point(innerMargin, 12);
            title.Size = new System.Drawing.Size(innerWidth, 24);
            slideAutoDimPanel.Controls.Add(title);

            Label note = new Label();
            note.Text = "Chọn 1 part trong drawing rồi bấm ô chức năng.";
            note.Font = new Font("Segoe UI", 8.5F, FontStyle.Bold);
            note.Location = new Point(innerMargin, 33);
            note.Size = new System.Drawing.Size(innerWidth, 18);
            slideAutoDimPanel.Controls.Add(note);

            Panel slot1 = MakeAutoDimImageSlotBox(
                "Slot01_light.png",
                innerMargin,
                startY,
                boxW,
                boxH,
                delegate { RunSelectedMainPartAutoDim(); });
            slideAutoDimPanel.Controls.Add(slot1);

            Panel slot2 = MakeAutoDimImageSlotBox(
                "Slot02.png",
                innerMargin + boxW + gap,
                startY,
                boxW,
                boxH,
                delegate { RunExternalAutoDimSlot("Tekla.Technology.Akit.UserScript.PHU_AutoDimSlot02"); });
            slideAutoDimPanel.Controls.Add(slot2);

            Panel slot3 = MakeAutoDimImageSlotBox(
                "Slot03_light.png",
                innerMargin,
                startY + boxH + gap,
                boxW,
                boxH,
                delegate { RunExternalAutoDimSlot("Tekla.Technology.Akit.UserScript.PHU_AutoDimSlot03"); });
            slideAutoDimPanel.Controls.Add(slot3);

            Panel slot4 = MakeAutoDimSlot04Box(
                "Slot04_light.png",
                innerMargin + boxW + gap,
                startY + boxH + gap,
                boxW,
                boxH);
            slideAutoDimPanel.Controls.Add(slot4);

            Panel slot5 = MakeAutoDimSlot05Box(
                "Slot05_light.png",
                innerMargin,
                startY + (boxH + gap) * 2,
                boxW,
                boxH);
            slideAutoDimPanel.Controls.Add(slot5);

            Panel slot6 = MakeAutoDimSlotBox(
                "⑥",
                "Slot 06",
                "Chờ gắn file CS",
                innerMargin + boxW + gap,
                startY + (boxH + gap) * 2,
                boxW,
                boxH,
                delegate { RunExternalAutoDimSlot("Tekla.Technology.Akit.UserScript.PHU_AutoDimSlot06"); });
            slideAutoDimPanel.Controls.Add(slot6);

            autoDimResultLabel = new Label();
            autoDimResultLabel.Text = "";
            autoDimResultLabel.Font = new Font("Segoe UI", 8.5F, FontStyle.Bold);
            autoDimResultLabel.TextAlign = ContentAlignment.MiddleCenter;
            autoDimResultLabel.Location = new Point(innerMargin, startY + (boxH + gap) * 3 + 4);
            autoDimResultLabel.Size = new System.Drawing.Size(innerWidth, 24);
            slideAutoDimPanel.Controls.Add(autoDimResultLabel);
        }

        private Panel MakeAutoDimSlotBox(
            string icon,
            string title,
            string desc,
            int x,
            int y,
            int w,
            int h,
            EventHandler clickHandler)
        {
            RoundedPanel p = new RoundedPanel();
            p.Location = new Point(x, y);
            p.Size = new System.Drawing.Size(w, h);
            p.BorderRadius = 12;
            p.Cursor = Cursors.Hand;

            Label ico = new Label();
            ico.Text = icon;
            ico.TextAlign = ContentAlignment.MiddleCenter;
            ico.Font = new Font("Segoe UI", 20F, FontStyle.Bold);
            ico.Location = new Point(0, 8);
            ico.Size = new System.Drawing.Size(w, 30);
            ico.BackColor = Color.Transparent;
            p.Controls.Add(ico);

            Label t = new Label();
            t.Text = title;
            t.TextAlign = ContentAlignment.MiddleCenter;
            t.Font = new Font("Segoe UI", 8.7F, FontStyle.Bold);
            t.Location = new Point(6, 42);
            t.Size = new System.Drawing.Size(w - 12, 20);
            t.BackColor = Color.Transparent;
            p.Controls.Add(t);

            Label d = new Label();
            d.Text = desc;
            d.TextAlign = ContentAlignment.MiddleCenter;
            d.Font = new Font("Segoe UI", 7.6F, FontStyle.Bold);
            d.Location = new Point(6, 62);
            d.Size = new System.Drawing.Size(w - 12, 18);
            d.BackColor = Color.Transparent;
            p.Controls.Add(d);

            WireClickToAll(p, clickHandler);
            return p;
        }

        private Panel MakeAutoDimImageSlotBox(
            string imageFileName,
            int x,
            int y,
            int w,
            int h,
            EventHandler clickHandler)
        {
            RoundedPanel p = new RoundedPanel();
            p.Location = new Point(x, y);
            p.Size = new System.Drawing.Size(w, h);
            p.BorderRadius = 12;
            p.Cursor = Cursors.Hand;

            PictureBox pic = new PictureBox();
            pic.SizeMode = PictureBoxSizeMode.Zoom;
            pic.BackColor = Color.Transparent;
            pic.Cursor = Cursors.Hand;

            int picW = Math.Max(70, w - 28);
            int picH = Math.Max(56, h - 24);
            pic.Size = new System.Drawing.Size(picW, picH);
            pic.Location = new Point((w - pic.Width) / 2, (h - pic.Height) / 2);

            pic.Tag = imageFileName;
            LoadAutoDimSlotPicture(pic, imageFileName);

            p.Controls.Add(pic);
            WireClickToAll(p, clickHandler);
            return p;
        }

        private Panel MakeAutoDimSlot04Box(
            string imageFileName,
            int x,
            int y,
            int w,
            int h)
        {
            RoundedPanel p = new RoundedPanel();
            p.Location = new Point(x, y);
            p.Size = new System.Drawing.Size(w, h);
            p.BorderRadius = 12;
            p.Cursor = Cursors.Hand;

            PictureBox pic = new PictureBox();
            pic.SizeMode = PictureBoxSizeMode.Zoom;
            pic.BackColor = Color.Transparent;
            pic.Cursor = Cursors.Hand;
            pic.Size = new System.Drawing.Size(Math.Max(64, w - 44), Math.Max(38, h - 50));
            pic.Location = new Point((w - pic.Width) / 2, 34);
            pic.Tag = imageFileName;
            LoadAutoDimSlotPicture(pic, imageFileName);
            p.Controls.Add(pic);

            slot04TargetSwitch = new Slot04TargetSwitch();
            slot04TargetSwitch.Location = new Point(w - 70, 10);
            slot04TargetSwitch.Size = new System.Drawing.Size(58, 20);
            slot04TargetSwitch.Visible = true;
            slot04TargetSwitch.CheckedChanged += delegate
            {
                slot04AutoMode = false;
                ApplySlot04ModeUi();
            };
            p.Controls.Add(slot04TargetSwitch);

            btnSlot04Auto = new ThemeButton();
            btnSlot04Auto.Text = "AUTO";
            btnSlot04Auto.Font = new Font("Segoe UI", 6.8F, FontStyle.Bold);
            btnSlot04Auto.Location = new Point(10, 10);
            btnSlot04Auto.Size = new System.Drawing.Size(44, 20);
            btnSlot04Auto.Click += delegate
            {
                slot04AutoMode = true;
                ApplySlot04ModeUi();
            };
            p.Controls.Add(btnSlot04Auto);

            WireClickToAll(pic, delegate { RunSlot04ByCurrentMode(); });
            p.Click += delegate { RunSlot04ByCurrentMode(); };

            ApplySlot04ModeUi();
            return p;
        }

        private Panel MakeAutoDimSlot05Box(
            string imageFileName,
            int x,
            int y,
            int w,
            int h)
        {
            RoundedPanel p = new RoundedPanel();
            p.Location = new Point(x, y);
            p.Size = new System.Drawing.Size(w, h);
            p.BorderRadius = 12;
            p.Cursor = Cursors.Hand;

            PictureBox pic = new PictureBox();
            pic.SizeMode = PictureBoxSizeMode.Zoom;
            pic.BackColor = Color.Transparent;
            pic.Cursor = Cursors.Hand;
            pic.Size = new System.Drawing.Size(Math.Max(94, w - 12), Math.Max(70, h - 12));
            pic.Location = new Point((w - pic.Width) / 2, (h - pic.Height) / 2);
            pic.Tag = imageFileName;
            LoadAutoDimSlotPicture(pic, imageFileName);
            p.Controls.Add(pic);

            slot05ModeSwitch = new Slot05ModeSwitch();
            slot05ModeSwitch.Location = new Point(w - 60, 8);
            slot05ModeSwitch.Size = new System.Drawing.Size(50, 22);
            slot05ModeSwitch.Visible = true;
            slot05ModeSwitch.CheckedChanged += delegate
            {
                ApplySlot05ModeUi();
                RefreshAutoDimSlotImages(slideAutoDimPanel);
            };
            p.Controls.Add(slot05ModeSwitch);
            slot05ModeSwitch.BringToFront();

            WireClickToAll(pic, delegate { RunSlot05ByCurrentMode(); });
            p.Click += delegate { RunSlot05ByCurrentMode(); };

            ApplySlot05ModeUi();
            return p;
        }

        private string ResolveAutoDimSlotImageFileName(string imageFileName)
        {
            if (string.Equals(imageFileName, "Slot01_light.png", StringComparison.OrdinalIgnoreCase))
                return _darkMode ? "Slot01_dark.png" : "Slot01_light.png";

            if (string.Equals(imageFileName, "Slot02.png", StringComparison.OrdinalIgnoreCase))
                return _darkMode ? "Slot02_dark.png" : "Slot02.png";

            if (string.Equals(imageFileName, "Slot03_light.png", StringComparison.OrdinalIgnoreCase))
                return _darkMode ? "Slot03_dark.png" : "Slot03_light.png";

            if (string.Equals(imageFileName, "Slot04_light.png", StringComparison.OrdinalIgnoreCase))
                return _darkMode ? "Slot04_dark.png" : "Slot04_light.png";

            if (string.Equals(imageFileName, "Slot05_light.png", StringComparison.OrdinalIgnoreCase))
            {
                if (slot05ModeSwitch != null && slot05ModeSwitch.SelectedMode == 1)
                    return _darkMode ? "Slot05.2_dark.png" : "Slot05.2_light.png";

                return _darkMode ? "Slot05_dark.png" : "Slot05_light.png";
            }

            return imageFileName;
        }

        private void LoadAutoDimSlotPicture(PictureBox pic, string imageFileName)
        {
            if (pic == null)
                return;

            try
            {
                string resolvedFileName = ResolveAutoDimSlotImageFileName(imageFileName);
                string imagePath = System.IO.Path.Combine(
                    Application.StartupPath,
                    "Resources",
                    resolvedFileName);

                if (System.IO.File.Exists(imagePath))
                {
                    using (Bitmap bitmap = new Bitmap(imagePath))
                    {
                        System.Drawing.Image oldImage = pic.Image;
                        pic.Image = new Bitmap(bitmap);

                        if (oldImage != null)
                            oldImage.Dispose();
                    }
                }
                else
                {
                    System.Drawing.Image oldImage = pic.Image;
                    pic.Image = null;

                    if (oldImage != null)
                        oldImage.Dispose();
                }
            }
            catch
            {
                System.Drawing.Image oldImage = pic.Image;
                pic.Image = null;

                if (oldImage != null)
                    oldImage.Dispose();
            }
        }

        private void RefreshAutoDimSlotImages(Control root)
        {
            if (root == null)
                return;

            PictureBox pic = root as PictureBox;
            if (pic != null && pic.Tag is string)
                LoadAutoDimSlotPicture(pic, pic.Tag as string);

            foreach (Control child in root.Controls)
                RefreshAutoDimSlotImages(child);
        }

        private void ApplySlot04ModeUi()
        {
            Color accent = _darkMode ? Color.FromArgb(224, 156, 96) : BrightBlue;

            if (slot04TargetSwitch != null)
            {
                slot04TargetSwitch.DarkMode = _darkMode;
                slot04TargetSwitch.AccentColor = accent;
                slot04TargetSwitch.Enabled = true;
                slot04TargetSwitch.Invalidate();
            }

            if (btnSlot04Auto != null)
            {
                btnSlot04Auto.UseCustomPaint = true;
                btnSlot04Auto.CustomBackColor = slot04AutoMode
                    ? accent
                    : (_darkMode ? Color.FromArgb(22, 22, 22) : Color.White);
                btnSlot04Auto.CustomBorderColor = accent;
                btnSlot04Auto.CustomTextColor = slot04AutoMode
                    ? (_darkMode ? Color.FromArgb(20, 16, 14) : Color.White)
                    : accent;
                btnSlot04Auto.Invalidate();
            }
        }

        private void RunSlot04ByCurrentMode()
        {
            if (slot04AutoMode || slot04TargetSwitch == null)
            {
                RunExternalAutoDimSlot("Tekla.Technology.Akit.UserScript.PHU_AutoDimSlot04");
                return;
            }

            if (slot04TargetSwitch.SelectedMode == 0)
            {
                RunExternalAutoDimSlot("Tekla.Technology.Akit.UserScript.PHU_AutoDimSlot04_Left");
                return;
            }

            if (slot04TargetSwitch.SelectedMode == 1)
            {
                RunExternalAutoDimSlot("Tekla.Technology.Akit.UserScript.PHU_AutoDimSlot04_Center");
                return;
            }

            RunExternalAutoDimSlot("Tekla.Technology.Akit.UserScript.PHU_AutoDimSlot04_Right");
        }

        private void ApplySlot05ModeUi()
        {
            Color accent = _darkMode ? Color.FromArgb(224, 156, 96) : BrightBlue;

            if (slot05ModeSwitch != null)
            {
                slot05ModeSwitch.DarkMode = _darkMode;
                slot05ModeSwitch.AccentColor = accent;
                slot05ModeSwitch.Enabled = true;
                slot05ModeSwitch.Invalidate();
            }
        }

        private void RunSlot05ByCurrentMode()
        {
            if (slot05ModeSwitch != null && slot05ModeSwitch.SelectedMode == 1)
            {
                RunExternalAutoDimSlot("Tekla.Technology.Akit.UserScript.PHU_AutoDimSlot05_TopBottomMode");
                return;
            }

            RunExternalAutoDimSlot("Tekla.Technology.Akit.UserScript.PHU_AutoDimSlot05");
        }

        private void RunSelectedMainPartAutoDim()
        {
            RunExternalAutoDimSlot("Tekla.Technology.Akit.UserScript.PHU_SelectedMainPartAutoDim");
        }

        private void RunExternalAutoDimSlot(string typeFullName)
        {
            try
            {
                Type t = FindTypeInLoadedAssemblies(typeFullName);
                if (t == null)
                {
                    SetAutoDimResult("Không tìm thấy: " + typeFullName);
                    SetMainStatus(
                        "Chưa gắn file CS cho chức năng này: " + typeFullName,
                        MainStatusKind.Warning);
                    return;
                }

                MethodInfo run = t.GetMethod(
                    "Run",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                    null,
                    Type.EmptyTypes,
                    null);

                if (run == null)
                {
                    SetAutoDimResult("Class thiếu hàm Run().");
                    SetMainStatus(
                        "Auto Dimension: class thiếu hàm public static void Run()",
                        MainStatusKind.Warning);
                    return;
                }

                run.Invoke(null, null);

                PropertyInfo successProperty = t.GetProperty(
                    "LastRunSucceeded",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

                if (successProperty != null && successProperty.PropertyType == typeof(bool))
                {
                    object successValue = successProperty.GetValue(null, null);
                    if (successValue is bool && !(bool)successValue)
                    {
                        SetAutoDimResult("NO DIM CREATED");
                        return;
                    }
                }

                SetAutoDimResult("DONE");
            }
            catch (Exception ex)
            {
                Exception real = ex.InnerException != null ? ex.InnerException : ex;
                SetAutoDimResult("ERROR: " + real.Message);
                SetMainStatus(
                    "Auto Dimension lỗi: " + real.Message,
                    MainStatusKind.Error);
            }
        }

        private Type FindTypeInLoadedAssemblies(string typeFullName)
        {
            if (string.IsNullOrEmpty(typeFullName))
                return null;

            Type t = Type.GetType(typeFullName);
            if (t != null)
                return t;

            System.Reflection.Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();

            foreach (System.Reflection.Assembly asm in assemblies)
            {
                try
                {
                    t = asm.GetType(typeFullName);
                    if (t != null)
                        return t;
                }
                catch
                {
                }
            }

            return null;
        }

        private void SetAutoDimResult(string text)
        {
            if (autoDimResultLabel == null)
                return;

            autoDimResultLabel.Text = text;
            autoDimResultLabel.Visible = true;
        }


        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (_shortcutManager != null)
            {
                Keys normalized = ShortcutManager.NormalizeShortcut(keyData);

                bool isBareTab = (normalized & Keys.KeyCode) == Keys.Tab &&
                    (normalized & Keys.Modifiers) == Keys.None;

                if (isBareTab && !IsShortcutInputFocused())
                {
                    if (!_tabShortcutCandidate)
                    {
                        _tabShortcutCandidate = true;
                        _tabShortcutCancelled = false;
                    }

                    return true;
                }

                if (_tabShortcutCandidate)
                {
                    _tabShortcutCancelled = true;
                    return true;
                }

                if ((normalized & Keys.Alt) == Keys.Alt)
                {
                    _modifierShortcutCandidate = Keys.None;
                    _modifierShortcutCancelled = true;
                    return base.ProcessCmdKey(ref msg, keyData);
                }

                if (ShortcutManager.IsBareModifier(normalized))
                {
                    Keys modifiers = normalized & Keys.Modifiers;
                    if (modifiers != Keys.None && !IsShortcutInputFocused())
                    {
                        _modifierShortcutCandidate = modifiers;
                        _modifierShortcutCancelled = false;
                    }

                    return base.ProcessCmdKey(ref msg, keyData);
                }

                if (_modifierShortcutCandidate != Keys.None)
                    _modifierShortcutCancelled = true;

                if (!ShouldIgnoreShortcutForFocusedControl(normalized))
                {
                    string actionId;
                    if (_shortcutManager.TryFindAction(normalized, out actionId))
                    {
                        RunShortcutAction(actionId);
                        return true;
                    }
                }
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            if (_tabShortcutCandidate && e.KeyCode == Keys.Tab)
            {
                bool cancelled = _tabShortcutCancelled;
                _tabShortcutCandidate = false;
                _tabShortcutCancelled = false;

                if (!cancelled && _shortcutManager != null && !IsShortcutInputFocused())
                {
                    string actionId;
                    if (_shortcutManager.TryFindAction(Keys.Tab, out actionId))
                        RunShortcutAction(actionId);
                }

                e.Handled = true;
                e.SuppressKeyPress = true;
            }

            if (_modifierShortcutCandidate != Keys.None &&
                (ModifierKeys & Keys.Modifiers) == Keys.None)
            {
                Keys candidate = _modifierShortcutCandidate;
                bool cancelled = _modifierShortcutCancelled;
                _modifierShortcutCandidate = Keys.None;
                _modifierShortcutCancelled = false;

                if (!cancelled && _shortcutManager != null && !IsShortcutInputFocused())
                {
                    string actionId;
                    if (_shortcutManager.TryFindModifierOnlyAction(candidate, out actionId))
                        RunShortcutAction(actionId);
                }
            }

            base.OnKeyUp(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            Keys keyCode = e.KeyCode & Keys.KeyCode;

            if (_tabShortcutCandidate)
            {
                if (keyCode != Keys.Tab)
                    _tabShortcutCancelled = true;

                e.Handled = true;
                e.SuppressKeyPress = true;
                base.OnKeyDown(e);
                return;
            }

            if (ShortcutManager.IsBareModifier(keyCode))
            {
                Keys modifiers = e.Modifiers & Keys.Modifiers;
                if ((modifiers & Keys.Alt) == Keys.Alt)
                {
                    _modifierShortcutCandidate = Keys.None;
                    _modifierShortcutCancelled = true;
                }
                else if (modifiers != Keys.None && !IsShortcutInputFocused())
                {
                    _modifierShortcutCandidate = modifiers;
                    _modifierShortcutCancelled = false;
                }
            }
            else if (_modifierShortcutCandidate != Keys.None)
            {
                _modifierShortcutCancelled = true;
            }

            base.OnKeyDown(e);
        }

        private bool IsShortcutInputFocused()
        {
            Control focus = GetDeepActiveControl(this);

            while (focus != null)
            {
                if (focus is TextBoxBase || focus is NumericUpDown || focus is ComboBox)
                    return true;

                focus = focus.Parent;
            }

            return false;
        }

        private bool ShouldIgnoreShortcutForFocusedControl(Keys keyData)
        {
            if (ShortcutManager.HasControlAltShift(keyData))
                return false;

            Control focus = GetDeepActiveControl(this);

            while (focus != null)
            {
                if (focus is TextBoxBase || focus is NumericUpDown)
                    return true;

                ComboBox combo = focus as ComboBox;
                if (combo != null)
                    return true;

                focus = focus.Parent;
            }

            return false;
        }

        private Control GetDeepActiveControl(ContainerControl container)
        {
            if (container == null)
                return null;

            Control control = container.ActiveControl;
            ContainerControl childContainer = control as ContainerControl;

            while (childContainer != null && childContainer.ActiveControl != null)
            {
                control = childContainer.ActiveControl;
                childContainer = control as ContainerControl;
            }

            return control;
        }

        private void RunShortcutAction(string actionId)
        {
            if (string.IsNullOrEmpty(actionId))
                return;

            if (string.Equals(actionId, ShortcutManager.ActionRepeatLast, StringComparison.OrdinalIgnoreCase))
            {
                RunRepeatLastShortcut();
                return;
            }

            if (string.Equals(actionId, ShortcutManager.ActionBatchCreate, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(actionId, ShortcutManager.ActionCheckScale, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(actionId, ShortcutManager.ActionAutoSection, StringComparison.OrdinalIgnoreCase))
            {
                _lastRepeatableShortcutActionId = null;
            }
            else
            {
                _lastRepeatableShortcutActionId = actionId;
            }

            if (string.Equals(actionId, ShortcutManager.ActionCreateDrawing, StringComparison.OrdinalIgnoreCase))
            {
                if (btnRun != null && btnRun.Enabled)
                    btnRun.PerformClick();
                return;
            }

            if (string.Equals(actionId, ShortcutManager.ActionBatchCreate, StringComparison.OrdinalIgnoreCase))
            {
                RunBatchCreateShortcut();
                return;
            }

            if (string.Equals(actionId, ShortcutManager.ActionCheckScale, StringComparison.OrdinalIgnoreCase))
            {
                RunCheckScaleShortcut();
                return;
            }

            if (string.Equals(actionId, ShortcutManager.ActionAutoSection, StringComparison.OrdinalIgnoreCase))
            {
                ToggleAutoSection();
                return;
            }

            if (string.Equals(actionId, ShortcutManager.ActionLineDistance, StringComparison.OrdinalIgnoreCase))
            {
                RunPickTwoPointsLine();
                return;
            }

            if (string.Equals(actionId, ShortcutManager.ActionOpenGrid, StringComparison.OrdinalIgnoreCase))
            {
                RunOpenGridView();
                return;
            }

            if (string.Equals(actionId, ShortcutManager.ActionFitView, StringComparison.OrdinalIgnoreCase))
            {
                RunFitView();
                return;
            }

            if (string.Equals(actionId, ShortcutManager.ActionNeighborGrid, StringComparison.OrdinalIgnoreCase))
            {
                RunNeighborGridMarkOffsets();
                return;
            }

            if (string.Equals(actionId, ShortcutManager.ActionSlot01, StringComparison.OrdinalIgnoreCase))
            {
                RunSelectedMainPartAutoDim();
                return;
            }

            if (string.Equals(actionId, ShortcutManager.ActionSlot02, StringComparison.OrdinalIgnoreCase))
            {
                RunExternalAutoDimSlot("Tekla.Technology.Akit.UserScript.PHU_AutoDimSlot02");
                return;
            }

            if (string.Equals(actionId, ShortcutManager.ActionSlot03, StringComparison.OrdinalIgnoreCase))
            {
                RunExternalAutoDimSlot("Tekla.Technology.Akit.UserScript.PHU_AutoDimSlot03");
                return;
            }

            if (string.Equals(actionId, ShortcutManager.ActionSlot04, StringComparison.OrdinalIgnoreCase))
            {
                RunSlot04ByCurrentMode();
                return;
            }

            if (string.Equals(actionId, ShortcutManager.ActionSlot05, StringComparison.OrdinalIgnoreCase))
            {
                RunSlot05ByCurrentMode();
                return;
            }

            if (string.Equals(actionId, ShortcutManager.ActionSlot06, StringComparison.OrdinalIgnoreCase))
            {
                RunExternalAutoDimSlot("Tekla.Technology.Akit.UserScript.PHU_AutoDimSlot06");
                return;
            }
        }

        private void RunCheckScaleShortcut()
        {
            try
            {
                if (rbBatch != null)
                    rbBatch.Checked = true;

                if (rbActive != null)
                    rbActive.Checked = false;

                UpdateModeUi();

                if (btnLoad != null && btnLoad.Enabled)
                    btnLoad.PerformClick();

                if (btnCheckScale != null && btnCheckScale.Enabled)
                    btnCheckScale.PerformClick();
            }
            catch (Exception ex)
            {
                SetMainStatus(
                    "Shortcut Check Scale lỗi: " + ex.Message,
                    MainStatusKind.Error);
            }
        }

        private void OpenShortcutSettingsDialog()
        {
            try
            {
                if (_shortcutManager == null)
                {
                    _shortcutManager = new ShortcutManager(Application.StartupPath);
                    _shortcutManager.Load();
                }

                using (ShortcutSettingsForm form = new ShortcutSettingsForm(
                    _shortcutManager,
                    _darkMode,
                    _autoSectionEnabled,
                    _isBatchRunning,
                    delegate (bool enabled)
                    {
                        SetAutoSectionEnabled(enabled, true);
                    }))
                {
                    form.ShowDialog(this);
                }
            }
            catch (Exception ex)
            {
                SetMainStatus(
                    "Shortcut Settings lỗi: " + ex.Message,
                    MainStatusKind.Error);
            }
        }


        private void BuildDimSpacingDetailPanel()
        {
            int innerMargin = 16;
            int innerWidth = slideDimPanel.Width - (innerMargin * 2);

            Label title = new Label();
            title.Text = "DIMENSION SPACING";
            title.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
            title.Location = new Point(innerMargin, 12);
            title.Size = new System.Drawing.Size(innerWidth, 24);
            slideDimPanel.Controls.Add(title);

            Label lbSpacing = new Label();
            lbSpacing.Text = "Khoảng cách (mm)";
            lbSpacing.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            lbSpacing.Location = new Point(innerMargin, 54);
            lbSpacing.Size = new System.Drawing.Size(innerWidth, 22);
            slideDimPanel.Controls.Add(lbSpacing);

            nudDimSpacing = new BorderNumericUpDown();
            nudDimSpacing.DecimalPlaces = 1;
            //Giá trị nhỏ nhất của tầng dim
            nudDimSpacing.Minimum = 50;
            nudDimSpacing.Maximum = 2000;
            //Giá trị mặt định khi mở form
            nudDimSpacing.Value = 50;
            //Bước nhảy của tầng Dim
            nudDimSpacing.Increment = 50;
            nudDimSpacing.Location = new Point(innerMargin, 79);
            nudDimSpacing.Size = new System.Drawing.Size(innerWidth, 28);
            slideDimPanel.Controls.Add(nudDimSpacing);

            Label lbScope = new Label();
            lbScope.Text = "Áp dụng cho";
            lbScope.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            lbScope.Location = new Point(innerMargin, 120);
            lbScope.Size = new System.Drawing.Size(innerWidth, 22);
            slideDimPanel.Controls.Add(lbScope);

            cboDimScope = new BorderComboBox();
            cboDimScope.DropDownStyle = ComboBoxStyle.DropDownList;
            cboDimScope.Items.Add("Toàn bộ bản vẽ");
            cboDimScope.Items.Add("Front");
            cboDimScope.Items.Add("Top");
            cboDimScope.Items.Add("Bottom");
            cboDimScope.SelectedIndex = 0;
            cboDimScope.Location = new Point(innerMargin, 145);
            cboDimScope.Size = new System.Drawing.Size(innerWidth, 28);
            slideDimPanel.Controls.Add(cboDimScope);

            SafeRoundedButton apply = new SafeRoundedButton();
            apply.Text = "✓  APPLY";
            apply.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            apply.Location = new Point(innerMargin, 195);
            apply.Size = new System.Drawing.Size(innerWidth, 38);
            apply.Click += delegate { RunDimSpacing(true); };
            slideDimPanel.Controls.Add(apply);

            dimResultLabel = new Label();
            dimResultLabel.Visible = false;
            slideDimPanel.Controls.Add(dimResultLabel);
        }

        private void BuildLineDistanceDetailPanel()
        {
            int innerMargin = 16;
            int innerWidth = slideLinePanel.Width - (innerMargin * 2);

            Label title = new Label();
            title.Text = "LINE DISTANCE";
            title.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
            title.Location = new Point(innerMargin, 14);
            title.Size = new System.Drawing.Size(innerWidth - 46, 24);
            slideLinePanel.Controls.Add(title);

            Button pickTwoPoints = new ThemeButton();
            pickTwoPoints.Text = "";
            pickTwoPoints.AccessibleName = "Pick two points to draw line";
            pickTwoPoints.Cursor = Cursors.Hand;
            pickTwoPoints.Location = new Point(innerMargin + innerWidth - 36, 8);
            pickTwoPoints.Size = new System.Drawing.Size(36, 36);
            pickTwoPoints.Paint += DrawPickTwoPointsIcon;
            pickTwoPoints.Click += delegate { RunPickTwoPointsLine(); };
            slideLinePanel.Controls.Add(pickTwoPoints);

            Label lbDistance = new Label();
            lbDistance.Text = "Chiều dài line (mm)";
            lbDistance.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            lbDistance.Location = new Point(innerMargin, 54);
            lbDistance.Size = new System.Drawing.Size(innerWidth, 22);
            slideLinePanel.Controls.Add(lbDistance);

            nudLineDistance = new BorderNumericUpDown();
            nudLineDistance.DecimalPlaces = 1;
            nudLineDistance.Minimum = 1;
            nudLineDistance.Maximum = 100000;
            nudLineDistance.Value = 100;
            nudLineDistance.Increment = 10;
            nudLineDistance.Location = new Point(innerMargin, 79);
            nudLineDistance.Size = new System.Drawing.Size(innerWidth, 28);
            slideLinePanel.Controls.Add(nudLineDistance);

            SafeRoundedButton apply = new SafeRoundedButton();
            apply.Text = "✓  DRAW LINE";
            apply.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            apply.Location = new Point(innerMargin, 128);
            apply.Size = new System.Drawing.Size(innerWidth, 38);
            apply.Click += delegate { RunLineDistance(); };
            slideLinePanel.Controls.Add(apply);

            lineResultLabel = new Label();
            lineResultLabel.Visible = false;
            slideLinePanel.Controls.Add(lineResultLabel);
        }

        private void BuildOpenGridDetailPanel()
        {
            int innerMargin = 16;
            int innerWidth = slideGridPanel.Width - (innerMargin * 2);

            Label title = new Label();
            title.Text = "OPEN GRID VIEW";
            title.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
            title.Location = new Point(innerMargin, 14);
            title.Size = new System.Drawing.Size(innerWidth, 24);
            slideGridPanel.Controls.Add(title);

            Label note = new Label();
            note.Text = "Mở khung view chạm trục trên / dưới / trái / phải";
            note.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            note.Location = new Point(innerMargin, 52);
            note.Size = new System.Drawing.Size(innerWidth, 34);
            slideGridPanel.Controls.Add(note);

            int buttonGap = 10;
            int buttonWidth = (innerWidth - buttonGap) / 2;

            SafeRoundedButton apply = new SafeRoundedButton();
            apply.Text = "✓  OPEN GRID";
            apply.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            apply.Location = new Point(innerMargin, 96);
            apply.Size = new System.Drawing.Size(buttonWidth, 38);
            apply.Click += delegate { RunOpenGridView(); };
            slideGridPanel.Controls.Add(apply);

            SafeRoundedButton fit = new SafeRoundedButton();
            fit.Text = "✓  FIT VIEW";
            fit.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            fit.Location = new Point(innerMargin + buttonWidth + buttonGap, 96);
            fit.Size = new System.Drawing.Size(innerWidth - buttonWidth - buttonGap, 38);
            fit.Click += delegate { RunFitView(); };
            slideGridPanel.Controls.Add(fit);

            gridResultLabel = new Label();
            gridResultLabel.Visible = false;
            slideGridPanel.Controls.Add(gridResultLabel);
        }

        private void BuildMarkOffsetsDetailPanel()
        {
            if (slideMarkOffsetsPanel == null)
                return;

            int outerMargin = 14;
            int innerWidth = slideMarkOffsetsPanel.Width - (outerMargin * 2);

            Label markTitle = new Label();
            markTitle.Text = "MARK OFFSETS";
            markTitle.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
            markTitle.Location = new Point(outerMargin, 16);
            markTitle.Size = new System.Drawing.Size(innerWidth, 24);
            slideMarkOffsetsPanel.Controls.Add(markTitle);

            RoundedPanel offsetBox = new RoundedPanel();
            offsetBox.Location = new Point(outerMargin, 58);
            offsetBox.Size = new System.Drawing.Size(innerWidth, 150);
            offsetBox.BorderRadius = 10;
            slideMarkOffsetsPanel.Controls.Add(offsetBox);

            int boxMargin = 16;
            int labelW = 116;
            int inputW = 62;
            int inputGap = 12;
            int xInput = boxMargin + labelW;
            int yInput = xInput + inputW + inputGap;
            int headerY = 22;
            int inputY = 54;
            int createY = 104;

            Label xTitle = new Label();
            xTitle.Text = "X";
            xTitle.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            xTitle.TextAlign = ContentAlignment.MiddleCenter;
            xTitle.Location = new Point(xInput, headerY);
            xTitle.Size = new System.Drawing.Size(inputW, 20);
            offsetBox.Controls.Add(xTitle);

            Label yTitle = new Label();
            yTitle.Text = "Y";
            yTitle.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            yTitle.TextAlign = ContentAlignment.MiddleCenter;
            yTitle.Location = new Point(yInput, headerY);
            yTitle.Size = new System.Drawing.Size(inputW, 20);
            offsetBox.Controls.Add(yTitle);

            Label neighborLabel = new Label();
            neighborLabel.Text = "Neighboring grids";
            neighborLabel.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            neighborLabel.TextAlign = ContentAlignment.MiddleLeft;
            neighborLabel.Location = new Point(boxMargin, inputY + 1);
            neighborLabel.Size = new System.Drawing.Size(labelW - 4, 26);
            offsetBox.Controls.Add(neighborLabel);

            nudNeighborGridX = new BorderNumericUpDown();
            nudNeighborGridX.DecimalPlaces = 3;
            nudNeighborGridX.Minimum = -100000;
            nudNeighborGridX.Maximum = 100000;
            nudNeighborGridX.Value = 30;
            nudNeighborGridX.Increment = 1;
            nudNeighborGridX.TextAlign = HorizontalAlignment.Center;
            nudNeighborGridX.Location = new Point(xInput, inputY);
            nudNeighborGridX.Size = new System.Drawing.Size(inputW, 28);
            offsetBox.Controls.Add(nudNeighborGridX);

            nudNeighborGridY = new BorderNumericUpDown();
            nudNeighborGridY.DecimalPlaces = 3;
            nudNeighborGridY.Minimum = -100000;
            nudNeighborGridY.Maximum = 100000;
            nudNeighborGridY.Value = 0;
            nudNeighborGridY.Increment = 1;
            nudNeighborGridY.TextAlign = HorizontalAlignment.Center;
            nudNeighborGridY.Location = new Point(yInput, inputY);
            nudNeighborGridY.Size = new System.Drawing.Size(inputW, 28);
            offsetBox.Controls.Add(nudNeighborGridY);

            SafeRoundedButton createNeighbor = new SafeRoundedButton();
            createNeighbor.Text = "+  CREATE";
            createNeighbor.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            createNeighbor.Location = new Point(boxMargin, createY);
            createNeighbor.Size = new System.Drawing.Size(offsetBox.Width - (boxMargin * 2), 34);
            createNeighbor.Click += delegate { RunNeighborGridMarkOffsets(); };
            offsetBox.Controls.Add(createNeighbor);
        }

        private void BuildArrangeViewDetailPanel()
        {
            int innerMargin = 16;
            int innerWidth = slideArrangePanel.Width - (innerMargin * 2);

            Label title = new Label();
            title.Text = "ARRANGE VIEW";
            title.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
            title.Location = new Point(innerMargin, 14);
            title.Size = new System.Drawing.Size(innerWidth - 86, 24);
            slideArrangePanel.Controls.Add(title);

            arrangeVerticalOrderBox = new Panel();
            arrangeVerticalOrderBox.Location = new Point(innerMargin + innerWidth - 72, 8);
            arrangeVerticalOrderBox.Size = new System.Drawing.Size(72, 34);
            arrangeVerticalOrderBox.BackColor = Color.Transparent;
            arrangeVerticalOrderBox.Visible = false;
            slideArrangePanel.Controls.Add(arrangeVerticalOrderBox);

            arrangeVerticalOrderSwitch = new ArrangeOrderSwitch();
            arrangeVerticalOrderSwitch.Location = new Point(4, 3);
            arrangeVerticalOrderSwitch.Size = new System.Drawing.Size(64, 28);
            arrangeVerticalOrderSwitch.CheckedChanged += delegate
            {
                arrangeVerticalBottomUp = arrangeVerticalOrderSwitch.Checked;
            };
            arrangeVerticalOrderBox.Controls.Add(arrangeVerticalOrderSwitch);

            Label lbSection = new Label();
            lbSection.Text = "Section View";
            lbSection.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            lbSection.Location = new Point(innerMargin, 54);
            lbSection.Size = new System.Drawing.Size(innerWidth, 22);
            slideArrangePanel.Controls.Add(lbSection);

            int boxW = 130;
            int boxH = 84;
            int boxGap = innerWidth - (boxW * 2);
            int boxY = 84;

            arrangeSectionHorizontalBox = MakeArrangeOptionBox("▭▭▭", "●", innerMargin, boxY, true);
            arrangeSectionVerticalBox = MakeArrangeOptionBox("▯▯\n▯▯", "○", innerMargin + boxW + boxGap, boxY, false);
            arrangeSectionHorizontalBox.Size = new System.Drawing.Size(boxW, boxH);
            arrangeSectionVerticalBox.Size = new System.Drawing.Size(boxW, boxH);

            WireClickToAll(arrangeSectionHorizontalBox, delegate
            {
                arrangeSectionHorizontal = true;
                arrangeVerticalBottomUp = false;
                if (arrangeVerticalOrderSwitch != null) arrangeVerticalOrderSwitch.Checked = false;
                ApplyArrangeOptionStyles();
            });
            WireClickToAll(arrangeSectionVerticalBox, delegate
            {
                arrangeSectionHorizontal = false;
                ApplyArrangeOptionStyles();
            });
            slideArrangePanel.Controls.Add(arrangeSectionHorizontalBox);
            slideArrangePanel.Controls.Add(arrangeSectionVerticalBox);

            Label lbGap = new Label();
            lbGap.Text = "Gap View";
            lbGap.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            lbGap.Location = new Point(innerMargin, 195);
            lbGap.Size = new System.Drawing.Size(110, 22);
            slideArrangePanel.Controls.Add(lbGap);

            nudArrangeGap = new BorderNumericUpDown();
            nudArrangeGap.DecimalPlaces = 1;
            nudArrangeGap.Minimum = 0;
            nudArrangeGap.Maximum = 500;
            nudArrangeGap.Value = 30;
            nudArrangeGap.Increment = 5;
            nudArrangeGap.Location = new Point(innerMargin + 140, 192);
            nudArrangeGap.Size = new System.Drawing.Size(innerWidth - 140, 28);
            slideArrangePanel.Controls.Add(nudArrangeGap);

            SafeRoundedButton apply = new SafeRoundedButton();
            apply.Text = "✓  ARRANGE";
            apply.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            apply.Location = new Point(innerMargin, 238);
            apply.Size = new System.Drawing.Size(innerWidth, 38);
            apply.Click += delegate { RunArrangeView(); };
            slideArrangePanel.Controls.Add(apply);

            arrangeResultLabel = new Label();
            arrangeResultLabel.Visible = false;
            slideArrangePanel.Controls.Add(arrangeResultLabel);
        }

        private Panel MakeArrangeOptionBox(string icon, string radio, int x, int y, bool selected)
        {
            RoundedPanel p = new RoundedPanel();
            p.Location = new Point(x, y);
            p.Size = new System.Drawing.Size(130, 78);
            p.BorderRadius = 10;
            p.Cursor = Cursors.Hand;

            Label ico = new Label();
            ico.Text = icon;
            ico.TextAlign = ContentAlignment.MiddleCenter;
            ico.Font = new Font("Segoe UI", 16F, FontStyle.Bold);
            ico.Location = new Point(0, 0);
            ico.Size = new System.Drawing.Size(130, 78);
            ico.BackColor = Color.Transparent;
            p.Controls.Add(ico);

            return p;
        }

        private void ApplyArrangeOptionStyles()
        {
            StyleArrangeOption(arrangeSectionHorizontalBox, arrangeSectionHorizontal);
            StyleArrangeOption(arrangeSectionVerticalBox, !arrangeSectionHorizontal);

            bool showVerticalSwitch = !arrangeSectionHorizontal;

            if (arrangeVerticalOrderBox != null)
            {
                arrangeVerticalOrderBox.Visible = showVerticalSwitch;
                arrangeVerticalOrderBox.Enabled = showVerticalSwitch;
                arrangeVerticalOrderBox.BringToFront();

                RoundedPanel orderPanel = arrangeVerticalOrderBox as RoundedPanel;
                if (orderPanel != null)
                {
                    orderPanel.BackColor = _darkMode ? Color.FromArgb(18, 18, 18) : Color.White;
                    orderPanel.BorderColor = _darkMode ? Color.FromArgb(201, 122, 64) : BrightBlue;
                }
            }

            if (arrangeVerticalOrderSwitch != null)
            {
                arrangeVerticalOrderSwitch.Visible = true;
                arrangeVerticalOrderSwitch.Enabled = showVerticalSwitch;
                arrangeVerticalOrderSwitch.DarkMode = _darkMode;
                arrangeVerticalOrderSwitch.AccentColor = _darkMode ? Color.FromArgb(224, 156, 96) : BrightBlue;
                arrangeVerticalOrderSwitch.BackPanelColor = _darkMode ? Color.FromArgb(18, 18, 18) : Color.White;

                if (!showVerticalSwitch && arrangeVerticalOrderSwitch.Checked)
                    arrangeVerticalOrderSwitch.Checked = false;

                arrangeVerticalOrderSwitch.Invalidate();
            }

            if (arrangeVerticalOrderIcon != null)
                arrangeVerticalOrderIcon.Visible = false;
        }

        private void StyleArrangeOption(Panel panel, bool selected)
        {
            if (panel == null)
                return;

            Color accent = _darkMode ? Color.FromArgb(224, 156, 96) : BrightBlue;
            Color border = selected
                ? accent
                : (_darkMode ? Color.FromArgb(73, 56, 43) : PanelBorder);
            Color back = selected
                ? (_darkMode ? Color.FromArgb(30, 24, 20) : Color.FromArgb(239, 246, 255))
                : (_darkMode ? Color.FromArgb(18, 18, 18) : Color.White);
            Color muted = _darkMode ? Color.FromArgb(92, 82, 72) : Color.FromArgb(148, 163, 184);

            panel.BackColor = back;
            RoundedPanel rp = panel as RoundedPanel;
            if (rp != null)
                rp.BorderColor = border;

            foreach (Control c in panel.Controls)
            {
                Label l = c as Label;
                if (l == null)
                    continue;

                if (l.Text == "●" || l.Text == "○")
                {
                    l.Text = selected ? "●" : "○";
                    l.ForeColor = selected ? accent : muted;
                }
                else
                {
                    l.ForeColor = selected ? accent : (_darkMode ? Color.FromArgb(160, 135, 112) : Color.FromArgb(100, 116, 139));
                }
            }

            panel.Invalidate();
        }

        private void ToggleSlideTools()
        {
            if (slideToolsOpen)
                CloseSlideTools();
            else
                OpenSlideTools();
        }

        private string AutoSectionSettingFile
        {
            get
            {
                return System.IO.Path.Combine(
                    Application.StartupPath,
                    "auto_section.cfg");
            }
        }

        private void LoadAutoSectionSetting()
        {
            // Chế độ cắt B/C luôn bắt đầu ở OFF sau mỗi lần mở chương trình.
            // Trong phiên hiện tại người dùng vẫn có thể bật/tắt và Batch vẫn chụp trạng thái như cũ.
            _autoSectionEnabled = DEFAULT_AUTO_SECTION_ENABLED;
            SaveAutoSectionSetting();
        }

        private void SaveAutoSectionSetting()
        {
            try
            {
                System.IO.File.WriteAllText(
                    AutoSectionSettingFile,
                    _autoSectionEnabled ? "ON" : "OFF");
            }
            catch
            {
            }
        }

        private void SetAutoSectionEnabled(bool enabled, bool persist)
        {
            if (_isBatchRunning)
            {
                ApplyAutoSectionSwitchUi();
                return;
            }

            _autoSectionEnabled = enabled;

            if (persist)
                SaveAutoSectionSetting();

            ApplyAutoSectionSwitchUi();
        }

        private void ToggleAutoSection()
        {
            if (_isBatchRunning)
            {
                SetMainStatus(
                    "Auto Section dang bi khoa trong khi Batch chay.",
                    MainStatusKind.Warning);
                return;
            }

            SetAutoSectionEnabled(!_autoSectionEnabled, true);
            SetMainStatus(
                "Auto Section: " + (_autoSectionEnabled ? "ON" : "OFF"),
                _autoSectionEnabled ? MainStatusKind.Success : MainStatusKind.Warning);
        }

        private void ApplyAutoSectionSwitchUi()
        {
            if (autoSectionSwitch == null)
                return;

            _syncingAutoSectionSwitch = true;
            try
            {
                autoSectionSwitch.DarkMode = _darkMode;
                autoSectionSwitch.AccentColor = _darkMode
                    ? Color.FromArgb(224, 126, 35)
                    : Color.FromArgb(37, 99, 235);
                autoSectionSwitch.Checked = _autoSectionEnabled;
                autoSectionSwitch.Enabled = !_isBatchRunning;
                autoSectionSwitch.Invalidate();
            }
            finally
            {
                _syncingAutoSectionSwitch = false;
            }
        }

        private void ToggleDictionaryPanel()
        {
            if (slideToolsOpen && slideDictionaryOpen)
                CloseSlideTools();
            else
                OpenDictionaryPanel();
        }

        private void OpenDictionaryPanel()
        {
            slideToolsOpen = true;
            slideDimOpen = false;
            slideLineOpen = false;
            slideGridOpen = false;
            slideArrangeOpen = false;
            slideAutoDimOpen = false;
            slideDictionaryOpen = true;

            if (slideToolsPanel != null)
                slideToolsPanel.Visible = true;

            if (japaneseDictionaryPanel != null)
            {
                japaneseDictionaryPanel.Visible = true;
                japaneseDictionaryPanel.ReloadEntries();
            }

            LayoutSlidePanels();
        }

        private void JapaneseDictionaryPanel_StatusChanged(
            object sender,
            JapaneseDictionaryStatusEventArgs e)
        {
            MainStatusKind kind;

            switch (e.Kind)
            {
                case JapaneseDictionaryStatusKind.Success:
                    kind = MainStatusKind.Success;
                    break;

                case JapaneseDictionaryStatusKind.Warning:
                    kind = MainStatusKind.Warning;
                    break;

                case JapaneseDictionaryStatusKind.Error:
                    kind = MainStatusKind.Error;
                    break;

                default:
                    kind = MainStatusKind.Information;
                    break;
            }

            SetMainStatus(e.Message, kind);
        }

        private void OpenSlideTools()
        {
            slideToolsOpen = true;
            slideDimOpen = false;
            slideLineOpen = false;
            slideGridOpen = false;
            slideArrangeOpen = false;
            slideAutoDimOpen = false;
            slideDictionaryOpen = false;

            if (slideToolsPanel != null)
                slideToolsPanel.Visible = true;

            if (slideDimPanel != null)
                slideDimPanel.Visible = false;

            if (slideLinePanel != null)
                slideLinePanel.Visible = false;

            if (slideGridPanel != null)
                slideGridPanel.Visible = false;

            if (slideMarkOffsetsPanel != null)
                slideMarkOffsetsPanel.Visible = false;

            if (slideArrangePanel != null)
                slideArrangePanel.Visible = false;

            if (slideAutoDimPanel != null)
                slideAutoDimPanel.Visible = false;

            LayoutSlidePanels();
        }

        private void CloseSlideTools()
        {
            slideToolsOpen = false;
            slideDimOpen = false;
            slideLineOpen = false;
            slideGridOpen = false;
            slideArrangeOpen = false;
            slideAutoDimOpen = false;
            slideDictionaryOpen = false;

            // Không ẩn panel ngay để còn thấy hiệu ứng trượt vào.
            LayoutSlidePanels();
        }

        private void OpenDimSpacingPanel()
        {
            slideToolsOpen = true;
            slideDimOpen = true;
            slideLineOpen = false;
            slideGridOpen = false;
            slideArrangeOpen = false;
            slideAutoDimOpen = false;
            slideDictionaryOpen = false;

            if (slideToolsPanel != null)
                slideToolsPanel.Visible = true;

            if (slideDimPanel != null)
                slideDimPanel.Visible = true;

            if (slideLinePanel != null)
                slideLinePanel.Visible = false;

            if (slideGridPanel != null)
                slideGridPanel.Visible = false;

            if (slideMarkOffsetsPanel != null)
                slideMarkOffsetsPanel.Visible = false;

            if (slideArrangePanel != null)
                slideArrangePanel.Visible = false;

            if (slideAutoDimPanel != null)
                slideAutoDimPanel.Visible = false;

            LayoutSlidePanels();
        }

        private void CloseDimSpacingPanel()
        {
            slideDimOpen = false;

            // Không ẩn panel ngay để còn thấy hiệu ứng trượt vào.
            LayoutSlidePanels();
        }

        private void OpenLineDistancePanel()
        {
            slideToolsOpen = true;
            slideDimOpen = false;
            slideLineOpen = true;
            slideGridOpen = false;
            slideArrangeOpen = false;
            slideAutoDimOpen = false;
            slideDictionaryOpen = false;

            if (slideToolsPanel != null)
                slideToolsPanel.Visible = true;

            if (slideDimPanel != null)
                slideDimPanel.Visible = false;

            if (slideLinePanel != null)
                slideLinePanel.Visible = true;

            if (slideGridPanel != null)
                slideGridPanel.Visible = false;

            if (slideMarkOffsetsPanel != null)
                slideMarkOffsetsPanel.Visible = false;

            if (slideArrangePanel != null)
                slideArrangePanel.Visible = false;

            if (slideAutoDimPanel != null)
                slideAutoDimPanel.Visible = false;

            LayoutSlidePanels();
        }

        private void CloseLineDistancePanel()
        {
            slideLineOpen = false;

            LayoutSlidePanels();
        }

        private void OpenOpenGridPanel()
        {
            slideToolsOpen = true;
            slideDimOpen = false;
            slideLineOpen = false;
            slideGridOpen = true;
            slideArrangeOpen = false;
            slideAutoDimOpen = false;
            slideDictionaryOpen = false;

            if (slideToolsPanel != null)
                slideToolsPanel.Visible = true;

            if (slideDimPanel != null)
                slideDimPanel.Visible = false;

            if (slideLinePanel != null)
                slideLinePanel.Visible = false;

            if (slideGridPanel != null)
                slideGridPanel.Visible = true;

            if (slideMarkOffsetsPanel != null)
                slideMarkOffsetsPanel.Visible = true;

            if (slideArrangePanel != null)
                slideArrangePanel.Visible = false;

            if (slideAutoDimPanel != null)
                slideAutoDimPanel.Visible = false;

            LayoutSlidePanels();
        }

        private void CloseOpenGridPanel()
        {
            slideGridOpen = false;

            LayoutSlidePanels();
        }

        private void OpenArrangeViewPanel()
        {
            slideToolsOpen = true;
            slideDimOpen = false;
            slideLineOpen = false;
            slideGridOpen = false;
            slideArrangeOpen = true;
            slideAutoDimOpen = false;
            slideDictionaryOpen = false;

            if (slideToolsPanel != null)
                slideToolsPanel.Visible = true;

            if (slideDimPanel != null)
                slideDimPanel.Visible = false;

            if (slideLinePanel != null)
                slideLinePanel.Visible = false;

            if (slideGridPanel != null)
                slideGridPanel.Visible = false;

            if (slideMarkOffsetsPanel != null)
                slideMarkOffsetsPanel.Visible = false;

            if (slideArrangePanel != null)
                slideArrangePanel.Visible = true;

            if (slideAutoDimPanel != null)
                slideAutoDimPanel.Visible = false;

            LayoutSlidePanels();
        }

        private void CloseArrangeViewPanel()
        {
            slideArrangeOpen = false;

            LayoutSlidePanels();
        }


        private void OpenAutoDimensionPanel()
        {
            slideToolsOpen = true;
            slideDimOpen = false;
            slideLineOpen = false;
            slideGridOpen = false;
            slideArrangeOpen = false;
            slideAutoDimOpen = true;
            slideDictionaryOpen = false;

            if (slideToolsPanel != null)
                slideToolsPanel.Visible = true;

            if (slideDimPanel != null)
                slideDimPanel.Visible = false;

            if (slideLinePanel != null)
                slideLinePanel.Visible = false;

            if (slideGridPanel != null)
                slideGridPanel.Visible = false;

            if (slideMarkOffsetsPanel != null)
                slideMarkOffsetsPanel.Visible = false;

            if (slideArrangePanel != null)
                slideArrangePanel.Visible = false;

            if (slideAutoDimPanel != null)
                slideAutoDimPanel.Visible = true;

            LayoutSlidePanels();
        }

        private void CloseAutoDimensionPanel()
        {
            slideAutoDimOpen = false;

            LayoutSlidePanels();
        }


        private int GetSlideTargetWidth()
        {
            int width = MainBaseWidth + SlideHandleWidth;

            if (slideToolsOpen)
                width = MainBaseWidth + SlidePanelGap + SlideToolsWidth + SlideRightMargin;

            return width;
        }

        private void ApplyMainFooterWidth()
        {
            if (mainFooter == null)
                return;

            mainFooter.Width = slideToolsOpen
                ? MainBaseWidth
                : MainBaseWidth + SlideHandleWidth;
        }

        private void LayoutSlidePanels()
        {
            slideTargetWidth = GetSlideTargetWidth();

            if (slideToolsOpen)
                ApplyMainFooterWidth();

            if (slideHandle != null)
            {
                slideHandle.Location = new Point(MainBaseWidth - 8, 292);
                slideHandle.Size = new System.Drawing.Size(20, 62);
                slideHandle.BringToFront();
            }

            if (slideHandleLabel != null)
                slideHandleLabel.Text = slideToolsOpen ? "‹" : "›";

            if (slideToolsPanel != null)
            {
                slideToolsPanel.Location = new Point(MainBaseWidth + SlidePanelGap, 14);
                slideToolsPanel.BringToFront();
            }

            LayoutDrawingToolOrder();

            ApplySlideTheme();
            StartSlideAnimation();
        }


        private void LayoutDrawingToolOrder()
        {
            const int x = 18;
            const int firstY = 62;
            const int gap = 12;
            const int toolH = 56;

            int y = firstY;

            // Khi một module đang mở, chỉ giữ lại module đó và ẩn các module còn lại.
            // Nhìn đồng bộ hơn, đồng thời panel chi tiết có nhiều khoảng trống hơn.
            bool anyToolOpen = slideDimOpen || slideLineOpen || slideGridOpen || slideArrangeOpen || slideAutoDimOpen || slideDictionaryOpen;
            if (slideAutoDimTool != null) slideAutoDimTool.Visible = !anyToolOpen || slideAutoDimOpen;
            if (slideDimTool != null) slideDimTool.Visible = !anyToolOpen || slideDimOpen;
            if (slideLineTool != null) slideLineTool.Visible = !anyToolOpen || slideLineOpen;
            if (slideGridTool != null) slideGridTool.Visible = !anyToolOpen || slideGridOpen;
            if (slideArrangeTool != null) slideArrangeTool.Visible = !anyToolOpen || slideArrangeOpen;

            if (slideTitleLabel != null)
                slideTitleLabel.Text = slideDictionaryOpen
                    ? "📖  Chữ Nhật hay dùng"
                    : "DRAWING TOOLS";

            if (slideAutoDimOpen)
            {
                if (slideAutoDimTool != null) slideAutoDimTool.Location = new Point(x, y);
                y += toolH + gap;

                if (slideAutoDimPanel != null) slideAutoDimPanel.Location = new Point(x, y);
            }
            else if (slideArrangeOpen)
            {
                if (slideArrangeTool != null) slideArrangeTool.Location = new Point(x, y);
                y += toolH + gap;

                if (slideArrangePanel != null) slideArrangePanel.Location = new Point(x, y);
            }
            else if (slideLineOpen)
            {
                if (slideLineTool != null) slideLineTool.Location = new Point(x, y);
                y += toolH + gap;

                if (slideLinePanel != null) slideLinePanel.Location = new Point(x, y);
            }
            else if (slideDimOpen)
            {
                if (slideDimTool != null) slideDimTool.Location = new Point(x, y);
                y += toolH + gap;

                if (slideDimPanel != null) slideDimPanel.Location = new Point(x, y);
            }
            else if (slideGridOpen)
            {
                if (slideGridTool != null) slideGridTool.Location = new Point(x, y);
                y += toolH + gap;

                if (slideGridPanel != null) slideGridPanel.Location = new Point(x, y);
                y += 155 + gap;

                if (slideMarkOffsetsPanel != null) slideMarkOffsetsPanel.Location = new Point(x, y);
            }
            else
            {
                if (slideAutoDimTool != null) slideAutoDimTool.Location = new Point(x, y);
                y += toolH + gap;

                if (slideArrangeTool != null) slideArrangeTool.Location = new Point(x, y);
                y += toolH + gap;

                if (slideLineTool != null) slideLineTool.Location = new Point(x, y);
                y += toolH + gap;

                if (slideDimTool != null) slideDimTool.Location = new Point(x, y);
                y += toolH + gap;

                if (slideGridTool != null) slideGridTool.Location = new Point(x, y);

                if (slideAutoDimPanel != null) slideAutoDimPanel.Location = new Point(x, firstY + toolH + gap);
                if (slideArrangePanel != null) slideArrangePanel.Location = new Point(x, firstY + toolH + gap);
                if (slideLinePanel != null) slideLinePanel.Location = new Point(x, firstY + toolH + gap);
                if (slideDimPanel != null) slideDimPanel.Location = new Point(x, firstY + toolH + gap);
                if (slideGridPanel != null) slideGridPanel.Location = new Point(x, firstY + toolH + gap);
                if (slideMarkOffsetsPanel != null) slideMarkOffsetsPanel.Location = new Point(x, firstY + toolH + gap + 155 + gap);
            }

            if (slideAutoDimTool != null) slideAutoDimTool.BringToFront();
            if (slideArrangeTool != null) slideArrangeTool.BringToFront();
            if (slideLineTool != null) slideLineTool.BringToFront();
            if (slideDimTool != null) slideDimTool.BringToFront();
            if (slideGridTool != null) slideGridTool.BringToFront();

            if (slideAutoDimOpen && slideAutoDimPanel != null) slideAutoDimPanel.BringToFront();
            if (slideArrangeOpen && slideArrangePanel != null) slideArrangePanel.BringToFront();
            if (slideLineOpen && slideLinePanel != null) slideLinePanel.BringToFront();
            if (slideDimOpen && slideDimPanel != null) slideDimPanel.BringToFront();
            if (slideGridOpen && slideGridPanel != null) slideGridPanel.BringToFront();
            if (slideGridOpen && slideMarkOffsetsPanel != null) slideMarkOffsetsPanel.BringToFront();
            if (slideDictionaryOpen && japaneseDictionaryPanel != null) japaneseDictionaryPanel.BringToFront();
        }

        private void StartSlideAnimation()
        {
            if (slideTimer == null)
            {
                ClientSize = new System.Drawing.Size(slideTargetWidth, MainBaseHeight);
                FinishSlideAnimation();
                return;
            }

            if (ClientSize.Width == slideTargetWidth)
            {
                FinishSlideAnimation();
                return;
            }

            slideTimer.Stop();
            slideTimer.Start();
        }

        private void SlideTimer_Tick(object sender, EventArgs e)
        {
            int current = ClientSize.Width;

            if (current == slideTargetWidth)
            {
                slideTimer.Stop();
                FinishSlideAnimation();
                return;
            }

            int next;

            if (current < slideTargetWidth)
                next = Math.Min(current + SlideAnimationStep, slideTargetWidth);
            else
                next = Math.Max(current - SlideAnimationStep, slideTargetWidth);

            ClientSize = new System.Drawing.Size(next, MainBaseHeight);

            if (next == slideTargetWidth)
            {
                slideTimer.Stop();
                FinishSlideAnimation();
            }
        }

        private void FinishSlideAnimation()
        {
            if (slideToolsPanel != null)
                slideToolsPanel.Visible = slideToolsOpen;

            if (slideDimPanel != null)
                slideDimPanel.Visible = slideToolsOpen && slideDimOpen;

            if (slideLinePanel != null)
                slideLinePanel.Visible = slideToolsOpen && slideLineOpen;

            if (slideGridPanel != null)
                slideGridPanel.Visible = slideToolsOpen && slideGridOpen;

            if (slideMarkOffsetsPanel != null)
                slideMarkOffsetsPanel.Visible = slideToolsOpen && slideGridOpen;

            if (slideArrangePanel != null)
                slideArrangePanel.Visible = slideToolsOpen && slideArrangeOpen;

            if (slideAutoDimPanel != null)
                slideAutoDimPanel.Visible = slideToolsOpen && slideAutoDimOpen;

            if (japaneseDictionaryPanel != null)
                japaneseDictionaryPanel.Visible = slideToolsOpen && slideDictionaryOpen;

            ApplyMainFooterWidth();

            if (slideHandle != null)
                slideHandle.BringToFront();
        }

        private void RunDimSpacing(bool apply)
        {
            try
            {
                double spacing = Convert.ToDouble(nudDimSpacing.Value);
                string scope = cboDimScope.Text;

                PHU_DimSpacingNormalize.Result result =
                    PHU_DimSpacingNormalize.Run(spacing, scope, apply);

                if (dimResultLabel != null)
                {
                    dimResultLabel.Text = result == null
                        ? "Không có kết quả."
                        : result.ToDisplayText(apply);

                    dimResultLabel.ForeColor = result != null && result.FailedCount > 0
                        ? Color.FromArgb(220, 38, 38)
                        : Color.FromArgb(22, 163, 74);
                }

                lblStatus.Text = apply
                    ? "✓  Dimension spacing applied"
                    : "✓  Dimension spacing preview";
                lblStatus.ForeColor = Color.FromArgb(22, 163, 74);
            }
            catch (Exception ex)
            {
                if (dimResultLabel != null)
                {
                    dimResultLabel.Text = ex.Message;
                    dimResultLabel.ForeColor = Color.FromArgb(220, 38, 38);
                }

                lblStatus.Text = "✗  Dimension spacing lỗi";
                lblStatus.ForeColor = Color.Firebrick;
            }
        }

        private void RunLineDistance()
        {
            try
            {
                double distance = Convert.ToDouble(nudLineDistance.Value);

                PHU_LineDistance.Result result = PHU_LineDistance.Run(distance);

                if (lineResultLabel != null)
                {
                    lineResultLabel.Text = result == null
                        ? "Không có kết quả."
                        : result.ToDisplayText();

                    lineResultLabel.ForeColor = result != null && result.Success
                        ? Color.FromArgb(22, 163, 74)
                        : Color.FromArgb(220, 38, 38);
                }

                lblStatus.Text = result != null && result.Success
                    ? "✓  Line distance created"
                    : "✗  Line distance lỗi";

                lblStatus.ForeColor = result != null && result.Success
                    ? Color.FromArgb(22, 163, 74)
                    : Color.Firebrick;
            }
            catch (Exception ex)
            {
                if (lineResultLabel != null)
                {
                    lineResultLabel.Text = ex.Message;
                    lineResultLabel.ForeColor = Color.FromArgb(220, 38, 38);
                }

                SetMainStatus(
                    "Line distance lỗi: " + ex.Message,
                    MainStatusKind.Error);
            }
        }

        private void RunPickTwoPointsLine()
        {
            try
            {
                TTSK_AutoDim_Plates.PHU_LineDistance.Result result =
                    TTSK_AutoDim_Plates.PHU_LineDistance.RunPickTwoPointsLine();

                bool success = result != null && result.Success;
                string message = result == null
                    ? "Không có kết quả."
                    : result.ToDisplayText();

                SetMainStatus(
                    message,
                    success ? MainStatusKind.Success : MainStatusKind.Error);
            }
            catch (Exception ex)
            {
                SetMainStatus(ex.Message, MainStatusKind.Error);
            }
        }

        private void RunRepeatLastShortcut()
        {
            if (string.IsNullOrEmpty(_lastRepeatableShortcutActionId))
            {
                SetMainStatus("Chưa có lệnh phù hợp để lặp lại.", MainStatusKind.Warning);
                return;
            }

            RunShortcutAction(_lastRepeatableShortcutActionId);
        }

        private void RunBatchCreateShortcut()
        {
            try
            {
                if (rbBatch != null)
                    rbBatch.Checked = true;

                if (rbActive != null)
                    rbActive.Checked = false;

                UpdateModeUi();

                if (btnLoad != null && btnLoad.Enabled)
                    btnLoad.PerformClick();

                if (_selectedDrawings.Count > 0 && btnRun != null && btnRun.Enabled)
                    btnRun.PerformClick();
            }
            catch (Exception ex)
            {
                SetMainStatus(
                    "Shortcut Batch Create lỗi: " + ex.Message,
                    MainStatusKind.Error);
            }
        }

        private enum MainStatusKind
        {
            Success,
            Information,
            Warning,
            Error
        }

        private void SetMainStatus(string message, MainStatusKind kind)
        {
            if (lblStatus == null)
                return;

            string text = string.IsNullOrWhiteSpace(message)
                ? "Không có nội dung thông báo."
                : message.Replace("\r", " ").Replace("\n", " ");

            switch (kind)
            {
                case MainStatusKind.Success:
                    lblStatus.Text = "✓  " + text;
                    lblStatus.ForeColor = Color.FromArgb(22, 163, 74);
                    break;

                case MainStatusKind.Information:
                    lblStatus.Text = "ℹ  " + text;
                    lblStatus.ForeColor = Blue;
                    break;

                case MainStatusKind.Warning:
                    lblStatus.Text = "⚠  " + text;
                    lblStatus.ForeColor = Color.DarkOrange;
                    break;

                default:
                    lblStatus.Text = "✗  " + text;
                    lblStatus.ForeColor = Color.Firebrick;
                    break;
            }
        }

        private void DrawPickTwoPointsIcon(object sender, PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            Color background = _darkMode
                ? Color.FromArgb(24, 24, 24)
                : Color.White;
            Color accent = _darkMode
                ? Color.FromArgb(201, 122, 64)
                : Blue;

            e.Graphics.Clear(background);

            RectangleF buttonBorder = new RectangleF(
                1.5F,
                1.5F,
                32.5F,
                32.5F);

            using (GraphicsPath borderPath = RoundedRectF(buttonBorder, 3.5F))
            using (Pen borderPen = new Pen(accent, 1.5F))
            {
                e.Graphics.DrawPath(borderPen, borderPath);
            }

            Point[] cursorPoints =
            {
                new Point(11, 8),
                new Point(11, 25),
                new Point(16, 20),
                new Point(20, 28),
                new Point(24, 26),
                new Point(20, 18),
                new Point(27, 18)
            };

            using (Pen outline = new Pen(accent, 1.8F))
            {
                e.Graphics.DrawPolygon(outline, cursorPoints);
            }
        }

        private void RunOpenGridView()
        {
            try
            {
                string selectionError;
                if (!PHU_OpenGridView.PrepareTargetViewSelectionForMacro(out selectionError))
                {
                    if (gridResultLabel != null)
                    {
                        gridResultLabel.Text = selectionError;
                        gridResultLabel.ForeColor = Color.FromArgb(220, 38, 38);
                    }

                    lblStatus.Text = "✗  Không chọn được view";
                    lblStatus.ForeColor = Color.Firebrick;
                    return;
                }

                string macroError;
                if (!TryRunGridVisibilityMacro("OPEN", out macroError))
                {
                    if (gridResultLabel != null)
                    {
                        gridResultLabel.Text = macroError;
                        gridResultLabel.ForeColor = Color.FromArgb(220, 38, 38);
                    }

                    lblStatus.Text = "✗  Không bật được grid";
                    lblStatus.ForeColor = Color.Firebrick;
                    return;
                }

                PHU_OpenGridView.Result result = PHU_OpenGridView.Run();

                if (gridResultLabel != null)
                {
                    gridResultLabel.Text = result == null ? "Không có kết quả." : result.ToDisplayText();
                    gridResultLabel.ForeColor = result != null && result.FailedCount > 0
                        ? Color.FromArgb(220, 38, 38)
                        : Color.FromArgb(22, 163, 74);
                }

                lblStatus.Text = "✓  Open grid view applied";
                lblStatus.ForeColor = Color.FromArgb(22, 163, 74);
            }
            catch (Exception ex)
            {
                if (gridResultLabel != null)
                {
                    gridResultLabel.Text = ex.Message;
                    gridResultLabel.ForeColor = Color.FromArgb(220, 38, 38);
                }

                lblStatus.Text = "✗  Open grid view lỗi";
                lblStatus.ForeColor = Color.Firebrick;
            }
        }


        private void RunFitView()
        {
            try
            {
                string selectionError;
                if (!PHU_OpenGridView.PrepareTargetViewSelectionForMacro(out selectionError))
                {
                    if (gridResultLabel != null)
                    {
                        gridResultLabel.Text = selectionError;
                        gridResultLabel.ForeColor = Color.FromArgb(220, 38, 38);
                    }

                    lblStatus.Text = "✗  Không chọn được view";
                    lblStatus.ForeColor = Color.Firebrick;
                    return;
                }

                string macroError;
                if (!TryRunGridVisibilityMacro("FIT", out macroError))
                {
                    if (gridResultLabel != null)
                    {
                        gridResultLabel.Text = macroError;
                        gridResultLabel.ForeColor = Color.FromArgb(220, 38, 38);
                    }

                    lblStatus.Text = "✗  Không tắt được grid";
                    lblStatus.ForeColor = Color.Firebrick;
                    return;
                }

                PHU_OpenGridView.Result result = PHU_OpenGridView.RunFitPadding20();

                string fitCompleteMacroError;
                if (!TryRunGridVisibilityMacro("FIT_COMPLETE", out fitCompleteMacroError))
                {
                    if (gridResultLabel != null)
                    {
                        gridResultLabel.Text = "Fit đã chạy nhưng chưa đặt được Collect By = 4: " + fitCompleteMacroError;
                        gridResultLabel.ForeColor = Color.FromArgb(220, 38, 38);
                    }

                    lblStatus.Text = "✗  Fit xong nhưng chưa hoàn tất Collect By";
                    lblStatus.ForeColor = Color.Firebrick;
                    return;
                }

                if (gridResultLabel != null)
                {
                    gridResultLabel.Text = result == null ? "Không có kết quả." : result.ToDisplayText();
                    gridResultLabel.ForeColor = result != null && result.FailedCount > 0
                        ? Color.FromArgb(220, 38, 38)
                        : Color.FromArgb(22, 163, 74);
                }

                lblStatus.Text = result != null && result.FailedCount > 0
                    ? "✗  Fit view lỗi"
                    : "✓  Fit view applied";

                lblStatus.ForeColor = result != null && result.FailedCount > 0
                    ? Color.Firebrick
                    : Color.FromArgb(22, 163, 74);
            }
            catch (Exception ex)
            {
                if (gridResultLabel != null)
                {
                    gridResultLabel.Text = ex.Message;
                    gridResultLabel.ForeColor = Color.FromArgb(220, 38, 38);
                }

                lblStatus.Text = "✗  Fit view lỗi";
                lblStatus.ForeColor = Color.Firebrick;
            }
        }

        private bool TryRunGridVisibilityMacro(string command, out string error)
        {
            error = string.Empty;

            if (_gridVisibilityMacroRunning)
            {
                error = "Macro Grid Visibility đang chạy.";
                return false;
            }

            if (!string.Equals(command, "OPEN", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(command, "FIT", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(command, "FIT_COMPLETE", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(command, "MARK_OFFSET", StringComparison.OrdinalIgnoreCase))
            {
                error = "Lệnh Grid Visibility không hợp lệ.";
                return false;
            }

            string macroPath = ResolveGridVisibilityMacroPath();
            if (string.IsNullOrEmpty(macroPath))
            {
                error = "Không tìm thấy macro " + GridVisibilityMacroFileName + ".";
                return false;
            }

            string commandPath = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                GridVisibilityCommandFileName);

            _gridVisibilityMacroRunning = true;

            try
            {
                System.IO.File.WriteAllText(commandPath, command);

                bool started = Tekla.Structures.Model.Operations.Operation.RunMacro(macroPath);
                if (!started)
                {
                    error = "Tekla không khởi chạy được macro Grid Visibility.";
                    return false;
                }

                const int timeoutMilliseconds = 5000;
                const int pollMilliseconds = 50;
                int elapsedMilliseconds = 0;

                while (elapsedMilliseconds < timeoutMilliseconds)
                {
                    Thread.Sleep(pollMilliseconds);
                    elapsedMilliseconds += pollMilliseconds;
                    Application.DoEvents();

                    string response;
                    try
                    {
                        response = System.IO.File.ReadAllText(commandPath).Trim();
                    }
                    catch
                    {
                        continue;
                    }

                    if (string.Equals(response, "DONE", StringComparison.OrdinalIgnoreCase))
                        return true;

                    if (response.StartsWith("ERROR|", StringComparison.OrdinalIgnoreCase))
                    {
                        error = response.Substring("ERROR|".Length).Trim();
                        if (string.IsNullOrEmpty(error))
                            error = "Macro Grid Visibility báo lỗi.";

                        return false;
                    }
                }

                error = "Macro Grid Visibility không hoàn tất trong 5 giây.";
                return false;
            }
            catch (Exception ex)
            {
                Exception real = ex.InnerException ?? ex;
                error = "Grid Visibility " + real.GetType().Name + ": " + real.Message;
                return false;
            }
            finally
            {
                _gridVisibilityMacroRunning = false;

                try
                {
                    if (System.IO.File.Exists(commandPath))
                        System.IO.File.Delete(commandPath);
                }
                catch
                {
                }
            }
        }

        private static string ResolveGridVisibilityMacroPath()
        {
            string currentDirectory = Application.StartupPath;

            for (int level = 0; level <= 5 && !string.IsNullOrEmpty(currentDirectory); level++)
            {
                string directPath = System.IO.Path.Combine(
                    currentDirectory,
                    GridVisibilityMacroFileName);

                if (System.IO.File.Exists(directPath))
                    return directPath;

                string drawingsPath = System.IO.Path.Combine(
                    System.IO.Path.Combine(
                        System.IO.Path.Combine(currentDirectory, "macros"),
                        "drawings"),
                    GridVisibilityMacroFileName);

                if (System.IO.File.Exists(drawingsPath))
                    return drawingsPath;

                try
                {
                    System.IO.DirectoryInfo parent =
                        System.IO.Directory.GetParent(currentDirectory);

                    currentDirectory = parent == null ? null : parent.FullName;
                }
                catch
                {
                    break;
                }
            }

            return null;
        }


        private void RunNeighborGridMarkOffsets()
        {
            try
            {
                string selectionError;
                if (!PHU_OpenGridView.PrepareTargetViewSelectionForMacro(out selectionError))
                {
                    if (gridResultLabel != null)
                    {
                        gridResultLabel.Text = selectionError;
                        gridResultLabel.ForeColor = Color.FromArgb(220, 38, 38);
                    }

                    lblStatus.Text = "✗  Không chọn được view";
                    lblStatus.ForeColor = Color.Firebrick;
                    return;
                }

                string macroError;
                if (!TryRunGridVisibilityMacro("MARK_OFFSET", out macroError))
                {
                    if (gridResultLabel != null)
                    {
                        gridResultLabel.Text = macroError;
                        gridResultLabel.ForeColor = Color.FromArgb(220, 38, 38);
                    }

                    lblStatus.Text = "✗  Không chuẩn bị được grid";
                    lblStatus.ForeColor = Color.Firebrick;
                    return;
                }

                double xOffset = nudNeighborGridX == null ? 30.0 : Convert.ToDouble(nudNeighborGridX.Value);
                double yOffset = nudNeighborGridY == null ? 0.0 : Convert.ToDouble(nudNeighborGridY.Value);

                PHU_OpenGridView.Result result = null;

                Type openGridType = typeof(PHU_OpenGridView);

                MethodInfo run2 = openGridType.GetMethod(
                    "RunNeighborGrid",
                    new Type[] { typeof(double), typeof(double) });

                if (run2 != null)
                {
                    result = (PHU_OpenGridView.Result)run2.Invoke(
                        null,
                        new object[] { xOffset, yOffset });
                }
                else
                {
                    MethodInfo run1 = openGridType.GetMethod(
                        "RunNeighborGrid",
                        new Type[] { typeof(double) });

                    if (run1 != null)
                    {
                        result = (PHU_OpenGridView.Result)run1.Invoke(
                            null,
                            new object[] { xOffset });
                    }
                    else
                    {
                        MethodInfo runDefault = openGridType.GetMethod(
                            "RunNeighborGrid30",
                            Type.EmptyTypes);

                        if (runDefault != null)
                            result = (PHU_OpenGridView.Result)runDefault.Invoke(null, null);
                    }
                }

                if (gridResultLabel != null)
                {
                    gridResultLabel.Text = result == null ? "Không có kết quả." : result.ToDisplayText();
                    gridResultLabel.ForeColor = result != null && result.FailedCount > 0
                        ? Color.FromArgb(220, 38, 38)
                        : Color.FromArgb(22, 163, 74);
                }

                lblStatus.Text = result != null && result.FailedCount > 0
                    ? "✗  Neighbor grid lỗi"
                    : "✓  Neighbor grid created";

                lblStatus.ForeColor = result != null && result.FailedCount > 0
                    ? Color.Firebrick
                    : Color.FromArgb(22, 163, 74);
            }
            catch (Exception ex)
            {
                if (gridResultLabel != null)
                {
                    gridResultLabel.Text = ex.Message;
                    gridResultLabel.ForeColor = Color.FromArgb(220, 38, 38);
                }

                lblStatus.Text = "✗  Neighbor grid lỗi";
                lblStatus.ForeColor = Color.Firebrick;
            }
        }

        private PHU_ArrangeView.Result RunArrangeViewWithVerticalOrder(bool sectionHorizontal, double gap, bool verticalBottomUp)
        {
            try
            {
                if (!sectionHorizontal)
                {
                    Type arrangeType = typeof(PHU_ArrangeView);

                    MethodInfo run3 = arrangeType.GetMethod(
                        "Run",
                        new Type[] { typeof(bool), typeof(double), typeof(bool) });

                    if (run3 != null)
                        return (PHU_ArrangeView.Result)run3.Invoke(
                            null,
                            new object[] { sectionHorizontal, gap, verticalBottomUp });

                    MethodInfo run4 = arrangeType.GetMethod(
                        "Run",
                        new Type[] { typeof(bool), typeof(bool), typeof(double), typeof(bool) });

                    if (run4 != null)
                        return (PHU_ArrangeView.Result)run4.Invoke(
                            null,
                            new object[] { false, sectionHorizontal, gap, verticalBottomUp });
                }
            }
            catch
            {
            }

            return PHU_ArrangeView.Run(sectionHorizontal, gap);
        }

        private void RunArrangeView()
        {
            try
            {
                double gap = Convert.ToDouble(nudArrangeGap.Value);

                PHU_ArrangeView.Result result = RunArrangeViewWithVerticalOrder(
                    arrangeSectionHorizontal,
                    gap,
                    arrangeVerticalBottomUp);

                if (arrangeResultLabel != null)
                {
                    arrangeResultLabel.Text = result == null ? "Không có kết quả." : result.ToDisplayText();
                    arrangeResultLabel.ForeColor = result != null && result.Success
                        ? Color.FromArgb(22, 163, 74)
                        : Color.FromArgb(220, 38, 38);
                }

                lblStatus.Text = result != null && result.Success
                    ? "✓  Arrange view applied"
                    : "✗  Arrange view lỗi";

                lblStatus.ForeColor = result != null && result.Success
                    ? Color.FromArgb(22, 163, 74)
                    : Color.Firebrick;
            }
            catch (Exception ex)
            {
                if (arrangeResultLabel != null)
                {
                    arrangeResultLabel.Text = ex.Message;
                    arrangeResultLabel.ForeColor = Color.FromArgb(220, 38, 38);
                }

                lblStatus.Text = "✗  Arrange view lỗi";
                lblStatus.ForeColor = Color.Firebrick;
            }
        }

        private void ApplySlideTheme()
        {
            if (slideHandle == null)
                return;

            Color accent = _darkMode ? Color.FromArgb(201, 122, 64) : Blue;
            Color accentText = _darkMode ? Color.FromArgb(20, 16, 14) : Color.White;
            Color panelBg = _darkMode ? Color.FromArgb(18, 18, 18) : Color.White;
            Color panelBg2 = _darkMode ? Color.FromArgb(15, 15, 15) : Color.White;
            Color text = _darkMode ? Color.FromArgb(226, 232, 240) : Color.FromArgb(15, 23, 42);
            Color muted = _darkMode ? Color.FromArgb(148, 163, 184) : Color.FromArgb(100, 116, 139);
            Color border = _darkMode ? Color.FromArgb(73, 56, 43) : PanelBorder;

            slideHandle.BackColor = _darkMode
                ? Color.FromArgb(30, 30, 30)
                : Color.FromArgb(230, 237, 250);

            RoundedPanel handlePanel = slideHandle as RoundedPanel;
            if (handlePanel != null)
                handlePanel.BorderColor = _darkMode ? Color.FromArgb(73, 56, 43) : PanelBorder;

            slideHandleLabel.ForeColor = _darkMode
                ? Color.FromArgb(210, 170, 120)
                : Blue;

            StyleSlidePanelRecursive(slideToolsPanel, panelBg, panelBg2, text, muted, border, accent);
            StyleSlidePanelRecursive(slideDimPanel, panelBg, panelBg2, text, muted, border, accent);
            StyleSlidePanelRecursive(slideLinePanel, panelBg, panelBg2, text, muted, border, accent);
            StyleSlidePanelRecursive(slideGridPanel, panelBg, panelBg2, text, muted, border, accent);
            StyleSlidePanelRecursive(slideMarkOffsetsPanel, panelBg, panelBg2, text, muted, border, accent);
            StyleSlidePanelRecursive(slideArrangePanel, panelBg, panelBg2, text, muted, border, accent);
            StyleSlidePanelRecursive(slideAutoDimPanel, panelBg, panelBg2, text, muted, border, accent);
            if (japaneseDictionaryPanel != null)
                japaneseDictionaryPanel.ApplyTheme(_darkMode);
            ApplyArrangeOptionStyles();
        }

        private void StyleSlidePanelRecursive(Control root, Color panelBg, Color panelBg2, Color text, Color muted, Color border, Color accent)
        {
            if (root == null)
                return;

            RoundedPanel rp = root as RoundedPanel;
            if (rp != null)
            {
                rp.BackColor = panelBg;
                rp.BorderColor = border;
            }
            else if (root is Panel)
            {
                root.BackColor = panelBg;
            }
            else if (root is Label)
            {
                Label l = root as Label;
                l.BackColor = Color.Transparent;
                if (l.Font != null && l.Font.Bold)
                    l.ForeColor = accent;
                else
                    l.ForeColor = muted;
            }
            else if (root is ComboBox)
            {
                ComboBox cb = root as ComboBox;
                cb.BackColor = panelBg2;
                cb.ForeColor = _darkMode ? Color.White : text;
                cb.FlatStyle = FlatStyle.Flat;

                BorderComboBox bcb = cb as BorderComboBox;
                if (bcb != null)
                {
                    bcb.CustomBorderColor = _darkMode
                        ? Color.FromArgb(94, 70, 50)
                        : Color.FromArgb(203, 213, 225);

                    bcb.ButtonBackColor = panelBg2;

                    bcb.ButtonBorderColor = _darkMode
                        ? Color.FromArgb(94, 70, 50)
                        : Color.FromArgb(203, 213, 225);

                    bcb.ArrowColor = _darkMode
                        ? Color.FromArgb(224, 156, 96)
                        : Color.FromArgb(30, 58, 138);

                    bcb.RefreshCustomButton();
                }
            }
            else if (root is NumericUpDown || root is TextBox)
            {
                root.BackColor = panelBg2;
                root.ForeColor = text;

                BorderNumericUpDown bnud = root as BorderNumericUpDown;
                if (bnud != null)
                {
                    bnud.CustomBorderColor = _darkMode
                        ? Color.FromArgb(94, 70, 50)
                        : Color.FromArgb(203, 213, 225);

                    bnud.ButtonBackColor = panelBg2;

                    bnud.ButtonBorderColor = _darkMode
                        ? Color.FromArgb(94, 70, 50)
                        : Color.FromArgb(203, 213, 225);

                    bnud.ArrowColor = _darkMode
                        ? Color.FromArgb(224, 156, 96)
                        : Color.FromArgb(30, 58, 138);

                    bnud.RefreshCustomButton();
                }
            }
            else if (root is SafeRoundedButton)
            {
                SafeRoundedButton button = root as SafeRoundedButton;
                button.FillColor = accent;
                button.BorderColor = accent;
                button.TextColor = _darkMode ? Color.FromArgb(20, 16, 14) : Color.White;
                button.Invalidate();
            }
            else if (root is Button)
            {
                Button b = root as Button;
                b.BackColor = accent;
                b.ForeColor = _darkMode ? Color.FromArgb(20, 16, 14) : Color.White;
                b.FlatStyle = FlatStyle.Flat;
                b.FlatAppearance.BorderSize = 0;
            }

            foreach (Control c in root.Controls)
                StyleSlidePanelRecursive(c, panelBg, panelBg2, text, muted, border, accent);
        }

        private Panel MakePanel(int x, int y, int w, int h)
        {
            RoundedPanel p = new RoundedPanel();
            p.Location = new Point(x, y);
            p.Size = new System.Drawing.Size(w, h);
            p.BackColor = Color.White;
            p.BorderColor = PanelBorder;
            p.BorderRadius = 10;
            return p;
        }

        private Panel MakeOptionBox(int x, int y, int w, int h, bool selected)
        {
            RoundedPanel p = new RoundedPanel();
            p.Location = new Point(x, y);
            p.Size = new System.Drawing.Size(w, h);
            p.BackColor = selected ? Color.FromArgb(246, 250, 255) : Color.White;
            p.BorderColor = selected ? BrightBlue : PanelBorder;
            p.BorderRadius = 10;
            return p;
        }

        private Label SectionTitle(string text, int x, int y)
        {
            Label l = new Label();
            l.Text = text;
            l.ForeColor = BrightBlue;
            l.Font = new Font("Segoe UI", 12F, FontStyle.Bold);
            l.Location = new Point(x, y);
            l.Size = new System.Drawing.Size(270, 30);
            return l;
        }

        private Label MakeCircleIcon(string text, int x, int y)
        {
            Label l = new Label();
            l.Text = text;
            l.TextAlign = ContentAlignment.MiddleCenter;
            l.Font = new Font("Segoe UI", 20F, FontStyle.Bold);
            l.ForeColor = Blue;
            l.BackColor = Color.FromArgb(235, 242, 255);
            l.Location = new Point(x, y);
            l.Size = new System.Drawing.Size(54, 54);
            return l;
        }

        private Panel MakeOptionText(string title, string desc, int x, int y)
        {
            Panel p = new Panel();
            p.Location = new Point(x, y);
            p.Size = new System.Drawing.Size(145, 92);
            p.BackColor = Color.Transparent;

            Label t = new Label();
            t.Text = title;
            t.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            t.ForeColor = Color.FromArgb(15, 23, 42);
            t.Location = new Point(0, 0);
            t.Size = new System.Drawing.Size(145, 42);
            p.Controls.Add(t);

            Label d = new Label();
            d.Text = desc;
            d.Font = new Font("Segoe UI", 8.8F);
            d.ForeColor = Color.FromArgb(51, 65, 85);
            d.Location = new Point(0, 42);
            d.Size = new System.Drawing.Size(145, 50);
            p.Controls.Add(d);

            return p;
        }

        private void UpdateModeUi()
        {
            if (btnLoad != null)
                btnLoad.Enabled = !_isBatchRunning && rbBatch != null && rbBatch.Checked;

            if (btnModeActive != null && btnModeBatch != null)
            {
                ApplyModeCardStyle(btnModeActive, rbActive != null && rbActive.Checked);
                ApplyModeCardStyle(btnModeBatch, rbBatch != null && rbBatch.Checked);
            }

            ApplyAutoSectionSwitchUi();
        }

        private Panel MakeModeButton(string title, string desc, int x, int y, int w, int h)
        {
            RoundedPanel p = new RoundedPanel();
            p.Location = new Point(x, y);
            p.Size = new System.Drawing.Size(w, h);
            p.BackColor = Color.White;
            p.BorderColor = PanelBorder;
            p.BorderRadius = 8;
            p.Cursor = Cursors.Hand;

            Label titleLabel = new Label();
            titleLabel.Text = title;
            titleLabel.TextAlign = ContentAlignment.MiddleCenter;
            titleLabel.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
            titleLabel.ForeColor = Color.FromArgb(15, 23, 42);
            titleLabel.Location = new Point(0, 5);
            titleLabel.Size = new System.Drawing.Size(w, 22);
            titleLabel.BackColor = Color.Transparent;
            p.Controls.Add(titleLabel);

            Label descLabel = new Label();
            descLabel.Text = desc;
            descLabel.TextAlign = ContentAlignment.MiddleCenter;
            descLabel.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            descLabel.ForeColor = Color.FromArgb(15, 23, 42);
            descLabel.Location = new Point(0, 25);
            descLabel.Size = new System.Drawing.Size(w, 20);
            descLabel.BackColor = Color.Transparent;
            p.Controls.Add(descLabel);

            return p;
        }

        private void ApplyModeCardStyle(Panel panel, bool selected)
        {
            if (panel == null)
                return;

            RoundedPanel rp = panel as RoundedPanel;

            if (_darkMode)
            {
                if (selected)
                {
                    panel.BackColor = Color.FromArgb(32, 31, 30);

                    if (rp != null)
                        rp.BorderColor = Color.FromArgb(201, 122, 64);

                    SetModeCardTextColor(panel, Color.FromArgb(224, 156, 96));
                }
                else
                {
                    panel.BackColor = Color.FromArgb(22, 22, 22);

                    if (rp != null)
                        rp.BorderColor = Color.FromArgb(60, 52, 44);

                    SetModeCardTextColor(panel, Color.FromArgb(226, 232, 240));
                }
            }
            else
            {
                if (selected)
                {
                    panel.BackColor = Color.FromArgb(239, 246, 255);

                    if (rp != null)
                        rp.BorderColor = BrightBlue;

                    SetModeCardTextColor(panel, Blue);
                }
                else
                {
                    panel.BackColor = Color.White;

                    if (rp != null)
                        rp.BorderColor = PanelBorder;

                    SetModeCardTextColor(panel, Color.FromArgb(15, 23, 42));
                }
            }

            panel.Invalidate();
        }

        private void SetModeCardTextColor(Control root, Color color)
        {
            foreach (Control child in root.Controls)
            {
                if (child is Label)
                    child.ForeColor = color;

                if (child.Controls.Count > 0)
                    SetModeCardTextColor(child, color);
            }
        }

        private void WireClickToAll(Control root, EventHandler handler)
        {
            if (root == null || handler == null)
                return;

            root.Click += handler;
            root.Cursor = Cursors.Hand;

            foreach (Control child in root.Controls)
            {
                WireClickToAll(child, handler);
            }
        }


        private void ApplyTheme()
        {
            if (_darkMode)
            {
                ApplyDarkThemeToControl(this);
                ApplyDarkGridTheme();
                ApplyDarkButtonTheme();

                if (themeSwitch != null)
                    themeSwitch.Checked = true;
            }
            else
            {
                ApplyLightThemeToControl(this);
                ApplyLightGridTheme();
                ApplyLightButtonTheme();

                if (themeSwitch != null)
                    themeSwitch.Checked = false;
            }

            ApplyWindowTitleBarTheme();
            UpdateModeUi();
            ApplySlideTheme();
            ApplySlot04ModeUi();
            ApplySlot05ModeUi();
            RefreshAutoDimSlotImages(slideAutoDimPanel);
            ApplyAutoSectionSwitchUi();
            Invalidate(true);
        }

        private void ApplyDarkThemeToControl(Control root)
        {
            if (root == null)
                return;

            Color formBg = Color.FromArgb(10, 10, 10);
            Color panelBg = Color.FromArgb(18, 18, 18);
            Color panelBg2 = Color.FromArgb(24, 24, 24);
            Color textColor = Color.FromArgb(226, 232, 240);
            Color muted = Color.FromArgb(148, 163, 184);
            Color accent = Color.FromArgb(201, 122, 64);
            Color accentSoft = Color.FromArgb(37, 30, 25);
            Color border = Color.FromArgb(73, 56, 43);

            if (root == this)
                root.BackColor = formBg;
            else if (root is SafeRoundedButton)
            {
                SafeRoundedButton button = root as SafeRoundedButton;
                button.FillColor = Color.FromArgb(201, 122, 64);
                button.BorderColor = Color.FromArgb(201, 122, 64);
                button.TextColor = Color.FromArgb(20, 16, 14);
                button.Invalidate();
            }
            else if (root is RoundedPanel)
            {
                RoundedPanel rp = root as RoundedPanel;
                rp.BackColor = panelBg;
                rp.BorderColor = border;
            }
            else if (root is Panel)
            {
                root.BackColor = (root.Location.Y >= 598 && root.Width >= 900)
                    ? Color.FromArgb(18, 18, 18)
                    : panelBg2;
            }
            else if (root is Label)
            {
                Label label = root as Label;
                label.BackColor = Color.Transparent;

                if (label.Text != null && label.Text.Contains("DANH SÁCH"))
                    label.ForeColor = accent;
                else if (label.Text != null && label.Text.StartsWith("v"))
                {
                    label.ForeColor = Color.FromArgb(224, 156, 96);
                    label.BackColor = accentSoft;
                }
                else if (label.Text != null && label.Text.Contains("Optimized"))
                    label.ForeColor = textColor;
                else if (label.Font != null && label.Font.Size >= 20)
                    label.ForeColor = Color.FromArgb(248, 250, 252);
                else if (label.Text != null && label.Text.Contains("Tekla Structures"))
                    label.ForeColor = muted;
                else
                    label.ForeColor = textColor;
            }
            else if (root is Button)
            {
                Button b = root as Button;
                b.BackColor = Color.FromArgb(30, 30, 30);
                b.ForeColor = textColor;
                b.FlatStyle = FlatStyle.Flat;
                b.FlatAppearance.BorderSize = 1;
                b.FlatAppearance.BorderColor = border;
            }

            foreach (Control child in root.Controls)
                ApplyDarkThemeToControl(child);
        }

        private void ApplyLightThemeToControl(Control root)
        {
            if (root == null)
                return;

            if (root == this)
                root.BackColor = SoftBg;
            else if (root is SafeRoundedButton)
            {
                SafeRoundedButton button = root as SafeRoundedButton;
                button.FillColor = Blue;
                button.BorderColor = Blue;
                button.TextColor = Color.White;
                button.Invalidate();
            }
            else if (root is RoundedPanel)
            {
                RoundedPanel rp = root as RoundedPanel;
                rp.BackColor = Color.White;
                rp.BorderColor = PanelBorder;
            }
            else if (root is Panel)
            {
                root.BackColor = (root.Location.Y >= 598 && root.Width >= 900)
                    ? Blue
                    : Color.White;
            }
            else if (root is Label)
            {
                Label label = root as Label;
                label.BackColor = Color.Transparent;

                if (label.Text != null && label.Text.Contains("DANH SÁCH"))
                    label.ForeColor = BrightBlue;
                else if (label.Text != null && label.Text.StartsWith("v"))
                {
                    label.ForeColor = BrightBlue;
                    label.BackColor = Color.FromArgb(235, 242, 255);
                }
                else if (label.Text != null && label.Text.Contains("Optimized"))
                    label.ForeColor = Color.White;
                else if (label.Font != null && label.Font.Size >= 20)
                    label.ForeColor = Color.FromArgb(15, 23, 42);
                else if (label.Text != null && label.Text.Contains("Tekla Structures"))
                    label.ForeColor = Color.FromArgb(75, 85, 99);
                else
                    label.ForeColor = Color.FromArgb(15, 23, 42);
            }
            else if (root is Button)
            {
                Button b = root as Button;
                b.BackColor = SystemColors.Control;
                b.ForeColor = Color.FromArgb(15, 23, 42);
                b.FlatStyle = FlatStyle.Standard;
            }

            foreach (Control child in root.Controls)
                ApplyLightThemeToControl(child);
        }

        private void ApplyDarkGridTheme()
        {
            if (dgvDrawings == null)
                return;

            dgvDrawings.BackgroundColor = Color.FromArgb(13, 13, 13);
            dgvDrawings.BorderStyle = BorderStyle.FixedSingle;
            dgvDrawings.GridColor = Color.FromArgb(48, 42, 36);
            dgvDrawings.EnableHeadersVisualStyles = false;
            dgvDrawings.CellBorderStyle = DataGridViewCellBorderStyle.Single;
            dgvDrawings.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;

            dgvDrawings.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(22, 22, 22);
            dgvDrawings.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(224, 156, 96);
            dgvDrawings.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(22, 22, 22);
            dgvDrawings.ColumnHeadersDefaultCellStyle.SelectionForeColor = Color.FromArgb(224, 156, 96);

            dgvDrawings.DefaultCellStyle.BackColor = Color.FromArgb(16, 16, 16);
            dgvDrawings.DefaultCellStyle.ForeColor = Color.FromArgb(203, 213, 225);
            dgvDrawings.DefaultCellStyle.SelectionBackColor = Color.FromArgb(42, 34, 29);
            dgvDrawings.DefaultCellStyle.SelectionForeColor = Color.FromArgb(238, 210, 185);
            dgvDrawings.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(16, 16, 16);

            foreach (DataGridViewRow row in dgvDrawings.Rows)
            {
                bool isRev = false;

                try
                {
                    object revValue = row.Cells["REV"].Value;
                    isRev = revValue != null &&
                            !string.IsNullOrWhiteSpace(revValue.ToString()) &&
                            revValue.ToString() != "-";
                }
                catch
                {
                }

                ApplyDrawingGridRowStyle(row.Index, isRev);
            }

            CleanDataGridView cleanGrid = dgvDrawings as CleanDataGridView;
            if (cleanGrid != null)
            {
                cleanGrid.DarkMode = true;
                cleanGrid.DrawSoftOuterBorder = false;
                cleanGrid.Invalidate();
            }

            dgvDrawings.Refresh();
        }

        private void ApplyLightGridTheme()
        {
            if (dgvDrawings == null)
                return;

            dgvDrawings.BackgroundColor = Color.White;
            dgvDrawings.BorderStyle = BorderStyle.FixedSingle;
            dgvDrawings.GridColor = Color.FromArgb(226, 232, 240);
            dgvDrawings.EnableHeadersVisualStyles = false;
            dgvDrawings.CellBorderStyle = DataGridViewCellBorderStyle.Single;
            dgvDrawings.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;

            dgvDrawings.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(248, 250, 252);
            dgvDrawings.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(15, 23, 42);
            dgvDrawings.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(248, 250, 252);
            dgvDrawings.ColumnHeadersDefaultCellStyle.SelectionForeColor = Color.FromArgb(15, 23, 42);

            dgvDrawings.DefaultCellStyle.BackColor = Color.White;
            dgvDrawings.DefaultCellStyle.ForeColor = Color.FromArgb(15, 23, 42);
            dgvDrawings.DefaultCellStyle.SelectionBackColor = Color.FromArgb(239, 246, 255);
            dgvDrawings.DefaultCellStyle.SelectionForeColor = Color.FromArgb(15, 23, 42);
            dgvDrawings.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(250, 252, 255);

            foreach (DataGridViewRow row in dgvDrawings.Rows)
            {
                bool isRev = false;

                try
                {
                    object revValue = row.Cells["REV"].Value;
                    isRev = revValue != null &&
                            !string.IsNullOrWhiteSpace(revValue.ToString()) &&
                            revValue.ToString() != "-";
                }
                catch
                {
                }

                ApplyDrawingGridRowStyle(row.Index, isRev);
            }

            CleanDataGridView cleanGrid = dgvDrawings as CleanDataGridView;
            if (cleanGrid != null)
            {
                cleanGrid.DarkMode = false;
                cleanGrid.DrawSoftOuterBorder = true;
                cleanGrid.SoftOuterBorderColor = Color.FromArgb(160, 165, 170);
                cleanGrid.Invalidate();
            }

            dgvDrawings.Refresh();
        }

        private void ApplyDarkButtonTheme()
        {
            if (btnRun != null)
            {
                btnRun.FillColor = Color.FromArgb(201, 122, 64);
                btnRun.BorderColor = Color.FromArgb(201, 122, 64);
                btnRun.TextColor = Color.FromArgb(20, 16, 14);
                btnRun.Invalidate();
            }

            StyleDarkSmallButton(btnLoad);
            StyleDarkSmallButton(btnCheckScale);
            StyleDarkSmallButton(btnDictionary);
            StyleDarkSmallButton(btnClear);
        }

        private void ApplyLightButtonTheme()
        {
            if (btnRun != null)
            {
                btnRun.FillColor = Blue;
                btnRun.BorderColor = Blue;
                btnRun.TextColor = Color.White;
                btnRun.Invalidate();
            }

            StyleLightSmallButton(btnLoad);
            StyleLightSmallButton(btnCheckScale);
            StyleLightSmallButton(btnDictionary);
            StyleLightSmallButton(btnClear);
        }

        private void StyleDarkSmallButton(SafeRoundedButton b)
        {
            if (b == null)
                return;

            b.FillColor = Color.FromArgb(28, 28, 28);
            b.BorderColor = Color.FromArgb(201, 122, 64);
            b.HoverBorderColor = Color.Empty;
            b.TextColor = Color.FromArgb(224, 156, 96);
            b.BorderRadius = 6;
            b.Invalidate();
        }

        private void StyleLightSmallButton(SafeRoundedButton b)
        {
            if (b == null)
                return;

            b.FillColor = SystemColors.Control;
            b.BorderColor = Color.FromArgb(160, 165, 170);
            b.HoverBorderColor = BrightBlue;
            b.TextColor = Color.FromArgb(15, 23, 42);
            b.BorderRadius = 6;
            b.Invalidate();
        }


        private void btnLoad_Click(object sender, EventArgs e)
        {
            _resumeIndex = 0;
            _stopRequested = false;
            _selectedDrawings.Clear();
            dgvDrawings.Rows.Clear();

            try
            {
                DrawingHandler dh = new DrawingHandler();
                object selector = InvokeNoArg(dh, "GetDrawingSelector");
                if (selector == null)
                    throw new Exception("Không lấy được DrawingSelector. Hãy chọn drawing trong Document Manager rồi thử lại.");

                object enumerator = InvokeNoArg(selector, "GetSelected");
                if (enumerator == null)
                    throw new Exception("Không lấy được list drawing đang chọn.");

                int count = 0;
                while (MoveNext(enumerator))
                {
                    Drawing dr = GetCurrent(enumerator) as Drawing;
                    if (dr == null)
                        continue;

                    _selectedDrawings.Add(dr);
                    count++;

                    string mark = SafeDrawingMark(dr);
                    string rev = SafeDrawingRevision(dr);
                    string changes = SafeDrawingChanges(dr);

                    AddDrawingGridRow(count, mark, rev, changes);
                }

                if (count == 0)
                {
                    lblStatus.Text = "✓  Loaded    |    0 bản vẽ";
                    lblStatus.ForeColor = Color.FromArgb(22, 163, 74);
                }

                dgvDrawings.ClearSelection();

                lblCount.Text = "Tổng số bản vẽ:  " + count;
                lblStatus.Text = "✓  Loaded    |    " + count + " bản vẽ";
                lblStatus.ForeColor = Color.FromArgb(22, 163, 74);
            }
            catch (Exception ex)
            {
                dgvDrawings.Rows.Clear();
                SetMainStatus(
                    "Load Selected Drawings lỗi: " + ex.Message,
                    MainStatusKind.Error);
            }
        }

        private void AddDrawingGridRow(int index, string mark, string rev, string changes)
        {
            bool isRev = !string.IsNullOrWhiteSpace(rev);

            string status = isRev ? "REV" : "READY";
            string result = isRev ? "SKIP" : "-";

            int rowIndex = dgvDrawings.Rows.Add(
                index.ToString("000"),
                mark,
                isRev ? rev : "-",
                string.IsNullOrWhiteSpace(changes) ? "-" : changes,
                status,
                result
            );

            ApplyDrawingGridRowStyle(rowIndex, isRev);
        }

        private void ApplyDrawingGridRowStyle(int rowIndex, bool isRev)
        {
            if (dgvDrawings == null)
                return;

            if (rowIndex < 0 || rowIndex >= dgvDrawings.Rows.Count)
                return;

            DataGridViewRow row = dgvDrawings.Rows[rowIndex];

            if (_darkMode)
            {
                row.DefaultCellStyle.ForeColor = Color.FromArgb(203, 213, 225);
                row.DefaultCellStyle.BackColor = Color.FromArgb(16, 16, 16);
                row.DefaultCellStyle.SelectionBackColor = Color.FromArgb(42, 34, 29);
                row.DefaultCellStyle.SelectionForeColor = Color.FromArgb(238, 210, 185);
            }
            else
            {
                row.DefaultCellStyle.ForeColor = Color.FromArgb(15, 23, 42);
                row.DefaultCellStyle.BackColor = Color.White;
                row.DefaultCellStyle.SelectionBackColor = Color.FromArgb(239, 246, 255);
                row.DefaultCellStyle.SelectionForeColor = Color.FromArgb(15, 23, 42);
            }

            row.Cells["STATUS"].Style.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            row.Cells["RESULT"].Style.Font = new Font("Segoe UI", 9F, FontStyle.Bold);

            if (isRev)
            {
                Color revColor = _darkMode
                    ? Color.FromArgb(224, 156, 96)
                    : Color.FromArgb(201, 122, 64);

                row.Cells["STATUS"].Style.ForeColor = revColor;
                row.Cells["RESULT"].Style.ForeColor = revColor;
            }
            else
            {
                row.Cells["STATUS"].Style.ForeColor = Color.FromArgb(22, 163, 74);
                row.Cells["RESULT"].Style.ForeColor = _darkMode
                    ? Color.FromArgb(148, 163, 184)
                    : Color.FromArgb(100, 116, 139);
            }
        }

        private void SetGridResult(int rowIndex, string text, Color color)
        {
            if (dgvDrawings == null)
                return;

            if (rowIndex < 0 || rowIndex >= dgvDrawings.Rows.Count)
                return;

            dgvDrawings.Rows[rowIndex].Cells["RESULT"].Value = text;
            dgvDrawings.Rows[rowIndex].Cells["RESULT"].Style.ForeColor = color;
            dgvDrawings.Rows[rowIndex].Cells["RESULT"].Style.Font =
                new Font("Segoe UI", 9F, FontStyle.Bold);

            dgvDrawings.ClearSelection();
            dgvDrawings.Rows[rowIndex].Selected = true;

            try
            {
                dgvDrawings.FirstDisplayedScrollingRowIndex = rowIndex;
            }
            catch { }

            Application.DoEvents();
        }

        private void SetGridStatusAndResult(
            int rowIndex,
            string statusText,
            Color statusColor,
            string resultText,
            Color resultColor)
        {
            if (dgvDrawings == null)
                return;

            if (rowIndex < 0 || rowIndex >= dgvDrawings.Rows.Count)
                return;

            dgvDrawings.Rows[rowIndex].Cells["STATUS"].Value = statusText;
            dgvDrawings.Rows[rowIndex].Cells["STATUS"].Style.ForeColor = statusColor;
            dgvDrawings.Rows[rowIndex].Cells["STATUS"].Style.Font =
                new Font("Segoe UI", 9F, FontStyle.Bold);

            dgvDrawings.Rows[rowIndex].Cells["RESULT"].Value = resultText;
            dgvDrawings.Rows[rowIndex].Cells["RESULT"].Style.ForeColor = resultColor;
            dgvDrawings.Rows[rowIndex].Cells["RESULT"].Style.Font =
                new Font("Segoe UI", 9F, FontStyle.Bold);

            dgvDrawings.ClearSelection();
            dgvDrawings.Rows[rowIndex].Selected = true;

            try
            {
                dgvDrawings.FirstDisplayedScrollingRowIndex = rowIndex;
            }
            catch
            {
            }

            Application.DoEvents();
        }

        private void dgvDrawings_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0)
                return;

            if (e.RowIndex >= _selectedDrawings.Count)
                return;

            try
            {
                Drawing dr = _selectedDrawings[e.RowIndex];

                if (dr == null)
                    return;

                DrawingHandler dh = new DrawingHandler();
                SetActiveDrawingSafe(dh, dr);

                lblStatus.Text = "✓  Opened drawing: " + SafeDrawingName(dr);
                lblStatus.ForeColor = Color.FromArgb(22, 163, 74);
            }
            catch (Exception ex)
            {
                SetMainStatus(
                    "Open drawing lỗi: " + ex.Message,
                    MainStatusKind.Error);
            }
        }



        private void MakeWholeBoxSelectable(Control root, RadioButton radio)
        {
            if (root == null || radio == null)
                return;

            root.Cursor = Cursors.Hand;

            root.Click += delegate
            {
                radio.Checked = true;
                UpdateModeUi();
            };

            foreach (Control child in root.Controls)
            {
                MakeWholeBoxSelectable(child, radio);
            }
        }



        private void btnCheckScale_Click(object sender, EventArgs e)
        {
            RunScaleCheck();
        }

        private class ScaleCheckResult
        {
            public Drawing Drawing;
            public string Mark;
            public string Rev;
            public string Changes;
            public string TitleScale;
            public string TopScale;
            public string FrontScale;
            public List<string> ViewScales = new List<string>();
            public List<string> ViewScaleLabels = new List<string>();
            public string Message;
        }

        private class ViewScaleEntry
        {
            public Tekla.Structures.Drawing.View View;
            public string Scale;
            public string ViewLabel;
            public double Height;
            public double Area;
        }

        private void RunScaleCheck()
        {
            _resumeIndex = 0;
            _stopRequested = false;

            if (_selectedDrawings.Count == 0)
            {
                lblStatus.Text = "✗  Chưa có bản vẽ để Check Scale";
                lblStatus.ForeColor = Color.Firebrick;
                return;
            }

            btnRun.Enabled = false;
            btnLoad.Enabled = false;
            btnCheckScale.Enabled = false;

            List<Drawing> sourceDrawings = new List<Drawing>(_selectedDrawings);
            List<ScaleCheckResult> errors = new List<ScaleCheckResult>();

            try
            {
                lblStatus.Text = "▶  Checking scale...";
                lblStatus.ForeColor = Blue;
                Application.DoEvents();

                DrawingHandler dh = new DrawingHandler();

                for (int i = 0; i < sourceDrawings.Count; i++)
                {
                    Drawing dr = sourceDrawings[i];
                    string mark = SafeDrawingMark(dr);
                    string rev = SafeDrawingRevision(dr);
                    string changes = SafeDrawingChanges(dr);

                    lblStatus.Text = "▶  Checking scale " + (i + 1) + "/" + sourceDrawings.Count + " : " + mark;
                    lblStatus.ForeColor = Blue;

                    SetGridStatusAndResult(
                        i,
                        "CHECKING",
                        Color.FromArgb(59, 130, 246),
                        "RUNNING",
                        Color.FromArgb(59, 130, 246));

                    ScaleCheckResult result = CheckDrawingScale(dh, dr, mark, rev);
                    result.Changes = changes;

                    if (IsScaleError(result))
                    {
                        errors.Add(result);

                        SetGridStatusAndResult(
                            i,
                            "SCALE ERROR",
                            Color.FromArgb(220, 38, 38),
                            BuildScaleErrorResultText(result),
                            Color.FromArgb(220, 38, 38));
                    }
                    else
                    {
                        SetGridStatusAndResult(
                            i,
                            "SCALE OK",
                            Color.FromArgb(22, 163, 74),
                            "OK",
                            Color.FromArgb(22, 163, 74));
                    }
                }

                if (errors.Count == 0)
                {
                    lblStatus.Text = "✓  Scale OK: không có bản vẽ sai tỉ lệ";
                    lblStatus.ForeColor = Color.FromArgb(22, 163, 74);
                    dgvDrawings.ClearSelection();
                    return;
                }

                _selectedDrawings.Clear();
                dgvDrawings.Rows.Clear();

                for (int i = 0; i < errors.Count; i++)
                {
                    ScaleCheckResult error = errors[i];
                    _selectedDrawings.Add(error.Drawing);
                    AddScaleErrorGridRow(i + 1, error);
                }

                dgvDrawings.ClearSelection();

                lblCount.Text = "Sai tỉ lệ:  " + errors.Count + "/" + sourceDrawings.Count;
                lblStatus.Text = "✗  Scale Error: " + errors.Count + "/" + sourceDrawings.Count + " bản vẽ";
                lblStatus.ForeColor = Color.FromArgb(220, 38, 38);
            }
            catch (Exception ex)
            {
                SetMainStatus(
                    "Check Scale lỗi: " + ex.Message,
                    MainStatusKind.Error);
            }
            finally
            {
                btnRun.Enabled = true;
                btnCheckScale.Enabled = true;
                UpdateModeUi();
            }
        }

        private void AddScaleErrorGridRow(int index, ScaleCheckResult error)
        {
            string resultText = BuildScaleErrorResultText(error);

            int rowIndex = dgvDrawings.Rows.Add(
                index.ToString("000"),
                error.Mark,
                string.IsNullOrWhiteSpace(error.Rev) ? "-" : error.Rev,
                string.IsNullOrWhiteSpace(error.Changes) ? "-" : error.Changes,
                "SCALE ERROR",
                resultText
            );

            DataGridViewRow row = dgvDrawings.Rows[rowIndex];

            row.Cells["STATUS"].Style.ForeColor = Color.FromArgb(220, 38, 38);
            row.Cells["STATUS"].Style.Font = new Font("Segoe UI", 9F, FontStyle.Bold);

            row.Cells["RESULT"].Style.ForeColor = Color.FromArgb(220, 38, 38);
            row.Cells["RESULT"].Style.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        }

        private static string BuildScaleErrorResultText(ScaleCheckResult error)
        {
            if (error == null)
                return "SCALE ERROR";

            string titleText = string.IsNullOrWhiteSpace(error.TitleScale) ? "?" : NormalizeScaleText(error.TitleScale);

            if (error.ViewScaleLabels != null &&
                error.ViewScales != null &&
                error.ViewScaleLabels.Count == error.ViewScales.Count &&
                error.ViewScales.Count > 0)
            {
                List<string> wrongViews = new List<string>();
                List<string> allViewScaleValues = new List<string>();

                for (int i = 0; i < error.ViewScales.Count; i++)
                {
                    string label = string.IsNullOrWhiteSpace(error.ViewScaleLabels[i]) ? "VIEW" : error.ViewScaleLabels[i];
                    string scale = string.IsNullOrWhiteSpace(error.ViewScales[i]) ? "?" : NormalizeScaleText(error.ViewScales[i]);

                    AddUniqueScale(allViewScaleValues, scale);

                    if (scale != titleText)
                    {
                        string viewScaleText = label + " " + scale;

                        if (!wrongViews.Contains(viewScaleText))
                            wrongViews.Add(viewScaleText);
                    }
                }

                if (wrongViews.Count == 0)
                    return "SCALE ERROR";

                if (allViewScaleValues.Count == 1 &&
                    allViewScaleValues[0] != titleText)
                    return "VIEW " + allViewScaleValues[0] + " / TITLE " + titleText;

                return string.Join(" / ", wrongViews.ToArray()) + " / TITLE " + titleText;
            }


            if (error.ViewScales == null || error.ViewScales.Count <= 2)
            {
                string topText = string.IsNullOrWhiteSpace(error.TopScale) ? "?" : NormalizeScaleText(error.TopScale);
                string frontText = string.IsNullOrWhiteSpace(error.FrontScale) ? "?" : NormalizeScaleText(error.FrontScale);

                bool topError = topText != titleText;
                bool frontError = frontText != titleText;

                if (topError && frontError)
                {
                    if (topText == frontText)
                        return "VIEW " + topText + " / TITLE " + titleText;

                    return "TOP " + topText + " / FRONT " + frontText + " / TITLE " + titleText;
                }

                if (topError)
                    return "TOP " + topText + " / TITLE " + titleText;

                if (frontError)
                    return "FRONT " + frontText + " / TITLE " + titleText;

                return "SCALE ERROR";
            }


            List<string> wrongScales = new List<string>();

            foreach (string viewScale in error.ViewScales)
            {
                string scale = string.IsNullOrWhiteSpace(viewScale) ? "?" : NormalizeScaleText(viewScale);

                if (scale != titleText)
                    AddUniqueScale(wrongScales, scale);
            }

            if (wrongScales.Count == 0)
                return "SCALE ERROR";

            if (wrongScales.Count == 1)
                return "VIEW " + wrongScales[0] + " / TITLE " + titleText;

            return "VIEW " + string.Join(", ", wrongScales.ToArray()) + " / TITLE " + titleText;
        }

        private static bool IsScaleError(ScaleCheckResult result)
        {
            if (result == null)
                return false;

            if (string.IsNullOrWhiteSpace(result.TitleScale))
                return true;

            if (result.ViewScales == null || result.ViewScales.Count == 0)
                return true;

            string titleScale = NormalizeScaleText(result.TitleScale);


            foreach (string viewScale in result.ViewScales)
            {
                if (string.IsNullOrWhiteSpace(viewScale))
                    return true;

                if (NormalizeScaleText(viewScale) != titleScale)
                    return true;
            }

            return false;
        }

        private static ScaleCheckResult CheckDrawingScale(DrawingHandler dh, Drawing dr, string mark, string rev)
        {
            ScaleCheckResult result = new ScaleCheckResult();
            result.Drawing = dr;
            result.Mark = mark;
            result.Rev = rev;

            bool openedForCheck = false;

            try
            {
                if (dh.IsAnyDrawingOpen())
                {
                    CloseActiveDrawingSafe(dh);
                    Thread.Sleep(80);
                }

                SetActiveDrawingSafe(dh, dr);
                openedForCheck = true;
                Thread.Sleep(80);

                Drawing activeDrawing = dh.GetActiveDrawing();
                if (activeDrawing == null)
                    throw new Exception("Không mở lại được drawing để Check Scale.");

                string activeMark = SafeDrawingMark(activeDrawing);
                if (activeDrawing.GetType() != dr.GetType() ||
                    (!string.IsNullOrWhiteSpace(mark) &&
                     !string.Equals(activeMark, mark, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new Exception("Drawing đang mở không đúng drawing cần Check Scale.");
                }

                result.Drawing = activeDrawing;
                result.TitleScale = GetDrawingTitle3Scale(activeDrawing);

                result.ViewScales = GetMainViewScales(dh, activeDrawing, result.ViewScaleLabels);

                if (result.ViewScales.Count >= 1)
                    result.TopScale = result.ViewScales[0];

                if (result.ViewScales.Count >= 2)
                    result.FrontScale = result.ViewScales[1];
            }
            catch (Exception ex)
            {
                result.Message = ex.Message;
            }
            finally
            {

                if (openedForCheck)
                {
                    try
                    {
                        CloseActiveDrawingSafe(dh);
                        Thread.Sleep(80);
                    }
                    catch
                    {
                    }
                }
            }

            return result;
        }

        private static List<string> GetMainViewScales(DrawingHandler dh, Drawing drawing, List<string> viewLabels)
        {
            List<string> scales = new List<string>();

            if (viewLabels != null)
                viewLabels.Clear();

            if (drawing == null)
                return scales;

            SinglePartDrawing spDrawing = drawing as SinglePartDrawing;
            AssemblyDrawing assemblyDrawing = drawing as AssemblyDrawing;

            if (spDrawing == null && assemblyDrawing == null)
                return scales;

            try
            {
                ContainerView sheet = drawing.GetSheet();
                if (sheet == null)
                    return scales;

                DrawingObjectEnumerator views = sheet.GetAllViews();

                while (views.MoveNext())
                {
                    Tekla.Structures.Drawing.View view =
                        views.Current as Tekla.Structures.Drawing.View;

                    if (view == null)
                        continue;

                    string viewLabel = GetScaleCheckViewTypeLabel(view);

                    if (string.IsNullOrWhiteSpace(viewLabel))
                        continue;

                    // Quan trọng:
                    // Tự select view giống thao tác tay để Tekla nạp đúng Drawing View Properties.
                    SelectDrawingObjectSafe(dh, view);
                    Thread.Sleep(80);

                    string scale = GetScaleFromSelectedView(view);

                    if (string.IsNullOrWhiteSpace(scale))
                        scale = "?";

                    scales.Add(NormalizeScaleText(scale));

                    if (viewLabels != null)
                        viewLabels.Add(viewLabel);
                }
            }
            catch
            {
            }

            return scales;
        }

        private static string GetScaleCheckViewTypeLabel(Tekla.Structures.Drawing.View view)
        {
            try
            {
                if (view == null)
                    return "";

                string text = "";

                try
                {
                    text = view.ViewType.ToString();
                }
                catch
                {
                    text = "";
                }

                if (string.IsNullOrWhiteSpace(text))
                    return "";

                if (string.Equals(text, "FrontView", StringComparison.OrdinalIgnoreCase) ||
                    text.IndexOf("Front", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "FRONT";

                if (string.Equals(text, "BottomView", StringComparison.OrdinalIgnoreCase) ||
                    text.IndexOf("Bottom", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "BOTTOM";

                if (string.Equals(text, "TopView", StringComparison.OrdinalIgnoreCase) ||
                    text.IndexOf("Top", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "TOP";

                if (string.Equals(text, "BackView", StringComparison.OrdinalIgnoreCase) ||
                    text.IndexOf("Back", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "BACK";

                if (string.Equals(text, "SectionView", StringComparison.OrdinalIgnoreCase) ||
                    text.IndexOf("Section", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "SECTION";
            }
            catch
            {
            }

            return "";
        }

        private static bool ViewContainsMainPart(
            Tekla.Structures.Drawing.View view,
            SinglePartDrawing spDrawing)
        {
            try
            {
                if (view == null || spDrawing == null)
                    return false;

                DrawingObjectEnumerator parts =
                    view.GetAllObjects(typeof(Tekla.Structures.Drawing.Part));

                while (parts.MoveNext())
                {
                    Tekla.Structures.Drawing.Part dp =
                        parts.Current as Tekla.Structures.Drawing.Part;

                    if (dp == null)
                        continue;

                    if (dp.ModelIdentifier.ID == spDrawing.PartIdentifier.ID)
                        return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private static bool AssemblyViewContainsPart(Tekla.Structures.Drawing.View view)
        {
            try
            {
                if (view == null)
                    return false;

                DrawingObjectEnumerator parts =
                    view.GetAllObjects(typeof(Tekla.Structures.Drawing.Part));

                while (parts.MoveNext())
                {
                    Tekla.Structures.Drawing.Part dp =
                        parts.Current as Tekla.Structures.Drawing.Part;

                    if (dp != null)
                        return true;
                }
            }
            catch
            {
            }

            return false;
        }


        private static string GetViewInfoText(Tekla.Structures.Drawing.View view)
        {
            if (view == null)
                return "";

            string result = "";

            try
            {
                if (!string.IsNullOrWhiteSpace(view.Name))
                    result += " " + view.Name;
            }
            catch
            {
            }

            try
            {
                object attributes = GetPropertyValue(view, "Attributes");
                if (attributes != null)
                    result += " " + attributes.ToString();
            }
            catch
            {
            }

            try
            {
                result += " " + view.GetType().FullName;
            }
            catch
            {
            }

            return result;
        }



        private static double GetViewBoxHeight(Tekla.Structures.Drawing.View view)
        {
            try
            {
                if (view == null)
                    return 999999999.0;

                Tekla.Structures.Geometry3d.AABB box = view.RestrictionBox;

                if (box == null || box.MinPoint == null || box.MaxPoint == null)
                    return 999999999.0;

                return Math.Abs(box.MaxPoint.Y - box.MinPoint.Y);
            }
            catch
            {
                return 999999999.0;
            }
        }

        private static double GetViewBoxArea(Tekla.Structures.Drawing.View view)
        {
            try
            {
                if (view == null)
                    return 0.0;

                Tekla.Structures.Geometry3d.AABB box = view.RestrictionBox;

                if (box == null || box.MinPoint == null || box.MaxPoint == null)
                    return 0.0;

                double width = Math.Abs(box.MaxPoint.X - box.MinPoint.X);
                double height = Math.Abs(box.MaxPoint.Y - box.MinPoint.Y);

                if (width <= 0.0 || height <= 0.0)
                    return 0.0;

                return width * height;
            }
            catch
            {
                return 0.0;
            }
        }



        private static void SelectDrawingObjectSafe(DrawingHandler dh, DrawingObject obj)
        {
            if (dh == null || obj == null)
                return;

            try
            {
                object selector = InvokeNoArg(dh, "GetDrawingObjectSelector");
                if (selector == null)
                    return;

                MethodInfo[] methods = selector.GetType().GetMethods(
                    BindingFlags.Public | BindingFlags.Instance);

                foreach (MethodInfo m in methods)
                {
                    if (m.Name != "SelectObject")
                        continue;

                    ParameterInfo[] ps = m.GetParameters();

                    if (ps.Length == 1 &&
                        ps[0].ParameterType.IsAssignableFrom(typeof(DrawingObject)))
                    {
                        m.Invoke(selector, new object[] { obj });
                        return;
                    }
                }

                foreach (MethodInfo m in methods)
                {
                    if (m.Name != "SelectObjects")
                        continue;

                    ParameterInfo[] ps = m.GetParameters();

                    if (ps.Length == 1)
                    {
                        System.Collections.ArrayList list = new System.Collections.ArrayList();
                        list.Add(obj);
                        m.Invoke(selector, new object[] { list });
                        return;
                    }
                }
            }
            catch
            {
            }
        }

        private static string GetScaleFromSelectedView(Tekla.Structures.Drawing.View view)
        {
            if (view == null)
                return "";

            // Sau khi đã select view, đọc lại Attributes trước.
            string scale = GetScaleFromView(view);

            if (!string.IsNullOrWhiteSpace(scale))
                return scale;

            // Fallback: quét sâu properties của view / attributes.
            object attributes = GetPropertyValue(view, "Attributes");
            scale = DeepFindScaleValue(attributes, 0);

            if (!string.IsNullOrWhiteSpace(scale))
                return scale;

            return DeepFindScaleValue(view, 0);
        }

        private static string DeepFindScaleValue(object obj, int depth)
        {
            if (obj == null || depth > 3)
                return "";

            Type type = obj.GetType();

            PropertyInfo[] props = type.GetProperties(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            foreach (PropertyInfo prop in props)
            {
                if (!prop.CanRead)
                    continue;

                if (prop.GetIndexParameters().Length > 0)
                    continue;

                string name = prop.Name;

                bool nameLooksLikeScale =
                    name.IndexOf("Scale", StringComparison.OrdinalIgnoreCase) >= 0;

                try
                {
                    object value = prop.GetValue(obj, null);

                    if (value == null)
                        continue;

                    if (nameLooksLikeScale)
                    {
                        string direct = NormalizeScaleInput(value);

                        if (!string.IsNullOrWhiteSpace(direct))
                            return direct;
                    }

                    Type valueType = value.GetType();

                    if (valueType == typeof(string) ||
                        valueType == typeof(int) ||
                        valueType == typeof(double) ||
                        valueType == typeof(float) ||
                        valueType == typeof(decimal) ||
                        valueType == typeof(bool))
                    {
                        continue;
                    }

                    string nested = DeepFindScaleValue(value, depth + 1);

                    if (!string.IsNullOrWhiteSpace(nested))
                        return nested;
                }
                catch
                {
                }
            }

            return "";
        }

        private static void AddUniqueScale(List<string> scales, string scale)
        {
            if (scales == null || string.IsNullOrWhiteSpace(scale))
                return;

            string normalized = NormalizeScaleText(scale);

            foreach (string existing in scales)
            {
                if (NormalizeScaleText(existing) == normalized)
                    return;
            }

            scales.Add(normalized);
        }

        private static void CollectViews(object container, List<object> views, HashSet<string> visited)
        {
            if (container == null)
                return;

            string key = container.GetType().FullName + ":" +
                System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(container).ToString();

            if (visited.Contains(key))
                return;

            visited.Add(key);

            object enumerator = InvokeNoArg(container, "GetAllObjects");
            if (enumerator == null)
                return;

            while (MoveNext(enumerator))
            {
                object obj = GetCurrent(enumerator);
                if (obj == null)
                    continue;

                string typeName = obj.GetType().Name;

                if (string.Equals(typeName, "View", StringComparison.OrdinalIgnoreCase))
                {
                    views.Add(obj);
                    continue;
                }

                if (HasNoArgMethod(obj, "GetAllObjects"))
                    CollectViews(obj, views, visited);
            }
        }

        private static string GetScaleFromView(object view)
        {
            if (view == null)
                return "";

            object attributes = GetPropertyValue(view, "Attributes");
            string attrScale = GetScaleValueFromObject(attributes);

            if (!string.IsNullOrWhiteSpace(attrScale))
                return attrScale;

            string directScale = GetScaleValueFromObject(view);
            if (!string.IsNullOrWhiteSpace(directScale))
                return directScale;

            return "";
        }

        private static string GetScaleValueFromObject(object obj)
        {
            if (obj == null)
                return "";

            string[] names = new string[]
            {
                "Scale",
                "ViewScale",
                "DrawingScale"
            };

            foreach (string name in names)
            {
                object value = GetPropertyValue(obj, name);

                if (value != null)
                {
                    string scale = NormalizeScaleInput(value);

                    if (!string.IsNullOrWhiteSpace(scale))
                        return scale;
                }
            }

            return "";
        }

        private static string GetDrawingTitle3Scale(Drawing dr)
        {
            if (dr == null)
                return "";

            string[] propNames = new string[]
            {
                "Title3",
                "Title 3",
                "TITLE3",
                "TitleThree",
                "DrawingTitle3"
            };

            foreach (string propName in propNames)
            {
                object value = GetPropertyValue(dr, propName);

                if (value != null)
                {
                    string scale = ExtractScaleText(value.ToString());

                    if (!string.IsNullOrWhiteSpace(scale))
                        return scale;
                }
            }

            string[] reportNames = new string[]
            {
                "TITLE3",
                "TITLE_3",
                "DRAWING_TITLE3",
                "DRAWING.TITLE3",
                "DRAWING_TITLE_3",
                "TITLE3_TEXT"
            };


            try
            {
                PropertyInfo pi = dr.GetType().GetProperty(
                    "Identifier",
                    BindingFlags.Instance | BindingFlags.NonPublic);

                if (pi != null)
                {
                    object rawId = pi.GetValue(dr, null);

                    Tekla.Structures.Identifier identifier =
                        rawId as Tekla.Structures.Identifier;

                    if (identifier != null)
                    {
                        Tekla.Structures.Model.Beam dummy =
                            new Tekla.Structures.Model.Beam();

                        dummy.Identifier = identifier;

                        foreach (string reportName in reportNames)
                        {
                            string value = "";

                            try
                            {
                                if (dummy.GetReportProperty(reportName, ref value))
                                {
                                    string scale = ExtractScaleText(value);

                                    if (!string.IsNullOrWhiteSpace(scale))
                                        return scale;
                                }
                            }
                            catch
                            {
                            }
                        }
                    }
                }
            }
            catch
            {
            }

            return "";
        }

        private static string ExtractScaleText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "";

            text = text.Trim();

            System.Text.RegularExpressions.Match ratioMatch =
                System.Text.RegularExpressions.Regex.Match(
                    text,
                    @"\b\d+\s*:\s*\d+\b");

            if (ratioMatch.Success)
                return NormalizeScaleText(ratioMatch.Value);

            System.Text.RegularExpressions.Match numberMatch =
                System.Text.RegularExpressions.Regex.Match(
                    text,
                    @"\b\d+(\.\d+)?\b");

            if (numberMatch.Success)
            {
                double number = 0.0;

                if (double.TryParse(
                    numberMatch.Value,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out number))
                {
                    if (number > 0.0)
                        return FormatScaleNumber(number);
                }
            }

            return NormalizeScaleText(text);
        }

        private static string NormalizeScaleInput(object value)
        {
            if (value == null)
                return "";

            try
            {
                if (value is int ||
                    value is double ||
                    value is float ||
                    value is decimal)
                {
                    double number = Convert.ToDouble(value);

                    if (number > 0.0)
                        return FormatScaleNumber(number);
                }
            }
            catch
            {
            }

            return ExtractScaleText(value.ToString());
        }

        private static string NormalizeScaleText(string scale)
        {
            if (string.IsNullOrWhiteSpace(scale))
                return "";

            scale = scale.Trim();
            scale = scale.Replace(" ", "");

            if (scale == "?")
                return "?";

            if (scale.StartsWith("Scale", StringComparison.OrdinalIgnoreCase))
                scale = scale.Substring(5).Trim();

            if (scale.StartsWith("=", StringComparison.OrdinalIgnoreCase))
                scale = scale.Substring(1).Trim();

            if (scale.IndexOf(":") >= 0)
                return scale;

            double number = 0.0;

            if (double.TryParse(
                scale,
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture,
                out number))
            {
                if (number > 0.0)
                    return FormatScaleNumber(number);
            }

            return scale;
        }

        private static string FormatScaleNumber(double scale)
        {
            double rounded = Math.Round(scale, 3);

            if (Math.Abs(rounded - Math.Round(rounded)) < 0.001)
                return "1:" + ((int)Math.Round(rounded)).ToString();

            return "1:" + rounded.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static object GetPropertyValue(object obj, string propName)
        {
            if (obj == null || string.IsNullOrWhiteSpace(propName))
                return null;

            try
            {
                PropertyInfo p = obj.GetType().GetProperty(
                    propName,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                if (p != null && p.GetIndexParameters().Length == 0)
                    return p.GetValue(obj, null);
            }
            catch
            {
            }

            return null;
        }

        private static bool HasNoArgMethod(object obj, string methodName)
        {
            if (obj == null)
                return false;

            try
            {
                MethodInfo m = obj.GetType().GetMethod(
                    methodName,
                    BindingFlags.Public | BindingFlags.Instance);

                if (m == null)
                    return false;

                return m.GetParameters().Length == 0;
            }
            catch
            {
                return false;
            }
        }

        private void btnRun_Click(object sender, EventArgs e)
        {
            if (_isBatchRunning)
            {
                _stopRequested = true;
                btnRun.Text = "■  STOPPING...";
                btnRun.Enabled = false;
                lblStatus.Text = "■  Stop requested. Sẽ dừng sau khi xong bản vẽ hiện tại...";
                lblStatus.ForeColor = Color.DarkOrange;
                Application.DoEvents();
                return;
            }

            if (rbActive.Checked)
            {
                btnRun.Enabled = false;
                btnLoad.Enabled = false;

                try
                {
                    RunActiveDrawing();
                }
                finally
                {
                    btnRun.Enabled = true;
                    btnRun.Text = "▶  CREATE DRAWING";
                    UpdateModeUi();
                }

                return;
            }

            RunBatchDrawings();
        }

        private enum AutoDimPartType
        {
            Unknown,
            Plate,
            ShapeIH,
            ShapeC,
            ShapeL,
            ShapeBox,
            ShapeUnknown
        }

        private enum AutoSectionStatus
        {
            NotApplicable,
            Disabled,
            HolesSame,
            HoleCheckUnknown,
            ExistingLayout,
            CreatedSingle,
            CreatedAssemblyBottom,
            PartialLayout,
            PreflightFailed,
            CreateFailed,
            RolledBack,
            UnsafeRollbackFailed
        }

        private class AutoDimExecutionResult
        {
            public AutoSectionStatus SectionStatus = AutoSectionStatus.NotApplicable;
            public int OriginalHoleResult = -1;
            public string SectionMessage = "";
            public bool CanSaveDrawing = true;
        }

        private enum ShapeProfileType
        {
            Unknown,
            IH,
            C,
            L,
            Box
        }

        private bool TryRunLoadStandardBeforeAutoDim(out string message)
        {
            message = string.Empty;

            try
            {
                Type t = FindTypeInLoadedAssemblies(
                    "Tekla.Technology.Akit.UserScript.PHU_LoadStandardService");

                if (t == null)
                {
                    message = "Không tìm thấy service load tiêu chuẩn: Tekla.Technology.Akit.UserScript.PHU_LoadStandardService";
                    return false;
                }

                MethodInfo run = t.GetMethod(
                    "Run",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                    null,
                    Type.EmptyTypes,
                    null);

                if (run == null)
                {
                    message = "Service load tiêu chuẩn thiếu hàm Run().";
                    return false;
                }

                run.Invoke(null, null);

                Application.DoEvents();

                PropertyInfo messageProperty = t.GetProperty(
                    "LastRunMessage",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

                if (messageProperty != null && messageProperty.PropertyType == typeof(string))
                {
                    object messageValue = messageProperty.GetValue(null, null);
                    if (messageValue is string)
                        message = messageValue as string;
                }

                PropertyInfo successProperty = t.GetProperty(
                    "LastRunSucceeded",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

                if (successProperty != null && successProperty.PropertyType == typeof(bool))
                {
                    object successValue = successProperty.GetValue(null, null);
                    if (successValue is bool && !(bool)successValue)
                    {
                        if (string.IsNullOrWhiteSpace(message))
                            message = "Load tiêu chuẩn thất bại.";

                        return false;
                    }
                }

                if (string.IsNullOrWhiteSpace(message))
                    message = "Load tiêu chuẩn hoàn tất.";

                return true;
            }
            catch (Exception ex)
            {
                Exception real = ex.InnerException != null ? ex.InnerException : ex;
                message = "Load tiêu chuẩn lỗi: " + real.Message;
                return false;
            }
        }

        private AutoDimExecutionResult RunCurrentAutoDimScript()
        {
            AutoDimExecutionResult execution = new AutoDimExecutionResult();
            AutoDimPartType partType = DetectActiveDrawingAutoDimPartType();

            if (partType == AutoDimPartType.Unknown)
            {
                MessageBox.Show(
                    "Không tự nhận diện được loại bản vẽ. Hiện hỗ trợ Plate, I/H, C, L, thép hộp.",
                    "TTSK AutoDim Auto Detect",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                execution.SectionMessage =
                    "Khong xac dinh duoc ModelPart can AutoDim.";
                execution.CanSaveDrawing = false;
                return execution;
            }

            int selectedShapeAssemblyPartId =
                CaptureSelectedShapeAssemblyPartId(partType);

            bool runAutoSection = ShouldRunAutoSection(partType);
            bool autoSectionDimPassRequired = false;
            bool autoSectionSingleLayout = false;

            // Geometry Standard is independent from Auto Section and must
            // finish before geometry analysis or any Section-specific work.
            string loadStandardMessage;
            if (!TryRunLoadStandardBeforeAutoDim(out loadStandardMessage))
                throw new Exception(loadStandardMessage);

            if (selectedShapeAssemblyPartId > 0 &&
                !RestoreSelectedDrawingPartByModelId(selectedShapeAssemblyPartId))
            {
                throw new Exception(
                    "Không khôi phục được part thép hình đã chọn sau khi Load Standard.");
            }

            if (partType == AutoDimPartType.ShapeIH && !runAutoSection)
                execution.SectionStatus = AutoSectionStatus.Disabled;

            if (runAutoSection)
            {
                bool sectionWorkerInvoked = false;
                DrawingHandler drawingHandler = new DrawingHandler();
                Drawing drawing = drawingHandler.GetActiveDrawing();
                Model model = new Model();
                Tekla.Structures.Model.Part part = ResolveAutoSectionModelPart(
                    drawing,
                    model,
                    selectedShapeAssemblyPartId);

                Tekla.Technology.Akit.UserScript.HShapeAutoSectionPrecheckResult precheck =
                    Tekla.Technology.Akit.UserScript.ShapeScript.PrepareAutoSectionPrecheck(
                        drawing,
                        model,
                        part);

                execution.OriginalHoleResult = precheck != null
                    ? precheck.HoleResult
                    : -1;

                if (precheck == null)
                {
                    execution.SectionStatus = AutoSectionStatus.HoleCheckUnknown;
                    execution.SectionMessage = "Precheck Auto Section khong tra ket qua.";
                }
                else if (precheck.HasPartialSectionLayout)
                {
                    execution.SectionStatus = AutoSectionStatus.PartialLayout;
                    execution.SectionMessage =
                        "Section layout dang co mot phan; khong tu repair.";
                }
                else if ((drawing is SinglePartDrawing && precheck.HasCompleteSingleLayout) ||
                         (drawing is AssemblyDrawing && precheck.HasCompleteAssemblyLayout))
                {
                    execution.SectionStatus = AutoSectionStatus.ExistingLayout;
                    execution.SectionMessage = "Section layout da ton tai.";
                }
                else if (!precheck.IsValid)
                {
                    execution.SectionStatus = AutoSectionStatus.HoleCheckUnknown;
                    execution.SectionMessage = precheck.Message;
                }
                else if (!precheck.HasTopBottomDifference)
                {
                    execution.SectionStatus = AutoSectionStatus.HolesSame;
                    execution.SectionMessage = precheck.Message;
                }
                else
                {
                    sectionWorkerInvoked = true;
                    Tekla.Technology.Akit.UserScript
                        .SectionViewAttributeResolution sectionAttributeResolution =
                            Tekla.Technology.Akit.UserScript
                                .SectionViewAttributeResolver.Resolve(
                                    drawing,
                                    model);
                    Tekla.Technology.Akit.UserScript.AutoSectionWorkerResult workerResult;

                    if (sectionAttributeResolution == null ||
                        !sectionAttributeResolution.Success ||
                        string.IsNullOrWhiteSpace(
                            sectionAttributeResolution.AttributeName))
                    {
                        workerResult =
                            new Tekla.Technology.Akit.UserScript
                                .AutoSectionWorkerResult();
                        workerResult.Status =
                            Tekla.Technology.Akit.UserScript
                                .AutoSectionWorkerStatus.PreflightFailed;
                        workerResult.Message =
                            "Khong xac dinh duoc Section view property sau " +
                            "khi precheck ket luan can tao Section. " +
                            (sectionAttributeResolution == null
                                ? "Resolver did not run."
                                : sectionAttributeResolution.Error);
                    }
                    else if (drawing is SinglePartDrawing)
                    {
                        workerResult = Tekla.Technology.Akit.UserScript.SectionScript.RunSingleSafe(
                            drawing,
                            model,
                            part,
                            precheck.TopView,
                            precheck.FrontView,
                            sectionAttributeResolution.AttributeName);
                    }
                    else if (drawing is AssemblyDrawing)
                    {
                        workerResult = Tekla.Technology.Akit.UserScript.SectionScript.RunAssemblySafe(
                            drawing,
                            model,
                            part,
                            precheck.TopView,
                            precheck.FrontView,
                            sectionAttributeResolution.AttributeName);
                    }
                    else
                    {
                        workerResult = new Tekla.Technology.Akit.UserScript.AutoSectionWorkerResult();
                        workerResult.Status =
                            Tekla.Technology.Akit.UserScript.AutoSectionWorkerStatus.PreflightFailed;
                        workerResult.Message = "Drawing khong phai Single Part hoac Assembly.";
                    }

                    ApplyAutoSectionWorkerResult(execution, workerResult);

                    if (workerResult != null &&
                        workerResult.Status ==
                            Tekla.Technology.Akit.UserScript.AutoSectionWorkerStatus.CreatedSingle)
                    {
                        autoSectionDimPassRequired = true;
                        autoSectionSingleLayout = true;
                    }
                    else if (workerResult != null &&
                             workerResult.Status ==
                                Tekla.Technology.Akit.UserScript.AutoSectionWorkerStatus.CreatedAssemblyBottom)
                    {
                        autoSectionDimPassRequired = true;
                    }
                }

                if (!execution.CanSaveDrawing)
                    return execution;

                if (sectionWorkerInvoked &&
                    selectedShapeAssemblyPartId > 0 &&
                    !RestoreSelectedDrawingPartByModelId(selectedShapeAssemblyPartId))
                {
                    if (autoSectionDimPassRequired)
                    {
                        execution.SectionMessage +=
                            " Shape DIM se dung selected ModelPart ID da capture.";
                    }
                    else
                    {
                        execution.SectionStatus = AutoSectionStatus.CreateFailed;
                        execution.SectionMessage =
                            "Khong restore duoc selected Drawing.Part sau Auto Section.";
                        execution.CanSaveDrawing = false;
                        return execution;
                    }
                }
            }

            switch (partType)
            {
                case AutoDimPartType.Plate:
                    Tekla.Technology.Akit.UserScript.Script.Run(null);
                    break;

                case AutoDimPartType.ShapeIH:
                    if (autoSectionDimPassRequired)
                    {
                        Tekla.Technology.Akit.UserScript.ShapeScript
                            .PrepareAutoSectionDimPass(
                                autoSectionSingleLayout,
                                selectedShapeAssemblyPartId);
                    }

                    Tekla.Technology.Akit.UserScript.ShapeScript.Run(null);
                    break;

                case AutoDimPartType.ShapeC:
                    RunOptionalShapeScriptByClassName(
                        "ShapeCScript",
                        "Đã nhận diện profile thép C, nhưng thuật toán thép C chưa được build.");
                    break;

                case AutoDimPartType.ShapeL:
                    RunOptionalShapeScriptByClassName(
                        "ShapeLScript",
                        "Đã nhận diện profile thép L, nhưng thuật toán thép L chưa được build.");
                    break;

                case AutoDimPartType.ShapeBox:
                    RunOptionalShapeScriptByClassName(
                        "ShapeBoxScript",
                        "Đã nhận diện profile thép hộp, nhưng thuật toán thép hộp chưa được build.");
                    break;

                case AutoDimPartType.ShapeUnknown:
                {
                    DrawingHandler unknownDrawingHandler = new DrawingHandler();
                    Drawing unknownDrawing = unknownDrawingHandler.GetActiveDrawing();
                    Model unknownModel = new Model();
                    Tekla.Structures.Model.Part unknownPart =
                        ResolveAutoSectionModelPart(
                            unknownDrawing,
                            unknownModel,
                            selectedShapeAssemblyPartId);

                    Tekla.Technology.Akit.UserScript.ShapeUnknownRunResult unknownResult =
                        Tekla.Technology.Akit.UserScript.ShapeUnknownScript.RunSafe(
                            unknownDrawing,
                            unknownModel,
                            unknownPart);

                    if (unknownResult == null || !unknownResult.Success)
                    {
                        execution.SectionMessage = unknownResult != null
                            ? unknownResult.Message
                            : "Shape Unknown khong tra ket qua.";
                        execution.CanSaveDrawing = false;
                        return execution;
                    }

                    execution.SectionMessage = unknownResult.Message;
                    break;
                }
            }

            if (!runAutoSection)
                execution.OriginalHoleResult = GetTopBottomHoleCheckResult();

            return execution;
        }

        private bool ShouldRunAutoSection(AutoDimPartType partType)
        {
            bool enabled = _isBatchRunning && _batchAutoSectionEnabledSnapshot.HasValue
                ? _batchAutoSectionEnabledSnapshot.Value
                : _autoSectionEnabled;

            return enabled && partType == AutoDimPartType.ShapeIH;
        }

        private Tekla.Structures.Model.Part ResolveAutoSectionModelPart(
            Drawing drawing,
            Model model,
            int selectedShapeAssemblyPartId)
        {
            try
            {
                if (model == null || !model.GetConnectionStatus())
                    return null;

                if (selectedShapeAssemblyPartId > 0)
                {
                    Tekla.Structures.Model.ModelObject selectedObject =
                        model.SelectModelObject(
                            new Tekla.Structures.Identifier(selectedShapeAssemblyPartId));

                    Tekla.Structures.Model.Part selectedPart =
                        selectedObject as Tekla.Structures.Model.Part;

                    if (selectedPart != null)
                        return selectedPart;
                }

                return GetMainModelPartFromDrawing(drawing);
            }
            catch
            {
                return null;
            }
        }

        private void ApplyAutoSectionWorkerResult(
            AutoDimExecutionResult execution,
            Tekla.Technology.Akit.UserScript.AutoSectionWorkerResult workerResult)
        {
            if (execution == null || workerResult == null)
                return;

            execution.SectionMessage = workerResult.Message ?? "";

            switch (workerResult.Status)
            {
                case Tekla.Technology.Akit.UserScript.AutoSectionWorkerStatus.CreatedSingle:
                    execution.SectionStatus = AutoSectionStatus.CreatedSingle;
                    break;

                case Tekla.Technology.Akit.UserScript.AutoSectionWorkerStatus.CreatedAssemblyBottom:
                    execution.SectionStatus = AutoSectionStatus.CreatedAssemblyBottom;
                    break;

                case Tekla.Technology.Akit.UserScript.AutoSectionWorkerStatus.PreflightFailed:
                    execution.SectionStatus = AutoSectionStatus.PreflightFailed;
                    break;

                case Tekla.Technology.Akit.UserScript.AutoSectionWorkerStatus.CreateFailed:
                    execution.SectionStatus = AutoSectionStatus.CreateFailed;
                    break;

                case Tekla.Technology.Akit.UserScript.AutoSectionWorkerStatus.RolledBack:
                    execution.SectionStatus = AutoSectionStatus.RolledBack;
                    break;

                case Tekla.Technology.Akit.UserScript.AutoSectionWorkerStatus.UnsafeRollbackFailed:
                    execution.SectionStatus = AutoSectionStatus.UnsafeRollbackFailed;
                    execution.CanSaveDrawing = false;
                    break;
            }

            if (!workerResult.IsSafeToContinue)
                execution.CanSaveDrawing = false;
        }

        private void ApplyActiveAutoDimExecutionStatus(
            AutoDimExecutionResult execution)
        {
            if (execution == null)
            {
                lblStatus.Text = "AutoDim khong tra ket qua.";
                lblStatus.ForeColor = Color.FromArgb(220, 38, 38);
                return;
            }

            if (!execution.CanSaveDrawing ||
                execution.SectionStatus == AutoSectionStatus.UnsafeRollbackFailed)
            {
                lblStatus.Text = !string.IsNullOrWhiteSpace(execution.SectionMessage)
                    ? "ERROR | " + execution.SectionMessage + " | KHONG SAVE"
                    : "ERROR | Rollback/selection unsafe | KHONG SAVE";
                lblStatus.ForeColor = Color.FromArgb(220, 38, 38);
                return;
            }

            switch (execution.SectionStatus)
            {
                case AutoSectionStatus.CreatedSingle:
                    lblStatus.Text = "Done | Single Section B/C created";
                    lblStatus.ForeColor = Color.FromArgb(22, 163, 74);
                    return;

                case AutoSectionStatus.CreatedAssemblyBottom:
                    lblStatus.Text = "Done | Assembly Bottom Section created";
                    lblStatus.ForeColor = Color.FromArgb(22, 163, 74);
                    return;

                case AutoSectionStatus.ExistingLayout:
                    lblStatus.Text = "Done | Section layout already exists";
                    lblStatus.ForeColor = Color.FromArgb(22, 163, 74);
                    return;

                case AutoSectionStatus.HoleCheckUnknown:
                    lblStatus.Text = "Done AutoDim | Hole check unknown";
                    lblStatus.ForeColor = Color.DarkOrange;
                    return;

                case AutoSectionStatus.PartialLayout:
                    lblStatus.Text = "Done AutoDim | Partial Section layout - no repair";
                    lblStatus.ForeColor = Color.DarkOrange;
                    return;

                case AutoSectionStatus.PreflightFailed:
                case AutoSectionStatus.CreateFailed:
                    lblStatus.Text = "Done AutoDim | Section failed";
                    lblStatus.ForeColor = Color.DarkOrange;
                    return;

                case AutoSectionStatus.RolledBack:
                    lblStatus.Text = "Done AutoDim | Section failed, rollback OK";
                    lblStatus.ForeColor = Color.DarkOrange;
                    return;

                case AutoSectionStatus.Disabled:
                    if (execution.OriginalHoleResult == 1)
                    {
                        lblStatus.Text = "TOP/BOTTOM KHAC LO | Auto Section OFF";
                        lblStatus.ForeColor = Color.DarkOrange;
                        return;
                    }
                    break;

                case AutoSectionStatus.NotApplicable:
                    if (execution.OriginalHoleResult == 1)
                    {
                        lblStatus.Text = "TOP/BOTTOM KHAC LO";
                        lblStatus.ForeColor = Color.DarkOrange;
                        return;
                    }
                    break;
            }

            lblStatus.Text = "Done active drawing";
            lblStatus.ForeColor = Color.FromArgb(22, 163, 74);
        }

        private string GetBatchAutoDimResultText(
            AutoDimExecutionResult execution,
            out Color color)
        {
            color = Color.FromArgb(22, 163, 74);

            if (execution == null || !execution.CanSaveDrawing ||
                execution.SectionStatus == AutoSectionStatus.UnsafeRollbackFailed)
            {
                color = Color.FromArgb(220, 38, 38);
                return "ERROR";
            }

            switch (execution.SectionStatus)
            {
                case AutoSectionStatus.CreatedSingle:
                    return "SECTION B/C";

                case AutoSectionStatus.CreatedAssemblyBottom:
                    return "BOTTOM SECTION";

                case AutoSectionStatus.ExistingLayout:
                    return "SECTION EXISTS";

                case AutoSectionStatus.Disabled:
                    if (execution.OriginalHoleResult == 1)
                    {
                        color = Color.DarkOrange;
                        return "SECTION OFF";
                    }
                    return "OK";

                case AutoSectionStatus.HoleCheckUnknown:
                    color = Color.DarkOrange;
                    return "CHECK UNKNOWN";

                case AutoSectionStatus.PartialLayout:
                case AutoSectionStatus.PreflightFailed:
                case AutoSectionStatus.CreateFailed:
                case AutoSectionStatus.RolledBack:
                    color = Color.DarkOrange;
                    return "SECTION FAIL";

                case AutoSectionStatus.NotApplicable:
                    if (execution.OriginalHoleResult == 1)
                    {
                        color = Color.DarkOrange;
                        return "TOP/BOTTOM KHAC";
                    }
                    return "OK";

                default:
                    return "OK";
            }
        }

        private bool IsBatchAutoDimFailure(AutoDimExecutionResult execution)
        {
            if (execution == null || !execution.CanSaveDrawing)
                return true;

            return execution.SectionStatus == AutoSectionStatus.PartialLayout ||
                   execution.SectionStatus == AutoSectionStatus.PreflightFailed ||
                   execution.SectionStatus == AutoSectionStatus.CreateFailed ||
                   execution.SectionStatus == AutoSectionStatus.RolledBack ||
                   execution.SectionStatus == AutoSectionStatus.UnsafeRollbackFailed;
        }

        private int CaptureSelectedShapeAssemblyPartId(AutoDimPartType partType)
        {
            if (partType != AutoDimPartType.ShapeIH &&
                partType != AutoDimPartType.ShapeC &&
                partType != AutoDimPartType.ShapeL &&
                partType != AutoDimPartType.ShapeBox &&
                partType != AutoDimPartType.ShapeUnknown)
                return 0;

            try
            {
                DrawingHandler drawingHandler = new DrawingHandler();
                Drawing drawing = drawingHandler.GetActiveDrawing();
                if (!(drawing is AssemblyDrawing))
                    return 0;

                DrawingObjectEnumerator selected =
                    drawingHandler.GetDrawingObjectSelector().GetSelected();

                while (selected != null && selected.MoveNext())
                {
                    Tekla.Structures.Drawing.Part drawingPart =
                        selected.Current as Tekla.Structures.Drawing.Part;

                    if (drawingPart != null && drawingPart.ModelIdentifier != null)
                        return drawingPart.ModelIdentifier.ID;
                }
            }
            catch
            {
            }

            return 0;
        }

        private bool RestoreSelectedDrawingPartByModelId(int modelId)
        {
            if (modelId <= 0)
                return false;

            try
            {
                DrawingHandler drawingHandler = new DrawingHandler();
                Drawing drawing = drawingHandler.GetActiveDrawing();
                if (!(drawing is AssemblyDrawing))
                    return false;

                ContainerView sheet = drawing.GetSheet();
                if (sheet == null)
                    return false;

                DrawingObjectEnumerator views = sheet.GetAllViews();
                while (views != null && views.MoveNext())
                {
                    Tekla.Structures.Drawing.View view =
                        views.Current as Tekla.Structures.Drawing.View;
                    if (view == null)
                        continue;

                    DrawingObjectEnumerator parts =
                        view.GetAllObjects(typeof(Tekla.Structures.Drawing.Part));

                    while (parts != null && parts.MoveNext())
                    {
                        Tekla.Structures.Drawing.Part drawingPart =
                            parts.Current as Tekla.Structures.Drawing.Part;

                        if (drawingPart == null || drawingPart.ModelIdentifier == null)
                            continue;

                        if (drawingPart.ModelIdentifier.ID != modelId)
                            continue;

                        System.Collections.ArrayList partToSelect =
                            new System.Collections.ArrayList();
                        partToSelect.Add(drawingPart);

                        return drawingHandler
                            .GetDrawingObjectSelector()
                            .SelectObjects(partToSelect, false);
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        private int GetTopBottomHoleCheckResult()
        {
            try
            {
                AutoDimPartType partType = DetectActiveDrawingAutoDimPartType();

                switch (partType)
                {
                    case AutoDimPartType.ShapeIH:
                        return Tekla.Technology.Akit.UserScript.ShapeScript.TopBottomHoleCheckResult;

                    case AutoDimPartType.ShapeC:
                        return Tekla.Technology.Akit.UserScript.ShapeCScript.TopBottomHoleCheckResult;

                    case AutoDimPartType.ShapeL:
                        return Tekla.Technology.Akit.UserScript.ShapeLScript.TopBottomHoleCheckResult;

                    case AutoDimPartType.ShapeBox:
                        return Tekla.Technology.Akit.UserScript.ShapeBoxScript.TopBottomHoleCheckResult;
                }
            }
            catch
            {
            }

            return 0;
        }


        private AutoDimPartType DetectActiveDrawingAutoDimPartType()
        {
            try
            {
                Tekla.Structures.Model.Part selectedUnknownPart =
                    GetSelectedAssemblyUnknownModelPartForAutoDim();

                if (selectedUnknownPart != null)
                    return AutoDimPartType.ShapeUnknown;

                Tekla.Structures.Model.Part part = GetActiveDrawingMainModelPart();
                if (part == null)
                    return AutoDimPartType.Unknown;

                if (IsPlatePart(part))
                    return AutoDimPartType.Plate;

                ShapeProfileType shapeType = DetectShapeProfile(part);

                if (shapeType == ShapeProfileType.IH)
                    return AutoDimPartType.ShapeIH;

                if (shapeType == ShapeProfileType.C)
                    return AutoDimPartType.ShapeC;

                if (shapeType == ShapeProfileType.L)
                    return AutoDimPartType.ShapeL;

                if (shapeType == ShapeProfileType.Box)
                    return AutoDimPartType.ShapeBox;

                return AutoDimPartType.ShapeUnknown;
            }
            catch
            {
                return AutoDimPartType.Unknown;
            }
        }

        private Tekla.Structures.Model.Part GetSelectedAssemblyUnknownModelPartForAutoDim()
        {
            try
            {
                DrawingHandler drawingHandler = new DrawingHandler();
                Drawing drawing = drawingHandler.GetActiveDrawing();
                if (!(drawing is AssemblyDrawing))
                    return null;

                Model model = new Model();
                if (!model.GetConnectionStatus())
                    return null;

                DrawingObjectEnumerator selected =
                    drawingHandler.GetDrawingObjectSelector().GetSelected();

                while (selected != null && selected.MoveNext())
                {
                    Tekla.Structures.Drawing.Part drawingPart =
                        selected.Current as Tekla.Structures.Drawing.Part;

                    if (drawingPart == null || drawingPart.ModelIdentifier == null)
                        continue;

                    Tekla.Structures.Model.Part modelPart =
                        model.SelectModelObject(drawingPart.ModelIdentifier)
                        as Tekla.Structures.Model.Part;

                    if (modelPart == null || IsPlatePart(modelPart))
                        continue;

                    if (DetectShapeProfile(modelPart) == ShapeProfileType.Unknown)
                        return modelPart;
                }
            }
            catch
            {
            }

            return null;
        }

        private Tekla.Structures.Model.Part GetActiveDrawingMainModelPart()
        {
            try
            {
                DrawingHandler dh = new DrawingHandler();
                Drawing drawing = dh.GetActiveDrawing();
                if (drawing == null)
                    return null;

                return GetMainModelPartFromDrawing(drawing);
            }
            catch
            {
                return null;
            }
        }

        private Tekla.Structures.Model.Part GetMainModelPartFromDrawing(Drawing drawing)
        {
            try
            {
                if (drawing == null)
                    return null;

                Model model = new Model();
                if (!model.GetConnectionStatus())
                    return null;

                SinglePartDrawing spDrawing = drawing as SinglePartDrawing;
                if (spDrawing != null)
                {
                    Tekla.Structures.Model.ModelObject mo =
                        model.SelectModelObject(spDrawing.PartIdentifier);

                    Tekla.Structures.Model.Part part =
                        mo as Tekla.Structures.Model.Part;

                    if (part != null)
                        return part;
                }

                AssemblyDrawing assemblyDrawing = drawing as AssemblyDrawing;
                if (assemblyDrawing != null)
                {
                    Tekla.Structures.Model.Part mainPart =
                        TryGetAssemblyDrawingMainPart(model, assemblyDrawing);

                    if (mainPart != null)
                        return mainPart;
                }

                // Fallback chung cho cả Single/Assembly:
                // quét các Drawing.Part trong các view và lấy model part lớn nhất.
                // Cách này giúp nhận diện được cả bản vẽ Assembly khi AssemblyIdentifier
                // không đọc được hoặc Tekla không trả main part trực tiếp.
                return FindLargestModelPartVisibleInDrawing(model, drawing);
            }
            catch
            {
                return null;
            }
        }

        private Tekla.Structures.Model.Part TryGetAssemblyDrawingMainPart(
            Model model,
            AssemblyDrawing assemblyDrawing)
        {
            try
            {
                if (model == null || assemblyDrawing == null)
                    return null;

                Tekla.Structures.Identifier assemblyIdentifier =
                    GetDrawingIdentifierByReflection(
                        assemblyDrawing,
                        "AssemblyIdentifier");

                if (assemblyIdentifier == null)
                    assemblyIdentifier =
                        GetDrawingIdentifierByReflection(
                            assemblyDrawing,
                            "ModelIdentifier");

                if (assemblyIdentifier == null)
                    return null;

                Tekla.Structures.Model.ModelObject mo =
                    model.SelectModelObject(assemblyIdentifier);

                Tekla.Structures.Model.Part directPart =
                    mo as Tekla.Structures.Model.Part;

                if (directPart != null)
                    return directPart;

                Tekla.Structures.Model.Assembly modelAssembly =
                    mo as Tekla.Structures.Model.Assembly;

                if (modelAssembly != null)
                {
                    Tekla.Structures.Model.ModelObject mainObject =
                        modelAssembly.GetMainPart() as Tekla.Structures.Model.ModelObject;

                    Tekla.Structures.Model.Part mainPart =
                        mainObject as Tekla.Structures.Model.Part;

                    if (mainPart != null)
                        return mainPart;
                }
            }
            catch
            {
            }

            return null;
        }

        private static Tekla.Structures.Identifier GetDrawingIdentifierByReflection(
            object drawingObject,
            string propertyName)
        {
            try
            {
                if (drawingObject == null || string.IsNullOrEmpty(propertyName))
                    return null;

                PropertyInfo prop =
                    drawingObject.GetType().GetProperty(
                        propertyName,
                        BindingFlags.Public | BindingFlags.Instance);

                if (prop == null || !prop.CanRead)
                    return null;

                object value = prop.GetValue(drawingObject, null);

                return value as Tekla.Structures.Identifier;
            }
            catch
            {
                return null;
            }
        }

        private Tekla.Structures.Model.Part FindLargestModelPartVisibleInDrawing(
            Model model,
            Drawing drawing)
        {
            try
            {
                if (model == null || drawing == null)
                    return null;

                ContainerView sheet = drawing.GetSheet();
                if (sheet == null)
                    return null;

                Dictionary<int, CandidateModelPart> candidates =
                    new Dictionary<int, CandidateModelPart>();

                DrawingObjectEnumerator views = sheet.GetAllViews();

                while (views.MoveNext())
                {
                    Tekla.Structures.Drawing.View view =
                        views.Current as Tekla.Structures.Drawing.View;

                    if (view == null)
                        continue;

                    DrawingObjectEnumerator parts =
                        view.GetAllObjects(typeof(Tekla.Structures.Drawing.Part));

                    while (parts.MoveNext())
                    {
                        Tekla.Structures.Drawing.Part drawingPart =
                            parts.Current as Tekla.Structures.Drawing.Part;

                        if (drawingPart == null || drawingPart.ModelIdentifier == null)
                            continue;

                        Tekla.Structures.Model.ModelObject mo =
                            model.SelectModelObject(drawingPart.ModelIdentifier);

                        Tekla.Structures.Model.Part modelPart =
                            mo as Tekla.Structures.Model.Part;

                        if (modelPart == null)
                            continue;

                        int id = drawingPart.ModelIdentifier.ID;

                        CandidateModelPart candidate;
                        if (!candidates.TryGetValue(id, out candidate))
                        {
                            candidate = new CandidateModelPart();
                            candidate.Part = modelPart;
                            candidate.Score = GetModelPartSizeScore(modelPart);
                            candidate.Count = 0;
                            candidates.Add(id, candidate);
                        }

                        candidate.Count++;
                    }
                }

                CandidateModelPart best = null;
                double bestScore = -1.0;

                foreach (CandidateModelPart candidate in candidates.Values)
                {
                    if (candidate == null || candidate.Part == null)
                        continue;

                    // Part xuất hiện nhiều view hơn được cộng nhẹ, nhưng kích thước vẫn là chính.
                    double score = candidate.Score + candidate.Count * 1000.0;

                    if (best == null || score > bestScore)
                    {
                        best = candidate;
                        bestScore = score;
                    }
                }

                if (best != null)
                    return best.Part;
            }
            catch
            {
            }

            return null;
        }

        private class CandidateModelPart
        {
            public Tekla.Structures.Model.Part Part;
            public double Score;
            public int Count;
        }

        private double GetModelPartSizeScore(Tekla.Structures.Model.Part part)
        {
            try
            {
                if (part == null)
                    return 0.0;

                Solid solid = part.GetSolid();
                if (solid == null)
                    return 0.0;

                Tekla.Structures.Geometry3d.Point min = solid.MinimumPoint;
                Tekla.Structures.Geometry3d.Point max = solid.MaximumPoint;

                double dx = Math.Abs(max.X - min.X);
                double dy = Math.Abs(max.Y - min.Y);
                double dz = Math.Abs(max.Z - min.Z);

                double longest = Math.Max(dx, Math.Max(dy, dz));
                double boxVolume = Math.Max(1.0, dx) * Math.Max(1.0, dy) * Math.Max(1.0, dz);

                return longest * 1000000.0 + boxVolume;
            }
            catch
            {
                return 0.0;
            }
        }

        private bool IsPlatePart(Tekla.Structures.Model.Part part)
        {
            try
            {
                if (part == null)
                    return false;

                string profileType = GetReportPropertyString(part, "PROFILE_TYPE");
                string normalizedType = NormalizeProfileText(profileType);

                if (normalizedType.Contains("PLATE") ||
                    normalizedType.Contains("CONTOURPLATE") ||
                    normalizedType == "B" ||
                    normalizedType == "PL")
                {
                    return true;
                }

                string profile = NormalizeProfileText(GetShapeProfileText(part));

                if (profile.StartsWith("PL") ||
                    profile.StartsWith("PLT") ||
                    profile.StartsWith("FL") ||
                    profile.StartsWith("FB"))
                {
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private string GetReportPropertyString(Tekla.Structures.Model.Part part, string propertyName)
        {
            try
            {
                if (part == null || string.IsNullOrWhiteSpace(propertyName))
                    return "";

                string value = "";
                part.GetReportProperty(propertyName, ref value);

                if (value == null)
                    return "";

                return value.Trim();
            }
            catch
            {
                return "";
            }
        }

        private ShapeProfileType DetectShapeProfile(Tekla.Structures.Model.Part part)
        {
            try
            {
                if (part == null)
                    return ShapeProfileType.Unknown;

                string profile = GetShapeProfileText(part);
                if (string.IsNullOrEmpty(profile))
                    return ShapeProfileType.Unknown;

                string p = NormalizeProfileText(profile);
                if (string.IsNullOrEmpty(p))
                    return ShapeProfileType.Unknown;

                // Chỉ cần chuỗi profile có ký tự □ thì nhận diện là thép hộp,
                // không phụ thuộc chiều rộng, chiều cao hoặc chiều dày profile.
                if (p.Contains("□") ||
                    p.StartsWith("RHS") ||
                    p.StartsWith("SHS") ||
                    p.StartsWith("BOX"))
                {
                    return ShapeProfileType.Box;
                }

                // I/H: để trước H/I thông thường và cả các profile built-up bắt đầu bằng BH.
                if (p.StartsWith("BH") ||
                    p.StartsWith("RH") ||
                    p.StartsWith("HM") ||
                    p.StartsWith("HN") ||
                    p.StartsWith("HW") ||
                    p.StartsWith("H") ||
                    p.StartsWith("I"))
                {
                    return ShapeProfileType.IH;
                }

                // C / Channel.
                if (p.StartsWith("[") ||
                    p.StartsWith("CH") ||
                    p.StartsWith("CHANNEL") ||
                    p.StartsWith("C"))
                {
                    return ShapeProfileType.C;
                }

                // L / Angle.
                if (p.StartsWith("L") ||
                    p.StartsWith("ANGLE"))
                {
                    return ShapeProfileType.L;
                }

                return ShapeProfileType.Unknown;
            }
            catch
            {
                return ShapeProfileType.Unknown;
            }
        }

        private string GetShapeProfileText(Tekla.Structures.Model.Part part)
        {
            try
            {
                string profile = "";
                part.GetReportProperty("PROFILE", ref profile);

                if (!string.IsNullOrEmpty(profile))
                    return profile;
            }
            catch
            {
            }

            try
            {
                object profileObj = GetPropertyValue(part, "Profile");
                object profileString = GetPropertyValue(profileObj, "ProfileString");

                if (profileString != null)
                    return profileString.ToString();
            }
            catch
            {
            }

            return "";
        }

        private string NormalizeProfileText(string profile)
        {
            if (profile == null)
                return "";

            string p = profile.Trim().ToUpperInvariant();
            p = p.Replace(" ", "");
            p = p.Replace("-", "");
            p = p.Replace("_", "");
            p = p.Replace("*", "X");
            return p;
        }

        private void RunOptionalShapeScriptByClassName(
            string className,
            string notReadyMessage)
        {
            try
            {
                string fullName = "Tekla.Technology.Akit.UserScript." + className;
                Type scriptType = Type.GetType(fullName);

                if (scriptType == null)
                {
                    foreach (System.Reflection.Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        scriptType = asm.GetType(fullName);
                        if (scriptType != null)
                            break;
                    }
                }

                if (scriptType == null)
                {
                    SetMainStatus(notReadyMessage, MainStatusKind.Warning);
                    return;
                }

                MethodInfo runMethod = scriptType.GetMethod(
                    "Run",
                    BindingFlags.Public | BindingFlags.Static);

                if (runMethod == null)
                {
                    SetMainStatus(notReadyMessage, MainStatusKind.Warning);
                    return;
                }

                ParameterInfo[] parameters = runMethod.GetParameters();
                if (parameters.Length == 1)
                    runMethod.Invoke(null, new object[] { null });
                else
                    runMethod.Invoke(null, null);
            }
            catch (Exception ex)
            {
                SetMainStatus(
                    "Không chạy được thuật toán Shape tương ứng: " + ex.Message,
                    MainStatusKind.Error);
            }
        }


        private void RunActiveDrawing()
        {
            try
            {
                DrawingHandler dh = new DrawingHandler();
                Drawing activeDrawing = dh.GetActiveDrawing();

                if (activeDrawing == null)
                    throw new Exception("Không có bản vẽ active. Hãy mở drawing trong Tekla Drawing Editor rồi chạy lại.");

                string activeMark = SafeDrawingMark(activeDrawing);
                string activeChanges = SafeDrawingChanges(activeDrawing);

                // ACTIVE cũng phải ưu tiên CHANGES = All Parts Deleted giống Batch.
                // Không xét REV trước, vì bản delete thường có REV nhưng vẫn cần vẽ dấu X.
                if (IsAllPartsDeletedChanges(activeChanges))
                {
                    lblStatus.Text = "▶  Mark deleted active drawing: " + activeMark;
                    lblStatus.ForeColor = Blue;
                    Application.DoEvents();

                    string markerError;
                    if (!TryRunAllPartsDeletedMarker(activeMark, out markerError))
                        throw new Exception(markerError);

                    Thread.Sleep(100);

                    bool savedDeleted = SaveActiveDrawingSafe(dh);
                    if (!savedDeleted)
                        throw new Exception("Không save được drawing sau khi tạo dấu X All Parts Deleted.");

                    lblStatus.Text = "✓  All Parts Deleted marked";
                    lblStatus.ForeColor = Color.FromArgb(22, 163, 74);
                    return;
                }

                lblStatus.Text = "▶  Running active drawing...";
                lblStatus.ForeColor = Blue;
                Application.DoEvents();

                // Chỉ chạy AutoDim 1 lần cho mỗi lần bấm CREATE DRAWING.
                // Bản cũ gọi RunCurrentAutoDimScript() 2 lần nên Tekla tạo/dim drawing lặp lại.
                AutoDimExecutionResult execution = RunCurrentAutoDimScript();

                int holeResult = GetTopBottomHoleCheckResult();
                ApplyActiveAutoDimExecutionStatus(execution);

                if (execution == null && holeResult == 1)
                {
                    lblStatus.Text = "⚠ TOP/BOTTOM KHÁC LỖ";
                    lblStatus.ForeColor = Color.DarkOrange;
                }
                else if (execution == null)
                {
                    lblStatus.Text = "✓  Done active drawing";
                    lblStatus.ForeColor = Color.FromArgb(22, 163, 74);
                }
            }
            catch (Exception ex)
            {
                SetMainStatus(
                    "Active drawing lỗi: " + ex.Message,
                    MainStatusKind.Error);

            }
        }
        private static bool IsAllPartsDeletedChanges(string changes)
        {
            return string.Equals(
                (changes ?? string.Empty).Trim(),
                "All Parts Deleted",
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryRunAllPartsDeletedMarker(string mark, out string error)
        {
            error = "";

            try
            {
                Type markerType = null;

                System.Reflection.Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
                foreach (System.Reflection.Assembly asm in assemblies)
                {
                    if (asm == null)
                        continue;

                    try
                    {
                        markerType = asm.GetType("PHU_AllPartsDeletedMarker", false);

                        if (markerType != null)
                            break;

                        Type[] types = asm.GetTypes();
                        foreach (Type t in types)
                        {
                            if (t != null && string.Equals(t.Name, "PHU_AllPartsDeletedMarker", StringComparison.Ordinal))
                            {
                                markerType = t;
                                break;
                            }
                        }

                        if (markerType != null)
                            break;
                    }
                    catch
                    {
                    }
                }

                if (markerType == null)
                {
                    error = "Thiếu file PHU_AllPartsDeletedMarker.cs trong project.";
                    return false;
                }

                MethodInfo runMethod = markerType.GetMethod(
                    "Run",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new Type[] { typeof(string) },
                    null);

                if (runMethod == null)
                {
                    error = "Không tìm thấy hàm PHU_AllPartsDeletedMarker.Run(string mark).";
                    return false;
                }

                object result = runMethod.Invoke(null, new object[] { mark ?? string.Empty });

                if (result is bool)
                    return (bool)result;

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private void RunBatchDrawings()
        {
            if (_selectedDrawings.Count == 0)
            {
                MessageBox.Show("Chưa load drawing. Hãy chọn drawing trong Document Manager rồi bấm Load Selected Drawings.", "TTSK AutoDim");
                return;
            }

            if (_resumeIndex < 0 || _resumeIndex >= _selectedDrawings.Count)
                _resumeIndex = 0;

            if (_resumeIndex == 0 || !_batchAutoSectionEnabledSnapshot.HasValue)
                _batchAutoSectionEnabledSnapshot = _autoSectionEnabled;

            _isBatchRunning = true;
            _stopRequested = false;
            ApplyAutoSectionSwitchUi();

            btnRun.Enabled = true;
            btnRun.Text = "■  STOP";
            btnLoad.Enabled = false;
            btnCheckScale.Enabled = false;

            DrawingHandler dh = new DrawingHandler();
            int ok = 0;
            int fail = 0;
            int skippedRevision = 0;
            bool paused = false;

            try
            {
                for (int i = _resumeIndex; i < _selectedDrawings.Count; i++)
                {
                    if (_stopRequested)
                    {
                        paused = true;
                        _resumeIndex = i;
                        break;
                    }

                    Drawing dr = _selectedDrawings[i];
                    string name = SafeDrawingMark(dr);
                    string rev = SafeDrawingRevision(dr);
                    string changes = SafeDrawingChanges(dr);

                    // ƯU TIÊN CAO NHẤT:
                    // Nếu CHANGES = All Parts Deleted thì KHÔNG được dừng ở REV.
                    // Trường hợp này phải mở drawing, xóa view, vẽ dấu X + MARK, save rồi mới qua bản tiếp theo.
                    if (IsAllPartsDeletedChanges(changes))
                    {
                        try
                        {
                            lblStatus.Text = "▶  Mark deleted drawing " + (i + 1) + "/" + _selectedDrawings.Count + " : " + name;
                            lblStatus.ForeColor = Blue;

                            SetGridStatusAndResult(
                                i,
                                "DELETE",
                                Color.FromArgb(201, 122, 64),
                                "RUNNING",
                                Color.FromArgb(59, 130, 246));

                            Application.DoEvents();

                            SetActiveDrawingSafe(dh, dr);
                            Thread.Sleep(100);

                            string markerError;
                            if (!TryRunAllPartsDeletedMarker(name, out markerError))
                                throw new Exception(markerError);

                            Thread.Sleep(100);

                            bool savedDeleted = SaveActiveDrawingSafe(dh);
                            if (!savedDeleted)
                                throw new Exception("Không save được drawing sau khi tạo dấu X All Parts Deleted.");

                            Thread.Sleep(100);
                            CloseActiveDrawingSafe(dh);
                            Thread.Sleep(100);

                            ok++;
                            SetGridStatusAndResult(
                                i,
                                "DELETE",
                                Color.FromArgb(201, 122, 64),
                                "MARKED",
                                Color.FromArgb(22, 163, 74));

                            _resumeIndex = i + 1;

                            if (_stopRequested)
                            {
                                paused = true;
                                break;
                            }

                            continue;
                        }
                        catch (Exception)
                        {
                            fail++;
                            SetGridStatusAndResult(
                                i,
                                "DELETE",
                                Color.FromArgb(201, 122, 64),
                                "ERROR",
                                Color.FromArgb(220, 38, 38));

                            try { CloseActiveDrawingSafe(dh); } catch { }

                            _resumeIndex = i + 1;

                            if (_stopRequested)
                            {
                                paused = true;
                                break;
                            }

                            continue;
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(rev))
                    {
                        skippedRevision++;
                        SetGridResult(i, "SKIP", Color.FromArgb(201, 122, 64));
                        _resumeIndex = i + 1;
                        continue;
                    }

                    try
                    {
                        lblStatus.Text = "▶  Running " + (i + 1) + "/" + _selectedDrawings.Count + " : " + name;
                        lblStatus.ForeColor = Blue;
                        SetGridResult(i, "RUNNING", Color.FromArgb(59, 130, 246));
                        Application.DoEvents();

                        SetActiveDrawingSafe(dh, dr);
                        Thread.Sleep(100);

                        AutoDimExecutionResult execution = RunCurrentAutoDimScript();

                        if (execution == null || !execution.CanSaveDrawing)
                        {
                            fail++;
                            SetGridResult(i, "ERROR", Color.FromArgb(220, 38, 38));
                            CloseActiveDrawingWithoutSaveSafe(dh);

                            _resumeIndex = i + 1;

                            if (_stopRequested)
                            {
                                paused = true;
                                break;
                            }

                            continue;
                        }

                        int holeResult = GetTopBottomHoleCheckResult();

                        Thread.Sleep(100);

                        try
                        {
                            Drawing activeDr = dh.GetActiveDrawing();
                            if (activeDr != null)
                            {
                                activeDr.CommitChanges();
                            }
                        }
                        catch { }

                        Thread.Sleep(100);

                        bool saved = SaveActiveDrawingSafe(dh);

                        if (!saved)
                        {
                            fail++;
                            SetGridResult(i, "ERROR", Color.FromArgb(220, 38, 38));
                            try { CloseActiveDrawingSafe(dh); } catch { }

                            _resumeIndex = i + 1;

                            if (_stopRequested)
                            {
                                paused = true;
                                break;
                            }

                            continue;
                        }

                        Thread.Sleep(100);

                        CloseActiveDrawingSafe(dh);
                        Thread.Sleep(100);

                        if (IsBatchAutoDimFailure(execution))
                            fail++;
                        else
                            ok++;

                        Color batchResultColor;
                        string batchResultText = GetBatchAutoDimResultText(
                            execution,
                            out batchResultColor);
                        SetGridResult(i, batchResultText, batchResultColor);

                        if (execution == null && holeResult == 1)
                        {
                            SetGridResult(
                                i,
                                "⚠ TOP/BOTTOM KHÁC",
                                Color.DarkOrange);
                        }
                        else if (execution == null)
                        {
                            SetGridResult(
                                i,
                                "OK",
                                Color.FromArgb(22, 163, 74));
                        }

                        _resumeIndex = i + 1;

                        if (_stopRequested)
                        {
                            paused = true;
                            break;
                        }
                    }
                    catch (Exception)
                    {
                        fail++;
                        SetGridResult(i, "ERROR", Color.FromArgb(220, 38, 38));
                        try { CloseActiveDrawingSafe(dh); } catch { }

                        _resumeIndex = i + 1;

                        if (_stopRequested)
                        {
                            paused = true;
                            break;
                        }
                    }
                }
            }
            finally
            {
                _isBatchRunning = false;
                _stopRequested = false;

                btnRun.Enabled = true;
                btnRun.Text = "▶  CREATE DRAWING";
                btnCheckScale.Enabled = true;
                UpdateModeUi();

                dgvDrawings.ClearSelection();
            }

            if (paused)
            {
                lblStatus.Text =
                    "■  Paused at " + _resumeIndex + "/" + _selectedDrawings.Count +
                    " | Bấm CREATE DRAWING để chạy tiếp";
                lblStatus.ForeColor = Color.DarkOrange;

                return;
            }

            _resumeIndex = 0;
            _batchAutoSectionEnabledSnapshot = null;

            lblStatus.Text = "✓  Batch done: " + ok + " OK, Revision skipped: " + skippedRevision + ", Error: " + fail;
            lblStatus.ForeColor = fail == 0 ? Color.FromArgb(22, 163, 74) : Color.DarkOrange;
        }


        private static string SafeDrawingMark(Drawing dr)
        {
            try
            {
                if (dr == null)
                    return "<null>";

                Tekla.Structures.Model.Model model = new Tekla.Structures.Model.Model();

                SinglePartDrawing sp = dr as SinglePartDrawing;
                if (sp != null)
                {
                    Tekla.Structures.Model.ModelObject obj = model.SelectModelObject(sp.PartIdentifier);
                    string mark = GetModelObjectReportString(obj, "PART_POS");
                    if (!string.IsNullOrWhiteSpace(mark))
                        return CleanDrawingMarkText(mark);

                    mark = GetModelObjectReportString(obj, "ASSEMBLY_POS");
                    if (!string.IsNullOrWhiteSpace(mark))
                        return CleanDrawingMarkText(mark);
                }

                AssemblyDrawing ad = dr as AssemblyDrawing;
                if (ad != null)
                {
                    Tekla.Structures.Identifier assemblyId = GetDrawingIdentifierByReflection(ad, "AssemblyIdentifier");
                    if (assemblyId == null)
                        assemblyId = GetDrawingIdentifierByReflection(ad, "ModelIdentifier");

                    if (assemblyId != null)
                    {
                        Tekla.Structures.Model.ModelObject obj = model.SelectModelObject(assemblyId);

                        string mark = GetModelObjectReportString(obj, "ASSEMBLY_POS");
                        if (!string.IsNullOrWhiteSpace(mark))
                            return CleanDrawingMarkText(mark);

                        Tekla.Structures.Model.Assembly ass = obj as Tekla.Structures.Model.Assembly;
                        if (ass != null)
                        {
                            Tekla.Structures.Model.ModelObject mainObj = ass.GetMainPart() as Tekla.Structures.Model.ModelObject;

                            mark = GetModelObjectReportString(mainObj, "ASSEMBLY_POS");
                            if (!string.IsNullOrWhiteSpace(mark))
                                return CleanDrawingMarkText(mark);

                            mark = GetModelObjectReportString(mainObj, "PART_POS");
                            if (!string.IsNullOrWhiteSpace(mark))
                                return CleanDrawingMarkText(mark);
                        }

                        mark = GetModelObjectReportString(obj, "PART_POS");
                        if (!string.IsNullOrWhiteSpace(mark))
                            return CleanDrawingMarkText(mark);
                    }
                }

                string[] drawingPropNames = new string[]
                {
                    "MARK",
                    "Mark",
                    "PartMark",
                    "AssemblyMark",
                    "DrawingMark",
                    "Title1",
                    "Title 1",
                    "TITLE1"
                };

                foreach (string propName in drawingPropNames)
                {
                    object value = GetPropertyValue(dr, propName);
                    if (value == null)
                        continue;

                    string text = value.ToString();
                    if (!string.IsNullOrWhiteSpace(text))
                        return CleanDrawingMarkText(text);
                }
            }
            catch
            {
            }

            return CleanDrawingMarkText(SafeDrawingName(dr));
        }

        private static string CleanDrawingMarkText(string mark)
        {
            if (string.IsNullOrWhiteSpace(mark))
                return mark;

            mark = mark.Trim();

            while (mark.Length >= 2 &&
                   mark.StartsWith("[") &&
                   mark.EndsWith("]"))
            {
                mark = mark.Substring(1, mark.Length - 2).Trim();
            }

            return mark;
        }


        private static string GetModelObjectReportString(Tekla.Structures.Model.ModelObject obj, string reportName)
        {
            try
            {
                if (obj == null || string.IsNullOrWhiteSpace(reportName))
                    return "";

                string value = "";
                if (obj.GetReportProperty(reportName, ref value))
                {
                    if (!string.IsNullOrWhiteSpace(value))
                        return value.Trim();
                }
            }
            catch
            {
            }

            return "";
        }

        private static string SafeDrawingName(Drawing dr)
        {
            try
            {
                if (dr == null)
                    return "<null>";

                if (dr is SinglePartDrawing sp)
                {
                    Tekla.Structures.Model.Model model =
                        new Tekla.Structures.Model.Model();

                    Tekla.Structures.Model.ModelObject obj =
                        model.SelectModelObject(sp.PartIdentifier);

                    if (obj != null)
                    {
                        string mark = "";

                        if (obj.GetReportProperty("PART_POS", ref mark))
                        {
                            if (!string.IsNullOrWhiteSpace(mark))
                                return mark;
                        }
                    }
                }

                string name = dr.Name;
                if (!string.IsNullOrEmpty(name))
                    return name;
            }
            catch
            {
            }

            return dr.GetType().Name;
        }

        private static object InvokeNoArg(object obj, string methodName)
        {
            if (obj == null) return null;
            MethodInfo m = obj.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);
            if (m == null) return null;
            return m.Invoke(obj, null);
        }

        private static bool MoveNext(object enumerator)
        {
            MethodInfo m = enumerator.GetType().GetMethod("MoveNext", BindingFlags.Public | BindingFlags.Instance);
            return m != null && (bool)m.Invoke(enumerator, null);
        }

        private static object GetCurrent(object enumerator)
        {
            PropertyInfo p = enumerator.GetType().GetProperty("Current", BindingFlags.Public | BindingFlags.Instance);
            return p == null ? null : p.GetValue(enumerator, null);
        }

        private static void SetActiveDrawingSafe(DrawingHandler dh, Drawing dr)
        {
            MethodInfo[] methods = dh.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance);
            foreach (MethodInfo m in methods)
            {
                if (m.Name != "SetActiveDrawing") continue;
                ParameterInfo[] ps = m.GetParameters();
                if (ps.Length >= 1 && ps[0].ParameterType.IsAssignableFrom(typeof(Drawing)))
                {
                    object[] args = new object[ps.Length];
                    args[0] = dr;
                    for (int i = 1; i < ps.Length; i++)
                    {
                        if (ps[i].ParameterType == typeof(bool)) args[i] = true;
                        else args[i] = Type.Missing;
                    }
                    m.Invoke(dh, args);
                    return;
                }
            }
            throw new Exception("Không tìm thấy SetActiveDrawing phù hợp.");
        }

        private static bool SaveActiveDrawingSafe(DrawingHandler dh)
        {
            try
            {
                if (dh == null)
                    return false;

                MethodInfo m = dh.GetType().GetMethod(
                    "SaveActiveDrawing",
                    BindingFlags.Public | BindingFlags.Instance
                );

                if (m == null)
                    return false;

                object result = m.Invoke(dh, null);

                if (result is bool)
                    return (bool)result;

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string SafeDrawingChanges(Drawing dr)
        {
            try
            {
                if (dr == null)
                    return "";

                // 1) Đọc trực tiếp property của Drawing trước.
                // Tekla ở mỗi bản có thể đặt tên hơi khác nhau nên dùng reflection an toàn.
                string[] propertyNames = new string[]
                {
                    "Changes",
                    "Change",
                    "Changed",
                    "DrawingChanges",
                    "UpToDateStatus",
                    "UpToDate",
                    "IsUpToDate",
                    "Status",
                    "DrawingStatus"
                };

                foreach (string propertyName in propertyNames)
                {
                    object propValue = GetPropertyValue(dr, propertyName);
                    string normalized = NormalizeDrawingChangeValue(propValue);

                    if (!string.IsNullOrWhiteSpace(normalized))
                        return normalized;
                }

                // 2) Fallback mạnh hơn: quét toàn bộ property có chữ Change / Status / UpToDate.
                // Mục tiêu là bắt đúng cột Changes trong Document Manager nếu Tekla trả bằng enum/text.
                try
                {
                    PropertyInfo[] props = dr.GetType().GetProperties(
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                    foreach (PropertyInfo prop in props)
                    {
                        if (prop == null || !prop.CanRead)
                            continue;

                        if (prop.GetIndexParameters().Length > 0)
                            continue;

                        string name = prop.Name ?? "";

                        bool looksLikeChanges =
                            name.IndexOf("Change", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            name.IndexOf("Status", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            name.IndexOf("UpToDate", StringComparison.OrdinalIgnoreCase) >= 0;

                        if (!looksLikeChanges)
                            continue;

                        try
                        {
                            object propValue = prop.GetValue(dr, null);
                            string normalized = NormalizeDrawingChangeValue(propValue);

                            if (!string.IsNullOrWhiteSpace(normalized))
                                return normalized;
                        }
                        catch
                        {
                        }
                    }
                }
                catch
                {
                }

                // 3) Fallback cuối: thử report property qua Identifier.
                PropertyInfo pi = dr.GetType().GetProperty(
                    "Identifier",
                    BindingFlags.Instance | BindingFlags.NonPublic);

                if (pi == null)
                    return "";

                object rawId = pi.GetValue(dr, null);

                Tekla.Structures.Identifier identifier =
                    rawId as Tekla.Structures.Identifier;

                if (identifier == null)
                    return "";

                Tekla.Structures.Model.Beam dummy =
                    new Tekla.Structures.Model.Beam();

                dummy.Identifier = identifier;

                string value = "";
                string[] stringReportNames = new string[]
                {
                    "CHANGES",
                    "CHANGE",
                    "DRAWING.CHANGES",
                    "DRAWING.CHANGE",
                    "DRAWING_CHANGES",
                    "DRAWING_CHANGE",
                    "DRAWING_STATUS.CHANGES",
                    "DRAWING_STATUS",
                    "STATUS",
                    "UP_TO_DATE",
                    "DRAWING.UP_TO_DATE",
                    "DRAWING_UP_TO_DATE",
                    "IS_UP_TO_DATE",
                    "DRAWING.IS_UP_TO_DATE",
                    "DRAWING_IS_UP_TO_DATE"
                };

                foreach (string reportName in stringReportNames)
                {
                    value = "";

                    try
                    {
                        if (dummy.GetReportProperty(reportName, ref value))
                        {
                            string normalized = NormalizeDrawingChangeValue(value);

                            if (!string.IsNullOrWhiteSpace(normalized))
                                return normalized;
                        }
                    }
                    catch
                    {
                    }
                }

                int intValue = 0;
                string[] intReportNames = new string[]
                {
                    "CHANGES",
                    "DRAWING.CHANGES",
                    "DRAWING_CHANGES",
                    "DRAWING_CHANGE_COUNT",
                    "CHANGE_COUNT",
                    "UP_TO_DATE",
                    "DRAWING_UP_TO_DATE"
                };

                foreach (string reportName in intReportNames)
                {
                    intValue = 0;

                    try
                    {
                        if (dummy.GetReportProperty(reportName, ref intValue))
                        {
                            string normalized = NormalizeDrawingChangeValue(intValue);

                            if (!string.IsNullOrWhiteSpace(normalized))
                                return normalized;
                        }
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }

            return "";
        }

        private static string NormalizeDrawingChangeValue(object rawValue)
        {
            if (rawValue == null)
                return "";

            try
            {
                if (rawValue is bool)
                    return ((bool)rawValue) ? "Changed" : "";

                if (rawValue is int ||
                    rawValue is double ||
                    rawValue is float ||
                    rawValue is decimal)
                {
                    double number = Convert.ToDouble(rawValue);

                    if (Math.Abs(number) < 0.0001)
                        return "";

                    return rawValue.ToString();
                }
            }
            catch
            {
            }

            string text = rawValue.ToString();

            if (string.IsNullOrWhiteSpace(text))
                return "";

            text = text.Trim();

            if (text == "0" ||
                text == "-" ||
                string.Equals(text, "False", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "None", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "No", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "Unknown", StringComparison.OrdinalIgnoreCase))
            {
                return "";
            }

            if (string.Equals(text, "True", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "Yes", StringComparison.OrdinalIgnoreCase))
            {
                return "Changed";
            }

            return CleanTeklaEnumText(text);
        }

        private static string CleanTeklaEnumText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "";

            text = text.Trim();

            int lastDot = text.LastIndexOf('.');
            if (lastDot >= 0 && lastDot < text.Length - 1)
                text = text.Substring(lastDot + 1);

            text = text.Replace("Drawing", "");
            text = text.Replace("drawing", "");
            text = text.Replace("UpToDate", "Up to date");
            text = text.Replace("upToDate", "Up to date");
            text = text.Replace("NotUpToDate", "Not up to date");
            text = text.Replace("notUpToDate", "Not up to date");

            // Tách CamelCase để dễ đọc hơn: PartsModified -> Parts Modified
            text = System.Text.RegularExpressions.Regex.Replace(
                text,
                "(?<=[a-z])(?=[A-Z])",
                " ");

            text = text.Replace("_", " ");
            text = System.Text.RegularExpressions.Regex.Replace(text, "\\s+", " ").Trim();

            return text;
        }

        private static string SafeDrawingRevision(Drawing dr)
        {
            try
            {
                if (dr == null)
                    return "";

                PropertyInfo pi = dr.GetType().GetProperty(
                    "Identifier",
                    BindingFlags.Instance | BindingFlags.NonPublic);

                if (pi == null)
                    return "";

                object rawId = pi.GetValue(dr, null);

                Tekla.Structures.Identifier identifier =
                    rawId as Tekla.Structures.Identifier;

                if (identifier == null)
                    return "";

                Tekla.Structures.Model.Beam dummy =
                    new Tekla.Structures.Model.Beam();

                dummy.Identifier = identifier;

                int revNo = 0;

                string[] intReportNames = new string[]
                {
            "REVISION.LAST_NUMBER",
            "REVISION.NUMBER",
            "LAST_REVISION_NUMBER",
            "DRAWING.REVISION.NUMBER"
                };

                foreach (string reportName in intReportNames)
                {
                    revNo = 0;

                    try
                    {
                        if (dummy.GetReportProperty(reportName, ref revNo))
                        {
                            if (revNo > 0)
                                return revNo.ToString();
                        }
                    }
                    catch
                    {
                    }
                }

                string value = "";

                string[] stringReportNames = new string[]
                {
            "REVISION.LAST_NUMBER",
            "REVISION.NUMBER",
            "REVISION.LAST",
            "REVISION.MARK",
            "REVISION.LAST_MARK",
            "REVISION"
                };

                foreach (string reportName in stringReportNames)
                {
                    value = "";

                    try
                    {
                        if (dummy.GetReportProperty(reportName, ref value))
                        {
                            if (!string.IsNullOrWhiteSpace(value))
                            {
                                value = value.Trim();

                                if (value != "0")
                                    return value;
                            }
                        }
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }

            return "";
        }

        private static void CloseActiveDrawingSafe(DrawingHandler dh)
        {
            MethodInfo[] methods = dh.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance);
            foreach (MethodInfo m in methods)
            {
                if (m.Name != "CloseActiveDrawing") continue;
                ParameterInfo[] ps = m.GetParameters();
                object[] args = new object[ps.Length];
                for (int i = 0; i < ps.Length; i++)
                {
                    if (ps[i].ParameterType == typeof(bool)) args[i] = true;
                    else args[i] = Type.Missing;
                }
                m.Invoke(dh, args);
                return;
            }
        }

        private static void CloseActiveDrawingWithoutSaveSafe(DrawingHandler dh)
        {
            if (dh == null)
                return;

            MethodInfo[] methods = dh.GetType().GetMethods(
                BindingFlags.Public | BindingFlags.Instance);

            foreach (MethodInfo method in methods)
            {
                if (method.Name != "CloseActiveDrawing")
                    continue;

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length == 1 &&
                    parameters[0].ParameterType == typeof(bool))
                {
                    method.Invoke(dh, new object[] { false });
                    return;
                }
            }
        }

        private class CleanDataGridView : DataGridView
        {
            public bool DarkMode { get; set; }
            public bool DrawSoftOuterBorder { get; set; }
            public Color SoftOuterBorderColor { get; set; }

            public CleanDataGridView()
            {
                DoubleBuffered = true;
                ScrollBars = ScrollBars.None;
                DrawSoftOuterBorder = false;
                SoftOuterBorderColor = Color.FromArgb(160, 165, 170);

                SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
                SetStyle(ControlStyles.AllPaintingInWmPaint, true);
                SetStyle(ControlStyles.ResizeRedraw, true);
            }

            protected override void OnMouseWheel(MouseEventArgs e)
            {
                try
                {
                    int visibleRows = Math.Max(1, DisplayedRowCount(false));
                    int maxFirst = Math.Max(0, RowCount - visibleRows);

                    int current = 0;
                    try
                    {
                        current = FirstDisplayedScrollingRowIndex;
                    }
                    catch
                    {
                        current = 0;
                    }

                    int step = e.Delta < 0 ? 3 : -3;
                    int next = current + step;

                    if (next < 0)
                        next = 0;

                    if (next > maxFirst)
                        next = maxFirst;

                    if (RowCount > 0)
                        FirstDisplayedScrollingRowIndex = next;
                }
                catch
                {
                }

                Invalidate();
                base.OnMouseWheel(e);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);

                if (DrawSoftOuterBorder)
                {
                    try
                    {
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
                    catch
                    {
                    }
                }

                try
                {
                    if (RowCount <= DisplayedRowCount(false))
                        return;

                    int scrollWidth = 12;
                    System.Drawing.Rectangle track = new System.Drawing.Rectangle(
                        Width - scrollWidth - 2,
                        0,
                        scrollWidth + 2,
                        Height);

                    Color trackColor = DarkMode
                        ? Color.FromArgb(18, 18, 18)
                        : Color.FromArgb(241, 245, 249);
                    Color thumbColor = DarkMode
                        ? Color.FromArgb(95, 82, 70)
                        : Color.FromArgb(148, 163, 184);

                    using (SolidBrush trackBrush = new SolidBrush(trackColor))
                    {
                        e.Graphics.FillRectangle(trackBrush, track);
                    }

                    int visibleRows = Math.Max(1, DisplayedRowCount(false));
                    int totalRows = Math.Max(1, RowCount);
                    int thumbHeight = Math.Max(34, (int)(Height * (visibleRows / (double)totalRows)));

                    int first = 0;
                    try
                    {
                        first = FirstDisplayedScrollingRowIndex;
                    }
                    catch
                    {
                        first = 0;
                    }

                    int maxFirst = Math.Max(1, totalRows - visibleRows);
                    int available = Math.Max(1, Height - thumbHeight - 8);
                    int thumbY = 4 + (int)(available * (first / (double)maxFirst));

                    System.Drawing.Rectangle thumb = new System.Drawing.Rectangle(
                        Width - scrollWidth + 1,
                        thumbY,
                        7,
                        thumbHeight);

                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

                    using (GraphicsPath path = RoundedRectF(
                        new RectangleF(thumb.X, thumb.Y, thumb.Width, thumb.Height),
                        5f))
                    using (SolidBrush thumbBrush = new SolidBrush(thumbColor))
                    {
                        e.Graphics.FillPath(thumbBrush, path);
                    }
                }
                catch
                {
                }
            }

            protected override void OnScroll(ScrollEventArgs e)
            {
                base.OnScroll(e);
                Invalidate();
            }

            protected override void OnRowsAdded(DataGridViewRowsAddedEventArgs e)
            {
                base.OnRowsAdded(e);
                Invalidate();
            }

            protected override void OnRowsRemoved(DataGridViewRowsRemovedEventArgs e)
            {
                base.OnRowsRemoved(e);
                Invalidate();
            }
        }


        private class SafeRoundedButton : Control
        {
            private bool _hovered;
            private bool _pressed;
            private bool _keyboardPressed;

            public Color FillColor { get; set; }
            public Color BorderColor { get; set; }
            public Color HoverBorderColor { get; set; }
            public Color TextColor { get; set; }
            public int BorderRadius { get; set; }

            public SafeRoundedButton()
            {
                DoubleBuffered = true;
                SetStyle(ControlStyles.UserPaint, true);
                SetStyle(ControlStyles.AllPaintingInWmPaint, true);
                SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
                SetStyle(ControlStyles.ResizeRedraw, true);
                SetStyle(ControlStyles.Selectable, true);
                TabStop = true;
                Cursor = Cursors.Hand;
                FillColor = Color.FromArgb(30, 58, 138);
                BorderColor = Color.FromArgb(30, 58, 138);
                HoverBorderColor = Color.Empty;
                TextColor = Color.White;
                BorderRadius = 8;
            }

            public void PerformClick()
            {
                if (Enabled)
                    OnClick(EventArgs.Empty);
            }

            protected override void OnPaintBackground(PaintEventArgs e)
            {
                Color background = Parent != null ? Parent.BackColor : BackColor;
                using (SolidBrush brush = new SolidBrush(background))
                    e.Graphics.FillRectangle(brush, ClientRectangle);
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

            protected override void OnMouseDown(MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left && Enabled)
                {
                    Focus();
                    _pressed = true;
                    Capture = true;
                    Invalidate();
                }
                base.OnMouseDown(e);
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left)
                {
                    _pressed = false;
                    Capture = false;
                    Invalidate();
                }
                base.OnMouseUp(e);
            }

            protected override void OnKeyDown(KeyEventArgs e)
            {
                if (Enabled && (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter))
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
                    PerformClick();
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
                _hovered = false;
                _pressed = false;
                _keyboardPressed = false;
                Cursor = Enabled ? Cursors.Hand : Cursors.Default;
                Invalidate();
                base.OnEnabledChanged(e);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
                e.Graphics.CompositingQuality = CompositingQuality.HighQuality;

                Color fill = FillColor;
                Color border = BorderColor;
                Color text = TextColor;

                if (!Enabled)
                {
                    Color parent = Parent != null ? Parent.BackColor : SystemColors.Control;
                    fill = MixButtonColor(FillColor, parent, 0.48);
                    border = MixButtonColor(BorderColor, parent, 0.38);
                    text = MixButtonColor(TextColor, parent, 0.42);
                }
                else if (_pressed || _keyboardPressed)
                {
                    fill = MixButtonColor(FillColor, Color.Black, 0.14);
                }
                else if (_hovered)
                {
                    fill = MixButtonColor(FillColor, Color.White, 0.10);
                    border = HoverBorderColor.IsEmpty
                        ? MixButtonColor(BorderColor, Color.White, 0.14)
                        : HoverBorderColor;
                }

                RectangleF rect = new RectangleF(1f, 1f, Width - 3f, Height - 3f);
                using (GraphicsPath path = RoundedRectF(rect, BorderRadius))
                {
                    using (SolidBrush brush = new SolidBrush(fill))
                        e.Graphics.FillPath(brush, path);

                    using (Pen pen = new Pen(border, 1.1f))
                        e.Graphics.DrawPath(pen, path);

                    GraphicsState state = e.Graphics.Save();
                    e.Graphics.SetClip(path);
                    TextRenderer.DrawText(
                        e.Graphics,
                        Text,
                        Font,
                        System.Drawing.Rectangle.Round(rect),
                        text,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                        TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                    e.Graphics.Restore(state);
                }
            }

            private static Color MixButtonColor(Color a, Color b, double amount)
            {
                int r = (int)Math.Round(a.R + (b.R - a.R) * amount);
                int g = (int)Math.Round(a.G + (b.G - a.G) * amount);
                int bl = (int)Math.Round(a.B + (b.B - a.B) * amount);
                return Color.FromArgb(r, g, bl);
            }
        }


        private class ThemeButton : Button
        {
            private bool _hovered = false;
            private bool _pressed = false;

            public bool UseCustomPaint { get; set; }
            public Color CustomBackColor { get; set; }
            public Color CustomBorderColor { get; set; }
            public Color CustomTextColor { get; set; }
            public Color CustomDisabledBackColor { get; set; }
            public Color CustomDisabledBorderColor { get; set; }
            public Color CustomDisabledTextColor { get; set; }

            public ThemeButton()
            {
                DoubleBuffered = true;
                CustomBackColor = Color.FromArgb(28, 28, 28);
                CustomBorderColor = Color.FromArgb(201, 122, 64);
                CustomTextColor = Color.FromArgb(224, 156, 96);
                CustomDisabledBackColor = Color.FromArgb(22, 22, 22);
                CustomDisabledBorderColor = Color.FromArgb(60, 52, 44);
                CustomDisabledTextColor = Color.FromArgb(226, 232, 240);
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
                _pressed = true;
                Invalidate();
                base.OnMouseDown(mevent);
            }

            protected override void OnMouseUp(MouseEventArgs mevent)
            {
                _pressed = false;
                Invalidate();
                base.OnMouseUp(mevent);
            }

            protected override void OnEnabledChanged(EventArgs e)
            {
                _hovered = false;
                _pressed = false;
                Invalidate();
                base.OnEnabledChanged(e);
            }

            private static Color MixColor(Color a, Color b, double amount)
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

            protected override void OnPaint(PaintEventArgs pevent)
            {
                if (!UseCustomPaint)
                {
                    base.OnPaint(pevent);
                    return;
                }

                pevent.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                pevent.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
                pevent.Graphics.CompositingQuality = CompositingQuality.HighQuality;

                Color back = Enabled ? CustomBackColor : CustomDisabledBackColor;
                Color border = Enabled ? CustomBorderColor : CustomDisabledBorderColor;
                Color text = Enabled ? CustomTextColor : CustomDisabledTextColor;
                float borderWidth = 1.2f;

                if (Enabled && _hovered)
                {
                    // Dark mode hover: nền sáng cam nhẹ + viền/text nổi hơn,
                    // không đổi layout và không đụng logic nút.
                    back = MixColor(CustomBackColor, CustomBorderColor, 0.18);
                    border = MixColor(CustomBorderColor, Color.White, 0.18);
                    text = MixColor(CustomTextColor, Color.White, 0.15);
                    borderWidth = 1.8f;
                }

                if (Enabled && _pressed)
                {
                    back = MixColor(CustomBackColor, Color.Black, 0.18);
                    border = CustomBorderColor;
                    text = CustomTextColor;
                    borderWidth = 1.4f;
                }

                RectangleF rect = new RectangleF(0.8f, 0.8f, Width - 1.6f, Height - 1.6f);

                using (GraphicsPath path = RoundedRectF(rect, 4.5f))
                {
                    using (SolidBrush brush = new SolidBrush(back))
                    {
                        pevent.Graphics.FillPath(brush, path);
                    }

                    using (Pen pen = new Pen(border, borderWidth))
                    {
                        pevent.Graphics.DrawPath(pen, path);
                    }
                }

                TextRenderer.DrawText(
                    pevent.Graphics,
                    Text,
                    Font,
                    ClientRectangle,
                    text,
                    TextFormatFlags.HorizontalCenter |
                    TextFormatFlags.VerticalCenter |
                    TextFormatFlags.EndEllipsis);
            }
        }


        private class Slot04TargetSwitch : Control
        {
            private int _selectedMode = 1;

            public event EventHandler CheckedChanged;
            public bool DarkMode { get; set; }
            public Color AccentColor { get; set; }

            public int SelectedMode
            {
                get { return _selectedMode; }
                set
                {
                    int next = value;
                    if (next < 0) next = 0;
                    if (next > 2) next = 2;

                    if (_selectedMode == next)
                        return;

                    _selectedMode = next;
                    Invalidate();

                    if (CheckedChanged != null)
                        CheckedChanged(this, EventArgs.Empty);
                }
            }

            public Slot04TargetSwitch()
            {
                DoubleBuffered = true;
                Cursor = Cursors.Hand;
                Size = new System.Drawing.Size(58, 20);
                DarkMode = false;
                AccentColor = Color.FromArgb(37, 99, 235);

                SetStyle(ControlStyles.UserPaint, true);
                SetStyle(ControlStyles.AllPaintingInWmPaint, true);
                SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
                SetStyle(ControlStyles.ResizeRedraw, true);
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                if (Enabled)
                {
                    int third = Math.Max(1, Width / 3);
                    int next = Math.Min(2, Math.Max(0, e.X / third));

                    if (_selectedMode == next)
                    {
                        Invalidate();

                        if (CheckedChanged != null)
                            CheckedChanged(this, EventArgs.Empty);
                    }
                    else
                    {
                        SelectedMode = next;
                    }
                }

                base.OnMouseDown(e);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);

                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
                e.Graphics.CompositingQuality = CompositingQuality.HighQuality;

                Color trackBack = DarkMode ? Color.FromArgb(25, 20, 16) : Color.FromArgb(239, 246, 255);
                Color border = Enabled ? AccentColor : (DarkMode ? Color.FromArgb(73, 56, 43) : Color.FromArgb(203, 213, 225));
                Color knob = Enabled ? AccentColor : (DarkMode ? Color.FromArgb(92, 82, 72) : Color.FromArgb(148, 163, 184));
                Color text = Enabled ? (DarkMode ? Color.FromArgb(245, 186, 126) : Color.FromArgb(30, 58, 138)) : (DarkMode ? Color.FromArgb(92, 82, 72) : Color.FromArgb(148, 163, 184));

                RectangleF rect = new RectangleF(1.5f, 1.5f, Width - 3f, Height - 3f);
                using (GraphicsPath path = RoundedRectF(rect, rect.Height / 2f))
                {
                    using (SolidBrush brush = new SolidBrush(trackBack))
                        e.Graphics.FillPath(brush, path);
                    using (Pen pen = new Pen(border, 1.2f))
                        e.Graphics.DrawPath(pen, path);
                }

                float cell = (Width - 6f) / 3f;
                float knobSize = Height - 8f;
                float knobX = 4f + (cell * _selectedMode) + ((cell - knobSize) / 2f);
                float knobY = (Height - knobSize) / 2f;

                using (SolidBrush brush = new SolidBrush(knob))
                    e.Graphics.FillEllipse(brush, knobX, knobY, knobSize, knobSize);

                string[] labels = new string[] { "L", "C", "R" };
                using (Font f = new Font("Segoe UI", 6.2F, FontStyle.Bold))
                using (StringFormat sf = new StringFormat())
                {
                    sf.Alignment = StringAlignment.Center;
                    sf.LineAlignment = StringAlignment.Center;

                    for (int i = 0; i < 3; i++)
                    {
                        RectangleF cellRect = new RectangleF(3f + cell * i, 0f, cell, Height);
                        using (SolidBrush brush = new SolidBrush(i == _selectedMode ? (DarkMode ? Color.FromArgb(20, 16, 14) : Color.White) : text))
                            e.Graphics.DrawString(labels[i], f, brush, cellRect, sf);
                    }
                }
            }
        }


        private class Slot05ModeSwitch : Control
        {
            private int _selectedMode = 0;

            public event EventHandler CheckedChanged;
            public bool DarkMode { get; set; }
            public Color AccentColor { get; set; }

            public int SelectedMode
            {
                get { return _selectedMode; }
                set
                {
                    int next = value;
                    if (next < 0) next = 0;
                    if (next > 1) next = 1;

                    if (_selectedMode == next)
                        return;

                    _selectedMode = next;
                    Invalidate();

                    if (CheckedChanged != null)
                        CheckedChanged(this, EventArgs.Empty);
                }
            }

            public Slot05ModeSwitch()
            {
                SetStyle(ControlStyles.UserPaint, true);
                SetStyle(ControlStyles.AllPaintingInWmPaint, true);
                SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
                SetStyle(ControlStyles.ResizeRedraw, true);
                SetStyle(ControlStyles.SupportsTransparentBackColor, true);

                DoubleBuffered = true;
                Cursor = Cursors.Hand;
                Size = new System.Drawing.Size(50, 22);
                DarkMode = false;
                AccentColor = Color.FromArgb(37, 99, 235);
                BackColor = Color.Transparent;
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                if (Enabled)
                {
                    int half = Math.Max(1, Width / 2);
                    SelectedMode = e.X < half ? 0 : 1;
                }

                base.OnMouseDown(e);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);

                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
                e.Graphics.CompositingQuality = CompositingQuality.HighQuality;

                RectangleF rect = new RectangleF(1.5f, 1.5f, Width - 3f, Height - 3f);
                float radius = rect.Height / 2f;

                Color trackBack = Enabled
                    ? (DarkMode ? Color.FromArgb(210, 24, 20, 17) : Color.FromArgb(225, 255, 255, 255))
                    : (DarkMode ? Color.FromArgb(170, 16, 16, 16) : Color.FromArgb(210, 245, 247, 250));

                Color trackBorder = Enabled
                    ? AccentColor
                    : (DarkMode ? Color.FromArgb(73, 56, 43) : Color.FromArgb(203, 213, 225));

                Color selectedBack = Enabled
                    ? AccentColor
                    : (DarkMode ? Color.FromArgb(92, 82, 72) : Color.FromArgb(148, 163, 184));

                Color normalText = Enabled
                    ? AccentColor
                    : (DarkMode ? Color.FromArgb(92, 82, 72) : Color.FromArgb(148, 163, 184));

                Color selectedText = DarkMode
                    ? Color.FromArgb(20, 16, 14)
                    : Color.White;

                using (GraphicsPath path = RoundedRectF(rect, radius))
                {
                    using (SolidBrush brush = new SolidBrush(trackBack))
                    {
                        e.Graphics.FillPath(brush, path);
                    }

                    using (Pen pen = new Pen(trackBorder, 1.35f))
                    {
                        e.Graphics.DrawPath(pen, path);
                    }
                }

                RectangleF selectedRect;
                if (_selectedMode == 0)
                    selectedRect = new RectangleF(rect.X + 2f, rect.Y + 2f, (rect.Width / 2f) - 2.5f, rect.Height - 4f);
                else
                    selectedRect = new RectangleF(rect.X + (rect.Width / 2f) + 0.5f, rect.Y + 2f, (rect.Width / 2f) - 2.5f, rect.Height - 4f);

                using (GraphicsPath selectedPath = RoundedRectF(selectedRect, selectedRect.Height / 2f))
                using (SolidBrush brush = new SolidBrush(selectedBack))
                {
                    e.Graphics.FillPath(brush, selectedPath);
                }

                RectangleF leftTextRect = new RectangleF(2f, 0f, Width / 2f - 2f, Height);
                RectangleF rightTextRect = new RectangleF(Width / 2f, 0f, Width / 2f - 2f, Height);

                using (Font f = new Font("Segoe UI", 8.5F, FontStyle.Bold))
                using (StringFormat sf = new StringFormat())
                {
                    sf.Alignment = StringAlignment.Center;
                    sf.LineAlignment = StringAlignment.Center;

                    using (SolidBrush brush = new SolidBrush(_selectedMode == 0 ? selectedText : normalText))
                    {
                        e.Graphics.DrawString("1", f, brush, leftTextRect, sf);
                    }

                    using (SolidBrush brush = new SolidBrush(_selectedMode == 1 ? selectedText : normalText))
                    {
                        e.Graphics.DrawString("2", f, brush, rightTextRect, sf);
                    }
                }
            }
        }


        private class ArrangeOrderSwitch : Control
        {
            private bool _checked;

            public event EventHandler CheckedChanged;

            public bool DarkMode { get; set; }
            public Color AccentColor { get; set; }
            public Color BackPanelColor { get; set; }

            public bool Checked
            {
                get { return _checked; }
                set
                {
                    if (_checked == value)
                        return;

                    _checked = value;
                    Invalidate();

                    if (CheckedChanged != null)
                        CheckedChanged(this, EventArgs.Empty);
                }
            }

            public ArrangeOrderSwitch()
            {
                DoubleBuffered = true;
                Cursor = Cursors.Hand;
                Size = new System.Drawing.Size(64, 28);
                DarkMode = false;
                AccentColor = Color.FromArgb(37, 99, 235);
                BackPanelColor = Color.White;

                SetStyle(ControlStyles.UserPaint, true);
                SetStyle(ControlStyles.AllPaintingInWmPaint, true);
                SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
                SetStyle(ControlStyles.ResizeRedraw, true);
            }

            protected override void OnClick(EventArgs e)
            {
                if (Enabled)
                    Checked = !Checked;

                base.OnClick(e);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);

                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
                e.Graphics.CompositingQuality = CompositingQuality.HighQuality;

                RectangleF rect = new RectangleF(1.5f, 1.5f, Width - 3f, Height - 3f);
                float radius = rect.Height / 2f;

                Color trackBack = Enabled
                    ? (DarkMode ? Color.FromArgb(30, 24, 20) : Color.FromArgb(239, 246, 255))
                    : (DarkMode ? Color.FromArgb(16, 16, 16) : Color.FromArgb(245, 247, 250));

                Color trackBorder = Enabled
                    ? AccentColor
                    : (DarkMode ? Color.FromArgb(73, 56, 43) : Color.FromArgb(203, 213, 225));

                Color knobColor = Enabled
                    ? AccentColor
                    : (DarkMode ? Color.FromArgb(92, 82, 72) : Color.FromArgb(148, 163, 184));

                Color iconColor = Enabled
                    ? AccentColor
                    : (DarkMode ? Color.FromArgb(92, 82, 72) : Color.FromArgb(148, 163, 184));

                using (GraphicsPath path = RoundedRectF(rect, radius))
                {
                    using (SolidBrush brush = new SolidBrush(trackBack))
                    {
                        e.Graphics.FillPath(brush, path);
                    }

                    using (Pen pen = new Pen(trackBorder, 1.4f))
                    {
                        e.Graphics.DrawPath(pen, path);
                    }
                }

                float knobSize = Height - 9f;
                float knobX = Checked ? Width - knobSize - 5f : 5f;
                float knobY = (Height - knobSize) / 2f;

                using (SolidBrush brush = new SolidBrush(knobColor))
                {
                    e.Graphics.FillEllipse(brush, knobX, knobY, knobSize, knobSize);
                }

                string icon = Checked ? "△" : "▽";

                RectangleF iconRect = Checked
                    ? new RectangleF(7f, 0f, 26f, Height)
                    : new RectangleF(Width - 33f, 0f, 26f, Height);

                using (Font f = new Font("Segoe UI Symbol", 13F, FontStyle.Bold))
                using (SolidBrush brush = new SolidBrush(iconColor))
                using (StringFormat sf = new StringFormat())
                {
                    sf.Alignment = StringAlignment.Center;
                    sf.LineAlignment = StringAlignment.Center;
                    e.Graphics.DrawString(icon, f, brush, iconRect, sf);
                }
            }
        }


        private class AutoSectionSwitch : Control
        {
            private bool _checked;

            public event EventHandler CheckedChanged;
            public bool DarkMode { get; set; }
            public Color AccentColor { get; set; }

            public bool Checked
            {
                get { return _checked; }
                set
                {
                    if (_checked == value)
                        return;

                    _checked = value;
                    Invalidate();

                    if (CheckedChanged != null)
                        CheckedChanged(this, EventArgs.Empty);
                }
            }

            public AutoSectionSwitch()
            {
                DoubleBuffered = true;
                Cursor = Cursors.Hand;
                Size = new System.Drawing.Size(48, 22);
                AccentColor = Color.FromArgb(37, 99, 235);
                TabStop = true;

                SetStyle(ControlStyles.UserPaint, true);
                SetStyle(ControlStyles.AllPaintingInWmPaint, true);
                SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
                SetStyle(ControlStyles.ResizeRedraw, true);
            }

            protected override void OnClick(EventArgs e)
            {
                if (Enabled)
                    Checked = !Checked;

                base.OnClick(e);
            }

            protected override void OnEnabledChanged(EventArgs e)
            {
                base.OnEnabledChanged(e);
                Cursor = Enabled ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

                RectangleF track = new RectangleF(1f, 1f, Width - 2f, Height - 2f);
                Color offBack = DarkMode
                    ? Color.FromArgb(42, 42, 42)
                    : Color.FromArgb(235, 242, 255);
                Color disabledBack = DarkMode
                    ? Color.FromArgb(28, 28, 28)
                    : Color.FromArgb(241, 245, 249);
                Color fill = Enabled
                    ? (Checked ? AccentColor : offBack)
                    : disabledBack;
                Color border = Enabled
                    ? (Checked ? AccentColor : (DarkMode
                        ? Color.FromArgb(92, 82, 72)
                        : Color.FromArgb(147, 197, 253)))
                    : (DarkMode
                        ? Color.FromArgb(73, 56, 43)
                        : Color.FromArgb(203, 213, 225));

                using (GraphicsPath path = RoundedRectF(track, track.Height / 2f))
                {
                    using (SolidBrush brush = new SolidBrush(fill))
                        e.Graphics.FillPath(brush, path);
                    using (Pen pen = new Pen(border, 1.2f))
                        e.Graphics.DrawPath(pen, path);
                }

                float knobSize = Height - 8f;
                float knobX = Checked ? Width - knobSize - 4f : 4f;
                float knobY = (Height - knobSize) / 2f;
                Color knob = Enabled
                    ? Color.White
                    : (DarkMode
                        ? Color.FromArgb(92, 82, 72)
                        : Color.FromArgb(148, 163, 184));

                using (SolidBrush brush = new SolidBrush(knob))
                    e.Graphics.FillEllipse(brush, knobX, knobY, knobSize, knobSize);
            }
        }

        private class ThemeSwitch : Control
        {
            private bool _checked;

            public event EventHandler CheckedChanged;

            public bool Checked
            {
                get { return _checked; }
                set
                {
                    if (_checked == value)
                        return;

                    _checked = value;
                    Invalidate();

                    if (CheckedChanged != null)
                        CheckedChanged(this, EventArgs.Empty);
                }
            }

            public ThemeSwitch()
            {
                DoubleBuffered = true;
                Cursor = Cursors.Hand;
                Size = new System.Drawing.Size(74, 32);

                SetStyle(ControlStyles.UserPaint, true);
                SetStyle(ControlStyles.AllPaintingInWmPaint, true);
                SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
                SetStyle(ControlStyles.ResizeRedraw, true);
            }

            protected override void OnClick(EventArgs e)
            {
                Checked = !Checked;
                base.OnClick(e);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);

                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
                e.Graphics.CompositingQuality = CompositingQuality.HighQuality;

                RectangleF rect = new RectangleF(1, 1, Width - 2, Height - 2);
                float radius = rect.Height / 2f;

                Color back = Checked
                    ? Color.FromArgb(32, 28, 24)
                    : Color.FromArgb(235, 242, 255);

                Color border = Checked
                    ? Color.FromArgb(201, 122, 64)
                    : Color.FromArgb(147, 197, 253);

                Color knob = Checked
                    ? Color.FromArgb(201, 122, 64)
                    : Color.White;

                using (GraphicsPath path = RoundedRectF(rect, radius))
                {
                    using (SolidBrush brush = new SolidBrush(back))
                    {
                        e.Graphics.FillPath(brush, path);
                    }

                    using (Pen pen = new Pen(border, 1.6f))
                    {
                        e.Graphics.DrawPath(pen, path);
                    }
                }

                float knobSize = Height - 10;
                float knobX = Checked ? Width - knobSize - 6 : 6;
                float knobY = 5;

                using (SolidBrush brush = new SolidBrush(knob))
                {
                    e.Graphics.FillEllipse(brush, knobX, knobY, knobSize, knobSize);
                }

                string icon = Checked ? "☾" : "☀";
                RectangleF iconRect = Checked
                    ? new RectangleF(8, 4, 26, Height - 8)
                    : new RectangleF(Width - 34, 4, 26, Height - 8);

                using (Font f = new Font("Segoe UI Symbol", 12F, FontStyle.Bold))
                using (SolidBrush brush = new SolidBrush(Checked ? Color.FromArgb(224, 156, 96) : Color.FromArgb(30, 58, 138)))
                using (StringFormat sf = new StringFormat())
                {
                    sf.Alignment = StringAlignment.Center;
                    sf.LineAlignment = StringAlignment.Center;
                    e.Graphics.DrawString(icon, f, brush, iconRect, sf);
                }
            }
        }


        private class BorderNumericUpDown : NumericUpDown
        {
            public Color CustomBorderColor { get; set; }
            public Color ButtonBackColor { get; set; }
            public Color ButtonBorderColor { get; set; }
            public Color ArrowColor { get; set; }

            private SpinnerOverlay _spinnerOverlay;

            public BorderNumericUpDown()
            {
                CustomBorderColor = Color.FromArgb(203, 213, 225);
                ButtonBackColor = Color.FromArgb(248, 250, 252);
                ButtonBorderColor = Color.FromArgb(203, 213, 225);
                ArrowColor = Color.FromArgb(30, 58, 138);
                BorderStyle = BorderStyle.FixedSingle;
            }

            protected override void OnCreateControl()
            {
                base.OnCreateControl();
                EnsureSpinnerOverlay();
            }

            protected override void OnResize(EventArgs e)
            {
                base.OnResize(e);
                LayoutSpinnerOverlay();
                Invalidate();
            }

            protected override void OnEnabledChanged(EventArgs e)
            {
                base.OnEnabledChanged(e);
                if (_spinnerOverlay != null)
                    _spinnerOverlay.Enabled = Enabled;
                Invalidate();
            }

            protected override void WndProc(ref Message m)
            {
                base.WndProc(ref m);

                const int WM_PAINT = 0x000F;
                const int WM_NCPAINT = 0x0085;
                const int WM_PRINTCLIENT = 0x0318;

                if (m.Msg == WM_PAINT || m.Msg == WM_NCPAINT || m.Msg == WM_PRINTCLIENT)
                {
                    PaintCustomOutline();
                    EnsureSpinnerOverlay();
                    LayoutSpinnerOverlay();
                }
            }

            public void RefreshCustomButton()
            {
                EnsureSpinnerOverlay();
                LayoutSpinnerOverlay();
                PaintCustomOutline();
            }

            private void EnsureSpinnerOverlay()
            {
                if (!IsHandleCreated)
                    return;

                if (_spinnerOverlay == null || _spinnerOverlay.IsDisposed)
                {
                    _spinnerOverlay = new SpinnerOverlay(this);
                    Controls.Add(_spinnerOverlay);
                }

                _spinnerOverlay.Enabled = Enabled;
                _spinnerOverlay.BringToFront();
                _spinnerOverlay.Invalidate();
            }

            private void LayoutSpinnerOverlay()
            {
                if (_spinnerOverlay == null || _spinnerOverlay.IsDisposed)
                    return;

                int buttonWidth = Math.Max(18, SystemInformation.VerticalScrollBarWidth + 1);
                _spinnerOverlay.Bounds = new System.Drawing.Rectangle(
                    Width - buttonWidth - 1,
                    1,
                    buttonWidth,
                    Math.Max(1, Height - 2));
                _spinnerOverlay.BringToFront();
            }

            private void PaintCustomOutline()
            {
                try
                {
                    using (Graphics g = Graphics.FromHwnd(Handle))
                    {
                        g.SmoothingMode = SmoothingMode.None;

                        using (Pen borderPen = new Pen(CustomBorderColor, 1f))
                            g.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);
                    }
                }
                catch
                {
                }
            }

            private void StepValue(bool up)
            {
                try
                {
                    decimal next = up ? Value + Increment : Value - Increment;
                    if (next > Maximum) next = Maximum;
                    if (next < Minimum) next = Minimum;
                    Value = next;
                }
                catch
                {
                }
            }

            private static Color MixColor(Color a, Color b, double amount)
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

            private class SpinnerOverlay : Control
            {
                private readonly BorderNumericUpDown _owner;

                public SpinnerOverlay(BorderNumericUpDown owner)
                {
                    _owner = owner;
                    Cursor = Cursors.Hand;
                    SetStyle(ControlStyles.UserPaint, true);
                    SetStyle(ControlStyles.AllPaintingInWmPaint, true);
                    SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
                    SetStyle(ControlStyles.ResizeRedraw, true);
                }

                protected override void OnMouseDown(MouseEventArgs e)
                {
                    base.OnMouseDown(e);
                    if (_owner == null || !_owner.Enabled)
                        return;

                    _owner.Focus();
                    _owner.StepValue(e.Y < Height / 2);
                    Invalidate();
                }

                protected override void OnPaint(PaintEventArgs e)
                {
                    base.OnPaint(e);

                    Color back = _owner.Enabled
                        ? _owner.ButtonBackColor
                        : MixColor(_owner.ButtonBackColor, Color.Gray, 0.25);

                    Color border = _owner.Enabled
                        ? _owner.ButtonBorderColor
                        : MixColor(_owner.ButtonBorderColor, Color.Gray, 0.25);

                    Color arrow = _owner.Enabled
                        ? _owner.ArrowColor
                        : Color.FromArgb(148, 163, 184);

                    using (SolidBrush brush = new SolidBrush(back))
                        e.Graphics.FillRectangle(brush, ClientRectangle);

                    using (Pen pen = new Pen(border, 1f))
                    {
                        e.Graphics.DrawLine(pen, 0, 0, 0, Height - 1);
                        e.Graphics.DrawLine(pen, 0, Height / 2, Width - 1, Height / 2);
                        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
                    }

                    // Chia nút spinner thành 2 ô bằng nhau để phần trên/dưới không bị lệch.
                    int separatorY = Height / 2;
                    int cx = Width / 2;
                    int topCy = separatorY / 2;
                    int bottomCy = separatorY + ((Height - separatorY) / 2);
                    int arrowHalfWidth = 4;
                    int arrowHalfHeight = 3;

                    Point[] upArrow = new Point[]
                    {
                        new Point(cx - arrowHalfWidth, topCy + arrowHalfHeight / 2),
                        new Point(cx + arrowHalfWidth, topCy + arrowHalfHeight / 2),
                        new Point(cx, topCy - arrowHalfHeight)
                    };

                    Point[] downArrow = new Point[]
                    {
                        new Point(cx - arrowHalfWidth, bottomCy - arrowHalfHeight / 2),
                        new Point(cx + arrowHalfWidth, bottomCy - arrowHalfHeight / 2),
                        new Point(cx, bottomCy + arrowHalfHeight)
                    };

                    using (SolidBrush arrowBrush = new SolidBrush(arrow))
                    {
                        e.Graphics.FillPolygon(arrowBrush, upArrow);
                        e.Graphics.FillPolygon(arrowBrush, downArrow);
                    }
                }
            }
        }


        private class BorderComboBox : ComboBox
        {
            public Color CustomBorderColor { get; set; }
            public Color ButtonBackColor { get; set; }
            public Color ButtonBorderColor { get; set; }
            public Color ArrowColor { get; set; }

            public BorderComboBox()
            {
                CustomBorderColor = Color.FromArgb(203, 213, 225);
                ButtonBackColor = Color.FromArgb(248, 250, 252);
                ButtonBorderColor = Color.FromArgb(203, 213, 225);
                ArrowColor = Color.FromArgb(30, 58, 138);
                FlatStyle = FlatStyle.Flat;
                IntegralHeight = false;
                DrawMode = DrawMode.OwnerDrawFixed;
                DropDownStyle = ComboBoxStyle.DropDownList;
            }

            protected override void OnDrawItem(DrawItemEventArgs e)
            {
                e.DrawBackground();

                if (e.Index >= 0 && e.Index < Items.Count)
                {
                    using (SolidBrush brush = new SolidBrush(ForeColor))
                    using (StringFormat sf = new StringFormat())
                    {
                        sf.LineAlignment = StringAlignment.Center;
                        sf.Alignment = StringAlignment.Near;
                        System.Drawing.Rectangle textRect = new System.Drawing.Rectangle(
                            e.Bounds.Left + 4,
                            e.Bounds.Top,
                            e.Bounds.Width - 8,
                            e.Bounds.Height);
                        e.Graphics.DrawString(Items[e.Index].ToString(), Font, brush, textRect, sf);
                    }
                }

                e.DrawFocusRectangle();
            }

            protected override void WndProc(ref Message m)
            {
                base.WndProc(ref m);

                const int WM_PAINT = 0x000F;
                const int WM_NCPAINT = 0x0085;
                const int WM_PRINTCLIENT = 0x0318;

                if (m.Msg == WM_PAINT || m.Msg == WM_NCPAINT || m.Msg == WM_PRINTCLIENT)
                    PaintCustomOutline();
            }

            protected override void OnResize(EventArgs e)
            {
                base.OnResize(e);
                Invalidate();
            }

            protected override void OnEnabledChanged(EventArgs e)
            {
                base.OnEnabledChanged(e);
                Invalidate();
            }

            public void RefreshCustomButton()
            {
                PaintCustomOutline();
                Invalidate();
            }

            private void PaintCustomOutline()
            {
                try
                {
                    using (Graphics g = Graphics.FromHwnd(Handle))
                    {
                        g.SmoothingMode = SmoothingMode.None;

                        int buttonWidth = Math.Max(22, SystemInformation.HorizontalScrollBarArrowWidth + 2);
                        System.Drawing.Rectangle buttonRect = new System.Drawing.Rectangle(
                            Width - buttonWidth - 1,
                            1,
                            buttonWidth,
                            Height - 2);

                        using (SolidBrush buttonBrush = new SolidBrush(ButtonBackColor))
                            g.FillRectangle(buttonBrush, buttonRect);

                        using (Pen borderPen = new Pen(CustomBorderColor, 1f))
                        {
                            g.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);
                            g.DrawLine(borderPen, buttonRect.Left, 1, buttonRect.Left, Height - 2);
                        }

                        int cx = buttonRect.Left + buttonRect.Width / 2;
                        int cy = Height / 2 + 1;

                        Point[] arrow = new Point[]
                        {
                            new Point(cx - 4, cy - 2),
                            new Point(cx + 4, cy - 2),
                            new Point(cx, cy + 3)
                        };

                        using (SolidBrush arrowBrush = new SolidBrush(Enabled ? ArrowColor : Color.FromArgb(148, 163, 184)))
                            g.FillPolygon(arrowBrush, arrow);
                    }
                }
                catch
                {
                }
            }
        }


        private class RoundedPanel : Panel
        {
            public int BorderRadius { get; set; }
            public Color BorderColor { get; set; }

            public RoundedPanel()
            {
                BorderRadius = 8;
                BorderColor = Color.FromArgb(220, 226, 235);
                DoubleBuffered = true;
                BorderStyle = BorderStyle.None;

                SetStyle(ControlStyles.UserPaint, true);
                SetStyle(ControlStyles.AllPaintingInWmPaint, true);
                SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
                SetStyle(ControlStyles.ResizeRedraw, true);
            }

            protected override void OnPaintBackground(PaintEventArgs e)
            {
                Color parentColor = Parent != null ? Parent.BackColor : Color.FromArgb(248, 250, 252);

                using (SolidBrush brush = new SolidBrush(parentColor))
                {
                    e.Graphics.FillRectangle(brush, ClientRectangle);
                }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
                e.Graphics.CompositingQuality = CompositingQuality.HighQuality;

                float inset = 1.0f;

                RectangleF rect = new RectangleF(
                    inset,
                    inset,
                    Width - inset * 2 - 1,
                    Height - inset * 2 - 1
                );

                using (GraphicsPath path = RoundedRectF(rect, BorderRadius))
                {
                    using (SolidBrush brush = new SolidBrush(BackColor))
                    {
                        e.Graphics.FillPath(brush, path);
                    }

                    using (Pen pen = new Pen(BorderColor, 1.4f))
                    {
                        e.Graphics.DrawPath(pen, path);
                    }
                }
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

        private static GraphicsPath RoundedRect(System.Drawing.Rectangle bounds, int radius)
        {
            int diameter = radius * 2;
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
