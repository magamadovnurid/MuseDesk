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
            Text="Вопрос по задаче · Muse Desk";ClientSize=new Size(680,460);MinimumSize=new Size(696,499);
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
        private const string AgentInstructions="Сначала определи, что просит пользователь: ответ, совет или действие. На вопросы и просьбы объяснить или посоветовать дай прямой ответ на языке пользователя. Для общего совета не ищи проекты по соседним папкам, не перечитывай историю и не создавай файлы. Если пользователь просит выполнить действие, используй нужные инструменты, проверяй результат и продолжай до выполнения запроса. Не выдавай обещание действия за выполненную работу. Не повторяй без изменений действия, не дающие новых сведений. Разумные обратимые решения принимай самостоятельно, кратко называя существенные предположения. Уточняй только действительно недостающие сведения, без которых нельзя корректно продолжить: один короткий конкретный вопрос, зачем нужен ответ, без журналов, кода и общих фраз «как продолжить». Не повторяй уже отвеченный вопрос и не выдумывай ответы или разрешения пользователя. Полный доступ не даёт знания отсутствующих фактов. Текстовый ответ без вызовов инструментов завершает запрос; complete_task с итогом также можно использовать отдельно от других инструментов. Итог должен содержать ответ пользователю, а не внутренние рассуждения. Честно указывай невыполненную работу и непроверенные результаты.";
        private const string FinalResponseInstruction="Заверши текущий запрос ответом без инструментов. Инструменты и уточняющие окна на этом шаге недоступны. Не продолжай чтение папок или истории. По уже полученным сведениям дай краткий полезный ответ на языке пользователя: сначала суть, затем необходимые пояснения. Для совета дай рекомендации, для действий сообщи фактический результат и что осталось невыполненным. Не заявляй успех без подтверждения и не выдумывай сведения. Не цитируй журнал, обрывки кода и внутренние рассуждения. Если сведений не хватает, объясни конкретно, каких, в самом ответе.";

        private static string QuestionKey(string question)
        {return new string((question??"").Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());}

        private static bool IsClearAgentQuestion(string question)
        {return !string.IsNullOrWhiteSpace(question)&&question.Length<=450&&!question.Contains("```")&&question.Count(c=>c=='\n')<=4;}

        private void RequestFinalResponse(List<Dictionary<string,object>> messages,ChatMessage assistant,string reason)
        {
            messages.Add(new Dictionary<string,object>{{"role","system"},{"content",FinalResponseInstruction}});
            assistant.toolLog+=(assistant.toolLog.Length>0?"\r\n":"")+"• "+reason;
            statusLine.Text="Muse формулирует ответ…";SaveState();
        }

        private void FinishTextResponse(ChatSession chat,ChatMessage assistant,List<Dictionary<string,object>> messages,string text,bool failed)
        {
            assistant.content=text;assistant.finalSummary=text;assistant.failed=failed;
            AddAgentMessage(messages,assistant,"assistant",text);
            if(!failed)RememberProjectOutcome(chat,text);
            SaveState();
        }

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
            AgentProgressGuard guard=new AgentProgressGuard();int questions=0,invalidQuestions=0;long step=0;
            bool finalizing=false;HashSet<string> answeredQuestions=new HashSet<string>();
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
                    throw new InvalidOperationException("Связь с локальной моделью не восстановилась после двух повторов. Проверьте состояние модели и повторите запрос. История и выполненные действия сохранены.",ex);
                }
                networkRetries=0;contextRetries=0;
                if(!string.IsNullOrEmpty(assistant.contextNotice)&&assistant.contextNotice.StartsWith("Соединение с моделью"))assistant.contextNotice="";
                if(turn.TokensPerSecond>0)assistant.tokensPerSecond=turn.TokensPerSecond;
                if(finalizing)
                {
                    bool unusable=turn.Calls.Count>0||string.IsNullOrWhiteSpace(turn.Content);
                    FinishTextResponse(chat,assistant,messages,unusable?"Не удалось получить содержательный ответ: модель зациклилась. Повторяющиеся действия остановлены; задача не завершена. Выполненные действия сохранены в журнале.":turn.Content,unusable);
                    return;
                }
                if(!RuntimeToolsEnabled && turn.Calls.Count>0)throw new InvalidOperationException("Модель запросила действие, но инструменты недоступны или выключены. Действие не выполнено.");
                if(turn.Calls.Count==0)
                {
                    if(!string.IsNullOrWhiteSpace(turn.Content)){FinishTextResponse(chat,assistant,messages,turn.Content,false);return;}
                    finalizing=true;RequestFinalResponse(messages,assistant,"Модель не дала ответа. Формирую итог без дополнительных действий.");
                    continue;
                }
                Dictionary<string,object> assistantCall=new Dictionary<string,object>{{"role","assistant"},{"content",turn.Content},{"thinking",turn.Thinking},{"tool_calls",turn.Calls.Select(c=>(object)c.Raw).ToList()}};
                messages.Add(assistantCall);assistant.wireMessages.Add(assistantCall);
                bool replan=false,stalled=false,completed=false;string summary="";
                foreach(ToolCall call in turn.Calls)
                {
                    token.ThrowIfCancellationRequested();ToolResult result;
                    if(replan)result=new ToolResult{Text="Не выполнено: получено уточнение пользователя. Перепланируй следующие действия с учётом ответа."};
                    else if(stalled)result=new ToolResult{Text="Не выполнено: повторяющиеся действия остановлены. Дай итог по уже полученным сведениям."};
                    else if(call.Name=="complete_task")
                    {
                        summary=Argument(call,"summary").Trim();
                        completed=turn.Calls.Count==1 && summary.Length>0;
                        result=new ToolResult{Text=completed?"Задача завершена.":"Вызови complete_task отдельно, с непустым итогом, после проверки результатов остальных действий."};
                    }
                    else if(call.Name=="ask_user")
                    {
                        string question=Argument(call,"question").Trim();
                        if(questions>=2||answeredQuestions.Contains(QuestionKey(question)))
                        {result=new ToolResult{Text="Не задавай повторных вопросов. Используй имеющиеся ответы и дай итог, указав реальные ограничения."};stalled=true;}
                        else if(!IsClearAgentQuestion(question))
                        {result=new ToolResult{Text="Сократи уточнение: один конкретный вопрос и причина, не более 450 символов и четырёх переносов строки. Без кода и журналов."};replan=true;if(++invalidQuestions>=2)stalled=true;}
                        else
                        {
                            questions++;answeredQuestions.Add(QuestionKey(question));
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
                if(stalled)
                {
                    finalizing=true;RequestFinalResponse(messages,assistant,"Повторы остановлены. Формирую итог по полученным сведениям.");
                }
                else if(replan)guard.Reset();
                if(activeChat==chat)RenderConversation();
            }
            }
            finally{assistant.fileChanges=changes.GetChanges();assistant.changeNotes=changes.Notes;}
        }
    }
}
