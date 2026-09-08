const fs=require('node:fs');
const path=require('node:path');
const assert=require('node:assert/strict');
async function run(win,store,app){
  const output=process.env.MUSE_DESK_SMOKE_OUTPUT||path.resolve(process.cwd(),'../.build/mac-smoke');fs.mkdirSync(output,{recursive:true});
  try{
    const script=code=>win.webContents.executeJavaScript(code);
    for(let i=0;i<100;i++){if(await script('window.museReady===true'))break;await new Promise(r=>setTimeout(r,50));}
    assert.equal(await script('document.getElementById("input").disabled'),false);
    assert.equal(await script('document.querySelectorAll(".message-user").length'),1);
    assert.equal(await script('document.querySelectorAll(".message-assistant").length'),1);
    assert.match(await script('document.getElementById("tree").textContent'),/Muse Studio/);
    assert.equal(await script('document.documentElement.scrollWidth <= innerWidth'),true);
    await script('Promise.all([...document.images].map(img=>img.decode())).then(()=>new Promise(r=>requestAnimationFrame(()=>requestAnimationFrame(r))))');
    fs.writeFileSync(path.join(output,'macos-overview.png'),(await win.webContents.capturePage()).toPNG());
    await script(`window.savedReply=structuredClone(active().messages[1]);Object.assign(active().messages[1],{content:'Файл обновлён. Проверка завершена.',finalSummary:'Файл обновлён. Проверка завершена.',completedAt:new Date().toISOString(),actions:[{name:'read_text_file',args:{path:'src/example.txt'},status:'завершено',output:'Исходный текст'},{name:'write_text_file',args:{path:'src/example.txt'},status:'завершено',output:'Файл обновлён'}]});renderMessages();`);
    assert.equal(await script('document.querySelector(".completion-state").textContent'),'Готово');
    assert.equal(await script('document.querySelector(".action-log").compareDocumentPosition(document.querySelector(".completion-state")) & Node.DOCUMENT_POSITION_FOLLOWING'),4);
    assert.equal(await script('getComputedStyle(document.querySelector(".action-log")).borderTopWidth'),'0px');
    assert.equal(await script('document.querySelectorAll(".action-entry").length'),2);
    await script('document.querySelector(".action-entry").open=true');await new Promise(r=>setTimeout(r,50));await script('renderMessages()');
    assert.equal(await script('document.querySelector(".action-entry").open'),true);
    fs.writeFileSync(path.join(output,'macos-completion.png'),(await win.webContents.capturePage()).toPNG());
    await script('active().messages[1].failed=true;renderMessages()');assert.equal(await script('document.querySelector(".completion-state").textContent'),'Не завершено');
    await script('active().messages[1]=window.savedReply;delete window.savedReply;renderMessages()');
    await script(`active().messages.push({role:'user',content:'Как быстро найти материал?'},{role:'assistant',content:'Используйте поиск и метки тем.\\n'+('Подробная строка ответа.\\n'.repeat(240))},{role:'user',content:'Что добавить на главную страницу?'},{role:'assistant',content:'Описание проекта и ссылки на разделы.'});renderMessages();`);
    await script('new Promise(r=>requestAnimationFrame(()=>requestAnimationFrame(r)))');
    assert.equal(await script('document.querySelectorAll("#topic-nav button").length'),3);
    assert.equal(await script('document.querySelectorAll(".message-content").length'),3);
    assert.equal(await script('[...document.querySelectorAll(".message-content")].every(e=>e.scrollHeight<=e.clientHeight+1)'),true);
    await script('document.querySelectorAll("#topic-nav button")[1].dispatchEvent(new PointerEvent("pointermove",{clientX:1000,clientY:150}))');
    assert.match(await script('document.getElementById("topic-preview").textContent'),/найти материал.*поиск/s);
    assert.equal(await script('(()=>{const r=document.getElementById("topic-preview").getBoundingClientRect();return r.left>=0&&r.top>=0&&r.right<=innerWidth&&r.bottom<=innerHeight})()'),true);
    await script('document.querySelectorAll("#topic-nav button")[1].click()');
    for(let i=0;i<60;i++){await new Promise(r=>setTimeout(r,50));if(await script('Math.abs(document.querySelectorAll("#messages article")[2].getBoundingClientRect().top-document.getElementById("messages").getBoundingClientRect().top-12)<2'))break;}
    const jump=await script('({top:document.querySelectorAll("#messages article")[2].getBoundingClientRect().top,host:document.getElementById("messages").getBoundingClientRect().top,scroll:document.getElementById("messages").scrollTop})');
    assert.ok(Math.abs(jump.top-jump.host-12)<2,JSON.stringify(jump));
    const hoverPoint=await script('(()=>{const r=document.querySelectorAll("#topic-nav button")[1].getBoundingClientRect();return {x:Math.round(r.left+r.width/2),y:Math.round(r.top+r.height/2)}})()');
    win.webContents.sendInputEvent({type:'mouseMove',...hoverPoint});await new Promise(r=>setTimeout(r,200));
    assert.equal(await script('document.getElementById("topic-preview").classList.contains("hidden")'),false);
    fs.writeFileSync(path.join(output,'macos-conversation-topics.png'),(await win.webContents.capturePage()).toPNG());
    await script('hideTopicPreview();active().messages.splice(2);renderMessages()');
    win.webContents.send('muse:event',{type:'status',data:{state:'loading',message:'Выгружаем модель'}});await new Promise(r=>setTimeout(r,50));assert.equal(await script('document.getElementById("input").disabled'),true);
    win.webContents.send('muse:event',{type:'status',data:{state:'ready',message:'Готово'}});await new Promise(r=>setTimeout(r,50));assert.equal(await script('document.getElementById("input").disabled'),false);
    await script('document.querySelector("[data-menu=view]").click()');assert.match(await script('document.getElementById("menu-popup").textContent'),/✓/);await script('document.body.click();document.getElementById("menu-popup").classList.add("hidden")');
    const count=store.state.chats.length;await script('document.getElementById("new").click()');await new Promise(r=>setTimeout(r,200));assert.equal(store.state.chats.length,count+1);
    await script('document.getElementById("input").value="строка\\n".repeat(300);document.getElementById("input").dispatchEvent(new Event("input"))');
    assert.equal(await script('document.getElementById("composer").offsetHeight <= document.getElementById("conversation").clientHeight/3+10'),true);
    await script('document.getElementById("input").value="";document.getElementById("input").dispatchEvent(new Event("input"))');
    await script('document.querySelector("[data-menu=help]").click();document.querySelector("#menu-popup button").click()');await new Promise(r=>setTimeout(r,200));
    assert.equal(await script('document.getElementById("onboarding").open'),true);assert.match(await script('document.getElementById("hardware").textContent'),/48 ГБ/);
    fs.writeFileSync(path.join(output,'macos-setup.png'),(await win.webContents.capturePage()).toPNG());
    console.log('MAC UI SMOKE PASSED: layout, menus, projects, chat creation, composer, setup');app.exit(0);
  }catch(error){console.error(error);fs.writeFileSync(path.join(output,'failure.png'),(await win.webContents.capturePage()).toPNG());app.exit(1);}
}
module.exports={run};
