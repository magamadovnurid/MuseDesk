'use strict';
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const setup=require('../macos/setup.cjs');
const {Engine}=require('../macos/engine.cjs');
(async()=>{
  assert.equal(process.platform,'darwin');assert.equal(process.arch,'arm64');
  const root=path.resolve('.build/mac-runtime');fs.mkdirSync(root,{recursive:true});
  await setup.install(root,{engineOnly:true,onProgress:p=>{if(p.stage!=='download')console.log(p.stage+': '+p.message);}});
  const engine=new Engine(root);
  try{await engine.start();const version=await engine.api('/api/version');assert.equal(version.version,setup.catalog.engine.version);assert.deepEqual((await engine.api('/api/ps')).models,[]);console.log('ARM64 RUNTIME SMOKE PASSED: pinned engine started, no models loaded');}
  finally{await engine.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
