using System;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace MuseDeskNative
{
    public sealed partial class MainForm
    {
        private sealed class StreamView
        {
            internal ChatMessage Message;
            internal Panel Row;
            internal RichTextBox Body;
            internal Label Status;
            internal RoundedButton Copy;
            internal string Displayed="";
        }
        private System.Windows.Forms.Timer streamAnimation;
        private bool followResponseTail=true;
        private int scrollAnimationTarget=-1;

        private Control BuildStreamingRow(ChatMessage message,int width)
        {
            Panel row=new Panel {Width=width,Height=62,BackColor=Canvas,Margin=new Padding(0,0,0,20)};
            Label status=new Label {Text="Muse отвечает…",Font=new Font("Segoe UI",9F),ForeColor=Muted,Location=new Point(18,8),Size=new Size(width-36,22)};
            RichTextBox body=MakeReadableText("",width-36,new Font("Segoe UI",10.5F),Surface,TextInk,1600);
            body.AccessibleName="Ответ Muse";body.Location=new Point(18,36);body.Height=24;body.Visible=false;
            RoundedButton copy=MakeCopyButton(()=>message.content,"Скопировать весь ответ",Surface);copy.Location=new Point(14,body.Bottom+8);copy.Visible=!string.IsNullOrEmpty(message.content);
            StreamView view=new StreamView {Message=message,Row=row,Body=body,Status=status,Copy=copy};row.Tag=view;row.Controls.AddRange(new Control[]{status,body,copy});
            EnsureStreamAnimation();return row;
        }

        private void EnsureStreamAnimation()
        {
            if(streamAnimation==null)
            {
                streamAnimation=new System.Windows.Forms.Timer {Interval=33};
                streamAnimation.Tick+=delegate{AnimateStreamFrame();};
            }
            streamAnimation.Start();
        }

        internal static int NextVisibleBoundary(string text,int current,int budget)
        {
            int end=current;
            while(end<text.Length && end-current<budget)
            {
                if(char.IsHighSurrogate(text[end]) && end+1==text.Length)break;
                end+=StringInfo.GetNextTextElement(text,end).Length;
            }
            return end;
        }

        private void AnimateStreamFrame()
        {
            if(isClosing || messageList==null || messageList.IsDisposed){if(streamAnimation!=null)streamAnimation.Stop();return;}
            bool active=false;
            messageList.SuspendLayout();
            foreach(Control row in messageList.Controls)
            {
                StreamView view=row.Tag as StreamView;if(view==null)continue;
                active=true;string target=view.Message.content??"";
                view.Status.Text=target.Length>0?"Muse отвечает…":!string.IsNullOrEmpty(view.Message.thinking)?"Muse рассуждает…":"Muse готовит ответ…";
                if(!string.IsNullOrWhiteSpace(view.Message.contextNotice))view.Status.Text=view.Message.contextNotice;
                if(target==view.Displayed)continue;
                if(!target.StartsWith(view.Displayed,StringComparison.Ordinal)){view.Displayed="";view.Body.Clear();}
                int backlog=target.Length-view.Displayed.Length;
                int count=SystemInformation.IsMenuAnimationEnabled?Math.Min(96,Math.Max(2,(backlog+7)/8)):backlog;
                int end=NextVisibleBoundary(target,view.Displayed.Length,count);
                if(end<=view.Displayed.Length)continue;
                string delta=target.Substring(view.Displayed.Length,end-view.Displayed.Length);
                int selection=view.Body.SelectionStart,length=view.Body.SelectionLength;
                view.Body.AppendText(delta);view.Displayed=target.Substring(0,end);
                int lines=Math.Max(1,NativeMethods.SendMessage(view.Body.Handle,0x00BA,IntPtr.Zero,null).ToInt32());
                view.Body.Height=Math.Min(1600,lines*view.Body.Font.Height+12);
                view.Body.ScrollBars=lines*view.Body.Font.Height+12>1600?RichTextBoxScrollBars.Vertical:RichTextBoxScrollBars.None;
                view.Body.Visible=true;view.Body.Select(Math.Min(selection,view.Body.TextLength),Math.Min(length,Math.Max(0,view.Body.TextLength-selection)));
                view.Copy.Visible=target.Length>0;view.Copy.Location=new Point(14,view.Body.Bottom+8);row.Height=view.Copy.Bottom+8;
            }
            messageList.ResumeLayout(true);
            if(active && followResponseTail)scrollAnimationTarget=Math.Max(0,messageList.DisplayRectangle.Height-messageList.ClientSize.Height);
            if(scrollAnimationTarget>=0)
            {
                int current=-messageList.AutoScrollPosition.Y,difference=scrollAnimationTarget-current;
                if(Math.Abs(difference)<2){((ModernFlowPanel)messageList).ScrollTo(scrollAnimationTarget);scrollAnimationTarget=-1;}
                else ((ModernFlowPanel)messageList).ScrollTo(current+(SystemInformation.IsMenuAnimationEnabled?(int)Math.Ceiling(Math.Abs(difference)*.28)*Math.Sign(difference):difference));
            }
            if(!active && scrollAnimationTarget<0 && streamAnimation!=null)streamAnimation.Stop();
        }
    }
}
