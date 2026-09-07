const {test}=require('node:test');
const assert=require('node:assert/strict');
const {Engine}=require('../engine.cjs');
test('Switching models waits for empty memory before preload and verifies residency',async()=>{
  let resident=[{name:'Old',size_vram:100}],calls=[],states=[];
  const engine=new Engine('/unused',s=>states.push(s.state),{test:true,fetch:async(url,options)=>{const route=new URL(url).pathname,body=options.body&&JSON.parse(options.body);calls.push({route,body});let data={};if(route==='/api/tags')data={models:[{name:'Muse'}]};if(route==='/api/ps')data={models:resident};if(route==='/api/generate')resident=body.keep_alive===0?[]:[{name:body.model,size_vram:100}];return new Response(JSON.stringify(data));}});
  await engine.prepare('Muse');assert.equal(engine.selected,'Muse');assert.equal(states.at(-1),'ready');const unload=calls.findIndex(c=>c.body?.keep_alive===0),load=calls.findIndex(c=>c.body?.keep_alive===-1);assert.ok(load>unload);assert.ok(calls.slice(unload+1,load).some(c=>c.route==='/api/ps'));await engine.assertReady('Muse');
  resident.push({name:'Other',size_vram:100});await assert.rejects(()=>engine.assertReady('Muse'));assert.equal(engine.selected,null);
});
test('Stream decoding handles split UTF-8 and requires the done marker',async()=>{
  const bytes=Buffer.from(JSON.stringify({message:{content:'Привет'}})+'\n'+JSON.stringify({done:true})+'\n');let content='';const engine=new Engine('/unused',()=>{},{test:true,fetch:async()=>new Response(new ReadableStream({start(controller){for(const byte of bytes)controller.enqueue(Uint8Array.of(byte));controller.close();}}))});
  await engine.api('/api/chat',{}, {onChunk:p=>content+=p.message?.content||''});assert.equal(content,'Привет');engine.fetch=async()=>new Response('{"message":{"content":"unfinished"}}\n');await assert.rejects(()=>engine.api('/api/chat',{}, {onChunk:()=>{}}),/прервался/);
});
