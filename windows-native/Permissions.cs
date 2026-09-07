using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace MuseDeskNative
{
    public sealed class SavedToolPermission
    {
        public string key {get;set;}
        public string description {get;set;}
        public string createdAt {get;set;}
        public override string ToString(){return description??"Разрешение";}
    }

    internal sealed class ToolPermissionScope
    {
        internal string Key,Title,Summary,Explanation;
        internal static ToolPermissionScope From(ToolCall call)
        {
            Func<string,string> arg=name=>{object value;return call.Arguments!=null && call.Arguments.TryGetValue(name,out value)?Convert.ToString(value)??"":"";};
            string target=arg(call.Name=="run_process"?"executable":call.Name=="fetch_web_page"?"url":call.Name=="open_path"?"target":"path");
            string canonical=target,title;
            switch(call.Name)
            {
                case "read_text_file":title="Чтение файла";break;
                case "list_directory":title="Просмотр папки";break;
                case "write_text_file":title="Запись файла";break;
                case "fetch_web_page":title="Загрузка страницы";break;
                case "open_path":title="Открытие объекта";break;
                case "run_process":title="Запуск команды";break;
                case "capture_screen":title="Снимок экрана";target="Все экраны";break;
                default:throw new ArgumentException("Неподдерживаемый тип разрешения.");
            }
            Uri uri;
            if(call.Name=="capture_screen")canonical="all-screens";
            else if((call.Name=="fetch_web_page"||call.Name=="open_path") && Uri.TryCreate(target,UriKind.Absolute,out uri) && (uri.Scheme=="http"||uri.Scheme=="https"))canonical=uri.AbsoluteUri;
            else if(call.Name=="fetch_web_page")throw new ArgumentException("Некорректный URL.");
            else if(call.Name=="run_process")canonical=target.Trim().ToUpperInvariant();
            else canonical=Path.GetFullPath(Environment.ExpandEnvironmentVariables(target)).ToUpperInvariant();
            List<string> parts=new List<string>{"muse-permission-v1",call.Name,canonical};
            if(call.Name=="run_process")
            {
                string folder=arg("working_directory");
                parts.Add(arg("arguments"));parts.Add(Path.GetFullPath(string.IsNullOrWhiteSpace(folder)?Environment.CurrentDirectory:Environment.ExpandEnvironmentVariables(folder)).ToUpperInvariant());
            }
            string serialized=new JavaScriptSerializer().Serialize(parts),key;
            using(SHA256 hash=SHA256.Create())key=Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(serialized)));
            return new ToolPermissionScope {Key=key,Title=title,Summary=title+" · "+target,Explanation=ToolPermissionStore.FullAccessExplanation};
        }
    }

    internal sealed class ToolPermissionStore
    {
        internal const string FullAccessKey="muse-full-access-v2";
        internal const string FullAccessExplanation="Полный доступ: чтение и изменение файлов, запуск команд, сеть и снимки экрана без повторных вопросов. Сохраняется после перезапуска для чатов без индивидуальных ограничений. Режим отдельного чата выбирается возле +. Отозвать полный доступ: Настройки → Сохранённые разрешения. Права администратора Windows не выдаются.";
        private readonly string path;
        internal ToolPermissionStore(string file){path=file;}
        internal List<SavedToolPermission> Load()
        {
            if(!File.Exists(path))return new List<SavedToolPermission>();
            try
            {
                return (new JavaScriptSerializer().Deserialize<List<SavedToolPermission>>(File.ReadAllText(path))??new List<SavedToolPermission>()).Where(p=>p!=null && !string.IsNullOrWhiteSpace(p.key)).ToList();
            }
            catch{return new List<SavedToolPermission>();} // Corrupt permission files never grant access.
        }
        // Earlier versions saved a SHA-256 scope for each click on Allow always.
        // Honor those existing choices under the requested global-access semantics.
        private static bool IsFullAccessChoice(SavedToolPermission permission)
        {
            if(permission.key==FullAccessKey)return true;
            try{return Convert.FromBase64String(permission.key).Length==32;}catch{return false;}
        }
        internal bool HasFullAccess {get{return Load().Any(IsFullAccessChoice);}}
        internal bool Allows(ToolPermissionScope scope){return scope!=null && HasFullAccess;}
        internal void Save(List<SavedToolPermission> permissions)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));string temporary=path+".tmp";
            File.WriteAllText(temporary,new JavaScriptSerializer().Serialize(permissions),new UTF8Encoding(false));
            if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);
        }
        internal void Grant(ToolPermissionScope scope)
        {
            Save(new List<SavedToolPermission>{new SavedToolPermission {key=FullAccessKey,description="Полный доступ ко всем инструментам Muse Desk",createdAt=DateTime.UtcNow.ToString("o")}});
        }
    }

    internal sealed class ToolApprovalDialog : Form
    {
        internal readonly RoundedButton AlwaysButton,OnceButton,DenyButton;
        internal ToolApprovalDialog(ToolPermissionScope scope,string description)
        {
            Text="Разрешение · Muse Desk";ClientSize=new Size(736,560);MinimumSize=new Size(752,599);
            StartPosition=FormStartPosition.CenterParent;BackColor=Color.White;Font=new Font("Segoe UI",10F);MinimizeBox=false;MaximizeBox=false;ShowInTaskbar=false;
            Panel heading=new Panel {Dock=DockStyle.Top,Height=112,BackColor=Color.White};
            heading.Controls.Add(new MuseMark {Tile=true,Location=new Point(28,28),Size=new Size(42,42)});
            heading.Controls.Add(new Label {Text="Разрешить действие?",Font=new Font("Segoe UI Semibold",19F),ForeColor=Color.FromArgb(35,35,35),AutoSize=true,Location=new Point(86,25)});
            heading.Controls.Add(new Label {Text=scope.Title,Font=new Font("Segoe UI",10F),ForeColor=Color.FromArgb(112,112,112),AutoSize=true,Location=new Point(89,64)});
            Panel body=new Panel {Dock=DockStyle.Fill,Padding=new Padding(28,0,28,8),BackColor=Color.White};
            RoundedComposerPanel frame=new RoundedComposerPanel {Dock=DockStyle.Fill,Radius=16,Padding=new Padding(16),BackColor=Color.FromArgb(247,247,247),BorderColor=Color.FromArgb(233,233,233)};
            frame.Controls.Add(new RichTextBox {AccessibleName="Полное описание действия",Text=description,ReadOnly=true,Dock=DockStyle.Fill,BorderStyle=BorderStyle.None,BackColor=frame.BackColor,ForeColor=Color.FromArgb(35,35,35),Font=new Font("Consolas",10F),ScrollBars=RichTextBoxScrollBars.Vertical,WordWrap=true,DetectUrls=false});body.Controls.Add(frame);
            Label scopeLabel=new Label {Text="Если выбрать «Разрешить всегда»:\r\n"+scope.Explanation,Dock=DockStyle.Bottom,Height=112,Padding=new Padding(30,12,30,0),ForeColor=Color.FromArgb(100,100,100),Font=new Font("Segoe UI",9F)};
            FlowLayoutPanel actions=new FlowLayoutPanel {Dock=DockStyle.Bottom,Height=78,Padding=new Padding(22,12,22,18),FlowDirection=FlowDirection.RightToLeft,WrapContents=false,BackColor=Color.White};
            AlwaysButton=ActionButton("Разрешить всегда",DialogResult.Yes,true,184);
            OnceButton=ActionButton("Разрешить один раз",DialogResult.OK,false,190);
            DenyButton=ActionButton("Отклонить",DialogResult.Cancel,false,126);
            actions.Controls.AddRange(new Control[]{AlwaysButton,OnceButton,DenyButton});
            Controls.Add(body);Controls.Add(scopeLabel);Controls.Add(actions);Controls.Add(heading);body.BringToFront();
            CancelButton=DenyButton;AcceptButton=DenyButton;ActiveControl=DenyButton;
        }
        internal static RoundedButton ActionButton(string text,DialogResult result,bool primary,int width)
        {
            return new RoundedButton {Text=text,AccessibleName=text,DialogResult=result,Width=width,Height=42,Radius=12,BackColor=primary?Color.FromArgb(35,35,35):Color.FromArgb(244,244,244),ForeColor=primary?Color.White:Color.FromArgb(35,35,35),Font=new Font("Segoe UI",9.5F),Cursor=Cursors.Hand,Margin=new Padding(8,0,0,0)};
        }
    }

    public sealed partial class MainForm
    {
        private string CurrentPermissionInstruction(ChatSession chat=null)
        {
            string mode=AccessMode(chat??generationChat??activeChat);
            if(mode=="limited")return "Текущий режим Muse Desk: ограниченный доступ. Разрешены только list_directory, read_text_file и write_text_file внутри папки проекта: "+(chat??generationChat??activeChat).projectPath+". Запуск процессов, сеть, экран и открытие внешних приложений запрещены. Не обходи эти ограничения. Изменить доступ может только пользователь через интерфейс. Инструменты памяти, времени и уточнений доступны.";
            return mode=="full"
                ? "Текущий режим Muse Desk: пользователь включил полный доступ. В этом чате все поддерживаемые и включённые инструменты разрешены для всех файлов, адресов и команд в рамках задачи пользователя. Не запрашивай повторное разрешение: вызывай нужные инструменты. Этот режим заменяет прежнее требование подтверждать каждое действие; права Windows остаются прежними. Уточнение задачи через ask_user всё равно требует настоящего ответа пользователя."
                : "Текущий режим Muse Desk: действия требуют подтверждения. Вызывай нужный инструмент; приложение само покажет окно разрешения.";
        }
        private ToolPermissionStore PermissionStore {get{return new ToolPermissionStore(Path.Combine(Path.GetDirectoryName(statePath),"permissions.json"));}}
        private bool ApproveTool(ToolCall call,string description,CancellationToken token,ChatSession chat=null)
        {
            token.ThrowIfCancellationRequested();ToolPermissionScope scope;
            chat=chat??generationChat??activeChat;
            string mode=AccessMode(chat);
            if(mode=="limited")return LimitedPathAllowed(call,chat==null?null:chat.projectPath);
            try{scope=ToolPermissionScope.From(call);}catch(Exception ex){MessageBox.Show(this,ex.Message,"Некорректное действие");return false;}
            if(mode=="full"){token.ThrowIfCancellationRequested();return true;}
            using(ToolApprovalDialog dialog=new ToolApprovalDialog(scope,description))
            using(System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer {Interval=100})
            {
                dialog.Icon=Icon;timer.Tick+=delegate{if(token.IsCancellationRequested)dialog.DialogResult=DialogResult.Cancel;};timer.Start();
                DialogResult decision=dialog.ShowDialog(this);token.ThrowIfCancellationRequested();
                if(decision==DialogResult.Yes)
                {
                    try{PermissionStore.Grant(scope);if(chat!=null&&!string.IsNullOrEmpty(chat.accessMode)){chat.accessMode="full";SaveState();}UpdateAccessUi();}catch(Exception ex){MessageBox.Show(this,"Не удалось сохранить разрешение. Действие не выполнено.\r\n"+ex.Message,"Разрешения Muse");return false;}
                    return true;
                }
                return decision==DialogResult.OK;
            }
        }

        private void ManagePermissions()
        {
            using(Form manager=new Form {Text="Сохранённые разрешения · Muse Desk",ClientSize=new Size(720,460),MinimumSize=new Size(736,499),StartPosition=FormStartPosition.CenterParent,BackColor=Surface,Font=new Font("Segoe UI",10F),Icon=Icon,MinimizeBox=false,MaximizeBox=false})
            {
                Label title=new Label {Text="Сохранённые разрешения",Font=new Font("Segoe UI Semibold",18F),AutoSize=true,Location=new Point(28,24)};
                Label note=new Label {ForeColor=Muted,AutoSize=true,Location=new Point(30,66)};
                RoundedComposerPanel card=new RoundedComposerPanel {Location=new Point(30,108),Size=new Size(660,264),Anchor=AnchorStyles.Top|AnchorStyles.Bottom|AnchorStyles.Left|AnchorStyles.Right,Radius=16,Padding=new Padding(16),BackColor=IrisSoft,BorderColor=Color.FromArgb(233,233,233)};
                Label details=new Label {Text=ToolPermissionStore.FullAccessExplanation,Dock=DockStyle.Fill,Padding=new Padding(6),Font=new Font("Segoe UI",11F),ForeColor=TextInk};card.Controls.Add(details);
                RoundedButton reset=ToolApprovalDialog.ActionButton("Отключить полный доступ",DialogResult.None,false,250);reset.Location=new Point(28,394);reset.Anchor=AnchorStyles.Bottom|AnchorStyles.Left;
                Action reload=delegate{bool enabled=PermissionStore.HasFullAccess||state.chats.Any(c=>c.accessMode=="full");note.Text=enabled?"Полный доступ включён · Сохранён для всех или отдельных чатов":"Полный доступ выключен · Действия требуют подтверждения";reset.Enabled=enabled;};
                RoundedButton done=ToolApprovalDialog.ActionButton("Готово",DialogResult.OK,true,118);done.Location=new Point(574,394);done.Anchor=AnchorStyles.Bottom|AnchorStyles.Right;
                reset.Click+=delegate{try{PermissionStore.Save(new List<SavedToolPermission>());foreach(ChatSession c in state.chats)if(c.accessMode=="full")c.accessMode="confirm";SaveState();UpdateAccessUi();reload();}catch(Exception ex){MessageBox.Show(manager,ex.Message,"Разрешения");}};
                manager.Controls.AddRange(new Control[]{title,note,card,reset,done});manager.AcceptButton=done;manager.CancelButton=done;reload();manager.ShowDialog(this);
            }
        }
    }
}
