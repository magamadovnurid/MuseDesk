const {app,BrowserWindow,ipcMain,dialog,Menu,safeStorage,nativeImage}=require('electron');
const fs=require('node:fs');
const path=require('node:path');
const https=require('node:https');
const tls=require('node:tls');
const {timingSafeEqual}=require('node:crypto');
let win,profile,active;
app.setName('Muse Desk');
if(!app.requestSingleInstanceLock()) app.quit();
app.on('second-instance',()=>{if(win){win.show();win.focus();}});
const file=name=>path.join(app.getPath('userData'),name);
function saveJSON(name,value){fs.mkdirSync(app.getPath('userData'),{recursive:true});const temp=file(name+'.tmp');fs.writeFileSync(temp,JSON.stringify(value),{mode:0o600});fs.renameSync(temp,file(name));}
function validateProfile(p){
  const u=new URL(p.url);
  if(u.protocol!=='https:' || u.username || u.password || u.pathname!=='/' || u.search || u.hash || !/^(192\.168\.|10\.|172\.(1[6-9]|2\d|3[01])\.|100\.(6[4-9]|[7-9]\d|1[01]\d|12[0-7])\.)\d/.test(u.hostname)) throw Error('Нужен HTTPS-адрес Windows в частной сети Tailscale.');
  if(!/^[a-f0-9]{64}$/.test(p.token)||!/^[a-f0-9]{64}$/.test(p.certificateSHA256)) throw Error('Файл подключения повреждён.');
  return {url:u.origin,token:p.token,certificateSHA256:p.certificateSHA256};
}
function request(route,body,onChunk){
  if(!profile) return Promise.reject(Error('Импортируйте файл подключения с Windows.'));
  const p={...profile},u=new URL(p.url),data=body?Buffer.from(JSON.stringify(body)):null;
  return new Promise((resolve,reject)=>{
    const agent=new https.Agent({keepAlive:false});
    agent.createConnection=(_options,callback)=>{
      const socket=tls.connect({host:u.hostname,port:Number(u.port)||443,rejectUnauthorized:false,minVersion:'TLSv1.2'});
      let called=false; const done=(error)=>{if(called)return;called=true;callback(error,error?undefined:socket);};
      socket.setTimeout(10000,()=>{const e=Error('Windows не отвечает. Включите Tailscale на Mac и Muse Desk на Windows.');done(e);socket.destroy();});
      socket.once('error',done);
      socket.once('secureConnect',()=>{
        const actual=Buffer.from((socket.getPeerCertificate().fingerprint256||'').replaceAll(':','').toLowerCase());
        const expected=Buffer.from(p.certificateSHA256);
        if(actual.length!==expected.length||!timingSafeEqual(actual,expected)){done(Error('Сертификат Windows изменился. Подключение остановлено.'));socket.destroy();return;}
        socket.setTimeout(0); done(null);
      });
    };
    const req=https.request(new URL(route,p.url),{method:data?'POST':'GET',agent,headers:{Authorization:'Bearer '+p.token,...(data?{'Content-Type':'application/json','Content-Length':data.length}:{})}},res=>{
      let buffer='',result='',bad='';
      res.setEncoding('utf8');
      res.on('data',chunk=>{
        if(res.statusCode!==200){bad+=chunk;return;}
        if(!onChunk){result+=chunk;return;}
        buffer+=chunk;
        for(let i;(i=buffer.indexOf('\n'))>=0;){const line=buffer.slice(0,i);buffer=buffer.slice(i+1);if(line.trim()){try{onChunk(JSON.parse(line));}catch{req.destroy(Error('Не удалось прочитать ответ Muse.'));}}}
      });
      res.on('error',reject);
      res.on('end',()=>{
        if(res.statusCode!==200){let message='Ошибка подключения ('+res.statusCode+').';try{message=JSON.parse(bad).error||message;}catch{}reject(Error(message));return;}
        try{if(onChunk&&buffer.trim())onChunk(JSON.parse(buffer));resolve(onChunk?null:JSON.parse(result));}catch{reject(Error('Неполный ответ сервера.'));}
      });
    });
    if(onChunk) active=req;
    req.setTimeout(120000,()=>req.destroy(Error('Muse долго не отвечает. Повторите запрос.')));
    req.on('error',reject);req.on('close',()=>{agent.destroy();if(active===req)active=null;});
    if(data)req.write(data);req.end();
  });
}
function trusted(event){if(!win||event.sender!==win.webContents||event.senderFrame!==win.webContents.mainFrame)throw Error('Недопустимый источник.');}
function handle(name,fn){ipcMain.handle(name,async(event,...args)=>{trusted(event);return fn(...args);});}
app.whenReady().then(()=>{
  try{if(safeStorage.isEncryptionAvailable()){const p=JSON.parse(fs.readFileSync(file('connection.json'),'utf8'));profile=validateProfile(JSON.parse(safeStorage.decryptString(Buffer.from(p.encrypted,'base64'))));}}catch{}
  Menu.setApplicationMenu(Menu.buildFromTemplate([{label:'Muse Desk',submenu:[{role:'about'},{type:'separator'},{role:'hide'},{role:'hideOthers'},{role:'unhide'},{type:'separator'},{role:'quit'}]},{label:'Правка',submenu:[{role:'undo'},{role:'redo'},{type:'separator'},{role:'cut'},{role:'copy'},{role:'paste'},{role:'selectAll'}]},{label:'Окно',submenu:[{role:'minimize'},{role:'zoom'},{role:'front'}]}]));
  win=new BrowserWindow({width:1260,height:840,minWidth:850,minHeight:600,title:'Muse Desk',backgroundColor:'#f7f8fc',titleBarStyle:'hiddenInset',webPreferences:{preload:path.join(__dirname,'preload.cjs'),contextIsolation:true,nodeIntegration:false,sandbox:true,webSecurity:true}});
  win.webContents.setWindowOpenHandler(()=>({action:'deny'}));
  win.webContents.on('will-navigate',event=>event.preventDefault());
  win.webContents.session.setPermissionRequestHandler((_wc,_permission,callback)=>callback(false));
  win.loadFile('index.html');
  handle('load',()=>{let chats=[];let recovered=false;try{chats=JSON.parse(fs.readFileSync(file('chats.json'),'utf8'));}catch{try{chats=JSON.parse(fs.readFileSync(file('chats.json.bak'),'utf8'));recovered=true;}catch{}}return{chats:Array.isArray(chats)?chats:[],connected:!!profile,address:profile?.url||'',recovered};});
  handle('save',chats=>{if(!Array.isArray(chats)||JSON.stringify(chats).length>40*1024*1024)throw Error('История слишком большая.');if(fs.existsSync(file('chats.json')))fs.copyFileSync(file('chats.json'),file('chats.json.bak'));saveJSON('chats.json',chats);});
  handle('pair',async()=>{
    if(active)throw Error('Сначала остановите ответ.');
    const selected=await dialog.showOpenDialog(win,{title:'Подключить Muse на Windows',properties:['openFile'],filters:[{name:'Подключение Muse Desk',extensions:['museconnection']}]});
    if(selected.canceled)return null;
    if(fs.statSync(selected.filePaths[0]).size>4096)throw Error('Неверный файл подключения.');
    const p=validateProfile(JSON.parse(fs.readFileSync(selected.filePaths[0],'utf8').replace(/^\uFEFF/,'')));
    if(!safeStorage.isEncryptionAvailable())throw Error('Связка ключей macOS недоступна. Разрешите доступ и повторите.');
    saveJSON('connection.json',{encrypted:safeStorage.encryptString(JSON.stringify(p)).toString('base64')});profile=p;
    return {address:profile.url};
  });
  handle('health',()=>request('/health'));
  handle('send',async({messages,think,id})=>{
    if(active)throw Error('Дождитесь текущего ответа.');
    if(!Array.isArray(messages)||messages.length>150)throw Error('Слишком длинный диалог. Создайте новый чат.');
    try{await request('/chat',{messages,think:think===true},part=>{if(!win.isDestroyed())win.webContents.send('chunk',{id,part});});return{ok:true};}catch(e){return{ok:false,error:e.message};}
  });
  handle('stop',()=>{active?.destroy(Error('Ответ остановлен.'));});
  handle('attach',async()=>{
    const selected=await dialog.showOpenDialog(win,{title:'Добавить текст или фотографию',properties:['openFile','multiSelections'],filters:[{name:'Текст и изображения',extensions:['txt','md','csv','json','js','py','html','css','log','png','jpg','jpeg','webp']}]});
    if(selected.canceled)return[];
    if(selected.filePaths.length>4)throw Error('Можно добавить до четырёх файлов.');
    return selected.filePaths.map(p=>{const size=fs.statSync(p).size,name=path.basename(p),ext=path.extname(p).toLowerCase();if(size>8*1024*1024)throw Error(name+': размер больше 8 МБ.');if(['.png','.jpg','.jpeg','.webp'].includes(ext)){let im=nativeImage.createFromPath(p);if(im.isEmpty())throw Error('Не удалось прочитать '+name);const {width,height}=im.getSize();if(Math.max(width,height)>1600)im=im.resize(width>height?{width:1600}:{height:1600});return{name,image:im.toJPEG(85).toString('base64')};}if(!['.txt','.md','.csv','.json','.js','.py','.html','.css','.log'].includes(ext)||size>2*1024*1024)throw Error('Формат или размер файла не поддерживается.');return{name,text:fs.readFileSync(p,'utf8').slice(0,100000)};});
  });
  handle('export',async chat=>{const selected=await dialog.showSaveDialog(win,{defaultPath:'Muse Desk — диалог.md',filters:[{name:'Markdown',extensions:['md']}]});if(selected.canceled)return;fs.writeFileSync(selected.filePath,'# '+chat.title+'\n\n'+chat.messages.map(m=>'## '+(m.role==='user'?'Вы':'Muse')+'\n\n'+m.content).join('\n\n'));});
  win.on('closed',()=>{active?.destroy();win=null;});
});
app.on('window-all-closed',()=>app.quit());
