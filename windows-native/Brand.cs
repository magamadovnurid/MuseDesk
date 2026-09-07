using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace MuseDeskNative
{
    // One vector master for the window, welcome screen and every Windows icon size.
    internal static class MuseBrand
    {
        internal const string MonogramPath = "M 25 71 L 25 34 C 25 28 31 26 35 31 L 50 51 L 65 31 C 69 26 75 28 75 34 L 75 71";
        internal static void Draw(Graphics graphics, RectangleF bounds, bool tile)
        {
            if(bounds.Width<=0 || bounds.Height<=0)return;
            GraphicsState saved=graphics.Save();
            try
            {
                float size=Math.Min(bounds.Width,bounds.Height);
                graphics.TranslateTransform(bounds.X+(bounds.Width-size)/2,bounds.Y+(bounds.Height-size)/2);
                graphics.ScaleTransform(size/100F,size/100F);
                graphics.SmoothingMode=SmoothingMode.AntiAlias;
                if(tile)
                {
                    using(GraphicsPath shape=new GraphicsPath())
                    {
                        shape.AddArc(2,2,46,46,180,90);shape.AddArc(52,2,46,46,270,90);
                        shape.AddArc(52,52,46,46,0,90);shape.AddArc(2,52,46,46,90,90);shape.CloseFigure();
                        using(LinearGradientBrush fill=new LinearGradientBrush(new RectangleF(0,0,100,100),Color.FromArgb(55,57,64),Color.FromArgb(20,21,25),65F))graphics.FillPath(fill,shape);
                        using(Pen edge=new Pen(Color.FromArgb(65,255,255,255),0.6F))graphics.DrawPath(edge,shape);
                    }
                }
                else
                {
                    graphics.TranslateTransform(-15,-15);graphics.ScaleTransform(1.3F,1.3F);
                }
                using(GraphicsPath glyph=new GraphicsPath())
                using(Pen stroke=new Pen(tile?Color.FromArgb(250,250,252):Color.FromArgb(35,35,35),12F))
                {
                    stroke.StartCap=stroke.EndCap=LineCap.Round;stroke.LineJoin=LineJoin.Round;
                    glyph.AddLine(25,71,25,34);glyph.AddBezier(25,34,25,28,31,26,35,31);
                    glyph.AddLine(35,31,50,51);glyph.AddLine(50,51,65,31);
                    glyph.AddBezier(65,31,69,26,75,28,75,34);glyph.AddLine(75,34,75,71);
                    graphics.DrawPath(stroke,glyph);
                }
            }
            finally { graphics.Restore(saved); }
        }

        internal static Bitmap Render(int size,bool tile)
        {
            using(Bitmap master=new Bitmap(size*4,size*4,PixelFormat.Format32bppArgb))
            {
                using(Graphics g=Graphics.FromImage(master)) { g.Clear(Color.Transparent);Draw(g,new RectangleF(0,0,master.Width,master.Height),tile); }
                Bitmap result=new Bitmap(size,size,PixelFormat.Format32bppArgb);
                using(Graphics g=Graphics.FromImage(result))
                using(ImageAttributes attributes=new ImageAttributes())
                {
                    g.CompositingMode=CompositingMode.SourceCopy;g.InterpolationMode=InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode=PixelOffsetMode.HighQuality;attributes.SetWrapMode(WrapMode.TileFlipXY);
                    g.DrawImage(master,new Rectangle(0,0,size,size),0,0,master.Width,master.Height,GraphicsUnit.Pixel,attributes);
                }
                return result;
            }
        }
    }
}
