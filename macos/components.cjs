'use strict';
// Only releases validated together with the bundled application may replace its runtime.
async function checkEngine(spec,{fetcher=fetch,signal}={}){
  try{
    const response=await fetcher('https://api.github.com/repos/ollama/ollama/releases/latest',{signal:AbortSignal.any([AbortSignal.timeout(10000),...(signal?[signal]:[])]),headers:{Accept:'application/vnd.github+json','User-Agent':'MuseDesk-Setup'}});
    if(!response.ok)throw Error('HTTP '+response.status);
    const release=await response.json();
    if(release.draft||release.prerelease||!/^v\d+\.\d+\.\d+$/.test(release.tag_name))throw Error('Invalid release metadata');
    const current=release.tag_name.slice(1),asset=release.assets?.find(a=>a.name===spec.url.split('/').pop());
    if(current===spec.version&&(!asset||asset.size!==spec.bytes||asset.digest!=='sha256:'+spec.sha256))throw Error('Release checksum differs from validated catalog');
    return {latest:current,selected:spec.version,status:current===spec.version?'current':'validated',message:current===spec.version?'Устанавливаем актуальный проверенный движок '+current:'Доступен движок '+current+'. Для этой версии Muse Desk устанавливаем проверенный '+spec.version+'.'};
  }catch(error){if(signal?.aborted)throw error;return {selected:spec.version,status:'offline',message:'Каталог обновлений недоступен. Используем проверенный движок '+spec.version+'.'};}
}
module.exports={checkEngine};
