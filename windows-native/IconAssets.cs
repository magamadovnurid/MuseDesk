using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;

namespace MuseDeskNative
{
    internal static class IconAssets
    {
        private static readonly Dictionary<string,SortedDictionary<int,Bitmap>> icons=Load();
        private static Dictionary<string,SortedDictionary<int,Bitmap>> Load()
        {
            var result=new Dictionary<string,SortedDictionary<int,Bitmap>>(StringComparer.Ordinal);
            using(Stream stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("MuseDesk.Icons.zip"))
            {
                if(stream==null)throw new InvalidOperationException("В сборке отсутствуют значки интерфейса Muse Desk.");
                using(ZipArchive zip=new ZipArchive(stream,ZipArchiveMode.Read))foreach(var entry in zip.Entries)
                {
                    string[] parts=entry.FullName.Split('/');int size;
                    if(parts.Length!=2||!int.TryParse(Path.GetFileNameWithoutExtension(parts[1]),out size))continue;
                    SortedDictionary<int,Bitmap> sizes;if(!result.TryGetValue(parts[0],out sizes)){sizes=new SortedDictionary<int,Bitmap>();result[parts[0]]=sizes;}
                    using(Stream data=entry.Open())using(Image original=Image.FromStream(data))sizes[size]=new Bitmap(original);
                }
            }
            return result;
        }
        internal static bool Has(string name){return icons.ContainsKey(name);}
        internal static int Count {get{return icons.Count;}}
        internal static void Draw(Graphics graphics,string name,RectangleF bounds,Color color)
        {
            SortedDictionary<int,Bitmap> sizes;
            if(bounds.Width<=0||bounds.Height<=0||!icons.TryGetValue(name??"",out sizes))return;
            int desired=(int)Math.Round(Math.Max(bounds.Width,bounds.Height));
            int size=sizes.Keys.FirstOrDefault(s=>s>=desired);if(size==0)size=sizes.Keys.Last();
            Bitmap bitmap=sizes[size];GraphicsState state=graphics.Save();
            try
            {
                graphics.InterpolationMode=InterpolationMode.HighQualityBicubic;graphics.PixelOffsetMode=PixelOffsetMode.Half;
                using(ImageAttributes attributes=new ImageAttributes())
                {
                    attributes.SetWrapMode(WrapMode.TileFlipXY);
                    attributes.SetColorMatrix(new ColorMatrix(new float[][]{new float[]{color.R/255F,0,0,0,0},new float[]{0,color.G/255F,0,0,0},new float[]{0,0,color.B/255F,0,0},new float[]{0,0,0,color.A/255F,0},new float[]{0,0,0,0,1}}));
                    graphics.DrawImage(bitmap,Rectangle.Round(bounds),0,0,bitmap.Width,bitmap.Height,GraphicsUnit.Pixel,attributes);
                }
            }
            finally{graphics.Restore(state);}
        }
    }
    internal static class InterfaceTypography
    {
        internal static Font Sidebar(){return new Font("Segoe UI",10.5F,FontStyle.Regular,GraphicsUnit.Point);}
        internal static Font Caption(){return new Font("Segoe UI",9F,FontStyle.Regular,GraphicsUnit.Point);}
        internal static readonly Color Primary=Color.FromArgb(26,28,31);
        internal static readonly Color Secondary=Color.FromArgb(99,101,103);
        internal static readonly Color Tertiary=Color.FromArgb(140,142,144);
    }
}
