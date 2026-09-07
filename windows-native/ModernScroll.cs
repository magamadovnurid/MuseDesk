using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MuseDeskNative
{
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

    internal sealed class ThinScrollBar : Control
    {
        private readonly ModernFlowPanel owner;
        private bool dragging,hover;
        private int dragY,dragOffset;
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
        internal Rectangle Thumb
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

    public sealed partial class MainForm
    {
        private static void InstallScrollBar(FlowLayoutPanel panel,Control parent)
        {
            ModernFlowPanel modern=panel as ModernFlowPanel;if(modern==null)return;
            parent.Controls.Add(new ThinScrollBar(modern));
        }
    }
}
