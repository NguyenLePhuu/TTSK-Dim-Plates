using System;
using System.Collections;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using D = Tekla.Structures.Drawing;

namespace Tekla.Technology.Akit.UserScript
{
    // Presentation only; the classifier and its results are unchanged.
    public sealed class PHU_DimensionReportForm : Form
    {
        PHU_DimensionCheck.Result result;
        readonly bool dark;
        readonly Color surface, ink, muted, border, accent, soft;
        DataGridView table;
        Label detailTitle, location, reason, feedback;
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
        Label listCount, emptyList, detailBadge, position, explanationHeading;
        Control listScroll;
        Button previous, next, recheckButton;
        bool populating;
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
            var root=Stack(116,74,incomplete?40:0,0,48);
            // A zero-height notice must not consume a percentage row.
            root.RowStyles[2]=new RowStyle(SizeType.Absolute,incomplete?40:0);
            var content=new Panel { Dock=DockStyle.Fill,Padding=new Padding(22,12,22,10),BackColor=BackColor };
            content.Controls.Add(root); Controls.Add(content);
            BuildChrome();

            var hero=Card(); hero.Padding=new Padding(20,8,20,8); hero.Margin=new Padding(0,0,0,8);
            var heroGrid=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty };
            heroGrid.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            heroGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            heroGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,220));
            var title=Stack(22,36,0);
            title.Controls.Add(TextLabel("TTSK   /   CHECK DIM",9,muted,true),0,0);
            title.Controls.Add(TextLabel("Kiểm tra chân kích thước",22,ink,true),0,1);
            title.Controls.Add(TextLabel(result.Drawing,11,muted),0,2);
            heroGrid.Controls.Add(title,0,0);
            var outcome=Stack(34,0);
            var badge=TextLabel(errors>0?"●  "+errors+" chân cần xem":incomplete?"●  Cần xác minh":"✓  Không phát hiện lỗi",
                12,errors>0||incomplete?Amber:Green,true);
            badge.TextAlign=ContentAlignment.MiddleRight;
            outcome.Controls.Add(badge,0,0);
            var timestamp=TextLabel("Lần kiểm tra  "+checkedAt.ToString("HH:mm:ss")+"\r\nKết quả tại thời điểm kiểm tra",9,muted);
            timestamp.TextAlign=ContentAlignment.MiddleRight;
            outcome.Controls.Add(timestamp,0,1); heroGrid.Controls.Add(outcome,1,0);
            hero.Controls.Add(heroGrid); root.Controls.Add(hero,0,0);

            var metrics=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=4,RowCount=1,Margin=Padding.Empty };
            metrics.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            for(int i=0;i<4;i++) metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,25));
            string[] numbers={errors.ToString(),reviews.ToString(),result.Dimensions.ToString(),result.Views.ToString()};
            string[] captions={"CHÂN CẦN XEM","CHÂN CHƯA KẾT LUẬN","CHUỖI ĐÃ KIỂM TRA","HÌNH CHIẾU"};
            for(int i=0;i<4;i++)
            {
                var card=Card(); card.Margin=new Padding(i==0?0:4,0,i==3?0:4,6); card.Padding=new Padding(14,4,10,4);
                var metric=Stack(29,0);
                metric.Controls.Add(TextLabel(numbers[i],21,i==0?Amber:i==1?accent:ink,true),0,0);
                metric.Controls.Add(TextLabel(captions[i],8,muted,true),0,1);
                card.Controls.Add(metric); metrics.Controls.Add(card,i,0);
            }
            root.Controls.Add(metrics,0,1);
            if(incomplete)
            {
                var notice=new Panel { Dock=DockStyle.Fill,BackColor=AmberSoft,Margin=new Padding(0,0,0,8),Padding=new Padding(10,0,6,0) };
                var explain=Button("Xem phạm vi",false); explain.Dock=DockStyle.Right; explain.Width=120;
                explain.Click+=(s,e)=>ShowCoverage();
                var label=TextLabel("!  Có chuỗi hoặc dữ liệu cần xác minh. Kết quả chưa bao phủ toàn bộ bản vẽ.",9,Amber);
                notice.Controls.Add(label); notice.Controls.Add(explain); root.Controls.Add(notice,0,2);
            }

            var body=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty };
            body.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,51)); body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,49));
            root.Controls.Add(body,0,3);
            var list=Card(); list.Margin=new Padding(0,0,7,0); list.Padding=new Padding(14,10,10,10);
            var listStack=Stack(32,30,0);
            listStack.Controls.Add(TextLabel("Vị trí cần kiểm tra",16,ink,true),0,0);
            listCount=TextLabel("",9,muted); listStack.Controls.Add(listCount,0,1);
            table=new ReportGrid { Dock=DockStyle.Fill,BackgroundColor=surface,BorderStyle=BorderStyle.None,
                ReadOnly=true,AllowUserToAddRows=false,AllowUserToDeleteRows=false,AllowUserToResizeRows=false,
                RowHeadersVisible=false,MultiSelect=false,SelectionMode=DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,AutoSizeRowsMode=DataGridViewAutoSizeRowsMode.AllCells,
                EnableHeadersVisualStyles=false,CellBorderStyle=DataGridViewCellBorderStyle.SingleHorizontal,
                ColumnHeadersBorderStyle=DataGridViewHeaderBorderStyle.None,GridColor=border,ColumnHeadersHeight=34,
                AccessibleName="Danh sách chân kích thước cần kiểm tra" };
            table.DefaultCellStyle=new DataGridViewCellStyle { BackColor=surface,ForeColor=ink,SelectionBackColor=soft,
                SelectionForeColor=ink,Padding=new Padding(9,13,7,13),WrapMode=DataGridViewTriState.True,Font=new Font(Font.FontFamily,10) };
            table.ColumnHeadersDefaultCellStyle=new DataGridViewCellStyle { BackColor=surface,ForeColor=muted,
                SelectionBackColor=surface,SelectionForeColor=muted,Padding=new Padding(7,0,0,0),Font=new Font(Font.FontFamily,9) };
            table.Columns.Add("view","Hình chiếu"); table.Columns.Add("value","Kích thước"); table.Columns.Add("location","Vị trí / trạng thái");
            table.Columns[0].FillWeight=78; table.Columns[1].FillWeight=100; table.Columns[2].FillWeight=122;
            table.Columns[1].DefaultCellStyle.Font=new Font(Font.FontFamily,10,FontStyle.Bold);
            foreach(DataGridViewColumn column in table.Columns) column.SortMode=DataGridViewColumnSortMode.NotSortable;
            table.CellPainting+=(s,e)=>{
                if(e.RowIndex<0 || e.ColumnIndex!=0 || !table.Rows[e.RowIndex].Selected)return;
                e.Paint(e.ClipBounds,DataGridViewPaintParts.All);
                using(var brush=new SolidBrush(accent)) e.Graphics.FillRectangle(brush,e.CellBounds.Left,e.CellBounds.Top,3,e.CellBounds.Height);
                e.Handled=true;
            };
            table.CellClick+=(s,e)=>{if(e.RowIndex>=0)SelectDimension();};
            table.KeyUp+=(s,e)=>{
                if(e.Modifiers==Keys.None && (e.KeyCode==Keys.Up || e.KeyCode==Keys.Down || e.KeyCode==Keys.PageUp || e.KeyCode==Keys.PageDown || e.KeyCode==Keys.Home || e.KeyCode==Keys.End)) SelectDimension();
            };
            var listBody=new Panel { Dock=DockStyle.Fill,BackColor=surface,Margin=Padding.Empty };
            var scroll=new ReportScroll(table,dark,accent) { Dock=DockStyle.Right,Width=16,BackColor=surface };
            listScroll=scroll;
            listBody.Controls.Add(table); listBody.Controls.Add(scroll);
            emptyList=TextLabel("",12,muted); emptyList.TextAlign=ContentAlignment.MiddleCenter; emptyList.BackColor=surface;
            listBody.Controls.Add(emptyList); emptyList.BringToFront();
            listStack.Controls.Add(listBody,0,2);
            list.Controls.Add(listStack); body.Controls.Add(list,0,0);

            var detail=Card(); detail.Padding=new Padding(18,12,18,12); detail.Margin=new Padding(7,0,0,0);
            var stack=Stack(26,56,60,0,40,26);
            var detailHeader=new Panel { Dock=DockStyle.Fill,Margin=Padding.Empty };
            detailBadge=TextLabel("",9,Amber,true);
            position=TextLabel("",9,muted); position.Dock=DockStyle.Right; position.Width=95; position.TextAlign=ContentAlignment.MiddleRight;
            detailHeader.Controls.Add(detailBadge); detailHeader.Controls.Add(position); stack.Controls.Add(detailHeader,0,0);
            detailTitle=new DimensionTitle { Dock=DockStyle.Fill,ForeColor=ink,Font=new Font(Font.FontFamily,23,FontStyle.Bold),Margin=Padding.Empty };
            stack.Controls.Add(detailTitle,0,1);
            detailTitle.TextChanged+=(s,e)=>tips.SetToolTip(detailTitle,detailTitle.Text);
            location=TextLabel("",11,ink); location.BackColor=soft; location.Padding=new Padding(12,6,10,6);
            location.Margin=new Padding(0,0,0,8); stack.Controls.Add(location,0,2);
            var explanation=Stack(23,0);
            explanationHeading=TextLabel("VÌ SAO CẦN XEM?",8,muted,true);
            explanation.Controls.Add(explanationHeading,0,0);
            reason=new Label { Dock=DockStyle.Top,AutoSize=true,ForeColor=ink,Font=new Font(Font.FontFamily,10),
                Padding=new Padding(0,0,8,0),Margin=Padding.Empty };
            var reasonHost=new Panel { Dock=DockStyle.Fill,AutoScroll=true,Margin=Padding.Empty };
            reasonHost.SizeChanged+=(s,e)=>reason.MaximumSize=new Size(Math.Max(80,reasonHost.ClientSize.Width-22),0);
            reasonHost.Controls.Add(reason); explanation.Controls.Add(reasonHost,0,1); stack.Controls.Add(explanation,0,3);
            var navigation=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=new Padding(0,3,0,4) };
            navigation.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            navigation.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50)); navigation.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
            previous=Button("←  Vị trí trước",false); previous.Dock=DockStyle.Fill; previous.Click+=(s,e)=>MoveFinding(-1);
            next=Button("Vị trí tiếp theo  →",false); next.Dock=DockStyle.Fill; next.Click+=(s,e)=>MoveFinding(1);
            navigation.Controls.Add(previous,0,0); navigation.Controls.Add(next,1,0); stack.Controls.Add(navigation,0,4);
            feedback=TextLabel("",9,muted); stack.Controls.Add(feedback,0,5);
            detail.Controls.Add(stack); body.Controls.Add(detail,1,0);
            var footer=new TableLayoutPanel { Dock=DockStyle.Fill,Padding=new Padding(0,7,0,0),ColumnCount=3,RowCount=1,Margin=Padding.Empty };
            footer.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,190)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,82));
            recheckButton=Button("↻  Kiểm tra lại  ·  F5",false); recheckButton.Name="Recheck"; recheckButton.Dock=DockStyle.Fill;
            recheckButton.AccessibleName="Kiểm tra lại bản vẽ đang mở"; recheckButton.Click+=(s,e)=>Recheck(recheckButton);
            footer.Controls.Add(recheckButton,1,0);
            var close=Button("Đóng",false); close.Dock=DockStyle.Fill; close.DialogResult=DialogResult.Cancel;
            footer.Controls.Add(close,2,0); CancelButton=close;
            footer.Controls.Add(TextLabel("Enter  chọn DIM   ·   Alt+↑/↓  chuyển vị trí   ·   Chỉ đọc bản vẽ",8,muted),0,0);
            root.Controls.Add(footer,0,4);
            table.CurrentCellChanged+=(s,e)=>{if(!populating)UpdateDetail();};
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
            var selected=Current;
            var findings=result.Findings.Where(IsActionable).OrderBy(f=>f.Status=="ERROR"?0:1).ToList();
            populating=true;
            try
            {
                table.Rows.Clear();
                foreach(var f in findings)
                {
                    int index=table.Rows.Add(f.ViewLabel,f.DimensionLabel,f.Location+"\r\n"+(f.Status=="ERROR"?"● Cần xem":"○ Chưa kết luận"));
                    table.Rows[index].Tag=f;
                }
                if(table.Rows.Count>0)
                {
                    int index=selected==null?0:Math.Max(0,findings.IndexOf(selected));
                    table.CurrentCell=table.Rows[index].Cells[0]; table.Rows[index].Selected=true;
                }
            }
            finally {populating=false;}
            int total=result.Findings.Count(IsActionable);
            listCount.Text=total+" vị trí cần đối chiếu";
            emptyList.Visible=findings.Count==0;
            table.Visible=listScroll.Visible=findings.Count>0;
            emptyList.Text=
                result.Warnings.Count>0?"Chưa ghi nhận vị trí cần xem.\r\nMột số dữ liệu chưa kiểm tra được.":"✓\r\nKhông có vị trí cần xử lý";
            UpdateDetail();
        }
        void MoveFinding(int offset)
        {
            if(checking || table.RowCount==0)return;
            int index=Math.Max(0,Math.Min(table.RowCount-1,(table.CurrentRow==null?0:table.CurrentRow.Index)+offset));
            table.CurrentCell=table.Rows[index].Cells[0];
            table.Rows[index].Selected=true;
            if(!table.Rows[index].Displayed)table.FirstDisplayedScrollingRowIndex=index;
            SelectDimension();
        }
        void ShowCoverage()
        {
            using(var dialog=new Form { Text="Phạm vi kiểm tra",StartPosition=FormStartPosition.CenterParent,
                Size=new Size(660,440),MinimumSize=new Size(440,280),BackColor=surface,ForeColor=ink,Font=Font,ShowIcon=false })
            {
                var info=new TextBox { Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,Dock=DockStyle.Fill,
                    BackColor=surface,ForeColor=ink,BorderStyle=BorderStyle.None,
                    Text="DỮ LIỆU CHƯA KIỂM TRA ĐƯỢC\r\n\r\n"+
                    (result.Warnings.Count>0?string.Join("\r\n\r\n",result.Warnings):"Không có cảnh báo đọc dữ liệu.")+
                    "\r\n\r\nCHÂN CHƯA KẾT LUẬN\r\n\r\n"+
                    string.Join("\r\n\r\n",result.Findings.Where(f=>f.Status=="REVIEW").Select(f=>f.ViewLabel+" · "+f.DimensionLabel+"\r\n"+f.Location+"\r\n"+f.Reason)) };
                dialog.Padding=new Padding(20); dialog.Controls.Add(info); dialog.ShowDialog(this);
            }
        }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if(keyData==Keys.F5){if(!checking)Recheck(recheckButton);return true;}

            if(keyData==(Keys.Alt|Keys.Down)){MoveFinding(1);return true;}
            if(keyData==(Keys.Alt|Keys.Up)){MoveFinding(-1);return true;}
            if(keyData==Keys.Enter && table.ContainsFocus){SelectDimension();return true;}
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
                        feedback.Text="Đã kiểm tra lại lúc "+DateTime.Now.ToString("HH:mm:ss");
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
        sealed class ReportGrid : DataGridView
        {
            public ReportGrid() { DoubleBuffered=true; ScrollBars=ScrollBars.None; }
            protected override void OnMouseWheel(MouseEventArgs e)
            {
                if(RowCount==0) return;
                int max=Math.Max(0,RowCount-Math.Max(1,DisplayedRowCount(false)));
                FirstDisplayedScrollingRowIndex=Math.Max(0,Math.Min(max,Math.Max(0,FirstDisplayedScrollingRowIndex)+(e.Delta<0?3:-3)));
            }
        }
        sealed class ReportScroll : Control
        {
            readonly DataGridView grid; readonly Color thumbColor,hoverColor;
            bool dragging,hover; int offset;
            public ReportScroll(DataGridView grid,bool dark,Color accent)
            {
                this.grid=grid; thumbColor=dark?Color.FromArgb(95,82,70):Color.FromArgb(148,163,184); hoverColor=accent;
                DoubleBuffered=true; Cursor=Cursors.Hand; AccessibleName="Cuộn danh sách lỗi";
                grid.Scroll+=(s,e)=>Invalidate(); grid.SizeChanged+=(s,e)=>Invalidate(); grid.RowsAdded+=(s,e)=>Invalidate();
                grid.RowHeightChanged+=(s,e)=>Invalidate();
            }
            int Maximum { get { return Math.Max(0,grid.RowCount-Math.Max(1,grid.DisplayedRowCount(false))); } }
            Rectangle Thumb
            {
                get
                {
                    int top=grid.ColumnHeadersHeight+5,space=Math.Max(1,Height-top-5);
                    int h=Math.Min(space,Math.Max(30,space*Math.Max(1,grid.DisplayedRowCount(false))/Math.Max(1,grid.RowCount)));
                    int y=top+(space-h)*Math.Max(0,grid.FirstDisplayedScrollingRowIndex)/Math.Max(1,Maximum);
                    return new Rectangle(5,y,Math.Max(4,Width-10),h);
                }
            }
            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e); if(Maximum==0) return;
                var r=Thumb; e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using(var path=new System.Drawing.Drawing2D.GraphicsPath())
                using(var brush=new SolidBrush(hover||dragging?hoverColor:thumbColor))
                { int d=r.Width; path.AddArc(r.X,r.Y,d,d,180,180); path.AddArc(r.X,r.Bottom-d,d,d,0,180); path.CloseFigure(); e.Graphics.FillPath(brush,path); }
            }
            void MoveThumb(int y)
            {
                if(Maximum==0) return;
                int top=grid.ColumnHeadersHeight+5,travel=Math.Max(1,Height-top-5-Thumb.Height);
                grid.FirstDisplayedScrollingRowIndex=Math.Max(0,Math.Min(Maximum,(int)Math.Round((y-offset-top)*(double)Maximum/travel)));
                Invalidate();
            }
            protected override void OnMouseDown(MouseEventArgs e)
            { base.OnMouseDown(e); if(e.Button!=MouseButtons.Left||Maximum==0)return; offset=Thumb.Contains(e.Location)?e.Y-Thumb.Y:Thumb.Height/2; dragging=true; Capture=true; MoveThumb(e.Y); }
            protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if(dragging) MoveThumb(e.Y); }
            protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); dragging=false; Capture=false; Invalidate(); }
            protected override void OnMouseCaptureChanged(EventArgs e) { base.OnMouseCaptureChanged(e); if(!Capture) dragging=false; Invalidate(); }
            protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover=true; Invalidate(); }
            protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover=false; Invalidate(); }
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
        PHU_DimensionCheck.Finding Current { get { return table==null || table.CurrentRow==null?null:table.CurrentRow.Tag as PHU_DimensionCheck.Finding; } }
        void UpdateDetail()
        {
            var f=Current;
            feedback.ForeColor=muted;
            UpdateActions();
            position.Text=f==null?"":(table.CurrentRow.Index+1)+" / "+table.RowCount;
            if(f==null)
            {
                explanationHeading.Text="VỀ KẾT QUẢ KIỂM TRA";
                bool incomplete=result.Warnings.Count>0 || result.Findings.Any(x=>x.Status=="REVIEW");
                detailBadge.Text=incomplete?"○  KIỂM TRA CHƯA ĐẦY ĐỦ":"✓  ĐÃ KIỂM TRA XONG";
                detailBadge.ForeColor=incomplete?Amber:Green;
                detailTitle.Text=incomplete?"Chưa thể kết luận":"Không phát hiện lỗi";
                location.Text=result.Dimensions+" chuỗi kích thước · "+result.Views+" hình chiếu\r\n"+result.Findings.Count+" chân kích thước đã được đọc";
                reason.Text=incomplete?"Một số dữ liệu chưa đọc được hoặc chưa được hỗ trợ. Mở ‘Xem phạm vi’ để biết phần cần kiểm tra thủ công.":
                    "Không ghi nhận chân kích thước có dấu hiệu bắt sai trong phạm vi đã kiểm tra.\r\n\r\nSau khi chỉnh sửa bản vẽ, nhấn F5 để cập nhật kết quả.";
                feedback.Text="Kết quả áp dụng tại thời điểm kiểm tra.";
                return;
            }
            bool review=f.Status=="REVIEW";
            explanationHeading.Text=review?"VÌ SAO CHƯA KẾT LUẬN?":"VÌ SAO CẦN XEM?";
            detailBadge.Text=review?"○  CHƯA KẾT LUẬN":"●  CHÂN CẦN XEM";
            detailBadge.ForeColor=review?accent:Amber;
            detailTitle.Text=f.DimensionLabel;
            location.Text=f.ViewLabel+"\r\n"+f.Location;
            reason.Text=PHU_DimensionCheck.UserReason(f)+"\r\n\r\n"+
                "Chọn DIM để đối chiếu chân với mốc đo dự kiến.\r\nSau khi sửa trong Tekla, nhấn F5 để kiểm tra lại.";
            feedback.Text="Bấm một dòng để đánh dấu cả chuỗi trong Tekla.";
        }
        sealed class DimensionTitle : Label
        {
            public DimensionTitle() { DoubleBuffered=true; ResizeRedraw=true; }
            protected override void OnPaint(PaintEventArgs e)
            {
                for(float size=23;size>=13;size--)
                    using(var candidate=new Font(Font.FontFamily,size,FontStyle.Bold))
                    {
                        if(size>13 && TextRenderer.MeasureText(e.Graphics,Text,candidate).Width>ClientSize.Width-6)continue;
                        TextRenderer.DrawText(e.Graphics,Text,candidate,ClientRectangle,ForeColor,
                            TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
                        break;
                    }
            }
        }
        protected override void Dispose(bool disposing)
        {
            if(disposing) tips.Dispose();
            base.Dispose(disposing);
        }
        void UpdateActions()
        {
            previous.Enabled=!checking && table.CurrentRow!=null && table.CurrentRow.Index>0;
            next.Enabled=!checking && table.CurrentRow!=null && table.CurrentRow.Index<table.RowCount-1;
        }
        void SelectDimension()
        {
            if(checking)return;
            try
            {
                var f=Current; if(f==null)return;
                selectFinding(result,f);
                feedback.ForeColor=Green;
                feedback.Text="✓ Đã chọn DIM trên bản vẽ · vị trí "+(table.CurrentRow.Index+1)+" / "+table.RowCount;
            }
            catch(Exception ex) { feedback.Text=ex.Message; MessageBox.Show(this,ex.Message,"Chọn kích thước",MessageBoxButtons.OK,MessageBoxIcon.Information); }
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
