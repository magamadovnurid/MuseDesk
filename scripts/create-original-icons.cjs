// Original geometric UI drawings for Muse Desk; no extracted application assets.
const fs=require('node:fs'),path=require('node:path');
const shapes={
folder:'<path d="M3 6h7l2 3h9v11H3z"/>',
'folder-open':'<path d="M3 19V6h7l2 3h8v3M3 19l3-7h16l-3 7z"/>',
more:'<circle cx="5" cy="12" r="1"/><circle cx="12" cy="12" r="1"/><circle cx="19" cy="12" r="1"/>',
results:'<rect x="3" y="4" width="18" height="16" rx="3"/><path d="M15 4v16M7 9h4M7 13h4"/>',
globe:'<circle cx="12" cy="12" r="9"/><ellipse cx="12" cy="12" rx="4" ry="9"/><path d="M3 12h18"/>',
export:'<path d="M12 16V3m-4 4 4-4 4 4M4 13v7h16v-7"/>',
add:'<path d="M12 4v16M4 12h16"/>',
sidebar:'<rect x="3" y="4" width="18" height="16" rx="3"/><path d="M9 4v16"/>',
edit:'<path d="m5 16 1-4L17 1l4 4-11 11-5 1m9-13 4 4M4 7H2v15h15v-3"/>',
search:'<circle cx="10" cy="10" r="7"/><path d="m15 15 6 6"/>',
image:'<rect x="3" y="3" width="18" height="18" rx="3"/><circle cx="8" cy="8" r="1.5"/><path d="m3 18 6-6 4 4 3-3 5 5"/>',
screen:'<rect x="2" y="3" width="20" height="14" rx="2"/><path d="M12 17v4M7 21h10"/>',
mic:'<rect x="9" y="2" width="6" height="13" rx="3"/><path d="M5 10v2a7 7 0 0 0 14 0v-2M12 19v3M8 22h8"/>',
sound:'<path d="M3 9h4l5-5v16l-5-5H3zM16 8q5 4 0 8M19 4q8 8 0 16"/>',
send:'<path d="M12 21V3M5 10l7-7 7 7"/>',
stop:'<rect x="5" y="5" width="14" height="14" rx="2"/>',
copy:'<rect x="8" y="3" width="12" height="14" rx="2"/><path d="M5 7H4a1 1 0 0 0-1 1v12a1 1 0 0 0 1 1h10a1 1 0 0 0 1-1"/>',
file:'<path d="M5 2h9l5 5v15H5zM14 2v6h5M8 12h8M8 16h6"/>',
sliders:'<path d="M3 6h7m5 0h6M3 12h2m5 0h11M3 18h12m5 0h1"/><circle cx="12.5" cy="6" r="2.5"/><circle cx="7.5" cy="12" r="2.5"/><circle cx="17.5" cy="18" r="2.5"/>',
chat:'<path d="M5 3h14a2 2 0 0 1 2 2v11a2 2 0 0 1-2 2H9l-6 4V5a2 2 0 0 1 2-2zM7 8h10M7 12h7"/>',
code:'<path d="m8 6-6 6 6 6m8-12 6 6-6 6M14 3l-4 18"/>',
retry:'<path d="M20 8a8 8 0 1 0 0 8M20 2v6h-6"/>',
settings:'<circle cx="12" cy="12" r="7"/><circle cx="12" cy="12" r="3"/><path d="M12 2v3m0 14v3M2 12h3m14 0h3M5 5l2 2m10 10 2 2M19 5l-2 2M7 17l-2 2"/>',
check:'<path d="m4 12 5 5L20 6"/>',
spark:'<path d="m12 2 3 7 7 3-7 3-3 7-3-7-7-3 7-3z"/>'
};
const dir=path.join(__dirname,'../windows-native/assets/icons');
for(const [name,shape] of Object.entries(shapes))fs.writeFileSync(path.join(dir,name+'.svg'),`<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round">${shape}</svg>\n`);
fs.writeFileSync(path.join(dir,'sources.json'),JSON.stringify(Object.keys(shapes).map(name=>({name,source:'Original Muse Desk geometric drawing',generator:'scripts/create-original-icons.cjs'})),null,2)+'\n');
