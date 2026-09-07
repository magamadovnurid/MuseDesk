const {test}=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const os=require('node:os');
const path=require('node:path');
const crypto=require('node:crypto');
const {download,hashFile,register,validateArchiveEntries,catalog}=require('../setup.cjs');
test('Downloader resumes a partial file, verifies bytes, and reuses verified cache',async()=>{
  const target=path.join(fs.mkdtempSync(path.join(os.tmpdir(),'muse-download-')),'model');const bytes=Buffer.from('pinned model fixture');fs.writeFileSync(target+'.part',bytes.subarray(0,5));
  const spec={bytes:bytes.length,sha256:crypto.createHash('sha256').update(bytes).digest('hex'),url:'https://example.com/pinned'};let called=0;
  await download(spec,target,{runner:async(exe,args)=>{called++;assert.equal(exe,'/usr/bin/curl');assert.ok(args.includes('--continue-at'));assert.equal(fs.readFileSync(target+'.part').length,5);fs.appendFileSync(target+'.part',bytes.subarray(5));}});
  assert.equal(await hashFile(target),spec.sha256);await download(spec,target,{runner:async()=>{throw Error('Unexpected download');}});assert.equal(called,1);
  fs.writeFileSync(target,'corrupt');await assert.rejects(()=>download(spec,target),/сумма/);
});
test('Cancellation preserves partial downloads and rejects invalid complete bytes',async()=>{
  const target=path.join(fs.mkdtempSync(path.join(os.tmpdir(),'muse-cancel-')),'file'),controller=new AbortController();const spec={bytes:10,sha256:'0'.repeat(64),url:'https://example.com/file'};
  await assert.rejects(()=>download(spec,target,{signal:controller.signal,runner:async()=>{fs.writeFileSync(target+'.part','part');controller.abort();}}),/остановлена/);assert.equal(fs.readFileSync(target+'.part','utf8'),'part');
  await assert.rejects(()=>download(spec,target,{runner:async()=>fs.writeFileSync(target+'.part','0123456789')}),/SHA-256/);assert.equal(fs.existsSync(target),false);
});
test('Archive paths cannot escape the runtime folder',()=>{validateArchiveEntries(['./ollama','./lib/ollama/runner']);for(const entry of ['/tmp/out','../out','lib/../../out'])assert.throws(()=>validateArchiveEntries([entry]));});
test('Registration selects the correct image encoder and arm64 metadata',()=>{
  const root=fs.mkdtempSync(path.join(os.tmpdir(),'muse-manifest-'));register(root,'glimmer-q4-f16');const manifest=JSON.parse(fs.readFileSync(path.join(root,'models/manifests/registry.ollama.ai/acc100/muse-glimmer-heretic/latest')));
  assert.equal(manifest.layers[1].digest,'sha256:'+catalog.files[2].sha256);const config=JSON.parse(fs.readFileSync(path.join(root,'models/blobs',manifest.config.digest.replace(':','-'))));assert.equal(config.architecture,'arm64');assert.equal(config.os,'darwin');assert.equal(config.renderer,'glimmer');
});
