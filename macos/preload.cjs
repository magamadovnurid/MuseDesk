const {contextBridge,ipcRenderer}=require('electron');
contextBridge.exposeInMainWorld('muse',Object.freeze({
  platform:process.platform,
  call:async(name,value)=>{const result=await ipcRenderer.invoke('muse:call',name,value);if(!result.ok)throw Error(result.error);return result.value;},
  onEvent:callback=>{ipcRenderer.on('muse:event',(_event,event)=>callback(event));}
}));
