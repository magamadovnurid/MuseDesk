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
            chatMenuButton.Click+=delegate{ContextMenuStrip menu=CreateChatMenu(activeChat);menu.Closed+=delegate{menu.Dispose();};menu.Show(chatMenuButton,new Point(0,32));};header.Controls.Add(chatMenuButton);
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
                AddResultRow(uri.Host+uri.AbsolutePath,(fetched?"Загружено инструментом Muse: ":"Ссылка из диалога, не проверена: ")+url,"globe",delegate{try{Process.Start(url);}catch(Exception ex){MessageBox.Show(this,ex.Message,"Ссылка");}});
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
            Uri uri;if(!Uri.TryCreate(value,UriKind.Absolute,out uri)||(uri.Scheme!="https"&&uri.Scheme!="http")){MessageBox.Show(this,"Введите полный адрес http:// или https://.","Ссылка");return;}
            // Manually saved links are references, not evidence of a successful fetch.
            if(activeChat.referenceUrls==null)activeChat.referenceUrls=new List<string>();activeChat.referenceUrls.Add(uri.AbsoluteUri);SaveState();RefreshResults(true);
        }

        private void ShowResultFile(string path)
        {
            if(!File.Exists(path)){MessageBox.Show(this,"Файл перемещён или удалён:\n"+path,"Результат недоступен");return;}
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
            catch(Exception ex){MessageBox.Show(this,ex.Message,"Открытие результата");}
        }

        private ContextMenuStrip CreateChatMenu(ChatSession chat)
        {
            ContextMenuStrip menu=new ContextMenuStrip();
            menu.Items.Add("Переименовать",null,delegate{RenameChat(chat);});
            menu.Items.Add("Выбрать папку проекта…",null,delegate{ChooseProject(chat);});
            if(!string.IsNullOrEmpty(chat.projectPath))menu.Items.Add("Убрать из проекта",null,delegate{chat.projectPath=null;SaveState();RefreshChatList();RenderConversation();});
            menu.Items.Add("Экспортировать Markdown",null,delegate{ExportChat(chat);});
            menu.Items.Add(new ToolStripSeparator());menu.Items.Add("Удалить",null,delegate{DeleteChat(chat);});return menu;
        }

        private void ChooseProject(ChatSession chat)
        {
            using(FolderBrowserDialog dialog=new FolderBrowserDialog {Description="Папка проекта для группировки чатов Muse",ShowNewFolderButton=false})
            {if(dialog.ShowDialog(this)!=DialogResult.OK)return;chat.projectPath=dialog.SelectedPath;collapsedProjects.Remove(chat.projectPath);SaveState();RefreshChatList();RenderConversation();}
        }

        private void AddChatRow(ChatSession chat,bool nested)
        {
            Panel row=new Panel {Width=226,Height=32,BackColor=Ink,Margin=new Padding(0,0,0,2)};
            RoundedButton item=(RoundedButton)MakeButton(chat.title,chat==activeChat?InkRaised:Ink,TextInk,198,32);
            item.Radius=8;item.TextAlign=ContentAlignment.MiddleLeft;item.Font=InterfaceTypography.Sidebar();item.IconTint=InterfaceTypography.Secondary;item.Padding=new Padding(nested?27:0,0,6,0);
            if(!nested)item.IconName="folder";
            item.GrayscaleText=true;
            tips.SetToolTip(item,chat.title);item.Click+=delegate{pendingAttachments.Clear();RenderAttachments();activeChat=chat;state.activeChatId=chat.id;SaveState();RefreshChatList();RenderConversation();};
            item.ContextMenuStrip=CreateChatMenu(chat);row.Controls.Add(item);
            RoundedButton more=(RoundedButton)MakeButton("Действия: "+chat.title,item.BackColor,Muted,24,24);more.IconName="more";more.IconOnly=true;more.Location=new Point(199,4);more.TabStop=true;
            more.Click+=delegate{ContextMenuStrip menu=CreateChatMenu(chat);menu.Closed+=delegate{menu.Dispose();};menu.Show(more,new Point(0,24));};row.Controls.Add(more);more.BringToFront();
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
            var groups=list.Where(c=>!string.IsNullOrWhiteSpace(c.projectPath)).GroupBy(c=>c.projectPath,StringComparer.OrdinalIgnoreCase).ToList();
            if(groups.Count>0)AddChatSection("Проекты");
            foreach(var group in groups)
            {
                string path=group.Key;bool collapsed=collapsedProjects.Contains(path)&&!searching;
                RoundedButton folder=(RoundedButton)MakeButton(Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar)),Ink,TextInk,226,32);folder.IconName=collapsed?"folder":"folder-open";folder.IconTint=InterfaceTypography.Secondary;folder.TextAlign=ContentAlignment.MiddleLeft;folder.Font=InterfaceTypography.Sidebar();folder.Margin=Padding.Empty;
                tips.SetToolTip(folder,path);folder.Click+=delegate{if(!collapsedProjects.Add(path))collapsedProjects.Remove(path);RefreshChatList();};chatList.Controls.Add(folder);
                folder.GrayscaleText=true;
                if(collapsed && generationCancellation!=null && group.Any(c=>c==generationChat))
                {
                    folder.Padding=new Padding(0,0,28,0);
                    ChatActivityIndicator spinner=new ChatActivityIndicator {Location=new Point(203,7),BackColor=folder.BackColor,ForeColor=Muted,Tag=generationChat.id};
                    spinner.Click+=delegate{folder.PerformClick();};folder.Controls.Add(spinner);
                }
                if(!collapsed)foreach(ChatSession chat in group)AddChatRow(chat,true);
            }
            var recent=list.Where(c=>string.IsNullOrWhiteSpace(c.projectPath)).ToList();
            if(recent.Count>0)AddChatSection("Недавние");foreach(ChatSession chat in recent)AddChatRow(chat,false);
            if(list.Count==0)chatList.Controls.Add(new Label {Text="Чаты не найдены",ForeColor=Muted,AutoSize=true,Margin=new Padding(10)});
        }
    }
}
