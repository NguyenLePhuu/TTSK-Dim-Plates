using System;
using System.Drawing;
using System.Windows.Forms;

namespace TTSK_AutoDim_Plates
{
    public partial class MainForm
    {
        private Panel checkMenuHost;
        private SafeRoundedButton btnCheckDim;
        private Timer checkMenuCloseTimer;

        private void InitializeCheckMenu()
        {
            checkMenuHost = new Panel { Visible = false, BackColor = Color.Transparent, Padding = Padding.Empty, Margin = Padding.Empty };
            btnCheckDim = new SafeRoundedButton { Text = "⌕  Check Dim", TabStop = true };
            checkMenuHost.Controls.Add(btnCheckDim);
            Controls.Add(checkMenuHost);
            checkMenuCloseTimer = new Timer { Interval = 180 };
            checkMenuCloseTimer.Tick += delegate
            {
                checkMenuCloseTimer.Stop();
                if (!IsCursorInsideControl(btnCheckScale) && !IsCursorInsideControl(checkMenuHost)
                    && !checkMenuHost.ContainsFocus) HideCheckMenu();
            };
            btnCheckScale.MouseHover += delegate { ShowCheckMenu(); };
            btnCheckScale.MouseEnter += delegate { checkMenuCloseTimer.Stop(); };
            btnCheckScale.MouseLeave += delegate { ScheduleCheckMenuClose(); };
            foreach (Control control in new Control[] { checkMenuHost, btnCheckDim })
            {
                control.MouseEnter += delegate { checkMenuCloseTimer.Stop(); };
                control.MouseLeave += delegate { ScheduleCheckMenuClose(); };
            }
            btnCheckDim.Click += delegate { RunDimensionCheck(); };
            btnCheckScale.Click += delegate { HideCheckMenu(); };
            btnCheckScale.EnabledChanged += delegate { if (!btnCheckScale.Enabled) HideCheckMenu(); };
            btnCheckScale.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode != Keys.Down && e.KeyCode != Keys.Up) return;
                ShowCheckMenu();
                btnCheckDim.Focus();
                e.Handled = e.SuppressKeyPress = true;
            };
            btnCheckDim.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode != Keys.Escape) return;
                HideCheckMenu();
                btnCheckScale.Focus();
                e.Handled = e.SuppressKeyPress = true;
            };
            Deactivate += delegate { HideCheckMenu(); };
            Resize += delegate { HideCheckMenu(); };
            Disposed += delegate { checkMenuCloseTimer.Dispose(); };
        }

        private void RunDimensionCheck()
        {
            HideCheckMenu();
            if (_snapshotExportRunning || _isBatchRunning || _fitAndCleanupRunning
                || btnCheckScale == null || !btnCheckScale.Enabled) return;
            Tekla.Technology.Akit.UserScript.PHU_DimensionCheck.ReportDarkMode = _darkMode;
            RunExternalAutoDimSlot("Tekla.Technology.Akit.UserScript.PHU_DimensionCheck");
        }

        private void ShowCheckMenu()
        {
            if (checkMenuHost == null || !btnCheckScale.Enabled) return;
            HidePrintMenu();
            checkMenuCloseTimer.Stop();
            ApplyCheckMenuTheme();
            Point anchor = PointToClient(btnCheckScale.PointToScreen(Point.Empty));
            checkMenuHost.Location = new Point(
                Math.Max(0, Math.Min(anchor.X, ClientSize.Width - checkMenuHost.Width)),
                Math.Max(0, Math.Min(anchor.Y + btnCheckScale.Height, ClientSize.Height - checkMenuHost.Height)));
            btnCheckScale.ConnectedBottom = true;
            btnCheckScale.Invalidate();
            checkMenuHost.Show();
            checkMenuHost.BringToFront();
        }

        private void HideCheckMenu()
        {
            if (checkMenuCloseTimer != null) checkMenuCloseTimer.Stop();
            if (checkMenuHost != null) checkMenuHost.Hide();
            if (btnCheckScale != null)
            {
                btnCheckScale.ConnectedBottom = false;
                btnCheckScale.Invalidate();
            }
        }

        private void ScheduleCheckMenuClose()
        {
            if (!checkMenuHost.Visible) return;
            checkMenuCloseTimer.Stop();
            checkMenuCloseTimer.Start();
        }

        private void ApplyCheckMenuTheme()
        {
            if (checkMenuHost == null || btnCheckDim == null) return;
            // Same-sized adjoining rows: only the outside corners are rounded.
            checkMenuHost.Size = btnCheckScale.Size;
            btnCheckDim.Location = Point.Empty;
            btnCheckDim.Size = btnCheckScale.Size;
            btnCheckDim.Font = btnCheckScale.Font;
            btnCheckDim.FillColor = btnCheckScale.FillColor;
            btnCheckDim.GradientEndColor = btnCheckScale.GradientEndColor;
            btnCheckDim.BorderColor = btnCheckScale.BorderColor;
            btnCheckDim.HoverBorderColor = btnCheckScale.HoverBorderColor;
            btnCheckDim.TextColor = btnCheckScale.TextColor;
            btnCheckDim.BorderRadius = btnCheckScale.BorderRadius;
            btnCheckDim.ConnectedTop = true;
            btnCheckDim.ConnectedBottom = false;
            btnCheckDim.FlushOuterEdge = btnCheckScale.FlushOuterEdge;
            btnCheckDim.Invalidate();
            checkMenuHost.Invalidate();
        }
    }
}
