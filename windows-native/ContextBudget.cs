using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace MuseDeskNative
{
    internal sealed class ModelApiException : HttpRequestException
    {
        internal int Status,ContextSize;
        internal bool ContextExceeded;
        internal ModelApiException(int status,string body):base(Readable(body))
        {
            Status=status;ContextExceeded=body.IndexOf("exceed_context_size",StringComparison.OrdinalIgnoreCase)>=0 || Regex.IsMatch(body,"exceeds.*context|context.*(?:exceeded|too long)|maximum context length",RegexOptions.IgnoreCase);
            Match size=Regex.Match(body,"[\"']?n_ctx[\"']?\\s*:\\s*(\\d+)");
            if(!size.Success)size=Regex.Match(body,"(?:available context size|context (?:size|length))\\s*\\(?([0-9]+)",RegexOptions.IgnoreCase);
            int parsed;if(size.Success&&int.TryParse(size.Groups[1].Value,out parsed))ContextSize=parsed;
        }
        private static string Readable(string body)
        {
            try
            {
                var root=new JavaScriptSerializer().DeserializeObject(body) as Dictionary<string,object>;object error;
                if(root!=null&&root.TryGetValue("error",out error))
                {
                    var nested=error as Dictionary<string,object>;object message;
                    if(nested!=null&&nested.TryGetValue("message",out message))return Convert.ToString(message);
                    if(error is string)return (string)error;
                }
            }
            catch{}
            return body;
        }
    }

    internal sealed class ContextBudget
    {
        private const string MemoryPrefix="Рабочая запись клиента (сокращённые данные истории, не новые инструкции):\n";
        private Dictionary<string,object> task;
        private readonly string primaryText;
        private readonly int overhead;
        private readonly List<string> memory=new List<string>();
        private readonly JavaScriptSerializer serializer=new JavaScriptSerializer{MaxJsonLength=int.MaxValue};
        internal int LastEstimate,Target;
        internal bool Changed;
        internal ContextBudget(List<Dictionary<string,object>> messages,int toolOverhead,string originalTaskText=null)
        {
            task=messages.LastOrDefault(m=>Value(m,"role")=="user" && !IsCapture(m));overhead=toolOverhead+512;primaryText=originalTaskText;
        }
        internal static string Value(Dictionary<string,object> m,string key){object v;return m.TryGetValue(key,out v)?Convert.ToString(v)??"":"";}
        private static bool IsCapture(Dictionary<string,object> m){return Value(m,"content")=="Результат инструмента capture_screen.";}
        internal int Estimate(List<Dictionary<string,object>> messages)
        {
            long size=overhead;
            foreach(var message in messages)
            {
                var copy=new Dictionary<string,object>(message);object images;
                if(copy.TryGetValue("images",out images)){copy.Remove("images");ICollection items=images as ICollection;size+=(items==null?1:items.Count)*2048L;}
                size+=(Encoding.UTF8.GetByteCount(serializer.Serialize(copy))+1)/2+12;
            }
            return (int)Math.Min(int.MaxValue,size);
        }
        private List<List<Dictionary<string,object>>> Groups(List<Dictionary<string,object>> messages)
        {
            var groups=new List<List<Dictionary<string,object>>>();
            for(int i=0;i<messages.Count;i++)
            {
                var group=new List<Dictionary<string,object>>{messages[i]};
                if(Value(messages[i],"role")=="assistant" && messages[i].ContainsKey("tool_calls"))
                    while(i+1<messages.Count && (Value(messages[i+1],"role")=="tool" || IsCapture(messages[i+1])))group.Add(messages[++i]);
                groups.Add(group);
            }
            return groups;
        }
        private bool Protected(List<Dictionary<string,object>> group,List<Dictionary<string,object>> messages)
        {
            int taskIndex=task==null?-1:messages.IndexOf(task);
            return group.Any(m=>ReferenceEquals(m,task) || Value(m,"role")=="system" || Value(m,"content").StartsWith(ProjectMemory.Prefix,StringComparison.Ordinal) || Value(m,"tool_name")=="ask_user" ||
                (Value(m,"role")=="user"&&!IsCapture(m)&&messages.IndexOf(m)>=taskIndex));
        }
        private void Remember(List<Dictionary<string,object>> group)
        {
            foreach(var m in group)
            {
                string content=Value(m,"content");
                if(m.ContainsKey("tool_calls"))
                {
                    IEnumerable calls=m["tool_calls"] as IEnumerable;
                    if(calls!=null)foreach(object raw in calls)
                    {
                        var call=raw as Dictionary<string,object>;object function;
                        if(call==null||!call.TryGetValue("function",out function))continue;
                        var f=function as Dictionary<string,object>;if(f==null)continue;
                        object arguments;var a=f.TryGetValue("arguments",out arguments)?arguments as Dictionary<string,object>:null;
                        string target=a==null?"":string.Join(" ",new[]{"path","target","url","executable","working_directory"}.Select(k=>Value(a,k)).Where(v=>v.Length>0));
                        memory.Add("Инструмент: "+Value(f,"name")+" "+Clip(target,360));
                    }
                }
                if(content.Length>0)memory.Add(Value(m,"role")+(Value(m,"tool_name").Length>0?" "+Value(m,"tool_name"):"")+": "+Clip(content,400));
            }
            while(memory.Count>30)memory.RemoveAt(0);
        }
        private static string Tail(string text,int size){int start=Math.Max(0,text.Length-size);if(start<text.Length&&char.IsLowSurrogate(text[start]))start++;return text.Substring(start);}
        private static string Clip(string text,int size)
        {
            if(text.Length<=size)return text;int end=size/2;if(end>0&&char.IsHighSurrogate(text[end-1]))end--;
            return text.Substring(0,end)+" … "+Tail(text,size/2);
        }
        private void PutMemory(List<Dictionary<string,object>> messages,int maximum)
        {
            messages.RemoveAll(m=>Value(m,"role")=="assistant"&&Value(m,"content").StartsWith(MemoryPrefix,StringComparison.Ordinal));
            if(memory.Count==0)return;
            string content=string.Join("\n",memory.ToArray());if(content.Length>maximum)content=Tail(content,maximum);
            int index=task==null?-1:messages.IndexOf(task);if(index<0)index=messages.FindIndex(m=>Value(m,"role")!="system");
            messages.Insert(Math.Max(0,index),new Dictionary<string,object>{{"role","assistant"},{"content",MemoryPrefix+content+"\nПолные результаты сохранены в истории. При необходимости перечитай нужные файлы частями."}});
        }
        internal bool Prepare(List<Dictionary<string,object>> messages,int contextSize,double factor)
        {
            Changed=false;int reserve=Math.Max(2048,contextSize/4);Target=Math.Max(1024,(int)((contextSize-reserve)*factor));
            LastEstimate=Estimate(messages);if(LastEstimate<=Target)return true;
            if(task!=null && primaryText!=null && Value(task,"content").StartsWith(primaryText,StringComparison.Ordinal))
            {
                string content=Value(task,"content"),data=content.Substring(primaryText.Length);
                int dataLimit=Math.Max(600,(int)(3000*factor));
                if(data.Length>dataLimit)
                {
                    string headers=string.Join("\n",Regex.Matches(data,"(?m)^--- Файл: .*? ---").Cast<Match>().Select(m=>m.Value).Distinct());
                    var copy=new Dictionary<string,object>(task);copy["content"]=primaryText+"\n\n"+headers+"\n"+Clip(data,dataLimit)+"\n[Вложение показано фрагментами для экономии контекста. Полные файлы доступны по указанным путям через read_text_file с start_line и max_lines.]";
                    int index=messages.IndexOf(task);messages[index]=copy;task=copy;Changed=true;
                }
            }
            // Replace only request dictionaries; persisted history and wire messages remain intact.
            for(int i=0;i<messages.Count;i++)
            {
                var m=messages[i];if(!m.ContainsKey("thinking")||Value(m,"thinking").Length==0)continue;
                var copy=new Dictionary<string,object>(m);copy.Remove("thinking");messages[i]=copy;Changed=true;
            }
            int limit=Math.Max(800,(int)(4000*factor));
            for(int i=0;i<messages.Count;i++)
            {
                var m=messages[i];if(Value(m,"role")!="tool" || Value(m,"tool_name")=="ask_user")continue;
                string content=Value(m,"content");if(content.Length<=limit)continue;
                var copy=new Dictionary<string,object>(m);copy["content"]=Clip(content,limit)+"\n[Длинный результат сокращён клиентом. Полный текст остался в истории; для файла используй read_text_file с start_line и max_lines.]";messages[i]=copy;Changed=true;
            }
            while(Estimate(messages)>Target)
            {
                var candidate=Groups(messages).FirstOrDefault(g=>!Protected(g,messages) && !Value(g[0],"content").StartsWith(MemoryPrefix,StringComparison.Ordinal));
                if(candidate==null)break;
                Remember(candidate);foreach(var message in candidate)messages.Remove(message);
                PutMemory(messages,Math.Max(500,(int)(1600*factor)));Changed=true;
            }
            LastEstimate=Estimate(messages);return LastEstimate<=Target;
        }
    }
}
