// Điều phối kiểm tra DIM theo danh sách bản vẽ; bộ phân loại vẫn là PHU_DimensionCheck.
// LUỒNG: Session giữ target/ngữ cảnh → kiểm từng Entry → Accept/Fail → PHU_DimensionReportForm.
// ĐIỂM SỬA: chuyển bản vẽ và kiểm model ở Session; kết luận chân DIM phải sửa ở PHU_DimensionCheck, không đổi Status để che lỗi.
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
        public sealed class Entry
        {
            public string Key, Name;
            public int Number;
            public D.Drawing Drawing;
            public bool Supported=true;
            public State Status=State.Pending;
            public PHU_DimensionCheck.Result Snapshot;
            public DateTime? CheckedAt;
            public string Problem="", Recovery="", Technical="";
            public int TotalFeet;
            public int Errors { get { return Snapshot==null?0:Snapshot.Findings.Count(f=>f.Status=="ERROR"); } }
            public int Reviews { get { return Snapshot==null?0:Snapshot.Findings.Count(f=>f.Status=="REVIEW"); } }
            public int Warnings { get { return Snapshot==null?0:Snapshot.Warnings.Count; } }
            public bool NeedsWork { get { return Status==State.NeedsAttention||Status==State.NeedsReview||Status==State.Failed||Status==State.Unsupported; } }
            public string StatusText { get {
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
            {Status=State.Pending;Snapshot=null;CheckedAt=null;TotalFeet=0;Problem=Recovery=Technical="";}
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
            void Open(Entry entry,bool visible);
            void CloseOwned(Entry entry);
            void ValidateActive(Entry entry);
            PHU_DimensionCheck.Result Analyze(Entry entry);
        }
        public sealed class Session
        {
            public readonly List<Entry> Entries;
            public readonly string ModelName;
            readonly IAdapter adapter;
            readonly Queue<Entry> queue=new Queue<Entry>();
            public bool Running {get;private set;}
            public string Notice {get;private set;}
            public Entry Current {get;private set;}
            public Session(IEnumerable<Entry> entries,IAdapter adapter,string modelName="")
            {
                Entries=entries.ToList();this.adapter=adapter;ModelName=modelName;
                if(Entries.Select(e=>e.Key).Distinct().Count()!=Entries.Count)throw new ArgumentException("Trùng định danh bản vẽ trong batch.");
                Notice="Chọn Bắt đầu để kiểm tra "+Entries.Count+" bản vẽ đã Load Selected.";
            }
            public bool Start(IEnumerable<Entry> entries)
            {
                if(Running)return false;
                var chosen=entries.Distinct().ToList();
                if(chosen.Any(e=>!Entries.Contains(e)))throw new ArgumentException("Bản vẽ không thuộc batch này.");
                try {adapter.EnsureContext();adapter.RequireClosed();}
                catch(Exception ex){Notice=ex.Message;return false;}
                queue.Clear();foreach(var entry in chosen){entry.Reset();queue.Enqueue(entry);}
                Running=queue.Count>0;Notice=Running?"Đang kiểm tra · có thể dừng sau bản hiện tại.":"Không có bản vẽ cần chạy.";
                return Running;
            }
            public void Pause()
            {Running=false;Current=null;Notice="Đã dừng · kết quả đã kiểm tra được giữ lại.";}
            // Called one drawing at a time by the UI's STA timer, never by a thread-pool worker.
            public void Step(Action progress=null)
            {
                if(!Running)return;
                try {adapter.EnsureContext();adapter.RequireClosed();}
                catch(Exception ex){Running=false;Notice=ex.Message;return;}
                var entry=queue.Dequeue();Current=entry;entry.Status=State.Checking;
                string stage="open";bool attemptedOpen=false;
                Exception failure=null;
                string earlierFailure=null;
                try
                {
                    if(progress!=null)progress();
                    if(!entry.Supported)
                    {entry.Status=State.Unsupported;entry.Problem="Chỉ hỗ trợ bản vẽ Assembly và SinglePart.";entry.Recovery="Kiểm tra thủ công loại bản vẽ này.";}
                    else
                    {
                        attemptedOpen=true;adapter.Open(entry,false);stage="analyze";
                        var fresh=adapter.Analyze(entry);adapter.EnsureContext();entry.Accept(fresh);
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
                    entry.Fail(failure,stage);
                    if(earlierFailure!=null)entry.Technical=earlierFailure+"\r\nĐóng bản vẽ: "+entry.Technical;
                    if(failure is ContextException)Running=false;
                    if(!Running)Notice=entry.Problem+" "+entry.Recovery;
                }
                Current=null;
                if(queue.Count==0&&Running){Running=false;Notice="Đã kiểm tra xong · chọn bản vẽ để xem hoặc kiểm tra lại.";}
            }
            public PHU_DimensionCheck.Result OpenForReview(Entry entry)
            {
                if(Running)throw new InvalidOperationException("Dừng batch trước khi xem chi tiết.");
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
                    adapter.EnsureContext();var fresh=adapter.Analyze(entry);adapter.EnsureContext();entry.Accept(fresh);return fresh;
                }
                catch(Exception ex){entry.Fail(ex,"analyze");throw;}
            }
            public string ExportText()
            {
                var text=new StringBuilder("CHECK DIM · BATCH\r\nModel: "+ModelName+"\r\nXuất lúc "+DateTime.Now.ToString("dd/MM/yyyy HH:mm")+"\r\n");
                foreach(var entry in Entries)
                {
                    text.AppendLine().AppendLine(entry.Number.ToString("000")+" · "+entry.Name+" · "+entry.StatusText)
                        .AppendLine("ID: "+entry.Key).AppendLine(entry.Summary);
                    if(entry.CheckedAt.HasValue)text.AppendLine("Kiểm tra: "+entry.CheckedAt.Value.ToString("dd/MM/yyyy HH:mm:ss"));
                    if(entry.Recovery.Length>0)text.AppendLine("Cách xử lý: "+entry.Recovery);
                    if(entry.Technical.Length>0)text.AppendLine("Chi tiết: "+entry.Technical);
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
                EnsureContext();
                if(handler.GetActiveDrawing()!=null)throw new ContextException("Hãy lưu/đóng bản vẽ đang mở trong Tekla, rồi bấm Tiếp tục.");
            }
            static bool Same(D.Drawing drawing,Entry entry)
            {return drawing!=null&&drawing.IsSameDatabaseObject(entry.Drawing);}
            public void Open(Entry entry,bool visible)
            {
                EnsureContext();var active=handler.GetActiveDrawing();
                if(active!=null)
                {
                    if(visible&&Same(active,entry))return;
                    throw new ContextException("Một bản vẽ khác đang mở. Lưu/đóng bản đó trước khi chuyển bản vẽ.");
                }
                owned=entry;
                if(!handler.SetActiveDrawing(entry.Drawing,visible))throw new InvalidOperationException("Tekla từ chối mở bản vẽ.");
                EnsureContext();
                if(!Same(handler.GetActiveDrawing(),entry))throw new ContextException("Bản vẽ đang mở không đúng bản vẽ cần kiểm tra.");
                if(visible)owned=null; // User owns this visible drawing; never auto-close their edits.
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
                EnsureContext();var active=handler.GetActiveDrawing();
                if(active==null){owned=null;return;}
                if(!Same(active,entry))throw new ContextException("Bản vẽ đã chuyển trong lúc chạy. Batch đã dừng và giữ nguyên bản đang mở.");
                if(!handler.CloseActiveDrawing(false)||handler.GetActiveDrawing()!=null)
                    throw new InvalidOperationException("Tekla chưa đóng được bản vẽ.");
                owned=null;
            }
        }
    }
}
