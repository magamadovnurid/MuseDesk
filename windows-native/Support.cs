using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Diagnostics;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace MuseDeskNative
{
    internal class RoundedComposerPanel : Panel
    {
        internal bool Active;
        internal int Radius = 22;
        internal Color BorderColor = Color.FromArgb(231,231,231);
        internal int ShadowDepth;
        public RoundedComposerPanel() { SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true); BackColor = Color.White; }
        protected override void OnPaintBackground(PaintEventArgs e)
        { PaintSurface(e.Graphics); }
        protected override void OnPaint(PaintEventArgs e)
        { PaintSurface(e.Graphics); base.OnPaint(e); }
        private void PaintSurface(Graphics graphics)
        {
            graphics.Clear(Parent == null ? SystemColors.Control : Parent.BackColor);
            if (Width < 4 || Height < 4) return;
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            int inset=Math.Max(0,ShadowDepth);
            RectangleF bounds = new RectangleF(inset+1F,inset+1F,Width-inset*2-3,Height-inset*2-3);
            if(bounds.Width<2 || bounds.Height<2)return;
            for(int spread=inset;spread>0;spread--)
            {
                RectangleF shadow=bounds;shadow.Inflate(spread,spread);shadow.Offset(0,.5F);
                using(var shadowPath=RoundedPath(shadow,Radius+spread))using(var shade=new SolidBrush(Color.FromArgb(3,0,0,0)))graphics.FillPath(shade,shadowPath);
            }
            using (System.Drawing.Drawing2D.GraphicsPath path = RoundedPath(bounds,Radius))
            using (SolidBrush fill = new SolidBrush(BackColor))
            using (Pen border = new Pen(Active ? Color.FromArgb(198,198,198) : BorderColor,1F))
            {
                graphics.FillPath(fill,path); graphics.DrawPath(border,path);
            }
        }
        internal static System.Drawing.Drawing2D.GraphicsPath RoundedPath(Rectangle r,int radius)
        { return RoundedPath((RectangleF)r,radius); }
        internal static System.Drawing.Drawing2D.GraphicsPath RoundedPath(RectangleF r,float radius)
        {
            float d=Math.Max(1,Math.Min(radius*2,Math.Min(r.Width,r.Height)));
            System.Drawing.Drawing2D.GraphicsPath path=new System.Drawing.Drawing2D.GraphicsPath();
            path.AddArc(r.X,r.Y,d,d,180,90);path.AddArc(r.Right-d,r.Y,d,d,270,90);path.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);path.AddArc(r.X,r.Bottom-d,d,d,90,90);path.CloseFigure();return path;
        }
    }

    internal class RoundedButton : Button
    {
        internal string IconName;
        internal bool IconOnly;
        internal int IconPixelSize;
        internal Color IconTint=Color.Empty;
        internal Color IconHoverTint=Color.Empty;
        internal bool GrayscaleText;
        internal bool Outline;
        private int radius = 10;
        private bool hovered;
        private bool pressed;
        internal int Radius { get { return radius; } set { radius=Math.Max(1,value); UpdateShape(); Invalidate(); } }
        public RoundedButton()
        {
            FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;UseVisualStyleBackColor=false;
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
            SetStyle(ControlStyles.Opaque,false);
        }
        private static Color Shade(Color color,int amount)
        {
            return Color.FromArgb(Math.Max(0,Math.Min(255,color.R+amount)),Math.Max(0,Math.Min(255,color.G+amount)),Math.Max(0,Math.Min(255,color.B+amount)));
        }
        internal void PaintSurface(Graphics graphics,Rectangle bounds,bool hover,bool down,bool keyboardFocus)
        {
            if(bounds.Width<4||bounds.Height<4)return;
            graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            bool dark=BackColor.GetBrightness()<0.4F;
            // Secondary actions use a subtle surface, never a permanent frame.
            int shade=Outline&&!dark?-7:0;
            if(Enabled&&(hover||down||keyboardFocus))shade=dark?(down?27:17):(down?-23:-14);
            Color fill=Shade(BackColor,shade);
            using(System.Drawing.Drawing2D.GraphicsPath path=RoundedComposerPanel.RoundedPath(new Rectangle(bounds.X+1,bounds.Y+1,bounds.Width-3,bounds.Height-3),Radius))
            using(SolidBrush brush=new SolidBrush(fill))
            {
                graphics.FillPath(brush,path);
                // One shared contour only for keyboard navigation; no nested focus box.
                if(Enabled&&keyboardFocus)using(Pen focus=new Pen(dark?Color.FromArgb(190,190,190):Color.FromArgb(125,125,125),1F))graphics.DrawPath(focus,path);
            }
        }
        private void UpdateShape()
        {
            Region previous=Region;
            Region=null;
            if (previous!=null) previous.Dispose();
        }
        protected override void OnPaintBackground(PaintEventArgs e) { e.Graphics.Clear(Parent==null?SystemColors.Control:Parent.BackColor); }
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e); UpdateShape();
        }
        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hovered=true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovered=false; pressed=false; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button==MouseButtons.Left) pressed=true; Invalidate(); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); pressed=false; Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); pressed=false; Invalidate(); }
        protected override void OnKeyDown(KeyEventArgs e) { base.OnKeyDown(e); if (e.KeyCode==Keys.Space) { pressed=true; Invalidate(); } }
        protected override void OnKeyUp(KeyEventArgs e) { base.OnKeyUp(e); pressed=false; Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            // ButtonBase may omit WM_ERASEBKGND (including on partial repaints).
            // Own the entire surface: otherwise old rectangular pixels survive at corners.
            e.Graphics.Clear(Parent==null?SystemColors.Control:Parent.BackColor);
            if (Width<2 || Height<2) return;
            PaintSurface(e.Graphics,ClientRectangle,hovered,pressed,Focused&&ShowFocusCues);
            Rectangle label=new Rectangle(Padding.Left,Padding.Top,Width-Padding.Horizontal,Height-Padding.Vertical);
            string glyph=Text=="↑"?"send":Text=="■"?"stop":IconName;
            bool only=IconOnly||Text=="↑"||Text=="■";
            if(!string.IsNullOrEmpty(glyph))
            {
                int baseSize=IconPixelSize>0?IconPixelSize:(Text=="↑"||Text=="■"?20:16);
                int iconSize=(int)Math.Round(baseSize*e.Graphics.DpiX/96F);
                Color iconColor=IconTint.IsEmpty?ForeColor:IconTint;
                if(hovered&&!IconHoverTint.IsEmpty)iconColor=IconHoverTint;
                MuseIcons.Draw(e.Graphics,glyph,new RectangleF(only?(Width-iconSize)/2:10,(Height-iconSize)/2,iconSize,iconSize),Enabled?iconColor:Color.FromArgb(148,149,151));
                if(only)return;
                label.X=Math.Max(label.X,38);label.Width=Width-label.X-Math.Max(10,Padding.Right);
            }
            TextFormatFlags flags=TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.SingleLine|TextFormatFlags.NoPrefix;
            if (TextAlign==ContentAlignment.MiddleLeft) flags|=TextFormatFlags.Left;
            else if (TextAlign==ContentAlignment.MiddleRight) flags|=TextFormatFlags.Right;
            else flags|=TextFormatFlags.HorizontalCenter;
            if(GrayscaleText)
            {
                e.Graphics.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                using(StringFormat format=new StringFormat(StringFormat.GenericTypographic))
                using(Brush brush=new SolidBrush(Enabled?ForeColor:Color.FromArgb(155,156,158)))
                {
                    format.FormatFlags=StringFormatFlags.NoWrap;format.LineAlignment=StringAlignment.Center;format.Trimming=StringTrimming.EllipsisCharacter;
                    format.Alignment=TextAlign==ContentAlignment.MiddleLeft?StringAlignment.Near:TextAlign==ContentAlignment.MiddleRight?StringAlignment.Far:StringAlignment.Center;
                    e.Graphics.DrawString(Text,Font,brush,label,format);
                }
            }
            else TextRenderer.DrawText(e.Graphics,Text,Font,label,Enabled ? ForeColor : Color.FromArgb(155,151,166),flags);
        }
    }

    public sealed partial class MainForm
    {
        private static readonly object startupLogLock = new object();
        private void AppendStartupLog(string message)
        {
            try
            {
                lock (startupLogLock)
                {
                    string folder=Path.Combine(ProjectRoot(),"runtime"); Directory.CreateDirectory(folder);
                    string path=Path.Combine(folder,"startup.log");
                    if (File.Exists(path)&&new FileInfo(path).Length>1024*1024) File.Move(path,path+"."+DateTime.Now.Ticks+".old");
                    File.AppendAllText(path,DateTime.Now.ToString("s")+" "+message+Environment.NewLine);
                }
            }
            catch { }
        }
        private async Task<string> FetchPageAsync(Uri uri, CancellationToken token)
        {
            using (CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(45));
                using (HttpResponseMessage response = await http.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead,timeout.Token))
                {
                    response.EnsureSuccessStatusCode();
                    using (Stream stream = await response.Content.ReadAsStreamAsync())
                    using (MemoryStream buffer = new MemoryStream())
                    {
                        byte[] chunk = new byte[8192]; int count;
                        while ((count = await stream.ReadAsync(chunk,0,chunk.Length,timeout.Token)) > 0)
                        {
                            if (buffer.Length + count > 2*1024*1024) throw new IOException("Страница превышает лимит 2 МБ.");
                            buffer.Write(chunk,0,count);
                        }
                        string declared=response.Content.Headers.ContentType==null?null:response.Content.Headers.ContentType.CharSet;
                        return TextEncoding.DecodeHtml(buffer.ToArray(),declared);
                    }
                }
            }
        }

        private static async Task<string> ReadBoundedOutputAsync(StreamReader reader)
        {
            using(MemoryStream result=new MemoryStream())
            {
            byte[] buffer = new byte[4096]; int count;bool truncated=false;
            try
            {
                while ((count = await reader.BaseStream.ReadAsync(buffer,0,buffer.Length)) > 0)
                {
                    int remaining = 200000-(int)result.Length;
                    if (remaining > 0) result.Write(buffer,0,Math.Min(remaining,count));
                    if(count>remaining)truncated=true;
                }
            }
            catch (ObjectDisposedException) { }
            catch (IOException) { }
            byte[] bytes=result.ToArray();string text=null;
            if(truncated)
            {
                // Preserve a complete Unicode prefix instead of misdetecting a split UTF-8 character.
                for(int trim=0;trim<=3 && trim<=bytes.Length;trim++)
                    try{string candidate=new UTF8Encoding(false,true).GetString(bytes,0,bytes.Length-trim);if(candidate.Any(c=>char.IsControl(c)&&c!='\r'&&c!='\n'&&c!='\t'))break;text=candidate;break;}catch(DecoderFallbackException){}
            }
            if(text==null)text=TextEncoding.Decode(bytes,null,true);
            return text+(truncated?"\r\n[вывод обрезан]":"");
            }
        }

        private static async Task<ToolResult> CollectProcessAsync(Process process, CancellationToken token)
        {
            Task<string> output = ReadBoundedOutputAsync(process.StandardOutput);
            Task<string> errors = ReadBoundedOutputAsync(process.StandardError);
            using (CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(120));
                using (timeout.Token.Register(delegate { try { if (!process.HasExited) process.Kill(); } catch { } }))
                {
                    while (!process.HasExited && !timeout.IsCancellationRequested) await Task.Delay(50);
                    token.ThrowIfCancellationRequested();
                    if (timeout.IsCancellationRequested) return new ToolResult { Text = "Процесс остановлен по тайм-ауту 120 секунд." };
                    Task both = Task.WhenAll(output,errors);
                    if (await Task.WhenAny(both,Task.Delay(2000,token)) != both)
                    {
                        token.ThrowIfCancellationRequested();
                        return new ToolResult { Text = "Код завершения: " + process.ExitCode + ". Поток вывода остался открыт дочерним процессом; ожидание прекращено." };
                    }
                    string stdout=await output,stderr=await errors;
                    return new ToolResult { Text = "Код завершения: " + process.ExitCode +
                        (stdout.Length>0?"\r\n\r\nСтандартный вывод (stdout):\r\n"+stdout:"")+
                        (stderr.Length>0?"\r\n\r\nДиагностика (stderr):\r\n"+stderr:"") };
                }
            }
        }

        private static XDocument ReadOfficeXml(ZipArchiveEntry entry)
        {
            if (entry == null) return new XDocument();
            if (entry.Length > 16L*1024*1024) throw new IOException("Часть документа превышает лимит распаковки 16 МБ.");
            using (Stream stream = entry.Open())
            using (XmlReader reader = XmlReader.Create(stream,new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 16L*1024*1024 }))
                return XDocument.Load(reader);
        }

        private static string ExtractOfficeDocument(string path)
        {
            string extension = Path.GetExtension(path).ToLowerInvariant();
            StringBuilder result = new StringBuilder();
            using (ZipArchive archive = ZipFile.OpenRead(path))
            {
                if (extension == ".xlsx")
                {
                    XNamespace s = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
                    XNamespace r = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
                    List<string> shared = ReadOfficeXml(archive.GetEntry("xl/sharedStrings.xml")).Descendants(s+"si").Select(x => string.Concat(x.Descendants(s+"t").Select(t=>t.Value))).ToList();
                    Dictionary<string,string> relationships = ReadOfficeXml(archive.GetEntry("xl/_rels/workbook.xml.rels")).Descendants().Where(x=>x.Name.LocalName=="Relationship").ToDictionary(x=>(string)x.Attribute("Id"),x=>(string)x.Attribute("Target"));
                    foreach (XElement sheet in ReadOfficeXml(archive.GetEntry("xl/workbook.xml")).Descendants(s+"sheet"))
                    {
                        string target; if (!relationships.TryGetValue((string)sheet.Attribute(r+"id") ?? "",out target)) continue;
                        string entryPath = target.StartsWith("/") ? target.TrimStart('/') : "xl/"+target;
                        result.AppendLine("Лист: "+(string)sheet.Attribute("name"));
                        foreach (XElement row in ReadOfficeXml(archive.GetEntry(entryPath)).Descendants(s+"row"))
                        {
                            foreach (XElement cell in row.Elements(s+"c"))
                            {
                                string value = (string)cell.Element(s+"v") ?? ""; string type = (string)cell.Attribute("t"); int index;
                                if (type=="s" && int.TryParse(value,out index) && index>=0 && index<shared.Count) value=shared[index];
                                else if (type=="inlineStr") value=string.Concat(cell.Descendants(s+"t").Select(x=>x.Value));
                                else if (type=="b") value = value=="1" ? "TRUE" : "FALSE";
                                result.Append((string)cell.Attribute("r")).Append("=").Append(value.Replace("\r"," ").Replace("\n"," ")).Append("\t");
                            }
                            result.AppendLine(); if (result.Length>120000) break;
                        }
                        if (result.Length>120000) break;
                    }
                }
                else
                {
                    IEnumerable<ZipArchiveEntry> entries = archive.Entries.Where(e=>extension==".docx" ? e.FullName=="word/document.xml" : Regex.IsMatch(e.FullName,"^ppt/slides/slide[0-9]+\\.xml$"));
                    entries = entries.OrderBy(e=>Regex.Replace(e.FullName,"[0-9]+",m=>m.Value.PadLeft(9,'0')));
                    foreach (ZipArchiveEntry entry in entries)
                    {
                        if (extension==".pptx") result.AppendLine("Слайд "+Regex.Match(entry.Name,"[0-9]+").Value);
                        foreach (XElement p in ReadOfficeXml(entry).Descendants().Where(x=>x.Name.LocalName=="p"))
                        {
                            result.AppendLine(string.Concat(p.Descendants().Where(x=>x.Name.LocalName=="t" || x.Name.LocalName=="tab" || x.Name.LocalName=="br").Select(x=>x.Name.LocalName=="t" ? x.Value : x.Name.LocalName=="tab" ? "\t" : "\n")));
                            if (result.Length>120000) break;
                        }
                        if (result.Length>120000) break;
                    }
                }
            }
            string text=result.ToString().Trim();
            return text.Length>120000 ? text.Substring(0,120000)+"\r\n[обрезано приложением]" : text.Length==0 ? "В документе не найден извлекаемый текст." : text;
        }
        private async Task<string> ReadHealthAsync(string path)
        {
            using (CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3)))
            using (HttpResponseMessage response = await http.GetAsync(NormalizeUrl(state.settings.baseUrl) + path, timeout.Token))
            {
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync();
            }
        }

        private string SnapshotAttachment(string source, string chatId)
        {
            if (!File.Exists(source)) throw new FileNotFoundException("Файл перемещён или удалён", source);
            string directory = Path.Combine(Path.GetDirectoryName(statePath), "attachments", chatId, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string destination = Path.Combine(directory, Path.GetFileName(source));
            File.Copy(source, destination, false);
            return destination;
        }

        private static string EncodeImage(string path)
        {
            using (Image source = Image.FromFile(path))
            {
                double ratio = Math.Min(1D, 1600D / Math.Max(source.Width, source.Height));
                using (Bitmap scaled = new Bitmap(Math.Max(1,(int)(source.Width*ratio)),Math.Max(1,(int)(source.Height*ratio))))
                using (Graphics graphics = Graphics.FromImage(scaled))
                using (MemoryStream output = new MemoryStream())
                {
                    graphics.Clear(Color.White);
                    graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    graphics.DrawImage(source, 0, 0, scaled.Width, scaled.Height);
                    scaled.Save(output, ImageFormat.Png);
                    return Convert.ToBase64String(output.ToArray());
                }
            }
        }

        private static void DisposeTree(Control root)
        {
            foreach (Control child in root.Controls) DisposeTree(child);
            PictureBox picture = root as PictureBox;
            if (picture != null && picture.Image != null) { picture.Image.Dispose(); picture.Image = null; }
            root.Dispose();
        }

        private RichTextBox MakeReadableText(string text, int width, Font font, Color back, Color fore, int maxHeight)
        {
            int measured = MeasureTextHeight(text,width-18,font,maxHeight)+12;
            RichTextBox box = new RichTextBox
            {
                Text = text, ReadOnly = true, BorderStyle = BorderStyle.None, Font = font,
                BackColor = back, ForeColor = fore, WordWrap = true, DetectUrls = true,
                ScrollBars = measured >= maxHeight ? RichTextBoxScrollBars.Vertical : RichTextBoxScrollBars.None, Width = width, Height = Math.Min(maxHeight,Math.Max(44,measured+12)),
                TabStop = false, HideSelection = false
            };
            box.LinkClicked += delegate(object sender,LinkClickedEventArgs e)
            {
                Uri uri;
                if (Uri.TryCreate(e.LinkText,UriKind.Absolute,out uri) && (uri.Scheme == "https" || uri.Scheme == "http"))
                { try { System.Diagnostics.Process.Start(uri.AbsoluteUri); } catch (Exception ex) { MuseDialog.Show(this,ex.Message,"Ссылка"); } }
            };
            box.MouseWheel+=delegate(object sender,MouseEventArgs e)
            {
                if(box.ScrollBars==RichTextBoxScrollBars.Vertical)return;
                Control parent=box.Parent;while(parent!=null && !(parent is ModernFlowPanel))parent=parent.Parent;
                if(parent!=null){((ModernFlowPanel)parent).ScrollWheel(e.Delta);HandledMouseEventArgs handled=e as HandledMouseEventArgs;if(handled!=null)handled.Handled=true;}
            };
            return box;
        }

        private Action<string> clipboardWriter=text=>Clipboard.SetText(text);
        private RoundedButton MakeCopyButton(Func<string> source,string accessibleName,Color background)
        {
            RoundedButton button=(RoundedButton)MakeButton("",background,Muted,30,30);
            button.IconName="copy";button.IconOnly=true;button.Font=new Font("Segoe UI",9F);button.AccessibleName=accessibleName;
            button.IconPixelSize=16;button.IconTint=InterfaceTypography.Tertiary;button.IconHoverTint=InterfaceTypography.Secondary;
            tips.SetToolTip(button,accessibleName);
            System.Windows.Forms.Timer reset=new System.Windows.Forms.Timer{Interval=1600};
            reset.Tick+=delegate{reset.Stop();if(!button.IsDisposed){button.IconName="copy";button.Invalidate();tips.SetToolTip(button,accessibleName);}};
            button.Disposed+=delegate{reset.Dispose();};
            button.Click+=delegate
            {
                try
                {
                    string text=source();if(string.IsNullOrEmpty(text))return;
                    clipboardWriter(text);button.IconName="check";button.Invalidate();tips.SetToolTip(button,"Скопировано");reset.Stop();reset.Start();
                }
                catch(Exception ex){MuseDialog.Show(this,"Не удалось скопировать текст.\r\n"+ex.Message,"Буфер обмена");}
            };
            return button;
        }

        private RoundedComposerPanel BuildTextBlock(string title,string text,int width,bool code)
        {
            Color background=Color.FromArgb(247,247,247);
            RoundedComposerPanel frame=new RoundedComposerPanel{Width=width,Radius=12,BackColor=background,BorderColor=Line};
            frame.Controls.Add(new Label{Text=title,AutoEllipsis=true,Font=new Font("Segoe UI",9F),ForeColor=Muted,Location=new Point(14,15),Size=new Size(Math.Max(48,width-164),22),BackColor=background});
            RoundedButton copy=MakeCopyButton(()=>text,title=="Рассуждение"?"Скопировать рассуждение":"Скопировать блок: "+title,background);copy.Location=new Point(width-copy.Width-10,7);frame.Controls.Add(copy);
            RichTextBox body=MakeReadableText(text,width-28,new Font(code?"Consolas":"Segoe UI",code?10F:10.5F),background,code?TextInk:Muted,code?800:420);
            body.AccessibleName=title;body.Location=new Point(14,46);frame.Controls.Add(body);FitRichText(body,code?800:420);
            frame.Height=body.Bottom+14;return frame;
        }

        private int AddAnswerContent(Control card,string content,int width,int y)
        {
            int offset=0;
            foreach(Match match in Regex.Matches(content,"(?ms)^[ ]{0,3}```([^\\r\\n]*)\\r?\\n(.*?)(?:^[ ]{0,3}```[ \\t]*(?:\\r?\\n|$)|\\z)"))
            {
                y=AddAnswerProse(card,content.Substring(offset,match.Index-offset),width,y);
                string language=match.Groups[1].Value.Trim();
                RoundedComposerPanel code=BuildTextBlock(language.Length==0?"Код":language,match.Groups[2].Value,width,true);
                code.Location=new Point(18,y);card.Controls.Add(code);y=code.Bottom+14;offset=match.Index+match.Length;
            }
            return AddAnswerProse(card,content.Substring(offset),width,y);
        }

        private int AddAnswerProse(Control card,string text,int width,int y)
        {
            if(string.IsNullOrWhiteSpace(text))return y;
            RichTextBox answer=MakeReadableText(text.Trim('\r','\n'),width,new Font("Segoe UI",10.5F),Surface,TextInk,32760);
            answer.AccessibleName="Ответ Muse";answer.Location=new Point(18,y);ApplyMarkdownStyles(answer);card.Controls.Add(answer);FitRichText(answer,32760);
            return answer.Bottom+12;
        }

        private void ApplyMarkdownStyles(RichTextBox box)
        {
            string original = box.Text;
            box.Clear();
            bool code = false;
            using (Font bold = new Font(box.Font,FontStyle.Bold))
            using (Font heading = new Font("Segoe UI Semibold",11F))
            using (Font mono = new Font("Consolas",10F))
            {
                foreach (string sourceLine in original.Replace("\r\n","\n").Split('\n'))
                {
                    string line = sourceLine;
                    if (line.TrimStart().StartsWith("```")) { code = !code; continue; }
                    bool isHeading = Regex.IsMatch(line,"^#{1,6} ");
                    if (isHeading) line = Regex.Replace(line,"^#{1,6} ","");
                    if (!code && line.StartsWith("- ")) line = "• " + line.Substring(2);
                    box.SelectionFont = code ? mono : isHeading ? heading : box.Font;
                    box.SelectionColor = TextInk;
                    box.SelectionBackColor = code ? Color.FromArgb(244,244,244) : box.BackColor;
                    if (!code && !isHeading)
                    {
                        int position = 0;
                        foreach (Match match in Regex.Matches(line,"\\*\\*(.+?)\\*\\*|`([^`]+)`"))
                        {
                            box.AppendText(line.Substring(position,match.Index-position));
                            box.SelectionFont = match.Groups[1].Success ? bold : mono;
                            box.AppendText(match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value);
                            box.SelectionFont = box.Font;
                            position = match.Index+match.Length;
                        }
                        box.AppendText(line.Substring(position)+Environment.NewLine);
                    }
                    else box.AppendText(line+Environment.NewLine);
                }
            }
            box.Select(0,0);
        }

        private async Task RetryLastAsync()
        {
            if (generationCancellation != null || activeChat == null || !connected || !modelAvailable) return;
            ChatMessage previous = activeChat.messages.LastOrDefault();
            if (previous == null || previous.role != "assistant") return;
            int previousIndex = activeChat.messages.Count-1;
            if (previousIndex < 1 || activeChat.messages[previousIndex-1].role != "user") return;
            ChatMessage question = activeChat.messages[previousIndex-1];
            input.Text = question.content;
            pendingAttachments.Clear();
            pendingAttachments.AddRange(question.images ?? new List<string>());
            pendingAttachments.AddRange(question.files ?? new List<string>());
            activeChat.messages.RemoveAt(previousIndex);
            activeChat.messages.RemoveAt(previousIndex-1);
            renderedChatId = null;
            RenderAttachments();
            await SendOrStopAsync();
        }
    }
}
