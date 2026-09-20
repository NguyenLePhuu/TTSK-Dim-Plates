using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace TTSK_AutoDim_Plates
{
    public partial class MainForm
    {
        private WorkSession workSession;
        private System.Windows.Forms.Timer workJournalTimer;
        private ToolTip workJournalTip;
        private Label workJournalIcon;
        private Label workJournalFortune;
        private string workJournalError;
        private string workJournalFolder = WorkJournal.Folder;

        private void InitializeWorkJournal()
        {
            workSession = WorkJournal.StartDay(DateTime.Now);
            workJournalIcon = new Label
            {
                Text = "⌬", AutoSize = false, Size = new Size(23, 24),
                Location = new Point(MainClientWidth - 94, 9), Font = new Font("Segoe UI", 10F),
                ForeColor = Color.White, BackColor = Color.Transparent, Cursor = Cursors.Hand,
                AccessibleName = "Chức năng 11: Nhật ký giờ làm", TabStop = true,
                TextAlign = ContentAlignment.MiddleCenter
            };
            mainFooter.Controls.Add(workJournalIcon);
            workJournalIcon.BringToFront();
            workJournalFortune = new Label { Text = "富", AutoSize = false, Size = new Size(25, 24),
                Location = new Point(MainClientWidth - 71, 9), Font = new Font("Segoe UI", 10F),
                ForeColor = Color.White, BackColor = Color.Transparent, TextAlign = ContentAlignment.MiddleCenter };
            mainFooter.Controls.Add(workJournalFortune);
            workJournalFortune.BringToFront();
            ApplyWorkJournalFooterTheme();
            workJournalTip = new ToolTip();
            workJournalTip.SetToolTip(workJournalIcon, "11 · Nhật ký dự án / giờ làm · Xuất TXT");
            workJournalIcon.Click += delegate { ShowWorkJournal(); };
            workJournalIcon.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) { e.Handled = true; ShowWorkJournal(); }
            };
            workJournalTimer = new System.Windows.Forms.Timer { Interval = 30000 };
            workJournalTimer.Tick += delegate
            {
                if (!EnsureWorkDay(DateTime.Now)) return;
                if (!CanApplyUpdateNow()) return;
                ObserveWorkProject();
                SaveWorkSession();
            };
            Shown += delegate { ObserveWorkProject(); SaveWorkSession(); workJournalTimer.Start(); };
        }

        // Read-only Tekla calls. No model/drawing modifications, macros or CommitChanges.
        private void ApplyWorkJournalFooterTheme()
        {
            // These glyphs sit on the colored footer, unlike ordinary body labels.
            Color foreground = _darkMode ? Color.FromArgb(226, 232, 240) : Color.White;
            foreach (var label in new[] { workJournalIcon, workJournalFortune })
                if (label != null) { label.ForeColor = foreground; label.BackColor = Color.Transparent; }
        }

        private void ObserveWorkProject()
        {
            if (!EnsureWorkDay(DateTime.Now)) return;
            try
            {
                var model = new Tekla.Structures.Model.Model();
                if (!model.GetConnectionStatus()) return;
                var project = model.GetProjectInfo();
                var info = model.GetInfo();
                if (project == null || info == null) return;
                DateTime day = DateTime.Now.Date;
                if (!workSession.Projects.Any(p => p.Day == day && p.Number == project.ProjectNumber
                    && p.Name == project.Name && p.ModelPath == info.ModelPath))
                    workSession.Projects.Add(new WorkProject { Day = day, FirstSeen = DateTime.Now, Number = project.ProjectNumber,
                        Name = project.Name, ModelPath = info.ModelPath });
            }
            catch (Exception) { /* Retain already observed projects if Tekla disconnects first. */ }
        }

        private bool SaveWorkSession()
        {
            try
            {
                WorkJournal.Save(workJournalFolder, workSession);
                workJournalError = null;
                workJournalTip.SetToolTip(workJournalIcon, "11 · Nhật ký dự án / giờ làm · Xuất TXT");
                return true;
            }
            catch (Exception ex)
            {
                workJournalError = "Không lưu được nhật ký: " + ex.Message;
                workJournalTip.SetToolTip(workJournalIcon, workJournalError);
                return false;
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (workSession != null)
            {
                workJournalTimer.Stop();
                // Capture the timestamp immediately; this event runs only after an accepted close.
                DateTime closedAt = DateTime.Now;
                EnsureWorkDay(closedAt);
                workSession.Closed = closedAt;
                if (!SaveWorkSession())
                    MessageBox.Show(workJournalError + "\r\nGiờ đóng: " + workSession.Closed.Value.ToString("dd/MM/yyyy HH:mm:ss")
                        + "\r\nHãy ghi lại giờ này để đối chiếu.", "TTSK · Nhật ký giờ làm", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                workJournalTimer.Dispose();
                workJournalTip.Dispose();
            }
            base.OnFormClosed(e);
        }

        private bool EnsureWorkDay(DateTime now)
        {
            try
            {
                workSession = WorkJournal.RollToDay(workJournalFolder, workSession, now);
                return true;
            }
            catch (Exception ex)
            {
                workJournalError = "Không lưu được nhật ký: " + ex.Message;
                workJournalTip.SetToolTip(workJournalIcon, workJournalError);
                return false;
            }
        }

        private void ShowWorkJournal()
        {
            if (!CanApplyUpdateNow()) return;
            using (var form = new WorkJournalReportForm(() =>
            {
                if (!EnsureWorkDay(DateTime.Now)) throw new IOException(workJournalError);
                ObserveWorkProject();
                if (!SaveWorkSession()) throw new IOException(workJournalError);
                int unreadable;
                var sessions = WorkJournal.Read(workJournalFolder, out unreadable);
                if (unreadable > 0) throw new IOException("Không đọc được " + unreadable + " tệp nhật ký.");
                return sessions;
            }, _darkMode))
            {
                form.ShowDialog(this);
            }
        }
    }
}
