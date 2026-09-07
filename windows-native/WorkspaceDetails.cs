using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace MuseDeskNative
{
    public sealed partial class MainForm
    {
        private RoundedComposerPanel resultsCard;
        private Panel resultsHost;
        private FlowLayoutPanel resultsBody;
        private Button resultsToggle, chatMenuButton, exportButton;
        private bool resultsRequested=true;
        private string resultsSignature="";
        private DateTime nextResultsUpdate=DateTime.MinValue;
        private readonly HashSet<string> collapsedProjects=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private int ResultsWidth { get { return resultsCard!=null && resultsRequested && !rightRail.Visible && center.ClientSize.Width>=900 ? 292 : 0; } }

        private void BuildResultsPanel()
        {
            resultsHost=new Panel {Dock=DockStyle.Right,Width=292,BackColor=Canvas};center.Controls.Add(resultsHost);
            resultsCard=new RoundedComposerPanel {Name="ResultsCard",AccessibleName="Результаты и источники текущего чата",Radius=16,ShadowDepth=4,Width=280,Height=260,BackColor=Surface,BorderColor=Line};
            resultsBody=new ModernFlowPanel {Location=new Point(18,16),Width=244,Height=228,BackColor=Surface,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true};
            resultsCard.Controls.Add(resultsBody);resultsHost.Controls.Add(resultsCard);
            center.Controls.SetChildIndex(resultsHost,1);center.Controls.SetChildIndex(messageList,0);
            InstallScrollBar(resultsBody,resultsCard);
            resultsToggle=MakeIconButton("Результаты","Показать или скрыть результаты · Ctrl+Shift+R");
            ((RoundedButton)resultsToggle).IconName="results";((RoundedButton)resultsToggle).IconOnly=true;
            resultsToggle.Click+=delegate{ToggleResults();};header.Controls.Add(resultsToggle);
            chatMenuButton=MakeIconButton("Меню чата","Действия с текущим чатом");
            ((RoundedButton)chatMenuButton).IconName="more";((RoundedButton)chatMenuButton).IconOnly=true;
            chatMenuButton.Click+=delegate{ShowTransientMenu(CreateChatMenu(activeChat),chatMenuButton,new Point(0,32));};header.Controls.Add(chatMenuButton);
            exportButton=MakeButton("Экспорт",Surface,Muted,104,32);((RoundedButton)exportButton).IconName="export";
            exportButton.Font=new Font("Segoe UI",9F);exportButton.Click+=delegate{ExportChat(activeChat);};header.Controls.Add(exportButton);
            Panel folder=new Panel {Name="HeaderFolder",Size=new Size(20,24),Location=new Point(18,12),BackColor=Surface};
            folder.Paint+=delegate(object s,PaintEventArgs e){MuseIcons.Draw(e.Graphics,"folder",new RectangleF(1,3,18,18),Muted);};header.Controls.Add(folder);
            rightRail.VisibleChanged+=delegate{LayoutWorkspace();};
        }

        private void ToggleResults()
        {
            resultsRequested=!resultsRequested;
            if(resultsRequested)
            {
                rightRail.Visible=false;
                if(center.Width<900 && Width<1200)Width=Math.Min(1200,Screen.FromControl(this).WorkingArea.Width);
                if(center.Width<900)sidebar.Visible=false;
            }
            RefreshResults(true);LayoutWorkspace();RenderConversation();
        }

        private void LayoutResults()
        {
            if(resultsCard==null || resultsToggle==null || exportButton==null || chatMenuButton==null)return;
            resultsHost.Visible=ResultsWidth>0;
            resultsCard.Location=new Point(0,8);
            int desired=resultsBody.Controls.Cast<Control>().Sum(c=>c.Height+c.Margin.Vertical)+36;
            resultsCard.Height=Math.Max(150,Math.Min(desired,Math.Min(440,center.ClientSize.Height-220)));
            resultsBody.Height=resultsCard.Height-32;
            resultsToggle.BackColor=resultsHost.Visible?IrisSoft:Surface;
            resultsToggle.Location=new Point(center.Width-90,8);
            exportButton.Location=new Point(center.Width-204,8);
            int labelWidth=Math.Min(Math.Max(100,center.Width-340),TextRenderer.MeasureText(activeTitle.Text,activeTitle.Font).Width+10);
            activeTitle.Location=new Point(48,13);activeTitle.Width=labelWidth;
            chatMenuButton.Location=new Point(52+labelWidth,8);
        }

        private void AddResultsHeading(string caption,Action add)
        {
            Panel row=new Panel {Width=234,Height=28,BackColor=Surface,Margin=new Padding(0,5,0,0)};
            row.Controls.Add(new Label {Text=caption,ForeColor=Muted,Font=new Font("Segoe UI",9F),Location=new Point(0,6),AutoSize=true});
            if(add!=null)
            {
                RoundedButton plus=(RoundedButton)MakeButton("Добавить: "+caption,Surface,Muted,24,24);plus.IconName="add";plus.IconOnly=true;plus.Location=new Point(210,1);plus.Click+=delegate{add();};row.Controls.Add(plus);
            }
            resultsBody.Controls.Add(row);
        }

        private void AddResultRow(string name,string detail,string icon,Action open)
        {
            RoundedButton item=(RoundedButton)MakeButton(name,Surface,TextInk,234,30);
            item.IconName=icon;item.TextAlign=ContentAlignment.MiddleLeft;item.Font=new Font("Segoe UI",9F);item.Margin=Padding.Empty;
            tips.SetToolTip(item,detail);item.Click+=delegate{open();};resultsBody.Controls.Add(item);
        }

        private void AddResultsNote(string text)
        {
            resultsBody.Controls.Add(new Label {Text=text,Font=new Font("Segoe UI",8.5F),ForeColor=Muted,Width=240,Height=36,Margin=new Padding(0,3,0,5)});
        }

        internal static List<string> ChatLinks(ChatSession chat)
        {
            HashSet<string> links=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(string value in (chat.sourceUrls??new List<string>()).Concat(chat.referenceUrls??new List<string>())){Uri uri;if(Uri.TryCreate(value,UriKind.Absolute,out uri)&&(uri.Scheme=="http"||uri.Scheme=="https"))links.Add(uri.AbsoluteUri);}
            foreach(ChatMessage message in chat.messages)
            foreach(Match match in Regex.Matches(message.content??"","https?://[^\\s<>\"\\)\\]]+",RegexOptions.IgnoreCase))
            { Uri uri;string value=match.Value.TrimEnd('.',',',';',':','!','?');if(Uri.TryCreate(value,UriKind.Absolute,out uri))links.Add(uri.AbsoluteUri);if(links.Count>=50)break; }
            return links.Take(50).ToList();
        }

        internal static List<string> ChatResultFiles(ChatSession chat)
        {
            HashSet<string> files=new HashSet<string>(chat.resultFiles??new List<string>(),StringComparer.OrdinalIgnoreCase);
            // Older histories recorded successful tool output before resultFiles existed.
            foreach(ChatMessage message in chat.messages)
            foreach(var wire in message.wireMessages??new List<System.Collections.Generic.Dictionary<string,object>>())
            {
                if(GetString(wire,"role")!="tool")continue;
                string text=GetString(wire,"content"),name=GetString(wire,"tool_name"),path="";
                if(name=="write_text_file")
                {
                    Match match=Regex.Match(text,"^Текстовый файл записан после подтверждения пользователя: (.+) · [0-9]+ символов$");
                    if(match.Success)path=match.Groups[1].Value;
                }
                if(name=="capture_screen" && text.StartsWith("Снимок экрана создан и приложен: ",StringComparison.Ordinal))path=text.Substring("Снимок экрана создан и приложен: ".Length);
                if(path.Length>0 && File.Exists(path))files.Add(path);
            }
            return files.Where(p=>!string.IsNullOrWhiteSpace(p)).ToList();
        }

        private void RefreshResults(bool force)
        {
            if(resultsBody==null || activeChat==null)return;
            if(!force && generationCancellation!=null && DateTime.UtcNow<nextResultsUpdate && resultsSignature.StartsWith(activeChat.id+"|",StringComparison.Ordinal))return;
            string signature=activeChat.id+"|"+activeChat.messages.Count+"|"+string.Join("|",activeChat.resultFiles??new List<string>())+"|"+activeChat.messages.Sum(m=>(m.content??"").Length)+"|"+(activeChat.sourceUrls??new List<string>()).Count+"|"+(activeChat.referenceUrls??new List<string>()).Count;
            if(!force && signature==resultsSignature)return;
            resultsSignature=signature;nextResultsUpdate=DateTime.UtcNow.AddSeconds(1);
            resultsBody.SuspendLayout();ClearControls(resultsBody);
            AddResultsHeading("Результаты",delegate{AddResultFile();});
            List<string> files=ChatResultFiles(activeChat);
            if(files.Count==0)AddResultsNote("Файлы, созданные Muse, появятся здесь.");
            foreach(string file in files) { string path=file;AddResultRow(Path.GetFileName(path),path,"file",delegate{ShowResultFile(path);}); }
            AddResultsHeading("Источники",delegate{AddSourceUrl();});
            List<string> links=ChatLinks(activeChat);
            if(links.Count==0)AddResultsNote("Ссылки из диалога появятся здесь.");
            foreach(string link in links)
            {
                string url=link;Uri uri=new Uri(url);
                bool fetched=(activeChat.sourceUrls??new List<string>()).Contains(url);
                AddResultRow(uri.Host+uri.AbsolutePath,(fetched?"Загружено инструментом Muse: ":"Ссылка из диалога, не проверена: ")+url,"globe",delegate{try{Process.Start(url);}catch(Exception ex){MuseDialog.Show(this,ex.Message,"Ссылка");}});
            }
            List<string> attachments=activeChat.messages.SelectMany(m=>(m.files??new List<string>()).Concat(m.images??new List<string>())).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if(attachments.Count>0)
            {
                AddResultsHeading("Вложения",delegate{AddFiles(false);});
                foreach(string file in attachments){string path=file;AddResultRow(Path.GetFileName(path),path,IsImage(path)?"image":"file",delegate{ShowResultFile(path);});}
            }
            resultsBody.ResumeLayout(true);
        }

        private void AddResultFile()
        {
            using(OpenFileDialog dialog=new OpenFileDialog {Title="Добавить файл в результаты чата",Multiselect=true})
            {
                if(dialog.ShowDialog(this)!=DialogResult.OK)return;
                if(activeChat.resultFiles==null)activeChat.resultFiles=new List<string>();
                activeChat.resultFiles.AddRange(dialog.FileNames);SaveState();RefreshResults(true);
            }
        }

        private void AddSourceUrl()
        {
            string value=PromptDialog.Show(this,"Добавить ссылку в чат", "https://");if(string.IsNullOrWhiteSpace(value))return;
            Uri uri;if(!Uri.TryCreate(value,UriKind.Absolute,out uri)||(uri.Scheme!="https"&&uri.Scheme!="http")){MuseDialog.Show(this,"Введите полный адрес http:// или https://.","Ссылка");return;}
            // Manually saved links are references, not evidence of a successful fetch.
            if(activeChat.referenceUrls==null)activeChat.referenceUrls=new List<string>();activeChat.referenceUrls.Add(uri.AbsoluteUri);SaveState();RefreshResults(true);
        }

        private void ShowResultFile(string path)
        {
            if(!File.Exists(path)){MuseDialog.Show(this,"Файл перемещён или удалён:\n"+path,"Результат недоступен");return;}
            try
            {
                if((IsTextFile(path)||IsOfficeDocument(path)) && new FileInfo(path).Length<=2*1024*1024)
                {
                    Form viewer=new Form {Text=Path.GetFileName(path),Size=new Size(880,680),StartPosition=FormStartPosition.CenterParent,Icon=Icon};
                    viewer.Controls.Add(new RichTextBox {Dock=DockStyle.Fill,ReadOnly=true,Text=IsOfficeDocument(path)?ExtractOfficeText(path):File.ReadAllText(path),Font=new Font("Consolas",10F),BackColor=Surface,ForeColor=TextInk,BorderStyle=BorderStyle.None});
                    viewer.Show(this);
                }
                else Process.Start("explorer.exe","/select,\""+Path.GetFullPath(path)+"\"");
            }
            catch(Exception ex){MuseDialog.Show(this,ex.Message,"Открытие результата");}
        }

        private ContextMenuStrip CreateChatMenu(ChatSession chat)
        {
            ContextMenuStrip menu=new ContextMenuStrip();
            menu.Items.Add("Переименовать",null,delegate{RenameChat(chat);});
            ToolStripMenuItem move=new ToolStripMenuItem("Переместить в проект");
            foreach(string project in ProjectPaths())
            {
                string target=project;
                var choice=new ToolStripMenuItem(ProjectName(target)){ToolTipText=target,Checked=string.Equals(chat.projectPath,target,StringComparison.OrdinalIgnoreCase)};
                choice.Click+=delegate{MoveChatToProject(chat,target);};move.DropDownItems.Add(choice);
            }
            move.DropDownItems.Add("Другая папка…",null,delegate{ChooseProject(chat);});menu.Items.Add(move);
            if(!string.IsNullOrEmpty(chat.projectPath))menu.Items.Add("Убрать из проекта",null,delegate{MoveChatToProject(chat,null);});
            menu.Items.Add("Экспортировать Markdown",null,delegate{ExportChat(chat);});
            menu.Items.Add(new ToolStripSeparator());menu.Items.Add("Удалить",null,delegate{DeleteChat(chat);});return menu;
        }

        private void ChooseProject(ChatSession chat)
        {
            using(FolderBrowserDialog dialog=new FolderBrowserDialog {Description="Переместить чат в проект: выберите рабочую папку",ShowNewFolderButton=true})
            {if(dialog.ShowDialog(this)!=DialogResult.OK)return;MoveChatToProject(chat,dialog.SelectedPath);}
        }

        private static string ProjectName(string path)
        { string name=Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar));return string.IsNullOrEmpty(name)?path:name; }

        private List<string> ProjectPaths()
        {
            state.projects=state.projects.Concat(state.chats.Select(c=>c.projectPath)).Where(p=>!string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return state.projects.ToList();
        }

        private void RegisterProject(string path)
        {if(!state.projects.Contains(path,StringComparer.OrdinalIgnoreCase))state.projects.Add(path);}

        private void AddProject()
        {using(var dialog=BuildProjectDialog())dialog.ShowDialog(this);}

        private Form BuildProjectDialog()
        {
            var dialog=new Form {Text="Добавить проект",ClientSize=new Size(540,280),FormBorderStyle=FormBorderStyle.FixedDialog,StartPosition=FormStartPosition.CenterParent,MaximizeBox=false,MinimizeBox=false,BackColor=Ink,Font=InterfaceTypography.Sidebar()};
            var title=new Label {Text="Добавить проект",Font=new Font("Segoe UI",16F),ForeColor=TextInk,AutoSize=true,Location=new Point(24,20)};
            var note=new Label {Text="Выберите папку. Её имя станет названием проекта.",ForeColor=Muted,Location=new Point(24,59),Size=new Size(490,30)};
            var card=new RoundedComposerPanel {Location=new Point(24,101),Size=new Size(492,82),Radius=12,BorderColor=Line,BackColor=Surface};
            var folderName=new Label {Text="Папка не выбрана",ForeColor=TextInk,Font=InterfaceTypography.Sidebar(),Location=new Point(16,15),Size=new Size(325,24),AutoEllipsis=true};
            var folderPath=new Label {Text="Чаты будут храниться в этом проекте",ForeColor=Muted,Font=InterfaceTypography.Caption(),Location=new Point(16,45),Size=new Size(455,22),AutoEllipsis=true};
            var browse=MakeButton("Выбрать…",Ink,TextInk,105,32);browse.Location=new Point(371,10);
            card.Controls.AddRange(new Control[]{folderName,folderPath,browse});
            var add=MakeButton("Добавить проект",Color.Black,Color.White,152,36);add.Location=new Point(364,216);add.Enabled=false;
            var cancel=MakeButton("Отмена",Ink,TextInk,100,36);cancel.Location=new Point(252,216);cancel.DialogResult=DialogResult.Cancel;
            string selected=null;
            browse.Click+=delegate{using(var picker=new FolderBrowserDialog {Description="Выберите существующую папку проекта",ShowNewFolderButton=false}){if(picker.ShowDialog(dialog)!=DialogResult.OK)return;selected=picker.SelectedPath;folderName.Text=ProjectName(selected);folderPath.Text=selected;add.Enabled=true;}};
            add.Click+=delegate{try{AttachExistingProject(selected);dialog.DialogResult=DialogResult.OK;dialog.Close();}catch(Exception ex){MuseDialog.Show(dialog,ex.Message,"Не удалось добавить проект",MessageBoxButtons.OK,MessageBoxIcon.Warning);}};
            dialog.Controls.AddRange(new Control[]{title,note,card,add,cancel});dialog.AcceptButton=add;dialog.CancelButton=cancel;return dialog;
        }

        private void AttachExistingProject(string path)
        {
            if(string.IsNullOrWhiteSpace(path)||!Directory.Exists(path))throw new IOException("Выберите существующую папку проекта.");
            path=Path.GetFullPath(path);string root=Path.GetPathRoot(path);if(path.Length>root.Length)path=path.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
            RegisterProject(path);collapsedProjects.Remove(path);searchBox.Clear();SaveProjectTree();RefreshChatList();
        }
        private void SaveProjectTree()
        {state.collapsedProjectPaths=collapsedProjects.ToList();SaveState();}

        private void CreateProjectChat(string path)
        {
            RegisterProject(path);collapsedProjects.Remove(path);
            pendingAttachments.Clear();RenderAttachments();input.Clear();
            activeChat=NewChat();activeChat.projectPath=path;state.activeChatId=activeChat.id;
            searchBox.Clear();SaveProjectTree();RefreshChatList();RenderConversation();input.Focus();
        }

        private void MoveChatToProject(ChatSession chat,string path)
        {
            if(chat==generationChat && generationCancellation!=null){MuseDialog.Show(this,"Остановите ответ перед сменой рабочей папки.","Чат занят");return;}
            if(!string.IsNullOrWhiteSpace(chat.projectPath))RegisterProject(chat.projectPath);
            if(path!=null){RegisterProject(path);collapsedProjects.Remove(path);}
            chat.projectPath=path;SaveProjectTree();RefreshChatList();RenderConversation();
        }

        private void RemoveProject(string path)
        {
            if(generationCancellation!=null && generationChat!=null && string.Equals(generationChat.projectPath,path,StringComparison.OrdinalIgnoreCase))
            {MuseDialog.Show(this,"Остановите ответ перед удалением проекта из списка.","Проект занят");return;}
            bool removedActive=activeChat!=null && string.Equals(activeChat.projectPath,path,StringComparison.OrdinalIgnoreCase);
            state.chats.RemoveAll(c=>string.Equals(c.projectPath,path,StringComparison.OrdinalIgnoreCase));
            if(removedActive){activeChat=null;pendingAttachments.Clear();input.Clear();RenderAttachments();EnsureActiveChat();}
            state.projects.RemoveAll(p=>string.Equals(p,path,StringComparison.OrdinalIgnoreCase));collapsedProjects.Remove(path);
            SaveProjectTree();RefreshChatList();RenderConversation();
        }

        private ContextMenuStrip CreateProjectMenu(string path)
        {
            var menu=new ContextMenuStrip();
            menu.Items.Add("Новый чат",null,delegate{CreateProjectChat(path);});
            menu.Items.Add("Открыть папку",null,delegate{try{Process.Start("explorer.exe","\""+path+"\"");}catch(Exception ex){MuseDialog.Show(this,ex.Message,"Папка проекта");}});
            menu.Items.Add("Скопировать путь",null,delegate{Clipboard.SetText(path);});
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Удалить проект и чаты…",null,delegate{
                int count=state.chats.Count(c=>string.Equals(c.projectPath,path,StringComparison.OrdinalIgnoreCase));
                if(MuseDialog.Show(this,"Удалить проект «"+ProjectName(path)+"» и все его чаты ("+count+") из Muse Desk? Папка и файлы на диске сохранятся.","Удалить проект и чаты",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)==DialogResult.Yes)RemoveProject(path);
            });return menu;
        }

        private void AddChatRow(ChatSession chat,bool nested)
        {
            Panel row=new Panel {Width=226,Height=32,BackColor=Ink,Margin=new Padding(0,0,0,2)};
            RoundedButton item=(RoundedButton)MakeButton(chat.title,chat==activeChat?InkRaised:Ink,TextInk,198,32);
            item.Radius=8;item.TextAlign=ContentAlignment.MiddleLeft;item.Font=InterfaceTypography.Sidebar();item.IconTint=InterfaceTypography.Secondary;item.Padding=new Padding(nested?27:0,0,6,0);
            item.GrayscaleText=true;
            tips.SetToolTip(item,chat.title);item.Click+=delegate{pendingAttachments.Clear();RenderAttachments();activeChat=chat;state.activeChatId=chat.id;SaveState();RefreshChatList();RenderConversation();};
            item.ContextMenuStrip=CreateChatMenu(chat);OwnMenu(item.ContextMenuStrip,item,false);row.Controls.Add(item);
            RoundedButton more=(RoundedButton)MakeButton("Действия: "+chat.title,item.BackColor,Muted,24,24);more.IconName="more";more.IconOnly=true;more.Location=new Point(199,4);more.TabStop=true;
            more.Click+=delegate{ShowTransientMenu(CreateChatMenu(chat),more,new Point(0,24));};row.Controls.Add(more);more.BringToFront();
            if(chat==generationChat && generationCancellation!=null)
            {
                item.Padding=new Padding(nested?27:0,0,26,0);
                ChatActivityIndicator spinner=new ChatActivityIndicator {Location=new Point(177,8),BackColor=item.BackColor,ForeColor=Muted,Tag=chat.id};
                spinner.Click+=delegate{item.PerformClick();};item.Controls.Add(spinner);
            }
            chatList.Controls.Add(row);
        }

        private void AddChatSection(string name)
        {
            chatList.Controls.Add(new Label {Text=name,ForeColor=Muted,Font=InterfaceTypography.Caption(),Width=218,Height=28,Padding=new Padding(10,7,0,0),Margin=new Padding(0,9,0,0)});
        }

        private void PopulateChatTree(IEnumerable<ChatSession> chats,bool searching)
        {
            List<ChatSession> list=chats.ToList();
            string query=searchBox.Text.Trim();
            var paths=ProjectPaths().Where(p=>!searching || p.IndexOf(query,StringComparison.CurrentCultureIgnoreCase)>=0 || list.Any(c=>string.Equals(c.projectPath,p,StringComparison.OrdinalIgnoreCase))).ToList();
            if(paths.Count>0)AddChatSection("Проекты");
            foreach(string path in paths)
            {
                var group=list.Where(c=>string.Equals(c.projectPath,path,StringComparison.OrdinalIgnoreCase)).ToList();bool collapsed=collapsedProjects.Contains(path)&&!searching;
                RoundedButton folder=(RoundedButton)MakeButton(ProjectName(path),Ink,TextInk,226,32);folder.IconName=collapsed?"folder":"folder-open";folder.IconTint=InterfaceTypography.Secondary;folder.TextAlign=ContentAlignment.MiddleLeft;folder.Font=InterfaceTypography.Sidebar();folder.Margin=Padding.Empty;folder.Padding=new Padding(0,0,60,0);
                tips.SetToolTip(folder,path);folder.Click+=delegate{if(!collapsedProjects.Add(path))collapsedProjects.Remove(path);SaveProjectTree();RefreshChatList();};chatList.Controls.Add(folder);
                folder.ContextMenuStrip=CreateProjectMenu(path);OwnMenu(folder.ContextMenuStrip,folder,false);
                RoundedButton add=(RoundedButton)MakeButton("Новый чат в «"+ProjectName(path)+"»",Ink,Muted,24,24);add.IconName="add";add.IconOnly=true;add.Location=new Point(172,4);tips.SetToolTip(add,"Новый чат в проекте");add.Click+=delegate{CreateProjectChat(path);};folder.Controls.Add(add);
                RoundedButton more=(RoundedButton)MakeButton("Действия проекта «"+ProjectName(path)+"»",Ink,Muted,24,24);more.IconName="more";more.IconOnly=true;more.Location=new Point(199,4);more.Click+=delegate{ShowTransientMenu(CreateProjectMenu(path),more,new Point(0,24));};folder.Controls.Add(more);
                folder.GrayscaleText=true;
                if(collapsed && generationCancellation!=null && group.Any(c=>c==generationChat))
                {
                    folder.Padding=new Padding(0,0,80,0);
                    ChatActivityIndicator spinner=new ChatActivityIndicator {Location=new Point(152,7),BackColor=folder.BackColor,ForeColor=Muted,Tag=generationChat.id};
                    spinner.Click+=delegate{folder.PerformClick();};folder.Controls.Add(spinner);
                }
                if(!collapsed)foreach(ChatSession chat in group)AddChatRow(chat,true);
                if(!collapsed && group.Count==0)
                {
                    var empty=(RoundedButton)MakeButton("Создать первый чат",Ink,Muted,226,30);empty.Padding=new Padding(27,0,0,0);empty.TextAlign=ContentAlignment.MiddleLeft;empty.Font=InterfaceTypography.Sidebar();empty.Click+=delegate{CreateProjectChat(path);};chatList.Controls.Add(empty);
                }
            }
            var recent=list.Where(c=>string.IsNullOrWhiteSpace(c.projectPath)).ToList();
            if(recent.Count>0)AddChatSection("Недавние");foreach(ChatSession chat in recent)AddChatRow(chat,false);
            if(list.Count==0 && paths.Count==0)chatList.Controls.Add(new Label {Text="Чаты не найдены",ForeColor=Muted,AutoSize=true,Margin=new Padding(10)});
        }
    }
}
