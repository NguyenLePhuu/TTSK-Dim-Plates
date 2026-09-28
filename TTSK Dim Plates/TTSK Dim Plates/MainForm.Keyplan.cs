using System;
using System.Threading.Tasks;
using TTSK_AutoDim_Plates.Keyplan;

namespace TTSK_AutoDim_Plates
{
    public partial class MainForm
    {
        private bool keyplanOpen;
        private void RunKeyplanSlot09()
        {
            if(operationRunning||keyplanOpen||_isBatchRunning||_snapshotExportRunning||_fitAndCleanupRunning)return;
            keyplanOpen=true;
            try
            {
                var preferences=new KeyplanPreferences(KeyplanPreferences.DefaultPath);
                bool autoApply=false;
                try {
                    if(System.IO.File.Exists(KeyplanPreferences.DefaultPath))autoApply=preferences.Load();
                    else {
                        // Read the previous portable preference once, migrate to
                        // user storage; never create or edit files beside a model.
                        var previous=System.IO.Path.Combine(System.Windows.Forms.Application.StartupPath,"keyplan.cfg");
                        if(System.IO.File.Exists(previous)){autoApply=new KeyplanPreferences(previous).Load();preferences.Save(autoApply);}
                    }
                }
                catch(Exception ex) {SetMainStatus("Chưa đọc được cấu hình Keyplan: "+ex.Message,MainStatusKind.Warning);}
                using(var form=new KeyplanForm(RunKeyplanCheck,_darkMode,autoApply,preferences.Save))form.ShowDialog(this);
            }
            finally { keyplanOpen=false; }
        }
        private async Task<KeyplanRunResult> RunKeyplanCheck(KeyplanRunRequest request)
        {
            KeyplanRunResult result=null;Exception error=null;
            await ExecuteTeklaCommandAsync(request.AutoApply?"Slot 09 · Tự áp dụng keyplan":"Slot 09 · Đọc keyplan",()=>
            {
                try { result=KeyplanApply.Run(request); } catch(Exception ex) { error=ex; }
            });
            if(error!=null)throw error;
            if(result==null)throw new InvalidOperationException("Tekla đang bận; hãy thử lại sau khi lệnh hiện tại kết thúc.");
            return result;
        }
    }
}
