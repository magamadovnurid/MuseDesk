using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MuseDeskNative
{
    internal sealed class ButtonPaintProbe : RoundedButton
    {
        internal void PaintOnly(Graphics graphics) { OnPaint(new PaintEventArgs(graphics,ClientRectangle)); }
    }
    internal sealed class PanelPaintProbe : RoundedComposerPanel
    {
        internal void PaintOnly(Graphics graphics){OnPaint(new PaintEventArgs(graphics,ClientRectangle));}
        internal bool RedrawOnResize {get{return GetStyle(ControlStyles.ResizeRedraw);}}
    }
    internal static class TestRunner
    {
        [STAThread] private static int Main(string[] args)
        {
            string output = Path.GetFullPath(args.Length>0 ? args[0] : "test-output");
            Directory.CreateDirectory(output);
            Environment.SetEnvironmentVariable("MUSE_DESK_TEST_DATA", Path.Combine(output,Guid.NewGuid().ToString("N")));
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                int result=0;
                using(MainForm form=new MainForm())
                {
                    form.ShowInTaskbar=false;form.Opacity=0;
                    form.Shown+=delegate {form.BeginInvoke((MethodInvoker)delegate
                    {
                        try {form.RunReviewTests(output,args.Contains("--live"));}
                        catch(Exception ex){Console.WriteLine("FAIL: "+ex);result=1;}
                        finally {form.Close();}
                    });};
                    Application.Run(form);
                }
                Console.WriteLine("Test UI shutdown completed.");return result;
            }
            catch (Exception ex) { Console.WriteLine("FAIL: "+ex); return 1; }
        }
    }

    public sealed partial class MainForm
    {
        private void SetReviewSize(Size desired)
        {
            Size=desired;PerformLayout();
            Check(Size==desired,"Requested test viewport: "+desired);
        }
        private static void Check(bool condition,string name)
        {
            if (!condition) throw new InvalidOperationException(name);
            Console.WriteLine("PASS: "+name);
        }
        private static T Pump<T>(Task<T> task,int timeoutSeconds)
        {
            Stopwatch clock=Stopwatch.StartNew();
            while (!task.IsCompleted && clock.Elapsed.TotalSeconds<timeoutSeconds) { Application.DoEvents(); Thread.Sleep(10); }
            if (!task.IsCompleted) throw new TimeoutException("Test exceeded "+timeoutSeconds+" seconds");
            return task.GetAwaiter().GetResult();
        }
        private static bool Pump(Task task,int timeoutSeconds)
        {
            Stopwatch clock=Stopwatch.StartNew();
            while (!task.IsCompleted && clock.Elapsed.TotalSeconds<timeoutSeconds) { Application.DoEvents(); Thread.Sleep(10); }
            if (!task.IsCompleted) throw new TimeoutException("Test exceeded "+timeoutSeconds+" seconds");
            task.GetAwaiter().GetResult(); return true;
        }
        private void CheckModelLifecycle()
        {
            var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();
            string previous=state.settings.baseUrl;string selected=state.settings.model;
            var requests=new List<string>();
            string other=json.Serialize(new{models=new[]{new{name="other-model",size_vram=1024}}});
            string ready=json.Serialize(new{models=new[]{new{name=selected,size_vram=2048}}});
            string[] replies={other,other,"{}","{\"models\":[]}","{}",ready};
            Task server=Task.Run(async delegate
            {
                foreach(string reply in replies)
                using(var client=await listener.AcceptTcpClientAsync())
                using(var stream=client.GetStream())
                {
                    string first,body;
                    using(var reader=new StreamReader(stream,Encoding.UTF8,false,1024,true))
                    {first=await reader.ReadLineAsync();string line;int length=0;
                    while(!string.IsNullOrEmpty(line=await reader.ReadLineAsync()))if(line.StartsWith("Content-Length:",StringComparison.OrdinalIgnoreCase))length=int.Parse(line.Substring(15).Trim());
                    char[] chars=new char[length];int offset=0;while(offset<length){int count=await reader.ReadAsync(chars,offset,length-offset);if(count==0)break;offset+=count;}body=new string(chars,0,offset);}
                    requests.Add(first+" "+body);
                    byte[] bytes=Encoding.UTF8.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: "+Encoding.UTF8.GetByteCount(reply)+"\r\nConnection: close\r\n\r\n"+reply);await stream.WriteAsync(bytes,0,bytes.Length);
                }
            });
            try
            {
                state.settings.baseUrl="http://127.0.0.1:"+((IPEndPoint)listener.LocalEndpoint).Port;
                Pump(PrepareSelectedModelAsync(CancellationToken.None),8);Pump(server,3);
                Check(requests[2].Contains("other-model")&&requests[2].Contains("\"keep_alive\":0"),"Previous model receives explicit unload before loading selection");
                Check(requests[3].StartsWith("GET /api/ps")&&requests[4].Contains(selected)&&requests[4].Contains("\"keep_alive\":-1"),"Selection preloads only after empty-memory confirmation and stays resident");
                Check(connectionTitle.Text=="Готово"&&!modelTransition,"Successful preload becomes ready only after residency verification");
            }
            finally{state.settings.baseUrl=previous;listener.Stop();}
        }
        private void CheckModelExclusion()
        {
            var selectedRow=new Dictionary<string,object>{{"name","Muse"},{"size_vram",1024L}};
            Check(!IsExclusiveGpuModel(new Dictionary<string,object>{{"models",new object[0]}},"Muse"),"Installed model is not marked GPU ready");
            Check(IsExclusiveGpuModel(new Dictionary<string,object>{{"models",new object[]{selectedRow}}},"Muse"),"Only the selected GPU-resident model is ready");
            Check(!IsExclusiveGpuModel(new Dictionary<string,object>{{"models",new object[]{selectedRow,selectedRow}}},"Muse"),"Two residents cannot show ready");
            Check(!IsExclusiveGpuModel(new Dictionary<string,object>{{"models",new object[]{selectedRow}}},"Qwen"),"Different resident cannot show selected model ready");
            selectedRow["size_vram"]=0L;
            Check(!IsExclusiveGpuModel(new Dictionary<string,object>{{"models",new object[]{selectedRow}}},"Muse"),"CPU-only residency is not GPU ready");
            var unloaded=new List<string>();int reads=0;
            Pump(RequireEmptyModelsAsync(()=>Task.FromResult(++reads<3?new List<string>{"Muse","Qwen"}:new List<string>()),name=>{unloaded.Add(name);return Task.FromResult(0);},CancellationToken.None),5);
            Check(unloaded.SequenceEqual(new[]{"Muse","Qwen"})&&reads==3,"Model loading waits until both old models disappear");
            using(var cancel=new CancellationTokenSource(150))
            {
                bool blocked=false;try{Pump(RequireEmptyModelsAsync(()=>Task.FromResult(new List<string>{"Muse"}),name=>Task.FromResult(0),cancel.Token),5);}catch(OperationCanceledException){blocked=true;}
                Check(blocked,"Stuck unload blocks loading instead of continuing");
            }
            bool failed=false;try{Pump(RequireEmptyModelsAsync(()=>Task.FromResult(new List<string>{"Muse"}),name=>{throw new IOException("unload failed");},CancellationToken.None),5);}catch(IOException){failed=true;}
            Check(failed,"Unload failure blocks next model");
        }
        private static void AddXml(ZipArchive zip,string path,string xml)
        {
            using (StreamWriter writer=new StreamWriter(zip.CreateEntry(path).Open(),new UTF8Encoding(false))) writer.Write(xml);
        }
        private List<Dictionary<string,object>> Request(string prompt)
        {
            return new List<Dictionary<string,object>> { new Dictionary<string,object>{{"role","user"},{"content",prompt}} };
        }
        internal void RunReviewTests(string output,bool live)
        {
            if(!live) modelProcessCount = name => 0;
            foreach(int size in new int[]{16,20,24,32,40,48,64,128,256})
            using(Bitmap icon=MuseBrand.Render(size,true))
            {
                Color stroke=icon.GetPixel(size/4,size/2);
                Check(icon.GetPixel(0,0).A==0 && stroke.R>220 && stroke.A>240,"Brand icon retains transparent corners and a readable white monogram at "+size+" px");
            }
            CheckButtonSurfaces(output);
            ShowInTaskbar=false; Opacity=0; Show(); SetReviewSize(new Size(1340,900)); PerformLayout();
            CheckMenuLifetime();
            CheckEngineStartupLifetime();
            CheckModelExclusion();
            CheckModelLifecycle();
            CheckProjectTree(output);
            CheckApplicationMenu(output);
            CheckThreeColumnScrollbars(output);
            CheckStyledDialogs(output);
            ModelStatus("Выгружаю предыдущую модель","Muse Glimmer · ожидание памяти",true,false);
            Check(!input.Enabled&&input.ReadOnly&&!sendButton.Enabled,"Composer and submit stay disabled during unloading");
            Check(connectionDot.Busy,"Model transition animates the status indicator");
            DesignPreview.Capture(this,Path.Combine(output,"model-unloading.png"));
            ModelStatus("Загружаю Muse Glimmer","В видеопамять · подождите",true,false);
            DesignPreview.Capture(this,Path.Combine(output,"model-loading.png"));
            ModelStatus("Готова к использованию","Muse Glimmer · в видеопамяти",false,true);
            Check(input.Enabled&&!input.ReadOnly&&sendButton.Enabled,"Composer and submit activate only with confirmed readiness");
            Check(!connectionDot.Busy&&connectionDot.ForeColor==Mint,"Confirmed ready status stops animation and turns green");
            DesignPreview.Capture(this,Path.Combine(output,"model-ready.png"));
            CheckComposerCaret();
            CheckPermissions(output);
            CheckAgentWorkflow(output);
            CheckUiPolish(output);
            CheckActionLog(output);
            CheckTaskSummary(output);
            CheckTextEncodings(output);
            CheckContextBudget(output);
            CheckProjectControls(output);
            CheckIconFidelity(output);
            if(Environment.GetEnvironmentVariable("MUSE_DESK_AGENT_LIVE")=="1")CheckLiveAgentWorkflow();
            Check(resultsHost.Visible && resultsCard.Visible && resultsCard.Controls.Count>0,"Results and sources panel is visible by default");
            CheckScrollBehavior();
            Check(center.Width>950 && sidebar.Width==254,"Native sidebar and chat layout");
            Check(input.Height>=24 && sendButton.Width==42,"Composer has editable area and send button");
            Check(sidebar.BackColor.GetBrightness()>0.9F && center.BackColor==Color.White && sendButton.BackColor.R==sendButton.BackColor.G,"Codex-style neutral palette and light workspace");
            sidebarToggle.PerformClick();PerformLayout();
            Check(!sidebar.Visible && center.Width>1200,"Sidebar can collapse without losing the editor");
            sidebarToggle.PerformClick();PerformLayout();
            Check(sidebar.Visible && sidebar.Width==254,"Sidebar can reopen with preserved width");
            bool previousThinking=state.settings.thinkingEnabled;
            composerThinkingButton.PerformClick();
            Check(state.settings.thinkingEnabled!=previousThinking && composerThinkingButton.Text.Contains(previousThinking?"выкл.":"вкл."),"Composer reasoning control updates the real setting");
            composerThinkingButton.PerformClick();
            using(RichTextBox longAnswer=MakeReadableText(string.Join("\n",Enumerable.Repeat("Длинный ответ должен оставаться доступным для чтения и прокрутки.",300).ToArray()),600,new Font("Segoe UI",11F),Color.White,Color.Black,1600))
            {
                FitRichText(longAnswer,1600);
                Check(longAnswer.ScrollBars==RichTextBoxScrollBars.Vertical,"Long answers retain scrolling after fitting the new message cards");
            }
            rightRail.Visible=true;PerformLayout();
            Check(center.Width>=790 && input.Width>=700,"Capabilities panel leaves room for the chat composer");
            rightRail.Visible=false;PerformLayout();
            string original=Path.Combine(output,"attachment.txt"); File.WriteAllText(original,"ORIGINAL DOCUMENT");
            string snapshot=SnapshotAttachment(original,activeChat.id); File.WriteAllText(original,"CHANGED SOURCE");
            Check(File.ReadAllText(snapshot)=="ORIGINAL DOCUMENT","Attachments are durable snapshots");
            ChatMessage user=new ChatMessage {role="user",content="Проверь документ и ответь по-русски."}; user.files.Add(snapshot);
            ChatMessage answer=new ChatMessage {role="assistant",content="## Результат\nДокумент прочитан. **Все данные** остаются локально.\n\n```python\nprint(17 * 23)\n```",thinking="Проверяю содержимое документа.",tokensPerSecond=25.4};
            activeChat.messages.Add(user);activeChat.messages.Add(answer); activeChat.title="Проверка Muse Desk";
            user.sentAt="2026-09-07T16:10:20.0000000Z";answer.completedAt="2026-09-07T16:10:35.0000000Z";
            Check(MessageTimestamp(new ChatMessage {role="user"})=="" && MessageTimestamp(new ChatMessage {role="assistant",completedAt="invalid"})=="","Legacy and invalid timestamps are not fabricated");
            Check(MessageTimestamp(user)==DateTimeOffset.Parse(user.sentAt).ToLocalTime().ToString("dd.MM.yyyy · HH:mm:ss",System.Globalization.CultureInfo.InvariantCulture),"Prompt timestamp uses local Windows time");
            activeChat.resultFiles.Add(original);activeChat.sourceUrls.Add("https://example.com/source");activeChat.referenceUrls.Add("https://example.com/reference");activeChat.projectPath=output;
            RefreshChatList();
            Check(chatList.Controls.OfType<RoundedButton>().Any(b=>b.IconName=="folder-open"),"Project groups display actual folder icons");
            Check(ChatLinks(activeChat).Count==2,"Results distinguish loaded sources and manually saved references");
            RenderConversation();
            Check(resultsBody.Controls.OfType<RoundedButton>().Any(b=>b.Text=="attachment.txt") && resultsBody.Controls.OfType<RoundedButton>().Any(b=>b.Text.Contains("example.com")),"Results panel lists real files, source links and attachments");
            Check(messageList.Controls.Count==2,"First message replaces welcome screen");
            Check(Descendants(messageList).OfType<Label>().Count(l=>l.AccessibleName=="Время отправки" || l.AccessibleName=="Время завершения ответа")==2,"Prompt and answer display separate timestamps");
            using(Control compact=BuildMessageCard(new ChatMessage {role="user",content="Короткий вопрос"},740))
            {
                Control bubble=compact.Controls[0];
                Check(bubble.BackColor==Color.Black && bubble.Width<300 && bubble.Height<65,"Short user messages use compact black bubbles without role headers");
                Check(bubble.Controls.OfType<RichTextBox>().Single().ForeColor==Color.White,"User text remains readable on its black bubble");
            }
            Control cached=messageList.Controls[0]; answer.content+="\nГотово."; RenderConversation();
            Check(object.ReferenceEquals(cached,messageList.Controls[0]),"Streaming does not rebuild unchanged messages");
            DesignPreview.Capture(this,Path.Combine(output,"native-window.png"));
            CheckStreamingBehavior(output);
            SetReviewSize(new Size(940,650)); PerformLayout();RenderConversation();
            Check(input.Width>=500 && input.Height>=24,"Minimum window keeps composer usable");
            string composerDraft=input.Text;input.Clear();int compactHeight=composerHost.Height;
            Check(compactHeight<170,"Empty composer removes the spare line of vertical space");
            input.Text=string.Join("\r\n",Enumerable.Repeat("Длинный промпт для проверки роста поля ввода.",100));Application.DoEvents();
            Check(composerHost.Height>compactHeight && composerHost.Height<=center.ClientSize.Height/3 && input.ScrollBars==ScrollBars.Vertical,"Long prompt grows upward only to one third of the workspace, then scrolls");
            input.Clear();Check(composerHost.Height==compactHeight && input.ScrollBars==ScrollBars.None,"Clearing a long prompt restores the compact composer");input.Text=composerDraft;
            Check(composerThinkingButton.PointToScreen(new Point(composerThinkingButton.Width,0)).X<=micButton.PointToScreen(Point.Empty).X,"Model and reasoning controls do not overlap voice buttons in narrow window");
            Check(composerThinkingButton.PointToScreen(new Point(composerThinkingButton.Width,0)).X<=composerModelButton.PointToScreen(Point.Empty).X && composerModelButton.PointToScreen(new Point(composerModelButton.Width,0)).X<=micButton.PointToScreen(Point.Empty).X,"Narrow composer keeps reasoning left and model selector right with no overlap");
            DesignPreview.Capture(this,Path.Combine(output,"native-window-small.png"));
            SaveState(); activeChat.title="Version two"; SaveState();
            StoredState roundTrip=LoadState(); Check(roundTrip.chats.First(x=>x.id==activeChat.id).messages.Count==2,"History survives reload");
            ChatSession savedDetails=roundTrip.chats.First(x=>x.id==activeChat.id);
            Check(savedDetails.messages[0].sentAt==user.sentAt && savedDetails.messages[1].completedAt==answer.completedAt,"Prompt and completion timestamps survive history reload");
            Check(savedDetails.projectPath==output && savedDetails.resultFiles.Contains(original) && savedDetails.referenceUrls.Count==1,"Project folders and results survive history reload");
            File.WriteAllText(statePath,"{broken"); StoredState recovered=LoadState();
            Check(recovered.chats.First(x=>x.id==activeChat.id).title=="Проверка Muse Desk","Corrupt history recovers backup");
            Check(Directory.GetFiles(Path.GetDirectoryName(statePath),"*.damaged-*").Length>0,"Damaged history is preserved");
            state=recovered; activeChat=state.chats.First(x=>x.id==activeChat.id); SaveState();
            List<Dictionary<string,object>> wire=BuildRequestMessages(activeChat,null);
            Check(wire.Any(x=>GetString(x,"content").Contains("ORIGINAL DOCUMENT")),"Document snapshot reaches model context");
            answer=new ChatMessage {role="assistant",content="FAILED_SENTINEL",failed=true};activeChat.messages.Add(answer);
            Check(!BuildRequestMessages(activeChat,null).Any(x=>GetString(x,"content").Contains("FAILED_SENTINEL")),"Failed response excluded from subsequent context");activeChat.messages.Remove(answer);
            state.settings.model=PreferredModel;PopulateModels(new List<string>{PreferredModel,"test:secondary"});state.settings.model="test:secondary"; modelCombo.SelectedItem="test:secondary";PopulateModels(new List<string>{PreferredModel,"test:secondary"});
            Check(state.settings.model=="test:secondary","Health refresh preserves model selection");state.settings.model=PreferredModel;
            string qwenModel="huihui_ai/Qwen3.8-abliterated:27b";
            PopulateModels(new List<string>{PreferredModel,qwenModel});
            modelCombo.SelectedItem=qwenModel;
            Check(state.settings.model==qwenModel,"Selecting Qwen uses its exact installed model identifier");
            PopulateModels(new List<string>{qwenModel,PreferredModel});
            Check(state.settings.model==qwenModel && Convert.ToString(modelCombo.SelectedItem)==qwenModel,"Model refresh and reorder preserve selected Qwen alongside Muse");
            modelCombo.SelectedItem=PreferredModel;
            Check(state.settings.model==PreferredModel && modelCombo.Items.Contains(qwenModel),"Switching back to Muse does not remove Qwen");
            bool remoteRejected=false; try { NormalizeUrl("https://example.com"); } catch { remoteRejected=true; }
            Check(remoteRejected,"Engine connection restricted to local host");
            string docx=Path.Combine(output,"test.docx");
            using (ZipArchive zip=ZipFile.Open(docx,ZipArchiveMode.Create)) AddXml(zip,"word/document.xml","<w:document xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main'><w:body><w:p><w:r><w:t>Проверка документа</w:t></w:r></w:p></w:body></w:document>");
            Check(ExtractOfficeText(docx)=="Проверка документа","DOCX text extraction");
            string xlsx=Path.Combine(output,"test.xlsx");
            using (ZipArchive zip=ZipFile.Open(xlsx,ZipArchiveMode.Create))
            {
                AddXml(zip,"xl/workbook.xml","<workbook xmlns='http://schemas.openxmlformats.org/spreadsheetml/2006/main' xmlns:r='http://schemas.openxmlformats.org/officeDocument/2006/relationships'><sheets><sheet name='Продажи' r:id='rId1'/></sheets></workbook>");
                AddXml(zip,"xl/_rels/workbook.xml.rels","<Relationships><Relationship Id='rId1' Target='worksheets/sheet1.xml'/></Relationships>");
                AddXml(zip,"xl/sharedStrings.xml","<sst xmlns='http://schemas.openxmlformats.org/spreadsheetml/2006/main'><si><t>Доход</t></si><si><t>Прибыль</t></si></sst>");
                AddXml(zip,"xl/worksheets/sheet1.xml","<worksheet xmlns='http://schemas.openxmlformats.org/spreadsheetml/2006/main'><sheetData><row r='1'><c r='A1' t='s'><v>1</v></c><c r='B1'><v>391</v></c></row></sheetData></worksheet>");
            }
            string sheet=ExtractOfficeText(xlsx); Check(sheet.Contains("Продажи")&&sheet.Contains("A1=Прибыль")&&sheet.Contains("B1=391"),"XLSX shared strings resolve to correct cells");
            string pptx=Path.Combine(output,"test.pptx");
            using (ZipArchive zip=ZipFile.Open(pptx,ZipArchiveMode.Create)) foreach (int n in new int[]{10,2,1}) AddXml(zip,"ppt/slides/slide"+n+".xml","<a:p xmlns:a='urn:a'><a:r><a:t>Page "+n+"</a:t></a:r></a:p>");
            string slides=ExtractOfficeText(pptx); Check(slides.IndexOf("Page 2")<slides.IndexOf("Page 10"),"PPTX slides sorted numerically");
            string evil=Path.Combine(output,"external.docx");
            using (ZipArchive zip=ZipFile.Open(evil,ZipArchiveMode.Create)) AddXml(zip,"word/document.xml","<!DOCTYPE x [<!ENTITY a SYSTEM 'file:///c:/windows/win.ini'>]><x>&a;</x>");
            bool blocked=false; try { ExtractOfficeText(evil); } catch (System.Xml.XmlException) { blocked=true; }
            Check(blocked,"External XML entities rejected");
            string fixture=Path.Combine(output,"vision-fixture.png");
            using (Bitmap bitmap=new Bitmap(640,400)) using (Graphics g=Graphics.FromImage(bitmap))
            { g.Clear(Color.White);g.FillEllipse(Brushes.Red,60,110,170,170);g.FillRectangle(Brushes.Blue,350,110,170,170);bitmap.Save(fixture); }
            Check(Convert.FromBase64String(EncodeImage(fixture)).Length>500,"Image encoded for model vision input");
            state.settings.contextSize=8192;state.settings.thinkingEnabled=false;state.settings.toolsEnabled=false;
            CheckCancellation();
            using (Process process=Process.Start(new ProcessStartInfo("powershell.exe","-NoProfile -NonInteractive -Command \"[Console]::Write('ready'); Start-Sleep -Seconds 10\"") { UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true }))
            using (CancellationTokenSource cancellation=new CancellationTokenSource(300))
            {
                Stopwatch timer=Stopwatch.StartNew();bool interrupted=false;
                try {Pump(CollectProcessAsync(process,cancellation.Token),4);} catch (OperationCanceledException) {interrupted=true;}
                Check(interrupted&&timer.ElapsedMilliseconds<2000&&process.HasExited,"Stop cancels a running local program");
            }
            if (synthesizer!=null) { synthesizer.SetOutputToWaveFile(Path.Combine(output,"voice-test.wav"));synthesizer.Speak("Проверка Muse Desk.");synthesizer.SetOutputToDefaultAudioDevice();Check(new FileInfo(Path.Combine(output,"voice-test.wav")).Length>1000,"Windows voice synthesizes audio"); }
            else Console.WriteLine("UNAVAILABLE: Windows speech engine in this process; button disabled with explanation.");
            if (live)
            {
                state.settings.baseUrl="http://127.0.0.1:11436";
                ModelTurn text=Pump(StreamTurnAsync(Request("Ответь кратко: сколько будет 17 умножить на 23?"),new ChatMessage(),CancellationToken.None),180);
                Check(text.Content.Contains("391"),"LIVE Muse text arithmetic");Console.WriteLine("TEXT: "+text.Content+" | tokens/s: "+text.TokensPerSecond);
                List<Dictionary<string,object>> visual=Request("Назови две фигуры на изображении и их цвета слева направо. Одно предложение по-русски.");visual[0]["images"]=new string[]{EncodeImage(fixture)};
                ModelTurn vision=Pump(StreamTurnAsync(visual,new ChatMessage(),CancellationToken.None),180);
                string v=vision.Content.ToLowerInvariant();Check(v.Contains("красн")&&(v.Contains("круг")||v.Contains("окруж"))&&v.Contains("син")&&v.Contains("квадрат"),"LIVE Muse vision: red circle and blue square");Console.WriteLine("VISION: "+vision.Content);
                state.settings.toolsEnabled=true;
                ChatSession toolChat=new ChatSession {id=Guid.NewGuid().ToString("N"),title="Tool test"};
                toolChat.messages.Add(new ChatMessage {role="user",content="Используй только инструмент get_current_time, затем назови текущую дату и время по его результату. Другие инструменты не нужны."});
                ChatMessage toolAnswer=new ChatMessage {role="assistant"};toolChat.messages.Add(toolAnswer);
                Pump(RunAgentAsync(toolChat,toolAnswer,CancellationToken.None),180);
                Check(toolAnswer.toolLog.Contains("get_current_time")&&toolAnswer.content.Length>10&&toolAnswer.wireMessages.Any(x=>GetString(x,"role")=="tool"),"LIVE Muse complete tool call and response cycle");Console.WriteLine("TOOLS: "+toolAnswer.toolLog+"\n"+toolAnswer.content);
                string persisted=json.Serialize(toolChat);ChatSession loaded=json.Deserialize<ChatSession>(persisted);
                Check(BuildRequestMessages(loaded,null).Any(x=>GetString(x,"role")=="tool"),"Tool context survives disk serialization");
            }
            Console.WriteLine("ALL CHECKS PASSED");
            SetReviewSize(new Size(1340,900));activeChat.messages.Clear();activeChat.title="Новый диалог";RefreshChatList();RenderConversation();
            connectionTitle.Text="Muse готова";connectionDetail.Text="На вашем компьютере";connectionDot.BackColor=Mint;
            state.settings.model=PreferredModel;PopulateModels(new List<string>{PreferredModel});installButton.Visible=false;
            DesignPreview.Capture(this,Path.Combine(output,"design-welcome.png"));
            activeChat.title="Идеи для нового проекта";
            activeChat.messages.Add(new ChatMessage {role="user",content="Помоги превратить идею в понятный план. С чего лучше начать?"});
            activeChat.messages.Add(new ChatMessage {role="assistant",content="## Давайте начнём с главного\n\nОпишите результат, который хотите получить, и для кого он нужен. Я помогу собрать идею в последовательный план.\n\n**Предлагаю три шага:**\n\n- Сформулировать задачу одним предложением.\n- Выбрать самое важное для первой версии.\n- Превратить это в несколько конкретных действий.\n\nМожете приложить заметки, документ или изображение — разберём их вместе.",tokensPerSecond=25.4});
            RefreshChatList();RenderConversation();DesignPreview.Capture(this,Path.Combine(output,"design-conversation.png"));
            rightRail.Visible=true;LayoutWorkspace();RenderConversation();DesignPreview.Capture(this,Path.Combine(output,"design-capabilities.png"));
            rightRail.Visible=false;SetReviewSize(new Size(940,650));RenderConversation();DesignPreview.Capture(this,Path.Combine(output,"design-small.png"));
            using(SettingsDialog settings=new SettingsDialog(new UserSettings())){settings.Opacity=0;settings.ShowInTaskbar=false;settings.Show();DesignPreview.Capture(settings,Path.Combine(output,"design-settings.png"));}
        }

        private static IEnumerable<Control> Descendants(Control parent)
        {
            foreach(Control child in parent.Controls){yield return child;foreach(Control nested in Descendants(child))yield return nested;}
        }
        private void CheckIconFidelity(string output)
        {
            Check(IconAssets.Count==25,"All 25 original Muse Desk interface icons are bundled");
            foreach(string name in new[]{"folder","folder-open","edit","copy","check","settings","sliders","search","more","mic","sidebar","results"})
            {
                Check(IconAssets.Has(name),"Source icon is available: "+name);
                foreach(int size in new[]{16,20,32,40})using(Bitmap bitmap=new Bitmap(size,size))using(Graphics g=Graphics.FromImage(bitmap))
                {
                    g.Clear(Color.Transparent);MuseIcons.Draw(g,name,new RectangleF(0,0,size,size),InterfaceTypography.Secondary);
                    int visible=0;bool colored=false;for(int y=0;y<size;y++)for(int x=0;x<size;x++){Color c=bitmap.GetPixel(x,y);if(c.A>100){visible++;if(Math.Abs(c.R-c.G)>8||Math.Abs(c.G-c.B)>8)colored=true;}}
                    Check(visible>3&&!colored,"Source icon is visible and neutral at "+size+" px: "+name);
                }
            }
            foreach(Button action in sidebar.Controls.OfType<Button>().Where(b=>new[]{"Новый чат","Возможности","Добавить файлы","Настройки"}.Contains(b.Text)))
                Check(action.Font.Name=="Segoe UI"&&Math.Abs(action.Font.SizeInPoints-10.5F)<.01F&&action.Font.Style==FontStyle.Regular,"Sidebar action matches 14 px regular typography: "+action.Text);
            using(RoundedButton copy=MakeCopyButton(()=>"Проверка копирования","Копировать",Color.White))
                Check(copy.IconPixelSize==16&&copy.Width==30&&copy.Text==""&&copy.IconTint==InterfaceTypography.Tertiary&&copy.IconHoverTint==InterfaceTypography.Secondary,"Copy uses the small Muse Desk glyph, a 30 px hit target, subtle default tint and no visible label");
            List<ChatSession> original=state.chats;ChatSession current=activeChat;Size oldSize=Size;
            try
            {
                var chat=new ChatSession{id="icon-review-chat",projectPath=@"C:\Projects\qwenchat",title="Создать чат-приложение Qwen"};
                chat.messages.Add(new ChatMessage{role="user",content="сними сам скриншот"});
                chat.messages.Add(new ChatMessage{role="assistant",content="Размеры панелей, шрифты, отступы, кнопки, иконки и поле ввода.\r\n\r\nНазвания чатов и пункты меню используют один размер шрифта. Папки и кнопки действий собраны из исходных значков интерфейса."});
                state.chats=new List<ChatSession>{new ChatSession{id="icon-review-project",projectPath=@"C:\Projects\matem",title="Создать локальный веб-сайт"},chat};activeChat=chat;
                SetReviewSize(new Size(1540,908));RefreshChatList();RenderConversation();PerformLayout();Application.DoEvents();
                var labels=Descendants(chatList).OfType<RoundedButton>().Where(b=>b.Text==chat.title||b.Text=="qwenchat"||b.Text=="matem").ToList();
                Check(labels.Count==3&&labels.All(b=>b.Font.Name=="Segoe UI"&&Math.Abs(b.Font.SizeInPoints-10.5F)<.01F),"Chat names and project names share the original 14 px Segoe UI scale");
                Check(labels.Where(b=>b.IconName=="folder-open").All(b=>b.IconTint==InterfaceTypography.Secondary),"Project-folder artwork uses the same secondary tint as navigation icons");
                DesignPreview.Capture(this,Path.Combine(output,"icon-fidelity-window.png"));
                SetReviewSize(new Size(940,650));PerformLayout();Application.DoEvents();
                Check(labels.All(b=>b.Height>=b.Font.Height+6),"Larger chat labels retain sufficient line height in the narrow window");
                DesignPreview.Capture(this,Path.Combine(output,"icon-fidelity-small.png"));
            }
            finally{state.chats=original;activeChat=current;SetReviewSize(oldSize);RefreshChatList();RenderConversation();}
        }

        private void CheckUiPolish(string output)
        {
            foreach(Size size in new Size[]{new Size(740,132),new Size(516,178),new Size(280,220)})
            using(Panel parent=new Panel{BackColor=Color.White})
            using(PanelPaintProbe panel=new PanelPaintProbe{Size=size,Radius=20,BorderColor=Color.FromArgb(220,220,220)})
            using(Bitmap bitmap=new Bitmap(size.Width,size.Height))
            using(Graphics g=Graphics.FromImage(bitmap))
            {
                parent.Controls.Add(panel);g.Clear(Color.Magenta);panel.PaintOnly(g);
                Check(panel.RedrawOnResize && bitmap.GetPixel(0,0).ToArgb()==Color.White.ToArgb() && bitmap.GetPixel(size.Width-1,size.Height-1).ToArgb()==Color.White.ToArgb(),"Rounded panels repaint all corners and invalidate on resize: "+size);
                bool straight=bitmap.GetPixel(size.Width/2,size.Height-2).R<250;int baseline=bitmap.GetPixel(size.Width/2,size.Height-2).ToArgb();
                for(int x=30;x<size.Width-30;x++)if(bitmap.GetPixel(x,size.Height-2).ToArgb()!=baseline)straight=false;
                Check(straight,"Composer lower border is one straight consistent line: "+size);
                bool symmetric=true;
                for(int y=0;y<24;y++)for(int x=0;x<24;x++)
                {if(Math.Abs(bitmap.GetPixel(x,y).R-bitmap.GetPixel(size.Width-1-x,y).R)>6)symmetric=false;}
                Check(symmetric,"Left and right rounded panel contours have matching raster geometry: "+size);
            }
            RoundedComposerPanel composer=Descendants(this).OfType<RoundedComposerPanel>().Single(p=>p.Name=="ChatComposer");
            Check(composer.Padding.Left==composer.Padding.Right && composer.Padding.Bottom>=12,"Composer protects its lower corners with balanced inner spacing");
            Check(input.Font.SizeInPoints==10.5F && input.ForeColor==TextInk,"Composer typography uses the shared neutral 14-pixel text scale");
            Check(resultsCard.ShadowDepth==4 && resultsBody.Left>=16 && resultsBody.Bottom<=resultsCard.Height-16,"Results content and scrollbar stay inside the rounded floating card");

            Action<string> savedWriter=clipboardWriter;string copied=null;clipboardWriter=text=>copied=text;
            ChatMessage message=new ChatMessage{role="assistant",content="## Итог\r\nОписание магазина.\r\n\r\n```html\r\n<h1>Магазин</h1>\r\n```\r\n\r\n```css\nbody { color: #232323; }\n```\nГотово. 😀",thinking="Проверяю структуру.",toolLog="Создан index.html\r\nПроверена разметка"};
            expandedThoughts.Add(message);
            try
            {
                using(Control row=BuildMessageCard(message,740))
                using(Form preview=new Form{Text="UI blocks review",ClientSize=new Size(780,Math.Min(1100,row.Height+40)),BackColor=Color.White,Opacity=0,ShowInTaskbar=false})
                {
                    row.Location=new Point(20,20);preview.Controls.Add(row);preview.Show();
                    RoundedButton whole=Descendants(row).OfType<RoundedButton>().Single(b=>b.AccessibleName=="Скопировать весь ответ");
                    whole.PerformClick();Check(copied==message.content,"Whole-answer button copies every character including all fenced code blocks and emoji");
                    Check(whole.Text=="" && whole.IconOnly && whole.IconName=="check" && whole.Width==30,"Copy button uses only a compact icon with a success checkmark and no text");
                    RoundedButton html=Descendants(row).OfType<RoundedButton>().Single(b=>b.AccessibleName=="Скопировать блок: html");html.PerformClick();
                    Check(copied=="<h1>Магазин</h1>\r\n","Code block button copies code only, without language label or fences");
                    RoundedButton css=Descendants(row).OfType<RoundedButton>().Single(b=>b.AccessibleName=="Скопировать блок: css");css.PerformClick();
                    Check(copied=="body { color: #232323; }\n","Each code block has an independent copy target");
                    Descendants(row).OfType<RoundedButton>().Single(b=>b.AccessibleName=="Скопировать рассуждение").PerformClick();
                    Check(copied==message.thinking,"Reasoning copy works independently of answer text");
                    Descendants(row).OfType<RoundedButton>().Single(b=>b.AccessibleName=="Скопировать блок: Действия").PerformClick();
                    Check(copied==message.toolLog,"Action block copies its full log, not the shortened preview");
                    string originalContent=message.content;message.content=new string('Ж',180000)+"\r\nПоследняя строка";whole.PerformClick();
                    Check(copied==message.content,"Whole-answer copy is not truncated by the visible block height or preview limits");message.content=originalContent;
                    Check(Descendants(row).OfType<RichTextBox>().All(r=>r.BackColor.R==r.BackColor.G && r.BackColor.G==r.BackColor.B),"All answer, code, reasoning and action surfaces use a consistent neutral palette");
                    Check(Descendants(row).OfType<RoundedButton>().Where(b=>b.AccessibleName!=null && b.AccessibleName.StartsWith("Скопировать")).All(b=>b.Left>=0 && b.Right<=b.Parent.Width && b.Bottom<=b.Parent.Height),"Every copy button fits inside its own answer block");
                    DesignPreview.Capture(preview,Path.Combine(output,"answer-blocks.png"));preview.Close();
                }
                using(Control stream=BuildStreamingRow(message,740))
                using(Form fixture=new Form{Opacity=0,ShowInTaskbar=false})
                {
                    fixture.Controls.Add(stream);fixture.Show();
                    message.content+="\nНовый полученный фрагмент";
                    Descendants(stream).OfType<RoundedButton>().Single(b=>b.AccessibleName=="Скопировать весь ответ").PerformClick();
                    Check(copied==message.content,"Streaming copy reads the latest complete received text, not a stale snapshot");
                }
            }
            finally{clipboardWriter=savedWriter;expandedThoughts.Remove(message);}
        }

        private void CheckActionLog(string output)
        {
            string raw="Код завершения: 1\n\nСтандартный вывод (stdout):\n"+string.Join("\n",Enumerable.Range(1,16).Select(i=>"["+i.ToString("00")+"] Проверка ресурса проекта site: структура папок и ссылки на изображения корректны.").ToArray())+"\n\nДиагностика (stderr):\n\u001b[31mОшибка: отсутствует index.html\u001b[0m\nФинальная строка 😀";
            ToolCall first=WorkflowCall("run_process","executable","powershell.exe","arguments","-NoProfile -File build.ps1","working_directory","C:\\Projects\\site");
            ToolCall second=WorkflowCall("read_text_file","path","C:\\Projects\\site\\package.json");
            ChatMessage message=new ChatMessage{role="assistant",content="Проверяю сборку.",toolLog="• run_process → Сокращённая запись",wireMessages=new List<Dictionary<string,object>>{
                new Dictionary<string,object>{{"role","assistant"},{"tool_calls",new object[]{first.Raw,second.Raw}}},
                new Dictionary<string,object>{{"role","tool"},{"tool_name","run_process"},{"content",raw}},
                new Dictionary<string,object>{{"role","tool"},{"tool_name","read_text_file"},{"content","{\n  \"name\": \"site\"\n}"}}
            }};
            var entries=ActionEntries(message);
            Check(entries.Count==2 && entries[0].Output.Contains("Финальная строка 😀") && entries[0].Output.Length>1200,"Action journal restores complete results from saved protocol, beyond the old 240-character preview");
            Check(!entries[0].Output.Contains("\u001b") && entries[0].Output.Contains("\r\n"),"Action output removes terminal escapes while preserving Unicode and line breaks");
            Check(entries[0].Input.Contains("Рабочая папка:\r\nC:\\Projects\\site") && entries[0].Target.Contains("build.ps1"),"Action journal separates command, arguments and working directory");
            Check(entries[0].Status=="Ошибка · код 1" && entries[1].Status=="Завершено","Action status distinguishes command failure from tool completion");
            Check(ReadActionStatus("Действие не разрешено пользователем.")=="Нет доступа" && ReadActionStatus("Не выполнено: уточнение")=="Пропущено","Action journal distinguishes denied and skipped actions");
            Check(ReadActionStatus("Код завершения: 0\nПредупреждение")=="Код 0","Successful process exit is not inferred from arbitrary diagnostic text");
            ChatMessage restored=json.Deserialize<ChatMessage>(json.Serialize(message));
            Check(ActionEntries(restored).Count==2 && FullActionLogText(restored).Contains("Финальная строка 😀"),"Structured action journal survives history serialization without executing tools");
            Action<string> saved=clipboardWriter;string copied=null;clipboardWriter=value=>copied=value;
            try
            {
                using(Control row=BuildMessageCard(message,740))
                using(Form preview=new Form{ClientSize=new Size(780,1050),BackColor=Color.White,Opacity=0,ShowInTaskbar=false})
                {
                    preview.Controls.Add(row);row.Location=new Point(20,20);preview.Show();
                    Check(Descendants(row).Count(c=>c.Name=="ActionRow")==2 && !Descendants(row).Any(c=>c.AccessibleName=="Действие: Результат"),"Action journal starts with compact rows rather than a wall of output");
                    int before=row.Height;
                    Descendants(row).OfType<RoundedButton>().Single(b=>b.AccessibleName=="Раскрыть действие 1").PerformClick();
                    Check(row.Height>before && Descendants(row).OfType<RichTextBox>().Any(b=>b.Text.Contains("Финальная строка 😀")),"Expanding an action reveals the full scrollable result and resizes its message");
                    Descendants(row).OfType<RoundedButton>().Single(b=>b.AccessibleName=="Скопировать действие 1").PerformClick();
                    Check(copied.Contains("Финальная строка 😀") && copied.Contains("build.ps1"),"Action copy includes full command and result, not the collapsed summary");
                    Descendants(row).OfType<RoundedButton>().Single(b=>b.AccessibleName=="Скопировать блок: Действия").PerformClick();
                    Check(copied.Contains("package.json") && copied.Contains("Финальная строка 😀"),"Journal copy includes every action and complete output");
                    Check(Descendants(row).OfType<RoundedButton>().Where(b=>(b.AccessibleName??"").StartsWith("Скопировать")).All(b=>b.IconOnly && b.Text==""),"Every action copy control is icon-only");
                    DesignPreview.Capture(preview,Path.Combine(output,"action-log-expanded.png"));
                    Descendants(row).OfType<RoundedButton>().Single(b=>b.AccessibleName=="Раскрыть действие 1").PerformClick();
                    Check(row.Height==before,"Collapsing an action restores the original message height without gaps");
                    DesignPreview.Capture(preview,Path.Combine(output,"action-log-compact.png"));
                }
                using(Control narrow=BuildMessageCard(message,320))
                    Check(Descendants(narrow).OfType<RoundedButton>().Where(b=>(b.AccessibleName??"").StartsWith("Скопировать")).All(b=>b.Left>=0&&b.Right<=b.Parent.Width),"Action copy buttons remain inside a narrow chat column");
                ChatMessage pending=new ChatMessage{role="assistant",canceled=true,wireMessages=new List<Dictionary<string,object>>{message.wireMessages[0]}};
                Check(ActionEntries(pending).All(e=>!e.HasResult&&e.Status=="Остановлено"),"Interrupted calls are never displayed as completed results");
                using(Control row=BuildStreamingRow(message,740))
                {
                    StreamView view=(StreamView)row.Tag;UpdateStreamActions(view);
                    Check(view.Actions!=null && Descendants(row).Count(c=>c.Name=="ActionRow")==2,"Action journal is available during streaming independently of response text");
                    var same=view.Actions;UpdateStreamActions(view);Check(ReferenceEquals(same,view.Actions),"Unchanged live action rows are not rebuilt on every animation frame");
                }
            }
            finally{clipboardWriter=saved;expandedActions.Remove(message);collapsedActionLogs.Remove(message);}
        }

        private void CheckTaskSummary(string output)
        {
            int add,remove;
            Check(TaskChangeTracker.CountLines("a\nb\nc\n","a\nB\nc\nd\n",out add,out remove)&&add==2&&remove==1,"Line diff counts replacements as one deletion plus additions");
            Check(TaskChangeTracker.CountLines("a\r\nb\r\n","a\nb\n",out add,out remove)&&add==0&&remove==0,"Line-ending conversion does not fabricate changed code lines");
            Check(TaskChangeTracker.CountLines("","one\ntwo",out add,out remove)&&add==2&&remove==0,"New files count all real lines without requiring a final newline");
            Random random=new Random(31);bool accurate=true;
            for(int sample=0;sample<100;sample++)
            {
                string[] a=Enumerable.Range(0,random.Next(1,12)).Select(i=>random.Next(4).ToString()).ToArray(),b=Enumerable.Range(0,random.Next(1,12)).Select(i=>random.Next(4).ToString()).ToArray();
                int[,] lcs=new int[a.Length+1,b.Length+1];for(int i=1;i<=a.Length;i++)for(int j=1;j<=b.Length;j++)lcs[i,j]=a[i-1]==b[j-1]?lcs[i-1,j-1]+1:Math.Max(lcs[i-1,j],lcs[i,j-1]);
                accurate&=TaskChangeTracker.CountLines(string.Join("\n",a),string.Join("\n",b),out add,out remove)&&add==b.Length-lcs[a.Length,b.Length]&&remove==a.Length-lcs[a.Length,b.Length];
            }
            Check(accurate,"Measured line counts agree with an independent reference diff on 100 varied cases");
            string folder=Path.Combine(output,"summary-fixture");Directory.CreateDirectory(folder);string path=Path.Combine(folder,"index.html");
            File.WriteAllText(path,"alpha\nbeta\n",new UTF8Encoding(false));
            TaskChangeTracker tracker=new TaskChangeTracker();tracker.TrackFile(path);File.WriteAllText(path,"alpha\nBETA\ngamma\n");tracker.RefreshFile(path);
            tracker.TrackFile(path);File.WriteAllText(path,"alpha\nBETA\ndelta\n");tracker.RefreshFile(path);
            FileChangeSummary change=tracker.GetChanges().Single();
            Check(change.added==2&&change.removed==1,"Repeated edits use the original baseline and never double-count intermediate changes");
            File.WriteAllText(path,"alpha\nbeta\n");tracker.RefreshFile(path);Check(tracker.GetChanges().Count==0,"Reverted file changes disappear from the final net diff");

            TaskChangeTracker command=new TaskChangeTracker();var before=command.CaptureDirectory(folder);
            File.Delete(path);string script=Path.Combine(folder,"app.js");File.WriteAllText(script,"const shop = true;\nconsole.log(shop);\n");command.RecordDirectoryChanges(before,command.CaptureDirectory(folder));
            Check(command.GetChanges().Any(c=>c.path==path&&c.kind=="Удалён"&&c.removed==2)&&command.GetChanges().Any(c=>c.path==script&&c.kind=="Создан"&&c.added==2),"Command directory comparison detects created and deleted files");
            File.WriteAllBytes(Path.Combine(folder,"image.bin"),new byte[]{0,1,2,3});TaskChangeTracker binary=new TaskChangeTracker();binary.TrackFile(Path.Combine(folder,"image.bin"));File.WriteAllBytes(Path.Combine(folder,"image.bin"),new byte[]{0,9,8,7});binary.RefreshFile(Path.Combine(folder,"image.bin"));
            Check(binary.GetChanges().Single().added==null,"Binary changes show no invented line count");

            bool previousTools=state.settings.toolsEnabled;state.settings.toolsEnabled=true;PermissionStore.Grant(ToolPermissionScope.From(WorkflowCall("write_text_file","path",path)));
            ChatMessage reply=RunScriptedAgent(delegate(int step,List<Dictionary<string,object>> messages)
            {
                if(step==1)return WorkflowTurn(WorkflowCall("write_text_file","path",path,"content","<h1>Магазин</h1>\n<p>Каталог товаров</p>\n"));
                if(step==2)return WorkflowTurn(WorkflowCall("run_process","executable",Environment.GetEnvironmentVariable("COMSPEC"),"arguments","/d /c echo body {}>style.css","working_directory",folder));
                return WorkflowTurn(WorkflowCall("complete_task","summary","Созданы страница магазина и файл стилей. Результат готов к просмотру."));
            },null);
            Check(reply.finalSummary!=null&&reply.fileChanges.Count==2&&reply.fileChanges.Any(c=>c.path==path&&c.added==2)&&reply.fileChanges.Any(c=>c.path.EndsWith("style.css")&&c.added==1),"Real agent completion records direct writes and subprocess changes in the final summary");
            ChatMessage restored=json.Deserialize<ChatMessage>(json.Serialize(reply));Check(restored.finalSummary==reply.finalSummary&&restored.fileChanges.Count==2,"Final answer and measured file changes survive history serialization");
            using(Control row=BuildMessageCard(restored,740))
            using(Form preview=new Form{ClientSize=new Size(790,1000),Opacity=0,ShowInTaskbar=false,BackColor=Color.White})
            {
                preview.Controls.Add(row);row.Location=new Point(20,20);preview.Show();
                Check(Descendants(row).Any(c=>c.Name=="TaskSummary")&&Descendants(row).OfType<RichTextBox>().Count(c=>c.Text.Contains(restored.finalSummary))==1,"Completed reply displays one separate final-result card without duplicating its summary");
                Action<string> original=clipboardWriter;string copied=null;clipboardWriter=value=>copied=value;
                try{Descendants(row).OfType<RoundedButton>().Single(b=>b.AccessibleName=="Скопировать итог и изменения").PerformClick();Check(copied.Contains("index.html")&&copied.Contains("+2")&&copied.Contains(restored.finalSummary),"Final-result copy includes summary, paths and measured line counts");}finally{clipboardWriter=original;}
                DesignPreview.Capture(preview,Path.Combine(output,"task-summary.png"));preview.Close();
            }
            state.settings.toolsEnabled=previousTools;PermissionStore.Save(new List<SavedToolPermission>());
        }

        private void CheckTextEncodings(string output)
        {
            string russian="Привет, мир! Создан файл магазина. Ошибка: доступ запрещён.\r\nСтрока 2: тестирование.";
            foreach(int page in new[]{65001,1251,866,1200,1201})
            {
                Encoding encoding=Encoding.GetEncoding(page);byte[] bytes=encoding.GetBytes(russian);
                Check(TextEncoding.Decode(bytes,null,page==866)==russian,"Russian text decodes without mojibake in code page "+page);
                byte[] bom=encoding.GetPreamble().Concat(bytes).ToArray();Check(TextEncoding.Decode(bom)==russian,"Encoding marker is respected for code page "+page);
            }
            string unicode="Русский English 日本語 😀 → ≤ <> & { }";
            Check(TextEncoding.Decode(new UTF8Encoding(false).GetBytes(unicode))==unicode,"UTF-8 preserves multilingual text, emoji and code symbols exactly");
            string html="<meta charset=windows-1251><h1>Магазин</h1>";Check(TextEncoding.DecodeHtml(Encoding.GetEncoding(1251).GetBytes(html),null)==html,"Web-page meta charset decodes Russian legacy HTML");
            string file=Path.Combine(output,"legacy-russian.txt");File.WriteAllBytes(file,Encoding.GetEncoding(1251).GetBytes(russian));Check(TextEncoding.ReadFile(file)==russian,"Legacy Windows text files are decoded before display and model context");
            // cmd's chcp needs a console and cannot set encoding in a headless runner.
            // Stream a known OEM file through the real command process instead.
            string oemFile=Path.Combine(output,"oem-russian.txt");File.WriteAllBytes(oemFile,Encoding.GetEncoding(866).GetBytes(russian));
            using(Process process=Process.Start(new ProcessStartInfo(Environment.GetEnvironmentVariable("COMSPEC"),"/d /c type \""+oemFile+"\""){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true}))
            {ToolResult result=Pump(CollectProcessAsync(process,CancellationToken.None),5);Check(result.Text.Contains("Привет, мир! Создан файл магазина."),"Actual Windows OEM command output preserves Russian characters");}
            using(MemoryStream stream=new MemoryStream(new UTF8Encoding(false).GetBytes(new string('я',100001))))
            using(StreamReader reader=new StreamReader(stream))
            {string text=Pump(ReadBoundedOutputAsync(reader),5);Check(!text.Contains("�")&&text.StartsWith("яяяя"),"Bounded command output preserves Unicode at the truncation boundary");}
        }

        private void CheckContextBudget(string output)
        {
            string error="{\"error\":{\"code\":400,\"message\":\"request (16458 tokens) exceeds the available context size (16384 tokens), try increasing it\",\"type\":\"exceed_context_size_error\",\"n_prompt_tokens\":16458,\"n_ctx\":16384}}";
            ModelApiException parsed=new ModelApiException(400,error);
            Check(parsed.ContextExceeded&&parsed.ContextSize==16384&&!parsed.Message.StartsWith("{"),"The reported 16458/16384 context error is classified and decoded correctly");
            var task=new Dictionary<string,object>{{"role","user"},{"content","Создай сайт магазина. Не удаляй существующие файлы."}};
            var messages=new List<Dictionary<string,object>>{new Dictionary<string,object>{{"role","system"},{"content","Обязательная инструкция"}},task};
            for(int i=0;i<30;i++)
            {
                ToolCall call=WorkflowCall("read_text_file","path","C:\\site\\file"+i+".txt");
                messages.Add(new Dictionary<string,object>{{"role","assistant"},{"content","Проверяю файл"},{"thinking",new string('Р',3000)},{"tool_calls",new object[]{call.Raw}}});
                messages.Add(new Dictionary<string,object>{{"role","tool"},{"tool_name","read_text_file"},{"content",new string('Ж',20000)}});
            }
            var saved=messages.ToList();string original=json.Serialize(saved);
            messages.Add(new Dictionary<string,object>{{"role","user"},{"content","Используй синий цвет, а не красный."}});
            ContextBudget budget=new ContextBudget(new List<Dictionary<string,object>>{messages[0],task},1000);
            Check(budget.Prepare(messages,16384,1)&&budget.Changed&&budget.LastEstimate<=budget.Target,"Long tool history is proactively reduced with room reserved for generation");
            Check(messages.Contains(task)&&messages.Any(m=>GetString(m,"content").Contains("Используй синий"))&&messages.Any(m=>GetString(m,"content")=="Обязательная инструкция"),"Current task, user clarification and system instructions survive compaction");
            Check(json.Serialize(saved)==original,"Compaction does not mutate full persisted history or original tool results");
            bool paired=true;for(int i=0;i<messages.Count;i++)if(GetString(messages[i],"role")=="tool")paired&=i>0&&(messages[i-1].ContainsKey("tool_calls")||GetString(messages[i-1],"role")=="tool");
            Check(paired&&messages.Any(m=>GetString(m,"content").StartsWith("Рабочая запись клиента")),"Old tool exchanges are removed as complete groups and replaced by a working record");
            var enormous=new List<Dictionary<string,object>>{new Dictionary<string,object>{{"role","user"},{"content",new string('Я',50000)}}};
            ContextBudget cannotFit=new ContextBudget(enormous,1000);Check(!cannotFit.Prepare(enormous,8192,1)&&GetString(enormous[0],"content").Length==50000,"Oversized primary requests are rejected without silently truncating the user's task");
            var imageMessages=new List<Dictionary<string,object>>{new Dictionary<string,object>{{"role","user"},{"content","Изображение"},{"images",new string[]{new string('A',1000000)}}}};
            Check(new ContextBudget(imageMessages,0).Estimate(imageMessages)<5000,"Image context estimation does not mistake base64 file bytes for text tokens");
            var attachmentRequest=new List<Dictionary<string,object>>{new Dictionary<string,object>{{"role","user"},{"content","Проверь код\r\n\r\n--- Файл: C:\\site\\app.js ---\r\n"+new string('Ж',30000)}}};
            var attachmentsBudget=new ContextBudget(attachmentRequest,1000,"Проверь код");
            Check(attachmentsBudget.Prepare(attachmentRequest,16384,1)&&GetString(attachmentRequest.Last(),"content").StartsWith("Проверь код")&&GetString(attachmentRequest.Last(),"content").Contains("C:\\site\\app.js"),"Large attachment previews shrink while preserving the user's exact task and source file path");

            bool previous=state.settings.toolsEnabled;state.settings.toolsEnabled=true;int calls=0,failedSize=0,retriedSize=0;
            ChatMessage recovered=RunScriptedAgent(delegate(int step,List<Dictionary<string,object>> request)
            {
                calls=step;
                if(step==1){request.Add(new Dictionary<string,object>{{"role","assistant"},{"content",new string('x',9000)}});return WorkflowTurn(WorkflowCall("get_current_time"));}
                if(step==2){failedSize=json.Serialize(request).Length;throw new ModelApiException(400,error);}
                retriedSize=json.Serialize(request).Length;return WorkflowTurn(WorkflowCall("complete_task","summary","Завершено после автоматического сокращения."));
            },delegate(AgentQuestionDialog dialog){throw new InvalidOperationException("Context overflow must not ask for a pointless repeat");});
            Check(calls==3&&retriedSize<failedSize&&recovered.finalSummary.Contains("автоматического"),"Context overflow retries a genuinely smaller request and completes without a popup");
            Check(recovered.wireMessages.Count(m=>GetString(m,"tool_name")=="get_current_time")==1,"Automatic context recovery never re-executes completed tool actions");
            bool rejected=false;int badCalls=0;
            try{RunScriptedAgent(delegate(int step,List<Dictionary<string,object>> request){badCalls++;throw new ModelApiException(400,"{\"error\":\"unsupported option\"}");},null);}catch(InvalidOperationException){rejected=true;}
            Check(rejected&&badCalls==1,"Non-retryable invalid requests stop with a useful error instead of repeated popups");
            string path=Path.Combine(output,"paged-context.txt");File.WriteAllText(path,string.Join("\n",Enumerable.Range(1,600).Select(i=>"Строка "+i)));
            PermissionStore.Grant(ToolPermissionScope.From(WorkflowCall("read_text_file","path",path)));
            ToolResult page=Pump(ExecuteToolAsync(WorkflowCall("read_text_file","path",path,"start_line","201","max_lines","10"),CancellationToken.None),3);
            Check(page.Text.Contains("Строка 201")&&page.Text.Contains("Строка 210")&&!page.Text.Contains("Строка 211"),"Large file reads support bounded line ranges instead of filling the entire context");
            state.settings.toolsEnabled=previous;PermissionStore.Save(new List<SavedToolPermission>());
        }

        private ToolCall WorkflowCall(string name,params string[] args)
        {
            Dictionary<string,object> arguments=new Dictionary<string,object>();
            for(int i=0;i<args.Length;i+=2)arguments[args[i]]=args[i+1];
            return ParseToolCall(new Dictionary<string,object>{{"function",new Dictionary<string,object>{{"name",name},{"arguments",arguments}}}});
        }

        private void CheckLiveAgentWorkflow()
        {
            bool previous=state.settings.toolsEnabled;bool thinking=state.settings.thinkingEnabled;
            state.settings.toolsEnabled=true;state.settings.thinkingEnabled=false;
            ChatSession chat=new ChatSession{id=Guid.NewGuid().ToString("N"),title="Live agent smoke test"};
            chat.messages.Add(new ChatMessage{role="user",content="Это тест рабочего цикла. Сначала вызови ask_user с вопросом о цвете тестовой страницы. После ответа пользователя вызови get_current_time. После получения времени вызови complete_task: сообщи выбранный пользователем цвет и полученное время. Не создавай файлов и не запускай программ. Важны все три вызова инструментов."});
            ChatMessage reply=new ChatMessage{role="assistant"};chat.messages.Add(reply);int questions=0;
            using(System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer{Interval=100})
            using(CancellationTokenSource cancellation=new CancellationTokenSource(150000))
            {
                timer.Tick+=delegate
                {
                    AgentQuestionDialog dialog=Application.OpenForms.OfType<AgentQuestionDialog>().FirstOrDefault();if(dialog==null)return;
                    if(++questions>1){dialog.StopButton.PerformClick();return;}
                    dialog.Answer.Text="Изумрудный";dialog.ContinueButton.PerformClick();
                };
                timer.Start();Pump(RunAgentAsync(chat,reply,cancellation.Token),160);
            }
            Check(questions==1 && reply.toolLog.Contains("ask_user") && reply.toolLog.Contains("get_current_time") && reply.toolLog.Contains("complete_task"),"LIVE Muse asks, receives clarification, executes a tool and explicitly completes the same task");
            Check(reply.content.ToLowerInvariant().Contains("изумруд"),"LIVE final answer includes the real clarification answer");
            Console.WriteLine("LIVE WORKFLOW: "+reply.toolLog+"\nFINAL: "+reply.content);
            state.settings.toolsEnabled=previous;state.settings.thinkingEnabled=thinking;
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll",CharSet=System.Runtime.InteropServices.CharSet.Unicode,SetLastError=true)]
        private static extern bool CreateHardLink(string file,string existing,IntPtr security);
        private void CheckProjectControls(string output)
        {
            ChatSession originalChat=activeChat;bool previousTools=state.settings.toolsEnabled;
            string oldKey=capabilityKey;bool? oldSupport=modelToolsSupported;
            string folder=Path.Combine(output,"restricted-project");Directory.CreateDirectory(folder);
            string outside=Path.Combine(output,"outside-private.txt");File.WriteAllText(outside,"OUTSIDE SENTINEL");
            ChatSession project=new ChatSession {id=Guid.NewGuid().ToString("N"),title="Проверка памяти проекта",projectPath=folder,accessMode="limited"};
            project.messages.Add(new ChatMessage{role="user",content="Создай магазин. Важное требование: не менять цены."});
            project.messages.Add(new ChatMessage{role="assistant",content=new string('Ж',15000)+"END_OF_ORIGINAL_EVENT"});
            project.messages.Add(new ChatMessage{role="user",content="Используй зелёный цвет, цены оставь прежними."});
            state.chats.Add(project);activeChat=project;state.settings.toolsEnabled=true;
            try
            {
                string file=Path.Combine(folder,"index.html");
                ToolCall write=WorkflowCall("write_text_file","path",file,"content","<h1>Магазин</h1>");
                Check(Pump(ExecuteToolAsync(write,CancellationToken.None,null,project),3).ResultPath==file && File.ReadAllText(file).Contains("Магазин"),"Limited access performs real in-project file writes without an approval popup");
                Check(Pump(ExecuteToolAsync(WorkflowCall("read_text_file","path",file),CancellationToken.None,null,project),3).Text.Contains("Магазин"),"Limited access reads project files");
                foreach(string path in new[]{outside,Path.Combine(folder,"..","outside-private.txt"),folder+"-other\\x.txt",file+":secret",@"\\localhost\c$\private.txt",@"\\?\C:\private.txt","relative.txt",Path.Combine(folder,".. ","outside-private.txt"),Path.Combine(folder,"NUL"),Path.Combine(folder,"COM1.txt")})
                    Check(!LimitedPathAllowed(WorkflowCall("read_text_file","path",path),folder),"Limited access blocks outside, traversal, ADS, device, network or relative path: "+path);
                foreach(string name in new[]{"run_process","capture_screen","open_path","fetch_web_page","unknown"})
                    Check(Pump(ExecuteToolAsync(WorkflowCall(name,"path",file),CancellationToken.None,null,project),3).Text.Contains("запрещено"),"Limited mode blocks tool before any side effect: "+name);
                string hardLink=Path.Combine(folder,"linked.txt");
                Check(CreateHardLink(hardLink,outside,IntPtr.Zero),"Hard-link escape test fixture created");
                Check(!LimitedPathAllowed(WorkflowCall("write_text_file","path",hardLink),folder),"Limited mode rejects hard links to external files");
                Check(File.ReadAllText(outside)=="OUTSIDE SENTINEL","Denied actions leave the outside file intact");
                Check(!LimitedPathAllowed(write,null)&&!LimitedPathAllowed(write,Path.Combine(folder,"missing")),"Limited mode fails closed without a valid project root");
                PermissionStore.Grant(ToolPermissionScope.From(write));
                Check(AccessMode(project)=="limited","Explicit limited access overrides a legacy global full-access grant");
                project.accessMode="confirm";Check(AccessMode(project)=="confirm","Explicit confirmation overrides a global full-access grant");
                Check(!DecideApproval(write,DialogResult.Cancel),"Explicit confirmation shows the real approval popup despite a saved global grant");
                project.accessMode="invalid";Check(AccessMode(project)=="confirm","Unknown stored access modes fail closed");
                project.accessMode="limited";
                ChatSession full=new ChatSession {id="full-fixture",title="Другой чат",accessMode="full"};
                activeChat=full;generationChat=project;
                Check(Pump(ExecuteToolAsync(WorkflowCall("read_text_file","path",outside),CancellationToken.None),3).Text.Contains("запрещено"),"Switching to a full-access chat cannot elevate a running limited task");
                Check(CurrentPermissionInstruction(project).Contains(folder)&&CurrentPermissionInstruction(project).Contains("ограниченный"),"Running task receives its own actual permission policy and root");
                generationChat=null;activeChat=project;
                UpdateProjectMemory(project,WorkflowCall("update_project_memory","plan","Макет → каталог → проверка","decisions","Пользователь: зелёный цвет. Предположение модели: CSS Grid.","next_step","Проверить цены"));
                RecordProjectStep(project,write,new ToolResult{Text="Файл записан",ResultPath=file});
                var request=BuildRequestMessages(project,null);
                Check(request.Any(m=>GetString(m,"role")=="system"&&GetString(m,"content")==ProjectMemorySkill),"Built-in context skill is included in each model request");
                Check(request.Any(m=>GetString(m,"role")=="assistant"&&GetString(m,"content").StartsWith(ProjectMemory.Prefix)&&GetString(m,"content").Contains("не менять цены")),"Original goal survives in a clearly labelled non-system memory message");
                Check(request.Any(m=>GetString(m,"content").StartsWith(ProjectMemory.Prefix)&&GetString(m,"content").Contains("Используй зелёный цвет")),"Recent user amendments enter memory automatically, without relying on model-written notes");
                Check(!request.Where(m=>GetString(m,"role")=="system").Any(m=>GetString(m,"content").Contains("CSS Grid")),"Model-written project decisions never become system instructions");
                for(int i=0;i<18;i++)request.Insert(request.Count-1,new Dictionary<string,object>{{"role","assistant"},{"content",new string('Ж',4000)}});
                ContextBudget budget=new ContextBudget(request,0);
                Check(budget.Prepare(request,16384,1)&&request.Any(m=>GetString(m,"content").StartsWith(ProjectMemory.Prefix)&&GetString(m,"content").Contains("не менять цены")),"Context compaction preserves durable project memory and the original requirement");
                string savedPath=Path.Combine(output,"project-memory-roundtrip.json");File.WriteAllText(savedPath,json.Serialize(project),new UTF8Encoding(false));
                ChatSession restored=json.Deserialize<ChatSession>(File.ReadAllText(savedPath));
                Check(restored.accessMode=="limited"&&restored.projectPath==folder&&restored.memory.decisions.Contains("зелёный")&&restored.memory.steps.Count==1,"Project memory, access mode and project folder survive disk serialization");
                Check(restored.messages[1].content.EndsWith("END_OF_ORIGINAL_EVENT"),"Full original history is preserved alongside compact memory");
                ToolResult oldGoal=RecallProjectHistory(restored,WorkflowCall("recall_project_history","start_event","0","offset","0","query","цены"));
                Check(oldGoal.Text.Contains("не менять цены")&&oldGoal.Text.Contains("зелёный"),"History retrieval finds both the original requirement and later user amendments");
                string page=RecallProjectHistory(restored,WorkflowCall("recall_project_history","start_event","1","offset","0","query","")).Text;
                Check(page.Length<5800&&page.Contains("next_event=1")&&page.Contains("next_offset="),"Large history events have bounded, resumable pagination");
                int offset=0;bool reached=false;
                for(int i=0;i<8;i++)
                {
                    page=RecallProjectHistory(restored,WorkflowCall("recall_project_history","start_event","1","offset",offset.ToString(),"query","")).Text;
                    if(page.Contains("END_OF_ORIGINAL_EVENT")){reached=true;break;}
                    var match=System.Text.RegularExpressions.Regex.Match(page,"next_offset=(\\d+)");if(!match.Success)break;offset=int.Parse(match.Groups[1].Value);
                }
                Check(reached,"History pagination can reach the end of an event without silently dropping its tail");
                Check(!RecallProjectHistory(restored,WorkflowCall("recall_project_history","start_event","0","offset","0","query","OUTSIDE SENTINEL")).Text.Contains("OUTSIDE SENTINEL"),"History recall cannot read other chats or arbitrary filesystem data");
                Check(RecallProjectHistory(restored,WorkflowCall("recall_project_history","start_event","-1","offset","0","query","")).Text.Contains("целые"),"History retrieval validates pagination arguments");
                int rounds=0;bool memorySeen=false;
                RunScriptedAgent(delegate(int step,List<Dictionary<string,object>> messages)
                {
                    rounds=step;
                    if(step==1)return WorkflowTurn(WorkflowCall("update_project_memory","plan","MEMORY_PLAN_SENTINEL","decisions","USER_DECISION_SENTINEL","next_step","Проверить историю"));
                    if(step==2){memorySeen=messages.Any(m=>GetString(m,"role")=="assistant"&&GetString(m,"content").StartsWith(ProjectMemory.Prefix)&&GetString(m,"content").Contains("MEMORY_PLAN_SENTINEL"));return WorkflowTurn(WorkflowCall("recall_project_history","start_event","0","offset","0","query","тестовую"));}
                    return WorkflowTurn(WorkflowCall("complete_task","summary","Память сохранена, исходная задача перечитана."));
                },null);
                Check(rounds==3&&memorySeen,"Real agent loop executes memory tools and feeds updated memory into the next step");
                capabilityKey=state.settings.baseUrl+"|"+state.settings.model;modelToolsSupported=false;
                Check(!RuntimeToolsEnabled&&!BuildRequestMessages(project,null).Any(m=>GetString(m,"content")==AgentInstructions),"A model without tool capability receives text-only instructions and no agent workflow");
                modelToolsSupported=true;Check(RuntimeToolsEnabled,"Tool-capable models retain functional tool execution");
                RefreshChatList();UpdateAccessUi();
                Check(composerAccessButton.Parent.Controls.GetChildIndex(composerAccessButton)==1,"Access-level selector sits immediately beside attachment plus");
                Check(composerAccessButton.Text=="Ограниченный","Composer displays the active chat's access level");
                using(ContextMenuStrip menu=CreateAccessMenu(project))
                {
                    menu.Items[0].PerformClick();Check(project.accessMode=="full"&&composerAccessButton.Text=="Полный доступ","Full-access menu choice updates real chat policy and composer label");
                    menu.Items[1].PerformClick();Check(project.accessMode=="confirm"&&composerAccessButton.Text=="Подтверждать","Confirmation menu choice revokes auto-consent for this chat");
                }
                project.accessMode="limited";UpdateAccessUi();
                using(CancellationTokenSource cancellation=new CancellationTokenSource())
                {
                    generationChat=project;generationCancellation=cancellation;activeChat=originalChat;RefreshChatList();
                    ChatActivityIndicator spinner=Descendants(chatList).OfType<ChatActivityIndicator>().Single();
                    Check((string)spinner.Tag==project.id&&spinner.TimerRunning,"Working chat retains a rotating indicator while another chat is selected");
                    float angle=spinner.Angle;Pump(Task.Delay(130),2);
                    Check(spinner.Angle>angle,"Chat activity advances clockwise over time");
                    DesignPreview.Capture(this,Path.Combine(output,"project-running-chat.png"));
                    RefreshChatList();Check(spinner.IsDisposed,"Rebuilding sidebar disposes old spinner and its timer");
                    collapsedProjects.Add(folder);RefreshChatList();
                    Check(Descendants(chatList).OfType<ChatActivityIndicator>().Count()==1,"Collapsed project folder still shows its running-chat activity");
                    collapsedProjects.Remove(folder);
                    generationCancellation=null;generationChat=null;RefreshChatList();
                    Check(!Descendants(chatList).OfType<ChatActivityIndicator>().Any(),"Chat activity disappears when generation finishes");
                }
                activeChat=project;RefreshChatList();SetReviewSize(new Size(940,650));PerformLayout();Application.DoEvents();
                Check(composerAccessButton.Right<=composerThinkingButton.Left&&composerThinkingButton.PointToScreen(new Point(composerThinkingButton.Width,0)).X<=composerModelButton.PointToScreen(Point.Empty).X,"Access, reasoning and model controls fit the minimum-width composer");
                DesignPreview.Capture(this,Path.Combine(output,"project-access-small.png"));
            }
            finally
            {
                generationChat=null;generationCancellation=null;state.chats.Remove(project);activeChat=originalChat;
                state.settings.toolsEnabled=previousTools;capabilityKey=oldKey;modelToolsSupported=oldSupport;
                PermissionStore.Save(new List<SavedToolPermission>());SetReviewSize(new Size(1340,900));RefreshChatList();RenderConversation();
            }
        }

        private ModelTurn WorkflowTurn(params ToolCall[] calls){ModelTurn turn=new ModelTurn();turn.Calls.AddRange(calls);return turn;}

        private ChatMessage RunScriptedAgent(Func<int,List<Dictionary<string,object>>,ModelTurn> next,Action<AgentQuestionDialog> answer)
        {
            ChatSession chat=new ChatSession{id=Guid.NewGuid().ToString("N"),title="Agent workflow test"};
            chat.messages.Add(new ChatMessage{role="user",content="Выполни тестовую задачу до конца"});
            ChatMessage reply=new ChatMessage{role="assistant"};chat.messages.Add(reply);int count=0;
            HashSet<AgentQuestionDialog> answeredDialogs=new HashSet<AgentQuestionDialog>();
            using(System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer{Interval=60})
            using(CancellationTokenSource timeout=new CancellationTokenSource(8000))
            {
                timer.Tick+=delegate{AgentQuestionDialog dialog=Application.OpenForms.OfType<AgentQuestionDialog>().FirstOrDefault();if(dialog!=null && dialog.Visible && dialog.DialogResult==DialogResult.None && answer!=null && answeredDialogs.Add(dialog))answer(dialog);};timer.Start();
                Pump(RunAgentLoopAsync(chat,reply,timeout.Token,delegate(List<Dictionary<string,object>> messages,ChatMessage visible,CancellationToken token){return Task.FromResult(next(++count,messages));}),10);
            }
            return reply;
        }

        private void CheckAgentWorkflow(string output)
        {
            bool previousTools=state.settings.toolsEnabled;state.settings.toolsEnabled=true;
            string site=Path.Combine(output,"agent-site");Directory.CreateDirectory(site);
            PermissionStore.Grant(ToolPermissionScope.From(WorkflowCall("write_text_file","path",Path.Combine(site,"index.html"))));
            int rounds=0;
            ChatMessage longRun=RunScriptedAgent(delegate(int step,List<Dictionary<string,object>> messages)
            {
                rounds=step;
                if(step<=10)return WorkflowTurn(WorkflowCall("write_text_file","path",Path.Combine(site,"page"+step+".html"),"content","<!doctype html><title>Page "+step+"</title>"));
                if(step<=20)return WorkflowTurn(WorkflowCall("read_text_file","path",Path.Combine(site,"page"+(step-10)+".html")));
                return WorkflowTurn(WorkflowCall("complete_task","summary","Созданы и прочитаны 10 страниц."));
            },null);
            Check(rounds==21 && Directory.GetFiles(site,"page*.html").Length==10,"Agent executes twenty tool rounds and verifies files beyond the old six-step limit");
            Check(longRun.content.Contains("Созданы и прочитаны") && !longRun.content.Contains("Лимит последовательных"),"Explicit completion displays the final summary without the former limit message");
            Check(longRun.wireMessages.Count(m=>GetString(m,"role")=="tool")==21,"Every tool result remains in the completed task context");
            ChatSession reloaded=json.Deserialize<ChatSession>(json.Serialize(new ChatSession{messages=new List<ChatMessage>{longRun}}));
            Check(BuildRequestMessages(reloaded,null).Count(m=>GetString(m,"role")=="tool")==21,"Completed multi-step progress survives history serialization");

            int questions=0;
            ChatMessage clarified=RunScriptedAgent(delegate(int step,List<Dictionary<string,object>> messages)
            {
                if(step==1)return WorkflowTurn(WorkflowCall("ask_user","question","Какой цвет выбрать для магазина?"),WorkflowCall("write_text_file","path",Path.Combine(site,"stale.txt"),"content","Must not execute"));
                Check(questions==1 && messages.Any(m=>GetString(m,"content").Contains("Ответ пользователя: Тёмно-синий")),"Model resumes only after the real clarification reaches its context");
                return WorkflowTurn(WorkflowCall("complete_task","summary","Выбран тёмно-синий цвет."));
            },delegate(AgentQuestionDialog dialog)
            {
                questions++;Check(!dialog.ContinueButton.Enabled,"Clarification cannot submit an empty answer");
                DesignPreview.Capture(dialog,Path.Combine(output,"agent-question.png"));
                dialog.Answer.Text="Тёмно-синий";dialog.ContinueButton.PerformClick();
            });
            Check(questions==1 && clarified.content.Contains("тёмно-синий"),"Global permissions never auto-answer clarification questions");
            Check(!File.Exists(Path.Combine(site,"stale.txt")),"Actions planned before a clarification are skipped until the model replans");

            int textSteps=0;
            RunScriptedAgent(delegate(int step,List<Dictionary<string,object>> messages)
            {textSteps=step;return step<=2?new ModelTurn{Content="Сейчас продолжу работу."}:WorkflowTurn(WorkflowCall("complete_task","summary","Готово."));},null);
            Check(textSteps==3,"Intermediate prose does not prematurely finish an agent task");
            int stalledQuestions=0;
            RunScriptedAgent(delegate(int step,List<Dictionary<string,object>> messages)
            {return step<=3?WorkflowTurn(WorkflowCall("missing_tool")):WorkflowTurn(WorkflowCall("complete_task","summary","Завершено после уточнения."));},delegate(AgentQuestionDialog dialog){stalledQuestions++;dialog.Answer.Text="Попробуй другой способ";dialog.ContinueButton.PerformClick();});
            Check(stalledQuestions==1,"Repeated identical unsuccessful actions pause once for guidance and then resume");
            AgentProgressGuard alternating=new AgentProgressGuard();
            Check(!alternating.Repeats("A") && !alternating.Repeats("B") && !alternating.Repeats("A") && !alternating.Repeats("B") && alternating.Repeats("A"),"Loop detection catches alternating repeated results");
            alternating.Reset();Check(!alternating.Repeats("A"),"User clarification resets the repetition guard");

            int reconnectQuestions=0;
            ChatMessage retried=RunScriptedAgent(delegate(int step,List<Dictionary<string,object>> messages)
            {if(step==1)throw new IOException("Fixture engine disconnected");return WorkflowTurn(WorkflowCall("complete_task","summary","Соединение восстановлено."));},delegate(AgentQuestionDialog dialog){reconnectQuestions++;dialog.Answer.Text="Повторить";dialog.ContinueButton.PerformClick();});
            Check(reconnectQuestions==0 && retried.content.Contains("восстановлено"),"A transient engine error retries automatically without asking the user to repeat the task");
            bool stopped=false;
            try{RunScriptedAgent(delegate(int step,List<Dictionary<string,object>> messages){return WorkflowTurn(WorkflowCall("ask_user","question","Уточнение"));},delegate(AgentQuestionDialog dialog){dialog.StopButton.PerformClick();});}catch(OperationCanceledException){stopped=true;}
            Check(stopped,"Stop in the clarification dialog cancels the task without inventing an answer");
            using(CancellationTokenSource canceled=new CancellationTokenSource(120))
            {bool caught=false;try{AskAgentUser("Cancellation fixture",canceled.Token);}catch(OperationCanceledException){caught=true;}Check(caught,"Cancellation closes a pending clarification dialog");}
            state.settings.toolsEnabled=false;int chatRounds=0;
            RunScriptedAgent(delegate(int step,List<Dictionary<string,object>> messages){chatRounds=step;return new ModelTurn{Content="Обычный ответ"};},null);
            Check(chatRounds==1,"Chat without tools still finishes normally without workflow tool calls");
            state.settings.toolsEnabled=previousTools;PermissionStore.Save(new List<SavedToolPermission>());
        }

        private bool DecideApproval(ToolCall call,DialogResult decision)
        {
            using(System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer {Interval=60})
            using(CancellationTokenSource timeout=new CancellationTokenSource(3000))
            {
                timer.Tick+=delegate
                {
                    ToolApprovalDialog dialog=Application.OpenForms.OfType<ToolApprovalDialog>().FirstOrDefault();if(dialog==null)return;
                    if(decision==DialogResult.Yes)dialog.AlwaysButton.PerformClick();else if(decision==DialogResult.OK)dialog.OnceButton.PerformClick();else dialog.DenyButton.PerformClick();
                };
                timer.Start();return ApproveTool(call,call.Name+"\r\n"+string.Join("\r\n",call.Arguments.Select(p=>p.Key+": "+p.Value)),timeout.Token);
            }
        }

        private void CheckPermissions(string output)
        {
            string target=Path.Combine(output,"permission-fixture.txt");
            ToolCall read=new ToolCall {Name="read_text_file",Arguments=new Dictionary<string,object>{{"path",target}}};
            ToolCall other=new ToolCall {Name="read_text_file",Arguments=new Dictionary<string,object>{{"path",target+".other"}}};
            ToolCall write=new ToolCall {Name="write_text_file",Arguments=new Dictionary<string,object>{{"path",target},{"content","One"}}};
            ToolPermissionScope scope=ToolPermissionScope.From(read);
            PermissionStore.Save(new List<SavedToolPermission>());
            Check(!PermissionStore.Allows(scope),"Tools have no permanent permission by default");
            Check(DecideApproval(read,DialogResult.OK) && !PermissionStore.Allows(scope),"Allow once authorizes only this call without saving a permission");
            Check(!DecideApproval(read,DialogResult.Cancel) && !PermissionStore.Allows(scope),"Deny does not execute or persist an approval");
            Check(DecideApproval(read,DialogResult.Yes),"Allow always is wired to the real approval dialog");
            Check(PermissionStore.Allows(scope),"Permanent approval survives a fresh permission-store instance");
            using(CancellationTokenSource timeout=new CancellationTokenSource(500))Check(ApproveTool(read,"same file",timeout.Token),"Saved permission bypasses the repeated popup");
            File.WriteAllText(target,"PERMISSION FIXTURE");
            using(CancellationTokenSource timeout=new CancellationTokenSource(1000))Check(Pump(ExecuteToolAsync(read,timeout.Token),3).Text=="PERMISSION FIXTURE","Saved permission authorizes the real tool execution path");
            Check(PermissionStore.Allows(ToolPermissionScope.From(other)) && PermissionStore.Allows(ToolPermissionScope.From(write)),"Allow always grants other files and write access globally");
            ToolCall casing=new ToolCall {Name="read_text_file",Arguments=new Dictionary<string,object>{{"path",target.ToUpperInvariant()}}};
            Check(ToolPermissionScope.From(casing).Key==scope.Key,"Windows file-path casing does not create duplicate permissions");
            string writeKey=ToolPermissionScope.From(write).Key;write.Arguments["content"]="Two";
            Check(ToolPermissionScope.From(write).Key==writeKey,"Permanent file-write scope covers changed content only at the same path");
            ToolCall process=new ToolCall {Name="run_process",Arguments=new Dictionary<string,object>{{"executable","example.exe"},{"arguments","--check"},{"working_directory",output}}};
            string processKey=ToolPermissionScope.From(process).Key;process.Arguments["arguments"]="--delete";
            Check(PermissionStore.Allows(ToolPermissionScope.From(process)),"Full access covers changed process arguments");
            process.Arguments["arguments"]="--check";process.Arguments["working_directory"]=Path.GetDirectoryName(output);
            Check(PermissionStore.Allows(ToolPermissionScope.From(process)),"Full access covers changed process working directory");
            ToolCall[] allTools={other,write,process,
                new ToolCall{Name="list_directory",Arguments=new Dictionary<string,object>{{"path",output}}},
                new ToolCall{Name="fetch_web_page",Arguments=new Dictionary<string,object>{{"url","https://example.com/new?q=1"}}},
                new ToolCall{Name="open_path",Arguments=new Dictionary<string,object>{{"target","https://example.org/"}}},
                new ToolCall{Name="capture_screen",Arguments=new Dictionary<string,object>()}};
            foreach(ToolCall tool in allTools)
                using(CancellationTokenSource timeout=new CancellationTokenSource(500))Check(ApproveTool(tool,"global approval test",timeout.Token),"No popup for globally authorized tool: "+tool.Name);
            Check(PermissionStore.Load().Count==1 && PermissionStore.Load()[0].key==ToolPermissionStore.FullAccessKey,"Allow always persists a single global grant");
            using(CancellationTokenSource timeout=new CancellationTokenSource(1000))Check(Pump(ExecuteToolAsync(write,timeout.Token),3).ResultPath==target && File.ReadAllText(target)=="Two","Global permission allows real file write after approving a read");
            Check(BuildRequestMessages(new ChatSession(),null).Any(m=>Convert.ToString(m["content"]).Contains("пользователь включил полный доступ")),"Model receives the active global permission mode");
            PermissionStore.Save(new List<SavedToolPermission>{new SavedToolPermission{key=scope.Key,description="Legacy grant"}});
            Check(allTools.All(t=>PermissionStore.Allows(ToolPermissionScope.From(t))),"Previously saved Allow always choices enable global access after upgrade");
            using(CancellationTokenSource canceled=new CancellationTokenSource())
            {canceled.Cancel();bool caught=false;try{ApproveTool(read,"canceled",canceled.Token);}catch(OperationCanceledException){caught=true;}Check(caught,"Canceled actions remain canceled despite saved permission");}
            using(ToolApprovalDialog preview=new ToolApprovalDialog(scope,"read_text_file\r\n\r\nC:\\Projects\\Muse\\notes.txt"))
            {
                preview.Opacity=0;preview.Show();preview.PerformLayout();
                Check(preview.AcceptButton==preview.DenyButton && preview.CancelButton==preview.DenyButton,"Enter and Escape cannot accidentally grant permanent access");
                Check(preview.AlwaysButton.Right<=preview.AlwaysButton.Parent.ClientSize.Width && preview.DenyButton.Left>=0,"All three approval buttons fit the dialog");
                DesignPreview.Capture(preview,Path.Combine(output,"permission-dialog.png"));preview.Close();
            }
            using(System.Windows.Forms.Timer managerTimer=new System.Windows.Forms.Timer{Interval=60})
            {
                managerTimer.Tick+=delegate
                {
                    Form manager=Application.OpenForms.Cast<Form>().FirstOrDefault(f=>f.Text=="Сохранённые разрешения · Muse Desk");if(manager==null)return;
                    managerTimer.Stop();manager.Opacity=0;manager.PerformLayout();
                    DesignPreview.Capture(manager,Path.Combine(output,"permission-settings.png"));
                    RoundedButton disable=manager.Controls.OfType<RoundedButton>().Single(b=>b.Text=="Отключить полный доступ");
                    Check(disable.Enabled,"Settings exposes an enabled full-access disable button");disable.PerformClick();
                    Check(!disable.Enabled && manager.Controls.OfType<Label>().Any(l=>l.Text.StartsWith("Полный доступ выключен")),"Disabling full access updates the actual settings interface");
                    manager.DialogResult=DialogResult.OK;
                };
                managerTimer.Start();ManagePermissions();
            }
            Check(!PermissionStore.Allows(scope),"Settings disable button revokes saved permissions and restores confirmation");
            Check(allTools.All(t=>!PermissionStore.Allows(ToolPermissionScope.From(t))),"Disabling full access revokes every tool including legacy grants");
            Check(!BuildRequestMessages(new ChatSession(),null).Any(m=>Convert.ToString(m["content"]).Contains("пользователь включил полный доступ")),"Model permission mode updates after full access is revoked");
            using(CancellationTokenSource canceledDialog=new CancellationTokenSource(100))
            {bool caught=false;try{ApproveTool(other,"pending permission",canceledDialog.Token);}catch(OperationCanceledException){caught=true;}Check(caught && !PermissionStore.Allows(ToolPermissionScope.From(other)),"Canceling a pending approval closes the popup without granting access");}
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(statePath),"permissions.json"),"{broken");
            Check(!PermissionStore.Allows(scope),"Corrupt permission storage fails closed");
            PermissionStore.Save(new List<SavedToolPermission>{new SavedToolPermission{key="unrecognized"}});
            Check(!PermissionStore.HasFullAccess,"Unrecognized permission records do not enable full access");
            PermissionStore.Save(new List<SavedToolPermission>());
        }

        private void CheckComposerCaret()
        {
            Label cue=input.Parent.Controls.OfType<Label>().Single(l=>l.Text.StartsWith("Спросите Muse"));
            input.Text="";ActiveControl=input;input.Focus();Application.DoEvents();
            Check(cue.Visible,"Placeholder remains visible on startup and programmatic focus until user interaction");
            typeof(Control).GetMethod("OnClick",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(cue,new object[]{EventArgs.Empty});
            Check(input.Focused && !cue.Visible,"Focused empty editor does not cover the insertion caret with its placeholder");
            input.Text="Проверка";input.SelectionStart=3;
            Check(input.SelectionStart==3 && !cue.Visible,"Text caret can be positioned inside a message");
            input.Clear();Check(!cue.Visible,"Clearing a focused editor keeps its caret unobstructed");
            settingsButton.Focus();Application.DoEvents();
            Check(cue.Visible,"Placeholder returns only when empty editor loses focus");
            input.Focus();Check(cue.Visible,"Automatic refocus does not dismiss the empty-field hint");
            typeof(Control).GetMethod("OnClick",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(input.Parent,new object[]{EventArgs.Empty});
            Check(input.Focused && !cue.Visible,"Clicking editor padding focuses the visible insertion caret");
            Check(input.Cursor==Cursors.IBeam && input.Parent.Cursor==Cursors.IBeam,"Text-entry area consistently uses the I-beam pointer");
            settingsButton.Focus();
        }

        private void CheckScrollBehavior()
        {
            using(Form fixture=new Form {Size=new Size(300,250),Opacity=0,ShowInTaskbar=false})
            using(ModernFlowPanel flow=new ModernFlowPanel {Dock=DockStyle.Fill,AutoScroll=true,FlowDirection=FlowDirection.TopDown,WrapContents=false})
            {
                fixture.Controls.Add(flow);InstallScrollBar(flow,fixture);
                for(int i=0;i<30;i++)flow.Controls.Add(new Label {Text="Строка "+i,Width=220,Height=28});
                fixture.Show();fixture.PerformLayout();flow.PerformLayout();
                ThinScrollBar bar=fixture.Controls.OfType<ThinScrollBar>().Single();
                Check(bar.Visible && bar.Width==10 && flow.MaximumOffset>300,"Long content uses a thin custom scrollbar");
                flow.ScrollWheel(-120);Check(-flow.AutoScrollPosition.Y>0,"Mouse wheel scrolls the actual content");
                flow.UserScrollTo(flow.MaximumOffset);Check(-flow.AutoScrollPosition.Y==flow.MaximumOffset,"Scrollbar reaches the final line without clipping");
                var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
                typeof(ThinScrollBar).GetMethod("OnKeyDown",flags).Invoke(bar,new object[]{new KeyEventArgs(Keys.Home)});
                Check(flow.AutoScrollPosition.Y==0,"Scrollbar Home key returns to the top");
                Rectangle thumb=bar.Thumb;
                typeof(ThinScrollBar).GetMethod("OnMouseDown",flags).Invoke(bar,new object[]{new MouseEventArgs(MouseButtons.Left,1,4,thumb.Top+3,0)});
                typeof(ThinScrollBar).GetMethod("OnMouseMove",flags).Invoke(bar,new object[]{new MouseEventArgs(MouseButtons.Left,1,4,thumb.Top+75,0)});
                typeof(ThinScrollBar).GetMethod("OnMouseUp",flags).Invoke(bar,new object[]{new MouseEventArgs(MouseButtons.Left,1,4,thumb.Top+75,0)});
                Check(-flow.AutoScrollPosition.Y>0,"Dragging the thin thumb scrolls content");
                fixture.Hide();
            }
        }

        private void CheckStreamingBehavior(string output)
        {
            ChatSession previous=activeChat;
            ChatSession fixture=new ChatSession {id="stream-review",title="Плавное появление ответа"};
            ChatMessage reply=new ChatMessage {role="assistant",content=string.Join("\n",Enumerable.Repeat("Плавное появление текста: ответ дополняется без пересоздания блока.",55).ToArray())};
            fixture.messages.Add(reply);state.chats.Add(fixture);state.activeChatId=fixture.id;activeChat=fixture;generationChat=fixture;generationCancellation=new CancellationTokenSource();
            try
            {
                RenderConversation();Control row=messageList.Controls[0];StreamView view=(StreamView)row.Tag;
                Check(!resultsBody.Controls.OfType<RoundedButton>().Any(b=>b.Text=="attachment.txt"),"Switching chats immediately replaces the results panel even during generation");
                AnimateStreamFrame();
                Check(view.Displayed.Length>0 && reply.content.StartsWith(view.Displayed),"Streaming appends an exact prefix of the model response");
                if(SystemInformation.IsMenuAnimationEnabled)Check(view.Displayed.Length<reply.content.Length,"Large response chunks are revealed gradually");
                RenderConversation();Check(object.ReferenceEquals(row,messageList.Controls[0]),"Incoming stream updates retain the same response control");
                for(int i=0;i<150;i++)AnimateStreamFrame();
                Check(view.Displayed==reply.content,"Smooth presentation loses no response characters");
                Check(center.Controls.OfType<ThinScrollBar>().Any(b=>b.Visible && b.Width==10),"Conversation scrollbar remains visible during a long streamed answer");
                ((ModernFlowPanel)messageList).UserScrollTo(0);reply.content+="\nНовая строка во время чтения истории.";
                for(int i=0;i<40;i++)AnimateStreamFrame();
                Check(!followResponseTail && messageList.AutoScrollPosition.Y==0,"Streaming does not pull a reader back down after scrolling up");
                Check(NextVisibleBoundary("A😀e\u0301B",1,1)==3 && NextVisibleBoundary("A😀e\u0301B",3,1)==5,"Animation preserves emoji pairs and combining characters");
                DesignPreview.Capture(this,Path.Combine(output,"streaming-window.png"));
                ((ModernFlowPanel)messageList).UserScrollTo(((ModernFlowPanel)messageList).MaximumOffset);
                followResponseTail=true;scrollAnimationTarget=((ModernFlowPanel)messageList).MaximumOffset;
                generationCancellation.Dispose();generationCancellation=null;generationChat=null;
                reply.tokensPerSecond=36.1;reply.completedAt=DateTime.UtcNow.ToString("o");
                RenderConversation();Application.DoEvents();AnimateStreamFrame();
                Check(scrollAnimationTarget==-1,"Completed answer discards the old streaming scroll target");
                var footer=Descendants(messageList).OfType<Label>().Single(l=>l.AccessibleName=="Время завершения ответа");
                int footerBottom=messageList.PointToClient(footer.PointToScreen(new Point(0,footer.Height))).Y;
                DesignPreview.Capture(this,Path.Combine(output,"footer-debug.png"));
                Check(footerBottom<=messageList.ClientSize.Height-24,"Completed answer footer stays fully visible above the composer: bottom="+footerBottom+", viewport="+messageList.ClientSize.Height+", offset="+(-messageList.AutoScrollPosition.Y)+", extent="+messageList.DisplayRectangle.Height+", row="+messageList.Controls[0].Bounds+", card="+footer.Parent.Bounds+", footer="+footer.Bounds+", padding="+messageList.Padding);
                DesignPreview.Capture(this,Path.Combine(output,"completed-answer-footer.png"));
            }
            finally
            {
                if(generationCancellation!=null)generationCancellation.Dispose();generationCancellation=null;generationChat=null;state.chats.Remove(fixture);state.activeChatId=previous.id;activeChat=previous;scrollAnimationTarget=-1;
                if(streamAnimation!=null)streamAnimation.Stop();RenderConversation();
            }
        }

        private static void CheckButtonSurfaces(string output)
        {
            using(Panel parent=new Panel {BackColor=Color.White})
            using(ButtonPaintProbe probe=new ButtonPaintProbe {Size=new Size(42,42),Radius=21,BackColor=Color.Black,ForeColor=Color.White,Text="↑"})
            using(Bitmap buffer=new Bitmap(42,42))
            using(Graphics g=Graphics.FromImage(buffer))
            {
                parent.Controls.Add(probe);
                g.Clear(Color.Magenta);
                probe.PaintOnly(g);
                Check(buffer.GetPixel(0,0).ToArgb()==Color.White.ToArgb() && buffer.GetPixel(41,41).ToArgb()==Color.White.ToArgb(),"Actual button paint clears stale pixels in rounded corners even without background paint");
            }
            foreach(int size in new int[]{32,40,48,64})
            using(Panel parent=new Panel {BackColor=Color.FromArgb(245,245,245)})
            using(ButtonPaintProbe probe=new ButtonPaintProbe {Size=new Size(size,size),Radius=size/2,BackColor=Color.Black,ForeColor=Color.White,Text="↑"})
            using(Bitmap buffer=new Bitmap(size,size))
            using(Graphics g=Graphics.FromImage(buffer))
            {
                parent.Controls.Add(probe);
                foreach(bool enabled in new bool[]{true,false,true})
                {
                    probe.Enabled=enabled;g.Clear(Color.Magenta);probe.PaintOnly(g);
                    Check(buffer.GetPixel(0,0).ToArgb()==parent.BackColor.ToArgb() && buffer.GetPixel(size-1,size-1).ToArgb()==parent.BackColor.ToArgb(),"Button corners clear across size "+size+" and enabled state "+enabled);
                }
            }
            using(RoundedButton button=new RoundedButton {Width=144,Height=40,BackColor=Color.White,ForeColor=Color.FromArgb(35,35,35),Outline=true,Radius=10})
            using(Bitmap sheet=new Bitmap(648,128))
            using(Graphics graphics=Graphics.FromImage(sheet))
            using(Font label=new Font("Segoe UI",9F))
            {
                graphics.Clear(Color.White);
                string[] labels={"Обычная","Наведение","Нажатие","Клавиатура"};
                int[] centers=new int[4];
                for(int i=0;i<4;i++)
                {
                    using(Bitmap sample=new Bitmap(144,40))
                    using(Graphics g=Graphics.FromImage(sample))
                    {
                        g.Clear(Color.White);button.PaintSurface(g,new Rectangle(0,0,144,40),i==1,i==2,i==3);
                        centers[i]=sample.GetPixel(72,20).R;
                        if(i==0)
                        {
                            Check(Enumerable.Range(0,10).All(y=>sample.GetPixel(72,y).R>=centers[i]),"Secondary button has no permanent border darker than its surface");
                            Check(sample.GetPixel(0,0).ToArgb()==Color.White.ToArgb(),"Rounded corners leave the parent background intact");
                        }
                        if(i==3)Check(Enumerable.Range(0,5).Any(y=>sample.GetPixel(72,y).R<190),"Keyboard navigation retains one visible focus contour");
                        graphics.DrawImageUnscaled(sample,12+i*160,48);
                        TextRenderer.DrawText(graphics,"Возможности",label,new Rectangle(12+i*160,48,144,40),Color.FromArgb(35,35,35),TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
                        TextRenderer.DrawText(graphics,labels[i],label,new Point(18+i*160,18),Color.FromArgb(100,100,100));
                    }
                }
                Check(centers[1]<centers[0]&&centers[2]<centers[1],"Hover and press use distinct consistent surface shades");
                Check(button.FlatAppearance.BorderSize==0,"Native rectangular button borders are disabled");
                sheet.Save(Path.Combine(output,"button-states.png"));
            }
        }

        private void CheckThreeColumnScrollbars(string output)
        {
            StoredState saved=state;ChatSession current=activeChat;Size oldSize=Size;bool oldResults=resultsRequested;
            try
            {
                SetReviewSize(new Size(1340,900));resultsRequested=true;rightRail.Visible=false;state=new StoredState();activeChat=null;
                for(int i=0;i<45;i++)state.chats.Add(new ChatSession {id="scroll-"+i,title="Проверка прокрутки "+i,updatedAt=DateTime.UtcNow.ToString("o")});
                activeChat=state.chats[0];state.activeChatId=activeChat.id;
                activeChat.messages.Add(new ChatMessage {role="assistant",content=string.Join("\n",Enumerable.Repeat("Строка длинного ответа для проверки прокрутки.",90)),completedAt=DateTime.UtcNow.ToString("o")});
                for(int i=0;i<45;i++)activeChat.referenceUrls.Add("https://example.com/source/"+i);
                RefreshChatList();RenderConversation();Application.DoEvents();
                var columns=new[]{(ModernFlowPanel)chatList,(ModernFlowPanel)messageList,(ModernFlowPanel)resultsBody};
                foreach(var column in columns)
                {
                    var bar=column.Parent.Controls.OfType<ThinScrollBar>().Single(b=>b.ScrollOwner==column);
                    Check(bar.Visible && bar.Width==10 && bar.Right==column.Right && bar.Height==column.Height,"Overflow column has an aligned thin scrollbar: "+Array.IndexOf(columns,column)+", form="+Size+", center="+center.ClientSize+", visible="+column.Visible+", offset="+column.MaximumOffset+", bar="+bar.Bounds+", column="+column.Bounds);
                    column.UserScrollTo(column.MaximumOffset);Check(-column.AutoScrollPosition.Y==column.MaximumOffset,"Each column reaches its final item: "+Array.IndexOf(columns,column));
                    Check(bar.Thumb.Bottom<=bar.Height,"Scrollbar thumb remains inside its track");
                    column.UserScrollTo(0);
                }
                DesignPreview.Capture(this,Path.Combine(output,"three-column-scrollbars.png"));
                state.chats.RemoveAll(c=>c!=activeChat);activeChat.messages.Clear();activeChat.referenceUrls.Clear();
                RefreshChatList();RenderConversation();Application.DoEvents();
                foreach(var column in columns)
                {
                    var bar=column.Parent.Controls.OfType<ThinScrollBar>().Single(b=>b.ScrollOwner==column);
                    Check(!bar.Visible && column.MaximumOffset==0,"Scrollbar disappears after content shrinks: "+Array.IndexOf(columns,column));
                }
            }
            finally{state=saved;activeChat=current;resultsRequested=oldResults;SetReviewSize(oldSize);RefreshChatList();RenderConversation();}
        }

        private void CheckStyledDialogs(string output)
        {
            using(var dialog=MuseDialog.Build("Удалить проект «Muse Studio» и все его чаты (3) из Muse Desk? Папка и файлы на диске сохранятся.","Удалить проект и чаты",MessageBoxButtons.YesNo,MessageBoxIcon.Warning))
            {
                dialog.Show(this);Application.DoEvents();
                Check(dialog.FormBorderStyle==FormBorderStyle.None && dialog.Controls.OfType<RoundedButton>().Count()==3,"Confirmations use Muse styling and custom controls");
                Check(((Button)dialog.AcceptButton).DialogResult==DialogResult.No && ((Button)dialog.CancelButton).DialogResult==DialogResult.No,"Enter and Escape safely dismiss deletion confirmations");
                DesignPreview.Capture(dialog,Path.Combine(output,"styled-confirmation.png"));
                dialog.Controls.OfType<RoundedButton>().Single(b=>b.DialogResult==DialogResult.Yes).PerformClick();
                Check(dialog.DialogResult==DialogResult.Yes,"Explicit confirmation preserves the existing deletion result");dialog.Close();
            }
            using(var dialog=MuseDialog.Build(new string('x',4000),"Сообщение",MessageBoxButtons.OK,MessageBoxIcon.Information))
            {
                Check(dialog.Height<=600 && Descendants(dialog).OfType<RichTextBox>().Single().ScrollBars==RichTextBoxScrollBars.Vertical,"Long error messages stay within a scrollable dialog");
                Check(((Button)dialog.CancelButton).DialogResult==DialogResult.OK,"Informational dialogs support Escape dismissal");
            }
        }

        private void CheckApplicationMenu(string output)
        {
            var newChat=sidebar.Controls.OfType<RoundedButton>().Single(b=>b.Text=="Новый чат");
            Check(newChat.Top+newChat.Height/2==sidebarToggle.Top+sidebarToggle.Height/2 && newChat.Right<sidebarToggle.Left,"New chat aligns with sidebar toggle without overlap");
            Check(applicationMenu.Renderer is MuseMenuRenderer && ((ToolStripMenuItem)applicationMenu.Items[0]).DropDown.Renderer is MuseMenuRenderer,"Top menu and dropdowns share Muse hover styling");
            Check(FormBorderStyle==FormBorderStyle.None && Descendants(titleBar).Contains(windowClose) && Descendants(titleBar).Contains(windowMaximize) && Descendants(titleBar).Contains(windowMinimize),"Unified title row replaces the system caption and contains window controls");
            Check(windowMinimize is WindowControlButton && windowMaximize is WindowControlButton && windowClose is WindowControlButton && windowMinimize.Size==windowMaximize.Size && windowMaximize.Size==windowClose.Size && windowMinimize.Top==windowClose.Top,"Window controls use equally sized geometric icons on one baseline");
            Check(Descendants(titleBar).OfType<Label>().Single(l=>l.Text=="Muse Desk").Font.Bold && !applicationMenu.Font.Bold,"Brand is bold while menu labels remain regular");
            Control brandArea=titleBar.Controls["WindowBrand"],actions=titleBar.Controls["WindowActions"];
            foreach(int testWidth in new[]{940,1340})
            {
                SetReviewSize(new Size(testWidth,Height));PerformLayout();titleBar.PerformLayout();
                Check(brandArea.Right<=applicationMenu.Left && applicationMenu.Right<=actions.Left && actions.Right<=titleBar.ClientSize.Width,"Title menu cannot cover brand or window controls at width "+testWidth);
                Point closeCenter=windowClose.PointToScreen(new Point(windowClose.Width/2,windowClose.Height/2));
                Check(titleBar.GetChildAtPoint(titleBar.PointToClient(closeCenter))==actions,"Window close button area is not covered by the menu at width "+testWidth);
            }
            using(Font expected=InterfaceTypography.Sidebar())Check(applicationMenu.Font.Name==expected.Name && applicationMenu.Font.Size==expected.Size,"Menu typography matches sidebar actions");
            Check(Descendants(titleBar).OfType<Label>().Any(l=>l.Text=="Muse Desk") && Descendants(titleBar).OfType<MuseMark>().Any(),"Application name and logo sit before the menu in the same row");
            Check(applicationMenu.Items.Cast<ToolStripItem>().Select(i=>i.Text).SequenceEqual(new[]{"Файл","Правка","Вид","Справка"}),"Application has the four requested native menus");
            Check(center.Top>=applicationMenu.Bottom && sidebar.Top>=applicationMenu.Bottom,"Menu occupies its own row above workspace and sidebar");
            Check(!center.Region.IsVisible(0,0) && center.Region.IsVisible(22,1) && center.Region.IsVisible(1,22),"Workspace clips only its upper-left corner to a round curve");
            string prior=input.Text;input.Text="Проверка меню";input.Focus();menuTextTarget=input;
            EditText("all");Check(input.SelectedText==input.Text,"Edit menu selects text in the prompt editor");
            bool locked=input.ReadOnly;input.ReadOnly=true;EditText("delete");Check(input.Text=="Проверка меню","Edit menu respects readiness and read-only input");input.ReadOnly=locked;input.Text=prior;
            var view=(ToolStripMenuItem)applicationMenu.Items[2];
            ((ToolStripMenuItem)view.DropDownItems[0]).PerformClick();Check(!sidebar.Visible,"View menu hides the sidebar");
            ((ToolStripMenuItem)view.DropDownItems[0]).PerformClick();Check(sidebar.Visible,"View menu restores the sidebar");
            var file=(ToolStripMenuItem)applicationMenu.Items[0];file.ShowDropDown();Application.DoEvents();
            DesignPreview.Capture(this,Path.Combine(output,"application-menu.png"));file.HideDropDown();
        }

        private void CheckProjectTree(string output)
        {
            StoredState saved=state;ChatSession current=activeChat;var collapsed=collapsedProjects.ToList();
            try
            {
                state=new StoredState();activeChat=null;collapsedProjects.Clear();
                string first=Path.Combine(output,"Muse Studio"),second=Path.Combine(output,"Research");
                Directory.CreateDirectory(first);Directory.CreateDirectory(second);
                AttachExistingProject(first);
                Check(ProjectName(first)=="Muse Studio" && state.projects.Contains(first),"Existing folder name becomes the project name without renaming");
                AttachExistingProject(first+Path.DirectorySeparatorChar);
                Check(state.projects.Count==1,"Reconnecting the same folder does not duplicate its project");
                using(var dialog=BuildProjectDialog())
                {dialog.Show(this);Application.DoEvents();Check(!Descendants(dialog).OfType<TextBox>().Any(),"Project dialog does not ask for a separate name");DesignPreview.Capture(dialog,Path.Combine(output,"project-dialog.png"));dialog.Close();}
                RegisterProject(first);RegisterProject(second);RegisterProject(first.ToUpperInvariant());
                Check(state.projects.Count==2,"Project registry deduplicates Windows paths");
                EnsureActiveChat();RefreshChatList();
                Check(chatList.Controls.OfType<RoundedButton>().Count(b=>b.Text=="Создать первый чат")==2,"Empty projects remain visible with a first-chat action");
                CreateProjectChat(first);ChatSession one=activeChat;one.title="Интерфейс приложения";
                CreateProjectChat(first);ChatSession two=activeChat;two.title="Проверка загрузки модели";
                Check(one.id!=two.id && one.projectPath==first && two.projectPath==first,"Project plus creates independent chats in its directory");
                MoveChatToProject(two,second);
                Check(two.projectPath==second && one.projectPath==first,"Moving a chat preserves other project chats");
                var request=BuildRequestMessages(one,null);
                Check(request.Any(m=>m.ContainsKey("content")&&Convert.ToString(m["content"]).Contains(first)),"Model receives the selected chat working directory");
                collapsedProjects.Add(second);SaveProjectTree();
                StoredState restored=LoadState();
                Check(restored.projects.Count==2 && restored.collapsedProjectPaths.Contains(second) && restored.chats.Single(c=>c.id==two.id).projectPath==second,"Projects, collapsed folders and chat membership survive reload");
                collapsedProjects.Clear();RefreshChatList();RenderConversation();
                DesignPreview.Capture(this,Path.Combine(output,"project-tree.png"));
                activeChat=one;one.messages.Add(new ChatMessage {role="user",content="Помоги спланировать небольшой проект: приложение для личных заметок.",sentAt="2026-09-07T12:00:00Z"});
                one.messages.Add(new ChatMessage {role="assistant",content="## Начнём с главного\nСделаем место, где удобно собирать мысли и быстро находить нужное.\n\n**Первый выпуск**\n• Создание и редактирование заметок\n• Поиск по тексту\n• Группировка по проектам\n• Локальное хранение\n\nДальше определим структуру экранов и подготовим первый прототип.",completedAt="2026-09-07T12:00:12Z"});
                state.activeChatId=one.id;RefreshChatList();RenderConversation();Application.DoEvents();DesignPreview.Capture(this,Path.Combine(output,"github-overview.png"));
                RemoveProject(first);
                Check(!state.chats.Contains(one) && state.chats.Contains(two) && Directory.Exists(first),"Deleting a project removes its chats, keeps other chats and preserves disk folders");
                RemoveProject(second);Check(!state.chats.Contains(two) && activeChat!=two && state.chats.Contains(activeChat),"Deleting the active project selects a surviving chat");
                Check(!ProjectPaths().Contains(first),"Removed project is not recreated by remaining chats");
            }
            finally{state=saved;activeChat=current;collapsedProjects.Clear();foreach(string p in collapsed)collapsedProjects.Add(p);SaveState();RefreshChatList();RenderConversation();}
        }

        private void CheckMenuLifetime()
        {
            Application.DoEvents();int baseline=ownedMenus.Count;
            for(int i=0;i<12;i++)
            {
                ContextMenuStrip menu=new ContextMenuStrip();menu.Items.Add("Проверка меню");
                ShowTransientMenu(menu,input,new Point(0,0));
                menu.Close(ToolStripDropDownCloseReason.AppClicked);
                Check(!menu.IsDisposed,"Menu survives the complete native close stack #"+i);
                Application.DoEvents();
                Check(menu.IsDisposed,"Closed transient menu is disposed on the next UI turn #"+i);
            }
            Button owner=new Button();Controls.Add(owner);
            ContextMenuStrip attached=new ContextMenuStrip();attached.Items.Add("Контекст");
            owner.ContextMenuStrip=attached;OwnMenu(attached,owner,false);
            owner.Dispose();Application.DoEvents();
            Check(attached.IsDisposed,"Rebuilt chat rows release their attached context menus");
            Check(ownedMenus.Count==baseline,"Repeated menu operations do not leak owned menus");
            using(MainForm closing=new MainForm())
            {
                ContextMenuStrip menu=new ContextMenuStrip();closing.OwnMenu(menu,null,true);
                closing.isClosing=true;closing.QueueMenuDisposal(menu);closing.Dispose();
                Check(menu.IsDisposed,"Form disposal releases menus queued during shutdown");
            }
        }

        private void CheckEngineStartupLifetime()
        {
            TcpListener listener=new TcpListener(IPAddress.Loopback,0);listener.Start();
            int port=((IPEndPoint)listener.LocalEndpoint).Port;
            Task server=Task.Run(async delegate
            {
                using(TcpClient client=await listener.AcceptTcpClientAsync())
                using(NetworkStream stream=client.GetStream())
                {
                    await Task.Delay(100);
                    byte[] reply=Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: 18\r\nConnection: close\r\n\r\n{\"version\":\"test\"}");
                    await stream.WriteAsync(reply,0,reply.Length);
                }
            });
            string previous=state.settings.baseUrl;
            try
            {
                state.settings.baseUrl="http://127.0.0.1:"+port;
                Task first=EnsureEngineAsync(),second=EnsureEngineAsync();
                Check(object.ReferenceEquals(first,second),"Overlapping engine startup requests share one operation");
                Pump(first,5);Pump(server,5);
                Check(engineProcess==null,"Healthy mock endpoint does not start an inference process");
                isClosing=true;Pump(EnsureEngineAsync(),2);
                Check(engineProcess==null,"Closing form cannot start an engine");
            }
            finally{isClosing=false;state.settings.baseUrl=previous;listener.Stop();}
        }

        private void CheckCancellation()
        {
            TcpListener listener=new TcpListener(IPAddress.Loopback,0);listener.Start();int port=((IPEndPoint)listener.LocalEndpoint).Port;
            string resident=json.Serialize(new Dictionary<string,object>{{"models",new object[]{new Dictionary<string,object>{{"name",state.settings.model},{"size_vram",1024L}}}}});
            Task server=Task.Run(async delegate
            {
                for(int check=0;check<1;check++)
                using(TcpClient probe=await listener.AcceptTcpClientAsync())
                using(NetworkStream probeStream=probe.GetStream())
                {
                    using(var reader=new StreamReader(probeStream,Encoding.ASCII,false,1024,true))
                    {while(!string.IsNullOrEmpty(await reader.ReadLineAsync())){} }
                    byte[] empty=Encoding.UTF8.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: "+Encoding.UTF8.GetByteCount(resident)+"\r\nConnection: close\r\n\r\n"+resident);
                    await probeStream.WriteAsync(empty,0,empty.Length);
                }
                using (TcpClient client=await listener.AcceptTcpClientAsync())
                using (NetworkStream stream=client.GetStream())
                {
                    using (StreamReader reader=new StreamReader(stream,Encoding.ASCII,false,1024,true))
                    {
                        string line;int size=0;while (!string.IsNullOrEmpty(line=await reader.ReadLineAsync())) if (line.StartsWith("Content-Length:",StringComparison.OrdinalIgnoreCase)) size=int.Parse(line.Substring(15).Trim());
                        char[] data=new char[size];int offset=0;while(offset<size)offset+=await reader.ReadAsync(data,offset,size-offset);
                    }
                    byte[] head=Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/x-ndjson\r\nConnection: close\r\n\r\n{\"message\":{\"content\":\"partial\"},\"done\":false}\n");
                    await stream.WriteAsync(head,0,head.Length);await stream.FlushAsync();await Task.Delay(3000);
                }
            });
            string previous=state.settings.baseUrl;state.settings.baseUrl="http://127.0.0.1:"+port;
            using (CancellationTokenSource cancellation=new CancellationTokenSource())
            {
                ChatMessage visible=new ChatMessage();Task<ModelTurn> task=StreamTurnAsync(Request("test"),visible,cancellation.Token);
                Stopwatch wait=Stopwatch.StartNew();while (visible.content.Length==0 && wait.ElapsedMilliseconds<4000) {Application.DoEvents();Thread.Sleep(10);}
                Check(visible.content=="partial","Cancellation fixture entered a stalled streaming read");
                Stopwatch timer=Stopwatch.StartNew();cancellation.Cancel();bool canceled=false;
                try {Pump(task,3);} catch (Exception) {canceled=cancellation.IsCancellationRequested;}
                Check(canceled&&timer.ElapsedMilliseconds<2000,"Stop interrupts stalled generation within two seconds");
            }
            state.settings.baseUrl=previous;listener.Stop();
        }
    }
}
