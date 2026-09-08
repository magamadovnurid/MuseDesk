'use strict';
const fs=require('node:fs');
const os=require('node:os');
const {execFileSync}=require('node:child_process');
const GiB=1024**3;
function parseRelease(text){return Object.fromEntries(text.split('\n').map(line=>line.match(/^([A-Z_]+)=["']?([^"']*)["']?$/)).filter(Boolean).map(m=>[m[1],m[2]]));}
function parseGPUs(text){return text.trim().split('\n').map(line=>{
  const [uuid,name,total,free,driver,compute]=line.split(',').map(s=>s.trim());
  return {uuid,name,vramBytes:Number(total)*1024**2,freeVramBytes:Number(free)*1024**2,driverMajor:Number(driver?.split('.')[0]),compute:Number(compute)};
}).filter(g=>/^GPU-[a-f\d-]+$/i.test(g.uuid)&&Number.isSafeInteger(g.vramBytes)&&g.vramBytes>0&&Number.isFinite(g.compute)&&Number.isInteger(g.driverMajor));}
function compatibleGPU(g){return g&&g.vramBytes>=23*GiB&&g.compute>=7&&g.driverMajor>=550;}
function probeLinux({read=fs.readFileSync,execute=execFileSync,memory=os.totalmem,cpus=os.cpus}={}){
  let release={},gpus=[];try{release=parseRelease(read('/etc/os-release','utf8'));}catch{}
  try{gpus=parseGPUs(execute('/usr/bin/nvidia-smi',['--query-gpu=uuid,name,memory.total,memory.free,driver_version,compute_cap','--format=csv,noheader,nounits'],{encoding:'utf8',timeout:5000}));}catch{}
  const gpu=gpus.filter(compatibleGPU).sort((a,b)=>b.vramBytes-a.vramBytes)[0]||gpus[0]||null;
  return {distro:release.ID,major:Number(release.VERSION_ID?.split('.')[0]),osVersion:release.PRETTY_NAME||'Linux (версия не определена)',ramBytes:memory(),chip:cpus()[0]?.model||'Процессор не определён',memorySource:'os.totalmem (physical memory available to the Linux kernel)',gpu,gpus};
}
function assessLinux(h,{loading=false}={}){
  const free=loading?40*GiB:(h.budgetFreeBytes??h.freeBytes),reasons=[];
  let profile='glimmer-q4-q8';
  if(h.arch!=='x64'||h.distro!=='ubuntu'||!Number.isInteger(h.major)||h.major<22){profile='unsupported';reasons.push('Нужна Ubuntu 22.04 или новее, x86-64.');}
  else if(!Number.isSafeInteger(h.ramBytes)||h.ramBytes<=0||!Number.isSafeInteger(free)||free<0){profile='unverified';reasons.push('Не удалось проверить физическую память или свободное место. Загрузка заблокирована.');}
  else {
    // Firmware reserves a portion of installed RAM/VRAM; do not round a 16 GB machine up to 32 GB.
    if(h.ramBytes<30*GiB)reasons.push('Для Glimmer нужны 32 ГБ RAM (не менее 30 ГиБ доступно ядру Linux).');
    if(free<40*GiB)reasons.push('Для установки нужно 40 ГиБ свободного места с учётом скачанных компонентов.');
    if(!compatibleGPU(h.gpu))reasons.push('Нужна NVIDIA с 24 ГБ VRAM (не менее 23 ГиБ), Compute Capability 7.0+ и драйвером 550+. Проверьте «Программы и обновления → Дополнительные драйверы». AMD, Intel и CPU для автоматической установки Glimmer пока не поддерживаются.');
    if(reasons.length)profile='app-only';
  }
  return {profile,eligible:profile==='glimmer-q4-q8',appSupported:profile!=='unsupported'&&profile!=='unverified',reasons};
}
module.exports={parseRelease,parseGPUs,compatibleGPU,probeLinux,assessLinux};
