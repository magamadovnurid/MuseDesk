using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

namespace MuseDeskNative
{
    public sealed class FileChangeSummary
    {
        public string path {get;set;}
        public string kind {get;set;}
        public int? added {get;set;}
        public int? removed {get;set;}
        public string note {get;set;}
    }

    internal sealed class FileSnapshot
    {
        internal bool Exists,Known=true;
        internal string Hash,Text;
        internal static FileSnapshot Read(string path)
        {
            try
            {
                if(!File.Exists(path))return new FileSnapshot();
                FileInfo info=new FileInfo(path);
                if(info.Length>2*1024*1024)return new FileSnapshot{Exists=true,Hash="metadata:"+info.Length+":"+info.LastWriteTimeUtc.Ticks};
                byte[] bytes=File.ReadAllBytes(path);string hash;
                using(SHA256 sha=SHA256.Create())hash=Convert.ToBase64String(sha.ComputeHash(bytes));
                string text=null;
                try
                {
                    text=TextEncoding.Decode(bytes);
                    if(text.IndexOf('\0')>=0)text=null;
                }
                catch(DecoderFallbackException){}
                return new FileSnapshot{Exists=true,Hash=hash,Text=text};
            }
            catch{return new FileSnapshot{Known=false};}
        }
    }

    internal sealed class TaskChangeTracker
    {
        private readonly Dictionary<string,FileSnapshot> before=new Dictionary<string,FileSnapshot>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string,FileSnapshot> after=new Dictionary<string,FileSnapshot>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> notes=new HashSet<string>();
        internal string Notes {get{return string.Join(" ",notes.ToArray());}}
        internal void TrackFile(string path)
        {
            path=Path.GetFullPath(path);if(!before.ContainsKey(path))before[path]=FileSnapshot.Read(path);
        }
        internal void RefreshFile(string path){path=Path.GetFullPath(path);after[path]=FileSnapshot.Read(path);}

        internal Dictionary<string,FileSnapshot> CaptureDirectory(string folder)
        {
            notes.Add("Учтены записи Muse и файлы в рабочих папках команд; изменения команд вне этих папок могут не отображаться.");
            Dictionary<string,FileSnapshot> result=new Dictionary<string,FileSnapshot>(StringComparer.OrdinalIgnoreCase);
            if(string.IsNullOrWhiteSpace(folder))folder=Environment.CurrentDirectory;
            long bytes=0;int count=0;Stack<string> pending=new Stack<string>();pending.Push(Path.GetFullPath(folder));
            while(pending.Count>0)
            {
                string current=pending.Pop();
                try
                {
                    if((File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)continue;
                    foreach(string directory in Directory.EnumerateDirectories(current))
                    {
                        string name=Path.GetFileName(directory).ToLowerInvariant();
                        if(new[]{".git","node_modules",".venv","venv","bin","obj","packages"}.Contains(name))continue;
                        pending.Push(directory);
                    }
                    foreach(string file in Directory.EnumerateFiles(current))
                    {
                        if(++count>2000 || bytes>32*1024*1024){notes.Add("Обход ограничен размером проекта; список может быть неполным.");return result;}
                        FileInfo info=new FileInfo(file);if((info.Attributes&FileAttributes.ReparsePoint)!=0)continue;
                        bytes+=Math.Min(info.Length,2*1024*1024);result[Path.GetFullPath(file)]=FileSnapshot.Read(file);
                    }
                }
                catch{notes.Add("Часть файлов или папок недоступна для сравнения.");}
            }
            return result;
        }
        internal void RecordDirectoryChanges(Dictionary<string,FileSnapshot> baseline,Dictionary<string,FileSnapshot> current)
        {
            foreach(string path in baseline.Keys.Union(current.Keys,StringComparer.OrdinalIgnoreCase))
            {
                FileSnapshot start,end;baseline.TryGetValue(path,out start);current.TryGetValue(path,out end);
                // A bounded/failed scan cannot prove creation or deletion for an absent entry.
                bool uncertain=notes.Any(n=>n.Contains("неполным")||n.Contains("недоступна"));
                start=start??new FileSnapshot{Known=!uncertain};end=end??new FileSnapshot{Known=!uncertain};
                if(!before.ContainsKey(path) && start.Known && end.Known && start.Exists==end.Exists && start.Hash==end.Hash)continue;
                if(!before.ContainsKey(path))before[path]=start;after[path]=end;
            }
        }
        internal List<FileChangeSummary> GetChanges()
        {
            List<FileChangeSummary> result=new List<FileChangeSummary>();
            foreach(var pair in before)
            {
                FileSnapshot end;if(!after.TryGetValue(pair.Key,out end))continue;
                FileSnapshot start=pair.Value;
                if(start.Known && end.Known && start.Exists==end.Exists && start.Hash==end.Hash)continue;
                FileChangeSummary change=new FileChangeSummary{path=pair.Key,kind=!start.Known||!end.Known?"Не удалось сравнить":!start.Exists?"Создан":!end.Exists?"Удалён":"Изменён"};
                if(!start.Known||!end.Known)change.note="Статистика недоступна: файл слишком большой или недоступен.";
                else if((start.Exists && start.Text==null)||(end.Exists && end.Text==null))change.note="Бинарный или крупный файл — строки не считаются.";
                else
                {
                    int add,remove;
                    if(CountLines(start.Text??"",end.Text??"",out add,out remove)){change.added=add;change.removed=remove;if(add==0&&remove==0&&start.Exists&&end.Exists)change.note="Изменена кодировка или окончания строк.";}
                    else change.note="Слишком большой diff; точное число строк не рассчитано.";
                }
                result.Add(change);
            }
            return result.OrderBy(c=>c.path,StringComparer.OrdinalIgnoreCase).ToList();
        }
        private static string[] Lines(string text)
        {
            if(text.Length==0)return new string[0];string[] lines=text.Replace("\r\n","\n").Replace('\r','\n').Split('\n');
            return lines.Length>0 && lines[lines.Length-1]==""?lines.Take(lines.Length-1).ToArray():lines;
        }
        internal static bool CountLines(string oldText,string newText,out int added,out int removed)
        {
            string[] a=Lines(oldText),b=Lines(newText);int n=a.Length,m=b.Length,max=n+m;
            added=removed=0;if(max==0)return true;
            if(n==0){added=m;return true;}if(m==0){removed=n;return true;}
            int offset=max+1;int[] v=new int[max*2+3];long work=0;
            for(int d=0;d<=max;d++)for(int k=-d;k<=d;k+=2)
            {
                if(++work>2000000)return false;
                int x=(k==-d || (k!=d && v[offset+k-1]<v[offset+k+1]))?v[offset+k+1]:v[offset+k-1]+1;
                int y=x-k;while(x<n && y<m && a[x]==b[y]){x++;y++;}
                v[offset+k]=x;
                if(x>=n && y>=m){added=(d+m-n)/2;removed=(d+n-m)/2;return true;}
            }
            return false;
        }
    }

    public sealed partial class MainForm
    {
        private string FullReplyCopyText(ChatMessage message)
        {
            string text=message.content??"";if(string.IsNullOrWhiteSpace(message.finalSummary))return text;
            if(text.EndsWith(message.finalSummary,StringComparison.Ordinal))text=text.Substring(0,text.Length-message.finalSummary.Length).TrimEnd();
            return (text.Length>0?text+"\r\n\r\n":"")+SummaryCopyText(message);
        }
        private string SummaryCopyText(ChatMessage message)
        {
            StringBuilder text=new StringBuilder(message.finalSummary??"");
            foreach(FileChangeSummary file in message.fileChanges??new List<FileChangeSummary>())text.Append("\r\n").Append(file.path).Append(" — ").Append(file.kind).Append(file.added.HasValue?"  +"+file.added+" / −"+file.removed:"  "+file.note);
            if(!string.IsNullOrEmpty(message.changeNotes))text.Append("\r\n").Append(message.changeNotes);
            return text.ToString();
        }
        private RoundedComposerPanel BuildTaskSummary(ChatMessage message,int width)
        {
            RoundedComposerPanel frame=new RoundedComposerPanel{Name="TaskSummary",AccessibleName="Итог работы",Width=width,Radius=14,BackColor=Surface,BorderColor=Line};
            frame.Controls.Add(new Label{Text="Итог",Font=new Font("Segoe UI Semibold",11F),ForeColor=TextInk,Location=new Point(16,15),AutoSize=true});
            RoundedButton copy=MakeCopyButton(()=>SummaryCopyText(message),"Скопировать итог и изменения",Surface);copy.Location=new Point(width-copy.Width-10,8);frame.Controls.Add(copy);
            RichTextBox summary=MakeReadableText(message.finalSummary??"",width-32,new Font("Segoe UI",10.5F),Surface,TextInk);
            summary.AccessibleName="Итоговый ответ";summary.Location=new Point(16,48);ApplyMarkdownStyles(summary);frame.Controls.Add(summary);FitRichText(summary);int y=summary.Bottom+12;
            List<FileChangeSummary> files=message.fileChanges??new List<FileChangeSummary>();
            Label heading=new Label{Text=files.Count==0?"Изменений файлов не зафиксировано":"Изменённые файлы · "+files.Count,Font=new Font("Segoe UI",9F),ForeColor=Muted,AutoSize=true,Location=new Point(16,y)};frame.Controls.Add(heading);y+=30;
            FlowLayoutPanel list=new FlowLayoutPanel{Location=new Point(12,y),Width=width-24,Height=files.Count*64,BackColor=Surface,AutoScroll=false,FlowDirection=FlowDirection.TopDown,WrapContents=false};
            if(files.Count>0)frame.Controls.Add(list);
            foreach(FileChangeSummary file in files)
            {
                Panel row=new Panel{Size=new Size(list.Width-12,58),BackColor=Color.FromArgb(247,247,247),Margin=new Padding(0,0,0,6)};
                RoundedButton open=(RoundedButton)MakeButton(Path.GetFileName(file.path),row.BackColor,TextInk,Math.Max(80,row.Width-148),30);open.Font=new Font("Segoe UI",9.5F);open.IconName="file";open.TextAlign=ContentAlignment.MiddleLeft;open.Location=new Point(0,1);tips.SetToolTip(open,file.path);
                open.Click+=delegate{try{if(File.Exists(file.path))System.Diagnostics.Process.Start("explorer.exe","/select,\""+file.path+"\"");}catch(Exception ex){MuseDialog.Show(this,ex.Message,"Файл");}};row.Controls.Add(open);
                Label stats=new Label{Text=file.added.HasValue?"+"+file.added:"Нет статистики",TextAlign=ContentAlignment.MiddleRight,Font=new Font("Segoe UI Semibold",9.5F),ForeColor=file.added.HasValue?Color.FromArgb(31,136,61):Muted,Location=new Point(row.Width-142,8),Size=new Size(file.added.HasValue?70:130,22)};row.Controls.Add(stats);
                if(file.removed.HasValue)row.Controls.Add(new Label{Text="−"+file.removed,TextAlign=ContentAlignment.MiddleRight,Font=new Font("Segoe UI Semibold",9.5F),ForeColor=Color.FromArgb(190,50,44),Location=new Point(row.Width-68,8),Size=new Size(56,22)});
                Label path=new Label{Text=file.kind+" · "+file.path,AutoEllipsis=true,Font=new Font("Segoe UI",8.5F),ForeColor=Muted,Location=new Point(12,34),Size=new Size(row.Width-24,20)};tips.SetToolTip(path,file.path+(string.IsNullOrEmpty(file.note)?"":"\r\n"+file.note));row.Controls.Add(path);
                list.Controls.Add(row);
            }
            y+=list.Height;
            if(!string.IsNullOrWhiteSpace(message.changeNotes))
            {
                Label note=new Label{Text=message.changeNotes,Font=new Font("Segoe UI",8.5F),ForeColor=Muted,Location=new Point(16,y),Width=width-32,Height=MeasureTextHeight(message.changeNotes,width-32,new Font("Segoe UI",8.5F),160)};frame.Controls.Add(note);y+=note.Height+8;
            }
            frame.Height=y+12;return frame;
        }
    }
}
