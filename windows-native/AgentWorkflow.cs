using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MuseDeskNative
{
    internal sealed class AgentQuestionDialog : Form
    {
        internal readonly TextBox Answer;
        internal readonly RoundedButton ContinueButton,StopButton;
        internal AgentQuestionDialog(string question)
        {
            Text="Вопрос по задаче · Muse Desk";ClientSize=new Size(736,550);MinimumSize=new Size(752,589);
            BackColor=Color.White;StartPosition=FormStartPosition.CenterParent;MinimizeBox=false;MaximizeBox=false;ShowInTaskbar=false;
            Font=new Font("Segoe UI",10F);
            Panel heading=new Panel{Dock=DockStyle.Top,Height=104,BackColor=Color.White};
            heading.Controls.Add(new MuseMark{Tile=true,Location=new Point(28,28),Size=new Size(42,42)});
            heading.Controls.Add(new Label{Text="Нужно ваше уточнение",Font=new Font("Segoe UI Semibold",19F),AutoSize=true,Location=new Point(86,25)});
            heading.Controls.Add(new Label{Text="После ответа Muse продолжит текущую задачу",ForeColor=Color.FromArgb(112,112,112),AutoSize=true,Location=new Point(89,65)});
            Panel content=new Panel{Dock=DockStyle.Fill,Padding=new Padding(28,0,28,0)};
            RoundedComposerPanel questionCard=new RoundedComposerPanel{Dock=DockStyle.Fill,Radius=16,Padding=new Padding(16),BackColor=Color.FromArgb(247,247,247),BorderColor=Color.FromArgb(233,233,233)};
            questionCard.Controls.Add(new RichTextBox{Text=question,ReadOnly=true,Dock=DockStyle.Fill,BorderStyle=BorderStyle.None,BackColor=questionCard.BackColor,Font=new Font("Segoe UI",11F),DetectUrls=false,ScrollBars=RichTextBoxScrollBars.Vertical});
            Panel replyArea=new Panel{Dock=DockStyle.Bottom,Height=142,Padding=new Padding(0,14,0,0)};
            RoundedComposerPanel replyCard=new RoundedComposerPanel{Dock=DockStyle.Fill,Radius=16,Padding=new Padding(16),BackColor=Color.White,BorderColor=Color.FromArgb(220,220,220)};
            Answer=new TextBox{AccessibleName="Ответ или указания для продолжения задачи",Multiline=true,AcceptsReturn=true,Dock=DockStyle.Fill,BorderStyle=BorderStyle.None,Font=new Font("Segoe UI",11F),ScrollBars=ScrollBars.Vertical};
            replyCard.Controls.Add(Answer);replyArea.Controls.Add(replyCard);content.Controls.Add(questionCard);content.Controls.Add(replyArea);
            FlowLayoutPanel actions=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=78,Padding=new Padding(22,12,22,18),FlowDirection=FlowDirection.RightToLeft,WrapContents=false};
            ContinueButton=ToolApprovalDialog.ActionButton("Продолжить",DialogResult.None,true,164);ContinueButton.Enabled=false;
            StopButton=ToolApprovalDialog.ActionButton("Остановить",DialogResult.Cancel,false,150);
            Answer.TextChanged+=delegate{ContinueButton.Enabled=!string.IsNullOrWhiteSpace(Answer.Text);};
            ContinueButton.Click+=delegate{if(!string.IsNullOrWhiteSpace(Answer.Text))DialogResult=DialogResult.OK;};
            actions.Controls.AddRange(new Control[]{ContinueButton,StopButton});
            Controls.Add(content);Controls.Add(actions);Controls.Add(heading);content.BringToFront();CancelButton=StopButton;
            Shown+=delegate{Answer.Focus();};
        }
    }

    internal sealed class AgentProgressGuard
    {
        private readonly Queue<string> recent=new Queue<string>();
        internal bool Repeats(string signature)
        {
            recent.Enqueue(signature);while(recent.Count>12)recent.Dequeue();
            return recent.Count(s=>s==signature)>=3;
        }
        internal void Reset(){recent.Clear();}
    }

    public sealed partial class MainForm
    {
        private const string AgentInstructions="Выполняй запрос пошагово до результата, а не только описывай план. Число шагов не ограничено. После вызовов инструментов проверяй результат и продолжай незавершённую работу. Исправляй ошибки доступным способом; не повторяй без изменений неудачные действия. Если нужен выбор, недостающая информация или объяснение пользователя, вызывай ask_user и после его ответа продолжай ту же задачу. Не выдумывай ответы пользователя. Уточнение задачи не является запросом разрешения и не заменяется полным доступом. Когда запрос действительно выполнен и результат проверен доступными средствами, вызови complete_task отдельно от других инструментов, с кратким итогом и тем, что проверено. Не объявляй успех при ошибке или невыполненных требованиях. Для обычного вопроса complete_task должен содержать сам ответ. Промежуточный текст не завершает задачу.";

        private string AskAgentUser(string question,CancellationToken token)
        {
            token.ThrowIfCancellationRequested();string previous=statusLine.Text;statusLine.Text="Ожидаю вашего ответа…";
            try
            {
                using(AgentQuestionDialog dialog=new AgentQuestionDialog(question))
                using(System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer{Interval=100})
                {
                    dialog.Icon=Icon;timer.Tick+=delegate{if(token.IsCancellationRequested||isClosing)dialog.DialogResult=DialogResult.Cancel;};timer.Start();
                    DialogResult result=dialog.ShowDialog(this);token.ThrowIfCancellationRequested();
                    if(result!=DialogResult.OK)throw new OperationCanceledException("Задача остановлена пользователем.",token);
                    return dialog.Answer.Text.Trim();
                }
            }
            finally{if(!isClosing)statusLine.Text=previous;}
        }

        private void AddAgentMessage(List<Dictionary<string,object>> messages,ChatMessage assistant,string role,string content)
        {
            Dictionary<string,object> message=new Dictionary<string,object>{{"role",role},{"content",content}};
            messages.Add(message);assistant.wireMessages.Add(message);
        }

        private void ClarifyAgent(List<Dictionary<string,object>> messages,ChatMessage assistant,string question,CancellationToken token)
        {
            // Save the question before waiting; never manufacture consent or an answer.
            AddAgentMessage(messages,assistant,"assistant",question);
            assistant.toolLog+="\r\n• Вопрос: "+question;SaveState();
            string answer=AskAgentUser(question,token);
            AddAgentMessage(messages,assistant,"user",answer);
            assistant.toolLog+="\r\n• Ваш ответ: "+answer;SaveState();
        }

        private async Task RunAgentLoopAsync(ChatSession chat,ChatMessage assistant,CancellationToken token,
            Func<List<Dictionary<string,object>>,ChatMessage,CancellationToken,Task<ModelTurn>> stream)
        {
            List<Dictionary<string,object>> messages=await Task.Run(delegate{return BuildRequestMessages(chat,assistant);},token);
            assistant.wireMessages=new List<Dictionary<string,object>>();
            TaskChangeTracker changes=new TaskChangeTracker();
            ChatMessage userRequest=chat.messages.LastOrDefault(m=>m.role=="user");
            ContextBudget budget=new ContextBudget(messages,RuntimeToolsEnabled?System.Text.Encoding.UTF8.GetByteCount(json.Serialize(BuildToolDefinitions()))/2:0,userRequest!=null&&userRequest.files!=null&&userRequest.files.Count>0?userRequest.content??"":null);
            int effectiveContext=state.settings.contextSize,contextRetries=0,networkRetries=0,retryDelay=0;
            double budgetFactor=1;
            AgentProgressGuard guard=new AgentProgressGuard();int emptyTurns=0;long step=0;
            try
            {
            while(true)
            {
                if(retryDelay>0){await Task.Delay(retryDelay,token);retryDelay=0;}
                token.ThrowIfCancellationRequested();statusLine.Text="Muse выполняет задачу · шаг "+(++step);
                // Permission may have changed during a tool call in the previous turn.
                messages.RemoveAll(m=>GetString(m,"role")=="system" && GetString(m,"content").StartsWith("Текущий режим Muse Desk:"));
                messages.Insert(string.IsNullOrWhiteSpace(state.settings.systemPrompt)?0:1,new Dictionary<string,object>{{"role","system"},{"content",CurrentPermissionInstruction(chat)}});
                RefreshProjectMemory(chat,messages);
                if(!budget.Prepare(messages,effectiveContext,budgetFactor))
                    throw new InvalidOperationException("Основная задача, обязательные инструкции или вложения не помещаются в контекст модели даже после сокращения истории. Сократите большой запрос, передавайте файлы частями или выберите больший контекст в настройках, если хватает памяти. Повтор неизменённого запроса не поможет. История и выполненные изменения сохранены.");
                if(budget.Changed)
                {
                    assistant.contextNotice="Контекст сокращён автоматически · полная история сохранена";
                    if(!assistant.toolLog.Contains(assistant.contextNotice))assistant.toolLog+="\r\n• "+assistant.contextNotice;
                    SaveState();
                }
                ModelTurn turn;string previousContent=assistant.content,previousThinking=assistant.thinking;
                try{turn=await stream(messages,assistant,token);}
                catch(Exception ex)
                {
                    token.ThrowIfCancellationRequested();
                    if(!(ex is HttpRequestException) && !(ex is IOException) && !(ex is TimeoutException) && !(ex is TaskCanceledException))throw;
                    assistant.content=previousContent;assistant.thinking=previousThinking;
                    ModelApiException api=ex as ModelApiException;
                    if(api!=null && api.ContextExceeded)
                    {
                        if(++contextRetries>3)throw new InvalidOperationException("Модель повторно отклонила сокращённый контекст. История сохранена. Нужен более короткий исходный запрос или больший контекст в настройках; повтор без изменений не выполняется.");
                        if(api.ContextSize>0)effectiveContext=Math.Min(effectiveContext,api.ContextSize);
                        budgetFactor=Math.Min(budgetFactor*.7,budget.Estimate(messages)*.65/Math.Max(1,effectiveContext-Math.Max(2048,effectiveContext/4)));
                        assistant.contextNotice="Контекст переполнен — сокращаю запрос и продолжаю автоматически";
                        continue;
                    }
                    if(api!=null && api.Status>=400 && api.Status<500 && api.Status!=408 && api.Status!=429)
                        throw new InvalidOperationException("Модель отклонила запрос (HTTP "+api.Status+"): "+api.Message+" Повтор без изменения настроек не выполняется.");
                    if(++networkRetries<=2){assistant.contextNotice="Соединение с моделью прервалось — повторяю запрос ("+networkRetries+"/2)";retryDelay=networkRetries*700;continue;}
                    ClarifyAgent(messages,assistant,"Не удалось продолжить запрос к локальной модели: "+ex.Message+"\r\nПроверьте движок. Напишите «повторить» или уточните, как продолжить.",token);
                    networkRetries=0;
                    continue;
                }
                networkRetries=0;contextRetries=0;
                if(!string.IsNullOrEmpty(assistant.contextNotice)&&assistant.contextNotice.StartsWith("Соединение с моделью"))assistant.contextNotice="";
                if(turn.TokensPerSecond>0)assistant.tokensPerSecond=turn.TokensPerSecond;
                if(!RuntimeToolsEnabled && turn.Calls.Count>0)throw new InvalidOperationException("Модель запросила действие, но инструменты недоступны или выключены. Действие не выполнено.");
                if(turn.Calls.Count==0)
                {
                    AddAgentMessage(messages,assistant,"assistant",turn.Content);
                    if(!RuntimeToolsEnabled){assistant.finalSummary=turn.Content;RememberProjectOutcome(chat,turn.Content);assistant.fileChanges=changes.GetChanges();SaveState();return;}
                    if(++emptyTurns>=3)
                    {
                        ClarifyAgent(messages,assistant,"Модель несколько раз ответила текстом, но не обозначила завершение и не выполнила следующий шаг.\r\nПоследний ответ: "+Compact(turn.Content,1600)+"\r\nЧто нужно сделать дальше?",token);emptyTurns=0;
                    }
                    else messages.Add(new Dictionary<string,object>{{"role","system"},{"content","Продолжай действия инструментами. Если нужен ответ пользователя — ask_user. Если всё выполнено — complete_task с итогом. Один текст без этого не завершает задачу."}});
                    SaveState();continue;
                }
                emptyTurns=0;
                Dictionary<string,object> assistantCall=new Dictionary<string,object>{{"role","assistant"},{"content",turn.Content},{"thinking",turn.Thinking},{"tool_calls",turn.Calls.Select(c=>(object)c.Raw).ToList()}};
                messages.Add(assistantCall);assistant.wireMessages.Add(assistantCall);
                bool replan=false,stalled=false,completed=false;string summary="";
                foreach(ToolCall call in turn.Calls)
                {
                    token.ThrowIfCancellationRequested();ToolResult result;
                    if(replan)result=new ToolResult{Text="Не выполнено: получено уточнение пользователя. Перепланируй следующие действия с учётом ответа."};
                    else if(stalled)result=new ToolResult{Text="Не выполнено: обнаружены повторяющиеся результаты. Продолжение после уточнения пользователя."};
                    else if(call.Name=="complete_task")
                    {
                        summary=Argument(call,"summary").Trim();
                        completed=turn.Calls.Count==1 && summary.Length>0;
                        result=new ToolResult{Text=completed?"Задача завершена.":"Вызови complete_task отдельно, с непустым итогом, после проверки результатов остальных действий."};
                    }
                    else if(call.Name=="ask_user")
                    {
                        string question=Argument(call,"question").Trim();
                        if(question.Length==0)result=new ToolResult{Text="Укажи непустой вопрос пользователю."};
                        else
                        {
                            assistant.toolLog+="\r\n• Вопрос: "+question;SaveState();
                            string answer=AskAgentUser(question,token);result=new ToolResult{Text="Ответ пользователя: "+answer};replan=true;
                        }
                    }
                    else if(call.Name=="update_project_memory")result=UpdateProjectMemory(chat,call);
                    else if(call.Name=="recall_project_history")result=RecallProjectHistory(chat,call);
                    else result=await ExecuteToolAsync(call,token,changes,chat);
                    RecordProjectStep(chat,call,result);
                    assistant.fileChanges=changes.GetChanges();assistant.changeNotes=changes.Notes;
                    if(!string.IsNullOrEmpty(result.ResultPath)){if(chat.resultFiles==null)chat.resultFiles=new List<string>();if(!chat.resultFiles.Contains(result.ResultPath))chat.resultFiles.Add(result.ResultPath);}
                    if(!string.IsNullOrEmpty(result.SourceUrl)){if(chat.sourceUrls==null)chat.sourceUrls=new List<string>();if(!chat.sourceUrls.Contains(result.SourceUrl))chat.sourceUrls.Add(result.SourceUrl);}
                    assistant.toolLog+=(assistant.toolLog.Length>0?"\r\n":"")+"• "+call.Name+" → "+Compact(result.Text,240);
                    Dictionary<string,object> toolMessage=new Dictionary<string,object>{{"role","tool"},{"tool_name",call.Name},{"content",result.Text}};
                    messages.Add(toolMessage);assistant.wireMessages.Add(toolMessage);
                    if(!string.IsNullOrWhiteSpace(result.ImagePath) && File.Exists(result.ImagePath))
                    {
                        messages.Add(new Dictionary<string,object>{{"role","user"},{"content","Результат инструмента capture_screen."},{"images",new string[]{EncodeImage(result.ImagePath)}}});
                        assistant.wireMessages.Add(new Dictionary<string,object>{{"role","user"},{"content","Результат инструмента capture_screen."},{"_imagePath",result.ImagePath}});
                    }
                    if(call.Name!="ask_user" && !completed)
                        stalled=guard.Repeats(call.Name+"\n"+json.Serialize(call.Arguments)+"\n"+result.Text)||stalled;
                    SaveState();if(activeChat==chat)RenderConversation();
                }
                if(completed)
                {
                    assistant.finalSummary=summary;assistant.fileChanges=changes.GetChanges();assistant.changeNotes=changes.Notes;
                    assistant.content+=(assistant.content.Length>0?"\r\n\r\n":"")+summary;
                    AddAgentMessage(messages,assistant,"assistant",summary);RememberProjectOutcome(chat,summary);SaveState();return;
                }
                if(replan)guard.Reset();
                else if(stalled)
                {
                    ClarifyAgent(messages,assistant,"Одно и то же действие уже трижды вернуло одинаковый результат. Muse приостановила повторения.\r\nПоследние действия:\r\n"+Compact(assistant.toolLog.Length>1800?assistant.toolLog.Substring(assistant.toolLog.Length-1800):assistant.toolLog,1800)+"\r\nУточните, как продолжить, или напишите «попробуй другой способ».",token);
                    guard.Reset();
                }
                if(activeChat==chat)RenderConversation();
            }
            }
            finally{assistant.fileChanges=changes.GetChanges();assistant.changeNotes=changes.Notes;}
        }
    }
}
