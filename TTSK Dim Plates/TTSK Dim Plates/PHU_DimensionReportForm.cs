// Một form báo cáo DIM gồm ba partial: batch, revision và kết quả kiểm tra.
// LUỒNG: PHU_DimensionCheck/PHU_DimensionBatch cung cấp dữ liệu → form hiển thị → người dùng chọn kiểm lại/tô màu/revision.
// ĐIỂM SỬA: batch ở region PHU_DimensionBatchForm; revision ở PHU_DimensionRevisionForm; khung chung ở PHU_DimensionReportForm.
// THAM SỐ: kích thước/margin/font trong form là pixel/điểm chữ; ngưỡng hình học nằm ở engine Check/Revision, không ở form.
// TRA CỨU: PROJECT_MAP.md ở gốc repo; chỉ mục kiểu/hàm và điểm chỉnh thông số.
// Mục lục:
//   PHU_DimensionBatchForm.cs
//   PHU_DimensionRevisionForm.cs
//   PHU_DimensionReportForm.cs

#region PHU_DimensionBatchForm - Danh sách batch, chạy từng bản vẽ và trạng thái


namespace Tekla.Technology.Akit.UserScript
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Windows.Forms;
    using B = Tekla.Technology.Akit.UserScript.PHU_DimensionBatch;

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
#endregion

#region PHU_DimensionRevisionForm - Thẻ revision, lựa chọn đối tượng và thao tác màu


namespace Tekla.Technology.Akit.UserScript
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Windows.Forms;
    using D = Tekla.Structures.Drawing;

    public sealed partial class PHU_DimensionReportForm
    {
        Panel revisionPanel;
        FlowLayoutPanel revisionCards;
        Panel revisionViewport;
        RevisionScroll revisionScroll;
        Control revisionSuccessCheck;
        bool revisionCompleted;
        Label revisionStatus, revisionSource, revisionDetail;
        Label revisionSnapshot;
        TableLayoutPanel revisionLayout;
        Button revisionNext, revisionPrevious;
        Button revisionFilter;
        ContextMenuStrip revisionFilterMenu;
        int revisionFilterIndex;
        readonly string[] revisionFilterNames={"Tất cả thay đổi","Đổi giá trị","DIM mới","Cần kiểm tra","Đã tắt"};
        Label revisionPosition;
        readonly bool revisionOnly;
        readonly Func<string> createRevisionDrawing;
        public PHU_DimensionReportForm(bool dark, Func<string> createDrawing)
            : this(new PHU_DimensionCheck.Result { Drawing = "Check Revision" }, dark)
        {
            revisionOnly = true;
            createRevisionDrawing = createDrawing;
            Text = "TTSK · Check Revision";
            OpenRevision();
        }
        PHU_DimensionRevision.Result revisionResult;
        readonly Dictionary<int, RevisionChoice> revisionChoices = new Dictionary<int, RevisionChoice>();
        readonly Dictionary<int, Control> revisionCardControls = new Dictionary<int, Control>();
        int revisionSelected;
        string revisionChoicesPath;
        string revisionChoicesHash;
        sealed class RevisionChoice
        {
            public string Signature;
            public D.DrawingColors Original;
            public bool Excluded;
        }
        void CloseRevision()
        {
            if (checking || revisionPanel == null) return;
            if (revisionOnly) { Close(); return; }
            LeaveRevision();
        }
        void CheckCurrentDimensions()
        {
            if(checking)return;
            LeaveRevision();
            Recheck(recheckButton);
        }
        void LeaveRevision()
        {
            if(revisionPanel==null)return;
            if(revisionFilterMenu!=null){revisionFilterMenu.Dispose();revisionFilterMenu=null;}
            revisionPanel.Dispose(); revisionPanel = null; reportContent.Show();
            revisionSuccessCheck=null;revisionCompleted=false;revisionResult=null;
            revisionChoices.Clear(); revisionChoicesPath=null; revisionChoicesHash=null; revisionSelected=0;
        }
        void OpenRevision()
        {
            if (checking) return;
            if (revisionPanel != null) { revisionPanel.BringToFront(); return; }
            BuildRevisionPanel();
        }
        void BuildRevisionPanel()
        {
            reportContent.Hide();
            revisionPanel = new Panel { Dock=DockStyle.Fill, BackColor=BackColor, Padding=new Padding(26,20,26,18) };
            var layout=revisionLayout=Stack(48,28,36,1,0,1,1);
            layout.RowStyles[3].SizeType=SizeType.AutoSize;
            layout.RowStyles[5].Height=0;
            layout.RowStyles[6].SizeType=SizeType.AutoSize;
            var heading=new Panel { Dock=DockStyle.Fill };
            var back=Button("←  Check DIM · C",false); back.Dock=DockStyle.Right; back.Width=175;
            back.Click+=(s,e)=>CheckCurrentDimensions();
            heading.Controls.Add(TextLabel("Revision  /  Thay đổi bản vẽ",20,ink,true)); heading.Controls.Add(back);
            layout.Controls.Add(heading,0,0);
            var metadata=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty};
            metadata.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
            metadata.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
            revisionSource=TextLabel("Bản vẽ đang mở trong Tekla",10,muted); metadata.Controls.Add(revisionSource,0,0);
            revisionSnapshot=TextLabel("",10,muted); revisionSnapshot.TextAlign=ContentAlignment.MiddleRight;
            metadata.Controls.Add(revisionSnapshot,1,0); layout.Controls.Add(metadata,0,1);
            revisionStatus=TextLabel("Bấm Bắt đầu để đối chiếu snapshot và tô đỏ DIM thay đổi.",12,accent,true); layout.Controls.Add(revisionStatus,0,2);
            var navigation=new TableLayoutPanel {Dock=DockStyle.Fill,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,
                ColumnCount=5,RowCount=1,Margin=new Padding(0,6,0,10),Padding=Padding.Empty};
            navigation.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            navigation.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            navigation.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            navigation.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,110));
            navigation.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            navigation.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            revisionFilterIndex=0;
            revisionFilter=Button(revisionFilterNames[0]+"  ▾",false);revisionFilter.Name="revisionFilter";
            revisionFilter.Width=225;revisionFilter.Margin=Padding.Empty;revisionFilter.Enabled=false;
            revisionFilter.AccessibleName="Lọc DIM Revision";
            revisionFilterMenu=new ContextMenuStrip {BackColor=surface,ForeColor=ink,ShowImageMargin=false,ShowCheckMargin=false,
                Renderer=new ToolStripProfessionalRenderer(new BatchMenuColors(surface,soft,border)),Font=Font};
            for(int i=0;i<revisionFilterNames.Length;i++)
            {
                int index=i;
                var option=new ToolStripMenuItem(revisionFilterNames[i]) {BackColor=surface,ForeColor=ink,Padding=new Padding(12,6,12,6)};
                option.Click+=(s,e)=>SetRevisionFilter(index);revisionFilterMenu.Items.Add(option);
            }
            revisionFilter.Click+=(s,e)=>revisionFilterMenu.Show(revisionFilter,new Point(0,revisionFilter.Height));
            revisionPrevious=Button("←  DIM trước",false);revisionPrevious.Name="revisionPrevious";revisionPrevious.Width=145;revisionPrevious.Margin=Padding.Empty;
            revisionPrevious.Click+=(s,e)=>MoveRevision(-1);
            revisionNext=Button("DIM tiếp  →",false);revisionNext.Name="revisionNext";revisionNext.Width=145;revisionNext.Margin=Padding.Empty;
            revisionNext.Click+=(s,e)=>MoveRevision(1);
            revisionPosition=TextLabel("Chưa chạy",10,muted);revisionPosition.Name="revisionPosition";revisionPosition.TextAlign=ContentAlignment.MiddleCenter;
            navigation.Controls.Add(revisionFilter,0,0);navigation.Controls.Add(revisionPrevious,2,0);
            navigation.Controls.Add(revisionPosition,3,0);navigation.Controls.Add(revisionNext,4,0);
            tips.SetToolTip(revisionPrevious,"Duyệt đoạn DIM trước và chọn cụm DIM tương ứng trên Tekla, kể cả DIM đã đỏ. ← / ↑");
            tips.SetToolTip(revisionNext,"Duyệt đoạn DIM tiếp và chọn cụm DIM tương ứng trên Tekla, kể cả DIM đã đỏ. → / ↓");
            layout.Controls.Add(navigation,0,3);
            var listHost=new Panel {Dock=DockStyle.Fill,BackColor=BackColor};
            revisionViewport=new Panel {Dock=DockStyle.Fill,BackColor=BackColor};
            revisionCards=new FlowLayoutPanel { FlowDirection=FlowDirection.TopDown,WrapContents=false,
                AutoScroll=false,BackColor=BackColor,Padding=new Padding(0,3,8,3) };
            revisionScroll=new RevisionScroll(revisionViewport,revisionCards,dark,accent) {Dock=DockStyle.Right,Width=16,BackColor=BackColor};
            revisionViewport.Controls.Add(revisionCards);
            revisionViewport.SizeChanged+=(s,e)=>ResizeRevisionCards();
            listHost.Controls.Add(revisionViewport);listHost.Controls.Add(revisionScroll);
            revisionSuccessCheck=new SuccessCheck {Name="RevisionSuccessCheck",Dock=DockStyle.Fill,
                ForeColor=Green,BackColor=BackColor,Font=new Font(Font.FontFamily,14),
                Text="Không có thay đổi",AccessibleName="Revision: không có thay đổi",Visible=false};
            listHost.Controls.Add(revisionSuccessCheck);revisionSuccessCheck.BringToFront();
            layout.Controls.Add(listHost,0,4);
            revisionDetail=TextLabel("",10,muted); revisionDetail.Visible=false; layout.Controls.Add(revisionDetail,0,5);
            // AutoSize accounts for the actual DPI-scaled button height, margins and shadow.
            // The old fixed 44px row + nested FlowLayoutPanel clipped the lower edges.
            var actions=new TableLayoutPanel {Name="revisionActions",Dock=DockStyle.Fill,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,
                ColumnCount=4,RowCount=1,Margin=Padding.Empty,Padding=new Padding(0,10,0,6)};
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));actions.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));actions.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            actions.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var refresh=Button("▶  Bắt đầu · R / F5",true); refresh.Name="revisionStart"; refresh.Size=new Size(190,44);refresh.Margin=new Padding(0,0,10,0); refresh.Click+=(s,e)=>RefreshRevision();
            var create=Button("Create drawing",false); create.Name="revisionCreate"; create.Size=new Size(175,44);create.Margin=Padding.Empty;
            create.Enabled=createRevisionDrawing!=null; create.Click+=(s,e)=>CreateRevisionDrawing();
            var delete=Button("Delete REV",false);delete.Name="revisionDelete";delete.Size=new Size(155,44);delete.Margin=Padding.Empty;
            delete.ForeColor=Color.IndianRed;delete.Click+=(s,e)=>DeleteRevisionSection();
            actions.Controls.Add(refresh,0,0);actions.Controls.Add(create,1,0);actions.Controls.Add(delete,3,0);layout.Controls.Add(actions,0,6);
            revisionPanel.Controls.Add(layout); Controls.Add(revisionPanel); revisionPanel.BringToFront();
            ResizeRevisionCards();
            UpdateRevisionNavigation();
        }
        void CreateRevisionDrawing()
        {
            if(checking || createRevisionDrawing==null)return;
            checking=true;UseWaitCursor=true;
            try
            {
                revisionResult=null;
                revisionChoices.Clear();revisionChoicesPath=null;revisionChoicesHash=null;
                createRevisionDrawing();
            }
            catch(Exception ex){ShowRevisionError("Create drawing: "+ex.Message);return;}
            finally{checking=false;UseWaitCursor=false;}
            RefreshRevision();
        }
        void ShowRevisionError(string message)
        {
            if(!String.IsNullOrEmpty(message)){revisionCompleted=false;UpdateRevisionSuccess();}
            revisionDetail.Text=message;
            revisionDetail.Visible=!String.IsNullOrEmpty(message);
            tips.SetToolTip(revisionDetail,message);
            revisionLayout.RowStyles[5].Height=String.IsNullOrEmpty(message)?0:Math.Max(32,revisionDetail.Font.Height+12);
        }
        void LoadRevisionChoices()
        {
            string path=revisionResult.BaselinePath+".choices.tsv";
            if(revisionChoicesPath==path && revisionChoicesHash==revisionResult.BaselineHash)return;
            revisionChoices.Clear();
            revisionChoicesPath=path; revisionChoicesHash=null;
            ReadRevisionOriginalColors();
            revisionChoicesHash=revisionResult.BaselineHash;
        }
        void ReadRevisionOriginalColors()
        {
            if(!File.Exists(revisionChoicesPath))return;
            foreach(string line in File.ReadAllLines(revisionChoicesPath,Encoding.UTF8))
            {
                var fields=line.Split('\t');int id,color;
                if(fields.Length!=4 || !Int32.TryParse(fields[0],out id) || !Int32.TryParse(fields[2],out color) ||
                    !Enum.IsDefined(typeof(D.DrawingColors),color) || (fields[3]!="0"&&fields[3]!="1"))
                    throw new InvalidDataException("Không đọc được lựa chọn Revision đã lưu. Chưa đổi màu tự động.");
                revisionChoices[id]=new RevisionChoice {Signature=Encoding.UTF8.GetString(Convert.FromBase64String(fields[1])),
                    Original=(D.DrawingColors)color,Excluded=false};
            }
        }
        void SaveRevisionChoices()
        {
            string temporary=revisionChoicesPath+"."+Guid.NewGuid().ToString("N")+".tmp";
            try
            {
                File.WriteAllLines(temporary,revisionChoices.OrderBy(p=>p.Key).Select(p=>p.Key+"\t"+
                    Convert.ToBase64String(Encoding.UTF8.GetBytes(p.Value.Signature))+"\t"+(int)p.Value.Original+"\t0"),Encoding.UTF8);
                if(File.Exists(revisionChoicesPath))File.Replace(temporary,revisionChoicesPath,null);
                else File.Move(temporary,revisionChoicesPath);
            }
            finally {if(File.Exists(temporary))File.Delete(temporary);}
        }
        bool CanAutoRevision(PHU_DimensionRevision.Item item)
        {return !item.ReadOnly && !item.Review && item.Changed && !String.IsNullOrEmpty(item.Signature) && !item.Before.Contains("?");}
        bool CanToggleRevision(PHU_DimensionRevision.Item item)
        {return !item.ReadOnly && !String.IsNullOrEmpty(item.Signature) && item.Segments.Count>0;}
        void RefreshRevision()
        {
            if(checking)return;
            revisionCompleted=false;UpdateRevisionSuccess();
            checking=true;UseWaitCursor=true;
            try
            {
                revisionResult=null;
                revisionResult=PHU_DimensionRevision.Analyze();
                LoadRevisionChoices();
                foreach(var item in revisionResult.Items.Where(CanToggleRevision))
                {
                    RevisionChoice choice;
                    if(!revisionChoices.TryGetValue(item.Id,out choice)||choice.Signature!=item.Signature)
                        revisionChoices[item.Id]=new RevisionChoice {Signature=item.Signature,
                            Original=item.AlreadyRed?D.DrawingColors.Black:PHU_DimensionRevision.GetColor(item)};
                }
                // Persist original colors only. Exclusions belong to this open Revision session.
                SaveRevisionChoices();
                // Repair only red DIMs for which this tool recorded the same geometry
                // and original colour. Unrecorded/manual colours remain user-owned.
                var restores=RevisionColorRestorations();
                foreach(var group in restores.GroupBy(p=>p.Value))
                    PHU_DimensionRevision.RestoreUnchangedColors(revisionResult,group.Select(p=>p.Key).ToList(),group.Key);
                var identities=revisionResult.Items.Where(CanToggleRevision).ToDictionary(i=>i,i=>i.Id);
                var targets=revisionResult.Items.Where(i=>CanAutoRevision(i)&&!revisionChoices[i.Id].Excluded).ToList();
                PHU_DimensionRevision.Apply(revisionResult,targets);
                RemapRevisionChoices(identities);
                revisionSource.Text=revisionResult.Drawing.Name+" / "+revisionResult.Drawing.Mark;
                revisionSnapshot.Text="Snapshot · "+revisionResult.BaselineTime.ToString("dd/MM/yyyy HH:mm");
                tips.SetToolTip(revisionSource,revisionSource.Text);
                tips.SetToolTip(revisionSnapshot,revisionResult.BaselinePath);
                RenderRevisionCards();
                UpdateRevisionSummary();
                ShowRevisionError("");
                revisionCompleted=true;UpdateRevisionSuccess();
            }
            catch(Exception ex)
            {
                if(revisionResult!=null){RenderRevisionCards();UpdateRevisionSummary();
                    revisionSource.Text=revisionResult.Drawing.Name+" / "+revisionResult.Drawing.Mark;
                    revisionSnapshot.Text="Snapshot · "+revisionResult.BaselineTime.ToString("dd/MM/yyyy HH:mm");}
                ShowRevisionError("Chưa hoàn tất: "+ex.Message);
            }
            finally {checking=false;UseWaitCursor=false;}
        }
        Dictionary<PHU_DimensionRevision.Item,D.DrawingColors> RevisionColorRestorations()
        {
            var result=new Dictionary<PHU_DimensionRevision.Item,D.DrawingColors>();
            foreach(var item in revisionResult.UnchangedItems)
            {
                RevisionChoice choice;
                if(!item.Review&&!item.Changed&&item.AlreadyRed&&revisionChoices.TryGetValue(item.Id,out choice)&&
                    choice.Signature==item.Signature&&choice.Original!=D.DrawingColors.Red)
                    result[item]=choice.Original;
            }
            return result;
        }
        void RemapRevisionChoices(Dictionary<PHU_DimensionRevision.Item,int> identities)
        {
            foreach(var pair in identities)
            {
                RevisionChoice choice;if(!revisionChoices.TryGetValue(pair.Value,out choice))continue;
                if(pair.Value!=pair.Key.Id){revisionChoices.Remove(pair.Value);revisionChoices[pair.Key.Id]=choice;}
                choice.Signature=pair.Key.Signature;
            }
            SaveRevisionChoices();
        }
        void UpdateRevisionSummary()
        {
            int on=revisionResult.Items.Count(i=>i.AlreadyRed);
            int off=revisionResult.Items.Count(i=>revisionChoices.ContainsKey(i.Id)&&revisionChoices[i.Id].Excluded);
            int pending=PendingRevisionItems().Count;
            revisionStatus.Text=on+" đang đỏ   ·   "+off+" đã tắt   ·   "+pending+" cần kiểm tra   ·   "+revisionResult.Unchanged+" không đổi"+
                "   ·   "+revisionResult.Items.Count(i=>i.ReadOnly)+" thông báo"+
                (revisionResult.Warnings.Count>0?"   ·   "+revisionResult.Warnings.Count+" lưu ý":"");
            tips.SetToolTip(revisionStatus,String.Join("\r\n",revisionResult.Warnings));
            UpdateRevisionNavigation();
            UpdateRevisionSuccess();
        }
        void UpdateRevisionSuccess()
        {
            if(revisionSuccessCheck==null)return;
            bool clean=revisionCompleted&&revisionResult!=null&&revisionResult.Compared>0&&
                revisionResult.Items.Count==0&&revisionResult.Warnings.Count==0;
            revisionSuccessCheck.Visible=clean;
            revisionViewport.Visible=revisionScroll.Visible=!clean;
            if(clean)revisionSuccessCheck.BringToFront();
        }
        List<PHU_DimensionRevision.Item> PendingRevisionItems()
        {return revisionResult.Items.Where(i=>!i.ReadOnly&&i.Review&&!i.AlreadyRed&&
            (!revisionChoices.ContainsKey(i.Id)||!revisionChoices[i.Id].Excluded)).ToList();}
        List<PHU_DimensionRevision.Item> VisibleRevisionItems()
        {
            if(revisionResult==null)return new List<PHU_DimensionRevision.Item>();
            var items=revisionResult.Items.AsEnumerable();
            switch(revisionFilterIndex)
            {
                case 1:items=items.Where(i=>!i.ReadOnly&&i.Changed&&!i.IsNew&&!i.Review);break;
                case 2:items=items.Where(i=>!i.ReadOnly&&i.IsNew);break;
                case 3:items=PendingRevisionItems();break;
                case 4:items=items.Where(i=>!i.ReadOnly&&revisionChoices.ContainsKey(i.Id)&&revisionChoices[i.Id].Excluded);break;
            }
            return items.ToList();
        }
        void SetRevisionFilter(int index)
        {
            if(checking||revisionResult==null||index<0||index>=revisionFilterNames.Length)return;
            revisionFilterIndex=index;revisionFilter.Text=revisionFilterNames[index]+"  ▾";
            revisionSelected=0;RenderRevisionCards();revisionScroll.Reset();
        }
        List<PHU_DimensionRevision.Item> NavigableRevisionItems()
        {return VisibleRevisionItems().Where(i=>!i.ReadOnly).ToList();}
        void UpdateRevisionNavigation()
        {
            if(revisionNext==null)return;
            var items=NavigableRevisionItems();int index=items.FindIndex(i=>i.Id==revisionSelected);
            revisionNext.Enabled=revisionPrevious.Enabled=items.Count>0;
            revisionFilter.Enabled=revisionResult!=null;
            revisionPosition.Text=revisionResult==null?"Chưa chạy":items.Count==0?"0 DIM":index<0?items.Count+" DIM":(index+1)+" / "+items.Count;
            tips.SetToolTip(revisionPosition,index<0?"Duyệt DIM theo bộ lọc; DIM đã đỏ vẫn chọn được.":items[index].View+" · "+items[index].Before+" → "+items[index].After);
        }
        void MoveRevision(int direction)
        {
            if(checking||revisionResult==null)return;
            var items=NavigableRevisionItems();
            if(items.Count==0)return;
            int index=items.FindIndex(i=>i.Id==revisionSelected);
            var item=items[index<0?(direction>0?0:items.Count-1):(index+direction+items.Count)%items.Count];
            SelectRevision(item);
            Control card;
            if(revisionCardControls.TryGetValue(item.Id,out card))revisionScroll.Reveal(card);
        }
        void ResizeRevisionCards()
        {
            if(revisionCards==null)return;
            int width=Math.Max(300,revisionViewport.ClientSize.Width);
            revisionCards.Width=width;
            foreach(Control card in revisionCards.Controls)
            {
                card.Width=width-revisionCards.Padding.Horizontal;
                var content=card.Controls.OfType<RevisionContentDiff>().FirstOrDefault();
                if(content!=null)card.Height=content.RequiredHeight(card.DisplayRectangle.Width)+card.Padding.Vertical+((RaisedCard)card).FrameSpace;
            }
            revisionCards.Height=revisionCards.Padding.Vertical+revisionCards.Controls.Cast<Control>().Sum(c=>c.Height+c.Margin.Vertical);
            revisionScroll.Clamp();
        }
        void RenderRevisionCards()
        {
            revisionCards.SuspendLayout();
            foreach(Control control in revisionCards.Controls.Cast<Control>().ToArray())control.Dispose();
            revisionCardControls.Clear();
            var visible=VisibleRevisionItems();
            foreach(var item in visible)
            {
                if(item.ReadOnly)
                {
                    var contentCard=Card();contentCard.Dock=DockStyle.None;
                    contentCard.Margin=new Padding(0,0,0,8);contentCard.Padding=new Padding(18,12,18,12);
                    contentCard.BackColor=item.Id==revisionSelected?soft:surface;
                    var comparison=new RevisionContentDiff(item,dark,ink,muted,border) {Dock=DockStyle.Fill};
                    contentCard.Controls.Add(comparison);
                    bool selectable=PHU_DimensionRevision.CanSelectMark(item);
                    contentCard.TabStop=selectable;contentCard.AccessibleName=comparison.AccessibleName;
                    if(selectable)
                    {
                        contentCard.Cursor=comparison.Cursor=Cursors.Hand;
                        tips.SetToolTip(contentCard,"Bấm để chọn mark trên bản vẽ");
                        tips.SetToolTip(comparison,"Bấm để chọn mark trên bản vẽ");
                        contentCard.Click+=(s,e)=>SelectRevision(item);comparison.Click+=(s,e)=>SelectRevision(item);
                        contentCard.KeyDown+=(s,e)=>{if(e.KeyCode==Keys.Enter||e.KeyCode==Keys.Space){SelectRevision(item);e.Handled=true;e.SuppressKeyPress=true;}};
                    }
                    revisionCards.Controls.Add(contentCard);revisionCardControls[item.Id]=contentCard;
                    continue;
                }
                bool capable=CanToggleRevision(item)&&revisionChoices.ContainsKey(item.Id);
                bool off=capable&&revisionChoices[item.Id].Excluded;
                bool on=item.AlreadyRed;
                var card=Card();card.Dock=DockStyle.None;card.Margin=new Padding(0,0,0,8);card.Padding=new Padding(16,8,14,8);
                card.BackColor=item.Id==revisionSelected?soft:surface;
                var rows=Stack(28,38,26);
                var top=new Panel {Dock=DockStyle.Fill,BackColor=Color.Transparent};
                var state=TextLabel(item.ReadOnly?"●  CHỈ THÔNG BÁO":on?"●  ĐANG ĐỎ":off?"○  ĐÃ TẮT":item.Changed&&!item.Review?"○  CHƯA GHI ĐƯỢC MÀU":"○  CẦN KIỂM TRA",9,on?Color.IndianRed:muted,true);
                state.Dock=DockStyle.Right;state.Width=210;state.TextAlign=ContentAlignment.MiddleRight;
                var view=TextLabel(item.View,11,ink,true);top.Controls.Add(view);top.Controls.Add(state);rows.Controls.Add(top,0,0);
                var middle=new Panel {Dock=DockStyle.Fill};
                var toggle=Button(on?"Tắt Revision":off?"Bật lại Revision":item.Changed&&!item.Review?"Thử ghi màu lại":"Bật lại Revision",false);
                toggle.Dock=DockStyle.Right;toggle.Width=175;toggle.Enabled=capable;
                toggle.AccessibleName=(on?"Tắt Revision ":"Bật Revision ")+item.View+" "+item.After;
                toggle.Click+=(s,e)=>ToggleRevision(item);
                toggle.Visible=!item.ReadOnly;
                var values=TextLabel(item.Before+"   →   "+item.After+(item.ReadOnly?"":" mm"),item.ReadOnly?11:18,on?Color.IndianRed:ink,true);
                middle.Controls.Add(values);middle.Controls.Add(toggle);rows.Controls.Add(middle,0,1);
                string noteText=item.ReadOnly?item.Reason:!capable?"Chưa đọc đủ DIM · kiểm tra trực tiếp trên bản vẽ":off?"Tạm tắt trong phiên này":
                    item.IsNew?"DIM mới · không so với cụm khác":item.Before.Contains("?")?"Đối chiếu thủ công trước khi bật đỏ":"Chỉ đổi màu đoạn này";
                var note=TextLabel(noteText,9,muted);
                // Absolute rows must include the actual font height and panel margins at the current DPI.
                int titleHeight=Math.Max(28,TextRenderer.MeasureText(item.View,view.Font).Height+top.Margin.Vertical+4);
                int valueHeight=Math.Max(38,TextRenderer.MeasureText(values.Text,values.Font).Height+middle.Margin.Vertical+4);
                if(item.ReadOnly){values.Text="Cũ: "+item.Before+"\r\nMới: "+item.After;valueHeight=70;}
                if(!String.IsNullOrEmpty(item.Message)){values.Text=item.Message;valueHeight=42;noteText="";}
                int noteHeight=noteText.Length==0?0:Math.Max(26,TextRenderer.MeasureText("Ag",note.Font).Height+4);
                note.Visible=noteHeight>0;
                rows.RowStyles[0].Height=titleHeight;rows.RowStyles[1].Height=valueHeight;rows.RowStyles[2].Height=noteHeight;
                card.Height=card.Padding.Vertical+((RaisedCard)card).FrameSpace+titleHeight+valueHeight+noteHeight;
                tips.SetToolTip(view,item.View);tips.SetToolTip(values,values.Text);tips.SetToolTip(note,item.Reason);
                tips.SetToolTip(toggle,on?"Khôi phục màu chữ; chỉ tắt tự động đánh dấu trong phiên này.":
                    "Sau khi kiểm tra, chuyển riêng giá trị DIM này sang đỏ.");
                rows.Controls.Add(note,0,2);card.Controls.Add(rows);
                foreach(Control hit in new Control[]{card,rows,top,view,state,middle,values,note})
                {hit.Cursor=Cursors.Hand;hit.Click+=(s,e)=>SelectRevision(item);}
                card.TabStop=true;card.AccessibleName=item.View+" "+item.Before+" sang "+item.After;
                card.KeyDown+=(s,e)=>{if(e.KeyCode==Keys.Enter||e.KeyCode==Keys.Space){SelectRevision(item);e.Handled=true;}};
                revisionCards.Controls.Add(card);revisionCardControls[item.Id]=card;
            }
            if(visible.Count==0)
            {
                var empty=TextLabel(revisionResult.Items.Count==0?"Không có thay đổi trong dữ liệu đã đối chiếu.\r\nXem trạng thái và lưu ý để biết phạm vi kiểm tra.":
                    "Không có DIM trong bộ lọc này\r\nChọn Tất cả thay đổi để xem toàn bộ kết quả.",16,muted,true);
                empty.Dock=DockStyle.None;empty.Height=160;revisionCards.Controls.Add(empty);
            }
            ResizeRevisionCards();revisionCards.ResumeLayout();
            UpdateRevisionNavigation();
        }
        // A read-only comparison surface. It never writes back to snapshot or drawing data.
        sealed class RevisionContentDiff : Control
        {
            sealed class Row
            {
                public int Before,After;
                public string[] Old,Current;
                public bool Changed { get { return Old==null||Current==null||!Old.SequenceEqual(Current); } }
            }
            sealed class Run
            {
                public string Text;
                public bool Changed;
                public Run(string text,bool changed) { Text=text;Changed=changed; }
            }
            readonly List<Row> rows;
            readonly bool table;
            readonly Color ink,muted,border,oldInk,newInk,oldFill,newFill;
            readonly Font body=new Font("Segoe UI",12),bold=new Font("Segoe UI",12,FontStyle.Bold),small=new Font("Segoe UI",9);
            readonly string title,summary,columnHint;
            const TextFormatFlags Flags=TextFormatFlags.NoPadding|TextFormatFlags.NoPrefix|TextFormatFlags.SingleLine;
            public RevisionContentDiff(PHU_DimensionRevision.Item item,bool dark,Color ink,Color muted,Color border)
            {
                this.ink=ink;this.muted=muted;this.border=border;
                SetStyle(ControlStyles.SupportsTransparentBackColor|ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
                BackColor=Color.Transparent;
                oldInk=dark?Color.FromArgb(255,186,165):Color.FromArgb(155,59,38);
                newInk=dark?Color.FromArgb(145,232,197):Color.FromArgb(16,107,77);
                oldFill=dark?Color.FromArgb(78,46,40):Color.FromArgb(255,233,225);
                newFill=dark?Color.FromArgb(30,69,56):Color.FromArgb(215,246,233);
                var source=item.ContentAfter??item.ContentBefore;
                table=source!=null?source.Kind=="Bảng thuộc tính":(item.View??"").StartsWith("Bảng");
                title=table?"Mark bảng":"Mark tên";
                var before=ContentRows(item.ContentBefore,item.Before,source==null);
                var after=ContentRows(item.ContentAfter,item.After,source==null);
                rows=AlignRows(before,after);
                int changed=rows.Count(r=>r.Changed),same=rows.Count-changed;
                string unit=table?"hàng":"dòng";
                summary=before.Count==0?"Được thêm mới":after.Count==0?"Đã xóa":changed+" "+unit+" thay đổi";
                if(table&&before.Count>0&&after.Count>0)
                {
                    var counts=new List<string>();
                    int added=rows.Count(r=>r.Old==null),removed=rows.Count(r=>r.Current==null),edited=changed-added-removed;
                    if(edited>0)counts.Add(edited+" hàng sửa");
                    if(added>0)counts.Add(added+" hàng thêm");
                    if(removed>0)counts.Add(removed+" hàng xóa");
                    summary=String.Join(" · ",counts);
                }
                if(table&&same>0)summary+=" · "+same+" hàng không đổi";
                columnHint=table&&before.Concat(after).Any(r=>r.Contains("製品マーク")&&r.Contains("数量"))?"  ·  Mark / Số lượng":"";
                if(table)rows=rows.Where(r=>r.Changed).ToList();
                AccessibleRole=AccessibleRole.StaticText;
                AccessibleName=title+". "+summary+". Bản cũ: "+item.Before+". Hiện tại: "+item.After;
            }
            static List<string[]> ContentRows(PHU_DimensionRevision.ContentBlock block,string fallback,bool legacy)
            {
                if(block==null)return legacy?new List<string[]> {new[]{fallback??""}}:new List<string[]>();
                return block.Values??new List<string[]> {new[]{block.Text??""}};
            }
            // Anchor identical rows so inserting one table row does not mark every later row as edited.
            static List<Row> AlignRows(IList<string[]> before,IList<string[]> after)
            {
                int n=before.Count,m=after.Count;
                var anchors=new List<Tuple<int,int>>();
                if((long)n*m<=250000)
                {
                    var lengths=new int[n+1,m+1];
                    for(int i=n-1;i>=0;i--)for(int j=m-1;j>=0;j--)
                        lengths[i,j]=before[i].SequenceEqual(after[j])?1+lengths[i+1,j+1]:Math.Max(lengths[i+1,j],lengths[i,j+1]);
                    for(int i=0,j=0;i<n&&j<m;)
                        if(before[i].SequenceEqual(after[j])){anchors.Add(Tuple.Create(i++,j++));}
                        else if(lengths[i+1,j]>=lengths[i,j+1])i++;else j++;
                }
                anchors.Add(Tuple.Create(n,m));
                var result=new List<Row>();int a=0,b=0;
                foreach(var anchor in anchors)
                {
                    while(a<anchor.Item1||b<anchor.Item2)
                    {
                        bool hasOld=a<anchor.Item1,hasNew=b<anchor.Item2;
                        result.Add(new Row {Before=hasOld?a+1:0,After=hasNew?b+1:0,Old=hasOld?before[a++]:null,Current=hasNew?after[b++]:null});
                    }
                    if(a<n&&b<m)result.Add(new Row {Before=a+1,After=b+1,Old=before[a++],Current=after[b++]});
                }
                return result;
            }
            static string[] Tokens(string value)
            {
                return System.Text.RegularExpressions.Regex.Matches(value??"",@"\p{L}+|\p{N}+|\s+|[^\p{L}\p{N}\s]")
                    .Cast<System.Text.RegularExpressions.Match>().Select(m=>m.Value).ToArray();
            }
            static List<Run> CellRuns(string value,string other,bool missing)
            {
                var tokens=Tokens(value);var matches=new bool[tokens.Length];
                if(!missing)
                {
                    var peers=Tokens(other);
                    if((long)tokens.Length*peers.Length<=250000)
                    {
                        var lengths=new int[tokens.Length+1,peers.Length+1];
                        for(int i=tokens.Length-1;i>=0;i--)for(int j=peers.Length-1;j>=0;j--)
                            lengths[i,j]=tokens[i]==peers[j]?1+lengths[i+1,j+1]:Math.Max(lengths[i+1,j],lengths[i,j+1]);
                        for(int i=0,j=0;i<tokens.Length&&j<peers.Length;)
                            if(tokens[i]==peers[j]){matches[i++]=true;j++;}
                            else if(lengths[i+1,j]>=lengths[i,j+1])i++;else j++;
                    }
                    else if(value==other)for(int i=0;i<matches.Length;i++)matches[i]=true;
                }
                if(tokens.Length==0)return new List<Run>{new Run("(trống)",missing||!String.IsNullOrEmpty(other))};
                var result=new List<Run>();
                for(int i=0;i<tokens.Length;i++)
                {
                    bool changed=!matches[i];
                    if(result.Count>0&&result[result.Count-1].Changed==changed)result[result.Count-1].Text+=tokens[i];
                    else result.Add(new Run(tokens[i],changed));
                }
                return result;
            }
            static List<Run> Runs(string[] value,string[] other)
            {
                if(value==null)return new List<Run>{new Run("—",false)};
                if(value.Length==0)return new List<Run>{new Run("(hàng trống)",other==null||other.Length!=0)};
                var result=new List<Run>();
                for(int c=0;c<value.Length;c++)
                {
                    if(c>0)result.Add(new Run("   ·   ",false));
                    result.AddRange(CellRuns(value[c],other!=null&&c<other.Length?other[c]:null,other==null||c>=other.Length));
                }
                return result;
            }
            int S(int value) { return Math.Max(1,(int)Math.Round(value*DeviceDpi/96.0)); }
            int TextWidth(string text,Font font,Graphics graphics)
            {
                var size=new Size(Int32.MaxValue,Int32.MaxValue);
                return (graphics==null?TextRenderer.MeasureText(text,font,size,Flags):TextRenderer.MeasureText(graphics,text,font,size,Flags)).Width;
            }
            int Flow(Graphics graphics,List<Run> runs,Rectangle area,bool previous)
            {
                int x=area.X,y=area.Y,line=TextRenderer.MeasureText("Ag",body).Height+S(6);
                foreach(var run in runs)
                {
                    Font font=run.Changed?bold:body;
                    // Break oversized tokens by Unicode text elements; no clipped long marks.
                    var pieces=new List<string>();
                    if(TextWidth(run.Text,font,graphics)>area.Width)
                    {
                        var elements=System.Globalization.StringInfo.GetTextElementEnumerator(run.Text);
                        while(elements.MoveNext())pieces.Add(elements.GetTextElement());
                    }
                    else pieces.Add(run.Text);
                    foreach(string piece in pieces)
                    {
                        if(piece.Contains("\n")||piece.Contains("\r")){x=area.X;y+=line;continue;}
                        int w=TextWidth(piece,font,graphics);
                        if(x>area.X&&x+w>area.Right){x=area.X;y+=line;}
                        if(graphics!=null)
                        {
                            var rect=new Rectangle(x,y,w,line-S(2));
                            if(run.Changed)using(var brush=new SolidBrush(previous?oldFill:newFill))graphics.FillRectangle(brush,rect);
                            TextRenderer.DrawText(graphics,piece,font,rect,run.Changed?(previous?oldInk:newInk):ink,Flags|TextFormatFlags.VerticalCenter);
                        }
                        x+=w;
                    }
                }
                return y-area.Y+line;
            }
            int LayoutContent(Graphics graphics,int width)
            {
                int label=S(76),gap=S(26),column=Math.Max(S(60),(width-label-gap)/2);
                int left=label,right=label+column+gap,y=S(72);
                if(graphics!=null)
                {
                    TextRenderer.DrawText(graphics,title,bold,new Rectangle(0,0,width,S(28)),ink,Flags);
                    TextRenderer.DrawText(graphics,summary,small,new Rectangle(0,S(28),width,S(22)),muted,Flags);
                    TextRenderer.DrawText(graphics,"BẢN CŨ"+columnHint,small,new Rectangle(left,S(52),column,S(20)),oldInk,Flags);
                    TextRenderer.DrawText(graphics,"HIỆN TẠI"+columnHint,small,new Rectangle(right,S(52),column,S(20)),newInk,Flags);
                }
                foreach(var row in rows)
                {
                    var oldRuns=Runs(row.Old,row.Current);var newRuns=Runs(row.Current,row.Old);
                    int height=Math.Max(Flow(null,oldRuns,new Rectangle(left,y,column,0),true),Flow(null,newRuns,new Rectangle(right,y,column,0),false))+S(14);
                    if(graphics!=null)
                    {
                        using(var pen=new Pen(border))graphics.DrawLine(pen,0,y,width,y);
                        string number=row.Before==row.After?row.Before.ToString():row.Before+" → "+row.After;
                        string rowLabel=row.Before==0?"Thêm · "+row.After:row.After==0?"Xóa · "+row.Before:(table?"Hàng ":"Dòng ")+number;
                        TextRenderer.DrawText(graphics,rowLabel,small,new Rectangle(0,y+S(10),label-S(6),height),muted,TextFormatFlags.NoPadding|TextFormatFlags.NoPrefix|TextFormatFlags.WordBreak);
                        Flow(graphics,oldRuns,new Rectangle(left,y+S(7),column,0),true);
                        Flow(graphics,newRuns,new Rectangle(right,y+S(7),column,0),false);
                    }
                    y+=height;
                }
                return y+S(4);
            }
            public int RequiredHeight(int width) { return LayoutContent(null,width); }
            protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e);LayoutContent(e.Graphics,ClientSize.Width); }
            protected override void Dispose(bool disposing) { if(disposing){body.Dispose();bold.Dispose();small.Dispose();}base.Dispose(disposing); }
        }
        void SelectRevision(PHU_DimensionRevision.Item item)
        {
            if(checking||(item.ReadOnly&&!PHU_DimensionRevision.CanSelectMark(item)))return;
            try
            {
                PHU_DimensionRevision.Select(revisionResult,item);revisionSelected=item.Id;
                foreach(var pair in revisionCardControls){pair.Value.BackColor=pair.Key==item.Id?soft:surface;pair.Value.Invalidate(true);}
                UpdateRevisionNavigation();
                ShowRevisionError("");
            }
            catch(Exception ex){ShowRevisionError(ex.GetBaseException().Message);}
        }
        void DeleteRevisionSection()
        {
            if(checking)return;
            checking=true;UseWaitCursor=true;
            try
            {
                PHU_DimensionRevision.DeleteRevisionSection();
                ShowRevisionError("");
            }
            catch(Exception ex){ShowRevisionError("Delete REV: "+ex.Message);}
            finally{checking=false;UseWaitCursor=false;}
        }
        void ToggleRevision(PHU_DimensionRevision.Item item)
        {
            if(checking||!CanToggleRevision(item)||!revisionChoices.ContainsKey(item.Id))return;
            checking=true;UseWaitCursor=true;
            var choice=revisionChoices[item.Id];bool old=choice.Excluded;
            try
            {
                bool disable=item.AlreadyRed;
                SaveRevisionChoices();
                var identities=revisionResult.Items.Where(CanToggleRevision).ToDictionary(i=>i,i=>i.Id);
                PHU_DimensionRevision.SetColor(revisionResult,new[]{item},disable?choice.Original:D.DrawingColors.Red);
                RemapRevisionChoices(identities);
                choice.Excluded=disable;
                revisionSelected=item.Id;RenderRevisionCards();UpdateRevisionSummary();
                ShowRevisionError("");
            }
            catch(Exception ex)
            {
                choice.Excluded=old;
                ShowRevisionError("Chưa hoàn tất: "+ex.Message);
                RenderRevisionCards();UpdateRevisionSummary();
            }
            finally{checking=false;UseWaitCursor=false;}
            // Selection failure must not undo a successful color choice.
            SelectRevisionAfterToggle(item);
        }
        void SelectRevisionAfterToggle(PHU_DimensionRevision.Item item)
        {
            if(!String.IsNullOrEmpty(revisionDetail.Text))return;
            SelectRevision(item);
        }
        // Same slim rounded thumb and theme colors as the Check DIM report.
        // The content is clipped by a viewport; no native white scrollbar is created.
        sealed class RevisionScroll : Control
        {
            readonly Panel viewport;readonly FlowLayoutPanel content;
            readonly Color thumbColor,hoverColor;
            int offset,position;bool dragging,hover;
            public RevisionScroll(Panel viewport,FlowLayoutPanel content,bool dark,Color accent)
            {
                this.viewport=viewport;this.content=content;
                thumbColor=dark?Color.FromArgb(95,82,70):Color.FromArgb(148,163,184);hoverColor=accent;
                DoubleBuffered=true;Cursor=Cursors.Hand;AccessibleName="Cuộn danh sách Revision";
                content.MouseWheel+=(s,e)=>MoveTo(position+(e.Delta<0?90:-90));
                viewport.MouseWheel+=(s,e)=>MoveTo(position+(e.Delta<0?90:-90));
                viewport.SizeChanged+=(s,e)=>Clamp();
            }
            int Maximum {get{return Math.Max(0,content.Height-viewport.ClientSize.Height);}}
            public void Clamp(){MoveTo(position);}
            public void Reset(){MoveTo(0);}
            public void Reveal(Control card)
            {
                if(card.Top<position)MoveTo(card.Top);
                else if(card.Bottom>position+viewport.ClientSize.Height)MoveTo(card.Bottom-viewport.ClientSize.Height);
            }
            void MoveTo(int value){position=Math.Max(0,Math.Min(Maximum,value));content.Location=new Point(0,-position);Invalidate();}
            Rectangle Thumb
            {
                get
                {
                    int space=Math.Max(1,Height-10),h=Math.Min(space,Math.Max(30,space*viewport.Height/Math.Max(1,content.Height)));
                    return new Rectangle(5,5+(space-h)*position/Math.Max(1,Maximum),Math.Max(4,Width-10),h);
                }
            }
            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);if(Maximum==0)return;
                e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using(var path=Rounded(Thumb,3))using(var brush=new SolidBrush(hover||dragging?hoverColor:thumbColor))e.Graphics.FillPath(brush,path);
            }
            void Drag(int y){MoveTo((int)Math.Round((y-offset-5)*(double)Maximum/Math.Max(1,Height-10-Thumb.Height)));}
            protected override void OnMouseDown(MouseEventArgs e)
            {base.OnMouseDown(e);if(e.Button!=MouseButtons.Left||Maximum==0)return;offset=Thumb.Contains(e.Location)?e.Y-Thumb.Y:Thumb.Height/2;dragging=true;Capture=true;Drag(e.Y);}
            protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);if(dragging)Drag(e.Y);}
            protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);dragging=false;Capture=false;Invalidate();}
            protected override void OnMouseCaptureChanged(EventArgs e){base.OnMouseCaptureChanged(e);if(!Capture)dragging=false;Invalidate();}
            protected override void OnMouseEnter(EventArgs e){base.OnMouseEnter(e);hover=true;Invalidate();}
            protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);hover=false;Invalidate();}
            protected override void OnMouseWheel(MouseEventArgs e){base.OnMouseWheel(e);MoveTo(position+(e.Delta<0?90:-90));}
        }
    }
}
#endregion

#region PHU_DimensionReportForm - Khung báo cáo chung, nhóm kết quả và kiểm lại


namespace Tekla.Technology.Akit.UserScript
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Linq;
    using System.Runtime.InteropServices;
    using System.Windows.Forms;
    using D = Tekla.Structures.Drawing;

    // Presentation only; the classifier and its results are unchanged.
    public sealed partial class PHU_DimensionReportForm : Form
    {
        PHU_DimensionCheck.Result result;
        readonly bool dark;
        readonly Color surface, ink, muted, border, accent, soft;
        FlowLayoutPanel findingCards;
        Panel findingViewport;
        RevisionScroll findingScroll;
        readonly List<PHU_DimensionCheck.Finding> findingItems=new List<PHU_DimensionCheck.Finding>();
        int findingIndex=-1;
        Panel reportContent;
        Label feedback;
        bool checking;
        readonly Func<PHU_DimensionCheck.Result> analyze;
        readonly Action<PHU_DimensionCheck.Result,PHU_DimensionCheck.Finding> selectFinding;
        readonly ToolTip tips=new ToolTip { AutoPopDelay=15000,InitialDelay=400,ReshowDelay=150 };
        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window,int message,IntPtr wParam,IntPtr lParam);

        public PHU_DimensionReportForm(PHU_DimensionCheck.Result result, bool dark, Func<PHU_DimensionCheck.Result> analyze = null,
            Action<PHU_DimensionCheck.Result,PHU_DimensionCheck.Finding> selectFinding = null)
        {
            this.analyze=analyze??PHU_DimensionCheck.Analyze;
            this.selectFinding=selectFinding??SelectInTekla;
            this.result=result; this.dark=dark;
            surface=dark?Color.FromArgb(30,32,36):Color.White;
            ink=dark?Color.FromArgb(235,238,244):Color.FromArgb(26,36,52);
            muted=dark?Color.FromArgb(157,168,188):Color.FromArgb(102,117,138);
            border=dark?Color.FromArgb(70,75,84):Color.FromArgb(208,220,236);
            accent=dark?Color.FromArgb(235,162,108):Color.FromArgb(41,99,222);
            soft=dark?Color.FromArgb(55,43,36):Color.FromArgb(235,242,255);
            BackColor=dark?Color.FromArgb(15,15,17):Color.FromArgb(248,250,252);
            ForeColor=ink; Font=new Font("Segoe UI",10);
            Text="TTSK  ·  Kiểm tra chân kích thước";
            ClientSize=new Size(1180,730); MinimumSize=new Size(1000,700);
            AutoScaleMode=AutoScaleMode.Dpi; StartPosition=FormStartPosition.CenterScreen;
            Padding=new Padding(1); ShowIcon=false; FormBorderStyle=FormBorderStyle.None;
            MaximizedBounds=Screen.FromPoint(Cursor.Position).WorkingArea;
            BuildContent();
        }
        Label emptyList, position;
        Control successCheck;

        Button previous, next, recheckButton, revisionButton;

        DateTime checkedAt = DateTime.Now;

        Label TextLabel(string text, float size, Color color, bool bold = false)
        {
            return new Label { Text=text, Dock=DockStyle.Fill, ForeColor=color, AutoEllipsis=true,
                Margin=Padding.Empty, TextAlign=ContentAlignment.MiddleLeft,
                Font=new Font(Font.FontFamily,size,bold?FontStyle.Bold:FontStyle.Regular) };
        }
        TableLayoutPanel Stack(params int[] heights)
        {
            var panel=new TableLayoutPanel { Dock=DockStyle.Fill, ColumnCount=1, RowCount=heights.Length,
                Margin=Padding.Empty, Padding=Padding.Empty };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            foreach(int h in heights) panel.RowStyles.Add(new RowStyle(h==0?SizeType.Percent:SizeType.Absolute,h==0?100:h));
            return panel;
        }
        Color Amber { get { return dark?Color.FromArgb(255,191,112):Color.FromArgb(166,88,16); } }
        Color AmberSoft { get { return dark?Color.FromArgb(58,43,29):Color.FromArgb(255,245,229); } }
        Color Green { get { return dark?Color.FromArgb(111,218,177):Color.FromArgb(22,124,94); } }

        void BuildContent()
        {
            int errors=result.Findings.Count(f=>f.Status=="ERROR");
            int reviews=result.Findings.Count(f=>f.Status=="REVIEW");
            bool incomplete=result.Warnings.Count>0 || reviews>0;
            var root=Stack(48,28,40,0,46);
            reportContent=new Panel {Dock=DockStyle.Fill,Padding=new Padding(26,20,26,18),BackColor=BackColor};
            reportContent.Controls.Add(root);Controls.Add(reportContent);BuildChrome();
            var heading=new Panel {Dock=DockStyle.Fill,Margin=Padding.Empty};
            heading.Controls.Add(TextLabel("Kiểm tra chân kích thước",20,ink,true));
            revisionButton=Button("Revision · R →",false);revisionButton.Name="OpenRevision";
            revisionButton.Dock=DockStyle.Right;revisionButton.Width=145;
            revisionButton.Click+=(s,e)=>OpenRevision();heading.Controls.Add(revisionButton);
            root.Controls.Add(heading,0,0);
            var metadata=new Panel {Dock=DockStyle.Fill,Margin=Padding.Empty};
            var timestamp=TextLabel("Kiểm tra · "+checkedAt.ToString("HH:mm"),10,muted);
            timestamp.Dock=DockStyle.Right;timestamp.Width=180;timestamp.TextAlign=ContentAlignment.MiddleRight;
            var drawing=TextLabel(result.Drawing,10,muted);tips.SetToolTip(drawing,result.Drawing);
            metadata.Controls.Add(drawing);metadata.Controls.Add(timestamp);root.Controls.Add(metadata,0,1);
            var summary=new Panel {Dock=DockStyle.Fill,Margin=Padding.Empty};
            string status=errors>0?errors+" chân cần xem":incomplete?"Cần xác minh":result.SkippedAngles.Count>0?"DIM thẳng: không phát hiện lỗi":"✓ Không phát hiện lỗi";
            if(reviews>0)status+="   ·   "+reviews+" chưa kết luận";
            status+="   ·   "+result.Dimensions+" chuỗi   ·   "+result.Views+" hình chiếu";
            if(result.SkippedAngles.Count>0)status+="   ·   Bỏ qua "+result.SkippedAngles.Count+" DIM góc";
            summary.Controls.Add(TextLabel(status,11,errors>0||incomplete?Amber:Green,true));
            if(result.Warnings.Count>0||result.SkippedAngles.Count>0)
            {
                var coverage=Button((result.Warnings.Count+result.SkippedAngles.Count)+" lưu ý",false);coverage.Dock=DockStyle.Right;coverage.Width=115;
                coverage.Click+=(s,e)=>ShowCoverage();summary.Controls.Add(coverage);
                tips.SetToolTip(coverage,"Xem DIM đã bỏ qua và dữ liệu chưa kiểm tra được");
            }
            root.Controls.Add(summary,0,2);
            var listHost=new Panel {Dock=DockStyle.Fill,Margin=Padding.Empty};
            findingViewport=new Panel {Dock=DockStyle.Fill,BackColor=BackColor};
            findingCards=new FlowLayoutPanel {Name="FindingCards",FlowDirection=FlowDirection.TopDown,WrapContents=false,
                AutoScroll=false,BackColor=BackColor,Padding=new Padding(0,3,8,3)};
            findingScroll=new RevisionScroll(findingViewport,findingCards,dark,accent) {Name="FindingScroll",Dock=DockStyle.Right,Width=16,BackColor=BackColor};
            findingScroll.AccessibleName="Cuộn vị trí cần kiểm tra";
            findingViewport.Controls.Add(findingCards);findingViewport.SizeChanged+=(s,e)=>ResizeFindingCards();
            listHost.Controls.Add(findingViewport);listHost.Controls.Add(findingScroll);
            emptyList=TextLabel(result.Warnings.Count>0?(result.Dimensions==0&&result.SkippedAngles.Count>0?"Không có DIM thẳng để kiểm tra.\r\nĐã bỏ qua "+result.SkippedAngles.Count+" DIM góc.":"Một số dữ liệu chưa kiểm tra được.\r\nMở lưu ý để xem chi tiết."):
                result.SkippedAngles.Count>0?"Đã kiểm tra các chân DIM thẳng.\r\nBỏ qua "+result.SkippedAngles.Count+" DIM góc; không tính vào kết quả.":"Không có vị trí cần xử lý",14,muted);
            emptyList.TextAlign=ContentAlignment.MiddleCenter;listHost.Controls.Add(emptyList);emptyList.BringToFront();
            successCheck=new SuccessCheck {Name="SuccessCheck",Dock=DockStyle.Fill,ForeColor=Green,BackColor=BackColor,
                Text=emptyList.Text,Font=emptyList.Font,AccessibleName="Không phát hiện lỗi kích thước",Visible=false};
            listHost.Controls.Add(successCheck);successCheck.BringToFront();
            root.Controls.Add(listHost,0,3);
            var footer=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=5,RowCount=1,Margin=Padding.Empty};
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,195));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,145));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,145));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,85));
            recheckButton=Button("↻  Kiểm tra · C / F5",false);recheckButton.Name="Recheck";recheckButton.Dock=DockStyle.Fill;
            recheckButton.Click+=(s,e)=>Recheck(recheckButton);footer.Controls.Add(recheckButton,0,0);
            previous=Button("←  Vị trí trước",false);previous.Dock=DockStyle.Fill;previous.Click+=(s,e)=>MoveFinding(-1);
            next=Button("Vị trí tiếp  →",false);next.Dock=DockStyle.Fill;next.Click+=(s,e)=>MoveFinding(1);
            tips.SetToolTip(previous,"Chọn DIM trước trên Tekla. ← / ↑");
            tips.SetToolTip(next,"Chọn DIM tiếp trên Tekla. → / ↓");
            footer.Controls.Add(previous,1,0);footer.Controls.Add(next,2,0);
            position=TextLabel("",10,muted);position.Padding=new Padding(12,0,0,0);footer.Controls.Add(position,3,0);
            var close=Button("Đóng",false);close.Dock=DockStyle.Fill;close.DialogResult=DialogResult.Cancel;
            footer.Controls.Add(close,4,0);CancelButton=close;root.Controls.Add(footer,0,4);
            feedback=position;
            PopulateFindings();
        }
        void BuildChrome()
        {
            var chrome=new Panel { Dock=DockStyle.Top,Height=36,BackColor=surface };
            var caption=TextLabel("TTSK   ·   Drawing Quality",9,muted); caption.Padding=new Padding(22,0,0,0);
            MouseEventHandler drag=(s,e)=>{if(e.Button==MouseButtons.Left){ReleaseCapture();SendMessage(Handle,0xA1,new IntPtr(2),IntPtr.Zero);}};
            caption.MouseDown+=drag; chrome.MouseDown+=drag; caption.DoubleClick+=(s,e)=>ToggleMaximize();
            var buttons=new FlowLayoutPanel { Dock=DockStyle.Right,Width=138,Margin=Padding.Empty };
            foreach(string symbol in new[]{"−","□","×"})
            {
                string action=symbol;
                var b=Button(symbol,false); b.Size=new Size(46,34); b.Margin=Padding.Empty; b.TabStop=false; b.FlatAppearance.BorderSize=0;
                b.AccessibleName=action=="×"?"Đóng cửa sổ":action=="□"?"Phóng to hoặc khôi phục":"Thu nhỏ";
                b.Click+=(s,e)=>{if(action=="×")Close();else if(action=="□")ToggleMaximize();else WindowState=FormWindowState.Minimized;};
                buttons.Controls.Add(b);
            }
            chrome.Controls.Add(caption); chrome.Controls.Add(buttons); Controls.Add(chrome);
        }
        static bool IsActionable(PHU_DimensionCheck.Finding f) { return f.Status=="ERROR" || f.Status=="REVIEW"; }
        void PopulateFindings()
        {
            findingItems.Clear();findingItems.AddRange(result.Findings.Where(IsActionable).OrderBy(f=>f.Status=="ERROR"?0:1));
            findingIndex=findingItems.Count>0?0:-1;
            foreach(Control control in findingCards.Controls.Cast<Control>().ToArray())control.Dispose();
            foreach(var finding in findingItems)
            {
                int index=findingItems.IndexOf(finding);
                var card=Card();card.Name="FindingCard";card.Tag=finding;card.Dock=DockStyle.None;
                card.Margin=new Padding(0,0,0,8);card.Padding=new Padding(16,8,14,8);card.TabStop=true;
                var rows=Stack(30,40,28,28);card.Controls.Add(rows);
                var top=new Panel {Dock=DockStyle.Fill,Margin=Padding.Empty};
                var view=TextLabel(finding.ViewLabel,11,ink,true);
                var state=TextLabel(finding.Status=="ERROR"?"●  CẦN XEM":"○  CHƯA KẾT LUẬN",9,finding.Status=="ERROR"?Amber:accent,true);
                state.Dock=DockStyle.Right;state.Width=190;state.TextAlign=ContentAlignment.MiddleRight;
                top.Controls.Add(view);top.Controls.Add(state);rows.Controls.Add(top,0,0);
                var valueRow=new Panel {Dock=DockStyle.Fill,Margin=Padding.Empty};
                var value=TextLabel(finding.DimensionLabel,18,ink,true);
                var select=Button("Chọn DIM",false);select.Dock=DockStyle.Right;select.Width=175;
                select.Click+=(s,e)=>ChooseFinding(index);
                valueRow.Controls.Add(value);valueRow.Controls.Add(select);rows.Controls.Add(valueRow,0,1);
                var location=TextLabel(finding.Location,10,ink);rows.Controls.Add(location,0,2);
                var reason=TextLabel(PHU_DimensionCheck.UserReason(finding),9,muted);reason.Name="FindingReason";rows.Controls.Add(reason,0,3);
                tips.SetToolTip(value,finding.DimensionLabel);tips.SetToolTip(view,finding.ViewLabel);
                tips.SetToolTip(location,finding.Location);tips.SetToolTip(reason,reason.Text);
                int titleHeight=Math.Max(30,TextRenderer.MeasureText("Ag",view.Font).Height+6);
                int valueHeight=Math.Max(40,TextRenderer.MeasureText("Ag",value.Font).Height+6);
                int locationHeight=Math.Max(28,TextRenderer.MeasureText("Ag",location.Font).Height+6);
                rows.RowStyles[0].Height=titleHeight;rows.RowStyles[1].Height=valueHeight;rows.RowStyles[2].Height=locationHeight;
                foreach(Control hit in new Control[]{card,rows,top,view,state,valueRow,value,location,reason})
                {hit.Cursor=Cursors.Hand;hit.Click+=(s,e)=>ChooseFinding(index);}
                card.AccessibleName=finding.ViewLabel+" "+finding.DimensionLabel+" "+finding.Location;
                card.KeyDown+=(s,e)=>{
                    if(e.KeyCode==Keys.Enter||e.KeyCode==Keys.Space){ChooseFinding(index);e.Handled=true;}
                    else if(e.KeyCode==Keys.Down||e.KeyCode==Keys.Up)
                    {findingIndex=index;MoveFinding(e.KeyCode==Keys.Down?1:-1);findingCards.Controls[findingIndex].Focus();e.Handled=true;}
                };
                findingCards.Controls.Add(card);
            }
            emptyList.Visible=findingItems.Count==0;
            bool showSuccess=findingItems.Count==0 && result.Warnings.Count==0 && result.Dimensions>0;
            successCheck.Visible=showSuccess;
            if(showSuccess)
            {
                emptyList.Visible=false;
            }
            findingViewport.Visible=findingScroll.Visible=findingItems.Count>0;
            previous.Visible=next.Visible=position.Visible=findingItems.Count>0;
            ResizeFindingCards();UpdateActions();
        }
        void ResizeFindingCards()
        {
            if(findingCards==null)return;
            findingCards.Width=Math.Max(300,findingViewport.ClientSize.Width);
            foreach(Control card in findingCards.Controls)
            {
                card.Width=findingCards.Width-findingCards.Padding.Horizontal;
                var rows=(TableLayoutPanel)card.Controls[0];
                var reason=(Label)rows.Controls.Find("FindingReason",false)[0];
                int width=Math.Max(100,card.Width-card.Padding.Horizontal-((RaisedCard)card).FrameSpace);
                int reasonHeight=Math.Max(26,TextRenderer.MeasureText(reason.Text,reason.Font,new Size(width,Int32.MaxValue),TextFormatFlags.WordBreak).Height+6);
                rows.RowStyles[3].Height=reasonHeight;
                card.Height=card.Padding.Vertical+((RaisedCard)card).FrameSpace+(int)rows.RowStyles.Cast<RowStyle>().Sum(r=>r.Height);
            }
            findingCards.Height=findingCards.Padding.Vertical+findingCards.Controls.Cast<Control>().Sum(c=>c.Height+c.Margin.Vertical);
            findingScroll.Clamp();
        }
        void ChooseFinding(int index)
        {
            if(checking||index<0||index>=findingItems.Count)return;
            findingIndex=index;UpdateActions();findingScroll.Reveal(findingCards.Controls[index]);SelectDimension();
        }
        void MoveFinding(int offset)
        {
            if(checking||findingItems.Count==0)return;
            ChooseFinding(Math.Max(0,Math.Min(findingItems.Count-1,findingIndex+offset)));
        }
        void ShowCoverage()
        {
            using(var dialog=new Form { Text="Phạm vi kiểm tra",StartPosition=FormStartPosition.CenterParent,
                Size=new Size(660,440),MinimumSize=new Size(440,280),BackColor=surface,ForeColor=ink,Font=Font,ShowIcon=false })
            {
                var info=new TextBox { Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,Dock=DockStyle.Fill,
                    BackColor=surface,ForeColor=ink,BorderStyle=BorderStyle.None,
                    Text=(result.SkippedAngles.Count>0?"DIM GÓC ĐÃ BỎ QUA\r\n\r\n"+String.Join("\r\n",result.SkippedAngles)+"\r\n\r\n":"")+"DỮ LIỆU CHƯA KIỂM TRA ĐƯỢC\r\n\r\n"+
                    (result.Warnings.Count>0?string.Join("\r\n\r\n",result.Warnings):"Không có cảnh báo đọc dữ liệu.")+
                    "\r\n\r\nCHÂN CHƯA KẾT LUẬN\r\n\r\n"+
                    string.Join("\r\n\r\n",result.Findings.Where(f=>f.Status=="REVIEW").Select(f=>f.ViewLabel+" · "+f.DimensionLabel+"\r\n"+f.Location+"\r\n"+f.Reason)) };
                dialog.Padding=new Padding(20); dialog.Controls.Add(info); dialog.ShowDialog(this);
            }
        }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if(batchSession!=null)
            {
                if(keyData==(Keys.Control|Keys.F)){batchSearch.Focus();batchSearch.SelectAll();return true;}
                if(keyData==Keys.F5){if(!checking&&!batchSession.Running)StartOrPauseBatch();return true;}
                if(keyData==Keys.Escape&&batchSession.Running){StartOrPauseBatch();return true;}
                return base.ProcessCmdKey(ref msg,keyData);
            }
            // These commands act on the current Tekla drawing, including after switching drawings.
            if(keyData==Keys.C){CheckCurrentDimensions();return true;}
            if(keyData==Keys.R){if(!checking){OpenRevision();RefreshRevision();}return true;}
            if(revisionPanel != null)
            {
                if(keyData==Keys.F5) { RefreshRevision(); return true; }
                if(keyData==Keys.Right || keyData==Keys.Down) { MoveRevision(1); return true; }
                if(keyData==Keys.Left || keyData==Keys.Up) { MoveRevision(-1); return true; }
                if(keyData==(Keys.Control|Keys.Right)) { MoveRevision(1); return true; }
                if(keyData==(Keys.Control|Keys.Left)) { MoveRevision(-1); return true; }
                if(keyData==Keys.Escape && !checking) { CloseRevision(); return true; }
                return base.ProcessCmdKey(ref msg,keyData);
            }
            if(keyData==Keys.F5){if(!checking)Recheck(recheckButton);return true;}

            if(keyData==Keys.Right || keyData==Keys.Down){MoveFinding(1);return true;}
            if(keyData==Keys.Left || keyData==Keys.Up){MoveFinding(-1);return true;}
            if(keyData==(Keys.Alt|Keys.Down)){MoveFinding(1);return true;}
            if(keyData==(Keys.Alt|Keys.Up)){MoveFinding(-1);return true;}
            if(keyData==Keys.Enter && findingCards.ContainsFocus){SelectDimension();return true;}
            return base.ProcessCmdKey(ref msg,keyData);
        }
        void Recheck(Button button)
        {
            if(checking) return;
            checking=true; button.Enabled=false; UpdateActions();
            button.Text="Đang kiểm tra…"; UseWaitCursor=true;
            BeginInvoke(new Action(()=>
            {
                try
                {
                    var fresh=analyze();
                    if(IsDisposed) return;
                    SuspendLayout();
                    try
                    {
                        foreach(Control control in Controls.Cast<Control>().ToArray()) control.Dispose();
                        result=fresh; checkedAt=DateTime.Now; BuildContent();
                        PHU_DimensionCheck.AcceptRecheck(fresh);

                    }
                    finally { ResumeLayout(true); }
                }
                catch(Exception ex)
                {
                    if(!IsDisposed)
                    {
                        feedback.Text="Kiểm tra lại chưa thành công.";
                        MessageBox.Show(this,"Không thể kiểm tra lại: "+ex.Message+"\r\nKết quả đang hiển thị là lần kiểm tra trước.","Recheck",MessageBoxButtons.OK,MessageBoxIcon.Information);
                        button.Text="↻  Kiểm tra lại  ·  F5"; button.Enabled=true;
                    }
                }
                finally { checking=false; if(!IsDisposed) { UseWaitCursor=false; UpdateActions(); } }
            }));
        }
        void ToggleMaximize()
        { MaximizedBounds=Screen.FromHandle(Handle).WorkingArea; WindowState=WindowState==FormWindowState.Maximized?FormWindowState.Normal:FormWindowState.Maximized; }
        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            if(message.Msg!=0x84 || WindowState!=FormWindowState.Normal) return;
            long coordinates=message.LParam.ToInt64();
            Point point=PointToClient(new Point(unchecked((short)(coordinates&0xffff)),unchecked((short)((coordinates>>16)&0xffff))));
            int edge=Math.Max(6,DeviceDpi/16);
            bool left=point.X<edge,right=point.X>=ClientSize.Width-edge,top=point.Y<edge,bottom=point.Y>=ClientSize.Height-edge;
            int hit=top?(left?13:right?14:12):bottom?(left?16:right?17:15):left?10:right?11:0;
            if(hit!=0) message.Result=new IntPtr(hit);
        }
        sealed class SuccessCheck : Control
        {
            public SuccessCheck() { DoubleBuffered=true;ResizeRedraw=true; }
            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                float size=Math.Min(150f*DeviceDpi/96f,Math.Min(ClientSize.Width,ClientSize.Height)*.65f);
                if(size<=0)return;
                int textHeight=TextRenderer.MeasureText(Text,Font,new Size(Math.Max(1,Width-24),Int32.MaxValue),TextFormatFlags.WordBreak).Height;
                float gap=20f*DeviceDpi/96f;
                float x=(ClientSize.Width-size)/2f,y=(ClientSize.Height-size-gap-textHeight)/2f;
                using(var pen=new Pen(ForeColor,Math.Max(3f,size*.085f)))
                {
                    pen.StartCap=pen.EndCap=System.Drawing.Drawing2D.LineCap.Round;
                    pen.LineJoin=System.Drawing.Drawing2D.LineJoin.Round;
                    e.Graphics.DrawLines(pen,new[]{new PointF(x+size*.12f,y+size*.52f),
                        new PointF(x+size*.39f,y+size*.78f),new PointF(x+size*.9f,y+size*.2f)});
                }
                TextRenderer.DrawText(e.Graphics,Text,Font,new Rectangle(12,(int)(y+size+gap),Math.Max(1,Width-24),textHeight),
                    ForeColor,TextFormatFlags.HorizontalCenter|TextFormatFlags.WordBreak);
            }
        }
        Panel Card()
        {
            return new RaisedCard(dark,border) { Dock=DockStyle.Fill,BackColor=surface };
        }
        static System.Drawing.Drawing2D.GraphicsPath Rounded(Rectangle rectangle, int radius)
        {
            var path=new System.Drawing.Drawing2D.GraphicsPath();
            int d=Math.Max(1,Math.Min(radius*2,Math.Min(rectangle.Width,rectangle.Height)));
            path.AddArc(rectangle.Left,rectangle.Top,d,d,180,90);
            path.AddArc(rectangle.Right-d,rectangle.Top,d,d,270,90);
            path.AddArc(rectangle.Right-d,rectangle.Bottom-d,d,d,0,90);
            path.AddArc(rectangle.Left,rectangle.Bottom-d,d,d,90,90);
            path.CloseFigure(); return path;
        }
        // Reserve space around child controls so they cannot cover the bevel or shadow.
        sealed class RaisedCard : Panel
        {
            readonly bool dark;
            readonly Color border;
            public RaisedCard(bool dark,Color border)
            { this.dark=dark; this.border=border; DoubleBuffered=true; ResizeRedraw=true; }
            int Rim { get { return Math.Max(2,DeviceDpi/48); } }
            int Shadow { get { return Math.Max(6,DeviceDpi/16); } }
            public int FrameSpace { get { return 2*Rim+Shadow; } }
            public override Rectangle DisplayRectangle
            {
                get { var r=base.DisplayRectangle; return new Rectangle(r.X+Rim,r.Y+Rim,
                    Math.Max(0,r.Width-2*Rim-Shadow),Math.Max(0,r.Height-2*Rim-Shadow)); }
            }
            protected override void OnPaintBackground(PaintEventArgs e)
            {
                e.Graphics.Clear(dark?Color.FromArgb(15,15,17):Color.FromArgb(248,250,252));
                e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                int shadow=Shadow;
                var face=new Rectangle(0,0,Math.Max(1,Width-shadow-1),Math.Max(1,Height-shadow-1));
                for(int i=shadow;i>=1;i--)
                    using(var brush=new SolidBrush(Color.FromArgb(dark?12:5,dark?Color.Black:Color.FromArgb(50,76,110))))
                    using(var path=Rounded(new Rectangle(i,i,face.Width,face.Height),10)) e.Graphics.FillPath(brush,path);
                using(var path=Rounded(face,10))
                {
                    using(var brush=new SolidBrush(BackColor)) e.Graphics.FillPath(brush,path);
                    using(var pen=new Pen(border)) e.Graphics.DrawPath(pen,path);
                }
            }
        }
        Label Label(string text,int x,int y,int w,int h,float size,Color color,bool bold=false)
        { return new Label { Text=text,Bounds=new Rectangle(x,y,w,h),ForeColor=color,Font=new Font(Font.FontFamily,size,bold?FontStyle.Bold:FontStyle.Regular),AutoEllipsis=true }; }
        Button Button(string text,bool primary)
        {
            var button=new RaisedButton(dark,border,primary) { Text=text,FlatStyle=FlatStyle.Flat,BackColor=primary?accent:surface,
                ForeColor=primary?(dark?Color.FromArgb(27,24,21):Color.White):ink,Cursor=Cursors.Hand,Height=40,UseVisualStyleBackColor=false };
            button.FlatAppearance.BorderSize=primary?0:1; button.FlatAppearance.BorderColor=border;
            button.FlatAppearance.MouseOverBackColor=primary?(dark?Color.FromArgb(245,182,132):Color.FromArgb(32,80,191)):soft;
            return button;
        }
        sealed class RaisedButton : Button
        {
            readonly bool dark, primary;
            readonly Color border;
            bool hover, pressed;
            public RaisedButton(bool dark,Color border,bool primary)
            { this.dark=dark; this.border=border; this.primary=primary; DoubleBuffered=true; ResizeRedraw=true; }
            protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover=true; Invalidate(); }
            protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover=false; Invalidate(); }
            protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); pressed=e.Button==MouseButtons.Left; Invalidate(); }
            protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); pressed=false; Invalidate(); }
            protected override void OnMouseCaptureChanged(EventArgs e) { base.OnMouseCaptureChanged(e); if(!Capture)pressed=false; Invalidate(); }
            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.Clear(Parent==null?BackColor:Parent.BackColor);
                e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                var face=new Rectangle(1,pressed?3:1,Math.Max(1,Width-3),Math.Max(1,Height-5));
                Color fill=!Enabled?(dark?Color.FromArgb(40,43,49):Color.FromArgb(239,243,248)):
                    hover?FlatAppearance.MouseOverBackColor:BackColor;
                if(Enabled && !pressed)
                    using(var shadow=Rounded(new Rectangle(1,4,face.Width,face.Height),7))
                    using(var brush=new SolidBrush(Color.FromArgb(dark?70:28,Color.Black)))e.Graphics.FillPath(brush,shadow);
                using(var path=Rounded(face,7))
                {
                    using(var brush=new System.Drawing.Drawing2D.LinearGradientBrush(face,ControlPaint.Light(fill,.06f),fill,90f)) e.Graphics.FillPath(brush,path);
                    using(var pen=new Pen(primary && Enabled?fill:border))e.Graphics.DrawPath(pen,path);
                }
                TextRenderer.DrawText(e.Graphics,Text,Font,face,Enabled?ForeColor:(dark?Color.FromArgb(132,141,155):Color.FromArgb(137,149,165)),
                    TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
                if(Focused && ShowFocusCues) {var focus=face;focus.Inflate(-4,-4);ControlPaint.DrawFocusRectangle(e.Graphics,focus,ForeColor,fill);}
            }
        }
        PHU_DimensionCheck.Finding Current { get { return findingIndex<0 || findingIndex>=findingItems.Count?null:findingItems[findingIndex]; } }
        protected override void Dispose(bool disposing)
        {
            if(disposing) {if(batchTimer!=null)batchTimer.Dispose();if(batchFilterMenu!=null)batchFilterMenu.Dispose();if(revisionFilterMenu!=null)revisionFilterMenu.Dispose();tips.Dispose();}
            base.Dispose(disposing);
        }
        void UpdateActions()
        {
            revisionButton.Enabled=!checking;
            previous.Enabled=!checking&&findingIndex>0;
            next.Enabled=!checking&&findingIndex>=0&&findingIndex<findingItems.Count-1;
            position.Text=findingIndex<0?"":(findingIndex+1)+" / "+findingItems.Count;
            for(int i=0;i<findingCards.Controls.Count;i++)
            {var card=findingCards.Controls[i];card.BackColor=i==findingIndex?soft:surface;card.Invalidate(true);}
        }
        void SelectDimension()
        {
            if(checking)return;
            try
            {
                var f=Current;if(f==null)return;
                selectFinding(result,f);
            }
            catch(Exception ex) { MessageBox.Show(this,ex.Message,"Chọn kích thước",MessageBoxButtons.OK,MessageBoxIcon.Information); }
        }
        static void SelectInTekla(PHU_DimensionCheck.Result snapshot,PHU_DimensionCheck.Finding finding)
        {
            var handler=new D.DrawingHandler(); var active=handler.GetActiveDrawing();
            if(active==null || !active.IsSameDatabaseObject(snapshot.SourceDrawing)) throw new InvalidOperationException("Bản vẽ đã thay đổi. Hãy chạy kiểm tra lại.");
            D.DrawingObject obj;
            if(!snapshot.Objects.TryGetValue(finding.Dimension,out obj) || !obj.Select()) throw new InvalidOperationException("Kích thước đã thay đổi. Hãy kiểm tra lại.");
            if(!handler.GetDrawingObjectSelector().SelectObjects(new ArrayList {obj},false))throw new InvalidOperationException("Chưa chọn được DIM. Hãy kiểm tra lại bản vẽ đang mở.");
        }
    }
}
#endregion
