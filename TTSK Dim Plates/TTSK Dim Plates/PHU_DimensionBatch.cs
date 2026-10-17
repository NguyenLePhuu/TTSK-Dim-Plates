// Điều phối kiểm tra DIM theo danh sách bản vẽ; bộ phân loại vẫn là PHU_DimensionCheck.
// LUỒNG: Session giữ hàng đợi/ngữ cảnh → ưu tiên active đúng target → Open/Analyze/CloseOwned → Accept/Fail; combo Scale → DIM → Góc Assembly dùng cùng phiên.
// ĐIỂM SỬA: target/tiến độ ở Session; TeklaAdapter dùng chung vòng đời bản vẽ cho Check DIM, Check Scale và mở dòng.
// Kết luận chân DIM ở PHU_DimensionCheck, không đổi Status để che lỗi.
// TRA CỨU: PROJECT_MAP.md ở gốc repo; chỉ mục kiểu/hàm và điểm chỉnh thông số.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using D = Tekla.Structures.Drawing;
using M = Tekla.Structures.Model;

namespace Tekla.Technology.Akit.UserScript
{
    // Chỉ điều phối batch; kết luận chân DIM lấy từ bộ phân loại chỉ đọc hiện tại.
    public static class PHU_DimensionBatch
    {
        public enum State { Pending, Checking, NeedsAttention, NeedsReview, Passed, Failed, Unsupported }
        // Kết quả từng bước của combo, không dùng lỗi đọc dữ liệu làm kết luận sai bản vẽ.
        public enum CheckState { Pending, Checking, Passed, Error, Unverified }
        public sealed class ScaleSample {public string Name;public double? Scale;}
        public static void EvaluateScale(CheckItem result,string title,IEnumerable<ScaleSample> source)
        {
            result.Details.Clear();
            var values=source.ToList();double numerator=0,denominator=0;
            var ratio=(title??"").Replace(" ","").Split(':');
            bool readable=ratio.Length==2&&double.TryParse(ratio[0],System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,out numerator)&&numerator>0&&
                double.TryParse(ratio[1],System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out denominator)&&
                denominator>0&&!double.IsInfinity(numerator)&&!double.IsInfinity(denominator);
            double expected=readable?denominator/numerator:0;
            readable=readable&&expected>0&&!double.IsInfinity(expected);
            int wrong=0,unknown=0;
            if(!readable){unknown++;result.Details.Add("Chưa đọc được tỉ lệ Title 3.");}
            if(values.Count==0){unknown++;result.Details.Add("Không có view trong phạm vi Check Scale.");}
            foreach(var view in values)
            {
                bool valid=view.Scale.HasValue&&view.Scale.Value>0&&!double.IsInfinity(view.Scale.Value)&&!double.IsNaN(view.Scale.Value);
                if(!valid){unknown++;result.Details.Add(view.Name+" · Chưa đọc được tỉ lệ.");}
                // Sai số mẫu số tỉ lệ 0,0001; không dùng số đã làm tròn để báo đạt.
                else if(readable&&Math.Abs(view.Scale.Value-expected)>0.0001)
                {wrong++;result.Details.Add(view.Name+" · 1:"+view.Scale.Value.ToString("0.###",System.Globalization.CultureInfo.InvariantCulture)+" ≠ Title 3 "+title);}
            }
            result.Status=wrong>0?CheckState.Error:unknown>0?CheckState.Unverified:CheckState.Passed;
            result.Summary=wrong>0?wrong+" view sai"+(unknown>0?" · thiếu dữ liệu":""):
                unknown>0?"Chưa đọc đủ tỉ lệ":title+" · "+values.Count+" view";
        }
        public sealed class CheckItem
        {
            public string Name, Summary="";
            public CheckState Status;
            public readonly List<string> Details=new List<string>();
            // UI đọc bản đã công bố, không duyệt Details khi worker đang bổ sung dữ liệu.
            internal volatile CheckItem Published;
            internal void Publish()
            {
                var copy=new CheckItem {Name=Name,Summary=Summary,Status=Status};
                copy.Details.AddRange(Details);Published=copy;
            }
            public string StatusText {get {return Status==CheckState.Passed?"Đúng":Status==CheckState.Error?"Sai":
                Status==CheckState.Unverified?"Chưa xác minh":Status==CheckState.Checking?"Đang kiểm tra":"Chờ kiểm tra";}}
        }
        public sealed class ComboResult
        {
            public readonly CheckItem Scale=new CheckItem {Name="Scale"};
            public readonly CheckItem Dim=new CheckItem {Name="DIM"};
            public readonly CheckItem Rotation;
            public PHU_DimensionCheck.Result Dimensions;
            public ComboResult(bool assembly) {if(assembly)Rotation=new CheckItem {Name="Góc quay"};}
            public IEnumerable<CheckItem> Checks {get {yield return Scale;yield return Dim;if(Rotation!=null)yield return Rotation;}}
            public State Status {get {return Checks.Any(c=>c.Status==CheckState.Error)?State.NeedsAttention:
                Checks.All(c=>c.Status==CheckState.Passed)?State.Passed:State.NeedsReview;}}
            public string Summary {get {return string.Join(" · ",Checks.Where(c=>c.Status!=CheckState.Passed)
                .Select(c=>c.Name+": "+c.Summary));}}
            public void AcceptDimensions(PHU_DimensionCheck.Result fresh)
            {
                Dimensions=fresh;Dim.Details.Clear();
                int errors=fresh.Findings.Count(f=>f.Status=="ERROR"),reviews=fresh.Findings.Count(f=>f.Status=="REVIEW");
                Dim.Status=errors>0?CheckState.Error:reviews>0||fresh.Warnings.Count>0||fresh.Dimensions==0?CheckState.Unverified:CheckState.Passed;
                Dim.Summary=errors>0?errors+" chân DIM sai":reviews>0?reviews+" chân cần xác minh":fresh.Dimensions==0?"Không có DIM thẳng":
                    fresh.Warnings.Count>0?"Chưa đọc đủ dữ liệu":fresh.Dimensions+" chuỗi DIM thẳng";
                foreach(var finding in fresh.Findings.Where(f=>f.Status=="ERROR"||f.Status=="REVIEW"))
                    Dim.Details.Add(finding.ViewLabel+" · "+finding.DimensionLabel+" · "+finding.Location+"\r\n"+PHU_DimensionCheck.UserReason(finding));
                Dim.Details.AddRange(fresh.Warnings.Distinct());
                if(fresh.SkippedAngles.Count>0)Dim.Details.Add("Ngoài phạm vi: "+fresh.SkippedAngles.Count+" DIM góc.");
            }
        }
        public sealed class Entry
        {
            public string Key, Name;
            public int Number;
            public D.Drawing Drawing;
            public bool Supported=true;
            public State Status=State.Pending;
            public PHU_DimensionCheck.Result Snapshot;
            public ComboResult Combo;
            public DateTime? CheckedAt;
            public string Problem="", Recovery="", Technical="";
            public int TotalFeet;
            public int Errors { get { return Snapshot==null?0:Snapshot.Findings.Count(f=>f.Status=="ERROR"); } }
            public int Reviews { get { return Snapshot==null?0:Snapshot.Findings.Count(f=>f.Status=="REVIEW"); } }
            public int Warnings { get { return Snapshot==null?0:Snapshot.Warnings.Count; } }
            public bool NeedsWork { get { return Status==State.NeedsAttention||Status==State.NeedsReview||Status==State.Failed||Status==State.Unsupported; } }
            public string StatusText { get {
                if(Combo!=null&&Status!=State.Checking&&Status!=State.Pending&&Status!=State.Failed&&Status!=State.Unsupported)
                    return Status==State.Passed?"Đúng":Status==State.NeedsAttention?"Có lỗi":"Chưa xác minh";
                switch(Status)
                {
                    case State.Checking:return "Đang kiểm tra";
                    case State.NeedsAttention:return "DIM cần xem";
                    case State.NeedsReview:return Snapshot!=null&&Snapshot.Dimensions==0&&Snapshot.SkippedAngles.Count>0?"Không có DIM thẳng":"Cần xác minh";
                    case State.Passed:return Snapshot!=null&&Snapshot.SkippedAngles.Count>0?"DIM thẳng: không lỗi":"Không phát hiện lỗi";
                    case State.Failed:return "Chưa kiểm tra được";
                    case State.Unsupported:return "Chưa hỗ trợ";
                    default:return "Chờ kiểm tra";
                }
            } }
            public string Summary { get {
                if(Status==State.Failed||Status==State.Unsupported)return Problem;
                if(Combo!=null)return Combo.Summary;
                if(Snapshot==null)return Status==State.Checking?"Đang đọc DIM của bản vẽ này…":"";
                var parts=new List<string>();
                if(Errors>0)parts.Add(Errors+" chân cần xem / "+Snapshot.Findings.Where(f=>f.Status=="ERROR").Select(f=>f.Dimension).Distinct().Count()+" chuỗi");
                if(Reviews>0)parts.Add(Reviews+" chân chưa kết luận");
                if(Warnings>0)parts.Add(Warnings+" lưu ý dữ liệu");
                parts.Add(Snapshot.Dimensions+" chuỗi đã đọc · "+Snapshot.Views+" hình chiếu");
                if(Snapshot.SkippedAngles.Count>0)parts.Add("Bỏ qua "+Snapshot.SkippedAngles.Count+" DIM góc");
                return String.Join("   ·   ",parts);
            } }
            public void Reset()
            {Status=State.Pending;Snapshot=null;Combo=null;CheckedAt=null;TotalFeet=0;Problem=Recovery=Technical="";}
            public void AcceptCombo(ComboResult fresh)
            {
                if(fresh==null)throw new InvalidOperationException("Chưa có kết quả combo.");
                Snapshot=null;TotalFeet=0;Problem=Recovery=Technical="";
                if(fresh.Dimensions!=null)Accept(fresh.Dimensions);
                // Batch chỉ giữ lỗi DIM và kết quả gọn, không giữ đồ thị hình học/live handles.
                fresh.Dimensions=null;Combo=fresh;Status=fresh.Status;CheckedAt=DateTime.Now;
            }
            public void Accept(PHU_DimensionCheck.Result fresh)
            {
                // Do not retain every drawing's geometry graph or live DrawingObject handles.
                var compact=new PHU_DimensionCheck.Result {Drawing=fresh.Drawing,Main=fresh.Main,Views=fresh.Views,Dimensions=fresh.Dimensions};
                compact.Findings.AddRange(fresh.Findings.Where(f=>f.Status=="ERROR"||f.Status=="REVIEW"));
                compact.Warnings.AddRange(fresh.Warnings.Distinct());
                compact.SkippedAngles.AddRange(fresh.SkippedAngles);
                if(fresh.Dimensions==0&&compact.Warnings.Count==0)compact.Warnings.Add("Không có DIM được kiểm tra.");
                Snapshot=compact;TotalFeet=fresh.Findings.Count;CheckedAt=DateTime.Now;Problem=Recovery=Technical="";
                Status=Errors>0?State.NeedsAttention:Reviews>0||Warnings>0?State.NeedsReview:State.Passed;
            }
            public void Fail(Exception error,string stage)
            {
                Reset();Status=State.Failed;CheckedAt=DateTime.Now;Technical=error.GetBaseException().Message;
                var interruption=error as ContextException;
                if(interruption!=null) {Problem=interruption.Message;Recovery="Khôi phục đúng model/bản vẽ rồi thử lại.";return;}
                if(error.GetType().Name.IndexOf("NotUpToDate",StringComparison.OrdinalIgnoreCase)>=0)
                {Problem="Bản vẽ chưa cập nhật.";Recovery="Cập nhật bản vẽ trong Document Manager rồi thử lại.";}
                else if(stage=="open")
                {Problem="Không mở được bản vẽ.";Recovery="Kiểm tra bản vẽ còn tồn tại và có thể mở trong Tekla, rồi thử lại.";}
                else if(stage=="close")
                {Problem="Chưa đóng được bản vẽ sau khi kiểm tra.";Recovery="Đóng bản vẽ trong Tekla rồi tiếp tục batch.";}
                else if(Technical.IndexOf("main",StringComparison.OrdinalIgnoreCase)>=0)
                {Problem="Chưa xác định được cấu kiện chính.";Recovery="Kiểm tra cấu kiện và liên kết của bản vẽ trong model.";}
                else
                {Problem="Chưa đọc đủ dữ liệu để kiểm tra.";Recovery="Mở bản vẽ kiểm tra dữ liệu, sau đó thử lại.";}
            }
        }
        public sealed class ContextException : InvalidOperationException
        {public ContextException(string message):base(message){} }
        public interface IAdapter
        {
            void EnsureContext();
            void RequireClosed();
            Entry FindActive(IEnumerable<Entry> entries);
            // Đúng active thì mượn; visible giao cho người dùng; chỉ hidden tự mở mới được đóng.
            void Open(Entry entry,bool visible);
            void CloseOwned(Entry entry);
            void ValidateActive(Entry entry);
            PHU_DimensionCheck.Result Analyze(Entry entry);
        }
        public static void RunComboStep(CheckItem step,Entry entry,IAdapter adapter,Action progress,Action read)
        {
            adapter.ValidateActive(entry);step.Status=CheckState.Checking;step.Publish();if(progress!=null)progress();
            try {read();}
            catch(ContextException){throw;}
            catch(Exception ex)
            {step.Status=CheckState.Unverified;step.Summary="Chưa đọc đủ dữ liệu";step.Details.Add(ex.GetBaseException().Message);}
            adapter.ValidateActive(entry);step.Publish();if(progress!=null)progress();
        }
        public sealed class Session
        {
            public readonly List<Entry> Entries;
            public readonly string ModelName;
            readonly IAdapter adapter;
            readonly Func<Entry,Action,ComboResult> comboAnalyze;
            public bool IsCombo {get {return comboAnalyze!=null;}}
            readonly List<Entry> queue=new List<Entry>();
            bool stepping;
            // Hàng đợi của lượt chạy, độc lập với kết quả cũ đang hiển thị.
            public int RemainingCount {get {return queue.Count;}}
            public bool Running {get;private set;}
            public string Notice {get;private set;}
            public Entry Current {get;private set;}
            public Session(IEnumerable<Entry> entries,IAdapter adapter,string modelName="",Func<Entry,Action,ComboResult> comboAnalyze=null)
            {
                Entries=entries.ToList();this.adapter=adapter;ModelName=modelName;this.comboAnalyze=comboAnalyze;
                if(Entries.Select(e=>e.Key).Distinct().Count()!=Entries.Count)throw new ArgumentException("Trùng định danh bản vẽ trong batch.");
                Notice=IsCombo?"Sẵn sàng kiểm tra "+Entries.Count+" bản vẽ.":"Chọn Bắt đầu để kiểm tra "+Entries.Count+" bản vẽ đã Load Selected.";
            }
            public bool Start(IEnumerable<Entry> entries)
            {
                if(Running||stepping)return false;
                var chosen=entries.Distinct().ToList();
                if(chosen.Any(e=>!Entries.Contains(e)))throw new ArgumentException("Bản vẽ không thuộc batch này.");
                try {adapter.EnsureContext();}
                catch(Exception ex){Notice=ex.Message;return false;}
                queue.Clear();queue.AddRange(chosen);
                Running=queue.Count>0;Notice=Running?"Đang kiểm tra · có thể dừng sau bản hiện tại.":"Không có bản vẽ cần chạy.";
                return Running;
            }
            public bool Continue()
            {
                if(Running||stepping||queue.Count==0)return false;
                try {adapter.EnsureContext();}
                catch(Exception ex){Notice=ex.Message;return false;}
                Running=true;Notice="Đang tiếp tục các bản vẽ chưa xử lý.";return true;
            }
            public void Pause()
            {Running=false;if(!stepping)Current=null;Notice="Đã dừng · kết quả đã kiểm tra được giữ lại.";}
            // Mỗi lần một bản trên STA: DIM cũ ở UI; combo dùng TeklaOperationDispatcher.
            public void Step(Action progress=null)
            {
                if(!Running||stepping)return;
                stepping=true;
                try {StepCore(progress);}
                finally {Current=null;stepping=false;}
            }
            void StepCore(Action progress)
            {
                Entry entry;
                try
                {
                    adapter.EnsureContext();
                    if(queue.Count==0){Running=false;return;}
                    // Kiểm tại chỗ bản người dùng đang mở trước, không đổi thứ tự danh sách UI.
                    entry=adapter.FindActive(queue)??queue[0];
                }
                catch(Exception ex){Running=false;Notice=ex.Message;return;}
                Current=entry;
                string stage="open";bool attemptedOpen=false;
                bool acquired=false;
                Exception failure=null;
                string earlierFailure=null;
                try
                {
                    if(!entry.Supported)
                    {entry.Reset();entry.Status=State.Unsupported;entry.Problem="Chỉ hỗ trợ bản vẽ Assembly và SinglePart.";entry.Recovery="Kiểm tra thủ công loại bản vẽ này.";}
                    else
                    {
                        attemptedOpen=true;adapter.Open(entry,false);acquired=true;stage="analyze";
                        entry.Reset();entry.Status=State.Checking;
                        if(progress!=null)progress();
                        AnalyzeAndAccept(entry,progress);
                    }
                }
                catch(Exception ex){failure=ex;}
                finally
                {
                    if(attemptedOpen)
                    {
                        try {adapter.CloseOwned(entry);}
                        catch(Exception ex){earlierFailure=failure==null?null:failure.Message;failure=ex;stage="close";Running=false;}
                    }
                }
                if(failure!=null)
                {
                    // Chưa lấy được bản cần kiểm: giữ cả kết quả cũ lẫn hàng đợi để tiếp tục.
                    if(!acquired&&failure is ContextException)
                    {Running=false;Notice=failure.Message+" Sau đó bấm Tiếp tục; hoặc Đóng báo cáo để sửa DIM.";return;}
                    entry.Fail(failure,stage);
                    if(earlierFailure!=null)entry.Technical=earlierFailure+"\r\nĐóng bản vẽ: "+entry.Technical;
                    if(failure is ContextException)Running=false;
                    if(!Running)Notice=entry.Problem+" "+entry.Recovery;
                }
                queue.Remove(entry);
                Current=null;
                if(queue.Count==0&&Running){Running=false;Notice="Đã kiểm tra xong · chọn bản vẽ để xem hoặc kiểm tra lại.";}
            }
            public PHU_DimensionCheck.Result OpenForReview(Entry entry)
            {
                if(Running||stepping)throw new InvalidOperationException("Dừng batch trước khi xem chi tiết.");
                if(!Entries.Contains(entry)||!entry.Supported)throw new InvalidOperationException("Bản vẽ không hỗ trợ kiểm tra.");
                adapter.EnsureContext();
                try {adapter.Open(entry,true);}
                catch(ContextException){throw;}
                catch(Exception ex){entry.Fail(ex,"open");throw;}
                return RefreshOpen(entry);
            }
            public void EnsureReviewContext(Entry entry)
            {
                if(!Entries.Contains(entry))throw new InvalidOperationException("Bản vẽ không thuộc batch này.");
                adapter.ValidateActive(entry);
            }
            public PHU_DimensionCheck.Result RefreshOpen(Entry entry)
            {
                if(!Entries.Contains(entry))throw new InvalidOperationException("Bản vẽ không thuộc batch này.");
                try
                {
                    return AnalyzeAndAccept(entry,null);
                }
                catch(Exception ex){entry.Fail(ex,"analyze");throw;}
            }
            PHU_DimensionCheck.Result AnalyzeAndAccept(Entry entry,Action progress)
            {
                if(!IsCombo){adapter.EnsureContext();var fresh=adapter.Analyze(entry);adapter.EnsureContext();entry.Accept(fresh);return fresh;}
                adapter.ValidateActive(entry);
                entry.Reset();entry.Status=State.Checking;
                var combo=comboAnalyze(entry,progress);
                adapter.ValidateActive(entry);
                var dimensions=combo.Dimensions;entry.AcceptCombo(combo);return dimensions;
            }
            public string ExportText()
            {
                var text=new StringBuilder((IsCombo?"CHECK BẢN VẼ · COMBO":"CHECK DIM · BATCH")+"\r\nModel: "+ModelName+"\r\nXuất lúc "+DateTime.Now.ToString("dd/MM/yyyy HH:mm")+"\r\n");
                foreach(var entry in Entries)
                {
                    text.AppendLine().AppendLine(entry.Number.ToString("000")+" · "+entry.Name+" · "+entry.StatusText)
                        .AppendLine("ID: "+entry.Key).AppendLine(entry.Summary);
                    if(entry.CheckedAt.HasValue)text.AppendLine("Kiểm tra: "+entry.CheckedAt.Value.ToString("dd/MM/yyyy HH:mm:ss"));
                    if(entry.Recovery.Length>0)text.AppendLine("Cách xử lý: "+entry.Recovery);
                    if(entry.Technical.Length>0)text.AppendLine("Chi tiết: "+entry.Technical);
                    if(entry.Combo!=null)
                    {
                        foreach(var check in entry.Combo.Checks)
                        {text.AppendLine(check.Name+" · "+check.StatusText+" · "+check.Summary);foreach(var detail in check.Details)text.AppendLine("  "+detail);}
                        continue;
                    }
                    if(entry.Snapshot==null)continue;
                    foreach(var finding in entry.Snapshot.Findings)
                        text.AppendLine("  "+finding.Status+" · "+finding.ViewLabel+" · "+finding.DimensionLabel+" · "+finding.Location)
                            .AppendLine("    "+PHU_DimensionCheck.UserReason(finding));
                    foreach(string warning in entry.Snapshot.Warnings)text.AppendLine("  Lưu ý dữ liệu: "+warning);
                    foreach(string skipped in entry.Snapshot.SkippedAngles)text.AppendLine("  Bỏ qua: "+skipped);
                }
                return text.ToString();
            }
        }
        public sealed class TeklaAdapter : IAdapter
        {
            readonly string modelPath;
            readonly D.DrawingHandler handler=new D.DrawingHandler();
            Entry owned;
            public TeklaAdapter(string modelPath){this.modelPath=modelPath;}
            public void EnsureContext()
            {
                var model=new M.Model();
                if(!model.GetConnectionStatus()||!handler.GetConnectionStatus())throw new ContextException("Mất kết nối Tekla. Kết nối lại rồi tiếp tục.");
                if(!String.Equals(model.GetInfo().ModelPath,modelPath,StringComparison.OrdinalIgnoreCase))
                    throw new ContextException("Model đã thay đổi. Trở về model cũ hoặc Load Selected lại.");
            }
            public void RequireClosed()
            {
                // Giữ API tương thích; kiểm chỉ đọc dùng Open theo target, không dùng rào đóng toàn cục.
                EnsureContext();
                if(handler.GetActiveDrawing()!=null)throw new ContextException("Hãy lưu/đóng bản vẽ đang mở trong Tekla, rồi bấm Tiếp tục.");
            }
            public Entry FindActive(IEnumerable<Entry> entries)
            {
                EnsureContext();var active=handler.GetActiveDrawing();
                return entries.FirstOrDefault(entry=>Same(active,entry));
            }
            static bool Same(D.Drawing drawing,Entry entry)
            {return drawing!=null&&drawing.IsSameDatabaseObject(entry.Drawing);}
            public void Open(Entry entry,bool visible)
            {
                // Quyền đóng chỉ sống trong một lần Open/CloseOwned, không kế thừa lượt lỗi trước.
                owned=null;
                EnsureContext();var active=handler.GetActiveDrawing();
                if(active!=null)
                {
                    if(Same(active,entry))return;
                    throw new ContextException("Đang giữ bản vẽ bạn mở trong Tekla. Hãy lưu/đóng bản đó trước khi chuyển sang bản được chọn.");
                }
                owned=visible?null:entry;
                if(!handler.SetActiveDrawing(entry.Drawing,visible))throw new InvalidOperationException("Tekla từ chối mở bản vẽ.");
                EnsureContext();
                if(!Same(handler.GetActiveDrawing(),entry))throw new ContextException("Bản vẽ đang mở không đúng bản vẽ cần kiểm tra.");
            }
            public PHU_DimensionCheck.Result Analyze(Entry entry)
            {
                ValidateActive(entry);
                var fresh=PHU_DimensionCheck.Analyze();EnsureContext();
                if(!Same(fresh.SourceDrawing,entry)||!Same(handler.GetActiveDrawing(),entry))
                    throw new ContextException("Bản vẽ thay đổi trong lúc đọc. Chưa ghi nhận kết quả.");
                return fresh;
            }
            public void ValidateActive(Entry entry)
            {
                EnsureContext();
                if(!Same(handler.GetActiveDrawing(),entry))throw new ContextException("Bản vẽ đã thay đổi. Mở lại đúng bản vẽ từ danh sách batch.");
            }
            public void CloseOwned(Entry entry)
            {
                if(!ReferenceEquals(owned,entry))return;
                // Dù mất model/đổi active/đóng lỗi, không nhận lại quyền đóng ở lần chạy sau.
                owned=null;
                EnsureContext();var active=handler.GetActiveDrawing();
                if(active==null)return;
                if(!Same(active,entry))throw new ContextException("Bản vẽ đã chuyển trong lúc chạy. Batch đã dừng và giữ nguyên bản đang mở.");
                if(!handler.CloseActiveDrawing(false)||handler.GetActiveDrawing()!=null)
                    throw new InvalidOperationException("Tekla chưa đóng được bản vẽ.");
            }
        }
    }
}
