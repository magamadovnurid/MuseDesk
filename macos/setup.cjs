'use strict';
const fs=require('node:fs');
const fsp=fs.promises;
const path=require('node:path');
const os=require('node:os');
const {spawn,execFileSync}=require('node:child_process');
const {createHash}=require('node:crypto');
const {profileFor,atomicJSON,GiB}=require('./core.cjs');
const catalog=require('./catalog.json');
const abort=signal=>{if(signal?.aborted)throw Error('Установка остановлена. Скачанные части сохранены.');};
async function hashFile(file){const hash=createHash('sha256');for await(const chunk of fs.createReadStream(file))hash.update(chunk);return hash.digest('hex');}
function run(executable,args,{signal,cwd,env}={}){
  return new Promise((resolve,reject)=>{abort(signal);const child=spawn(executable,args,{cwd,env,windowsHide:true,stdio:['ignore','pipe','pipe']});let out='',err='';
    const stop=()=>child.kill('SIGTERM');signal?.addEventListener('abort',stop,{once:true});
    child.stdout.on('data',c=>{if(out.length<1024*1024)out+=c;});child.stderr.on('data',c=>{if(err.length<1024*1024)err+=c;});
    child.on('error',reject);child.on('close',code=>{signal?.removeEventListener('abort',stop);if(signal?.aborted)reject(Error('Установка остановлена. Скачанные части сохранены.'));else if(code!==0)reject(Error(err||`Команда завершилась с кодом ${code}`));else resolve(out);});
  });
}
async function download(spec,target,{signal,onProgress=()=>{},runner=run}={}){
  abort(signal);await fsp.mkdir(path.dirname(target),{recursive:true});
  if(fs.existsSync(target)){onProgress({stage:'verify',message:'Проверяем сохранённый файл',percent:-1});if(fs.statSync(target).size===spec.bytes&&await hashFile(target)===spec.sha256)return;throw Error('Контрольная сумма сохранённого файла не совпала: '+target);}
  const part=target+'.part';let bytes=fs.existsSync(part)?fs.statSync(part).size:0;
  if(bytes>spec.bytes)throw Error('Повреждён частичный файл: '+part);
  const timer=setInterval(()=>{try{bytes=fs.statSync(part).size;onProgress({stage:'download',message:`${(bytes/GiB).toFixed(2)} / ${(spec.bytes/GiB).toFixed(2)} ГБ`,percent:Math.min(99,100*bytes/spec.bytes)});}catch{}},500);
  try{if(bytes<spec.bytes)await runner('/usr/bin/curl',['--fail','--location','--proto','=https','--proto-redir','=https','--retry','3','--connect-timeout','30','--speed-limit','1024','--speed-time','120','--silent','--show-error','--continue-at','-','--output',part,spec.url],{signal});
    abort(signal);onProgress({stage:'verify',message:'Проверяем SHA-256. Это может занять несколько минут.',percent:-1});
    if(fs.statSync(part).size!==spec.bytes||await hashFile(part)!==spec.sha256)throw Error('Файл не прошёл проверку SHA-256: '+part);
    abort(signal);await fsp.rename(part,target);
  }finally{clearInterval(timer);}
}
function probe(root){
  fs.mkdirSync(root,{recursive:true,mode:0o700});let major=0,chip='Apple Silicon',osVersion=os.release();
  if(process.platform==='darwin'){osVersion=execFileSync('/usr/bin/sw_vers',['-productVersion'],{encoding:'utf8'}).trim();major=Number(osVersion.split('.')[0]);try{chip=execFileSync('/usr/sbin/sysctl',['-n','machdep.cpu.brand_string'],{encoding:'utf8'}).trim();}catch{}}
  const disk=fs.statfsSync(root);const freeBytes=disk.bavail*disk.bsize;
  const hardware={platform:process.platform,arch:process.arch,major,ramBytes:os.totalmem(),freeBytes,chip,osVersion};
  // Already allocated verified/partial components count toward the original disk budget on resume.
  let allocated=0;for(const spec of [catalog.engine,...catalog.files]){const p=spec===catalog.engine?path.join(root,'downloads','ollama.tgz'):path.join(root,'models','blobs','sha256-'+spec.sha256);for(const candidate of [p,p+'.part']){try{allocated+=Math.min(fs.statSync(candidate).size,spec.bytes);}catch{}}}
  return {...hardware,profile:profileFor({...hardware,freeBytes:freeBytes+allocated})};
}
function blob(root,text){const bytes=Buffer.from(text),hash=createHash('sha256').update(bytes).digest('hex');fs.mkdirSync(root,{recursive:true});fs.writeFileSync(path.join(root,'sha256-'+hash),bytes);return {digest:'sha256:'+hash,size:bytes.length};}
function register(root,profile){
  const blobs=path.join(root,'models','blobs'),model=catalog.files[0],projector=catalog.files[profile==='glimmer-q4-f16'?2:1];
  const config=blob(blobs,JSON.stringify({model_format:'gguf',model_family:'muse-glimmer',model_families:['muse-glimmer'],model_type:'27.9B',file_type:'Q4_K_M',renderer:'glimmer',parser:'glimmer',requires:'0.32.8',architecture:'arm64',os:'darwin'}));
  const params=blob(blobs,JSON.stringify({temperature:1,top_k:64,top_p:0.95,num_ctx:8192}));
  const manifest={schemaVersion:2,mediaType:'application/vnd.docker.distribution.manifest.v2+json',config:{mediaType:'application/vnd.docker.container.image.v1+json',...config},layers:[{mediaType:'application/vnd.ollama.image.model',digest:'sha256:'+model.sha256,size:model.bytes},{mediaType:'application/vnd.ollama.image.projector',digest:'sha256:'+projector.sha256,size:projector.bytes},{mediaType:'application/vnd.ollama.image.params',...params}]};
  atomicJSON(path.join(root,'models','manifests','registry.ollama.ai','acc100','muse-glimmer-heretic','latest'),manifest);
}
function validateArchiveEntries(entries){for(const name of entries){if(!name.trim())continue;if(path.posix.isAbsolute(name)||name.split('/').includes('..'))throw Error('Недопустимый путь в архиве движка');}}
function findEngine(root){const start=path.join(root,'runtime',catalog.engine.version);if(!fs.existsSync(start))return null;const queue=[start];while(queue.length){const dir=queue.shift();for(const file of fs.readdirSync(dir,{withFileTypes:true})){const p=path.join(dir,file.name);if(file.isDirectory())queue.push(p);else if(file.name==='ollama')return p;}}return null;}
async function install(root,{signal,onProgress=()=>{},engineOnly=false}={}){
  const hardware=probe(root);if(hardware.profile==='unsupported')throw Error('Нужны macOS 14+ и Apple Silicon. Запуск через Rosetta не поддерживается.');
  if(!engineOnly&&hardware.profile==='app-only')throw Error('Для Glimmer нужны 32 ГБ объединённой памяти и 40 ГБ свободного места.');
  if(engineOnly&&hardware.freeBytes<2*GiB)throw Error('Для движка нужно 2 ГБ свободного места.');
  const report=s=>{onProgress(s);fs.appendFileSync(path.join(root,'installation.log'),JSON.stringify({...s,utc:new Date().toISOString()})+'\n');};
  const archive=path.join(root,'downloads','ollama.tgz');report({stage:'engine',message:'Скачиваем движок для Apple Silicon',percent:-1});
  await download(catalog.engine,archive,{signal,onProgress:report});
  const runtime=path.join(root,'runtime',catalog.engine.version);await fsp.mkdir(runtime,{recursive:true});
  const entries=await run('/usr/bin/tar',['-tzf',archive],{signal});validateArchiveEntries(entries.split('\n'));
  report({stage:'extract',message:'Подготавливаем локальный движок',percent:-1});await run('/usr/bin/tar',['-xzf',archive,'-C',runtime],{signal});
  const exe=findEngine(root);if(!exe)throw Error('Архив не содержит движок Ollama');await fsp.chmod(exe,0o755);
  if(!engineOnly){for(const spec of [catalog.files[0],catalog.files[hardware.profile==='glimmer-q4-f16'?2:1]]){report({stage:'model',message:spec.file,percent:-1});await download(spec,path.join(root,'models','blobs','sha256-'+spec.sha256),{signal,onProgress:report});}abort(signal);register(root,hardware.profile);}
  atomicJSON(path.join(root,'installation.json'),{status:'complete',profile:engineOnly?'engine-only':hardware.profile,source:catalog.source,revision:catalog.revision,installedAt:new Date().toISOString()});
  report({stage:'complete',message:engineOnly?'Движок установлен. Добавьте совместимую модель.':'Muse Glimmer установлена. Подготавливаем модель…',percent:100});return hardware;
}
module.exports={probe,download,hashFile,run,register,validateArchiveEntries,findEngine,install,catalog};
