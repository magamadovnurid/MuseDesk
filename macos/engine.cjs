'use strict';
const fs=require('node:fs');
const path=require('node:path');
const {spawn}=require('node:child_process');
const {exclusive,requireGlimmer}=require('./core.cjs');
const {findEngine,probe}=require('./setup.cjs');
const sleep=ms=>new Promise(resolve=>setTimeout(resolve,ms));
class Engine{
  constructor(root,status=()=>{},options={}){this.root=root;this.status=status;this.base=options.base||'http://127.0.0.1:11436';this.fetch=options.fetch||globalThis.fetch;this.child=null;this.preparing=false;this.selected=null;this.test=options.test===true;this.probe=options.probe||probe;this.closed=false;}
  async api(route,body,{signal,onChunk,timeout=120000}={}){
    const combined=AbortSignal.any([AbortSignal.timeout(timeout),...(signal?[signal]:[])]);
    const response=await this.fetch(this.base+route,{method:body?'POST':'GET',headers:body?{'Content-Type':'application/json'}:{},body:body?JSON.stringify(body):undefined,signal:combined});
    if(!response.ok)throw Error(`Ollama ${response.status}: ${(await response.text()).slice(0,1800)}`);
    if(!onChunk)return response.json();
    let buffer='',done=false;const decoder=new TextDecoder();
    const parse=line=>{if(!line.trim())return;const part=JSON.parse(line);if(part.error)throw Error(String(part.error));done ||= part.done===true;onChunk(part);};
    for await(const chunk of response.body){buffer+=decoder.decode(chunk,{stream:true});if(buffer.length>8*1024*1024)throw Error('Слишком большой фрагмент ответа');let at;while((at=buffer.indexOf('\n'))>=0){parse(buffer.slice(0,at));buffer=buffer.slice(at+1);}}
    buffer+=decoder.decode();parse(buffer);if(!done)throw Error('Ответ прервался до завершения');
  }
  async start(){if(!this.starting)this.starting=this.startOwned().finally(()=>{this.starting=null;});return this.starting;}
  async startOwned(){
    if(this.test)return;
    if(this.closed)throw Error('Движок остановлен');
    if(this.child&&this.child.exitCode===null){await this.api('/api/version',null,{timeout:2000});return;}
    let occupied=false;try{await this.api('/api/version',null,{timeout:1000});occupied=true;}catch{}
    if(occupied)throw Error('Порт 11436 занят другим движком. Закройте другую копию Muse Desk и повторите.');
    if(this.closed)throw Error('Движок остановлен');
    const executable=findEngine(this.root);if(!executable)throw Error('Сначала установите локальный движок');
    const log=fs.openSync(path.join(this.root,'engine.log'),'a',0o600);
    this.child=spawn(executable,['serve'],{cwd:path.dirname(executable),env:{...process.env,OLLAMA_HOST:'127.0.0.1:11436',OLLAMA_MODELS:path.join(this.root,'models'),OLLAMA_MAX_LOADED_MODELS:'1',OLLAMA_NUM_PARALLEL:'1',OLLAMA_NO_CLOUD:'1',OLLAMA_KEEP_ALIVE:'-1'},stdio:['ignore',log,log],windowsHide:true});
    fs.closeSync(log);let startupError=null;this.child.on('error',e=>{startupError=e;});
    this.child.on('exit',()=>{this.selected=null;if(!this.closed)this.status({state:'stopped',message:'Движок остановлен. Повторите загрузку.'});});
    for(let i=0;i<60;i++){if(startupError)throw startupError;if(this.child.exitCode!==null)throw Error('Движок завершился. Подробности в engine.log');try{await this.api('/api/version',null,{timeout:1000});return;}catch{}await sleep(500);}
    this.child.kill();throw Error('Движок не ответил за 30 секунд');
  }
  async prepare(model,context=8192){
    if(this.preparing)throw Error('Дождитесь завершения загрузки');this.preparing=true;this.selected=null;
    try{if(!this.test&&/muse-glimmer/i.test(model))requireGlimmer(this.probe(this.root),{loading:true});this.status({state:'loading',message:'Подключаем движок'});await this.start();
      const tags=(await this.api('/api/tags')).models||[];
      if(!tags.some(row=>row.name===model||row.model===model))throw Error('Модель не установлена. Откройте настройку Glimmer.');
      this.status({state:'loading',message:'Выгружаем предыдущие модели'});
      for(const row of (await this.api('/api/ps')).models||[])await this.api('/api/generate',{model:row.name||row.model,keep_alive:0,stream:false});
      let empty=false;for(let i=0;i<100;i++){if(!((await this.api('/api/ps')).models||[]).length){empty=true;break;}await sleep(200);}
      if(!empty)throw Error('Предыдущая модель не выгрузилась. Новая загрузка заблокирована.');
      this.status({state:'loading',message:'Загружаем '+model});
      await this.api('/api/generate',{model,keep_alive:-1,stream:false,options:{num_ctx:context}},{timeout:600000});
      if(!exclusive((await this.api('/api/ps')).models||[],model))throw Error('Не подтверждена единственная модель в памяти GPU');
      this.selected=model;this.status({state:'ready',message:'Готово',model});
    }catch(error){this.status({state:'stopped',message:error.message});throw error;}finally{this.preparing=false;}
  }
  async assertReady(model){if(this.preparing||this.selected!==model||!exclusive((await this.api('/api/ps')).models||[],model)){this.selected=null;this.status({state:'stopped',message:'Состояние модели изменилось. Повторите загрузку.'});throw Error('Модель пока не готова');}}
  async models(){await this.start();return ((await this.api('/api/tags')).models||[]).map(m=>m.name);}
  async close(){this.closed=true;this.selected=null;if(this.child&&this.child.exitCode===null){try{for(const row of (await this.api('/api/ps',null,{timeout:1000})).models||[])await this.api('/api/generate',{model:row.name,keep_alive:0,stream:false},{timeout:2000});}catch{}this.child.kill('SIGTERM');}this.child=null;}
}
module.exports={Engine};
