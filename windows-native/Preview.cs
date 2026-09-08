using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Linq;

namespace MuseDeskNative
{
    // Render our own test form, including RichEdit's native print support.
    // No screen capture, external windows, or user history are accessed.
    internal static class DesignPreview
    {
        [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left,Top,Right,Bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct Range { public int Min,Max; }
        [StructLayout(LayoutKind.Sequential)] private struct FormatRange { public IntPtr Hdc,Target; public Rect Area,Page; public Range Chars; }
        [DllImport("user32.dll",CharSet=CharSet.Auto)] private static extern IntPtr SendMessage(IntPtr hwnd,int message,IntPtr parameter,IntPtr data);
        internal static void Capture(Form form,string path)
        {
            Application.DoEvents();
            using(Bitmap bitmap=new Bitmap(form.Width,form.Height))
            using(Graphics graphics=Graphics.FromImage(bitmap))
            {
                form.DrawToBitmap(bitmap,new Rectangle(0,0,bitmap.Width,bitmap.Height));
                PaintText(form,form,graphics,new Rectangle(0,0,bitmap.Width,bitmap.Height));
                bitmap.Save(path,System.Drawing.Imaging.ImageFormat.Png);
            }
        }
        private static void PaintText(Form form,Control parent,Graphics target,Rectangle inheritedClip)
        {
            foreach(Control control in parent.Controls.Cast<Control>().Reverse())
            {
                if(!control.Visible)continue;
                Point location=control.PointToScreen(Point.Empty);location.Offset(-form.Left,-form.Top);
                Rectangle bounds=new Rectangle(location,control.ClientSize);
                Rectangle clip=Rectangle.Intersect(inheritedClip,bounds);if(clip.Width<=0||clip.Height<=0)continue;
                RichTextBox rich=control as RichTextBox;
                if(rich!=null)
                {
                    using(Bitmap textImage=new Bitmap(Math.Max(1,rich.Width),Math.Max(1,rich.Height)))
                    using(Graphics g=Graphics.FromImage(textImage))
                    {
                        g.Clear(rich.BackColor);float dpiX=g.DpiX,dpiY=g.DpiY;IntPtr hdc=g.GetHdc();
                        FormatRange format=new FormatRange {Hdc=hdc,Target=hdc,Area=new Rect {Right=(int)(rich.Width*1440D/dpiX),Bottom=(int)(rich.Height*1440D/dpiY)},Chars=new Range {Min=0,Max=-1}};format.Page=format.Area;
                        IntPtr memory=Marshal.AllocCoTaskMem(Marshal.SizeOf(typeof(FormatRange)));
                        try{Marshal.StructureToPtr(format,memory,false);SendMessage(rich.Handle,0x0439,new IntPtr(1),memory);}
                        finally{SendMessage(rich.Handle,0x0439,IntPtr.Zero,IntPtr.Zero);Marshal.FreeCoTaskMem(memory);g.ReleaseHdc(hdc);}
                        System.Drawing.Drawing2D.GraphicsState saved=target.Save();target.SetClip(clip);target.DrawImageUnscaled(textImage,location);target.Restore(saved);
                    }
                }
                TextBox edit=control as TextBox;
                if(edit!=null)
                {
                    string content=edit.Text;
                    Label cue=parent.Controls.OfType<Label>().FirstOrDefault(l=>l.Visible);
                    if(content.Length==0&&cue!=null)content=cue.Text;
                    if(content.Length>0)
                    {
                        System.Drawing.Drawing2D.GraphicsState saved=target.Save();target.SetClip(clip);
                        TextRenderer.DrawText(target,content,edit.Font,bounds,edit.TextLength==0&&cue!=null?cue.ForeColor:edit.ForeColor,edit.BackColor,TextFormatFlags.WordBreak|TextFormatFlags.NoPadding|TextFormatFlags.NoPrefix);target.Restore(saved);
                    }
                }
                PaintText(form,control,target,clip);
            }
        }
    }
}
