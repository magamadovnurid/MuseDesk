const {test}=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const os=require('node:os');
const path=require('node:path');
const {GiB,profileFor,compatibilityFor}=require('../core.cjs');
const {install,catalog,probe}=require('../setup.cjs');
const {Engine}=require('../engine.cjs');
const base={platform:'darwin',arch:'arm64',major:14,ramBytes:32*GiB,freeBytes:80*GiB};
const temp=()=>fs.mkdtempSync(path.join(os.tmpdir(),'muse-hardware-'));

test('Unknown, malformed or nonphysical memory cannot select Glimmer',()=>{
  for(const value of [undefined,null,NaN,Infinity,-1,0,'34359738368']){
    assert.equal(profileFor({...base,ramBytes:value}),'unverified');
    assert.equal(compatibilityFor({...base,ramBytes:value}).eligible,false);
  }
  for(const field of ['major','freeBytes'])assert.equal(profileFor({...base,[field]:NaN}),'unverified');
  for(const ram of [8,16,24])assert.equal(compatibilityFor({...base,ramBytes:ram*GiB}).eligible,false);
});

test('Unsupported and small Macs stop installation before any component or model download',async()=>{
  for(const h of [8,16,24].map(ram=>({...base,ramBytes:ram*GiB})).concat([{...base,ramBytes:NaN},{...base,arch:'x64'},{...base,major:13},{...base,freeBytes:39*GiB}])){
    const root=temp();let network=0;
    await assert.rejects(()=>install(root,{}, {probe:()=>({...h,profile:'glimmer-q4-f16'}),checkEngine:async()=>{network++;},download:async()=>{network++;}}));
    assert.equal(network,0);
    const audit=JSON.parse(fs.readFileSync(path.join(root,'hardware-check.json')));
    assert.equal(audit.compatibility.eligible,false);
    assert.equal(fs.existsSync(path.join(root,'installation.json')),false);
  }
});

function services(root,scan,downloads){
  const exe=path.join(root,'fixture-engine');fs.writeFileSync(exe,'synthetic engine; never executed');
  return {probe:scan,checkEngine:async()=>({message:'fixture'}),download:async spec=>downloads.push(spec.sha256),run:async()=>'',findEngine:()=>exe};
}
test('Installer rescans hardware after preparing the engine and before downloading weights',async()=>{
  const root=temp(),downloads=[];let scans=0;
  await assert.rejects(()=>install(root,{},services(root,()=>({...base,ramBytes:(++scans===1?32:16)*GiB}),downloads)),/32/);
  assert.equal(scans,2);assert.deepEqual(downloads,[catalog.engine.sha256]);
  assert.equal(fs.existsSync(path.join(root,'models/manifests')),false);
});
test('Validated 32 and 64 GB Macs select only their compatible model files',async()=>{
  for(const ram of [32,64]){
    const root=temp(),downloads=[];
    await install(root,{},services(root,()=>({...base,ramBytes:ram*GiB}),downloads));
    assert.deepEqual(downloads,[catalog.engine.sha256,catalog.files[0].sha256,catalog.files[ram===64?2:1].sha256]);
    assert.equal(JSON.parse(fs.readFileSync(path.join(root,'installation.json'))).status,'complete');
  }
});
test('An already installed Glimmer cannot preload on a small Mac',async()=>{
  let calls=0;const engine=new Engine(temp(),()=>{},{probe:()=>({...base,ramBytes:16*GiB}),fetch:async()=>{calls++;throw Error('Network must not start');}});
  await assert.rejects(()=>engine.prepare('acc100/muse-glimmer-heretic:latest'),/32/);
  assert.equal(calls,0);assert.equal(engine.preparing,false);assert.equal(engine.selected,null);
});
test('Actual Mac probe reads physical memory consistently',{skip:process.platform!=='darwin'},()=>{
  const h=probe(temp());assert.equal(h.ramBytes,os.totalmem());assert.ok(Number.isSafeInteger(h.ramBytes));
  assert.equal(h.compatibility.eligible,h.ramBytes>=32*GiB&&h.budgetFreeBytes>=40*GiB&&process.arch==='arm64'&&h.major>=14);
});
