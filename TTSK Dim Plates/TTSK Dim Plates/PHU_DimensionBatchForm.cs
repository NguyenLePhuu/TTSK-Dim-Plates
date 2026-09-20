using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using B = Tekla.Technology.Akit.UserScript.PHU_DimensionBatch;

namespace Tekla.Technology.Akit.UserScript
{
    public sealed partial class PHU_DimensionReportForm
    {
        B.Session batchSession;
        Timer batchTimer;
        Panel batchViewport;
        FlowLayoutPanel batchCards;
        RevisionScroll batchScroll;
        Label batchSummary,batchNotice,batchEmpty;
        Button batchStart,batchRetry,batchExport;
        Button batchFilter;
        ContextMenuStrip batchFilterMenu;
        int batchFilterIndex;
        readonly string[] batchFilterNames={"Tất cả bản vẽ","Cần xử lý","Chưa kiểm tra được","Không phát hiện lỗi","Chờ kiểm tra","Chưa hỗ trợ"};
        TextBox batchSearch;
        sealed class BatchSearchBox : Panel
        {
            public readonly TextBox Editor;
            readonly Label placeholder,clear;
            readonly Color surface,soft,border,accent,muted;
            public BatchSearchBox(Color surface,Color soft,Color border,Color accent,Color ink,Color muted,Font font)
            {
                this.surface=surface;this.soft=soft;this.border=border;this.accent=accent;this.muted=muted;
                DoubleBuffered=true;ResizeRedraw=true;Cursor=Cursors.IBeam;
                Editor=new TextBox {Name="BatchSearch",BorderStyle=BorderStyle.None,BackColor=surface,ForeColor=ink,Font=font};
                Editor.AccessibleName="Tìm theo tên hoặc mã bản vẽ";
                placeholder=new Label {Text="Tìm mã hoặc tên bản vẽ…",ForeColor=muted,BackColor=surface,Font=font,AutoEllipsis=true,TextAlign=ContentAlignment.MiddleLeft,Cursor=Cursors.IBeam};
                clear=new Label {Name="ClearBatchSearch",Text="×",ForeColor=muted,BackColor=surface,Font=font,TextAlign=ContentAlignment.MiddleCenter,Cursor=Cursors.Hand,Visible=false,TabStop=false};
                clear.AccessibleName="Xóa nội dung tìm kiếm";
                Controls.Add(Editor);Controls.Add(placeholder);Controls.Add(clear);
                placeholder.Click+=(s,e)=>Editor.Focus();clear.Click+=(s,e)=>{Editor.Clear();Editor.Focus();};
                Editor.TextChanged+=(s,e)=>UpdateAppearance();Editor.GotFocus+=(s,e)=>UpdateAppearance();Editor.LostFocus+=(s,e)=>UpdateAppearance();
                Click+=(s,e)=>Editor.Focus();SizeChanged+=(s,e)=>Arrange();
            }
            void Arrange()
            {
                int h=Editor.PreferredHeight;
                Editor.SetBounds(40,Math.Max(2,(Height-h-3)/2),Math.Max(20,Width-80),h);
                placeholder.Bounds=Editor.Bounds;clear.SetBounds(Math.Max(0,Width-36),3,28,Math.Max(1,Height-8));
            }
            void UpdateAppearance()
            {
                Color fill=Editor.Focused?soft:surface;
                Editor.BackColor=placeholder.BackColor=clear.BackColor=fill;
                placeholder.Visible=Editor.TextLength==0;clear.Visible=Editor.TextLength>0;Invalidate();
            }
            protected override void OnPaintBackground(PaintEventArgs e)
            {
                e.Graphics.Clear(Parent==null?surface:Parent.BackColor);
                e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                var face=new Rectangle(1,1,Math.Max(1,Width-3),Math.Max(1,Height-4));
                using(var path=Rounded(face,12))
                {
                    using(var brush=new SolidBrush(Editor.Focused?soft:surface))e.Graphics.FillPath(brush,path);
                    using(var pen=new Pen(Editor.Focused?accent:border,1))e.Graphics.DrawPath(pen,path);
                }
                int y=Height/2-7;
                using(var pen=new Pen(Editor.Focused?accent:muted,1.7f))
                {e.Graphics.DrawEllipse(pen,15,y,10,10);e.Graphics.DrawLine(pen,23,y+8,28,y+13);}
            }
        }
        sealed class BatchMenuColors : ProfessionalColorTable
        {
            readonly Color surface,selected,border;
            public BatchMenuColors(Color surface,Color selected,Color border)
            {this.surface=surface;this.selected=selected;this.border=border;UseSystemColors=false;}
            public override Color ToolStripDropDownBackground {get{return surface;}}
            public override Color MenuItemSelected {get{return selected;}}
            public override Color MenuItemBorder {get{return border;}}
            public override Color MenuBorder {get{return border;}}
            public override Color ImageMarginGradientBegin {get{return surface;}}
            public override Color ImageMarginGradientMiddle {get{return surface;}}
            public override Color ImageMarginGradientEnd {get{return surface;}}
        }
        readonly List<BatchCard> batchBindings=new List<BatchCard>();
        sealed class BatchCard
        {
            public B.Entry Entry;
            public Panel Card;
            public Label State,Summary,Recovery;
            public Button Action,Info;
        }
        public PHU_DimensionReportForm(B.Session session,bool dark,bool autoStart=false):this(new PHU_DimensionCheck.Result {Drawing="Batch"},dark)
        {
            batchSession=session;reportContent.Dispose();
            Text="TTSK · Check DIM Batch";
            BuildBatchContent();
            batchTimer=new Timer {Interval=120};
            batchTimer.Tick+=(s,e)=>RunBatchStep();
            Shown+=(s,e)=>{
                if(!autoStart)return;
                autoStart=false;
                if(!IsDisposed&&!Disposing&&!batchSession.Running)StartOrPauseBatch();
            };
            FormClosing+=(s,e)=>{
                if(checking){e.Cancel=true;return;}
                batchTimer.Stop();if(batchSession.Running)batchSession.Pause();
            };
        }
        void BuildBatchContent()
        {
            var panel=new Panel {Dock=DockStyle.Fill,BackColor=BackColor,Padding=new Padding(26,20,26,18)};
            var layout=Stack(48,32,44,0,32,46);panel.Controls.Add(layout);
            var heading=new Panel {Dock=DockStyle.Fill,Margin=Padding.Empty};
            heading.Controls.Add(TextLabel("Check DIM  /  Batch",20,ink,true));
            batchExport=Button("Xuất báo cáo",false);batchExport.Dock=DockStyle.Right;batchExport.Width=150;
            batchExport.Click+=(s,e)=>ExportBatch();heading.Controls.Add(batchExport);layout.Controls.Add(heading,0,0);
            batchSummary=TextLabel("",11,accent,true);layout.Controls.Add(batchSummary,0,1);
            tips.SetToolTip(batchSummary,batchSession.Entries.Count+" bản vẽ đã Load Selected · Kiểm tra chỉ đọc\r\n"+batchSession.ModelName);
            var filters=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty,Padding=new Padding(0,3,8,4)};
            filters.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            filters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,230));
            filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            batchFilter=Button(batchFilterNames[0]+"  ▾",false);batchFilter.Name="BatchFilter";batchFilter.Dock=DockStyle.Fill;
            batchFilter.AccessibleName="Lọc kết quả theo trạng thái bản vẽ";
            batchFilter.Margin=new Padding(0,0,10,0);
            batchFilterMenu=new ContextMenuStrip {BackColor=surface,ForeColor=ink,ShowImageMargin=false,ShowCheckMargin=false,
                Renderer=new ToolStripProfessionalRenderer(new BatchMenuColors(surface,soft,border)),Font=Font};
            for(int i=0;i<batchFilterNames.Length;i++)
            {
                int index=i;
                var item=new ToolStripMenuItem(batchFilterNames[i]) {BackColor=surface,ForeColor=ink,Padding=new Padding(12,6,12,6)};
                item.Click+=(s,e)=>SetBatchFilter(index);batchFilterMenu.Items.Add(item);
            }
            batchFilter.Click+=(s,e)=>batchFilterMenu.Show(batchFilter,new Point(0,batchFilter.Height));
            filters.Controls.Add(batchFilter,0,0);
            var searchBox=new BatchSearchBox(surface,soft,border,accent,ink,muted,Font) {Name="BatchSearchBox",Dock=DockStyle.Fill,Margin=Padding.Empty};
            batchSearch=searchBox.Editor;filters.Controls.Add(searchBox,1,0);layout.Controls.Add(filters,0,2);
            batchViewport=new Panel {Dock=DockStyle.Fill,BackColor=BackColor};
            batchCards=new FlowLayoutPanel {Name="BatchCards",FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=false,Padding=new Padding(0,3,8,3),BackColor=BackColor};
            batchScroll=new RevisionScroll(batchViewport,batchCards,dark,accent) {Dock=DockStyle.Right,Width=16,BackColor=BackColor};
            batchScroll.AccessibleName="Cuộn kết quả theo bản vẽ";
            batchViewport.Controls.Add(batchCards);batchViewport.SizeChanged+=(s,e)=>ResizeBatchCards();
            var listHost=new Panel {Dock=DockStyle.Fill,Margin=Padding.Empty};listHost.Controls.Add(batchViewport);listHost.Controls.Add(batchScroll);
            batchEmpty=TextLabel("Không có bản vẽ trong bộ lọc này",13,muted);batchEmpty.TextAlign=ContentAlignment.MiddleCenter;
            listHost.Controls.Add(batchEmpty);batchEmpty.BringToFront();layout.Controls.Add(listHost,0,3);
            batchNotice=TextLabel("",9,muted);layout.Controls.Add(batchNotice,0,4);
            var footer=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=4,RowCount=1,Margin=Padding.Empty};
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,230));footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,250));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,95));
            batchStart=Button("Bắt đầu",true);batchStart.Name="BatchStart";batchStart.Dock=DockStyle.Fill;batchStart.Click+=(s,e)=>StartOrPauseBatch();
            batchRetry=Button("Kiểm tra lại cần xử lý",false);batchRetry.Name="BatchRetry";batchRetry.Dock=DockStyle.Fill;
            batchRetry.Click+=(s,e)=>StartBatch(batchSession.Entries.Where(x=>x.NeedsWork&&x.Supported));
            var close=Button("Đóng",false);close.Dock=DockStyle.Fill;close.Click+=(s,e)=>Close();CancelButton=close;
            footer.Controls.Add(batchStart,0,0);footer.Controls.Add(batchRetry,1,0);footer.Controls.Add(close,3,0);layout.Controls.Add(footer,0,5);
            Controls.Add(panel);panel.BringToFront();
            batchSearch.TextChanged+=(s,e)=>RefreshBatchView();
            RefreshBatchView();
        }
        void SetBatchFilter(int index)
        {
            if(index<0||index>=batchFilterNames.Length)return;
            batchFilterIndex=index;batchFilter.Text=batchFilterNames[index]+"  ▾";RefreshBatchView();
        }
        IEnumerable<B.Entry> VisibleBatchEntries()
        {
            string query=batchSearch.Text.Trim();
            return batchSession.Entries.Where(e=>query.Length==0||e.Name.IndexOf(query,StringComparison.CurrentCultureIgnoreCase)>=0)
                .Where(e=>batchFilterIndex==1?e.NeedsWork:batchFilterIndex==2?e.Status==B.State.Failed:
                    batchFilterIndex==3?e.Status==B.State.Passed:batchFilterIndex==4?e.Status==B.State.Pending||e.Status==B.State.Checking:
                    batchFilterIndex==5?e.Status==B.State.Unsupported:true);
        }
        void RefreshBatchView()
        {
            var visible=VisibleBatchEntries().ToList();
            if(!batchBindings.Select(b=>b.Entry).SequenceEqual(visible))
            {
                batchCards.SuspendLayout();
                foreach(Control card in batchCards.Controls.Cast<Control>().ToArray())card.Dispose();batchBindings.Clear();
                foreach(var entry in visible)AddBatchCard(entry);
                batchCards.ResumeLayout();ResizeBatchCards();
            }
            foreach(var binding in batchBindings)
            {
                var entry=binding.Entry;
                binding.State.Text=entry.StatusText;
                binding.State.ForeColor=entry.Status==B.State.Passed?Green:entry.Status==B.State.Failed?Color.IndianRed:
                    entry.Status==B.State.NeedsAttention?Amber:entry.Status==B.State.NeedsReview?accent:muted;
                binding.Summary.Text=entry.Summary;
                binding.Recovery.Text=entry.Recovery.Length>0?entry.Recovery:entry.CheckedAt.HasValue?"Kiểm tra lúc "+entry.CheckedAt.Value.ToString("HH:mm:ss"):"";
                tips.SetToolTip(binding.Summary,entry.Summary);tips.SetToolTip(binding.Recovery,entry.Recovery);
                binding.Action.Text=entry.Snapshot!=null?"Xem DIM":entry.Status==B.State.Failed?"Thử lại bản này":"Kiểm tra bản này";
                binding.Action.Enabled=!batchSession.Running&&!checking&&entry.Supported;
                binding.Info.Enabled=!batchSession.Running&&!checking&&(entry.Snapshot!=null||entry.Problem.Length>0);
                binding.Info.Visible=entry.Warnings>0||(entry.Snapshot!=null&&entry.Snapshot.SkippedAngles.Count>0)||entry.Technical.Length>0||entry.Status==B.State.Unsupported;
                binding.Card.BackColor=entry.Status==B.State.Checking?soft:surface;binding.Card.Invalidate(true);
            }
            batchEmpty.Visible=visible.Count==0;
            batchEmpty.Text=batchSession.Running?"Kết quả trong bộ lọc này sẽ cập nhật sau mỗi bản vẽ.":"Không có bản vẽ trong bộ lọc này";
            batchViewport.Visible=batchScroll.Visible=visible.Count>0;
            int complete=batchSession.Entries.Count(e=>e.Status!=B.State.Pending&&e.Status!=B.State.Checking);
            batchSummary.Text=complete+" / "+batchSession.Entries.Count+" đã xử lý   ·   "+batchSession.Entries.Count(e=>e.Status==B.State.NeedsAttention)+" cần xem   ·   "+
                batchSession.Entries.Count(e=>e.Status==B.State.NeedsReview)+" cần xác minh   ·   "+batchSession.Entries.Count(e=>e.Status==B.State.Failed)+" lỗi chạy";
            batchStart.Enabled=!checking||batchSession.Running;
            int pending=batchSession.Entries.Count(e=>e.Status==B.State.Pending);
            batchStart.Text=batchSession.Running?"■  Dừng sau bản hiện tại":pending>0?(complete>0?"Tiếp tục · ":"Bắt đầu · ")+pending+" bản":"Kiểm tra lại tất cả";
            batchRetry.Enabled=!batchSession.Running&&!checking&&batchSession.Entries.Any(e=>e.NeedsWork&&e.Supported);
            batchExport.Enabled=!batchSession.Running&&!checking;
            var current=batchSession.Current;
            SetBatchNotice(current==null?batchSession.Notice:"Đang kiểm tra "+current.Number+" / "+batchSession.Entries.Count+" · "+current.Name);
            if(current!=null&&batchFilterIndex==0&&batchSearch.Text.Length==0)
            {
                var active=batchBindings.FirstOrDefault(b=>b.Entry==current);
                if(active!=null)batchScroll.Reveal(active.Card);
            }
        }
        void AddBatchCard(B.Entry entry)
        {
            var card=Card();card.Name="BatchCard";card.Tag=entry;card.Dock=DockStyle.None;card.Padding=new Padding(16,8,14,8);card.Margin=new Padding(0,0,0,8);
            var rows=Stack(32,34,28);card.Controls.Add(rows);
            var titleRow=new Panel {Dock=DockStyle.Fill,Margin=Padding.Empty};
            var name=TextLabel(entry.Number.ToString("000")+"   "+entry.Name,12,ink,true);tips.SetToolTip(name,entry.Name);
            var state=TextLabel("",10,muted,true);state.Dock=DockStyle.Right;state.Width=220;state.TextAlign=ContentAlignment.MiddleRight;
            titleRow.Controls.Add(name);titleRow.Controls.Add(state);rows.Controls.Add(titleRow,0,0);
            var middle=new Panel {Dock=DockStyle.Fill,Margin=Padding.Empty};
            var summary=TextLabel("",10,ink);var action=Button("",false);action.Dock=DockStyle.Right;action.Width=175;
            action.Click+=(s,e)=>{if(entry.Snapshot!=null)OpenBatchEntry(entry);else StartBatch(new[]{entry});};
            middle.Controls.Add(summary);middle.Controls.Add(action);rows.Controls.Add(middle,0,1);
            var bottom=new Panel {Dock=DockStyle.Fill,Margin=Padding.Empty};
            var recovery=TextLabel("",9,muted);var info=Button("Thông tin",false);info.Dock=DockStyle.Right;info.Width=105;
            info.Click+=(s,e)=>ShowBatchEntryInfo(entry);bottom.Controls.Add(recovery);bottom.Controls.Add(info);rows.Controls.Add(bottom,0,2);
            rows.RowStyles[0].Height=Math.Max(32,TextRenderer.MeasureText("Ag",name.Font).Height+6);
            rows.RowStyles[1].Height=Math.Max(34,TextRenderer.MeasureText("Ag",summary.Font).Height+10);
            rows.RowStyles[2].Height=Math.Max(28,TextRenderer.MeasureText("Ag",recovery.Font).Height+8);
            card.Height=card.Padding.Vertical+((RaisedCard)card).FrameSpace+(int)rows.RowStyles.Cast<RowStyle>().Sum(r=>r.Height);
            batchCards.Controls.Add(card);batchBindings.Add(new BatchCard {Entry=entry,Card=card,State=state,Summary=summary,Recovery=recovery,Action=action,Info=info});
        }
        void ResizeBatchCards()
        {
            if(batchCards==null)return;
            batchCards.Width=Math.Max(300,batchViewport.ClientSize.Width);
            foreach(Control card in batchCards.Controls)card.Width=batchCards.Width-batchCards.Padding.Horizontal;
            batchCards.Height=batchCards.Padding.Vertical+batchCards.Controls.Cast<Control>().Sum(c=>c.Height+c.Margin.Vertical);batchScroll.Clamp();
        }
        void SetBatchNotice(string message)
        {batchNotice.Text=message;tips.SetToolTip(batchNotice,message);}
        void StartOrPauseBatch()
        {
            if(checking)return;
            if(batchSession.Running){batchTimer.Stop();batchSession.Pause();RefreshBatchView();return;}
            var pending=batchSession.Entries.Where(e=>e.Status==B.State.Pending).ToList();
            StartBatch(pending.Count>0?pending:batchSession.Entries);
        }
        void StartBatch(IEnumerable<B.Entry> entries)
        {
            if(checking||batchSession.Running)return;
            if(batchSession.Start(entries))batchTimer.Start();RefreshBatchView();
        }
        void RunBatchStep()
        {
            if(checking)return;
            batchTimer.Stop();checking=true;
            string failure=null;
            try {batchSession.Step(()=>{RefreshBatchView();Update();});}
            catch(Exception ex){batchSession.Pause();failure="Batch đã dừng: "+ex.GetBaseException().Message;}
            finally {checking=false;RefreshBatchView();if(failure!=null)SetBatchNotice(failure);if(batchSession.Running)batchTimer.Start();}
        }
        void OpenBatchEntry(B.Entry entry)
        {
            if(checking||batchSession.Running)return;
            checking=true;UseWaitCursor=true;RefreshBatchView();
            try
            {
                var fresh=batchSession.OpenForReview(entry);
                UseWaitCursor=false;
                using(var report=new PHU_DimensionReportForm(fresh,dark,()=>batchSession.RefreshOpen(entry),
                    (snapshot,finding)=>{batchSession.EnsureReviewContext(entry);SelectInTekla(snapshot,finding);}))
                    report.ShowDialog(this);
                // The visible drawing stays with the user; it may contain their edits.
                SetBatchNotice(entry.Name+" · Sau khi sửa trong Tekla, dùng F5 trong chi tiết hoặc kiểm tra lại bản này.");
            }
            catch(Exception ex){SetBatchNotice(entry.Name+" · "+ex.GetBaseException().Message);}
            finally
            {
                string notice=batchNotice.Text;checking=false;UseWaitCursor=false;RefreshBatchView();SetBatchNotice(notice);
            }
        }
        void ShowBatchEntryInfo(B.Entry entry)
        {
            var text=new StringBuilder(entry.StatusText+"\r\n\r\n"+entry.Summary);
            if(entry.Recovery.Length>0)text.AppendLine().AppendLine(entry.Recovery);
            if(entry.Technical.Length>0)text.AppendLine().AppendLine("Chi tiết kỹ thuật: "+entry.Technical);
            if(entry.Snapshot!=null)foreach(string warning in entry.Snapshot.Warnings)text.AppendLine().AppendLine("• "+warning);
            if(entry.Snapshot!=null)foreach(string skipped in entry.Snapshot.SkippedAngles)text.AppendLine().AppendLine("• "+skipped);
            using(var dialog=new Form {Text=entry.Name,Size=new Size(720,400),MinimumSize=new Size(500,280),StartPosition=FormStartPosition.CenterParent,BackColor=surface,ForeColor=ink,Padding=new Padding(20),ShowIcon=false})
            {
                dialog.Controls.Add(new TextBox {Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,Text=text.ToString(),BackColor=surface,ForeColor=ink,BorderStyle=BorderStyle.None,Font=Font});
                dialog.ShowDialog(this);
            }
        }
        void ExportBatch()
        {
            using(var dialog=new SaveFileDialog {Title="Xuất kết quả Check DIM",Filter="Báo cáo văn bản (*.txt)|*.txt",FileName="Check-DIM-"+DateTime.Now.ToString("yyyyMMdd-HHmm")+".txt"})
            {
                if(dialog.ShowDialog(this)!=DialogResult.OK)return;
                try{File.WriteAllText(dialog.FileName,batchSession.ExportText(),new UTF8Encoding(true));SetBatchNotice("Đã xuất báo cáo: "+dialog.FileName);}
                catch(Exception ex){SetBatchNotice("Chưa xuất được báo cáo: "+ex.Message);}
            }
        }
    }
}
