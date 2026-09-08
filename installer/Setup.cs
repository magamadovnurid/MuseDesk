using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

[assembly: AssemblyTitle("Muse Desk Setup")]
[assembly: AssemblyVersion("1.24.5.0")]
namespace MuseDeskSetup
{
    internal sealed class SoftButton:Button
    {
        internal bool Primary;
        private bool hot;
        internal SoftButton(){FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;Font=new Font("Segoe UI",10F);Cursor=Cursors.Hand;SetStyle(ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint,true);}
        protected override void OnMouseEnter(EventArgs e){base.OnMouseEnter(e);hot=true;Invalidate();}
        protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);hot=false;Invalidate();}
        protected override void OnPaint(PaintEventArgs e){e.Graphics.Clear(Parent.BackColor);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;using(var p=SetupWindow.Round(new Rectangle(0,0,Width-1,Height-1),10))using(var b=new SolidBrush(Primary?(hot?Color.FromArgb(65,65,65):Color.FromArgb(35,35,35)):(hot?Color.FromArgb(229,229,229):Color.FromArgb(242,242,242))))e.Graphics.FillPath(b,p);TextRenderer.DrawText(e.Graphics,Text,Font,ClientRectangle,Enabled?(Primary?Color.White:Color.FromArgb(65,65,65)):Color.Gray,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);}
    }
    internal sealed class ProgressTrack:Control
    {
        internal int Value=-1;private int phase;private readonly Timer timer=new Timer{Interval=35};
        internal ProgressTrack(){DoubleBuffered=true;timer.Tick+=delegate{phase=(phase+4)%Math.Max(1,Width);Invalidate();};timer.Start();}
        protected override void OnPaint(PaintEventArgs e){e.Graphics.Clear(BackColor);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;using(var b=new SolidBrush(Color.FromArgb(230,230,230)))using(var p=SetupWindow.Round(new Rectangle(0,0,Width-1,Height-1),5))e.Graphics.FillPath(b,p);int w=Value<0?80:Math.Max(1,(Width-1)*Value/100);int x=Value<0?Math.Min(phase,Math.Max(0,Width-w)):0;using(var b=new SolidBrush(Color.FromArgb(95,95,95)))using(var p=SetupWindow.Round(new Rectangle(x,0,w,Height-1),5))e.Graphics.FillPath(b,p);}
        protected override void Dispose(bool disposing){if(disposing)timer.Dispose();base.Dispose(disposing);}
    }
    internal sealed class SetupWindow:Form
    {
        private readonly JavaScriptSerializer json=new JavaScriptSerializer();
        private readonly string stage;
        private readonly bool preview;
        private Label status,details,hardware,steps;
        private Label destination;
        private SoftButton install,browse,cancel;
        private CheckBox onlyApp;
        private ProgressTrack progress;
        private Process worker;
        private bool busy,ready,complete,probing;
        internal static GraphicsPath Round(Rectangle r,int radius){var p=new GraphicsPath();int d=Math.Max(1,Math.Min(radius*2,Math.Min(r.Width,r.Height)));p.AddArc(r.X,r.Y,d,d,180,90);p.AddArc(r.Right-d,r.Y,d,d,270,90);p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.X,r.Bottom-d,d,d,90,90);p.CloseFigure();return p;}
        internal SetupWindow(string work,bool designPreview)
        {
            stage=work;preview=designPreview;Text="Установка Muse Desk";ClientSize=new Size(820,590);FormBorderStyle=FormBorderStyle.None;MaximizeBox=false;StartPosition=FormStartPosition.CenterScreen;BackColor=Color.FromArgb(245,245,245);Font=new Font("Segoe UI",10F);AutoScaleDimensions=new SizeF(96F,96F);AutoScaleMode=AutoScaleMode.Dpi;
            var close=new SoftButton{Text="×",AccessibleName="Закрыть установщик",Location=new Point(770,16),Size=new Size(32,32)};close.Click+=delegate{Close();};Controls.Add(close);
            var brand=new Label{Text="Muse Desk",Font=new Font("Segoe UI",12F,FontStyle.Bold),ForeColor=Color.FromArgb(45,45,45),Location=new Point(63,24),AutoSize=true};Controls.Add(brand);
            Paint+=delegate(object s,PaintEventArgs e){var saved=e.Graphics.Save();e.Graphics.ScaleTransform(e.Graphics.DpiX/96F,e.Graphics.DpiY/96F);MuseDeskNative.MuseBrand.Draw(e.Graphics,new Rectangle(24,20,28,28),false);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;using(var p=Round(new Rectangle(230,70,564,486),20))using(var b=new SolidBrush(Color.White))e.Graphics.FillPath(b,p);e.Graphics.Restore(saved);};
            Controls.Add(new Label{Text="Ваш помощник.\nЛокально.\nКаждый день.",Font=new Font("Segoe UI",20F),ForeColor=Color.FromArgb(65,65,65),Location=new Point(26,102),Size=new Size(195,160)});
            steps=new Label{Text="01  Проверка системы\n\n02  Приложение и движок\n\n03  Загрузка Glimmer\n\n04  Готово к работе",Font=new Font("Segoe UI",9F),ForeColor=Color.FromArgb(120,120,120),Location=new Point(28,303),Size=new Size(190,160)};Controls.Add(steps);
            status=new Label{Text="Подготовим всё для вас",Font=new Font("Segoe UI",19F),ForeColor=Color.FromArgb(60,60,60),BackColor=Color.White,Location=new Point(256,99),Size=new Size(505,65)};
            details=new Label{Text="Проверяем память, видеокарту и свободное место.",Font=new Font("Segoe UI",10F),ForeColor=Color.FromArgb(117,117,117),BackColor=Color.White,Location=new Point(258,174),Size=new Size(502,67)};
            hardware=new Label{Text="Определение оборудования…",ForeColor=Color.FromArgb(80,80,80),BackColor=Color.White,Location=new Point(258,253),Size=new Size(500,64)};
            destination=new Label{Text=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","Muse Desk"),AutoEllipsis=true,TextAlign=ContentAlignment.MiddleLeft,Padding=new Padding(10,0,10,0),BackColor=Color.FromArgb(247,247,247),Font=new Font("Segoe UI",9F),Location=new Point(258,333),Size=new Size(386,38)};
            browse=new SoftButton{Text="Папка…",Location=new Point(658,333),Size=new Size(104,38)};
            onlyApp=new CheckBox{Text="Установить только приложение, без модели",BackColor=Color.White,ForeColor=Color.FromArgb(100,100,100),Font=new Font("Segoe UI",9F),Location=new Point(258,393),Size=new Size(504,26)};
            progress=new ProgressTrack{Location=new Point(258,447),Size=new Size(504,7),BackColor=Color.White};
            cancel=new SoftButton{Text="Закрыть",Location=new Point(496,492),Size=new Size(102,38)};
            install=new SoftButton{Text="Проверка…",Primary=true,Enabled=false,Location=new Point(612,492),Size=new Size(150,38)};
            Controls.AddRange(new Control[]{status,details,hardware,destination,browse,onlyApp,progress,cancel,install});
            browse.Click+=async delegate{using(var picker=new FolderBrowserDialog{Description="Выберите пустую папку для установки",SelectedPath=destination.Text}){if(picker.ShowDialog(this)==DialogResult.OK){destination.Text=picker.SelectedPath;await ProbeAsync();}}};
            cancel.Click+=delegate{if(busy){File.WriteAllText(Path.Combine(stage,"cancel"),"cancel");details.Text="Останавливаю загрузку. Проверенные и частичные файлы сохранятся.";}else Close();};
            install.Click+=async delegate{if(complete){Process.Start(new ProcessStartInfo(Path.Combine(destination.Text,"release","MuseDesk.exe")){UseShellExecute=true});Close();return;}await InstallAsync();};
            FormClosing+=delegate(object s,FormClosingEventArgs e){if(busy){e.Cancel=true;File.WriteAllText(Path.Combine(stage,"cancel"),"cancel");details.Text="Останавливаю установку… Дождитесь завершения текущего шага.";}};
            Shown+=async delegate{if(preview){SetPreview();return;}await ProbeAsync();};
        }
        private ProcessStartInfo Command(string extra)
        {
            var start=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe"));
            start.Arguments="-NoProfile -ExecutionPolicy Bypass -File \""+Path.Combine(stage,"Install.ps1")+"\" -Destination \""+destination.Text+"\" "+extra;
            start.UseShellExecute=false;start.CreateNoWindow=true;start.WindowStyle=ProcessWindowStyle.Hidden;start.RedirectStandardOutput=true;start.RedirectStandardError=true;start.StandardOutputEncoding=Encoding.UTF8;start.StandardErrorEncoding=Encoding.UTF8;return start;
        }
        private async Task ProbeAsync()
        {
            if(probing)return;probing=true;install.Enabled=false;browse.Enabled=false;
            try{
                using(var p=new Process{StartInfo=Command("-Probe")}){p.Start();var output=p.StandardOutput.ReadToEndAsync();var error=p.StandardError.ReadToEndAsync();await Task.Run(()=>p.WaitForExit());string text=await output;
                    if(p.ExitCode!=0)throw new Exception(await error);
                    var data=json.Deserialize<System.Collections.Generic.Dictionary<string,object>>(text.Trim());string profile=Convert.ToString(data["profile"]);
                    hardware.Text=Convert.ToString(data["gpu"])+"\nRAM: "+data["ramGiB"]+" ГБ   ·   VRAM: "+Math.Round(Convert.ToDouble(data["gpuMiB"])/1024,1)+" ГБ   ·   Свободно: "+data["freeGiB"]+" ГБ";
                    ready=profile!="unsupported";onlyApp.Checked=profile=="app-only";onlyApp.Enabled=profile!="app-only"&&ready;
                    status.Text=profile.StartsWith("glimmer")?"Glimmer подходит вашей системе":ready?"Начнём с приложения":"Система пока не поддерживается";
                    details.Text=profile.StartsWith("glimmer")?"Будет загружена Glimmer Q4_K_M с кодировщиком изображений. Около "+(profile.EndsWith("f16")?"22":"20")+" ГБ из Hugging Face и GitHub. После установки обычный чат работает локально.":"Автоподбор требует NVIDIA ≥24 ГБ VRAM, 32 ГБ RAM, Compute Capability ≥7.5 и 40 ГБ свободного места. Для AMD/Intel и менее мощных систем доступна установка приложения без модели.";
                    if(!ready)details.Text="Требуется Windows 10 версии 2004 или новее / Windows 11, процессор x64 и .NET Framework 4.8. ARM64 пока не поддерживается.";
                    install.Text="Установить";install.Enabled=ready;progress.Value=0;
                }
            }catch(Exception ex){status.Text="Не удалось проверить систему";details.Text=ex.Message;install.Enabled=false;}
            finally{probing=false;browse.Enabled=true;}
        }
        private async Task InstallAsync()
        {
            busy=true;install.Enabled=false;browse.Enabled=false;onlyApp.Enabled=false;cancel.Text="Остановить";
            string stop=Path.Combine(stage,"cancel");if(File.Exists(stop))File.Delete(stop);
            try{
                worker=new Process{StartInfo=Command((onlyApp.Checked?"-AppOnly ":"")+"-CancelFile \""+stop+"\"")};worker.Start();var errors=worker.StandardError.ReadToEndAsync();
                string line;while((line=await worker.StandardOutput.ReadLineAsync())!=null){
                    try{var data=json.Deserialize<System.Collections.Generic.Dictionary<string,object>>(line);string step=Convert.ToString(data["stage"]);details.Text=Convert.ToString(data["message"]);progress.Value=Convert.ToInt32(data["percent"]);progress.Invalidate();status.Text=step=="complete"?"Всё готово":step=="error"?"Установка не завершена":step=="verify"?"Проверяем файлы":step=="download"?"Скачиваем компоненты":"Устанавливаем Muse Desk";}catch{}}
                await Task.Run(()=>worker.WaitForExit());if(worker.ExitCode==3010){status.Text="Нужна перезагрузка Windows";install.Text="После перезагрузки";install.Enabled=false;}
                else if(worker.ExitCode!=0){string error=await errors;if(!string.IsNullOrWhiteSpace(error))details.Text=error;install.Text="Повторить";install.Enabled=true;}
                else{complete=true;install.Text="Открыть Muse Desk";install.Enabled=true;progress.Value=100;}
            }catch(Exception ex){status.Text="Установка не завершена";details.Text=ex.Message;install.Text="Повторить";install.Enabled=true;}
            finally{if(worker!=null)worker.Dispose();worker=null;busy=false;cancel.Text="Закрыть";browse.Enabled=!complete;onlyApp.Enabled=!complete;}
        }
        internal void SetPreview(){status.Text="Glimmer подходит вашей системе";details.Text="Подберём модель, скачаем проверенные файлы и подготовим локальный помощник. Всё в одном установщике.";hardware.Text="NVIDIA · 24 ГБ VRAM\nRAM: 64 ГБ   ·   Свободно: 120 ГБ (демонстрация)";destination.Text="Папка пользователя · Programs · Muse Desk";cancel.Focus();progress.Value=42;install.Text="Установить";install.Enabled=false;}
    }
    internal static class Program
    {
        [STAThread] private static int Main(string[] args)
        {
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            string stage=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"MuseDeskInstaller",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);
            foreach(string name in new[]{"Install.ps1","Components.ps1","catalog.json","application.zip"})using(var source=Assembly.GetExecutingAssembly().GetManifestResourceStream(name))using(var target=File.Create(Path.Combine(stage,name)))source.CopyTo(target);
            using(var window=new SetupWindow(stage,args.Contains("--preview")||args.Contains("--screenshot"))){
                int shot=Array.IndexOf(args,"--screenshot");if(shot>=0 && shot+1<args.Length){window.Shown+=delegate{window.SetPreview();using(var bitmap=new Bitmap(window.Width,window.Height)){window.DrawToBitmap(bitmap,new Rectangle(Point.Empty,window.Size));bitmap.Save(args[shot+1]);}window.Close();};}
                Application.Run(window);
            }return 0;
        }
    }
}
