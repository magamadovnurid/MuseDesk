// Offline conversion of checked-in SVGs. No installed Codex application is needed.
const fs = require('node:fs');
const path = require('node:path');
const sharp = require('sharp');
const JSZip = require('jszip');
const dir = path.resolve(__dirname, '../windows-native/assets/icons');
const sources = JSON.parse(fs.readFileSync(path.join(dir, 'sources.json'), 'utf8'));
const sizes = [12,14,16,18,20,21,24,28,30,32,36,40,42,48,64,84];
(async () => {
  const zip = new JSZip();
  for (const {name} of sources) {
    const svg = fs.readFileSync(path.join(dir, name + '.svg'), 'utf8').replaceAll('currentColor', '#ffffff');
    for (const size of sizes) {
      const png = await sharp(Buffer.from(svg), {density:384}).resize(size,size,{fit:'fill'}).png().toBuffer();
      zip.file(name + '/' + size + '.png', png, {date:new Date('2020-01-01T00:00:00Z')});
    }
  }
  fs.writeFileSync(path.join(dir,'icons.zip'), await zip.generateAsync({type:'nodebuffer',compression:'DEFLATE'}));
  console.log(`Built ${sources.length} icons at ${sizes.length} sizes.`);
})().catch(error => { console.error(error); process.exitCode = 1; });
