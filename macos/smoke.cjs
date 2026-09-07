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
    fs.writeFileSync(path.join(output,'macos-overview.png'),(await win.webContents.capturePage()).toPNG());
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
