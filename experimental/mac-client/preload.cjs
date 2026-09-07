const {contextBridge,ipcRenderer}=require('electron');
contextBridge.exposeInMainWorld('muse',Object.freeze({
  load:()=>ipcRenderer.invoke('load'),save:chats=>ipcRenderer.invoke('save',chats),pair:()=>ipcRenderer.invoke('pair'),health:()=>ipcRenderer.invoke('health'),
  send:value=>ipcRenderer.invoke('send',value),stop:()=>ipcRenderer.invoke('stop'),attach:()=>ipcRenderer.invoke('attach'),export:chat=>ipcRenderer.invoke('export',chat),
  onChunk:callback=>{ipcRenderer.on('chunk',(_event,data)=>callback(data));}
}));
