using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace MuseDeskNative
{
    internal sealed class ActionLogEntry
    {
        internal string Name="", Target="", Input="", Output="", Status="Завершено";
        internal bool HasResult, Legacy;
        internal int Index;
    }

    public sealed partial class MainForm
    {
        // UI state stays separate from history and from instructions sent to the model.
        private readonly Dictionary<ChatMessage,HashSet<int>> expandedActions=new Dictionary<ChatMessage,HashSet<int>>();
        private readonly HashSet<ChatMessage> collapsedActionLogs=new HashSet<ChatMessage>();

        private static string ActionTitle(string name)
        {
            switch(name)
            {
                case "run_process":return "Запуск команды";
                case "read_text_file":return "Чтение файла";
                case "write_text_file":return "Запись файла";
                case "list_directory":return "Просмотр папки";
                case "fetch_web_page":return "Загрузка страницы";
                case "capture_screen":return "Снимок экрана";
                case "open_path":return "Открытие файла или ссылки";
                case "get_current_time":return "Проверка времени";
                case "ask_user":return "Уточнение у пользователя";
                case "update_project_memory":return "Обновление памяти проекта";
                case "recall_project_history":return "Поиск в истории проекта";
                case "complete_task":return "Завершение задачи";
                default:return string.IsNullOrEmpty(name)?"Запись журнала":name;
            }
        }
        private static string ActionIcon(string name)
        {
            switch(name){case "run_process":return "code";case "list_directory":return "folder";case "read_text_file":case "write_text_file":return "file";case "fetch_web_page":return "globe";case "capture_screen":return "screen";case "ask_user":return "chat";case "complete_task":return "check";default:return "more";}
        }
        internal static string CleanActionOutput(string value)
        {
            // Strip terminal control sequences, not Unicode or meaningful whitespace.
            string text=value??"";
            text=Regex.Replace(text,"\x1B\\][^\x07\x1B]*(?:\x07|\x1B\\\\)","");
            text=Regex.Replace(text,"\x1B\\[[0-?]*[ -/]*[@-~]","");
            return text.Replace("\r\n","\n").Replace('\r','\n').Replace("\0","").Replace("\n","\r\n");
        }
        private static string ReadActionStatus(string output)
        {
            string text=(output??"").TrimStart();
            if(text.StartsWith("Действие не разрешено")||text.StartsWith("Действие запрещено"))return "Нет доступа";
            if(text.StartsWith("Не выполнено:"))return "Пропущено";
            Match exit=Regex.Match(text,@"^Код завершения:\s*(-?\d+)");
            if(exit.Success)return exit.Groups[1].Value=="0"?"Код 0":"Ошибка · код "+exit.Groups[1].Value;
            if(text.StartsWith("Ошибка")||text.StartsWith("Процесс остановлен по тайм-ауту")||text.StartsWith("Файл не найден:")||text.StartsWith("Папка не найдена:")||text.StartsWith("Рабочая папка не найдена:")||text.StartsWith("Объект не найден:")||text.StartsWith("Неизвестный инструмент:"))return "Ошибка";
            return "Завершено";
        }
        private string FormatActionInput(Dictionary<string,object> args)
        {
            if(args==null||args.Count==0)return "";
            StringBuilder text=new StringBuilder();
            foreach(var pair in args)
            {
                string label=pair.Key;
                switch(label){case "executable":label="Программа";break;case "arguments":label="Аргументы";break;case "working_directory":label="Рабочая папка";break;case "path":label="Файл или папка";break;case "url":label="Адрес";break;case "content":label="Содержимое";break;case "question":label="Вопрос";break;case "summary":label="Итог";break;}
                string value=pair.Value is string?(string)pair.Value:json.Serialize(pair.Value);
                if(text.Length>0)text.Append("\r\n\r\n");text.Append(label).Append(":\r\n").Append(CleanActionOutput(value));
            }
            return text.ToString();
        }
        private List<ActionLogEntry> ActionEntries(ChatMessage message)
        {
            List<ActionLogEntry> entries=new List<ActionLogEntry>();
            foreach(var wire in message.wireMessages??new List<Dictionary<string,object>>())
            {
                if(wire==null)continue;
                object calls;
                if(GetString(wire,"role")=="assistant" && wire.TryGetValue("tool_calls",out calls) && calls is IEnumerable)
                {
                    foreach(object raw in (IEnumerable)calls)
                    {
                        var call=raw as Dictionary<string,object>;if(call==null)continue;
                        ToolCall parsed=null;
                        try{parsed=ParseToolCall(call);}catch(ArgumentException){ }catch(InvalidOperationException){ }
                        if(parsed==null)continue;
                        string target=Argument(parsed,"path");
                        if(parsed.Name=="run_process")target=(Argument(parsed,"executable")+" "+Argument(parsed,"arguments")).Trim();
                        if(target.Length==0)target=Argument(parsed,"url");if(target.Length==0)target=Argument(parsed,"target");if(target.Length==0)target=Argument(parsed,"question");
                        entries.Add(new ActionLogEntry{Name=parsed.Name,Target=CleanActionOutput(target),Input=FormatActionInput(parsed.Arguments),Index=entries.Count,Status=message.canceled?"Остановлено":message.failed?"Прервано":"Ожидание"});
                    }
                }
                if(GetString(wire,"role")=="tool")
                {
                    string name=GetString(wire,"tool_name");
                    ActionLogEntry entry=entries.FirstOrDefault(e=>!e.HasResult && (name.Length==0||e.Name==name));
                    if(entry==null){entry=new ActionLogEntry{Name=name,Index=entries.Count};entries.Add(entry);}
                    entry.Output=CleanActionOutput(GetString(wire,"content"));entry.HasResult=true;entry.Status=ReadActionStatus(entry.Output);
                }
            }
            if(entries.Count==0 && !string.IsNullOrWhiteSpace(message.toolLog))
            {
                // Old histories without protocol records cannot recover text already truncated by old versions.
                foreach(string raw in Regex.Split(CleanActionOutput(message.toolLog).Trim(),@"(?:\r?\n)+(?=•)|\r?\n(?=[^\s•])"))
                {
                    string line=raw.Trim().TrimStart('•').Trim();if(line.Length==0)continue;
                    int arrow=line.IndexOf(" → ",StringComparison.Ordinal);
                    entries.Add(new ActionLogEntry{Index=entries.Count,Name=arrow>0?line.Substring(0,arrow):"",Output=arrow>0?line.Substring(arrow+3):line,HasResult=true,Legacy=true,Status="Из истории"});
                }
            }
            if(generationCancellation!=null && generationChat!=null && generationChat.messages.Contains(message))
            {ActionLogEntry running=entries.FirstOrDefault(e=>!e.HasResult);if(running!=null)running.Status="Выполняется";}
            return entries;
        }
        private string ActionCopyText(ActionLogEntry entry)
        {
            return ActionTitle(entry.Name)+" · "+entry.Status+(entry.Input.Length>0?"\r\n\r\n"+entry.Input:"")+(entry.HasResult?"\r\n\r\nРезультат:\r\n"+entry.Output:"");
        }
        private string FullActionLogText(ChatMessage message)
        {
            var entries=ActionEntries(message);
            if(entries.Count==0||entries.All(e=>e.Legacy))return message.toolLog??"";
            return string.Join("\r\n\r\n────────────────────\r\n\r\n",entries.Select(ActionCopyText));
        }
        private int ActionLogVersion(ChatMessage message)
        {return (message.wireMessages==null?0:message.wireMessages.Count)*397+(message.toolLog??"").Length;}
        private Panel BuildActionLog(ChatMessage message,int width,Action resized=null)
        {
            Panel frame=new Panel{Name="ActionLog",AccessibleName="Журнал действий",Width=width,BackColor=Surface};
            HashSet<int> expanded;if(!expandedActions.TryGetValue(message,out expanded)){expanded=new HashSet<int>();expandedActions[message]=expanded;}
            Action rebuild=null;
            rebuild=delegate
            {
                frame.SuspendLayout();ClearControls(frame);
                List<ActionLogEntry> entries=ActionEntries(message);bool collapsed=collapsedActionLogs.Contains(message);
                RoundedButton header=(RoundedButton)MakeButton((collapsed?"▸ ":"▾ ")+"Действия · "+entries.Count,Surface,Muted,Math.Max(150,width-42),32);
                header.TextAlign=ContentAlignment.MiddleLeft;header.Font=new Font("Segoe UI",9.5F);header.AccessibleName="Свернуть или раскрыть журнал действий";
                header.Click+=delegate{if(!collapsedActionLogs.Add(message))collapsedActionLogs.Remove(message);rebuild();};frame.Controls.Add(header);
                RoundedButton copy=MakeCopyButton(()=>FullActionLogText(message),"Скопировать блок: Действия",Surface);copy.Location=new Point(width-32,1);frame.Controls.Add(copy);
                int y=36;
                if(!collapsed)
                foreach(ActionLogEntry entry in entries)
                {
                    bool open=expanded.Contains(entry.Index);
                    Panel row=new Panel{Name="ActionRow",AccessibleName=ActionTitle(entry.Name),Location=new Point(0,y),Width=width,BackColor=Surface};
                    RoundedButton toggle=(RoundedButton)MakeButton(ActionTitle(entry.Name),Surface,TextInk,Math.Max(100,width-150),28);
                    toggle.IconName=ActionIcon(entry.Name);toggle.IconPixelSize=16;toggle.IconTint=Muted;toggle.TextAlign=ContentAlignment.MiddleLeft;toggle.Font=new Font("Segoe UI",9.5F);toggle.AccessibleName="Раскрыть действие "+(entry.Index+1);tips.SetToolTip(toggle,entry.Name);
                    toggle.Click+=delegate{if(!expanded.Add(entry.Index))expanded.Remove(entry.Index);rebuild();};row.Controls.Add(toggle);
                    Label stateLabel=new Label{Name="ActionStatus",Text=entry.Status,TextAlign=ContentAlignment.MiddleRight,Font=new Font("Segoe UI",8.5F),ForeColor=entry.Status.StartsWith("Ошибка")?Color.FromArgb(185,45,45):Muted,Location=new Point(width-151,5),Size=new Size(114,20)};row.Controls.Add(stateLabel);
                    RoundedButton itemCopy=MakeCopyButton(()=>ActionCopyText(entry),"Скопировать действие "+(entry.Index+1),Surface);itemCopy.Location=new Point(width-32,0);row.Controls.Add(itemCopy);
                    int bottom=32;
                    string preview=entry.Target.Length>0?entry.Target:entry.Output.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()??"";
                    if(preview.Length>0){Label detail=new Label{Text=(open?"▾ ":"▸ ")+Compact(preview,200),AutoEllipsis=true,Font=new Font(entry.Name=="run_process"?"Consolas":"Segoe UI",9F),ForeColor=Muted,Location=new Point(30,31),Size=new Size(width-40,23),Cursor=Cursors.Hand};detail.Click+=delegate{if(!expanded.Add(entry.Index))expanded.Remove(entry.Index);rebuild();};tips.SetToolTip(detail,entry.Target);row.Controls.Add(detail);bottom=57;}
                    if(open)
                    {
                        if(entry.Input.Length>0)bottom=AddActionDetail(row,"Параметры",entry.Input,width,bottom,true);
                        bottom=AddActionDetail(row,entry.HasResult?"Результат":"Статус",entry.HasResult?(entry.Output.Length==0?"Действие завершено без текстового вывода.":entry.Output):entry.Status,width,bottom,entry.Name=="run_process"||entry.Name=="read_text_file"||entry.Name=="list_directory");
                    }
                    row.Height=bottom+6;row.Paint+=delegate(object sender,PaintEventArgs e){using(Pen line=new Pen(Line))e.Graphics.DrawLine(line,30,row.Height-1,row.Width-1,row.Height-1);};frame.Controls.Add(row);y+=row.Height+5;
                }
                frame.Height=entries.Count==0?0:y;frame.ResumeLayout(true);if(resized!=null)resized();
            };
            rebuild();return frame;
        }
        private int AddActionDetail(Control parent,string title,string text,int width,int y,bool monospace)
        {
            Label label=new Label{Text=title,Font=new Font("Segoe UI",8.5F),ForeColor=Muted,AutoSize=true,Location=new Point(32,y+4)};parent.Controls.Add(label);
            RichTextBox body=MakeReadableText(text,width-52,new Font(monospace?"Consolas":"Segoe UI",9.5F),Color.FromArgb(247,247,247),TextInk,320);
            body.DetectUrls=false;body.AccessibleName="Действие: "+title;body.Location=new Point(32,y+26);body.TabStop=true;parent.Controls.Add(body);FitRichText(body,320);return body.Bottom+12;
        }
    }
}
