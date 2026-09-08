const {test}=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs'),os=require('node:os'),path=require('node:path');
const {probeLinux,parseGPUs}=require('../linux-platform.cjs');
const {GiB,compatibilityFor}=require('../core.cjs');
const {install,register,probe}=require('../setup.cjs');
const gpu={uuid:'GPU-1234-abcd',name:'Fixture GPU',vramBytes:24*GiB,freeVramBytes:23*GiB,driverMajor:550,compute:8.6};
const base={platform:'linux',distro:'ubuntu',arch:'x64',major:24,ramBytes:31*GiB,freeBytes:60*GiB,gpu};
const temp=()=>fs.mkdtempSync(path.join(os.tmpdir(),'muse-linux-'));
test('Ubuntu gate distinguishes RAM, one GPU VRAM, architecture, driver and disk',()=>{
  assert.equal(compatibilityFor(base).eligible,true);
  for(const delta of [{ramBytes:16*GiB},{ramBytes:NaN},{freeBytes:39*GiB},{arch:'arm64'},{distro:'debian'},{major:20},{gpu:null},{gpu:{...gpu,vramBytes:16*GiB}},{gpu:{...gpu,compute:6.1}},{gpu:{...gpu,driverMajor:535}}])assert.equal(compatibilityFor({...base,...delta}).eligible,false,JSON.stringify(delta));
  assert.equal(compatibilityFor({...base,freeBytes:0},{loading:true}).eligible,true);
});
test('Unsupported Ubuntu hardware cannot download any components or weights',async()=>{
  for(const delta of [{gpu:null},{ramBytes:16*GiB},{gpu:{...gpu,vramBytes:12*GiB}}]){
    let calls=0;await assert.rejects(()=>install(temp(),{}, {probe:()=>({...base,...delta}),checkEngine:async()=>{calls++;},download:async()=>{calls++;}}));assert.equal(calls,0);
  }
});
test('Linux probe parses OS without executing it, and never sums GPU memory',()=>{
  const h=probeLinux({read:()=> 'ID=ubuntu\nVERSION_ID="24.04"\nPRETTY_NAME="Ubuntu 24.04 LTS"',execute:()=> 'GPU-abcd, Small, 12288, 11000, 550.1, 8.6\nGPU-1234, Large, 24576, 22000, 570.1, 8.9',memory:()=>31*GiB,cpus:()=>[{model:'Test CPU'}]});
  assert.equal(h.gpu.uuid,'GPU-1234');assert.equal(h.gpu.vramBytes,24*GiB);assert.equal(h.major,24);
  assert.deepEqual(parseGPUs('N/A, unavailable, N/A, N/A, N/A, N/A'),[]);
});
test('Ubuntu manifests identify Linux x64 and the shared Glimmer renderer',()=>{
  const root=temp();register(root,'glimmer-q4-q8',base);const manifest=JSON.parse(fs.readFileSync(path.join(root,'models/manifests/registry.ollama.ai/acc100/muse-glimmer-heretic/latest')));const config=JSON.parse(fs.readFileSync(path.join(root,'models/blobs',manifest.config.digest.replace(':','-'))));assert.equal(config.architecture,'amd64');assert.equal(config.os,'linux');assert.equal(config.renderer,'glimmer');
});
test('Actual Ubuntu probe records physical RAM and blocks CI without GPU',{skip:process.platform!=='linux'},()=>{
  const h=probe(temp());assert.equal(h.platform,'linux');assert.equal(h.ramBytes,os.totalmem());assert.ok(h.osVersion.includes('Ubuntu'));if(!h.gpu)assert.equal(h.compatibility.eligible,false);
});
