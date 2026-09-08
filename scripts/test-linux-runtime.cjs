'use strict';
const assert=require('node:assert/strict');
const path=require('node:path');
const setup=require('../macos/setup.cjs');
const {Engine}=require('../macos/engine.cjs');
(async()=>{
  assert.equal(process.platform,'linux');assert.equal(process.arch,'x64');
  const root=path.resolve('.build/linux-runtime');
  await setup.install(root,{engineOnly:true,onProgress:p=>{if(p.stage!=='download')console.log(p.stage+': '+p.message);}});
  const engine=new Engine(root);
  try{await engine.start();assert.equal((await engine.api('/api/version')).version,setup.catalog.engine.version);assert.deepEqual((await engine.api('/api/ps')).models,[]);console.log('LINUX RUNTIME PASSED: verified engine starts without models');}
  finally{await engine.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
