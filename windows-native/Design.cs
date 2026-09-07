using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace MuseDeskNative
{
    internal static class MuseIcons
    {
        internal static void Draw(Graphics graphics,string icon,RectangleF bounds,Color color)
        {
            IconAssets.Draw(graphics,icon,bounds,color);
        }
    }

    internal sealed class MuseMark : Control
    {
        internal bool Tile=true;
        public MuseMark(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.SupportsTransparentBackColor,true);BackColor=Color.Transparent;}
        protected override void OnPaint(PaintEventArgs e)
        {
            MuseBrand.Draw(e.Graphics,ClientRectangle,Tile);
        }
    }

    internal sealed class RoundedPictureBox : PictureBox
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent==null?BackColor:Parent.BackColor);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            using(GraphicsPath path=RoundedComposerPanel.RoundedPath(new Rectangle(0,0,Width-1,Height-1),10))
            {
                GraphicsState state=e.Graphics.Save();e.Graphics.SetClip(path);base.OnPaint(e);e.Graphics.Restore(state);
                using(Pen border=new Pen(Color.FromArgb(225,225,225)))e.Graphics.DrawPath(border,path);
            }
        }
    }

    internal sealed class MuseToggle : CheckBox
    {
        public MuseToggle(){AutoSize=false;Height=28;ForeColor=Color.FromArgb(35,35,35);SetStyle(ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint|ControlStyles.UserPaint,true);}
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            Rectangle track=new Rectangle(Width-40,(Height-22)/2,38,22);
            using(GraphicsPath path=RoundedComposerPanel.RoundedPath(track,11))
            using(SolidBrush fill=new SolidBrush(Checked?Color.FromArgb(35,35,35):Color.FromArgb(204,204,204)))e.Graphics.FillPath(fill,path);
            using(SolidBrush knob=new SolidBrush(Color.White))e.Graphics.FillEllipse(knob,track.X+(Checked?18:2),track.Y+2,18,18);
            TextRenderer.DrawText(e.Graphics,Text,Font,new Rectangle(0,0,Width-56,Height),ForeColor,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
            if(Focused&&ShowFocusCues)using(GraphicsPath focus=RoundedComposerPanel.RoundedPath(new Rectangle(track.X-2,track.Y-2,track.Width+4,track.Height+4),13))using(Pen p=new Pen(Color.FromArgb(125,125,125)))e.Graphics.DrawPath(p,focus);
        }
    }

    internal sealed class IdeaButton : RoundedButton
    {
        internal string Caption;
        internal string Detail;
        internal string Glyph;
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            MuseIcons.Draw(e.Graphics,Glyph,new RectangleF(12,(Height-18)/2,18,18),Color.FromArgb(100,100,100));
            using(Font title=new Font("Segoe UI Semibold",10F))
            using(Font body=new Font("Segoe UI",8.7F))
            {
                TextRenderer.DrawText(e.Graphics,Caption,body,new Rectangle(38,0,Width-46,Height),Color.FromArgb(90,90,90),TextFormatFlags.EndEllipsis|TextFormatFlags.SingleLine|TextFormatFlags.VerticalCenter);
            }
        }
    }

    public sealed partial class MainForm
    {
        private Panel composerHost;
        private Button composerModelButton;
        private Button composerThinkingButton;
        private Button sidebarToggle;
        private bool layingOutWorkspace;
        private int ChatColumnWidth(){return Math.Max(300,Math.Min(740,center.ClientSize.Width-ResultsWidth-64));}
        private void LayoutWorkspace()
        {
            if(layingOutWorkspace||center==null)return;
            layingOutWorkspace=true;
            try
            {
                int column=ChatColumnWidth(),left=Math.Max(24,(center.ClientSize.Width-ResultsWidth-column)/2);
                if(composerHost!=null)composerHost.Padding=new Padding(left,8,center.ClientSize.Width-column-left,12);
                if(messageList!=null)messageList.Padding=new Padding(left,24,Math.Max(0,messageList.ClientSize.Width-left-column),24);
                if(activeTitle!=null){activeTitle.Location=new Point(60,13);activeTitle.Size=new Size(Math.Max(100,center.Width-130),23);}
                if(statusLine!=null)statusLine.Visible=false;
                if(detailsButton!=null)detailsButton.Location=new Point(center.Width-detailsButton.Width-18,8);
                if(sidebarToggle!=null)
                {
                    Control parent=sidebar.Visible?(Control)sidebar:header;
                    if(sidebarToggle.Parent!=parent)parent.Controls.Add(sidebarToggle);
                    sidebarToggle.Location=sidebar.Visible?new Point(211,14):new Point(8,8);sidebarToggle.BackColor=sidebar.Visible?Ink:Surface;
                    Control[] folders=header.Controls.Find("HeaderFolder",false);if(folders.Length>0)folders[0].Visible=sidebar.Visible;
                }
                LayoutResults();
            }
            finally{layingOutWorkspace=false;}
        }

        private void BuildSidebar()
        {
            MuseMark mark=new MuseMark {Tile=false,Location=new Point(17,14),Size=new Size(28,28)};
            Label brand=new Label {Text="Muse Desk",Font=new Font("Segoe UI Semibold",11F),ForeColor=TextInk,Location=new Point(48,18),AutoSize=true};
            sidebar.Controls.AddRange(new Control[]{mark,brand});
            RoundedButton create=(RoundedButton)MakeButton("Новый чат",Ink,TextInk,222,36);create.IconName="edit";create.TextAlign=ContentAlignment.MiddleLeft;create.Radius=8;create.Location=new Point(16,65);create.Click+=delegate{CreateChat();};sidebar.Controls.Add(create);
            RoundedButton capabilities=(RoundedButton)MakeButton("Возможности",Ink,TextInk,222,36);capabilities.IconName="sliders";capabilities.TextAlign=ContentAlignment.MiddleLeft;capabilities.Location=new Point(16,105);capabilities.Click+=delegate{ToggleCapabilities();};sidebar.Controls.Add(capabilities);
            RoundedButton documents=(RoundedButton)MakeButton("Добавить файлы",Ink,TextInk,222,36);documents.IconName="file";documents.TextAlign=ContentAlignment.MiddleLeft;documents.Location=new Point(16,145);documents.Click+=delegate{AddFiles(false);};sidebar.Controls.Add(documents);
            RoundedComposerPanel search=new RoundedComposerPanel {Location=new Point(16,198),Size=new Size(222,36),Radius=8,BackColor=Ink,BorderColor=Color.FromArgb(221,221,221)};
            search.Paint+=delegate(object s,PaintEventArgs e){MuseIcons.Draw(e.Graphics,"search",new RectangleF(10,9,17,17),Muted);};
            searchBox=new TextBox {AccessibleName="Поиск чатов",BorderStyle=BorderStyle.None,BackColor=Ink,ForeColor=TextInk,Font=InterfaceTypography.Sidebar(),Location=new Point(35,8),Size=new Size(176,22)};
            search.Controls.Add(searchBox);sidebar.Controls.Add(search);
            NativeMethods.SendMessage(searchBox.Handle,0x1501,new IntPtr(1),"Поиск чатов");
            searchBox.TextChanged+=delegate{RefreshChatList();};
            RoundedButton project=(RoundedButton)MakeButton("Добавить проект",Ink,Muted,222,30);project.IconName="add";project.TextAlign=ContentAlignment.MiddleLeft;project.Font=new Font("Segoe UI",8.5F);project.Location=new Point(16,248);project.Click+=delegate{ChooseProject(activeChat);};sidebar.Controls.Add(project);
            chatList=new ModernFlowPanel {Location=new Point(12,283),Width=234,Height=330,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true,BackColor=Ink};sidebar.Controls.Add(chatList);InstallScrollBar(chatList,sidebar);
            settingsButton=MakeButton("Настройки",Ink,TextInk,222,36);settingsButton.TextAlign=ContentAlignment.MiddleLeft;((RoundedButton)settingsButton).IconName="sliders";settingsButton.Click+=delegate{OpenSettings();};sidebar.Controls.Add(settingsButton);
            Panel status=new Panel {Size=new Size(222,56),BackColor=Ink};
            connectionDot=new Panel {Location=new Point(13,12),Size=new Size(6,6),BackColor=Warning};
            connectionTitle=new Label {Text="Подключение…",ForeColor=TextInk,Font=new Font("Segoe UI",8.5F),Location=new Point(29,6),Size=new Size(182,22),AutoEllipsis=true};
            connectionDetail=new Label {Text="Muse Glimmer · локально",ForeColor=Muted,Font=new Font("Segoe UI",8F),Location=new Point(29,28),Size=new Size(185,20),AutoEllipsis=true};
            status.Controls.AddRange(new Control[]{connectionDot,connectionTitle,connectionDetail});sidebar.Controls.Add(status);
            Action layout=delegate{int h=sidebar.ClientSize.Height;chatList.Height=Math.Max(80,h-283-130);settingsButton.Location=new Point(16,h-56);status.Location=new Point(16,h-117);};sidebar.Resize+=delegate{layout();};layout();
            foreach(RoundedButton action in sidebar.Controls.OfType<RoundedButton>())
            {
                action.Font=action==project?InterfaceTypography.Caption():InterfaceTypography.Sidebar();
                action.IconTint=InterfaceTypography.Secondary;
                action.GrayscaleText=true;
            }
        }

        private void BuildHeader()
        {
            activeTitle=new Label {Text="Новый диалог",AutoEllipsis=true,ForeColor=TextInk,Font=new Font("Segoe UI Semibold",10.5F)};
            statusLine=new Label {Text="Muse Glimmer · локальная модель",AutoEllipsis=true,ForeColor=Muted,Font=new Font("Segoe UI",8F)};
            sidebarToggle=MakeButton("Боковая панель",Surface,Muted,32,32);((RoundedButton)sidebarToggle).IconName="sidebar";((RoundedButton)sidebarToggle).IconOnly=true;sidebarToggle.Location=new Point(16,14);sidebarToggle.Click+=delegate{sidebar.Visible=!sidebar.Visible;LayoutWorkspace();RenderConversation();};tips.SetToolTip(sidebarToggle,"Показать или скрыть чаты");
            sidebarToggle.Top=8;
            sidebarToggle.Location=new Point(211,14);sidebarToggle.BackColor=Ink;sidebar.Controls.Add(sidebarToggle);
            detailsButton=MakeButton("Возможности",Surface,Muted,32,32);((RoundedButton)detailsButton).IconName="sliders";((RoundedButton)detailsButton).IconOnly=true;tips.SetToolTip(detailsButton,"Возможности Muse");
            detailsButton.Click+=delegate{ToggleCapabilities();};
            header.Controls.AddRange(new Control[]{activeTitle,statusLine,detailsButton});
            header.Resize+=delegate{LayoutWorkspace();};
            RoundedComposerPanel modelFrame=new RoundedComposerPanel {Location=new Point(20,512),Size=new Size(220,36),Radius=10,BackColor=Surface,BorderColor=Line};
            modelCombo=new ComboBox {AccessibleName="Модель",DropDownStyle=ComboBoxStyle.DropDownList,Width=200,Font=new Font("Segoe UI",9F),Location=new Point(10,7),FlatStyle=FlatStyle.Flat,BackColor=Surface,DrawMode=DrawMode.OwnerDrawFixed,ItemHeight=20,DropDownWidth=340};
            modelCombo.DrawItem+=delegate(object s,DrawItemEventArgs e){if(e.Index<0)return;e.DrawBackground();string name=modelCombo.Items[e.Index].ToString();if(name==PreferredModel)name="Muse Glimmer · Heretic";TextRenderer.DrawText(e.Graphics,name,modelCombo.Font,e.Bounds,e.ForeColor,TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);};
            modelCombo.SelectedIndexChanged+=delegate{if(!refreshingModels&&modelCombo.SelectedItem!=null){state.settings.model=modelCombo.SelectedItem.ToString();modelAvailable=true;SaveState();UpdateCapabilityUi();}};modelFrame.Controls.Add(modelCombo);rightRail.Controls.Add(modelFrame);
        }

        private void BuildRightRail()
        {
            rightRail.BackColor=Ink;rightRail.AutoScrollMinSize=new Size(0,720);
            rightRail.Controls.Add(new Label {Text="Возможности",Font=new Font("Segoe UI Semibold",16F),ForeColor=TextInk,AutoSize=true,Location=new Point(20,25)});
            rightRail.Controls.Add(new Label {Text="Настройте свой способ работы",Font=new Font("Segoe UI",8.7F),ForeColor=Muted,AutoSize=true,Location=new Point(20,61)});
            AddCapabilityCard("image","Изображения","Фото и снимки экрана",100,Iris);
            AddCapabilityCard("file","Документы","Текст, таблицы и код",172,Iris);
            AddCapabilityCard("mic","Голос","Диктовка и озвучивание",244,Iris);
            AddCapabilityCard("sliders","Инструменты",PermissionStore.HasFullAccess?"Полный доступ включён":"Действия с подтверждением",316,Iris);
            thinkingButton=MakeButton("Рассуждение: включено",IrisSoft,Iris,220,36);thinkingButton.Location=new Point(20,404);thinkingButton.Click+=delegate{state.settings.thinkingEnabled=!state.settings.thinkingEnabled;SaveState();UpdateCapabilityUi();};
            toolsButton=MakeButton("Инструменты: выключены",Surface,TextInk,220,36);toolsButton.Location=new Point(20,448);toolsButton.Click+=delegate{state.settings.toolsEnabled=!state.settings.toolsEnabled;SaveState();UpdateCapabilityUi();};
            contextValue=new Label {ForeColor=Muted,Font=new Font("Segoe UI",8F),AutoSize=true,Location=new Point(20,488)};
            installButton=MakeButton("Загрузить модель",Iris,Color.White,220,38);installButton.Location=new Point(20,562);installButton.Click+=async delegate{if(!connected){await EnsureEngineAsync();await RefreshConnectionAsync();}else await PullModelAsync();};
            rightRail.Controls.AddRange(new Control[]{thinkingButton,toolsButton,contextValue,installButton});
            rightRail.Controls.Add(new Label {Text="Muse работает на вашем компьютере. Голос обрабатывается средствами Windows.",ForeColor=Muted,Font=new Font("Segoe UI",9F),Location=new Point(20,610),Size=new Size(220,80)});UpdateCapabilityUi();
        }

        private void AddCapabilityCard(string code,string title,string value,int top,Color accent)
        {
            RoundedComposerPanel card=new RoundedComposerPanel {Location=new Point(20,top),Size=new Size(220,60),Radius=12,BackColor=Surface,BorderColor=Line};
            card.Paint+=delegate(object s,PaintEventArgs e){MuseIcons.Draw(e.Graphics,code,new RectangleF(14,19,22,22),accent);};
            card.Controls.Add(new Label {Text=title,Font=new Font("Segoe UI Semibold",9.5F),ForeColor=TextInk,Location=new Point(48,10),Size=new Size(158,22)});
            card.Controls.Add(new Label {Text=value,Font=new Font("Segoe UI",8F),ForeColor=Muted,Location=new Point(48,34),Size=new Size(164,20),AutoEllipsis=true});rightRail.Controls.Add(card);
        }

        private Control BuildWelcome(int width)
        {
            int available=Math.Max(270,messageList.ClientSize.Height-48);
            int top=Math.Max(8,(available-246)/2);
            Panel empty=new Panel {Width=width,Height=available,Margin=Padding.Empty,BackColor=Canvas};
            MuseMark mark=new MuseMark {Tile=false,Location=new Point((width-58)/2,top),Size=new Size(58,58)};
            Label title=new Label {Text="Что будем делать?",TextAlign=ContentAlignment.MiddleCenter,ForeColor=TextInk,Font=new Font("Segoe UI",24F),Location=new Point(0,top+65),Size=new Size(width,48)};
            Label note=new Label {Text="Muse Glimmer",TextAlign=ContentAlignment.TopCenter,ForeColor=Muted,Font=new Font("Segoe UI",16F),Location=new Point(0,top+117),Size=new Size(width,37)};
            int gap=10,total=Math.Min(width,590),start=(width-total)/2,cardWidth=(total-2*gap)/3;
            string[] names={"Обсудить идею","Разобрать документ","Посмотреть фото"};
            string[] details={"Найти подход и продумать детали","Выделить главное и сделать выводы","Замечать детали и находить ответы"};
            string[] icons={"spark","file","image"};
            for(int i=0;i<3;i++)
            {
                int selected=i;IdeaButton card=new IdeaButton {Caption=names[i],Detail=details[i],Glyph=icons[i],AccessibleName=names[i],Text="",Radius=9,Outline=true,BackColor=Surface,ForeColor=Iris,Location=new Point(start+i*(cardWidth+gap),top+188),Size=new Size(cardWidth,40),Cursor=Cursors.Hand};
                card.Click+=delegate{if(selected==1)AddFiles(false);else if(selected==2)AddFiles(true);input.Text=selected==0?"Помоги развить идею: ":selected==1?"Изучи документ и выдели главное.":"Опиши изображение и важные детали.";input.Focus();input.SelectionStart=input.TextLength;};empty.Controls.Add(card);
            }
            empty.Controls.AddRange(new Control[]{mark,title,note});return empty;
        }

        private void ToggleCapabilities()
        {
            if(!rightRail.Visible&&Width<1200)Width=Math.Min(1200,Screen.FromControl(this).WorkingArea.Width);
            rightRail.Visible=!rightRail.Visible;detailsButton.BackColor=rightRail.Visible?IrisSoft:Surface;LayoutWorkspace();RenderConversation();
        }

        private void ShowComposerModels()
        {
            if(generationCancellation!=null)return;
            ContextMenuStrip menu=new ContextMenuStrip();
            foreach(object entry in modelCombo.Items){string name=entry.ToString();ToolStripMenuItem item=new ToolStripMenuItem(name==PreferredModel?"Muse Glimmer · Heretic":name);item.Checked=name==state.settings.model;item.Click+=delegate{modelCombo.SelectedItem=name;UpdateCapabilityUi();};menu.Items.Add(item);}
            if(menu.Items.Count==0)menu.Items.Add("Модели ещё не загружены").Enabled=false;
            menu.Closed+=delegate{menu.Dispose();};menu.Show(composerModelButton,new Point(0,composerModelButton.Height));
        }

        private static void FitRichText(RichTextBox box,int maximum)
        {
            IntPtr handle=box.Handle;
            box.Select(0,0);
            box.ScrollToCaret();
            int height=box.GetPositionFromCharIndex(box.TextLength).Y+box.Font.Height+12;
            box.Height=Math.Max(box.Font.Height+12,Math.Min(maximum,height));
            box.ScrollBars=height>maximum?RichTextBoxScrollBars.Vertical:RichTextBoxScrollBars.None;
        }
    }
}
