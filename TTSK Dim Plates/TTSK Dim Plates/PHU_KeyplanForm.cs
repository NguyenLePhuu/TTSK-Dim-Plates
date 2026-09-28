using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TTSK_AutoDim_Plates.Keyplan
{
    internal sealed class KeyplanForm : Form
    {
        readonly Func<KeyplanRunRequest,Task<KeyplanRunResult>> run;
        readonly Action<bool> saveAutoApply;
        readonly SlideToggle applyToggle;
        readonly Palette palette;
        readonly Label drawing, status;
        readonly CoordinateBadge calibration;
        readonly ReferenceCard referenceFirst, referenceSecond;
        readonly TableLayoutPanel referencesPanel, stack;
        readonly AngleCard[] cards;
        readonly TableLayoutPanel values;
        readonly OutcomeBadge outcome;
        readonly PaintedButton beam, column, x, y, auto, autoModeButton, refresh, copy, settings, gridButton, rotate;
        readonly Panel options;
        readonly ToolTip tips=new ToolTip();
        Snapshot snapshot;
        Angles result;
        QuickState? displayedState;
        bool busy, isColumn, alongY, savedAutoApply, restoringPreference;
        bool autoDirection=true;
        bool autoMode=true;
        int gridId, quarterTurns;
        string identity;
        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h,int msg,IntPtr w,IntPtr l);

        internal KeyplanForm(Func<KeyplanRunRequest,Task<KeyplanRunResult>> run,bool dark,bool autoApply=false,Action<bool> saveAutoApply=null)
        {
            this.run=run;this.saveAutoApply=saveAutoApply;savedAutoApply=autoApply;palette=new Palette(dark);
            Text="TTSK · Keyplan";Name="KeyplanForm";ClientSize=new Size(660,550);
            FormBorderStyle=FormBorderStyle.None;StartPosition=FormStartPosition.CenterParent;
            ShowInTaskbar=false;AutoScaleMode=AutoScaleMode.Dpi;AutoScaleDimensions=new SizeF(96,96);
            BackColor=palette.Border;ForeColor=palette.Ink;Padding=new Padding(1);
            Font=new Font("Segoe UI",10);DoubleBuffered=true;KeyPreview=true;

            var chrome=new Panel { Dock=DockStyle.Top,Height=38,BackColor=palette.Bar };
            chrome.Paint+=(s,e)=> { using(var pen=new Pen(palette.Border))e.Graphics.DrawLine(pen,0,chrome.Height-1,chrome.Width,chrome.Height-1); };
            var caption=TextLabel("TTSK    /    SLOT 09",9,palette.BarInk);caption.Padding=new Padding(20,0,0,0);
            MouseEventHandler drag=(s,e)=> { if(e.Button==MouseButtons.Left) { ReleaseCapture();SendMessage(Handle,0xA1,new IntPtr(2),IntPtr.Zero); } };
            caption.MouseDown+=drag;chrome.MouseDown+=drag;
            var close=Button("×");close.AccessibleName="Đóng";close.Dock=DockStyle.Right;close.Width=42;close.Chrome=true;
            close.Click+=delegate { Close(); };chrome.Controls.Add(caption);chrome.Controls.Add(close);

            var footer=new Panel { Dock=DockStyle.Bottom,Height=60,BackColor=palette.Bar,Padding=new Padding(18,12,18,12) };
            footer.Paint+=(s,e)=> { using(var pen=new Pen(palette.Border))e.Graphics.DrawLine(pen,0,0,footer.Width,0); };
            settings=Button("Hiệu chỉnh");settings.Name="Settings";settings.Chrome=true;settings.Dock=DockStyle.Left;settings.Width=116;
            copy=Button("Sao chép");copy.Name="Copy";copy.Chrome=true;copy.Dock=DockStyle.Right;copy.Width=104;copy.Enabled=false;
            refresh=Button("Kiểm tra lại");refresh.Name="Refresh";refresh.Selected=true;refresh.Primary=true;refresh.Dock=DockStyle.Right;refresh.Width=132;
            var gap=new Panel { Dock=DockStyle.Right,Width=10 };
            footer.Controls.Add(settings);footer.Controls.Add(copy);footer.Controls.Add(gap);footer.Controls.Add(refresh);

            var body=new Panel { Dock=DockStyle.Fill,BackColor=palette.Background,Padding=new Padding(24,18,24,16) };
            stack=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=1,RowCount=7,Margin=Padding.Empty };
            stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            foreach(int height in new[]{32,48,44,34,154,90,16})stack.RowStyles.Add(new RowStyle(SizeType.Absolute,height));
            var heading=new Panel { Dock=DockStyle.Fill,Margin=Padding.Empty };
            heading.Controls.Add(TextLabel("KEYPLAN",16,palette.Accent,true));
            drawing=TextLabel("Đang đọc bản vẽ…",10,palette.Muted);drawing.Dock=DockStyle.Right;drawing.Width=250;drawing.TextAlign=ContentAlignment.MiddleRight;
            heading.Controls.Add(drawing);stack.Controls.Add(heading,0,0);
            var choices=new FlowLayoutPanel { Dock=DockStyle.Fill,WrapContents=false,Padding=new Padding(0,7,0,7),Margin=Padding.Empty };
            beam=Button("Dầm");column=Button("Cột");x=Button("X");y=Button("Y");
            autoModeButton=Button("Tự nhận");autoModeButton.Name="ModeAuto";autoModeButton.Size=new Size(88,36);autoModeButton.Margin=new Padding(0,0,5,0);
            auto=Button("Tự động");auto.Name="DirectionAuto";auto.Size=new Size(88,36);auto.Margin=new Padding(0,0,5,0);
            beam.Name="Beam";column.Name="Column";x.Name="DirectionX";y.Name="DirectionY";
            foreach(var b in new[]{beam,column,x,y}) { b.Size=new Size(76,36);b.Margin=new Padding(0,0,5,0); }
            var separator=TextLabel("Hướng",9,palette.Muted);separator.Dock=DockStyle.None;separator.Size=new Size(84,36);separator.TextAlign=ContentAlignment.MiddleLeft;
            var modes=new FlowLayoutPanel { Dock=DockStyle.Fill,WrapContents=false,Padding=new Padding(0,7,0,7),Margin=Padding.Empty };
            var modeLabel=TextLabel("Phương án",9,palette.Muted);modeLabel.Dock=DockStyle.None;modeLabel.Size=new Size(84,36);modeLabel.TextAlign=ContentAlignment.MiddleLeft;
            modes.Controls.AddRange(new Control[]{modeLabel,autoModeButton,beam,column});
            applyToggle=new SlideToggle(palette) {Name="AutoApply",Text="Tự áp dụng",Size=new Size(216,36),Margin=new Padding(24,0,0,0),Checked=autoApply};
            modes.Controls.Add(applyToggle);
            tips.SetToolTip(applyToggle,"Ghi nhớ bật/tắt cho những lần mở Slot 09 sau, kể cả khi đóng chương trình. Khi bật, tự áp dụng Local và X/Y/Z, tự xác nhận Yes nếu Tekla cần tái tạo bản vẽ.");
            var angleHeading=new Panel {Dock=DockStyle.Fill,Margin=new Padding(0,4,0,4)};
            angleHeading.Controls.Add(TextLabel("GÓC XOAY",9,palette.Muted,true));
            calibration=new CoordinateBadge(palette) {Dock=DockStyle.Right,Width=250,Name="CoordinateBadge",Text="Đang đọc…"};
            angleHeading.Controls.Add(calibration);stack.Controls.Add(angleHeading,0,3);
            tips.SetToolTip(calibration,"Drawing properties → View creation → Attributes → Coordinate system.");
            choices.Controls.AddRange(new Control[]{separator,auto,x,y});
            stack.Controls.Add(modes,0,1);stack.Controls.Add(choices,0,2);
            values=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=3,RowCount=1,Margin=Padding.Empty,Visible=false };
            cards=new[]{new AngleCard("X",palette),new AngleCard("Y",palette),new AngleCard("Z",palette)};
            for(int i=0;i<3;i++) { values.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100F/3));cards[i].Dock=DockStyle.Fill;cards[i].Margin=new Padding(i==0?0:5,0,i==2?0:5,0);values.Controls.Add(cards[i],i,0); }
            var resultHost=new Panel { Dock=DockStyle.Fill,Margin=Padding.Empty,BackColor=palette.Background };
            outcome=new OutcomeBadge(palette) { Dock=DockStyle.Fill,Name="Outcome",Text="Đang kiểm tra…" };
            resultHost.Controls.Add(values);resultHost.Controls.Add(outcome);stack.Controls.Add(resultHost,0,4);
            referencesPanel=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=new Padding(0,14,0,0),Visible=false};
            referencesPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));referencesPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
            referenceFirst=new ReferenceCard(palette) {Dock=DockStyle.Fill,Margin=new Padding(0,0,5,0),Name="ReferenceFirst"};
            referenceSecond=new ReferenceCard(palette) {Dock=DockStyle.Fill,Margin=new Padding(5,0,0,0),Name="ReferenceSecond"};
            referencesPanel.Controls.Add(referenceFirst,0,0);referencesPanel.Controls.Add(referenceSecond,1,0);stack.Controls.Add(referencesPanel,0,5);
            status=TextLabel("",9,palette.Amber);status.Name="ResultStatus";status.Padding=new Padding(0,4,0,0);status.TextAlign=ContentAlignment.MiddleCenter;stack.Controls.Add(status,0,6);body.Controls.Add(stack);

            options=new Panel { Dock=DockStyle.Bottom,Height=92,Visible=false,BackColor=palette.Surface,Padding=new Padding(24,10,24,10) };
            var rows=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=1,RowCount=2 };
            foreach(int h in new[]{28,40})rows.RowStyles.Add(new RowStyle(SizeType.Absolute,h));
            rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            rows.Controls.Add(TextLabel("Chuẩn keyplan · chiều trục hiển thị dưới kết quả",9,palette.Muted),0,0);
            var references=new FlowLayoutPanel { Dock=DockStyle.Fill,WrapContents=false,Margin=Padding.Empty };
            gridButton=Button("Lưới: tự động");gridButton.Size=new Size(292,32);
            rotate=Button("Xoay chuẩn keyplan: 0°");rotate.Size=new Size(270,32);
            references.Controls.AddRange(new Control[]{gridButton,rotate});rows.Controls.Add(references,0,1);options.Controls.Add(rows);
            Controls.Add(body);Controls.Add(options);Controls.Add(footer);Controls.Add(chrome);

            beam.Click+=async delegate { autoMode=false;isColumn=false;await CheckNow(); };column.Click+=async delegate { autoMode=false;isColumn=true;await CheckNow(); };
            autoModeButton.Click+=async delegate { autoMode=true;autoDirection=true;await CheckNow(); };
            x.Click+=async delegate { autoDirection=false;alongY=false;await CheckNow(); };y.Click+=async delegate { autoDirection=false;alongY=true;await CheckNow(); };
            auto.Click+=async delegate { autoDirection=true;await CheckNow(); };
            refresh.Click+=async delegate { await CheckNow(); };
            applyToggle.CheckedChanged+=async delegate {
                if(restoringPreference)return;
                try {if(saveAutoApply!=null)saveAutoApply(applyToggle.Checked);savedAutoApply=applyToggle.Checked;}
                catch(Exception ex) {restoringPreference=true;applyToggle.Checked=savedAutoApply;restoringPreference=false;SetStatus("Chưa lưu được Tự áp dụng: "+ex.Message);return;}
                if(applyToggle.Checked&&!busy)await CheckNow();
            };
            FormClosing+=(s,e)=> { if(busy)e.Cancel=true; };
            settings.Click+=delegate { bool show=!options.Visible;options.Visible=show;ClientSize=new Size(ClientSize.Width,ClientSize.Height+(show?options.Height:-options.Height));settings.Text=show?"Thu gọn":"Hiệu chỉnh"; };
            rotate.Click+=async delegate { quarterTurns=(quarterTurns+1)%4;rotate.Text="Xoay chuẩn keyplan: "+quarterTurns*90+"°";await CheckNow(); };
            gridButton.Click+=delegate
            {
                if(snapshot==null)return;
                var menu=new ContextMenuStrip();
                var automatic=new ToolStripMenuItem("Tự động chọn lưới") {Checked=gridId==0};
                automatic.Click+=async delegate { gridId=0;await CheckNow(); };menu.Items.Add(automatic);
                foreach(var candidate in snapshot.Grids.OrderBy(g=>g.Id))
                {
                    var selected=candidate;
                    var item=new ToolStripMenuItem(candidate.ToString()) {Checked=gridId==candidate.Id,Enabled=candidate.Error==null,ToolTipText=candidate.Error};
                    item.Click+=async delegate { gridId=selected.Id;await CheckNow(); };menu.Items.Add(item);
                }
                menu.Closed+=delegate { menu.Dispose(); };menu.Show(gridButton,new Point(0,gridButton.Height));
            };
            copy.Click+=delegate { if(result!=null)try { Clipboard.SetText(result.ToString());copy.Text="Đã chép ✓"; } catch(Exception) { SetStatus("Chưa sao chép được. Hãy thử lại."); } };
            Shown+=async delegate { await CheckNow(); };
            KeyDown+=async (s,e)=> { if(e.KeyCode==Keys.Escape)Close();else if(e.KeyCode==Keys.F5&&!busy) { e.Handled=true;await CheckNow(); } };
            UpdateModes();
        }
        Label TextLabel(string text,float size,Color color,bool bold=false)
        { return new Label { Text=text,Dock=DockStyle.Fill,AutoEllipsis=true,ForeColor=color,BackColor=Color.Transparent,TextAlign=ContentAlignment.MiddleLeft,Margin=Padding.Empty,Font=new Font("Segoe UI",size,bold?FontStyle.Bold:FontStyle.Regular) }; }
        PaintedButton Button(string text) { return new PaintedButton(palette) { Text=text,Font=new Font("Segoe UI",9F,FontStyle.Bold) }; }
        void UpdateModes()
        {
            beam.Selected=!isColumn&&(!autoMode||displayedState.HasValue);column.Selected=isColumn&&(!autoMode||displayedState.HasValue);
            autoModeButton.Selected=autoMode;autoModeButton.Enabled=!busy;
            x.Selected=!isColumn&&!alongY&&(!autoDirection||displayedState.HasValue);
            y.Selected=!isColumn&&alongY&&(!autoDirection||displayedState.HasValue);
            x.Enabled=y.Enabled=!isColumn&&!busy;
            auto.Enabled=!isColumn&&!busy;auto.Selected=!isColumn&&autoDirection;
            x.Text=isColumn?"Trái X":"X";y.Text=isColumn?"Phải Y":"Y";
            foreach(var b in new[]{beam,column,auto,autoModeButton,x,y})b.Invalidate();
        }
        void ClearResult()
        {
            result=null;displayedState=null;copy.Enabled=false;copy.Visible=false;values.Visible=false;
            copy.Text="Sao chép";referencesPanel.Visible=false;
            calibration.Text="Chưa xác minh";calibration.NeedsChange=false;
            outcome.Correct=false;outcome.Visible=true;outcome.Text="Đang kiểm tra…";foreach(var c in cards)c.Value="—";
        }
        async Task CheckNow()
        {
            if(busy)return;busy=true;ClearResult();SetStatus("");
            applyToggle.Enabled=false;
            foreach(var b in new[]{beam,column,refresh,gridButton,rotate})b.Enabled=false;UpdateModes();
            try
            {
                if(applyToggle.Checked)outcome.Text="Đang kiểm tra và áp dụng…";
                var checkedRun=await run(new KeyplanRunRequest {PreviousIdentity=identity,Column=autoMode?(bool?)null:isColumn,
                    AlongY=autoDirection?(bool?)null:alongY,GridId=gridId,QuarterTurns=quarterTurns,AutoApply=applyToggle.Checked,
                    ConfirmRecreate=ConfirmRecreation});
                if(IsDisposed)return;
                if(checkedRun==null||checkedRun.Snapshot==null||checkedRun.Result==null)throw new InvalidOperationException("Chưa nhận được kết quả kiểm tra từ Tekla.");
                var fresh=checkedRun.Snapshot;
                if(checkedRun.ResetOverrides) { gridId=quarterTurns=0;autoMode=autoDirection=true;rotate.Text="Xoay chuẩn keyplan: 0°"; }
                identity=fresh.Identity;
                var output=checkedRun.Result;
                Render(fresh,output);
                if(checkedRun.Note!=null) {SetStatus((checkedRun.Applied?"Đã áp dụng. ":"")+CompactNote(output),checkedRun.Note+"\n"+output.Message);if(output.State==QuickState.Correct)status.ForeColor=palette.Green;}
            }
            catch(Exception ex) { if(!IsDisposed) { ClearResult();outcome.Text="Chưa thể kết luận";SetStatus(ex.Message); } }
            finally
            {
                busy=false;if(!IsDisposed) { applyToggle.Enabled=true;foreach(var b in new[]{beam,column,refresh,gridButton,rotate})b.Enabled=true;UpdateModes(); }
            }
        }
        bool ConfirmRecreation()
        {
            if(IsDisposed)return false;
            if(InvokeRequired)return (bool)Invoke(new Func<bool>(ConfirmRecreation));
            // The user explicitly authorized automatic Yes for Slot 09.
            // Consent is scoped to the active auto-apply operation only.
            return busy&&applyToggle.Checked;
        }
        void Render(Snapshot fresh,QuickResult output)
        {
                snapshot=fresh;drawing.Text=fresh.Mark;tips.SetToolTip(drawing,fresh.Mark);
                calibration.Text=fresh.Settings==null||fresh.Settings.Error!=null?"Chưa xác minh":"Hệ tọa độ · "+fresh.Settings.CoordinateSystem;
                var grid=fresh.Grids.SingleOrDefault(g=>g.Id==gridId);gridButton.Text=grid==null?"Lưới: tự động":"Lưới: "+grid.Name+" · "+grid.Id;
                isColumn=output.IsColumn;
                if(!isColumn)alongY=output.AlongY;
                grid=fresh.Grids.Single(g=>g.Id==output.GridId);
                gridButton.Text=(gridId==0?"Tự động: ":"Lưới: ")+grid.Name+" · "+grid.Id;
                tips.SetToolTip(gridButton,output.SelectionNote+". Bấm để chọn lưới khác hoặc trở về tự động.");
                tips.SetToolTip(auto,output.SelectionNote);
                tips.SetToolTip(autoModeButton,output.SelectionNote);
                displayedState=output.State;
                // The UI never promotes a mathematical candidate to a verified recommendation.
                result=output.CanApply&&(output.State==QuickState.VerifiedSuggestion||output.State==QuickState.IncompleteViews)?output.Angles:null;
                string firstType=output.FirstViewType==null?"":" · "+output.FirstViewType.Replace("View","").ToUpperInvariant();
                string secondType=output.SecondViewType==null?"":" · "+output.SecondViewType.Replace("View","").ToUpperInvariant();
                if(isColumn&&output.FirstViewType!=null&&!fresh.Views.Any(v=>v.Type==output.FirstViewType))firstType=" · CHƯA CÓ VIEW";
                if(isColumn&&output.SecondViewType!=null&&!fresh.Views.Any(v=>v.Type==output.SecondViewType))secondType=" · CHƯA CÓ VIEW";
                referenceFirst.Set(output.ReferenceFirstCaption??(fresh.Overview?"KEYPLAN · X":isColumn?"VIEW TRÁI"+firstType:"TOP · TRỤC ĐỨNG"),output.ReferenceFirst,isColumn?"X":"→");
                referenceSecond.Set(output.ReferenceSecondCaption??(fresh.Overview?"KEYPLAN · Y":isColumn?"VIEW PHẢI"+secondType:"TOP · TRỤC NGANG"),output.ReferenceSecond,isColumn?"Y":"↑");
                referencesPanel.Visible=true;
                tips.SetToolTip(referenceFirst,output.Reference+"\n"+output.SelectionNote);tips.SetToolTip(referenceSecond,output.Reference+"\n"+output.SelectionNote);
                outcome.Correct=output.State==QuickState.Correct;
                outcome.Text=outcome.Correct?"Đúng trục theo keyplan":output.State==QuickState.Overview?"Đối chiếu theo từng view":output.State==QuickState.LayoutMismatch?"Cần sắp lại view":output.State==QuickState.IncompleteViews?"Góc đúng · cần bổ sung view":output.State==QuickState.NoSolution?"Không có bộ góc phù hợp":"Chưa thể xuất góc quay";
                SetStatus(outcome.Correct?"":CompactNote(output),output.Message+"\n"+output.ActionNote);
                tips.SetToolTip(calibration,output.Message+"\nDrawing properties → View creation → Attributes → Coordinate system.");
                if(result!=null)
                {
                    double[] numbers={result.X,result.Y,result.Z};
                    for(int i=0;i<3;i++)cards[i].Value=numbers[i].ToString("0.###",CultureInfo.InvariantCulture)+"°";
                    copy.Enabled=copy.Visible=true;values.Visible=true;outcome.Visible=false;
                    calibration.NeedsChange=!fresh.Settings.IsLocal;
                    calibration.Text=fresh.Settings.IsLocal?"LOCAL":"Đổi "+fresh.Settings.CoordinateSystem+" → Local";
                }
        }
        static string CompactNote(QuickResult output)
        {
            if(!string.IsNullOrWhiteSpace(output.BriefNote))return output.BriefNote;
            string first=(output.FirstViewType??"View X").Replace("View","");
            string second=(output.SecondViewType??"View Y").Replace("View","");
            switch(output.State)
            {
                case QuickState.Correct:return "Các view đúng keyplan.";
                case QuickState.LayoutMismatch:return output.IsColumn?"Đặt "+first+" bên trái, "+second+" bên phải.":"Đặt "+second+" trên "+first+".";
                case QuickState.IncompleteViews:return "Góc Local đúng. Cần đủ "+first+" / "+second+" để xác nhận.";
                case QuickState.NoSolution:return "Không tìm được góc phù hợp với các view hiện có.";
                case QuickState.VerifiedSuggestion:
                    if(!output.CompleteViews)return "Góc đề xuất; cần bổ sung view để xác nhận.";
                    return output.ActionNote??"";
                default:return output.Message??"";
            }
        }
        void SetStatus(string message,string details=null)
        {
            string full=(details??message??"").Trim();string brief=(message??"").Trim();
            if(brief.Length>220)brief=brief.Substring(0,217).TrimEnd()+"…";
            status.Text=brief;status.ForeColor=palette.Amber;tips.SetToolTip(status,full);status.AccessibleDescription=full;
            float scale=DeviceDpi/96F;
            float height=status.Text.Length==0?16*scale:Math.Max(44*scale,TextRenderer.MeasureText(status.Text,status.Font,new Size(Math.Max(150,status.Width-8),int.MaxValue),TextFormatFlags.WordBreak).Height+14*scale);
            int delta=(int)(height-stack.RowStyles[6].Height);
            stack.RowStyles[6].Height=height;
            ClientSize=new Size(ClientSize.Width,ClientSize.Height+delta);
        }
        protected override void Dispose(bool disposing) { if(disposing)tips.Dispose();base.Dispose(disposing); }

        sealed class SlideToggle : CheckBox
        {
            readonly Palette p;readonly Timer animation=new Timer {Interval=16};float position;
            internal SlideToggle(Palette p)
            {
                this.p=p;Appearance=Appearance.Button;FlatStyle=FlatStyle.Flat;Cursor=Cursors.Hand;
                AccessibleName="Tự áp dụng Local và góc xoay vào bản vẽ";
                SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
                animation.Tick+=(s,e)=> {float target=Checked?1:0;position+=(target-position)*0.35F;if(Math.Abs(position-target)<0.02F){position=target;animation.Stop();}Invalidate();};
            }
            protected override void OnCheckedChanged(EventArgs e) {animation.Start();Invalidate();base.OnCheckedChanged(e);}
            protected override void OnEnabledChanged(EventArgs e) {Invalidate();base.OnEnabledChanged(e);}
            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.Clear(Parent.BackColor);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;float scale=DeviceDpi/96F;
                int w=(int)(42*scale),h=(int)(24*scale);var track=new Rectangle(Width-w-2,(Height-h)/2,w,h);
                Color ink=Enabled?p.ButtonInk:p.Muted,fill=Checked?p.PrimaryBack:p.Border;
                using(var path=Rounded(track,h/2))using(var brush=new SolidBrush(fill))e.Graphics.FillPath(brush,path);
                float d=h-6*scale,left=track.Left+3*scale+position*(w-h);
                using(var brush=new SolidBrush(Color.White))e.Graphics.FillEllipse(brush,left,track.Top+3*scale,d,d);
                using(var font=new Font("Segoe UI",9F,FontStyle.Bold))TextRenderer.DrawText(e.Graphics,Text,font,new Rectangle(0,0,track.Left-(int)(10*scale),Height),ink,TextFormatFlags.Right|TextFormatFlags.VerticalCenter);
                if(Focused&&ShowFocusCues)ControlPaint.DrawFocusRectangle(e.Graphics,ClientRectangle,ink,Parent.BackColor);
            }
            protected override void Dispose(bool disposing) {if(disposing)animation.Dispose();base.Dispose(disposing);}
        }

        // Ordinary buttons follow MainForm.ApplyMainPresentationTheme gradients.
        // Light selections use a soft blue surface; only the main action is filled.
        sealed class Palette
        {
            internal readonly Color Background,Surface,Ink,Muted,Border,Accent,SelectedInk,Bar,BarInk,Green,Amber;
            internal readonly Color ButtonBack,ButtonEnd,ButtonBorder,ButtonInk,ButtonHoverBorder,BadgeBack;
            internal readonly Color SelectionBack,SelectionEnd,SelectionBorder,SelectionInk,PrimaryBack,PrimaryEnd;
            internal readonly Color ReferenceInk,ReferenceCaption,ReferenceSymbol;
            internal Palette(bool dark)
            {
                Background=dark?Color.FromArgb(18,18,18):Color.FromArgb(248,250,252);
                Surface=dark?Color.FromArgb(30,32,36):Color.White;
                Ink=dark?Color.FromArgb(235,235,235):Color.FromArgb(15,23,42);
                Muted=dark?Color.FromArgb(170,165,158):Color.FromArgb(100,116,139);
                Border=dark?Color.FromArgb(70,75,84):Color.FromArgb(208,220,236);
                Accent=dark?Color.FromArgb(201,122,64):Color.FromArgb(62,104,169);
                ButtonBack=dark?Color.FromArgb(29,34,42):Color.White;
                ButtonEnd=dark?Color.FromArgb(22,26,33):Color.FromArgb(239,244,251);
                ButtonBorder=dark?Color.FromArgb(137,96,65):Color.FromArgb(150,174,210);
                ButtonInk=dark?Color.FromArgb(235,173,119):Color.FromArgb(30,58,138);
                ButtonHoverBorder=Accent;
                SelectionBack=dark?Color.FromArgb(215,141,80):Color.FromArgb(230,240,253);
                SelectionEnd=dark?Accent:Color.FromArgb(211,228,248);
                SelectionBorder=dark?Accent:Color.FromArgb(116,151,197);
                SelectionInk=dark?Color.FromArgb(20,16,14):Color.FromArgb(42,76,126);
                PrimaryBack=dark?SelectionBack:ButtonInk;
                PrimaryEnd=dark?Accent:ButtonInk;
                ReferenceInk=Ink;
                ReferenceCaption=Muted;
                ReferenceSymbol=dark?Accent:ButtonInk;
                BadgeBack=dark?Color.FromArgb(30,24,20):Color.FromArgb(235,243,253);
                SelectedInk=dark?Color.FromArgb(20,16,14):Color.White;
                Bar=Surface;
                BarInk=Ink;
                Green=dark?Color.FromArgb(111,218,177):Color.FromArgb(22,124,94);
                Amber=dark?Color.FromArgb(255,191,112):Color.FromArgb(166,88,16);
            }
        }
        sealed class CoordinateBadge : Control
        {
            readonly Palette p;bool needsChange;
            internal bool NeedsChange {get {return needsChange;}set {needsChange=value;Invalidate();}}
            internal CoordinateBadge(Palette p) {this.p=p;DoubleBuffered=true;ResizeRedraw=true;AccessibleRole=AccessibleRole.StaticText;}
            protected override void OnTextChanged(EventArgs e) {base.OnTextChanged(e);AccessibleName=Text;Invalidate();}
            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.Clear(Parent.BackColor);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
                float scale=DeviceDpi/96F;
                using(var font=new Font("Segoe UI",9,FontStyle.Bold))
                {
                    int width=Math.Min(Width-2,TextRenderer.MeasureText(Text,font).Width+(int)(24*scale));
                    var rect=new Rectangle(Width-width-1,1,width,Math.Max(2,Height-3));
                    using(var path=Rounded(rect,(int)(6*scale)))using(var brush=new SolidBrush(needsChange?p.BadgeBack:p.Surface))
                    using(var pen=new Pen(needsChange?p.SelectionBorder:p.Border)) {e.Graphics.FillPath(brush,path);e.Graphics.DrawPath(pen,path);}
                    TextRenderer.DrawText(e.Graphics,Text,font,rect,needsChange?p.ButtonInk:p.Muted,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
                }
            }
        }
        sealed class ReferenceCard : Control
        {
            readonly Palette p;string caption="",value="",symbol="";
            internal ReferenceCard(Palette p) {this.p=p;DoubleBuffered=true;ResizeRedraw=true;AccessibleRole=AccessibleRole.StaticText;}
            internal void Set(string caption,string value,string symbol)
            {this.caption=caption;this.value=value??"—";this.symbol=symbol;AccessibleName=caption+" · "+symbol+": "+this.value;Invalidate();}
            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.Clear(Parent.BackColor);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;float scale=DeviceDpi/96F;
                using(var path=Rounded(new Rectangle(1,1,Width-3,Height-3),(int)(9*scale)))
                using(var brush=new SolidBrush(p.Surface))using(var pen=new Pen(p.Border)) {e.Graphics.FillPath(brush,path);e.Graphics.DrawPath(pen,path);}
                using(var font=new Font("Segoe UI Semibold",22,FontStyle.Regular))
                    TextRenderer.DrawText(e.Graphics,symbol,font,new Rectangle((int)(9*scale),0,(int)(31*scale),Height),p.ReferenceSymbol,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding);
                int left=(int)(48*scale),right=(int)(12*scale);
                using(var font=new Font("Segoe UI",8,FontStyle.Bold))TextRenderer.DrawText(e.Graphics,caption,font,new Rectangle(left,(int)(10*scale),Width-left-right,(int)(18*scale)),p.ReferenceCaption,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
                using(var font=new Font("Segoe UI Semibold",18,FontStyle.Regular))TextRenderer.DrawText(e.Graphics,value,font,new Rectangle(left,(int)(29*scale),Width-left-right,Height-(int)(36*scale)),p.ReferenceInk,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
            }
        }
        sealed class OutcomeBadge : Control
        {
            readonly Palette p;bool correct;
            internal bool Correct { get { return correct; }set { correct=value;Invalidate(); } }
            internal OutcomeBadge(Palette p) { this.p=p;DoubleBuffered=true;ResizeRedraw=true;AccessibleRole=AccessibleRole.StaticText; }
            protected override void OnTextChanged(EventArgs e) { AccessibleName=Text;Invalidate();base.OnTextChanged(e); }
            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.Clear(Parent.BackColor);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
                float size=Math.Min(76F*DeviceDpi/96F,Height*.58F),left=(Width-size)/2F,top=7F*DeviceDpi/96F;
                Color ink=correct?p.Green:p.Amber;
                using(var pen=new Pen(ink,Math.Max(3F,size*.08F)))
                {
                    pen.StartCap=pen.EndCap=LineCap.Round;pen.LineJoin=LineJoin.Round;
                    if(correct)e.Graphics.DrawLines(pen,new[]{new PointF(left+size*.12F,top+size*.52F),new PointF(left+size*.39F,top+size*.78F),new PointF(left+size*.9F,top+size*.2F)});
                    else
                    {
                        pen.Width=Math.Max(2F,size*.035F);e.Graphics.DrawEllipse(pen,left+size*.15F,top+size*.12F,size*.7F,size*.7F);
                        using(var font=new Font("Segoe UI",size*.3F,FontStyle.Bold))TextRenderer.DrawText(e.Graphics,"!",font,new Rectangle((int)left,(int)(top+size*.1F),(int)size,(int)(size*.75F)),ink,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
                    }
                }
                using(var font=new Font("Segoe UI",15,FontStyle.Bold))TextRenderer.DrawText(e.Graphics,Text,font,new Rectangle(8,(int)(top+size),Width-16,Math.Max(1,Height-(int)(top+size))),ink,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
            }
        }
        static GraphicsPath Rounded(Rectangle rect,int radius)
        {
            int d=radius*2;var path=new GraphicsPath();path.AddArc(rect.X,rect.Y,d,d,180,90);path.AddArc(rect.Right-d,rect.Y,d,d,270,90);
            path.AddArc(rect.Right-d,rect.Bottom-d,d,d,0,90);path.AddArc(rect.X,rect.Bottom-d,d,d,90,90);path.CloseFigure();return path;
        }
        sealed class PaintedButton : Button
        {
            readonly Palette p;bool hover,pressed;
            internal bool Selected,Chrome,Primary;
            internal PaintedButton(Palette p)
            {
                this.p=p;FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;Cursor=Cursors.Hand;
                SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
                BackColor=p.Surface;ForeColor=p.Ink;AccessibleRole=AccessibleRole.PushButton;
            }
            protected override void OnMouseEnter(EventArgs e) { hover=true;Invalidate();base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { hover=pressed=false;Invalidate();base.OnMouseLeave(e); }
            protected override void OnMouseDown(MouseEventArgs e) { pressed=e.Button==MouseButtons.Left;Invalidate();base.OnMouseDown(e); }
            protected override void OnMouseUp(MouseEventArgs e) { pressed=false;Invalidate();base.OnMouseUp(e); }
            protected override void OnEnabledChanged(EventArgs e) { Cursor=Enabled?Cursors.Hand:Cursors.Default;Invalidate();base.OnEnabledChanged(e); }
            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.Clear(Parent==null?p.Background:Parent.BackColor);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
                Color fill=Primary?p.PrimaryBack:Selected?p.SelectionBack:p.ButtonBack;
                Color end=Primary?p.PrimaryEnd:Selected?p.SelectionEnd:p.ButtonEnd;
                Color ink=Enabled?(Primary?p.SelectedInk:Selected?p.SelectionInk:p.ButtonInk):p.Muted;
                if(Enabled&&pressed) {fill=ControlPaint.Dark(fill,0.06F);end=ControlPaint.Dark(end,0.06F);}
                else if(Enabled&&hover) {fill=ControlPaint.Light(fill,0.06F);end=ControlPaint.Light(end,0.06F);}
                var rect=new Rectangle(1,1,Math.Max(1,Width-3),Math.Max(1,Height-3));
                using(var path=Rounded(rect,6))using(var brush=new LinearGradientBrush(rect,fill,end,12F))
                using(var pen=new Pen(Enabled&&hover?p.ButtonHoverBorder:Primary?p.Accent:Selected?p.SelectionBorder:p.ButtonBorder)) { e.Graphics.FillPath(brush,path);e.Graphics.DrawPath(pen,path); }
                TextRenderer.DrawText(e.Graphics,Text,Font,rect,ink,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
                if(Focused&&ShowFocusCues)ControlPaint.DrawFocusRectangle(e.Graphics,Rectangle.Inflate(rect,-4,-4),ink,fill);
            }
        }
        sealed class AngleCard : Control
        {
            readonly Palette p;readonly string axis;string value="—";
            internal string Value { get { return value; }set { this.value=value;AccessibleName="Around "+axis+": "+value;Invalidate(); } }
            internal AngleCard(string axis,Palette p) { this.axis=axis;this.p=p;Name="Angle"+axis;AccessibleRole=AccessibleRole.StaticText;SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true); }
            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.Clear(Parent.BackColor);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
                float scale=DeviceDpi/96F;
                var rect=new Rectangle(1,1,Width-3,Height-3);
                using(var path=Rounded(rect,(int)(12*scale)))using(var brush=new LinearGradientBrush(rect,ControlPaint.Light(p.Surface,0.035F),p.Surface,90F))using(var pen=new Pen(p.Border)) { e.Graphics.FillPath(brush,path);e.Graphics.DrawPath(pen,path); }
                using(var labelFont=new Font("Segoe UI",11,FontStyle.Bold))
                    TextRenderer.DrawText(e.Graphics,axis,labelFont,new Rectangle((int)(18*scale),(int)(13*scale),(int)(24*scale),(int)(26*scale)),p.Ink,TextFormatFlags.Left|TextFormatFlags.VerticalCenter);
                float size=value.Length>7?28:value.Length>5?34:52;
                using(var numberFont=new Font("Segoe UI Semibold",size,FontStyle.Regular))
                    TextRenderer.DrawText(e.Graphics,value,numberFont,new Rectangle((int)(8*scale),(int)(43*scale),Width-(int)(16*scale),Height-(int)(58*scale)),p.PrimaryEnd,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding);
            }
        }
    }
}
