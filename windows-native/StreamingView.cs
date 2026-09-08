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
            internal Panel Actions;
            internal int ActionVersion=-1;
        }
        private System.Windows.Forms.Timer streamAnimation;
        private bool followResponseTail=true;
        private int scrollAnimationTarget=-1;

        private Control BuildStreamingRow(ChatMessage message,int width)
        {
            Panel row=new Panel {Width=width,Height=62,BackColor=Canvas,Margin=new Padding(0,0,0,20)};
            Label status=new Label {Text="Muse отвечает…",Font=new Font("Segoe UI",9F),ForeColor=Muted,Location=new Point(18,8),Size=new Size(width-36,22)};
            RichTextBox body=MakeReadableText("",width-36,new Font("Segoe UI",10.5F),Surface,TextInk);
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

        private void LayoutStreamRow(StreamView view)
        {
            int top=36;
            if(view.Actions!=null && view.Actions.Height>0){view.Actions.Location=new Point(18,top);top=view.Actions.Bottom+12;}
            view.Body.Top=top;view.Copy.Location=new Point(14,view.Body.Bottom+8);
            view.Row.Height=Math.Max(62,view.Body.Visible?view.Copy.Bottom+8:top);

        }

        private void UpdateStreamActions(StreamView view)
        {
            int version=ActionLogVersion(view.Message);
            if(version==view.ActionVersion)return;
            view.ActionVersion=version;
            if(view.Actions!=null){view.Row.Controls.Remove(view.Actions);DisposeTree(view.Actions);view.Actions=null;}
            if(ActionEntries(view.Message).Count>0)
            {
                view.Actions=BuildActionLog(view.Message,view.Row.Width-36,delegate{LayoutStreamRow(view);});
                view.Row.Controls.Add(view.Actions);
            }
            LayoutStreamRow(view);
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
                UpdateStreamActions(view);
                if(target==view.Displayed)continue;
                if(!target.StartsWith(view.Displayed,StringComparison.Ordinal)){view.Displayed="";view.Body.Clear();}
                int backlog=target.Length-view.Displayed.Length;
                int count=SystemInformation.IsMenuAnimationEnabled?Math.Min(96,Math.Max(2,(backlog+7)/8)):backlog;
                int end=NextVisibleBoundary(target,view.Displayed.Length,count);
                if(end<=view.Displayed.Length)continue;
                string delta=target.Substring(view.Displayed.Length,end-view.Displayed.Length);
                int selection=view.Body.SelectionStart,length=view.Body.SelectionLength;
                view.Body.AppendText(delta);view.Displayed=target.Substring(0,end);
                FitRichText(view.Body);
                view.Body.Visible=true;view.Body.Select(Math.Min(selection,view.Body.TextLength),Math.Min(length,Math.Max(0,view.Body.TextLength-selection)));
                view.Copy.Visible=target.Length>0;view.Copy.Location=new Point(14,view.Body.Bottom+8);LayoutStreamRow(view);
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
