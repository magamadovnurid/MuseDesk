const {test}=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const os=require('node:os');
const path=require('node:path');
const {profileFor,GiB,Store,exclusive,safeProjectPath}=require('../core.cjs');
const tools=require('../tools.cjs');
const temp=()=>fs.realpathSync(fs.mkdtempSync(path.join(os.tmpdir(),'musedesk-test-')));
test('Apple Silicon profiles use unified memory, not Windows VRAM',()=>{
  const h={platform:'darwin',arch:'arm64',major:14,ramBytes:32*GiB,freeBytes:40*GiB};
  assert.equal(profileFor(h),'glimmer-q4-q8');assert.equal(profileFor({...h,ramBytes:36*GiB}),'glimmer-q4-q8');assert.equal(profileFor({...h,ramBytes:64*GiB}),'glimmer-q4-f16');
  for(const ram of [8,16,24])assert.equal(profileFor({...h,ramBytes:ram*GiB}),'app-only');
  assert.equal(profileFor({...h,freeBytes:39*GiB}),'app-only');
  for(const change of [{arch:'x64'},{platform:'win32'},{major:13}])assert.equal(profileFor({...h,...change}),'unsupported');
});
test('Project removal deletes its chats while preserving files and other chats',()=>{
  const root=temp(),folder=path.join(root,'Project');fs.mkdirSync(folder);fs.writeFileSync(path.join(folder,'keep.txt'),'private');const store=new Store(path.join(root,'app'));
  const project=store.addProject(folder);assert.equal(project.name,'Project');assert.equal(store.addProject(folder).id,project.id);
  const chat=store.newChat(project.id),other=store.newChat();store.moveChat(other.id,project.id);store.moveChat(other.id,null);store.removeProject(project.id);
  assert.equal(store.state.chats.length,1);assert.equal(store.state.chats[0].id,other.id);assert.equal(fs.readFileSync(path.join(folder,'keep.txt'),'utf8'),'private');assert.throws(()=>store.chat(chat.id));
  assert.equal(new Store(path.join(root,'app')).state.chats[0].id,other.id);
});
test('Corrupted history recovers the last backup',()=>{const root=temp(),store=new Store(root);store.newChat();store.newChat();fs.writeFileSync(store.file,'broken');const recovered=new Store(root);assert.equal(recovered.recovered,true);assert.equal(recovered.state.chats.length,1);});
test('Only one selected GPU resident can become ready',()=>{
  const row={name:'Muse',size_vram:1024};assert.equal(exclusive([row],'Muse'),true);for(const rows of [[],[row,row],[{...row,name:'Qwen'}],[{...row,size_vram:0}]])assert.equal(exclusive(rows,'Muse'),false);
});
test('Project paths reject traversal, absolute paths and symlink escape',()=>{
  const root=temp();fs.writeFileSync(path.join(root,'inside.txt'),'ok');assert.equal(safeProjectPath(root,'inside.txt'),path.join(root,'inside.txt'));assert.throws(()=>safeProjectPath(root,'../outside',true));assert.throws(()=>safeProjectPath(root,path.resolve(root,'inside.txt')));
  if(process.platform!=='win32'){const outside=temp();fs.symlinkSync(outside,path.join(root,'link'));assert.throws(()=>safeProjectPath(root,'link/file.txt',true));}
});
test('File tools require approval, record results, and bind to the selected project',async()=>{
  const root=temp(),project={path:root};const call={function:{name:'write_text_file',arguments:{path:'notes.txt',content:'Привет'}}};
  const denied=await tools.execute(call,{project,approve:async()=>false});assert.match(denied.text,/отклонил/);assert.equal(fs.existsSync(path.join(root,'notes.txt')),false);
  const result=await tools.execute(call,{project,approve:async()=>true});assert.equal(result.result,'notes.txt');assert.equal(fs.readFileSync(path.join(root,'notes.txt'),'utf8'),'Привет');
  await assert.rejects(()=>tools.execute(call,{approve:async()=>true}),/проект/);
});
test('Context trimming keeps complete turns and refuses an oversized latest prompt',()=>{
  const settings={context:8192};const messages=[];for(let i=0;i<8;i++)messages.push({role:'user',content:'q'+i},{role:'assistant',content:'a'.repeat(8000)});messages.push({role:'user',content:'latest'});
  const wire=tools.messagesFor({messages},settings);assert.equal(wire[0].role,'system');assert.equal(wire[1].role,'user');assert.equal(wire.at(-1).content,'latest');assert.ok(wire.length<messages.length);
  assert.throws(()=>tools.messagesFor({messages:[{role:'user',content:'x'.repeat(30000)}]},settings),/велико/);
  const image='A'.repeat(2000000);const vision=tools.messagesFor({messages:[{role:'user',content:'Опиши изображение',images:[image]}]},settings);assert.equal(vision.at(-1).images[0],image);assert.ok(tools.wireCost(vision)<10000);
});
test('Approved macOS commands preserve Unicode and stop on cancellation',{skip:process.platform!=='darwin'},async()=>{
  const project={path:temp()};const result=await tools.execute({function:{name:'run_command',arguments:{command:"printf 'Привет\\n'"}}},{project,approve:async()=>true});assert.match(result.text,/Привет/);
  const controller=new AbortController();const running=tools.execute({function:{name:'run_command',arguments:{command:'sleep 20'}}},{project,approve:async()=>true,signal:controller.signal});setTimeout(()=>controller.abort(),100);await assert.rejects(()=>running,/остановлена/);
});
