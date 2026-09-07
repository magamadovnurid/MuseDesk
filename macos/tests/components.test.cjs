const {test}=require('node:test');
const assert=require('node:assert/strict');
const {checkEngine}=require('../components.cjs');
const spec={version:'0.33.3',bytes:42,sha256:'a'.repeat(64),url:'https://github.com/ollama/ollama/releases/download/v0.33.3/ollama-darwin.tgz'};
const mock=(version='v0.33.3',digest='sha256:'+spec.sha256)=>async()=>({ok:true,json:async()=>({tag_name:version,assets:[{name:'ollama-darwin.tgz',size:42,digest}]})});
test('Latest engine metadata must match the validated platform asset',async()=>{
 assert.equal((await checkEngine(spec,{fetcher:mock()})).status,'current');
 assert.equal((await checkEngine(spec,{fetcher:mock('v0.33.3','sha256:bad')})).status,'offline');
 const next=await checkEngine(spec,{fetcher:mock('v0.34.0')});assert.equal(next.status,'validated');assert.equal(next.selected,spec.version);
});
test('Unavailable release service keeps the verified fallback; cancellation propagates',async()=>{
 assert.equal((await checkEngine(spec,{fetcher:async()=>{throw Error('offline');}})).selected,spec.version);
 const controller=new AbortController();controller.abort();await assert.rejects(()=>checkEngine(spec,{signal:controller.signal,fetcher:async()=>{throw Error('aborted');}}));
});
