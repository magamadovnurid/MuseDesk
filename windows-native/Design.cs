using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace MuseDeskNative
{
    internal sealed class MuseMenuRenderer : ToolStripProfessionalRenderer
    {
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {e.Graphics.Clear(e.ToolStrip is ToolStripDropDown?Color.FromArgb(250,250,250):Color.FromArgb(245,245,245));}
        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e){}
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if(!e.Item.Enabled || (!e.Item.Selected && !e.Item.Pressed))return;
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            using(var path=RoundedComposerPanel.RoundedPath(new Rectangle(3,1,e.Item.Width-6,e.Item.Height-2),7))
            using(var brush=new SolidBrush(Color.FromArgb(e.Item.Pressed?226:233,e.Item.Pressed?226:233,e.Item.Pressed?226:233)))e.Graphics.FillPath(brush,path);
        }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {e.TextColor=e.Item.Enabled?InterfaceTypography.Primary:InterfaceTypography.Tertiary;base.OnRenderItemText(e);}
        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {e.ArrowColor=InterfaceTypography.Secondary;base.OnRenderArrow(e);}
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            Rectangle area=e.ImageRectangle;
            float x=area.Left+area.Width/2F,y=area.Top+area.Height/2F;
            using(var pen=new Pen(e.Item.Enabled?InterfaceTypography.Secondary:InterfaceTypography.Tertiary,1.5F))
            {
                pen.StartCap=pen.EndCap=LineCap.Round;pen.LineJoin=LineJoin.Round;
                e.Graphics.DrawLines(pen,new[]{new PointF(x-4,y),new PointF(x-1,y+3),new PointF(x+5,y-4)});
            }
        }
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {if(e.ToolStrip is ToolStripDropDown)using(var pen=new Pen(Color.FromArgb(224,224,224)))e.Graphics.DrawRectangle(pen,0,0,e.ToolStrip.Width-1,e.ToolStrip.Height-1);}
        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {using(var pen=new Pen(Color.FromArgb(229,229,229)))e.Graphics.DrawLine(pen,12,e.Item.Height/2,e.Item.Width-12,e.Item.Height/2);}
    }

    internal sealed class WindowControlButton : RoundedButton
    {
        internal string Action="close";
        internal bool Restore;
        private bool hot,down;
        protected override void OnMouseEnter(EventArgs e){base.OnMouseEnter(e);hot=true;Invalidate();}
        protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);hot=false;down=false;Invalidate();}
        protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);down=true;Invalidate();}
        protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);down=false;Invalidate();}
        protected override void OnPaint(PaintEventArgs e)
        {
            PaintSurface(e.Graphics,ClientRectangle,hot,down,Focused&&ShowFocusCues);
            float unit=Math.Max(1F,Height/36F),side=10F*unit;
            float x=(Width-side)/2F,y=(Height-side)/2F;
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            using(var pen=new Pen(hot?InterfaceTypography.Primary:InterfaceTypography.Secondary,unit))
            {
                if(Action=="minimize")e.Graphics.DrawLine(pen,x,Height/2F,x+side,Height/2F);
                else if(Action=="maximize")
                {
                    if(Restore){e.Graphics.DrawLines(pen,new[]{new PointF(x+3*unit,y),new PointF(x+side,y),new PointF(x+side,y+7*unit)});e.Graphics.DrawRectangle(pen,x,y+3*unit,7*unit,7*unit);}
                    else e.Graphics.DrawRectangle(pen,x,y,side,side);
                }
                else{e.Graphics.DrawLine(pen,x,y,x+side,y+side);e.Graphics.DrawLine(pen,x+side,y,x,y+side);}
            }
        }
    }

    internal static class MuseDialog
    {
        internal static Form Build(string text,string title,MessageBoxButtons buttons,MessageBoxIcon icon)
        {
            bool confirmation=buttons==MessageBoxButtons.YesNo;
            var background=Color.FromArgb(245,245,245);
            var form=new Form {Text=title,ClientSize=new Size(540,280),FormBorderStyle=FormBorderStyle.None,StartPosition=FormStartPosition.CenterParent,ShowInTaskbar=false,BackColor=background,Font=InterfaceTypography.Sidebar(),Padding=new Padding(1)};
            form.Paint+=delegate(object sender,PaintEventArgs e){using(var pen=new Pen(Color.FromArgb(215,215,215)))e.Graphics.DrawRectangle(pen,0,0,form.Width-1,form.Height-1);};
            var heading=new Label {Text=title,Font=new Font("Segoe UI",14F),ForeColor=InterfaceTypography.Primary,Location=new Point(24,23),Size=new Size(455,55),AutoEllipsis=true};
            var close=new WindowControlButton {Action="close",AccessibleName="Закрыть",Location=new Point(493,14),Size=new Size(32,32),BackColor=background};
            close.Click+=delegate{form.DialogResult=confirmation?DialogResult.No:DialogResult.OK;form.Close();};
            int bodyHeight=Math.Min(360,Math.Max(65,TextRenderer.MeasureText(text,form.Font,new Size(476,int.MaxValue),TextFormatFlags.WordBreak).Height+24));
            var frame=new RoundedComposerPanel {Location=new Point(24,84),Size=new Size(492,bodyHeight),Radius=12,BorderColor=Color.FromArgb(228,228,228),BackColor=Color.White};
            var body=new RichTextBox {Text=text,ReadOnly=true,BorderStyle=BorderStyle.None,BackColor=Color.White,ForeColor=Color.FromArgb(50,50,50),Font=InterfaceTypography.Sidebar(),Location=new Point(14,12),Size=new Size(464,bodyHeight-24),ScrollBars=RichTextBoxScrollBars.Vertical,DetectUrls=false,TabStop=false};frame.Controls.Add(body);
            int footer=frame.Bottom+24;form.ClientSize=new Size(540,footer+60);
            var primary=new RoundedButton {Text=confirmation?(title.IndexOf("удал",StringComparison.OrdinalIgnoreCase)>=0?"Удалить":"Да"):"Понятно",AccessibleName=confirmation?"Подтвердить":"Понятно",DialogResult=confirmation?DialogResult.Yes:DialogResult.OK,Location=new Point(384,footer),Size=new Size(132,36),Radius=9,BackColor=Color.FromArgb(35,35,35),ForeColor=Color.White,Font=InterfaceTypography.Sidebar()};
            form.Controls.AddRange(new Control[]{heading,close,frame,primary});
            if(confirmation)
            {
                var cancel=new RoundedButton {Text="Отмена",DialogResult=DialogResult.No,Location=new Point(268,footer),Size=new Size(104,36),Radius=9,BackColor=background,ForeColor=Color.FromArgb(50,50,50),Font=InterfaceTypography.Sidebar()};form.Controls.Add(cancel);form.AcceptButton=cancel;form.CancelButton=cancel;form.Shown+=delegate{cancel.Focus();};
            }
            else{form.AcceptButton=primary;form.CancelButton=primary;}
            return form;
        }

        internal static DialogResult Show(IWin32Window owner,string text,string title="Muse Desk",MessageBoxButtons buttons=MessageBoxButtons.OK,MessageBoxIcon icon=MessageBoxIcon.None)
        {using(var dialog=Build(text,title,buttons,icon))return dialog.ShowDialog(owner);}
    }

    internal sealed class WorkspaceSurface : Panel
    {
        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if(Width<24 || Height<24)return;
            using(var path=new GraphicsPath())
            {
                path.StartFigure();path.AddArc(0,0,40,40,180,90);
                path.AddLine(20,0,Width,0);path.AddLine(Width,0,Width,Height);
                path.AddLine(Width,Height,0,Height);path.CloseFigure();
                Region previous=Region;Region=new Region(path);if(previous!=null)previous.Dispose();
            }
        }
    }

    internal sealed class ModelStatusIndicator : Panel
    {
        private readonly Timer animation=new Timer{Interval=40};
        private int angle;
        internal bool Busy {get{return animation.Enabled;}set{animation.Enabled=value;Invalidate();}}
        internal ModelStatusIndicator(){DoubleBuffered=true;animation.Tick+=delegate{angle=(angle+12)%360;Invalidate();};}
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            using(var pen=new Pen(ForeColor,2)){pen.StartCap=pen.EndCap=LineCap.Round;
                if(Busy)e.Graphics.DrawArc(pen,3,3,Width-6,Height-6,angle,250);
                else e.Graphics.DrawEllipse(pen,4,4,Width-8,Height-8);}
        }
        protected override void Dispose(bool disposing){if(disposing)animation.Dispose();base.Dispose(disposing);}
    }
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
        private MenuStrip applicationMenu;
        private static void StyleMenu(ToolStrip menu)
        {
            menu.Renderer=new MuseMenuRenderer();menu.Font=InterfaceTypography.Sidebar();menu.ForeColor=InterfaceTypography.Primary;
            foreach(ToolStripItem item in menu.Items)
            {
                item.ForeColor=InterfaceTypography.Primary;
                if(menu is ToolStripDropDown && !(item is ToolStripSeparator))item.Padding=new Padding(5,5,8,5);
                var branch=item as ToolStripMenuItem;if(branch!=null && branch.HasDropDownItems)StyleMenu(branch.DropDown);
            }
        }
        private Panel titleBar;
        private Button windowMinimize,windowMaximize,windowClose;
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool ReleaseCapture();

        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            if(message.Msg==0x84 && !fullScreen && WindowState==FormWindowState.Normal)
            {
                long position=message.LParam.ToInt64();Point point=PointToClient(new Point((short)(position&0xffff),(short)((position>>16)&0xffff)));
                bool left=point.X<6,right=point.X>=ClientSize.Width-6,top=point.Y<6,bottom=point.Y>=ClientSize.Height-6;
                int hit=top?(left?13:right?14:12):bottom?(left?16:right?17:15):left?10:right?11:0;
                if(hit!=0)message.Result=new IntPtr(hit);
            }
        }

        private void ToggleMaximized()
        {
            if(fullScreen)ToggleFullScreen();
            MaximizedBounds=Screen.FromControl(this).WorkingArea;
            WindowState=WindowState==FormWindowState.Maximized?FormWindowState.Normal:FormWindowState.Maximized;
        }

        private void DragWindow(object sender,MouseEventArgs e)
        {
            if(e.Button!=MouseButtons.Left || fullScreen)return;
            if(sender==applicationMenu && applicationMenu.GetItemAt(e.Location)!=null)return;
            if(e.Clicks==2){ToggleMaximized();return;}
            ReleaseCapture();NativeMethods.SendMessage(Handle,0xA1,new IntPtr(2),null);
        }
        private TextBoxBase menuTextTarget;
        private bool fullScreen;
        private Rectangle normalWindowBounds;
        private FormWindowState normalWindowState;

        private TextBoxBase FocusedText(Control root)
        {
            var text=root as TextBoxBase;if(text!=null && text.Focused)return text;
            foreach(Control child in root.Controls){var found=FocusedText(child);if(found!=null)return found;}
            return null;
        }

        private void EditText(string action)
        {
            var text=FocusedText(this)??menuTextTarget;
            if(text==null || text.IsDisposed || !text.Enabled)return;
            if(action=="copy")text.Copy();
            else if(action=="all")text.SelectAll();
            else if(!text.ReadOnly)
            {
                if(action=="cut")text.Cut();
                if(action=="paste")text.Paste();
                if(action=="undo" && text.CanUndo)text.Undo();
                if(action=="delete")text.SelectedText="";
            }
            text.Focus();
        }

        private ToolStripMenuItem AddCommand(ToolStripMenuItem parent,string title,Keys shortcut,Action action)
        {
            var item=new ToolStripMenuItem(title){ShortcutKeys=shortcut};
            item.Click+=delegate{action();};parent.DropDownItems.Add(item);return item;
        }

        private void ToggleFullScreen()
        {
            if(!fullScreen)
            {
                normalWindowState=WindowState;normalWindowBounds=WindowState==FormWindowState.Normal?Bounds:RestoreBounds;
                WindowState=FormWindowState.Normal;FormBorderStyle=FormBorderStyle.None;Bounds=Screen.FromControl(this).Bounds;fullScreen=true;
            }
            else
            {
                FormBorderStyle=FormBorderStyle.None;Bounds=normalWindowBounds;WindowState=normalWindowState;fullScreen=false;
            }
        }

        private void BuildApplicationMenu()
        {
            titleBar=new Panel {Dock=DockStyle.Top,Height=40,BackColor=Ink};
            applicationMenu=new MenuStrip {BackColor=Ink,ForeColor=TextInk,Dock=DockStyle.Fill,Font=InterfaceTypography.Sidebar(),Padding=new Padding(0,6,0,6),Height=40,AutoSize=false,GripStyle=ToolStripGripStyle.Hidden,Renderer=new ToolStripProfessionalRenderer(new ProfessionalColorTable {UseSystemColors=true})};
            var brandArea=new Panel {Name="WindowBrand",Dock=DockStyle.Left,Width=147,BackColor=Ink};
            var windowActions=new Panel {Name="WindowActions",Dock=DockStyle.Right,Width=132,BackColor=Ink};
            var mark=new MuseMark {Tile=false,Location=new Point(4,8),Size=new Size(24,24)};
            var brand=new Label {Text="Muse Desk",Font=new Font("Segoe UI",11F,FontStyle.Bold),ForeColor=Color.FromArgb(43,45,48),BackColor=Ink,Location=new Point(36,0),Size=new Size(100,40),TextAlign=ContentAlignment.MiddleLeft};
            titleBar.Controls.Add(applicationMenu);titleBar.Controls.Add(brandArea);titleBar.Controls.Add(windowActions);
            brandArea.Controls.Add(mark);brandArea.Controls.Add(brand);brandArea.MouseDown+=DragWindow;
            applicationMenu.MouseDown+=DragWindow;mark.MouseDown+=DragWindow;brand.MouseDown+=DragWindow;
            windowMinimize=new WindowControlButton {Action="minimize",BackColor=Ink,Size=new Size(44,36)};windowMinimize.AccessibleName="Свернуть окно";
            windowMaximize=new WindowControlButton {Action="maximize",BackColor=Ink,Size=new Size(44,36)};windowMaximize.AccessibleName="Развернуть или восстановить окно";
            windowClose=new WindowControlButton {Action="close",BackColor=Ink,Size=new Size(44,36)};windowClose.AccessibleName="Закрыть окно";
            foreach(Button button in new[]{windowMinimize,windowMaximize,windowClose})button.Font=new Font("Segoe UI",13F);
            windowMinimize.Click+=delegate{WindowState=FormWindowState.Minimized;};windowMaximize.Click+=delegate{ToggleMaximized();};windowClose.Click+=delegate{Close();};
            windowActions.Controls.AddRange(new Control[]{windowMinimize,windowMaximize,windowClose});
            windowMinimize.Location=new Point(0,2);windowMaximize.Location=new Point(44,2);windowClose.Location=new Point(88,2);
            Resize+=delegate{((WindowControlButton)windowMaximize).Restore=WindowState==FormWindowState.Maximized;windowMaximize.Invalidate();};
            applicationMenu.MenuActivate+=delegate{menuTextTarget=FocusedText(this);};
            var file=new ToolStripMenuItem("Файл");var edit=new ToolStripMenuItem("Правка");var view=new ToolStripMenuItem("Вид");var help=new ToolStripMenuItem("Справка");
            applicationMenu.Items.AddRange(new ToolStripItem[]{file,edit,view,help});
            AddCommand(file,"Новый чат",Keys.Control|Keys.N,CreateChat);
            AddCommand(file,"Добавить проект…",Keys.Control|Keys.Shift|Keys.O,AddProject);

            AddCommand(file,"Добавить файлы…",Keys.Control|Keys.O,()=>AddFiles(false));
            file.DropDownItems.Add(new ToolStripSeparator());
            AddCommand(file,"Экспортировать чат…",Keys.Control|Keys.Shift|Keys.E,()=>ExportChat(EnsureActiveChat()));
            AddCommand(file,"Настройки…",Keys.Control|Keys.Oemcomma,OpenSettings);
            file.DropDownItems.Add(new ToolStripSeparator());
            AddCommand(file,"Закрыть окно",Keys.Alt|Keys.F4,Close);
            AddCommand(edit,"Отменить",Keys.Control|Keys.Z,()=>EditText("undo"));
            edit.DropDownItems.Add(new ToolStripSeparator());
            AddCommand(edit,"Вырезать",Keys.Control|Keys.X,()=>EditText("cut"));
            AddCommand(edit,"Копировать",Keys.Control|Keys.C,()=>EditText("copy"));
            AddCommand(edit,"Вставить",Keys.Control|Keys.V,()=>EditText("paste"));
            AddCommand(edit,"Удалить",Keys.None,()=>EditText("delete"));
            AddCommand(edit,"Выделить всё",Keys.Control|Keys.A,()=>EditText("all"));
            edit.DropDownItems.Add(new ToolStripSeparator());
            AddCommand(edit,"Поиск чатов",Keys.Control|Keys.K,()=>{sidebar.Visible=true;LayoutWorkspace();searchBox.Focus();});
            var side=AddCommand(view,"Боковая панель",Keys.Control|Keys.B,()=>{sidebarToggle.PerformClick();});
            var results=AddCommand(view,"Результаты и источники",Keys.Control|Keys.Shift|Keys.R,ToggleResults);
            var capabilities=AddCommand(view,"Возможности",Keys.None,ToggleCapabilities);
            view.DropDownItems.Add(new ToolStripSeparator());
            var full=AddCommand(view,"Полноэкранный режим",Keys.F11,ToggleFullScreen);
            view.DropDownOpening+=delegate{side.Checked=sidebar.Visible;results.Checked=resultsRequested;capabilities.Checked=rightRail.Visible;full.Checked=fullScreen;};
            AddCommand(help,"Горячие клавиши",Keys.F1,()=>MuseDialog.Show(this,"Ctrl+N — новый чат\nCtrl+Shift+O — добавить проект\nCtrl+O — добавить файлы\nCtrl+K — поиск чатов\nCtrl+B — боковая панель\nCtrl+Shift+R — результаты\nCtrl+, — настройки\nF11 — полный экран\nEnter — отправить промпт\nShift+Enter — новая строка\nEsc — остановить ответ","Горячие клавиши Muse Desk",MessageBoxButtons.OK,MessageBoxIcon.Information));
            AddCommand(help,"Открыть папку журналов",Keys.None,()=>{string path=System.IO.Path.Combine(ProjectRoot(),"runtime");System.IO.Directory.CreateDirectory(path);System.Diagnostics.Process.Start("explorer.exe","\""+path+"\"");});
            help.DropDownItems.Add(new ToolStripSeparator());
            AddCommand(help,"О программе Muse Desk",Keys.None,()=>MuseDialog.Show(this,"Muse Desk "+System.Reflection.Assembly.GetExecutingAssembly().GetName().Version+"\nЛокальный помощник: проекты, чаты и модели на вашем компьютере.","О программе",MessageBoxButtons.OK,MessageBoxIcon.Information));
            StyleMenu(applicationMenu);MainMenuStrip=applicationMenu;Controls.Add(titleBar);
        }

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
                if(messageList!=null)messageList.Padding=new Padding(left,24,Math.Max(0,messageList.ClientSize.Width-left-column),56);
                if(activeTitle!=null){activeTitle.Location=new Point(60,13);activeTitle.Size=new Size(Math.Max(100,center.Width-130),23);}
                if(statusLine!=null)statusLine.Visible=false;
                if(detailsButton!=null)detailsButton.Location=new Point(center.Width-detailsButton.Width-18,8);
                if(sidebarToggle!=null)
                {
                    Control parent=sidebar.Visible?(Control)sidebar:header;
                    if(sidebarToggle.Parent!=parent)parent.Controls.Add(sidebarToggle);
                    sidebarToggle.Location=sidebar.Visible?new Point(211,8):new Point(8,8);sidebarToggle.BackColor=sidebar.Visible?Ink:Surface;
                    Control[] folders=header.Controls.Find("HeaderFolder",false);if(folders.Length>0)folders[0].Visible=sidebar.Visible;
                }
                LayoutResults();
            }
            finally{layingOutWorkspace=false;}
        }

        private void BuildSidebar()
        {
            RoundedButton create=(RoundedButton)MakeButton("Новый чат",Ink,TextInk,190,36);create.IconName="edit";create.TextAlign=ContentAlignment.MiddleLeft;create.Radius=8;create.Location=new Point(16,6);create.Click+=delegate{CreateChat();};sidebar.Controls.Add(create);
            RoundedButton capabilities=(RoundedButton)MakeButton("Возможности",Ink,TextInk,222,36);capabilities.IconName="sliders";capabilities.TextAlign=ContentAlignment.MiddleLeft;capabilities.Location=new Point(16,46);capabilities.Click+=delegate{ToggleCapabilities();};sidebar.Controls.Add(capabilities);
            RoundedButton documents=(RoundedButton)MakeButton("Добавить файлы",Ink,TextInk,222,36);documents.IconName="file";documents.TextAlign=ContentAlignment.MiddleLeft;documents.Location=new Point(16,86);documents.Click+=delegate{AddFiles(false);};sidebar.Controls.Add(documents);
            RoundedComposerPanel search=new RoundedComposerPanel {Location=new Point(16,139),Size=new Size(222,36),Radius=8,BackColor=Ink,BorderColor=Color.FromArgb(221,221,221)};
            search.Paint+=delegate(object s,PaintEventArgs e){MuseIcons.Draw(e.Graphics,"search",new RectangleF(10,9,17,17),Muted);};
            searchBox=new TextBox {AccessibleName="Поиск чатов",BorderStyle=BorderStyle.None,BackColor=Ink,ForeColor=TextInk,Font=InterfaceTypography.Sidebar(),Location=new Point(35,8),Size=new Size(176,22)};
            search.Controls.Add(searchBox);sidebar.Controls.Add(search);
            NativeMethods.SendMessage(searchBox.Handle,0x1501,new IntPtr(1),"Поиск чатов");
            searchBox.TextChanged+=delegate{RefreshChatList();};
            RoundedButton project=(RoundedButton)MakeButton("Добавить проект",Ink,Muted,222,30);project.IconName="add";project.TextAlign=ContentAlignment.MiddleLeft;project.Font=new Font("Segoe UI",8.5F);project.Location=new Point(16,189);project.Click+=delegate{AddProject();};sidebar.Controls.Add(project);
            chatList=new ModernFlowPanel {Location=new Point(12,224),Width=234,Height=330,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true,BackColor=Ink};sidebar.Controls.Add(chatList);InstallScrollBar(chatList,sidebar);
            settingsButton=MakeButton("Настройки",Ink,TextInk,222,36);settingsButton.TextAlign=ContentAlignment.MiddleLeft;((RoundedButton)settingsButton).IconName="sliders";settingsButton.Click+=delegate{OpenSettings();};sidebar.Controls.Add(settingsButton);
            Panel status=new Panel {Size=new Size(222,56),BackColor=Ink};
            connectionDot=new ModelStatusIndicator {Location=new Point(9,3),Size=new Size(18,18),BackColor=status.BackColor,ForeColor=Warning,Busy=true};
            connectionTitle=new Label {Text="Ожидание",ForeColor=TextInk,Font=new Font("Segoe UI",8.5F),Location=new Point(29,6),Size=new Size(182,22),AutoEllipsis=true};
            connectionDetail=new Label {Text="Muse Glimmer · локально",ForeColor=Muted,Font=new Font("Segoe UI",8F),Location=new Point(29,28),Size=new Size(185,20),AutoEllipsis=true};
            status.Controls.AddRange(new Control[]{connectionDot,connectionTitle,connectionDetail});sidebar.Controls.Add(status);
            foreach(Control statusPart in new Control[]{status,connectionDot,connectionTitle,connectionDetail})
            {statusPart.Cursor=Cursors.Hand;statusPart.Click+=async delegate{if(!modelTransition&&generationCancellation==null)await LoadSelectedFromUiAsync();};}
            Action layout=delegate{int h=sidebar.ClientSize.Height;chatList.Height=Math.Max(80,h-224-136);settingsButton.Location=new Point(16,h-62);status.Location=new Point(16,h-123);};sidebar.Resize+=delegate{layout();};layout();
            foreach(RoundedButton action in sidebar.Controls.OfType<RoundedButton>())
            {
                action.Font=action==project?InterfaceTypography.Caption():InterfaceTypography.Sidebar();
                action.IconTint=InterfaceTypography.Secondary;
                action.GrayscaleText=true;
            }
        }

        private void BuildHeader()
        {
            activeTitle=new Label {Text="Новый диалог",AutoEllipsis=true,ForeColor=InterfaceTypography.Secondary,Font=InterfaceTypography.Sidebar()};
            statusLine=new Label {Text="Muse Glimmer · локальная модель",AutoEllipsis=true,ForeColor=Muted,Font=new Font("Segoe UI",8F)};
            sidebarToggle=MakeButton("Боковая панель",Surface,Muted,32,32);((RoundedButton)sidebarToggle).IconName="sidebar";((RoundedButton)sidebarToggle).IconOnly=true;sidebarToggle.Location=new Point(16,14);sidebarToggle.Click+=delegate{sidebar.Visible=!sidebar.Visible;LayoutWorkspace();RenderConversation();};tips.SetToolTip(sidebarToggle,"Показать или скрыть чаты");
            sidebarToggle.Top=8;
            sidebarToggle.Location=new Point(211,8);sidebarToggle.BackColor=Ink;sidebar.Controls.Add(sidebarToggle);
            detailsButton=MakeButton("Возможности",Surface,Muted,32,32);((RoundedButton)detailsButton).IconName="sliders";((RoundedButton)detailsButton).IconOnly=true;tips.SetToolTip(detailsButton,"Возможности Muse");
            detailsButton.Click+=delegate{ToggleCapabilities();};
            header.Controls.AddRange(new Control[]{activeTitle,statusLine,detailsButton});
            header.Resize+=delegate{LayoutWorkspace();};
            RoundedComposerPanel modelFrame=new RoundedComposerPanel {Location=new Point(20,512),Size=new Size(220,36),Radius=10,BackColor=Surface,BorderColor=Line};
            modelCombo=new ComboBox {AccessibleName="Модель",DropDownStyle=ComboBoxStyle.DropDownList,Width=200,Font=new Font("Segoe UI",9F),Location=new Point(10,7),FlatStyle=FlatStyle.Flat,BackColor=Surface,DrawMode=DrawMode.OwnerDrawFixed,ItemHeight=20,DropDownWidth=340};
            modelCombo.DrawItem+=delegate(object s,DrawItemEventArgs e){if(e.Index<0)return;e.DrawBackground();string name=modelCombo.Items[e.Index].ToString();if(name==PreferredModel)name="Muse Glimmer · Heretic";TextRenderer.DrawText(e.Graphics,name,modelCombo.Font,e.Bounds,e.ForeColor,TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);};
            modelCombo.SelectedIndexChanged+=async delegate{if(!refreshingModels&&modelCombo.SelectedItem!=null){state.settings.model=modelCombo.SelectedItem.ToString();modelAvailable=true;ModelStatus("Модель не готова",SelectedModelLabel()+" · подготовка",true,false);SaveState();UpdateCapabilityUi();if(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MUSE_DESK_TEST_DATA")))await LoadSelectedFromUiAsync();}};modelFrame.Controls.Add(modelCombo);rightRail.Controls.Add(modelFrame);
        }

        private void BuildRightRail()
        {
            rightRail.BackColor=Ink;rightRail.AutoScrollMinSize=new Size(0,720);
            rightRail.Controls.Add(new Label {Text="Возможности",Font=new Font("Segoe UI",16F),ForeColor=TextInk,AutoSize=true,Location=new Point(20,25)});
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
            card.Controls.Add(new Label {Text=title,Font=new Font("Segoe UI",9.5F),ForeColor=TextInk,Location=new Point(48,10),Size=new Size(158,22)});
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
                card.Click+=delegate{if(!modelReady)return;if(selected==1)AddFiles(false);else if(selected==2)AddFiles(true);input.Text=selected==0?"Помоги развить идею: ":selected==1?"Изучи документ и выдели главное.":"Опиши изображение и важные детали.";input.Focus();input.SelectionStart=input.TextLength;};empty.Controls.Add(card);
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
            if(generationCancellation!=null||modelTransition)return;
            ContextMenuStrip menu=new ContextMenuStrip();
            foreach(object entry in modelCombo.Items){string name=entry.ToString();ToolStripMenuItem item=new ToolStripMenuItem(name==PreferredModel?"Muse Glimmer · Heretic":name);item.Checked=name==state.settings.model;item.Click+=async delegate{if(modelCombo.SelectedItem as string==name)await LoadSelectedFromUiAsync();else modelCombo.SelectedItem=name;UpdateCapabilityUi();};menu.Items.Add(item);}
            if(menu.Items.Count==0)menu.Items.Add("Модели ещё не загружены").Enabled=false;
            ShowTransientMenu(menu,composerModelButton,new Point(0,composerModelButton.Height));
        }

        private readonly HashSet<ContextMenuStrip> ownedMenus=new HashSet<ContextMenuStrip>();
        private readonly HashSet<ContextMenuStrip> pendingMenuDisposal=new HashSet<ContextMenuStrip>();

        private void OwnMenu(ContextMenuStrip menu,Control owner,bool transient)
        {
            StyleMenu(menu);
            ownedMenus.Add(menu);
            EventHandler ownerDisposed=null;
            if(owner!=null)
            {
                ownerDisposed=delegate {QueueMenuDisposal(menu);};
                owner.Disposed+=ownerDisposed;
            }
            menu.Disposed+=delegate
            {
                ownedMenus.Remove(menu);pendingMenuDisposal.Remove(menu);
                if(owner!=null)owner.Disposed-=ownerDisposed;
            };
            if(transient)menu.Closed+=delegate {QueueMenuDisposal(menu);};
        }

        private void ShowTransientMenu(ContextMenuStrip menu,Control owner,Point location)
        {
            OwnMenu(menu,owner,true);
            if(isClosing || IsDisposed || owner.IsDisposed){QueueMenuDisposal(menu);return;}
            menu.Show(owner,location);
        }

        private void QueueMenuDisposal(ContextMenuStrip menu)
        {
            if(menu.IsDisposed || !pendingMenuDisposal.Add(menu))return;
            // Closed runs inside ToolStripDropDown.SetVisibleCore. Disposing there
            // destroys the handle before WinForms finishes reparenting the menu.
            // Dispatch on the form, not the source button (chat rows are rebuilt).
            if(IsHandleCreated && !IsDisposed && !Disposing && !isClosing)
                BeginInvoke((MethodInvoker)delegate
                {
                    pendingMenuDisposal.Remove(menu);
                    if(!menu.IsDisposed)menu.Dispose();
                });
            // During shutdown the form's Disposed handler owns final cleanup.
        }

        private void DisposeOwnedMenus()
        {
            foreach(ContextMenuStrip menu in ownedMenus.ToArray())menu.Dispose();
            pendingMenuDisposal.Clear();
        }

        private static void FitRichText(RichTextBox box)
        {
            ((TranscriptTextBox)box).FitToText();
        }
    }
}
