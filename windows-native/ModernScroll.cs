using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace MuseDeskNative
{
    // Rich Edit reports its full laid-out height as a 32-bit rectangle. Measuring
    // the final character alone can leave a capped, independently scrolling box.
    internal sealed class TranscriptTextBox : RichTextBox
    {
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window,int message,IntPtr wParam,IntPtr lParam);
        private int textHeight;
        protected override void OnContentsResized(ContentsResizedEventArgs e){textHeight=e.NewRectangle.Height;base.OnContentsResized(e);}
        internal void FitToText()
        {
            ScrollBars=RichTextBoxScrollBars.None;
            SendMessage(Handle,0x441,IntPtr.Zero,IntPtr.Zero); // EM_REQUESTRESIZE
            Height=Math.Max(Font.Height+12,textHeight+12);
        }
    }

    internal class ModernFlowPanel : FlowLayoutPanel
    {
        [DllImport("user32.dll")] private static extern bool ShowScrollBar(IntPtr window,int bar,bool show);
        private bool hiding;
        internal event EventHandler ViewChanged;
        internal event EventHandler UserScrolled;
        public ModernFlowPanel(){DoubleBuffered=true;}
        private void HideNativeBars()
        {
            if(hiding || !IsHandleCreated)return;
            hiding=true;try{ShowScrollBar(Handle,3,false);}finally{hiding=false;}
        }
        protected override void OnLayout(LayoutEventArgs e){base.OnLayout(e);HideNativeBars();if(ViewChanged!=null)ViewChanged(this,EventArgs.Empty);}
        protected override void OnScroll(ScrollEventArgs e){base.OnScroll(e);HideNativeBars();if(ViewChanged!=null)ViewChanged(this,EventArgs.Empty);}
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            ScrollWheel(e.Delta);
            HandledMouseEventArgs handled=e as HandledMouseEventArgs;if(handled!=null)handled.Handled=true;
        }
        internal void ScrollWheel(int delta)
        {
            int step=SystemInformation.MouseWheelScrollLines;
            int distance=step<0?ClientSize.Height:Math.Max(1,step)*24;
            UserScrollTo(-AutoScrollPosition.Y-delta*distance/120);
        }
        internal int MaximumOffset {get{return Math.Max(0,DisplayRectangle.Height-ClientSize.Height);}}
        internal void UserScrollTo(int offset){ScrollTo(offset);if(UserScrolled!=null)UserScrolled(this,EventArgs.Empty);}
        internal void ScrollTo(int offset)
        {
            AutoScrollPosition=new Point(0,Math.Max(0,Math.Min(MaximumOffset,offset)));
            HideNativeBars();if(ViewChanged!=null)ViewChanged(this,EventArgs.Empty);
        }
    }

    internal class ThinScrollBar : Control
    {
        private readonly ModernFlowPanel owner;
        private bool dragging,hover;
        private int dragY,dragOffset;
        internal ModernFlowPanel ScrollOwner {get{return owner;}}
        internal ThinScrollBar(ModernFlowPanel panel)
        {
            owner=panel;Width=10;TabStop=true;AccessibleName="Прокрутка";AccessibleRole=AccessibleRole.ScrollBar;Cursor=Cursors.Default;
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
            owner.ViewChanged+=UpdateView;owner.VisibleChanged+=UpdateView;owner.LocationChanged+=UpdateView;owner.SizeChanged+=UpdateView;
        }
        private void UpdateView(object sender,EventArgs e)
        {
            if(IsDisposed || owner.IsDisposed)return;
            Bounds=new Rectangle(owner.Right-Width,owner.Top,Width,owner.Height);
            BackColor=owner.BackColor;Visible=owner.Visible && owner.MaximumOffset>0;
            if(Visible)BringToFront();Invalidate();
        }
        protected override void OnParentChanged(EventArgs e){base.OnParentChanged(e);if(owner!=null)UpdateView(this,EventArgs.Empty);}
        protected override void OnMouseWheel(MouseEventArgs e)
        {base.OnMouseWheel(e);owner.ScrollWheel(e.Delta);var handled=e as HandledMouseEventArgs;if(handled!=null)handled.Handled=true;}
        internal virtual Rectangle Thumb
        {
            get
            {
                int track=Math.Max(1,Height-8),length=Math.Min(track,Math.Max(28,(int)((long)track*owner.ClientSize.Height/Math.Max(1,owner.DisplayRectangle.Height))));
                int top=4+(int)((long)Math.Max(0,-owner.AutoScrollPosition.Y)*(track-length)/Math.Max(1,owner.MaximumOffset));
                return new Rectangle(2,top,hover||dragging?7:5,length);
            }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);if(!Visible || Height<12)return;
            Rectangle thumb=Thumb;e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using(var path=RoundedComposerPanel.RoundedPath(thumb,3))
            using(Brush brush=new SolidBrush(dragging?Color.FromArgb(145,145,145):hover?Color.FromArgb(168,168,168):Color.FromArgb(206,206,206)))e.Graphics.FillPath(brush,path);
            if(Focused&&ShowFocusCues)ControlPaint.DrawFocusRectangle(e.Graphics,ClientRectangle);
        }
        protected override void OnMouseEnter(EventArgs e){base.OnMouseEnter(e);hover=true;Invalidate();}
        protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);hover=false;Invalidate();}
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);if(e.Button!=MouseButtons.Left)return;Focus();
            if(!Thumb.Contains(e.Location))owner.UserScrollTo(-owner.AutoScrollPosition.Y+(e.Y<Thumb.Top?-owner.Height:owner.Height));
            dragging=true;Capture=true;dragY=e.Y;dragOffset=-owner.AutoScrollPosition.Y;Invalidate();
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);if(dragging)owner.UserScrollTo(dragOffset+(int)((long)(e.Y-dragY)*owner.MaximumOffset/Math.Max(1,Height-8-Thumb.Height)));
        }
        protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);dragging=false;Capture=false;Invalidate();}
        protected override void OnMouseCaptureChanged(EventArgs e){base.OnMouseCaptureChanged(e);if(!Capture){dragging=false;Invalidate();}}
        protected override bool IsInputKey(Keys keyData){Keys key=keyData&Keys.KeyCode;return key==Keys.Up||key==Keys.Down||key==Keys.PageUp||key==Keys.PageDown||key==Keys.Home||key==Keys.End||base.IsInputKey(keyData);}
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);int value=-owner.AutoScrollPosition.Y;
            switch(e.KeyCode){case Keys.Up:value-=48;break;case Keys.Down:value+=48;break;case Keys.PageUp:value-=owner.Height;break;case Keys.PageDown:value+=owner.Height;break;case Keys.Home:value=0;break;case Keys.End:value=owner.MaximumOffset;break;default:return;}
            owner.UserScrollTo(value);e.Handled=true;
        }
        protected override void Dispose(bool disposing)
        {
            if(disposing){owner.ViewChanged-=UpdateView;owner.VisibleChanged-=UpdateView;owner.LocationChanged-=UpdateView;owner.SizeChanged-=UpdateView;}
            base.Dispose(disposing);
        }
    }

    internal sealed class ConversationTopic
    {
        internal Control Row;
        internal string Title;
        internal Func<string> Preview;
    }

    internal sealed class ConversationNavigator : Control
    {
        private readonly List<ConversationTopic> topics=new List<ConversationTopic>();
        private readonly ToolTip preview=new ToolTip{OwnerDraw=true,UseAnimation=false,UseFading=false,ShowAlways=true};
        private readonly Timer animation=new Timer{Interval=16};
        private int selected=-1;
        private float[] strengths=new float[0];
        private readonly ModernFlowPanel owner;
        internal ModernFlowPanel ScrollOwner {get{return owner;}}
        private string previewTitle="",previewText="";
        private Size previewSize=new Size(286,100);
        internal int TopicCount {get{return topics.Count;}}
        internal int SelectedTopic {get{return selected;}}
        internal string PreviewTitle {get{return previewTitle;}}
        internal string PreviewText {get{return previewText;}}
        internal ConversationNavigator(ModernFlowPanel panel)
        {
            owner=panel;Width=24;TabStop=true;AccessibleRole=AccessibleRole.List;AccessibleName="Темы диалога. Стрелки — переход между вопросами";
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
            owner.ViewChanged+=UpdatePosition;owner.VisibleChanged+=UpdatePosition;owner.LocationChanged+=UpdatePosition;owner.SizeChanged+=UpdatePosition;
            preview.Popup+=delegate(object sender,PopupEventArgs e){e.ToolTipSize=previewSize;};
            preview.Draw+=delegate(object sender,DrawToolTipEventArgs e)
            {
                e.Graphics.Clear(Color.White);e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using(var path=RoundedComposerPanel.RoundedPath(new Rectangle(1,1,e.Bounds.Width-2,e.Bounds.Height-2),9))
                using(Brush fill=new SolidBrush(Color.FromArgb(250,250,250)))
                using(Pen pen=new Pen(Color.FromArgb(227,227,227))){e.Graphics.FillPath(fill,path);e.Graphics.DrawPath(pen,path);}
                using(Font title=new Font("Segoe UI Semibold",9F))
                using(Font detail=new Font("Segoe UI",9F))
                {
                    TextRenderer.DrawText(e.Graphics,previewTitle,title,new Rectangle(13,11,e.Bounds.Width-26,36),Color.FromArgb(70,70,70),TextFormatFlags.WordBreak|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
                    TextRenderer.DrawText(e.Graphics,previewText,detail,new Rectangle(13,50,e.Bounds.Width-26,40),Color.FromArgb(125,125,125),TextFormatFlags.WordBreak|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
                }
            };
            animation.Tick+=delegate
            {
                bool moving=false;
                for(int i=0;i<strengths.Length;i++){float target=i==selected?1:0;strengths[i]+=(target-strengths[i])*.25F;if(Math.Abs(target-strengths[i])<.025F)strengths[i]=target;else moving=true;}
                if(!moving)animation.Stop();Invalidate();
            };
        }
        private void UpdatePosition(object sender,EventArgs e)
        {
            if(IsDisposed||owner.IsDisposed)return;Bounds=new Rectangle(owner.Left+2,owner.Top,Width,owner.Height);BackColor=owner.BackColor;Visible=owner.Visible&&topics.Count>0;if(Visible)BringToFront();Invalidate();
        }
        protected override void OnParentChanged(EventArgs e){base.OnParentChanged(e);if(owner!=null)UpdatePosition(this,EventArgs.Empty);}
        protected override void OnMouseWheel(MouseEventArgs e){owner.ScrollWheel(e.Delta);var handled=e as HandledMouseEventArgs;if(handled!=null)handled.Handled=true;}
        internal static string Excerpt(string text,int length)
        {
            text=Regex.Replace(text??"","[`#*_]","");text=Regex.Replace(text,"\\s+"," ").Trim();
            if(text.Length<=length)return text;
            int end=length-1;if(end>0&&char.IsHighSurrogate(text[end-1]))end--;
            return text.Substring(0,end).TrimEnd()+"…";
        }
        internal void SetTopics(IEnumerable<ConversationTopic> items)
        {
            List<ConversationTopic> updated=items.ToList();
            bool same=updated.Count==topics.Count && updated.Where((t,i)=>t.Row!=topics[i].Row||t.Title!=topics[i].Title).Count()==0;
            if(!same){preview.Hide(this);selected=-1;animation.Stop();strengths=new float[updated.Count];}
            topics.Clear();topics.AddRange(updated);UpdatePosition(this,EventArgs.Empty);
        }
        private int Offset(int index){return topics[index].Row.Top-ScrollOwner.AutoScrollPosition.Y;}
        internal int TopicY(int index)
        {float span=Math.Min(360,Math.Max(1,Height-40)),step=Math.Min(14,span/Math.Max(1,topics.Count-1));return (int)Math.Round((Height-step*(topics.Count-1))/2+step*index);}
        private int ActiveTopic(){if(owner.MaximumOffset>0 && -owner.AutoScrollPosition.Y>=owner.MaximumOffset-1)return topics.Count-1;int active=0;for(int i=0;i<topics.Count;i++)if(!topics[i].Row.IsDisposed&&topics[i].Row.Top<=24)active=i;return active;}
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);if(!Visible)return;
            e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            int previous=-20,active=ActiveTopic();
            for(int i=0;i<topics.Count;i++)
            {
                int y=TopicY(i);if(y-previous<4 && i!=selected)continue;previous=y;
                float amount=strengths[i];if(selected<0&&i==active)amount=Math.Max(.45F,amount);
                using(Pen pen=new Pen(Color.FromArgb((int)(192-85*amount),(int)(192-85*amount),(int)(192-85*amount)),i==selected?2:1.5F))
                {pen.StartCap=pen.EndCap=System.Drawing.Drawing2D.LineCap.Round;e.Graphics.DrawLine(pen,7,y,12+7*amount,y);}
            }
        }
        internal int HitTopic(Point point)
        {
            if(point.X<0 || point.X>=Width || topics.Count==0)return -1;
            int nearest=-1,distance=8;
            for(int i=0;i<topics.Count;i++){int d=Math.Abs(point.Y-TopicY(i));if(d<distance){nearest=i;distance=d;}}
            return nearest;
        }
        private void SelectTopic(int index,Point point)
        {
            if(index==selected)return;selected=index;preview.Hide(this);
            if(SystemInformation.IsMenuAnimationEnabled)animation.Start();else{for(int i=0;i<strengths.Length;i++)strengths[i]=i==index?1:0;Invalidate();}
            Cursor=index>=0?Cursors.Hand:Cursors.Default;
            if(index<0)return;
            previewTitle=topics[index].Title;previewText=Excerpt(topics[index].Preview(),140);
            if(previewText.Length==0)previewText="Перейти к этому вопросу";
            Point screen=PointToScreen(point);Rectangle bounds=Screen.FromPoint(screen).WorkingArea;
            Point location=new Point(Math.Max(bounds.Left+8,Math.Min(bounds.Right-previewSize.Width-8,screen.X+18)),Math.Max(bounds.Top+8,Math.Min(bounds.Bottom-previewSize.Height-8,screen.Y+14)));
            preview.Show(previewTitle+"\n"+previewText,this,PointToClient(location),15000);
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {base.OnMouseMove(e);SelectTopic(HitTopic(e.Location),e.Location);}
        protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);SelectTopic(-1,Point.Empty);}
        internal void JumpToTopic(int index)
        {if(index<0||index>=topics.Count)return;ScrollOwner.UserScrollTo(Math.Max(0,Offset(index)-12));preview.Hide(this);}
        protected override void OnMouseDown(MouseEventArgs e)
        {base.OnMouseDown(e);if(e.Button==MouseButtons.Left){int index=HitTopic(e.Location);if(index>=0){Focus();JumpToTopic(index);}}}
        protected override bool IsInputKey(Keys keyData){Keys key=keyData&Keys.KeyCode;return key==Keys.Up||key==Keys.Down||key==Keys.Home||key==Keys.End||base.IsInputKey(keyData);}
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if(e.KeyCode==Keys.Up||e.KeyCode==Keys.Down)
            {
                int position=-ScrollOwner.AutoScrollPosition.Y+13,index=-1;
                if(e.KeyCode==Keys.Down){for(int i=0;i<topics.Count;i++)if(Offset(i)>position){index=i;break;}}
                else for(int i=topics.Count-1;i>=0;i--)if(Offset(i)<position-2){index=i;break;}
                JumpToTopic(index);e.Handled=true;e.SuppressKeyPress=true;return;
            }
            if(e.KeyCode==Keys.Home||e.KeyCode==Keys.End){JumpToTopic(e.KeyCode==Keys.Home?0:topics.Count-1);e.Handled=true;return;}
            base.OnKeyDown(e);
        }
        protected override void Dispose(bool disposing){if(disposing){owner.ViewChanged-=UpdatePosition;owner.VisibleChanged-=UpdatePosition;owner.LocationChanged-=UpdatePosition;owner.SizeChanged-=UpdatePosition;preview.Dispose();animation.Dispose();}base.Dispose(disposing);}
    }

    public sealed partial class MainForm
    {
        private ConversationNavigator conversationScroll;
        private void RefreshConversationTopics(ChatSession chat)
        {
            if(conversationScroll==null)return;
            List<ConversationTopic> topics=new List<ConversationTopic>();
            for(int i=0;i<chat.messages.Count;i++)
            {
                ChatMessage question=chat.messages[i];if(question.role!="user")continue;
                Tuple<string,Control> row;if(!renderedRows.TryGetValue(question,out row))continue;
                ChatMessage answer=chat.messages.Skip(i+1).TakeWhile(m=>m.role!="user").LastOrDefault(m=>m.role=="assistant");
                string title=ConversationNavigator.Excerpt(question.content,85);
                topics.Add(new ConversationTopic{Row=row.Item2,Title=title.Length>0?title:"Вопрос с вложением",Preview=()=>answer==null?"Ожидание ответа":string.IsNullOrWhiteSpace(answer.content)?"Ответ формируется…":answer.content});
            }
            conversationScroll.SetTopics(topics);
        }
        private static void InstallScrollBar(FlowLayoutPanel panel,Control parent)
        {
            ModernFlowPanel modern=panel as ModernFlowPanel;if(modern==null)return;
            parent.Controls.Add(new ThinScrollBar(modern));
        }
    }
}
