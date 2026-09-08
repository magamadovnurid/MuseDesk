'use strict';
const $=id=>document.getElementById(id),api=(name,value)=>window.muse.call(name,value);
const make=(tag,cls,text)=>{const el=document.createElement(tag);if(cls)el.className=cls;if(text!==undefined)el.textContent=text;return el;};
const icon=name=>{const img=make('img');img.src='assets/'+name+'.svg';img.alt='';return img;};
const button=(text,click,cls='')=>{const el=make('button',cls,text);el.type='button';el.onclick=()=>Promise.resolve().then(click).catch(showError);return el;};
let data={projects:[],chats:[],settings:{},status:{state:'stopped'},busy:false},pending=[],models=[],modalResolve,setupHardware;
let followTail=true,lastChatId=null,drafts=new Map();
const active=()=>data.chats.find(c=>c.id===data.activeId);
const modelName=name=>name?.includes('muse-glimmer')?'Muse Glimmer':name||'Модель';
const date=value=>value?new Date(value).toLocaleString('ru-RU',{day:'2-digit',month:'2-digit',year:'numeric',hour:'2-digit',minute:'2-digit',second:'2-digit'}):'';
function showError(error){$('notice').textContent=error.message||String(error);}
async function update(name,value){data=await api(name,value);render();return data;}
function closeMenu(){$('menu-popup').classList.add('hidden');}
function popup(anchor,items){const menu=$('menu-popup');menu.replaceChildren();for(const item of items){if(!item){menu.append(make('hr'));continue;}const b=button(item.text,async()=>{closeMenu();await item.run();});b.disabled=!!item.disabled;menu.append(b);}const rect=anchor.getBoundingClientRect();menu.classList.remove('hidden');menu.style.left=Math.min(rect.left,innerWidth-menu.offsetWidth-12)+'px';menu.style.top=Math.min(rect.bottom+3,innerHeight-menu.offsetHeight-12)+'px';}
document.addEventListener('pointerdown',e=>{if(!$('menu-popup').contains(e.target)&&!e.target.closest('[data-menu],#chat-more,.more'))closeMenu();});
function modal(title,body,choices=[{text:'Закрыть',value:false}]){
  if(modalResolve){modalResolve(false);modalResolve=null;}if($('modal').open)$('modal').close();
  $('modal-title').textContent=title;$('modal-body').replaceChildren();if(typeof body==='string')$('modal-body').append(make('p','',body));else $('modal-body').append(body);
  $('modal-actions').replaceChildren();return new Promise(resolve=>{modalResolve=resolve;for(const choice of choices){const b=button(choice.text,()=>{$('modal').close();modalResolve=null;resolve(choice.value);},choice.class||'');$('modal-actions').append(b);}$('modal').showModal();$('modal-actions').querySelector('button')?.focus();});
}
$('modal').addEventListener('cancel',()=>{modalResolve?.(false);modalResolve=null;});
async function confirm(title,text){return modal(title,text,[{text:'Отмена',value:false},{text:'Удалить',value:true,class:'danger'}]);}
async function rename(chat){const input=make('input');input.value=chat.title;input.maxLength=120;if(await modal('Название чата',input,[{text:'Отмена',value:false},{text:'Сохранить',value:true,class:'primary'}]))await update('chat-rename',{id:chat.id,title:input.value});}
async function move(chat){const select=make('select');select.append(new Option('Вне проекта',''));for(const p of data.projects)select.append(new Option(p.name,p.id));select.value=chat.projectId||'';if(await modal('Переместить чат',select,[{text:'Отмена',value:false},{text:'Переместить',value:true,class:'primary'}]))await update('chat-move',{id:chat.id,projectId:select.value||null});}
function chatMenu(anchor,chat){if(!chat)return;popup(anchor,[{text:'Переименовать',run:()=>rename(chat),disabled:data.busy},{text:'Переместить…',run:()=>move(chat),disabled:data.busy},{text:'Экспортировать',run:()=>api('export',{id:chat.id})},null,{text:'Удалить чат',disabled:data.busy,run:async()=>{if(await confirm('Удалить чат?',`«${chat.title}» будет удалён из Muse Desk.`))await update('chat-delete',{id:chat.id});}}]);}
function chatRow(chat,nested){const row=make('div','chat-row'+(nested?' nested':'')+(chat.id===data.activeId?' active':''));const select=button(chat.title,()=>update('chat-select',{id:chat.id}));select.disabled=data.busy;row.append(select);const more=button('···',()=>chatMenu(more,chat),'icon more');more.setAttribute('aria-label','Меню чата '+chat.title);row.append(more);return row;}
function renderTree(){const tree=$('tree');tree.replaceChildren();const query=$('search').value.toLowerCase();const matches=chat=>(chat.title+' '+chat.messages.map(m=>m.content).join(' ')).toLowerCase().includes(query);
  if(data.projects.length)tree.append(make('div','eyebrow','Проекты'));
  for(const project of data.projects){const chats=data.chats.filter(c=>c.projectId===project.id&&matches(c));if(query&&!chats.length&&!project.name.toLowerCase().includes(query))continue;
    const row=make('div','project-row');const title=button('',()=>update('project-collapse',{id:project.id}));title.append(icon(project.collapsed?'folder':'folder-open'),document.createTextNode(project.name));title.title=project.path;row.append(title);
    const add=button('+',()=>update('chat-new',{projectId:project.id}),'icon');add.disabled=data.busy;add.setAttribute('aria-label','Добавить чат в '+project.name);row.append(add);
    const more=button('···',()=>popup(more,[{text:'Открыть в Finder',run:()=>api('reveal-project',{id:project.id})},{text:'Удалить проект и чаты',disabled:data.busy,run:async()=>{if(await confirm('Удалить проект?',`Удалить «${project.name}» и его чаты из Muse Desk? Папка и файлы на диске сохранятся.`))await update('project-delete',{id:project.id});}}]),'icon more');more.setAttribute('aria-label','Меню проекта');row.append(more);tree.append(row);if(!project.collapsed||query)for(const chat of chats)tree.append(chatRow(chat,true));
  }
  const recent=data.chats.filter(c=>!c.projectId&&matches(c));if(recent.length)tree.append(make('div','eyebrow','Недавние'));for(const chat of recent)tree.append(chatRow(chat,false));
}
function inline(text,parent){const pattern=/(`[^`\n]+`|\*\*[^*\n]+\*\*|\[[^\]\n]+\]\(https?:\/\/[^\s)]+\))/g;let start=0;for(const match of text.matchAll(pattern)){parent.append(document.createTextNode(text.slice(start,match.index)));const token=match[0];if(token[0]==='`')parent.append(make('code','',token.slice(1,-1)));else if(token.startsWith('**'))parent.append(make('strong','',token.slice(2,-2)));else{const parsed=/^\[([^\]]+)\]\((.+)\)$/.exec(token);const a=make('a','',parsed[1]);a.href=parsed[2];a.onclick=e=>{e.preventDefault();api('open-link',{url:parsed[2]}).catch(showError);};parent.append(a);}start=match.index+token.length;}parent.append(document.createTextNode(text.slice(start)));}
function markdown(text){const block=make('div','message-content');const parts=String(text||'').split(/```[^\n]*\n([\s\S]*?)```/g);parts.forEach((part,index)=>{if(index%2){const pre=make('pre','',part);const copy=button('Копировать код',()=>api('copy',{text:part}));copy.className='secondary';pre.append(copy);block.append(pre);}else{const lines=part.split('\n');lines.forEach((line,i)=>{if(/^#{1,3} /.test(line)){const h=make('h3');inline(line.replace(/^#{1,3} /,''),h);block.append(h);}else inline(line,block);if(i<lines.length-1)block.append(document.createTextNode('\n'));});}});return block;}
function topicExcerpt(text,limit){const plain=String(text||'').replace(/[`#*_]/g,'').replace(/\s+/g,' ').trim();const chars=Array.from(plain);return chars.length>limit?chars.slice(0,limit-1).join('')+'…':plain;}
const topicPreview=$('topic-preview');
const topicObserver=new ResizeObserver(()=>layoutTopics());
function hideTopicPreview(){topicPreview.classList.add('hidden');for(const b of $('topic-nav').children)b.removeAttribute('aria-describedby');}
function layoutTopics(){const host=$('messages');$('topic-nav').hidden=host.scrollHeight<=host.clientHeight;const top=host.getBoundingClientRect().top;for(const mark of $('topic-nav').children){const row=host.children[Number(mark.dataset.message)];if(row)mark.style.top=(7+(row.getBoundingClientRect().top-top+host.scrollTop)*Math.max(1,host.clientHeight-14)/Math.max(1,host.scrollHeight))+'px';}}
function showTopicPreview(mark,title,preview,x,y){topicPreview.querySelector('strong').textContent=title;topicPreview.querySelector('p').textContent=preview||'Перейти к этому вопросу';topicPreview.classList.remove('hidden');mark.setAttribute('aria-describedby','topic-preview');topicPreview.style.left=Math.max(8,Math.min(innerWidth-topicPreview.offsetWidth-8,x-topicPreview.offsetWidth-16))+'px';topicPreview.style.top=Math.max(8,Math.min(innerHeight-topicPreview.offsetHeight-8,y+14))+'px';}
let topicChatId=null;
function renderTopics(messages){
  const host=$('messages'),nav=$('topic-nav');
  const questions=messages.map((message,index)=>({message,index})).filter(item=>item.message.role==='user');
  const same=topicChatId===data.activeId&&questions.length===nav.children.length&&questions.every((q,i)=>nav.children[i].dataset.message===String(q.index)&&nav.children[i].dataset.title===(topicExcerpt(q.message.content,85)||'Вопрос с вложением'));
  topicObserver.disconnect();
  if(!same){hideTopicPreview();nav.replaceChildren();}
  topicChatId=data.activeId;
  questions.forEach(({message,index},position)=>{
    const title=topicExcerpt(message.content,85)||'Вопрос с вложением';
    const following=messages.slice(index+1),end=following.findIndex(m=>m.role==='user');
    const answer=following.slice(0,end<0?following.length:end).filter(m=>m.role==='assistant').at(-1);
    const excerpt=topicExcerpt(answer?.content,140)||(answer?'Ответ формируется…':'Ожидание ответа');
    if(same){nav.children[position].dataset.preview=excerpt;return;}
    const mark=button('',()=>{
      hideTopicPreview();followTail=false;
      const row=host.children[index];
      host.scrollTo({top:host.scrollTop+row.getBoundingClientRect().top-host.getBoundingClientRect().top-12,behavior:matchMedia('(prefers-reduced-motion: reduce)').matches?'instant':'smooth'});
    });
    mark.dataset.message=String(index);mark.dataset.title=title;mark.dataset.preview=excerpt;
    mark.setAttribute('aria-label','Перейти к теме: '+title);
    mark.addEventListener('pointermove',e=>showTopicPreview(mark,title,mark.dataset.preview,e.clientX,e.clientY));
    mark.addEventListener('pointerleave',hideTopicPreview);
    mark.addEventListener('focus',()=>{const r=mark.getBoundingClientRect();showTopicPreview(mark,title,mark.dataset.preview,r.left,r.top);});
    mark.addEventListener('blur',hideTopicPreview);
    mark.addEventListener('keydown',e=>{if(e.key==='Escape')hideTopicPreview();});
    nav.append(mark);
  });
  topicObserver.observe(host);for(const row of host.children)topicObserver.observe(row);layoutTopics();
}
const transcriptExpansion=new Map();
function keepExpanded(details,key,initial=false){details.open=transcriptExpansion.has(key)?transcriptExpansion.get(key):initial;details.addEventListener('toggle',()=>{if(details.isConnected)transcriptExpansion.set(key,details.open);});}
function actionTrace(message,key){
  const group=make('details','action-log');keepExpanded(group,key,true);
  group.append(make('summary','',`Действия · ${message.actions.length}`));
  const names={read_text_file:'Чтение файла',write_text_file:'Запись файла',list_directory:'Просмотр папки',list_files:'Просмотр папки',run_command:'Запуск команды',run_process:'Запуск команды',fetch_web_page:'Загрузка страницы'};
  message.actions.forEach((action,index)=>{
    const detail=make('details','action-entry');keepExpanded(detail,key+':'+index);
    const summary=make('summary');const target=action.args?.path||action.args?.url||action.args?.executable||action.args?.command||'';
    const name=make('span','action-name',(names[action.name]||action.name)+(target?' · '+target:''));name.title=target;
    summary.append(name,make('span','action-state'+(action.status==='ошибка'?' error':''),action.status));detail.append(summary);
    detail.append(make('pre','',JSON.stringify(action.args,null,2)+'\n'+(action.output||'')));group.append(detail);
  });return group;
}
function renderMessages(){const host=$('messages'),oldTop=host.scrollTop;host.replaceChildren();const chat=active();if(!chat?.messages.length){renderTopics([]);const empty=make('div','empty');empty.append(icon('brand'),make('h1','','С чего начнём?'),make('p','',data.status.state==='ready'?'Опишите задачу — Muse поможет.':'Подготовьте модель и дождитесь статуса «Готово».'));host.append(empty);return;}
  for(const [messageIndex,message] of chat.messages.entries()){const article=make('article','message-'+message.role);
    if(message.role==='user'){article.append(make('div','bubble',message.content));for(const image of message.images||[]){const img=make('img','attached-image');img.src='data:image/jpeg;base64,'+image;img.alt='Вложение';article.append(img);}}
    else{
      const key=chat.id+':'+messageIndex;
      if(message.thinking){const details=make('details','thinking');keepExpanded(details,key+':thinking');details.append(make('summary','','Рассуждение'),make('p','',message.thinking));article.append(details);}
      if(message.actions?.length)article.append(actionTrace(message,key+':actions'));
      const final=message.finalSummary||'',content=message.content||'';
      if(final&&content.endsWith(final)){const work=content.slice(0,-final.length).trim();if(work)article.append(markdown(work));}
      if(message.completedAt)article.append(make('div','completion-state',message.failed||message.error?(message.error==='Ответ остановлен'?'Остановлено':'Не завершено'):'Готово'));
      article.append(markdown(final||content));
      if(message.error&&message.completedAt)article.append(make('p','error',message.error));
    }
    const meta=make('div','message-meta');meta.append(make('span','',date(message.role==='user'?message.sentAt:message.completedAt)));if(message.tokensPerSecond)meta.append(make('span','',message.tokensPerSecond.toLocaleString('ru-RU',{maximumFractionDigits:1})+' токенов/с'));
    const copy=button('',()=>api('copy',{text:message.content}),'icon');copy.append(icon('copy'));copy.title='Копировать';copy.setAttribute('aria-label','Копировать сообщение');meta.append(copy);article.append(meta);host.append(article);
  }
  if(followTail)host.scrollTop=host.scrollHeight;else host.scrollTop=oldTop;
  renderTopics(chat.messages);
}
function renderResults(){const chat=active(),results=$('result-list'),sources=$('sources');results.replaceChildren();sources.replaceChildren();const files=[...new Set((chat?.messages||[]).flatMap(m=>(m.actions||[]).map(a=>a.result).filter(Boolean)))];if(!files.length)results.textContent='Созданные файлы появятся здесь.';else for(const file of files)results.append(make('p','',file));const urls=[...new Set((chat?.messages||[]).flatMap(m=>(m.content||'').match(/https?:\/\/[^\s<>"\])]+/g)||[]))];if(!urls.length)sources.textContent='Ссылки из диалога появятся здесь.';else for(const url of urls.slice(0,100))sources.append(button(url,()=>api('open-link',{url})));}
function renderStatus(){const state=data.status.state;$('status').textContent=state==='ready'?'Готово':state==='loading'?'Ожидание':'Остановлено';$('ring').className='ring '+state;$('model-detail').textContent=state==='ready'?modelName(data.settings.model)+' · в памяти GPU':data.status.message||'Muse Glimmer · на устройстве';$('model-status').title=data.status.message||'';controls();}
function controls(){const ready=data.status.state==='ready'&&!data.busy;$('input').disabled=!ready;$('send').disabled=!ready&&!data.busy;$('send').textContent=data.busy?'■':'↑';$('send').setAttribute('aria-label',data.busy?'Остановить':'Отправить промпт');for(const id of ['new','project','attach','attach-side','think','tools','model'])$(id).disabled=data.busy;$('think').textContent='Думать: '+(data.settings.think?'вкл.':'выкл.');$('tools').textContent=data.settings.tools?'Инструменты: вкл.':'Подтверждать';$('tools').title=data.settings.tools?'Каждое действие требует вашего разрешения':'Включить инструменты с подтверждением каждого действия';}
function fitInput(){const input=$('input');input.style.height='28px';const max=Math.max(60,Math.floor($('conversation').clientHeight/3)-72);input.style.maxHeight=max+'px';input.style.height=Math.min(max,input.scrollHeight)+'px';input.style.overflowY=input.scrollHeight>max?'auto':'hidden';}
function renderAttachments(){const host=$('attachments');host.replaceChildren();pending.forEach((file,i)=>host.append(button(file.name+' ×',()=>{pending.splice(i,1);renderAttachments();},'attachment')));}
function render(){if(lastChatId!==data.activeId){if(lastChatId)drafts.set(lastChatId,$('input').value);$('input').value=drafts.get(data.activeId)||'';lastChatId=data.activeId;pending=[];renderAttachments();followTail=true;fitInput();}$('chat-title').textContent=active()?.title||'Новый диалог';renderTree();renderMessages();renderResults();renderStatus();if(!models.includes(data.settings.model))models.unshift(data.settings.model);$('model').replaceChildren(...models.filter(Boolean).map(m=>new Option(modelName(m),m)));$('model').value=data.settings.model;}
async function attach(){const files=await api('attach');if(pending.length+files.length>4)throw Error('Максимум четыре файла');pending.push(...files);renderAttachments();}
async function send(){if(data.busy){await api('stop');return;}const text=$('input').value.trim();if(!text&&!pending.length)return;if(!active())await update('chat-new');const id=data.activeId,attachments=pending;pending=[];$('input').value='';drafts.delete(id);renderAttachments();fitInput();followTail=true;$('notice').textContent='';try{await update('send',{id,text,attachments});}catch(error){$('input').value=text;pending=attachments;renderAttachments();fitInput();throw error;}}
async function settings(){const body=make('div');const label=make('label','','Контекст диалога');const select=make('select');for(const size of [8192,16384,32768])select.append(new Option(size.toLocaleString('ru-RU')+' токенов',String(size)));select.value=String(data.settings.context);body.append(label,select,make('p','','Больший контекст использует больше памяти. При изменении модель будет перезагружена.'),button('Установить / проверить Glimmer',()=>{$('modal').close();modalResolve?.(false);modalResolve=null;return openSetup();}));if(await modal('Настройки Muse Desk',body,[{text:'Закрыть',value:false},{text:'Сохранить',value:true,class:'primary'}]))await update('settings',{context:Number(select.value)});}
const canInstallGlimmer=h=>h?.profile==='glimmer-q4-q8'||h?.profile==='glimmer-q4-f16';
function renderSetupHardware(h){
  setupHardware=h;const eligible=canInstallGlimmer(h);
  $('setup-title').textContent=eligible?'Glimmer подходит вашему Mac':h.profile==='unsupported'?'Этот Mac не поддерживается':h.profile==='unverified'?'Не удалось проверить Mac':'Muse Glimmer не подходит этому Mac';
  $('setup-description').textContent=eligible?'Перед скачиванием параметры Mac будут проверены повторно.':h.profile==='unsupported'?'Для этой версии приложения нужны Apple Silicon и macOS 14+. Установка компонентов заблокирована.':h.profile==='unverified'?'Сначала нужно подтвердить параметры системы. Скачивание заблокировано.':'Модель не будет скачана. Приложение можно оставить для просмотра чатов; работа с Glimmer недоступна.';
  const memory=Number.isFinite(h.ramBytes)?Math.floor(h.ramBytes/1024**3)+' ГБ':'не определена';
  const disk=Number.isFinite(h.freeBytes)?Math.floor(h.freeBytes/1024**3)+' ГиБ':'не определено';
  $('hardware').textContent=h.chip+' · macOS '+h.osVersion+'\nУстановленная объединённая память: '+memory+'\nСвободно на диске: '+disk;
  $('setup-profile').textContent=eligible?'Glimmer Q4_K_M + '+(h.profile.endsWith('f16')?'F16':'Q8')+' кодировщик. Загрузка около '+(h.profile.endsWith('f16')?'21':'19')+' ГБ.':(h.compatibility?.reasons||['Для Glimmer нужны Apple Silicon, macOS 14+, не менее 32 ГБ объединённой памяти и 40 ГиБ для установки.']).join(' ');
  $('setup-install').disabled=!eligible||data.busy;$('setup-install').textContent=eligible?'Установить Glimmer':'Glimmer недоступна';
}
async function openSetup(){renderSetupHardware(await api('hardware'));$('setup-message').textContent='';$('setup-cancel').textContent='Позже';$('setup-progress').style.width='0';$('setup-progress').classList.remove('indeterminate');if(!$('onboarding').open)$('onboarding').showModal();}
async function install(){
  if(data.busy)return;$('setup-install').disabled=true;$('setup-close').disabled=true;
  try{
    renderSetupHardware(await api('hardware'));
    if(!canInstallGlimmer(setupHardware))return;
    $('setup-install').disabled=true;$('setup-cancel').textContent='Остановить';
    await update('install',{engineOnly:false});$('setup-message').textContent='Готово. Можно закрыть это окно.';
  }catch(error){$('setup-message').textContent=error.message;$('setup-install').textContent='Повторить проверку';}
  finally{$('setup-close').disabled=false;$('setup-install').disabled=!canInstallGlimmer(setupHardware)||data.busy;$('setup-cancel').textContent='Закрыть';}
}

function toggleSidebar(){$('sidebar').classList.toggle('hidden');$('reopen').classList.toggle('hidden');fitInput();}
function menuCommand(command){const commands={new:()=>update('chat-new'),project:()=>update('project-add'),attach,export:()=>active()&&api('export',{id:data.activeId}),settings,setup:openSetup,sidebar:toggleSidebar,results:()=>$('results').classList.toggle('hidden')};return commands[command]?.();}
for(const el of document.querySelectorAll('[data-menu]'))el.onclick=()=>{const groups={file:[{text:'Новый чат',run:()=>menuCommand('new'),disabled:data.busy},{text:'Добавить проект…',run:()=>menuCommand('project'),disabled:data.busy},{text:'Добавить файлы…',run:attach,disabled:data.busy},{text:'Экспорт…',run:()=>menuCommand('export')}],edit:[{text:'Выделить текст промпта',run:()=>{$('input').focus();$('input').select();}},{text:'Копировать промпт',run:()=>api('copy',{text:$('input').value})}],view:[{text:(!$('sidebar').classList.contains('hidden')?'✓  ':'    ')+'Боковая колонка',run:toggleSidebar},{text:(!$('results').classList.contains('hidden')?'✓  ':'    ')+'Результаты и источники',run:()=>menuCommand('results')}],help:[{text:'Настройка Glimmer',run:openSetup},{text:'Журналы',run:()=>api('logs')},{text:'О Muse Desk',run:()=>modal('Muse Desk 1.23','Локальный помощник для Apple Silicon.\nПредварительная версия macOS. Проекты, чаты и модель находятся на вашем компьютере.')}]};popup(el,groups[el.dataset.menu]);};
const bind=(id,fn)=>$(id).onclick=()=>Promise.resolve().then(fn).catch(showError);
bind('new',()=>update('chat-new'));bind('project',()=>update('project-add'));bind('collapse',toggleSidebar);bind('reopen',toggleSidebar);bind('features',()=>modal('Возможности','Локальные ответы и рассуждения, текстовые файлы и изображения, проекты и чаты. Инструменты читают и записывают файлы, выполняют команды с вашим подтверждением. Включите их в поле ввода для чата в проекте.'));bind('attach',attach);bind('attach-side',attach);bind('settings',settings);bind('settings-top',settings);bind('export',()=>menuCommand('export'));bind('results-toggle',()=>menuCommand('results'));bind('chat-more',()=>chatMenu($('chat-more'),active()));bind('model-status',()=>update('prepare'));bind('think',()=>update('settings',{think:!data.settings.think}));bind('tools',()=>update('settings',{tools:!data.settings.tools}));bind('send',send);bind('setup-install',install);bind('setup-close',()=>{if(!data.busy)$('onboarding').close();});bind('setup-cancel',()=>{if(data.busy)return api('stop');$('onboarding').close();});
bind('speak',()=>{if(speechSynthesis.speaking){speechSynthesis.cancel();return;}const text=active()?.messages.filter(m=>m.role==='assistant').at(-1)?.content;if(text){const utterance=new SpeechSynthesisUtterance(text);utterance.lang='ru-RU';speechSynthesis.speak(utterance);}});
$('model').onchange=()=>update('settings',{model:$('model').value}).catch(showError);$('search').oninput=renderTree;$('input').oninput=fitInput;window.onresize=fitInput;
$('messages').addEventListener('scroll',()=>{const el=$('messages');followTail=el.scrollHeight-el.scrollTop-el.clientHeight<70;});
$('input').onkeydown=e=>{if(e.key==='Enter'&&!e.shiftKey&&!e.isComposing){e.preventDefault();send().catch(showError);}};
document.addEventListener('keydown',e=>{if(e.key==='Escape'&&data.busy){api('stop');}if((e.metaKey||e.ctrlKey)&&e.key==='n'){e.preventDefault();if(!data.busy)update('chat-new').catch(showError);}});
$('onboarding').addEventListener('cancel',e=>{if(data.busy){e.preventDefault();api('stop');}});
window.muse.onEvent(async({type,data:payload})=>{try{
  if(type==='state'){data=payload;render();}
  if(type==='status'){data.status=payload;renderStatus();}
  if(type==='stream'){const chat=data.chats.find(c=>c.id===payload.chatId);if(chat){chat.messages[chat.messages.length-1]=payload.message;if(chat.id===data.activeId)renderMessages();}}
  if(type==='notice')showError(payload);
  if(type==='menu')await menuCommand(payload);
  if(type==='install'){const progress=$('setup-progress');progress.classList.toggle('indeterminate',payload.percent<0);if(payload.percent>=0)progress.style.width=payload.percent+'%';$('setup-message').textContent=payload.message;}
  if(type==='permission'){const body=make('div');body.append(make('p','',payload.chatTitle+'\n'+payload.folder),make('pre','',payload.detail));const allow=await modal(payload.title,body,[{text:'Отклонить',value:false},{text:'Разрешить один раз',value:true,class:'primary'}]);await api('permission',{id:payload.id,allow});}
  if(type==='permission-cancel'){if($('modal').open)$('modal').close();modalResolve?.(false);modalResolve=null;}
}catch(error){showError(error);}});
(async()=>{data=await api('load');render();try{models=await api('models');render();}catch{}window.museReady=true;})().catch(showError);
