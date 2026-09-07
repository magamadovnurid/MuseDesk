using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using MuseDeskNative;
internal static class BuildIcon
{
    private static void Main(string[] args)
    {
        int[] sizes={16,20,24,32,40,48,64,128,256};byte[][] images=new byte[sizes.Length][];
        for(int i=0;i<sizes.Length;i++)
        {
            int s=sizes[i];
            using(Bitmap b=MuseBrand.Render(s,true)) using(MemoryStream m=new MemoryStream())
            {
                b.Save(m,ImageFormat.Png);images[i]=m.ToArray();
            }
        }
        using(BinaryWriter w=new BinaryWriter(File.Create(args[0])))
        {
            w.Write((short)0);w.Write((short)1);w.Write((short)sizes.Length);int offset=6+16*sizes.Length;
            for(int i=0;i<sizes.Length;i++){w.Write((byte)(sizes[i]==256?0:sizes[i]));w.Write((byte)(sizes[i]==256?0:sizes[i]));w.Write((short)0);w.Write((short)1);w.Write((short)32);w.Write(images[i].Length);w.Write(offset);offset+=images[i].Length;}
            foreach(byte[] image in images)w.Write(image);
        }
        string folder=Path.GetDirectoryName(Path.GetFullPath(args[0]));
        using(Bitmap icon=MuseBrand.Render(512,true))icon.Save(Path.Combine(folder,"MuseDesk-icon.png"),ImageFormat.Png);
        using(Bitmap logo=MuseBrand.Render(512,false))logo.Save(Path.Combine(folder,"MuseDesk-logo.png"),ImageFormat.Png);
        File.WriteAllText(Path.Combine(folder,"MuseDesk-logo.svg"),"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 100 100\"><g transform=\"translate(-15 -15) scale(1.3)\"><path d=\""+MuseBrand.MonogramPath+"\" fill=\"none\" stroke=\"#232323\" stroke-width=\"12\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/></g></svg>");
        using(Bitmap sheet=new Bitmap(760,300))
        using(Graphics g=Graphics.FromImage(sheet))
        using(Font title=new Font("Segoe UI Semibold",26))
        using(Font caption=new Font("Segoe UI",10))
        {
            g.Clear(Color.FromArgb(248,248,248));
            MuseBrand.Draw(g,new RectangleF(36,36,128,128),true);
            MuseBrand.Draw(g,new RectangleF(211,51,86,86),false);
            g.DrawString("Muse Desk",title,Brushes.Black,318,69);
            g.DrawString("Логотип и иконка приложения",caption,Brushes.Gray,321,116);
            int x=44;
            foreach(int s in new int[]{16,20,24,32,40,48,64})
            {
                using(Bitmap icon=MuseBrand.Render(s,true))g.DrawImageUnscaled(icon,x,231-s/2);
                g.DrawString(s.ToString(),caption,Brushes.Gray,x,271);x+=s+34;
            }
            sheet.Save(Path.Combine(folder,"MuseDesk-brand-preview.png"),ImageFormat.Png);
        }
    }
}
