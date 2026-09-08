'use strict';
const fs=require('node:fs');
const path=require('node:path');
const crypto=require('node:crypto');
const GiB=1024**3;
function profileFor(h) {
  if(h.platform==='linux')return require('./linux-platform.cjs').assessLinux(h).profile;
  const {platform,arch,major,ramBytes,freeBytes}=h;
  if(platform!=='darwin'||arch!=='arm64')return 'unsupported';
  if(!Number.isSafeInteger(major)||major<=0||!Number.isSafeInteger(ramBytes)||ramBytes<=0||!Number.isSafeInteger(freeBytes)||freeBytes<0)return 'unverified';
  if(major<14)return 'unsupported';
  if(ramBytes<32*GiB||freeBytes<40*GiB)return 'app-only';
  return ramBytes>=64*GiB?'glimmer-q4-f16':'glimmer-q4-q8';
}
function compatibilityFor(h,{loading=false}={}){
  if(h.platform==='linux')return require('./linux-platform.cjs').assessLinux(h,{loading});
  const profile=profileFor({...h,freeBytes:loading?40*GiB:(h.budgetFreeBytes??h.freeBytes)});
  const reasons=[];
  if(profile==='unsupported')reasons.push('Для приложения нужны Apple Silicon, macOS 14+ и запуск без Rosetta.');
  else if(profile==='unverified')reasons.push('Не удалось достоверно определить параметры Mac. Загрузка Glimmer заблокирована. Повторите проверку системы.');
  else {
    if(h.ramBytes<32*GiB)reasons.push(`Установлено ${(h.ramBytes/GiB).toFixed(0)} ГБ объединённой памяти. Для Muse Glimmer нужно не менее 32 ГБ. Место на SSD не заменяет память.`);
    if(!loading&&(h.budgetFreeBytes??h.freeBytes)<40*GiB)reasons.push('Недостаточно места для установки: нужно 40 ГиБ с учётом уже скачанных компонентов.');
  }
  return {profile,appSupported:profile!=='unsupported'&&profile!=='unverified',eligible:profile==='glimmer-q4-q8'||profile==='glimmer-q4-f16',reasons};
}
function requireGlimmer(h,options){const assessment=compatibilityFor(h,options);if(!assessment.eligible)throw Error(assessment.reasons.join(' '));return assessment;}
function atomicJSON(file,data){
  fs.mkdirSync(path.dirname(file),{recursive:true,mode:0o700});
  const temp=file+'.tmp';const fd=fs.openSync(temp,'w',0o600);
  try{fs.writeFileSync(fd,JSON.stringify(data,null,2));fs.fsyncSync(fd);}finally{fs.closeSync(fd);}
  fs.renameSync(temp,file);
}
function freshState(){return {schema:1,projects:[],chats:[],activeId:null,settings:{model:'acc100/muse-glimmer-heretic:latest',context:8192,think:true,tools:false}};}
class Store{
  constructor(root){this.file=path.join(root,'history.json');this.recovered=false;this.state=freshState();
    for(const candidate of [this.file,this.file+'.bak']){try{const value=JSON.parse(fs.readFileSync(candidate,'utf8'));if(!Array.isArray(value.chats)||!Array.isArray(value.projects))throw Error('Bad history');this.state={...freshState(),...value,settings:{...freshState().settings,...value.settings}};this.recovered=candidate.endsWith('.bak');break;}catch(error){if(error.code!=='ENOENT'&&candidate===this.file)this.recovered=true;}}
  }
  save(){if(fs.existsSync(this.file)&&!this.recovered)fs.copyFileSync(this.file,this.file+'.bak');atomicJSON(this.file,this.state);this.recovered=false;}
  chat(id){const chat=this.state.chats.find(c=>c.id===id);if(!chat)throw Error('Чат не найден');return chat;}
  addProject(folder){const canonical=fs.realpathSync(folder);if(!fs.statSync(canonical).isDirectory())throw Error('Выберите папку');let p=this.state.projects.find(p=>p.path===canonical);if(!p){p={id:crypto.randomUUID(),name:path.basename(canonical)||canonical,path:canonical,collapsed:false};this.state.projects.push(p);this.save();}return p;}
  newChat(projectId=null){if(projectId&&!this.state.projects.some(p=>p.id===projectId))throw Error('Проект не найден');const chat={id:crypto.randomUUID(),projectId,title:'Новый диалог',messages:[],updatedAt:new Date().toISOString()};this.state.chats.unshift(chat);this.state.activeId=chat.id;this.save();return chat;}
  removeProject(id){this.state.projects=this.state.projects.filter(p=>p.id!==id);this.state.chats=this.state.chats.filter(c=>c.projectId!==id);this.reconcile();}
  removeChat(id){this.state.chats=this.state.chats.filter(c=>c.id!==id);this.reconcile();}
  reconcile(){if(!this.state.chats.some(c=>c.id===this.state.activeId))this.state.activeId=this.state.chats[0]?.id||null;this.save();}
  moveChat(id,projectId){if(projectId&&!this.state.projects.some(p=>p.id===projectId))throw Error('Проект не найден');this.chat(id).projectId=projectId;this.save();}
}
function exclusive(models,selected){return models.length===1&&(models[0].name===selected||models[0].model===selected)&&Number(models[0].size_vram)>0;}
function safeProjectPath(root,relative,write=false){
  if(typeof relative!=='string'||!relative||path.isAbsolute(relative))throw Error('Нужен относительный путь внутри проекта');
  const realRoot=fs.realpathSync(root), target=path.resolve(realRoot,relative);
  const within=p=>p===realRoot||p.startsWith(realRoot+path.sep);
  if(!within(target))throw Error('Путь выходит за пределы проекта');
  let check=target;while(!fs.existsSync(check)){const parent=path.dirname(check);if(parent===check)throw Error('Папка недоступна');check=parent;}
  if(!within(fs.realpathSync(check)))throw Error('Символическая ссылка выходит за пределы проекта');
  if(!write&&!fs.existsSync(target))throw Error('Файл не найден');
  return target;
}
module.exports={GiB,profileFor,compatibilityFor,requireGlimmer,atomicJSON,freshState,Store,exclusive,safeProjectPath};
