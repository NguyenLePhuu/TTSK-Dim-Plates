using System;
using System.Drawing;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Linq;
using D = Tekla.Structures.Drawing;
using B = Tekla.Technology.Akit.UserScript.PHU_DimensionBatch;

namespace TTSK_AutoDim_Plates
{
    public partial class MainForm
    {
        private Panel checkMenuHost;
        private SafeRoundedButton btnCheckDim;
        private SafeRoundedButton btnCheckRevision;
        private Timer checkMenuCloseTimer;
        private B.Session dimensionCheckBatch;
        private string dimensionCheckModelPath;
        private bool dimensionCheckOpen;

        private void ResetDimensionCheckBatch()
        {dimensionCheckBatch=null;dimensionCheckModelPath=null;}
        private void CaptureDimensionCheckBatchSource()
        {
            var model=new Tekla.Structures.Model.Model();
            dimensionCheckModelPath=model.GetConnectionStatus()?model.GetInfo().ModelPath:null;
        }

        private void InitializeCheckMenu()
        {
            checkMenuHost = new Panel { Visible = false, BackColor = Color.Transparent, Padding = Padding.Empty, Margin = Padding.Empty };
            btnCheckDim = new SafeRoundedButton { Text = "⌕  Check Dim", TabStop = true };
            btnCheckRevision = new SafeRoundedButton { Text = "↺  Revision", TabStop = true };
            btnCheckScale.IconAlignmentReference = btnCheckScale.Text;
            btnCheckDim.IconAlignmentReference = btnCheckScale.Text;
            btnCheckRevision.IconAlignmentReference = btnCheckScale.Text;
            checkMenuHost.Controls.Add(btnCheckDim);
            checkMenuHost.Controls.Add(btnCheckRevision);
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
            foreach (Control control in new Control[] { checkMenuHost, btnCheckDim, btnCheckRevision })
            {
                control.MouseEnter += delegate { checkMenuCloseTimer.Stop(); };
                control.MouseLeave += delegate { ScheduleCheckMenuClose(); };
            }
            btnCheckDim.Click += delegate { RunDimensionCheck(); };
            btnCheckRevision.Click += delegate { RunRevisionCheck(); };
            btnCheckDim.KeyDown += delegate(object sender, KeyEventArgs e)
            { if (e.KeyCode == Keys.Down) { btnCheckRevision.Focus(); e.Handled = e.SuppressKeyPress = true; } };
            btnCheckRevision.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Up) btnCheckDim.Focus();
                else if (e.KeyCode == Keys.Escape) { HideCheckMenu(); btnCheckScale.Focus(); }
                else return;
                e.Handled = e.SuppressKeyPress = true;
            };
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
            if (dimensionCheckOpen || _snapshotExportRunning || _isBatchRunning || _fitAndCleanupRunning
                || btnCheckScale == null || !btnCheckScale.Enabled) return;
            if(rbBatch!=null&&rbBatch.Checked){RunDimensionCheckBatch();return;}
            Tekla.Technology.Akit.UserScript.PHU_DimensionCheck.ReportDarkMode = _darkMode;
            // Check DIM findings and execution failures are not AutoDim's "NO DIM CREATED".
            dimensionCheckOpen=true;
            try
            {
                var result=Tekla.Technology.Akit.UserScript.PHU_DimensionCheck.Analyze();
                Tekla.Technology.Akit.UserScript.PHU_DimensionCheck.AcceptRecheck(result);
                using(var report=new Tekla.Technology.Akit.UserScript.PHU_DimensionReportForm(result,_darkMode,
                    ()=>{result=Tekla.Technology.Akit.UserScript.PHU_DimensionCheck.Analyze();return result;}))report.ShowDialog(this);
                bool attention=result.Warnings.Count>0||result.Findings.Any(f=>f.Status=="ERROR"||f.Status=="REVIEW");
                SetMainStatus("Check DIM: "+result.Summary,attention?MainStatusKind.Warning:MainStatusKind.Success);
            }
            catch(Exception ex){SetMainStatus("Check DIM chưa hoàn tất: "+ex.GetBaseException().Message,MainStatusKind.Warning);}
            finally{dimensionCheckOpen=false;}
        }

        private void RunRevisionCheck()
        {
            HideCheckMenu();
            if (dimensionCheckOpen || _snapshotExportRunning || _isBatchRunning || _fitAndCleanupRunning
                || !btnCheckScale.Enabled) return;
            dimensionCheckOpen = true;
            try
            {
                using (var report = new Tekla.Technology.Akit.UserScript.PHU_DimensionReportForm(_darkMode, CreateRevisionDimensions))
                    report.ShowDialog(this);
            }
            catch (Exception ex) { SetMainStatus("Check Revision: " + ex.GetBaseException().Message, MainStatusKind.Warning); }
            finally { dimensionCheckOpen = false; }
        }

        private string CreateRevisionDimensions()
        {
            // Resolve before entering the scoped operation. No Load Standard, Auto Section,
            // grid fitting, autoscale or MainForm Create Drawing orchestration is invoked.
            var model = new Tekla.Structures.Model.Model();
            var drawing = new D.DrawingHandler().GetActiveDrawing();
            var part = Tekla.Technology.Akit.UserScript.PHU_MainPartResolver.Resolve(model, drawing);
            if (part == null) throw new InvalidOperationException("Không xác định được main part.");
            var type = DetectAutoDimPartType(part);
            if (type == AutoDimPartType.Plate && !(drawing is D.SinglePartDrawing))
                throw new InvalidOperationException("Plate Create drawing cần bản vẽ single part.");
            Tekla.Technology.Akit.UserScript.PHU_BeamGridDimensionEngine.Reset();
            Tekla.Technology.Akit.UserScript.PHU_VerticalShapeViewLayoutContext.Reset();
            return Tekla.Technology.Akit.UserScript.PHU_DimensionRevision.CreateRevisionDrawing(() =>
            {
                switch (type)
                {
                    case AutoDimPartType.Plate: Tekla.Technology.Akit.UserScript.Script.Run(null); break;
                    case AutoDimPartType.ShapeIH: Tekla.Technology.Akit.UserScript.ShapeScript.Run(null); break;
                    case AutoDimPartType.ShapeC: Tekla.Technology.Akit.UserScript.ShapeCScript.Run(null); break;
                    case AutoDimPartType.ShapeL: Tekla.Technology.Akit.UserScript.ShapeLScript.Run(null); break;
                    case AutoDimPartType.ShapeBox: Tekla.Technology.Akit.UserScript.ShapeBoxScript.Run(null); break;
                    case AutoDimPartType.ShapeUnknown:
                        var result = Tekla.Technology.Akit.UserScript.ShapeUnknownScript.RunSafe(drawing, model, part);
                        if (result == null || !result.Success)
                            throw new InvalidOperationException(result == null ? "Không có kết quả DIM." : result.Message);
                        break;
                    default: throw new InvalidOperationException("Profile chưa hỗ trợ Create drawing.");
                }
            });
        }

        private void RunDimensionCheckBatch()
        {
            if(_selectedDrawings.Count==0)
            {SetMainStatus("Check DIM Batch: chọn bản vẽ trong Document Manager rồi bấm Load Selected.",MainStatusKind.Warning);return;}
            dimensionCheckOpen=true;
            try
            {
                var model=new Tekla.Structures.Model.Model();
                if(!model.GetConnectionStatus()||String.IsNullOrEmpty(dimensionCheckModelPath)||
                    !String.Equals(model.GetInfo().ModelPath,dimensionCheckModelPath,StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Hãy Load Selected lại trong model hiện tại.");
                var drawings=new List<D.Drawing>();
                foreach(var drawing in _selectedDrawings)
                    if(!drawings.Any(existing=>existing.IsSameDatabaseObject(drawing)))drawings.Add(drawing);
                if(dimensionCheckBatch==null||dimensionCheckBatch.Entries.Count!=drawings.Count||
                    dimensionCheckBatch.Entries.Where((e,i)=>!e.Drawing.IsSameDatabaseObject(drawings[i])).Any())
                {
                    var entries=drawings.Select((d,i)=>new B.Entry {Key=Guid.NewGuid().ToString("N"),Number=i+1,
                        Name=SafeDrawingMark(d)+" / "+d.Name,Drawing=d,Supported=d is D.AssemblyDrawing||d is D.SinglePartDrawing}).ToList();
                    dimensionCheckBatch=new B.Session(entries,new B.TeklaAdapter(dimensionCheckModelPath),dimensionCheckModelPath);
                }
                using(var report=new Tekla.Technology.Akit.UserScript.PHU_DimensionReportForm(dimensionCheckBatch,_darkMode,true))report.ShowDialog(this);
                int work=dimensionCheckBatch.Entries.Count(e=>e.NeedsWork);
                int pending=dimensionCheckBatch.Entries.Count(e=>e.Status==B.State.Pending);
                int unsupported=dimensionCheckBatch.Entries.Count(e=>e.Status==B.State.Unsupported);
                SetMainStatus("Check DIM Batch: "+dimensionCheckBatch.Entries.Count+" bản · "+work+" cần xử lý · "+pending+" chưa chạy · "+unsupported+" chưa hỗ trợ",
                    work+pending+unsupported>0?MainStatusKind.Warning:MainStatusKind.Success);
            }
            catch(Exception ex){SetMainStatus("Check DIM Batch: "+ex.GetBaseException().Message,MainStatusKind.Warning);}
            finally{dimensionCheckOpen=false;}
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
            int menuWidth = btnCheckScale.Width;
            checkMenuHost.Size = new Size(menuWidth, btnCheckScale.Height * 2);
            btnCheckDim.Location = Point.Empty;
            btnCheckDim.Size = new Size(menuWidth, btnCheckScale.Height);
            btnCheckDim.Font = btnCheckScale.Font;
            btnCheckDim.FillColor = btnCheckScale.FillColor;
            btnCheckDim.GradientEndColor = btnCheckScale.GradientEndColor;
            btnCheckDim.BorderColor = btnCheckScale.BorderColor;
            btnCheckDim.HoverBorderColor = btnCheckScale.HoverBorderColor;
            btnCheckDim.TextColor = btnCheckScale.TextColor;
            btnCheckDim.BorderRadius = btnCheckScale.BorderRadius;
            btnCheckDim.ConnectedTop = true;
            btnCheckDim.ConnectedBottom = true;
            btnCheckDim.FlushOuterEdge = btnCheckScale.FlushOuterEdge;
            btnCheckDim.Invalidate();
            btnCheckRevision.Location = new Point(0, btnCheckScale.Height);
            btnCheckRevision.Size = btnCheckDim.Size;
            btnCheckRevision.Font = btnCheckScale.Font;
            btnCheckRevision.FillColor = btnCheckScale.FillColor;
            btnCheckRevision.GradientEndColor = btnCheckScale.GradientEndColor;
            btnCheckRevision.BorderColor = btnCheckScale.BorderColor;
            btnCheckRevision.HoverBorderColor = btnCheckScale.HoverBorderColor;
            btnCheckRevision.TextColor = btnCheckScale.TextColor;
            btnCheckRevision.BorderRadius = btnCheckScale.BorderRadius;
            btnCheckRevision.ConnectedTop = true;
            btnCheckRevision.FlushOuterEdge = btnCheckScale.FlushOuterEdge;
            btnCheckRevision.Invalidate();
            checkMenuHost.Invalidate();
        }
    }
}
