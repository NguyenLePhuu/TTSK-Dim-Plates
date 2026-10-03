using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using D = Tekla.Structures.Drawing;

namespace TTSK_AutoDim_Plates
{
    public partial class MainForm
    {
        // Worker-only read/resolve; publish a complete, model-validated snapshot.
        private void LoadContextDrawingsCore()
        {
            var model=new Tekla.Structures.Model.Model();
            var handler=new D.DrawingHandler();
            if(!model.GetConnectionStatus()||!handler.GetConnectionStatus())
                throw new InvalidOperationException("Chưa kết nối được Tekla Model/Drawing.");
            string modelPath=model.GetInfo().ModelPath;
            bool preferActive=ReadUi(()=>rbActive.Checked);
            var targets=DrawingTargetSelection.Resolve(preferActive,handler.GetActiveDrawing(),()=>
            {
                int[] indices=ReadUi(()=>dgvDrawings.SelectedRows.Cast<DataGridViewRow>()
                    .Where(r=>!r.IsNewRow).Select(r=>r.Index).OrderBy(i=>i).ToArray());
                var selected=new List<D.Drawing>();
                if(indices.Length==0)return selected;
                if(!string.Equals(modelPath,dimensionCheckModelPath,StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Danh sách thuộc model khác. Hãy Load Selected lại.");
                foreach(int i in indices)
                {
                    if(i<0||i>=_selectedDrawings.Count||_selectedDrawings[i]==null)
                        throw new InvalidOperationException("Dòng đã chọn không còn hợp lệ. Hãy Load Selected lại.");
                    selected.Add(_selectedDrawings[i]);
                }
                return selected;
            },()=>
            {
                var selected=new List<D.Drawing>();
                var enumerator=handler.GetDrawingSelector().GetSelected();
                while(enumerator.MoveNext())if(enumerator.Current!=null)selected.Add(enumerator.Current);
                return selected;
            },(a,b)=>a.IsSameDatabaseObject(b));
            if(targets.Drawings.Count==0)
                throw new InvalidOperationException("Hãy mở một bản vẽ hoặc chọn bản vẽ trong danh sách / Document Manager.");
            ResetDimensionCheckBatch();
            dimensionCheckModelPath=modelPath;
            _resumeIndex=0;_stopRequested=false;
            _selectedDrawings.Clear();_selectedDrawings.AddRange(targets.Drawings);
            Ui(()=>
            {
                rbActive.Checked=targets.IsActive;rbBatch.Checked=!targets.IsActive;
                UpdateModeUi();
            });
            RefreshContextDrawingRows();
            SetMainStatus("Đã chọn "+targets.Drawings.Count+" bản từ "+targets.Source,MainStatusKind.Information);
        }

        // Chỉ hiển thị lô >1 bản; giữ đủ đối tượng xử lý kể cả khi bảng trống.
        private void RefreshContextDrawingRows()
        {
            Ui(()=>dgvDrawings.Rows.Clear());
            if(_selectedDrawings.Count>1)
            for(int i=0;i<_selectedDrawings.Count;i++)
            {
                var d=_selectedDrawings[i];
                AddDrawingGridRow(i+1,SafeDrawingMark(d),SafeDrawingRevision(d),SafeDrawingChanges(d));
            }
            Ui(()=>{dgvDrawings.ClearSelection();lblCount.Text=_selectedDrawings.Count==1
                ? "Xử lý 1 bản vẽ" : "Tổng số bản vẽ:  "+_selectedDrawings.Count;});
        }
    }
}
