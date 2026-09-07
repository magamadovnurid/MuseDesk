// Optional live smoke tests. Synthetic prompts only; tool calls are NOT executed.
// Requires Node.js 22+. The model must already be installed in Muse Desk's engine.
const model = process.argv[2];
if (!model || !process.argv.includes('--live')) {
  console.error('Usage: node scripts/verify-model.cjs <installed-model-name> --live');
  console.error('Loads a full model on the GPU. Do not run while diagnosing unexpected computer resets.');
  process.exit(2);
}
const base = 'http://127.0.0.1:11436';
async function api(route, body) {
  const r = await fetch(base + route, {
    method: body ? 'POST' : 'GET',
    headers: {'Content-Type':'application/json'},
    body: body ? JSON.stringify(body) : undefined,
    signal: AbortSignal.timeout(600000)
  });
  if (!r.ok) throw Error(route + ': ' + r.status + ' ' + await r.text());
  return r.json();
}
async function chat(messages, extra = {}) {
  return api('/api/chat', {model, messages, stream:false, think:false,
    options:{num_ctx:16384,num_predict:512,temperature:0}, keep_alive:'2m', ...extra});
}
(async () => {
  const show = await api('/api/show', {model});
  console.log(JSON.stringify({model,capabilities:show.capabilities,details:show.details}));
  const text = await chat([{role:'user',content:'Вычисли 17 * 19. Ответь по-русски: Результат: и число. Без пояснений.'}]);
  if (!text.message?.content?.includes('323')) throw Error('Arithmetic/text smoke test failed: '+JSON.stringify(text.message));
  console.log('PASS text: '+text.message.content);
  const thinking = await chat([{role:'user',content:'Сколько будет 2 + 2? Ответь одним числом.'}],{think:true});
  if (!thinking.message?.content?.includes('4')) throw Error('Thinking smoke test failed: '+JSON.stringify(thinking.message));
  console.log('PASS thinking toggle; reasoning chars: '+(thinking.message.thinking||'').length);
  if (!show.capabilities?.includes('tools')) throw Error('The installed model does not advertise tools.');
  const tools=[{type:'function',function:{name:'get_test_value',description:'Get the numeric value for this test. Call without arguments.',parameters:{type:'object',properties:{},required:[]}}}];
  const question={role:'user',content:'Вызови get_test_value без аргументов. Не угадывай результат. После получения результата инструмента умножь его на два.'};
  const first=await chat([question],{tools});
  if(first.message?.tool_calls?.[0]?.function?.name!=='get_test_value') throw Error('Structured tool call missing: '+JSON.stringify(first.message));
  const answer=await chat([question,first.message,{role:'tool',tool_name:'get_test_value',content:'7'}],{tools});
  if(!answer.message?.content?.includes('14')) throw Error('Tool continuation failed: '+JSON.stringify(answer.message));
  console.log('PASS structured tool call + synthetic tool result: '+answer.message.content);
  if (show.capabilities.includes('vision')) {
    // Synthetic 64x64 red PNG. No screenshot or personal image is used.
    const redPng='iVBORw0KGgoAAAANSUhEUgAAAEAAAABACAIAAAAlC+aJAAAACXBIWXMAAAPoAAAD6AG1e1JrAAAAlklEQVRoge2SwQkAMBCD3H9pO0QfchBwACNBOA25gRtAXtFdiLuQG7gB5BXdhbgLuYEbQF7RXYi7kBu4AeQV3YW4C7mBG0Be0V2Iu5AbuAHkFd2FuAu5gRtAXtFdiLuQG7gB5BXdhbgLuYEbQF7RXYi7kBu4AeQV3YW4C7mBG0Be0V2Iu5AbuAHkFd2FuAu5gRtAXvGHB6nc8OJ57m0sAAAAAElFTkSuQmCC';
    const vision=await chat([{role:'user',content:'Какого цвета это одноцветное изображение? Ответь одним словом.',images:[redPng]}]);
    if(!/красн|red/i.test(vision.message?.content||'')) throw Error('Vision smoke test failed: '+JSON.stringify(vision.message));
    console.log('PASS image input: '+vision.message.content);
  }
  console.log('LIVE MODEL CHECKS PASSED');
})().catch(error=>{console.error(error);process.exitCode=1;});
