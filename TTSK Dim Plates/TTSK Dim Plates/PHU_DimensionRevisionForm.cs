using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using D = Tekla.Structures.Drawing;

namespace Tekla.Technology.Akit.UserScript
{
    public sealed partial class PHU_DimensionReportForm
    {
        Panel revisionPanel;
        FlowLayoutPanel revisionCards;
        Panel revisionViewport;
        RevisionScroll revisionScroll;
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
            if(revisionFilterMenu!=null){revisionFilterMenu.Dispose();revisionFilterMenu=null;}
            revisionPanel.Dispose(); revisionPanel = null; reportContent.Show();
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
            var back=Button(revisionOnly?"Đóng":"←  Check DIM",false); back.Dock=DockStyle.Right; back.Width=100;
            back.Click+=(s,e)=>CloseRevision();
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
            layout.Controls.Add(listHost,0,4);
            revisionDetail=TextLabel("",10,muted); revisionDetail.Visible=false; layout.Controls.Add(revisionDetail,0,5);
            // AutoSize accounts for the actual DPI-scaled button height, margins and shadow.
            // The old fixed 44px row + nested FlowLayoutPanel clipped the lower edges.
            var actions=new TableLayoutPanel {Name="revisionActions",Dock=DockStyle.Fill,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,
                ColumnCount=4,RowCount=1,Margin=Padding.Empty,Padding=new Padding(0,10,0,6)};
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));actions.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));actions.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            actions.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var refresh=Button("▶  Bắt đầu · F5",true); refresh.Name="revisionStart"; refresh.Size=new Size(165,44);refresh.Margin=new Padding(0,0,10,0); refresh.Click+=(s,e)=>RefreshRevision();
            var create=Button("Create drawing",false); create.Name="revisionCreate"; create.Size=new Size(175,44);create.Margin=Padding.Empty;
            create.Enabled=createRevisionDrawing!=null; create.Click+=(s,e)=>CreateRevisionDrawing();
            var delete=Button("Delete REV",false);delete.Name="revisionDelete";delete.Size=new Size(155,44);delete.Margin=Padding.Empty;
            delete.ForeColor=Color.IndianRed;delete.Click+=(s,e)=>DeleteRevisionSection();
            actions.Controls.Add(refresh,0,0);actions.Controls.Add(create,1,0);actions.Controls.Add(delete,3,0);layout.Controls.Add(actions,0,6);
            revisionPanel.Controls.Add(layout); Controls.Add(revisionPanel); revisionPanel.BringToFront();
            foreach (var text in new[]{
                new[]{"Bắt đầu", "Đối chiếu DIM hiện tại với snapshot và tô đỏ các giá trị thay đổi."},
                new[]{"Create drawing", "Tạo lại DIM và Check REV. Giữ scale, căn cạnh dưới và tâm ngang thanh theo snapshot."},
                new[]{"Delete REV", "Chọn view mặt cắt cần xóa trong Tekla, rồi bấm Delete REV để ghi dấu xóa."}})
            {
                var card=Card();card.Dock=DockStyle.None;card.Height=100;card.Margin=new Padding(0,0,0,12);card.Padding=new Padding(18,12,18,12);
                var rows=Stack(32,36);rows.Controls.Add(TextLabel(text[0],14,ink,true),0,0);rows.Controls.Add(TextLabel(text[1],10,muted),0,1);
                card.Controls.Add(rows);revisionCards.Controls.Add(card);
            }
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
            foreach(Control card in revisionCards.Controls)card.Width=width-revisionCards.Padding.Horizontal;
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
                var empty=TextLabel(revisionResult.Items.Count==0?"✓  Không phát hiện thay đổi\r\nDIM và nội dung đã đối chiếu trùng với snapshot.":
                    "Không có DIM trong bộ lọc này\r\nChọn Tất cả thay đổi để xem toàn bộ kết quả.",16,muted,true);
                empty.Dock=DockStyle.None;empty.Height=160;revisionCards.Controls.Add(empty);
            }
            ResizeRevisionCards();revisionCards.ResumeLayout();
            UpdateRevisionNavigation();
        }
        void SelectRevision(PHU_DimensionRevision.Item item)
        {
            if(checking)return;
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

