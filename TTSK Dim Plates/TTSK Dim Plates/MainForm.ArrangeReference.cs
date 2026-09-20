using System;
using System.Drawing;
using Rectangle = System.Drawing.Rectangle;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Tekla.Structures.Drawing;
using R = Tekla.Technology.Akit.UserScript.PHU_DimensionRevision;

namespace TTSK_AutoDim_Plates
{
    public partial class MainForm
    {
        private void BuildArrangeReferenceControls()
        {
            slideArrangePanel.Height = 410;
            var run = new SafeRoundedButton { Text = "Theo bản mẫu", Bounds = new Rectangle(16, 358, 282, 40),
                Font = new Font("Segoe UI", 10F, FontStyle.Bold) };
            run.Click += async delegate
            {
                if (_isBatchRunning || _snapshotExportRunning || _fitAndCleanupRunning) return;
                if (LoadSelectedArrangeDrawings()) await RunArrangeReferenceBatch();
            };
            slideArrangePanel.Controls.Add(run);
        }
        private bool LoadSelectedArrangeDrawings()
        {
            if (_isBatchRunning || _snapshotExportRunning || _fitAndCleanupRunning) return false;
            rbBatch.Checked = true;
            UpdateModeUi();
            btnLoad_Click(btnLoad, EventArgs.Empty);
            if (_selectedDrawings.Count == 0) return false;
            if (_selectedDrawings.Count < 2)
            {
                SetMainStatus("Hãy chọn ít nhất 2 bản vẽ trong Document Manager để căn theo bản mẫu.", MainStatusKind.Warning);
                return false;
            }
            return true;
        }

        private async Task RunArrangeReferenceBatch()
        {
            if (_isBatchRunning || _snapshotExportRunning || _fitAndCleanupRunning) return;
            var handler = new DrawingHandler();
            if (_selectedDrawings.Count < 2)
            { SetMainStatus("Hãy chọn ít nhất 2 bản vẽ trong Document Manager để căn theo bản mẫu.", MainStatusKind.Warning); return; }
            if (handler.GetActiveDrawing() != null)
            { SetMainStatus("Hãy lưu và đóng bản vẽ đang mở trước khi căn cả danh sách.", MainStatusKind.Warning); return; }
            var drawings = _selectedDrawings.ToList();
            var modelPath = new Tekla.Structures.Model.Model().GetInfo().ModelPath;
            var progress = new Form { Text = "Arrange View · Theo bản đầu", Width = 580, Height = 180,
                StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false, ControlBox = false };
            var status = new Label { Dock = DockStyle.Fill, Padding = new Padding(16), Text = "Đang đọc bản chuẩn..." };
            bool cancel = false;
            var stop = new Button { Text = "Dừng sau bản hiện tại", Dock = DockStyle.Bottom, Height = 36 };
            stop.Click += delegate { cancel = true; stop.Enabled = false; };
            progress.Controls.Add(status); progress.Controls.Add(stop);
            int ok = 0, failed = 0; Drawing owned = null;
            _isBatchRunning = true; Enabled = false;
            try
            {
                progress.Show(this);
                R.ArrangeReference reference = null;
                for (int i = 0; i < drawings.Count && !cancel; i++)
                {
                    status.Text = (i + 1) + " / " + drawings.Count + " · " + SafeDrawingMark(drawings[i]);
                    await Task.Delay(1); // Yield paint/cancel only; Tekla calls stay on the UI STA.
                    if (cancel) break;
                    if (!String.Equals(modelPath, new Tekla.Structures.Model.Model().GetInfo().ModelPath, StringComparison.OrdinalIgnoreCase)
                        || handler.GetActiveDrawing() != null) throw new InvalidOperationException("Model/bản vẽ đang mở đã thay đổi; dừng batch.");
                    bool saveFailed = false;
                    try
                    {
                        if (!handler.SetActiveDrawing(drawings[i], false)) throw new InvalidOperationException("Không mở được bản vẽ.");
                        owned = handler.GetActiveDrawing();
                        if (owned == null || !owned.IsSameDatabaseObject(drawings[i]))
                        { owned = null; throw new InvalidOperationException("Tekla mở sai bản vẽ."); }
                        string result;
                        if (i == 0) { reference = R.CaptureArrangeReference(); result = "REFERENCE · giữ nguyên"; }
                        else
                        {
                            var before = R.CaptureArrangeReference();
                            result = R.ApplyArrangeReference(reference);
                            saveFailed = true;
                            if (!handler.SaveActiveDrawing())
                                throw new InvalidOperationException("Lưu thất bại; giữ bản vẽ đang mở để kiểm tra.");
                            // Tekla can save AND close a drawing opened with visible=false.
                            // Always reopen the persisted drawing; never validate a null active drawing.
                            var afterSave = handler.GetActiveDrawing();
                            if (afterSave != null)
                            {
                                if (!afterSave.IsSameDatabaseObject(drawings[i]) || !handler.CloseActiveDrawing(false))
                                    throw new InvalidOperationException("Không đóng được đúng bản vẽ sau lưu.");
                            }
                            if (!handler.SetActiveDrawing(drawings[i], false))
                                throw new InvalidOperationException("Đã lưu nhưng không mở lại được bản vẽ để xác minh.");
                            owned = handler.GetActiveDrawing();
                            if (owned == null || !owned.IsSameDatabaseObject(drawings[i]))
                            {
                                owned = null;
                                throw new InvalidOperationException("Bản vẽ mở lại không đúng bản vừa lưu.");
                            }
                            R.VerifyArrangeReference(reference, before);
                            saveFailed = false;
                            ok++;
                        }
                        SetArrangeRowResult(i, result);
                    }
                    catch (Exception ex)
                    {
                        failed++; SetArrangeRowResult(i, "ERROR · " + (saveFailed ? "Lưu/xác minh chưa hoàn tất; giữ bản vẽ đang mở. " : "") + ex.GetBaseException().Message);
                        if (saveFailed) throw;
                        if (i == 0) throw new InvalidOperationException("Bản chuẩn không hợp lệ: " + ex.GetBaseException().Message);
                    }
                    finally
                    {
                        if (owned != null && !saveFailed)
                        {
                            var active = handler.GetActiveDrawing();
                            if (active == null || !active.IsSameDatabaseObject(owned)
                                || !handler.CloseActiveDrawing(false) || handler.GetActiveDrawing() != null)
                                throw new InvalidOperationException("Không đóng được đúng bản vẽ batch; dừng để kiểm tra.");
                            owned = null;
                        }
                    }
                }
                SetMainStatus((cancel ? "Đã dừng" : failed > 0 ? (ok == 0 ? "Không căn được bản nào" : "Kết thúc có lỗi") : "Hoàn tất")
                    + " · căn " + ok + " bản · lỗi/bỏ qua " + failed
                    + " · chưa xử lý " + Math.Max(0, drawings.Count - 1 - ok - failed),
                    failed > 0 ? (ok == 0 ? MainStatusKind.Error : MainStatusKind.Warning) : MainStatusKind.Success);
            }
            catch (Exception ex) { SetMainStatus(ex.Message, MainStatusKind.Error); }
            finally { progress.Close(); progress.Dispose(); Enabled = true; _isBatchRunning = false; UpdateModeUi(); }
        }

        private void SetArrangeRowResult(int index, string result)
        {
            if (index >= dgvDrawings.Rows.Count) return;
            var row = dgvDrawings.Rows[index];
            row.Cells["STATUS"].Value = result.StartsWith("ERROR") ? "ERROR" : result.StartsWith("REFERENCE") ? "REFERENCE" : "OK";
            row.Cells["RESULT"].Value = result;
            row.Cells["RESULT"].ToolTipText = result;
        }
    }
}
