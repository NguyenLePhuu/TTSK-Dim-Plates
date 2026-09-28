using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using D = Tekla.Structures.Drawing;

namespace Tekla.Technology.Akit.UserScript
{
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
