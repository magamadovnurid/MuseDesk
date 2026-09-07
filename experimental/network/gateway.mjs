import https from 'node:https';
import { readFileSync } from 'node:fs';
import { timingSafeEqual } from 'node:crypto';
import { once } from 'node:events';
import { pathToFileURL } from 'node:url';

const MODEL = 'acc100/muse-glimmer-heretic:latest';
const MAX_BODY = 16 * 1024 * 1024;
export function ipv4Number(ip) {
  const parts = ip.replace(/^::ffff:/, '').split('.');
  if (parts.length !== 4 || parts.some(x => !/^\d{1,3}$/.test(x) || +x > 255)) return null;
  return parts.reduce((v, x) => ((v << 8) | +x) >>> 0, 0);
}
export function inSubnet(ip, subnet) {
  const [address, prefix] = subnet.split('/');
  const n = ipv4Number(ip), base = ipv4Number(address), bits = Number(prefix);
  if (n === null || base === null || !Number.isInteger(bits) || bits < 8 || bits > 32) return false;
  const mask = (0xffffffff << (32 - bits)) >>> 0;
  return (n & mask) === (base & mask);
}
export function cleanChat(body) {
  if (!body || !Array.isArray(body.messages) || !body.messages.length || body.messages.length > 150) throw Error('Ожидается от 1 до 150 сообщений.');
  const messages = body.messages.map(m => {
    if (!m || !['system','user','assistant'].includes(m.role) || typeof m.content !== 'string' || m.content.length > 150000) throw Error('Недопустимое сообщение.');
    const out = { role: m.role, content: m.content };
    if (m.images !== undefined) {
      if (m.role !== 'user' || !Array.isArray(m.images) || m.images.length > 4 || m.images.some(i => typeof i !== 'string' || i.length > 6000000 || !/^[A-Za-z0-9+/]+={0,2}$/.test(i))) throw Error('Недопустимое изображение.');
      out.images = m.images;
    }
    return out;
  });
  return { model: MODEL, messages, stream: true, think: body.think === true, keep_alive: '10m', options: { num_ctx: 16384, num_predict: 8192, temperature: 1 } };
}
function reply(res, status, error) { res.writeHead(status, {'Content-Type':'application/json; charset=utf-8','Cache-Control':'no-store'}); res.end(JSON.stringify({error})); }
export function createGateway(config, tls, upstream = 'http://127.0.0.1:11436') {
  if (!/^[a-f0-9]{64}$/.test(config.token)) throw Error('Invalid access key');
  const expected = Buffer.from('Bearer ' + config.token);
  let busy = false;
  const server = https.createServer({ ...tls, minVersion: 'TLSv1.2', maxHeaderSize:8192 }, async (req, res) => {
    res.setHeader('Cache-Control','no-store');
    res.setHeader('X-Content-Type-Options','nosniff');
    const remote = req.socket.remoteAddress || '';
    if (!inSubnet(remote, config.subnet) && remote !== '127.0.0.1') return reply(res,403,'Доступ разрешён только из домашней сети.');
    // Native clients only: prevent browser origins from calling the gateway.
    if (req.headers.origin) return reply(res,403,'Браузерный доступ отключён.');
    const received = Buffer.from(req.headers.authorization || '');
    if (received.length !== expected.length || !timingSafeEqual(received,expected)) return reply(res,401,'Неверный ключ подключения.');
    const abort = new AbortController();
    const timer = setTimeout(() => abort.abort(), 10 * 60 * 1000);
    res.on('close', () => { clearTimeout(timer); if(!res.writableEnded) abort.abort(); });
    try {
      if (req.method === 'GET' && req.url === '/health') {
        const response = await fetch(upstream + '/api/tags', {signal: AbortSignal.any([abort.signal,AbortSignal.timeout(5000)])});
        if (!response.ok) throw Error('Движок не отвечает.');
        const tags = await response.json();
        const ready = tags.models?.some(m => (m.name || m.model) === MODEL) === true;
        res.writeHead(ready ? 200 : 503,{'Content-Type':'application/json'});
        res.end(JSON.stringify({ready,model:MODEL,busy,version:1})); return;
      }
      if (req.method !== 'POST' || req.url !== '/chat') return reply(res,404,'Этот метод недоступен.');
      if (!(req.headers['content-type'] || '').startsWith('application/json')) return reply(res,415,'Ожидается JSON.');
      if (Number(req.headers['content-length']) > MAX_BODY) { req.resume(); return reply(res,413,'Вложения слишком большие.'); }
      const chunks = []; let size = 0;
      for await (const chunk of req) { size += chunk.length; if(size > MAX_BODY) { reply(res,413,'Вложения слишком большие.'); req.destroy(); return; } chunks.push(chunk); }
      let payload;
      try { payload = cleanChat(JSON.parse(Buffer.concat(chunks).toString('utf8'))); }
      catch (e) { return reply(res,400,e.message); }
      if (busy) return reply(res,409,'Muse отвечает другому сетевому клиенту. Попробуйте после завершения ответа.');
      busy = true;
      try {
        const response = await fetch(upstream + '/api/chat', {method:'POST', headers:{'Content-Type':'application/json'}, body:JSON.stringify(payload),signal:abort.signal});
        if(!response.ok) return reply(res,502,'Движок не смог начать ответ. Проверьте Muse Desk на Windows.');
        res.writeHead(200,{'Content-Type':'application/x-ndjson; charset=utf-8'}); res.flushHeaders();
        for await (const chunk of response.body) {
          if (abort.signal.aborted) break;
          if(!res.write(chunk)) await Promise.race([once(res,'drain'),once(res,'close')]);
        }
        res.end();
      } finally { busy = false; }
    } catch (e) {
      if (!res.headersSent) reply(res,503,'Muse недоступна. Откройте Muse Desk на Windows и повторите подключение.');
      else if (!res.writableEnded) res.destroy();
    } finally { clearTimeout(timer); }
  });
  server.requestTimeout = 30000;
  server.headersTimeout = 10000;
  server.keepAliveTimeout = 5000;
  return server;
}
if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const config = JSON.parse(readFileSync(new URL('./private/server.json',import.meta.url),'utf8').replace(/^\uFEFF/,''));
  const server = createGateway(config,{pfx:readFileSync(new URL('./private/server.pfx',import.meta.url)),passphrase:config.passphrase});
  server.on('error',e => { console.error('Muse Wi-Fi: '+e.code); process.exitCode=1; });
  server.listen(config.port,config.host,()=>console.log(`Muse Wi-Fi: https://${config.host}:${config.port}`));
}
