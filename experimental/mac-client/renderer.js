const $=id=>document.getElementById(id);
let chats=[],current=null,pending=[],running=false,requestId=null,answer=null,connected=false;
const make=(tag,cls,text)=>{const e=document.createElement(tag);if(cls)e.className=cls;if(text!==undefined)e.textContent=text;return e;};
function notice(text=''){$('notice').textContent=text;}
function persist(){return window.muse.save(chats).catch(e=>notice('История не сохранена: '+e.message));}
function newChat(){if(running)return;current={id:crypto.randomUUID(),title:'Новый диалог',messages:[]};chats.unshift(current);pending=[];render();persist();$('input').focus();}
function sidebar(){const q=$('search').value.toLowerCase();$('chats').replaceChildren();for(const chat of chats){if(!(chat.title+' '+chat.messages.map(m=>m.content).join(' ')).toLowerCase().includes(q))continue;const b=make('button',chat===current?'active':'',chat.title);b.disabled=running;b.onclick=()=>{current=chat;pending=[];render();};$('chats').append(b);}}
function drawWelcome(){const w=make('div','welcome');w.append(make('span','mark','✧'),make('h2','','С чего начнём?'),make('p','','Спрашивайте, создавайте и разбирайтесь в сложном.'));const p=make('p','',connected?'Muse работает на Windows. Вы общаетесь с Mac.':'Подключите Muse на Windows, чтобы начать диалог.');w.append(p);const ideas=make('div','ideas');for(const [icon,title,subtitle,prompt] of [['✧','Исследовать идею','Найти подход и продумать детали','Помоги мне продумать новую идею.'],['▤','Разобрать текст','Выделить главное и сделать выводы','Помоги разобраться в этом тексте: '],['▧','Посмотреть на фото','Заметить детали и найти ответы','Что изображено на этой фотографии?']]){const b=make('button','idea');b.append(make('b','',icon),make('strong','',title),make('small','',subtitle));b.onclick=()=>{$('input').value=prompt;$('input').focus();};ideas.append(b);}w.append(ideas);$('messages').append(w);}
function drawMessage(m){const card=make('article','message '+m.role),role=make('div','role');if(m.role==='assistant')role.append(make('span','mark','✧'));role.append(make('span','',m.role==='user'?'Вы':'Muse Glimmer'));const copy=make('button','copy','Копировать');copy.onclick=async()=>{try{await navigator.clipboard.writeText(m.content);copy.textContent='Скопировано';}catch{notice('Не удалось скопировать текст. Выделите его и нажмите ⌘ C.');}};role.append(copy);card.append(role);if(m.thinking){const d=make('details');d.append(make('summary','','Рассуждение'),make('pre','',m.thinking));card.append(d);}card.append(make('div','content',m.content||(!m.failed?'Muse думает…':'')));for(const img of m.images||[]){const el=make('img');el.src='data:image/jpeg;base64,'+img;el.alt='Прикреплённое изображение';card.append(el);}if(m.failed)card.append(make('div','failure',m.error||'Ответ не завершён.'));return card;}
function renderMessages(){const area=$('messages');const bottom=area.scrollHeight-area.scrollTop-area.clientHeight<100;area.replaceChildren();if(!current?.messages.length)drawWelcome();else for(const m of current.messages)area.append(drawMessage(m));if(bottom)area.scrollTop=area.scrollHeight;}
function attachments(){$('attachments').replaceChildren();pending.forEach((f,i)=>{const chip=make('span','chip',f.name);const remove=make('button','','×');remove.setAttribute('aria-label','Убрать '+f.name);remove.onclick=()=>{pending.splice(i,1);attachments();};chip.append(remove);$('attachments').append(chip);});}
function controls(){$('send').textContent=running?'■':'↑';$('send').setAttribute('aria-label',running?'Остановить ответ':'Отправить сообщение');for(const id of ['attach','new','remove','pair','think'])$(id).disabled=running;$('input').readOnly=running;sidebar();}
function render(){$('title').textContent=current?.title||'Новый диалог';sidebar();renderMessages();attachments();controls();}
async function health(){try{const r=await window.muse.health();connected=r.ready===true;$('status').textContent=connected?'Muse готова':'Модель недоступна';$('dot').classList.toggle('ready',connected);if(connected)notice();}catch(e){connected=false;$('status').textContent='Нет подключения';$('dot').classList.remove('ready');notice(e.message);}if(!current?.messages.length)renderMessages();}
$('pair').onclick=async()=>{try{const p=await window.muse.pair();if(p){$('address').textContent=p.address;await health();}}catch(e){notice(e.message);}};
$('new').onclick=newChat;$('search').oninput=sidebar;
$('attach').onclick=async()=>{try{const files=await window.muse.attach();if(pending.length+files.length>4)throw Error('Можно приложить до четырёх файлов.');pending.push(...files);attachments();}catch(e){notice(e.message);}};
$('export').onclick=()=>current&&window.muse.export(current).catch(e=>notice(e.message));
$('remove').onclick=()=>{if(running||!current)return;if(!confirm('Удалить этот диалог с Mac?'))return;chats=chats.filter(c=>c!==current);current=chats[0];if(!current)newChat();else{render();persist();}};
$('input').oninput=()=>{$('input').style.height='68px';$('input').style.height=Math.min(180,$('input').scrollHeight)+'px';};
async function send(){
  if(running){await window.muse.stop();return;}
  const text=$('input').value.trim();if(!text&&!pending.length)return;
  if(!connected){notice('Подключите Muse на Windows перед отправкой.');return;}
  if(!current)newChat();
  notice();const full=text+pending.filter(f=>f.text!==undefined).map(f=>'\n\n--- Файл: '+f.name+' ---\n'+f.text).join('');
  const m={role:'user',content:full,images:pending.filter(f=>f.image).map(f=>f.image)};
  current.messages.push(m);if(current.title==='Новый диалог')current.title=(text||pending[0]?.name||'Диалог').slice(0,52);
  const wire=current.messages.filter(m=>!m.failed).map(m=>({role:m.role,content:m.content,...(m.images?.length?{images:m.images}:{})}));
  wire.unshift({role:'system',content:'Ты Muse Glimmer, домашний ассистент. Отвечай на языке пользователя. Используй текст и изображения. Не утверждай, что можешь управлять компьютером: в этом сетевом клиенте таких инструментов нет.'});
  answer={role:'assistant',content:'',thinking:'',failed:true,error:'Ответ не завершён.'};current.messages.push(answer);pending=[];$('input').value='';$('input').style.height='68px';running=true;requestId=crypto.randomUUID();render();$('messages').scrollTop=$('messages').scrollHeight;await persist();
  try{const result=await window.muse.send({messages:wire,think:$('think').checked,id:requestId});if(!result.ok){answer.failed=true;answer.error=result.error;notice(result.error);}else if(!answer.done){answer.failed=true;answer.error='Соединение прервалось до завершения ответа.';}}catch(e){answer.failed=true;answer.error=e.message;notice(e.message);}finally{running=false;requestId=null;render();await persist();}
}
window.muse.onChunk(({id,part})=>{if(id!==requestId||!answer)return;if(part.error){answer.failed=true;answer.error=String(part.error);return;}answer.content+=part.message?.content||'';answer.thinking+=part.message?.thinking||'';if(part.done){answer.done=true;answer.failed=false;delete answer.error;}renderMessages();});
$('send').onclick=send;
$('input').onkeydown=e=>{if(e.key==='Enter'&&!e.shiftKey&&!e.isComposing){e.preventDefault();send();}if(e.key==='Escape'&&running)window.muse.stop();};
document.onkeydown=e=>{if((e.metaKey||e.ctrlKey)&&e.key==='n'){e.preventDefault();newChat();}};
$('speak').onclick=()=>{if(speechSynthesis.speaking){speechSynthesis.cancel();return;}const text=current?.messages.filter(m=>m.role==='assistant').at(-1)?.content;if(!text)return;const speech=new SpeechSynthesisUtterance(text);speech.lang='ru-RU';speechSynthesis.speak(speech);};
(async()=>{const data=await window.muse.load();chats=data.chats;current=chats[0];connected=false;if(!current)newChat();else render();if(data.address)$('address').textContent=data.address;if(data.connected)await health();if(data.recovered)notice('История восстановлена из резервной копии.');})().catch(e=>notice(e.message));
setInterval(()=>{if(!running)health();},30000);
