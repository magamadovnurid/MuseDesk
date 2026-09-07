'use strict';
const {app,BrowserWindow,ipcMain,dialog,Menu,shell,clipboard,nativeImage}=require('electron');
const fs=require('node:fs');
const path=require('node:path');
const os=require('node:os');
const crypto=require('node:crypto');
const {Store,GiB}=require('./core.cjs');
const setup=require('./setup.cjs');
const {Engine}=require('./engine.cjs');
const tools=require('./tools.cjs');
const smoke=process.argv.includes('--smoke-test');
if(smoke)app.disableHardwareAcceleration();
app.setName('Muse Desk');
if(smoke)app.setPath('userData',fs.mkdtempSync(path.join(os.tmpdir(),'musedesk-ui-')));
let win,store,engine,installController,requestController,quitting=false;
let status={state:'stopped',message:'Остановлено'},installStatus=null;
const approvals=new Map();
const root=()=>path.join(app.getPath('userData'),'local');
const emit=(type,data)=>{if(win&&!win.isDestroyed())win.webContents.send('muse:event',{type,data});};
const snapshot=()=>({...store.state,status,installStatus,busy:!!requestController||!!installController||engine.preparing});
const stateChanged=()=>emit('state',snapshot());
const ensureIdle=()=>{if(requestController||installController||engine.preparing)throw Error('Сначала дождитесь завершения текущего действия');};
const notify=error=>emit('notice',error.message||String(error));
function cancel(){requestController?.abort();installController?.abort();for(const resolve of approvals.values())resolve(false);approvals.clear();emit('permission-cancel',null);}
function permission(data){if(requestController?.signal.aborted)return Promise.resolve(false);return new Promise(resolve=>{const id=crypto.randomUUID();approvals.set(id,resolve);emit('permission',{id,...data});});}
async function prepare(){if(smoke){status={state:'ready',message:'Готово',model:store.state.settings.model};stateChanged();return;}await engine.prepare(store.state.settings.model,store.state.settings.context);}
async function send({id,text,attachments=[]}){
  ensureIdle();const chat=store.chat(id);const settings={...store.state.settings};
  if(typeof text!=='string'||text.length>200000||!Array.isArray(attachments)||attachments.length>4)throw Error('Некорректное сообщение');
  if(!text.trim()&&!attachments.length)return;
  const controller=new AbortController();requestController=controller;
  const project=store.state.projects.find(p=>p.id===chat.projectId);let answer;
  try{
    await engine.assertReady(settings.model);
    const content=text+attachments.filter(a=>typeof a.text==='string').map(a=>'\n\n--- '+a.name+' ---\n'+a.text).join('');
    if(JSON.stringify(attachments).length>20*1024*1024)throw Error('Слишком большой объём вложений');
    const user={role:'user',content,images:attachments.filter(a=>typeof a.image==='string').map(a=>a.image),sentAt:new Date().toISOString()};
    const candidate={...chat,messages:[...chat.messages,user]};const wire=tools.messagesFor(candidate,settings);
    chat.messages.push(user);if(chat.title==='Новый диалог')chat.title=(text.trim()||attachments[0]?.name||'Диалог').slice(0,60);
    answer={role:'assistant',content:'',thinking:'',failed:true,error:'Ответ не завершён',actions:[],wire:[]};chat.messages.push(answer);chat.updatedAt=user.sentAt;store.save();stateChanged();
    for(let round=0;round<12;round++){
      if(controller.signal.aborted)throw Error('Ответ остановлен');
      await engine.assertReady(settings.model);let segment={role:'assistant',content:'',thinking:'',tool_calls:[]};
      await engine.api('/api/chat',{model:settings.model,messages:wire,think:settings.think,stream:true,keep_alive:-1,options:{num_ctx:settings.context},...(settings.tools&&project?{tools:tools.definitions}:{})},{signal:controller.signal,timeout:600000,onChunk:part=>{
        segment.content+=part.message?.content||'';segment.thinking+=part.message?.thinking||'';if(part.message?.tool_calls)segment.tool_calls.push(...part.message.tool_calls);
        answer.content+=part.message?.content||'';answer.thinking+=part.message?.thinking||'';
        if(part.done){answer.tokensPerSecond=part.eval_duration?part.eval_count/(part.eval_duration/1e9):null;}
        emit('stream',{chatId:chat.id,message:answer});
      }});
      if(!segment.tool_calls.length)delete segment.tool_calls;if(!segment.thinking)delete segment.thinking;
      wire.push(segment);answer.wire.push(segment);
      if(!segment.tool_calls?.length){answer.failed=false;delete answer.error;break;}
      if(!settings.tools||!project)throw Error('Модель запросила недоступный инструмент');
      if(segment.tool_calls.length>8)throw Error('Модель запросила слишком много действий за один шаг');
      for(const call of segment.tool_calls){
        const action={name:call.function?.name,args:call.function?.arguments,status:'ожидание',at:new Date().toISOString()};answer.actions.push(action);stateChanged();
        let result;try{result=await tools.execute(call,{project,signal:controller.signal,approve:data=>permission({...data,chatTitle:chat.title})});action.status='завершено';}catch(error){result={text:error.message};action.status='ошибка';}
        action.output=result.text;action.result=result.result;const reply={role:'tool',tool_name:call.function.name,content:result.text.slice(0,18000)};wire.push(reply);answer.wire.push(reply);store.save();stateChanged();
      }
      if(tools.wireCost(wire)>settings.context*3)throw Error('Достигнут предел контекста инструментов. Начните новый чат.');
      if(round===11)throw Error('Достигнут предел 12 шагов. Проверьте выполненные действия.');
    }
  }catch(error){if(answer){answer.failed=true;answer.error=controller.signal.aborted?'Ответ остановлен':error.message;}else throw error;
  }finally{if(answer)answer.completedAt=new Date().toISOString();requestController=null;store.save();stateChanged();}
}
async function action(name,value={}){
  switch(name){
    case 'load':return snapshot();
    case 'hardware':return smoke?{profile:'glimmer-q4-q8',ramBytes:48*GiB,freeBytes:160*GiB,chip:'Apple M4 Pro',osVersion:'15.6',arch:'arm64'}:setup.probe(root());
    case 'prepare':ensureIdle();await prepare();return snapshot();
    case 'models':return smoke?[store.state.settings.model]:engine.models();
    case 'install':{
      ensureIdle();if(smoke)throw Error('Скачивание отключено в тесте');installController=new AbortController();stateChanged();
      try{await setup.install(root(),{signal:installController.signal,engineOnly:value.engineOnly===true,onProgress:part=>{installStatus=part;emit('install',part);}});}
      finally{installController=null;stateChanged();}
      if(!value.engineOnly)await prepare();return snapshot();
    }
    case 'stop':cancel();return;
    case 'permission':{const resolve=approvals.get(value.id);if(resolve){approvals.delete(value.id);resolve(value.allow===true);}return;}
    case 'chat-new':ensureIdle();store.newChat(value.projectId||null);return snapshot();
    case 'chat-select':ensureIdle();store.chat(value.id);store.state.activeId=value.id;store.save();return snapshot();
    case 'chat-delete':ensureIdle();store.removeChat(value.id);return snapshot();
    case 'chat-rename':ensureIdle();if(typeof value.title!=='string'||!value.title.trim())throw Error('Введите название');store.chat(value.id).title=value.title.trim().slice(0,120);store.save();return snapshot();
    case 'chat-move':ensureIdle();store.moveChat(value.id,value.projectId||null);return snapshot();
    case 'project-add':{
      ensureIdle();const selected=await dialog.showOpenDialog(win,{title:'Добавить папку проекта',properties:['openDirectory','createDirectory']});if(!selected.canceled)store.addProject(selected.filePaths[0]);return snapshot();
    }
    case 'project-delete':ensureIdle();store.removeProject(value.id);return snapshot();
    case 'project-collapse':{const p=store.state.projects.find(p=>p.id===value.id);if(p){p.collapsed=!p.collapsed;store.save();}return snapshot();}
    case 'settings':{
      ensureIdle();const before={...store.state.settings};const next=value;
      if(next.model!==undefined){if(typeof next.model!=='string'||next.model.length>180)throw Error('Неверная модель');store.state.settings.model=next.model;}
      if(next.context!==undefined){if(![8192,16384,32768].includes(next.context))throw Error('Неверный размер контекста');store.state.settings.context=next.context;}
      for(const field of ['think','tools'])if(typeof next[field]==='boolean')store.state.settings[field]=next[field];store.save();
      if(before.model!==store.state.settings.model||before.context!==store.state.settings.context)await prepare();return snapshot();
    }
    case 'send':if(smoke)throw Error('Генерация отключена в UI-тесте');await send(value);return snapshot();
    case 'attach':{
      ensureIdle();const pick=await dialog.showOpenDialog(win,{title:'Добавить текст или изображение',properties:['openFile','multiSelections'],filters:[{name:'Файлы',extensions:['txt','md','csv','json','js','ts','py','html','css','log','png','jpg','jpeg','webp']}]});if(pick.canceled)return[];if(pick.filePaths.length>4)throw Error('Максимум четыре файла');
      return pick.filePaths.map(p=>{if(fs.statSync(p).size>8*1024*1024)throw Error('Файл больше 8 МБ: '+path.basename(p));const name=path.basename(p);if(/\.(png|jpe?g|webp)$/i.test(p)){let image=nativeImage.createFromPath(p);if(image.isEmpty())throw Error('Не удалось прочитать изображение');const size=image.getSize();if(Math.max(size.width,size.height)>1600)image=image.resize(size.width>size.height?{width:1600}:{height:1600});return {name,image:image.toJPEG(85).toString('base64')};}return {name,text:fs.readFileSync(p,'utf8').slice(0,80000)};});
    }
    case 'copy':if(typeof value.text==='string')clipboard.writeText(value.text);return;
    case 'export':{const chat=store.chat(value.id);const pick=await dialog.showSaveDialog(win,{defaultPath:'Muse Desk — '+chat.title.replace(/[/:]/g,'-')+'.md',filters:[{name:'Markdown',extensions:['md']}]});if(!pick.canceled)fs.writeFileSync(pick.filePath,'# '+chat.title+'\n\n'+chat.messages.map(m=>'## '+(m.role==='user'?'Вы':'Muse')+' · '+(m.sentAt||m.completedAt||'')+'\n\n'+m.content).join('\n\n'),'utf8');return;}
    case 'open-link':{const url=new URL(value.url);if(!['https:','http:'].includes(url.protocol))throw Error('Неподдерживаемая ссылка');return shell.openExternal(url.href);}
    case 'reveal-project':{const project=store.state.projects.find(p=>p.id===value.id);if(project)shell.showItemInFolder(project.path);return;}
    case 'logs':shell.showItemInFolder(root());return;
    default:throw Error('Неизвестное действие');
  }
}
if(!app.requestSingleInstanceLock())app.quit();
app.on('second-instance',()=>{win?.show();win?.focus();});
app.whenReady().then(async()=>{
  store=new Store(app.getPath('userData'));fs.mkdirSync(root(),{recursive:true,mode:0o700});
  if(smoke){const folder=path.join(app.getPath('userData'),'Muse Studio');fs.mkdirSync(folder);const project=store.addProject(folder);const chat=store.newChat(project.id);chat.title='Интерфейс приложения';chat.messages=[{role:'user',content:'Помоги спланировать приложение для личных заметок.',sentAt:'2026-09-07T12:00:00Z'},{role:'assistant',content:'Начнём с главного\n\n**Первый выпуск**\n- Создание и редактирование заметок\n- Поиск по тексту\n- Группировка по проектам\n- Локальное хранение\n\nДальше определим структуру экранов и подготовим первый прототип.',completedAt:'2026-09-07T12:00:12Z',tokensPerSecond:36.1}];store.save();status={state:'ready',message:'Готово',model:store.state.settings.model};}
  engine=new Engine(root(),data=>{status=data;emit('status',data);});
  Menu.setApplicationMenu(Menu.buildFromTemplate([
    {label:'Muse Desk',submenu:[{role:'about'},{label:'Настройки…',accelerator:'CmdOrCtrl+,',click:()=>emit('menu','settings')},{type:'separator'},{role:'hide'},{role:'hideOthers'},{role:'quit'}]},
    {label:'Файл',submenu:[{label:'Новый чат',accelerator:'CmdOrCtrl+N',click:()=>emit('menu','new')},{label:'Добавить проект…',click:()=>emit('menu','project')},{label:'Добавить файлы…',click:()=>emit('menu','attach')},{label:'Экспорт…',click:()=>emit('menu','export')},{role:'close'}]},
    {label:'Правка',submenu:[{role:'undo'},{role:'redo'},{type:'separator'},{role:'cut'},{role:'copy'},{role:'paste'},{role:'selectAll'}]},
    {label:'Вид',submenu:[{label:'Боковая колонка',click:()=>emit('menu','sidebar')},{label:'Результаты и источники',click:()=>emit('menu','results')},{role:'togglefullscreen'},{role:'resetZoom'},{role:'zoomIn'},{role:'zoomOut'}]},
    {label:'Окно',submenu:[{role:'minimize'},{role:'zoom'},{role:'front'}]},
    {label:'Справка',submenu:[{label:'Установка Glimmer',click:()=>emit('menu','setup')},{label:'Журналы',click:()=>shell.showItemInFolder(root())},{label:'GitHub Releases',click:()=>shell.openExternal('https://github.com/magamadovnurid/MuseDesk/releases')}]}]));
  win=new BrowserWindow({width:1340,height:900,minWidth:940,minHeight:650,title:'Muse Desk',backgroundColor:'#f5f5f5',titleBarStyle:'hiddenInset',trafficLightPosition:{x:17,y:16},show:!smoke,webPreferences:{preload:path.join(__dirname,'preload.cjs'),contextIsolation:true,nodeIntegration:false,sandbox:true,webSecurity:true,backgroundThrottling:false,offscreen:smoke}});
  win.webContents.setWindowOpenHandler(()=>({action:'deny'}));win.webContents.on('will-navigate',e=>e.preventDefault());win.webContents.session.setPermissionRequestHandler((_w,_p,cb)=>cb(false));
  ipcMain.handle('muse:call',async(event,name,value)=>{if(event.sender!==win.webContents||event.senderFrame!==win.webContents.mainFrame)throw Error('Недопустимый источник');try{return {ok:true,value:await action(name,value)};}catch(error){return {ok:false,error:error.message};}});
  await win.loadFile('index.html');
  if(smoke){await require('./smoke.cjs').run(win,store,app);return;}
  if(store.recovered)emit('notice','История восстановлена из резервной копии.');
  if(setup.findEngine(root()))prepare().catch(notify);else emit('menu','setup');
}).catch(error=>{console.error(error);app.exit(1);});
app.on('before-quit',event=>{if(quitting||smoke)return;event.preventDefault();quitting=true;cancel();Promise.resolve(engine?.close()).finally(()=>app.quit());});
app.on('window-all-closed',()=>app.quit());
