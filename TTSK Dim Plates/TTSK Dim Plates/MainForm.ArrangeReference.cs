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
                if (operationRunning || _isBatchRunning || _snapshotExportRunning || _fitAndCleanupRunning) return;
                if (await LoadSelectedArrangeDrawings()) await RunArrangeReferenceBatch();
            };
            slideArrangePanel.Controls.Add(run);
        }
        private async Task<bool> LoadSelectedArrangeDrawings()
        {
            if (operationRunning || _isBatchRunning || _snapshotExportRunning || _fitAndCleanupRunning) return false;
            rbBatch.Checked = true;
            UpdateModeUi();
            await ExecuteTeklaCommandAsync("Load Selected", LoadSelectedDrawingsCore);
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
            await ExecuteTeklaCommandAsync("Arrange Reference", RunArrangeReferenceCore);
        }

        private void RunArrangeReferenceCore()
        {
            var handler = new DrawingHandler();
            if (_selectedDrawings.Count < 2)
            { SetMainStatus("Hãy chọn ít nhất 2 bản vẽ trong Document Manager để căn theo bản mẫu.", MainStatusKind.Warning); return; }
            if (handler.GetActiveDrawing() != null)
            { SetMainStatus("Hãy lưu và đóng bản vẽ đang mở trước khi căn cả danh sách.", MainStatusKind.Warning); return; }
            var drawings = _selectedDrawings.ToList();
            var modelPath = new Tekla.Structures.Model.Model().GetInfo().ModelPath;
            _stopRequested = false;
            var display = ReadUi(() =>
            {
                var form = new Form { Text = "Arrange View · Theo bản đầu", Width = 580, Height = 180,
                    StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false, ControlBox = false };
                var label = new Label { Dock = DockStyle.Fill, Padding = new Padding(16), Text = "Đang đọc bản chuẩn..." };
                var stop = new Button { Text = "Dừng sau bản hiện tại", Dock = DockStyle.Bottom, Height = 36 };
                stop.Click += delegate { _stopRequested = true; stop.Enabled = false; };
                form.Controls.Add(label); form.Controls.Add(stop);
                return Tuple.Create(form, label);
            });
            var progress = display.Item1;
            var status = display.Item2;
            int ok = 0, failed = 0; Drawing owned = null;
            _isBatchRunning = true;
            Ui(() => { btnRun.Text = "■  STOP"; btnRun.Enabled = true; });
            try
            {
                Ui(() => progress.Show(this));
                R.ArrangeReference reference = null;
                for (int i = 0; i < drawings.Count && !_stopRequested; i++)
                {
                    string progressText = (i + 1) + " / " + drawings.Count + " · " + SafeDrawingMark(drawings[i]);
                    Ui(() => status.Text = progressText);
                    if (_stopRequested) break;
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
                            int moved;
                            result = R.ApplyArrangeReference(reference, before, out moved);
                            // A fresh snapshot already proves an unchanged drawing is aligned.
                            // Avoid Save/reopen when this operation wrote nothing.
                            if (moved > 0)
                            {
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
                            }
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
                SetMainStatus((_stopRequested ? "Đã dừng" : failed > 0 ? (ok == 0 ? "Không căn được bản nào" : "Kết thúc có lỗi") : "Hoàn tất")
                    + " · căn " + ok + " bản · lỗi/bỏ qua " + failed
                    + " · chưa xử lý " + Math.Max(0, drawings.Count - 1 - ok - failed),
                    failed > 0 ? (ok == 0 ? MainStatusKind.Error : MainStatusKind.Warning) : MainStatusKind.Success);
            }
            catch (Exception ex) { SetMainStatus(ex.Message, MainStatusKind.Error); }
            finally
            {
                _isBatchRunning = false; _stopRequested = false;
                Ui(() => { progress.Close(); progress.Dispose(); btnRun.Text = "▶  CREATE DRAWING"; btnRun.Enabled = true; UpdateModeUi(); });
            }
        }

        private void SetArrangeRowResult(int index, string result)
        {
            if (InvokeRequired) { Ui(() => SetArrangeRowResult(index, result)); return; }
            if (index >= dgvDrawings.Rows.Count) return;
            var row = dgvDrawings.Rows[index];
            row.Cells["STATUS"].Value = result.StartsWith("ERROR") ? "ERROR" : result.StartsWith("REFERENCE") ? "REFERENCE" : "OK";
            row.Cells["RESULT"].Value = result;
            row.Cells["RESULT"].ToolTipText = result;
        }
    }
}
