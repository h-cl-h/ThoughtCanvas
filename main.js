const { app, BrowserWindow, Menu, ipcMain, dialog } = require('electron');
const path = require('path');
const fs = require('fs');

// 开发版放源码目录；便携版放 EXE 同级；安装版放 Electron 的 userData，避免 Program Files 写权限问题。
const textStylesDir = () => process.env.THOUGHTCANVAS_TEXT_STYLES_DIR || path.join(!app.isPackaged ? __dirname : (process.env.PORTABLE_EXECUTABLE_DIR || path.join(app.getPath('appData'), 'ThoughtCanvas')), 'text-styles');
const textStylesFile = () => path.join(textStylesDir(), 'custom-text-styles.json');
const textStyleCandidates = () => [...new Set([textStylesFile(), ...(app.isPackaged ? [path.join(path.dirname(process.execPath),'text-styles','custom-text-styles.json')] : [])])];
let textStyleWatchStarted = false;
async function readTextStyles() {
  const found=[];for(const file of textStyleCandidates()){try{const st=await fs.promises.stat(file);found.push({file,mtime:st.mtimeMs});}catch(_){}}
  found.sort((a,b)=>b.mtime-a.mtime);
  for(const item of found){try{const json=JSON.parse(await fs.promises.readFile(item.file,'utf8'));return {ok:true,path:item.file,defaultStyleId:json.defaultStyleId||'classic',styleSettings:json.styleSettings||{},styles:Array.isArray(json.styles)?json.styles:[]};}catch(_){}}
  return {ok:false,path:textStylesFile(),defaultStyleId:'classic',styleSettings:{},styles:[],error:''};
}
function startTextStyleWatch() {
  if (textStyleWatchStarted) return;
  textStyleWatchStarted = true;
  fs.mkdirSync(path.dirname(textStylesFile()), { recursive: true });
  textStyleCandidates().forEach(file=>fs.watchFile(file,{interval:450},()=>{if(mainWin&&!mainWin.isDestroyed())mainWin.webContents.send('text-styles-changed');}));
}

const FILTERS = [
  { name: 'ThoughtCanvas 思维导图', extensions: ['bmap'] },
  { name: '所有文件', extensions: ['*'] }
];

let mainWin = null;
let pendingFile = null;   // 启动时通过双击文件传入、等待渲染层读取的路径
let allowClose = false;   // 是否已确认可以关闭

// 让安装版在 Windows 的任务栏、通知和文件关联中使用稳定的应用标识。
if (process.platform === 'win32') app.setAppUserModelId('com.thoughtcanvas.app');

// 从命令行参数里找出 .bmap 文件路径（双击文件时由系统传入）
function fileFromArgv(argv) {
  if (!argv) return null;
  const hit = argv.find(a => typeof a === 'string' && /\.bmap$/i.test(a) && fs.existsSync(a));
  return hit || null;
}
async function readFileSafe(fp) {
  try { return { ok: true, path: fp, name: path.basename(fp), content: await fs.promises.readFile(fp, 'utf8') }; }
  catch (err) { return { ok: false, error: err.message }; }
}

let saveSessionId = 0;
const saveSessions = new Map();
const saveSenderCleanupBound = new Set();

async function cleanupSaveArtifacts(s, closeHandle = true) {
  if (!s) return;
  if (closeHandle) await s.handle.close().catch(() => {});
  await fs.promises.unlink(s.tempPath).catch(() => {});
}
async function discardSaveSession(id) {
  const s = saveSessions.get(id);
  if (!s) return;
  saveSessions.delete(id);
  await cleanupSaveArtifacts(s);
}
async function discardSenderSaveSessions(senderId) {
  const ids = [...saveSessions].filter(([, s]) => s.senderId === senderId).map(([id]) => id);
  await Promise.all(ids.map(discardSaveSession));
}
function bindSaveSenderCleanup(sender) {
  const senderId = sender.id;
  if (saveSenderCleanupBound.has(senderId)) return;
  saveSenderCleanupBound.add(senderId);
  const cleanup = () => {
    saveSenderCleanupBound.delete(senderId);
    sender.removeListener('destroyed', cleanup);
    sender.removeListener('render-process-gone', cleanup);
    void discardSenderSaveSessions(senderId);
  };
  sender.once('destroyed', cleanup);
  sender.once('render-process-gone', cleanup);
}

function createWindow() {
  const win = new BrowserWindow({
    width: 1280,
    height: 820,
    minWidth: 760,
    minHeight: 520,
    backgroundColor: '#f5f6f8',
    title: 'ThoughtCanvas',
    icon: path.join(__dirname, 'build', 'icon.ico'),
    autoHideMenuBar: true,
    webPreferences: {
      preload: path.join(__dirname, 'preload.js'),
      contextIsolation: true,
      nodeIntegration: false
    }
  });

  Menu.setApplicationMenu(null);          // 去掉浏览器式菜单栏
  win.loadFile(path.join(__dirname, 'index.html'));
  mainWin = win;
  startTextStyleWatch();

  // 关闭前：交给渲染层判断是否有未保存更改
  win.on('close', (e) => {
    if (allowClose) return;
    e.preventDefault();
    win.webContents.send('app-close-request');
  });
}

// 渲染层处理完“保存/不保存/取消”后回传结果
ipcMain.on('close-confirmed', (e, proceed) => {
  if (proceed && mainWin) { allowClose = true; mainWin.close(); }
});

// 大文件按画布分片写入：渲染层不再构造并跨 IPC 复制一份完整 JSON。
ipcMain.handle('save-begin', async (e, { filePath, defaultName }) => {
  let target = filePath;
  if (!target) {
    const r = await dialog.showSaveDialog(mainWin, {
      title: '另存为',
      defaultPath: (defaultName || '未命名思维导图') + '.bmap',
      filters: FILTERS
    });
    if (r.canceled || !r.filePath) return { canceled: true };
    target = r.filePath;
  }
  const id = `${process.pid}-${++saveSessionId}`;
  const tempPath = `${target}.tmp-${id}`;
  const handle = await fs.promises.open(tempPath, 'w');
  saveSessions.set(id, { handle, tempPath, filePath: target, senderId: e.sender.id, position: 0 });
  bindSaveSenderCleanup(e.sender);
  return { canceled: false, id, path: target, name: path.basename(target) };
});

ipcMain.handle('save-chunk', async (e, { id, chunk }) => {
  const s = saveSessions.get(id);
  if (!s || s.senderId !== e.sender.id) throw new Error('保存会话无效');
  const buf = Buffer.from(String(chunk || ''), 'utf8');
  await s.handle.write(buf, 0, buf.length, s.position);
  s.position += buf.length;
  return { ok: true };
});

ipcMain.handle('save-end', async (e, { id, abort }) => {
  const s = saveSessions.get(id);
  if (!s || s.senderId !== e.sender.id) throw new Error('保存会话无效');
  saveSessions.delete(id);
  let closed = false;
  try {
    await s.handle.close();
    closed = true;
    if (abort) return { ok: false, canceled: true };
    await fs.promises.copyFile(s.tempPath, s.filePath);
    return { ok: true, path: s.filePath, name: path.basename(s.filePath) };
  } finally {
    await cleanupSaveArtifacts(s, !closed);
  }
});

// 打开：选择文件并读取内容
ipcMain.handle('open', async () => {
  const { canceled, filePaths } = await dialog.showOpenDialog(mainWin, {
    title: '打开思维导图',
    properties: ['openFile'],
    filters: FILTERS
  });
  if (canceled || !filePaths.length) return { canceled: true };
  const filePath = filePaths[0];
  const content = await fs.promises.readFile(filePath, 'utf8');
  return { canceled: false, path: filePath, name: path.basename(filePath), content };
});

// 按路径直接打开（用于“最近使用”）
ipcMain.handle('open-path', async (e, filePath) => {
  try {
    const content = await fs.promises.readFile(filePath, 'utf8');
    return { ok: true, path: filePath, name: path.basename(filePath), content };
  } catch (err) {
    return { ok: false, error: err.message };
  }
});

// 导出图片 / PDF：选择保存位置并写入二进制
ipcMain.handle('export-save', async (e, { base64, defaultName, ext }) => {
  const names = { pdf: 'PDF 文档', jpg: 'JPEG 图片', png: 'PNG 图片' };
  const { canceled, filePath } = await dialog.showSaveDialog(mainWin, {
    title: '导出',
    defaultPath: (defaultName || '思维导图') + '.' + ext,
    filters: [{ name: names[ext] || ext, extensions: [ext] }]
  });
  if (canceled || !filePath) return { canceled: true };
  await fs.promises.writeFile(filePath, Buffer.from(base64, 'base64'));
  return { canceled: false, path: filePath };
});

// UI 皮肤持久化到 userData 下的文件（localStorage 万一被清也能恢复，导入后无需重复导入）
const skinsFile = () => path.join(app.getPath('userData'), 'ui-skins.json');
function orderedTextStore(fileFor){
  let queue=Promise.resolve();
  return {
    read:async()=>{await queue;return fs.promises.readFile(fileFor(),'utf8');},
    write:json=>{const task=queue.then(()=>fs.promises.writeFile(fileFor(),json,'utf8'));queue=task.catch(()=>{});return task;}
  };
}
const skinsStore=orderedTextStore(skinsFile);
ipcMain.handle('skins-load', async () => { try { return await skinsStore.read(); } catch (err) { return null; } });
ipcMain.handle('skins-save', async (e, json) => { try { await skinsStore.write(json); return { ok: true }; } catch (err) { return { ok: false, error: err.message }; } });

ipcMain.handle('text-styles-load', async () => readTextStyles());
ipcMain.handle('text-styles-location', async () => ({ directory: textStylesDir(), file: textStylesFile(), executable: process.execPath }));
ipcMain.handle('text-styles-save', async (e, library) => {
  try {
    const clean={format:'thoughtcanvas-text-styles',version:1,defaultStyleId:(library&&library.defaultStyleId)||'classic',styleSettings:(library&&library.styleSettings)||{},styles:Array.isArray(library&&library.styles)?library.styles:[]};
    await fs.promises.mkdir(textStylesDir(),{recursive:true});
    const tmp=textStylesFile()+'.tmp';await fs.promises.writeFile(tmp,JSON.stringify(clean,null,2),'utf8');await fs.promises.copyFile(tmp,textStylesFile());await fs.promises.unlink(tmp);
    return {ok:true,path:textStylesFile()};
  } catch(err){ return {ok:false,error:err.message,path:textStylesFile()}; }
});

// AI 设置（后端/地址/模型名/API Key）持久化到 userData 下的文件——同 UI 皮肤，重启不丢；密钥只落本地，绝不上传
const aiConfFile = () => path.join(app.getPath('userData'), 'ai-config.json');
const aiConfStore=orderedTextStore(aiConfFile);
ipcMain.handle('aiconf-load', async () => { try { return await aiConfStore.read(); } catch (err) { return null; } });
ipcMain.handle('aiconf-save', async (e, json) => { try { await aiConfStore.write(json); return { ok: true }; } catch (err) { return { ok: false, error: err.message }; } });

// AI 请求走主进程（Node，无浏览器 CORS 限制，可直连本地 Ollama/LM Studio 与各家云）。
// 密钥/地址由渲染层传来，主进程只负责转发，不落任何日志、不改内容。
const aiReqs = new Map();
ipcMain.on('ai-chat-start', async (e, { reqId, url, headers, body }) => {
  const controller = new AbortController();
  aiReqs.set(reqId, controller);
  const send = (ch, data) => { if (mainWin && !mainWin.isDestroyed()) mainWin.webContents.send(ch, Object.assign({ reqId }, data)); };
  try {
    const res = await fetch(url, { method: 'POST', headers, body, signal: controller.signal });
    if (!res.ok) { let t = ''; try { t = await res.text(); } catch (_) {}
      send('ai-chat-error', { message: 'HTTP ' + res.status + (t ? (' · ' + t.slice(0, 300)) : '') }); aiReqs.delete(reqId); return; }
    if (!res.body) { const j = await res.json(); const c = (((j.choices || [])[0] || {}).message || {}).content || '';
      send('ai-chat-raw', { chunk: 'data: ' + JSON.stringify({ choices: [{ delta: { content: c } }] }) + '\n' }); send('ai-chat-end', {}); aiReqs.delete(reqId); return; }
    for await (const chunk of res.body) { send('ai-chat-raw', { chunk: Buffer.from(chunk).toString('utf8') }); }
    send('ai-chat-end', {});
  } catch (err) { send('ai-chat-error', { message: (err && err.message) || String(err) }); }
  aiReqs.delete(reqId);
});
ipcMain.on('ai-chat-abort', (e, { reqId }) => { const c = aiReqs.get(reqId); if (c) { try { c.abort(); } catch (_) {} aiReqs.delete(reqId); } });
// 测试连接（非流式，返回结果）
ipcMain.handle('ai-probe', async (e, { url, headers, body }) => {
  try { const res = await fetch(url, { method: 'POST', headers, body });
    if (res.ok) return { ok: true };
    let t = ''; try { t = await res.text(); } catch (_) {}
    return { ok: false, status: res.status, text: t.slice(0, 200) };
  } catch (err) { return { ok: false, error: (err && err.message) || String(err) }; }
});
// 列出端点可用模型（GET /models，OpenAI 兼容；Ollama/LM Studio/各家云都支持）
ipcMain.handle('ai-models', async (e, { url, headers }) => {
  try { const res = await fetch(url, { method: 'GET', headers });
    if (!res.ok) { let t = ''; try { t = await res.text(); } catch (_) {} return { ok: false, status: res.status, text: t.slice(0, 200) }; }
    const j = await res.json(); return { ok: true, data: j };
  } catch (err) { return { ok: false, error: (err && err.message) || String(err) }; }
});

// 渲染层启动时索取“双击打开的初始文件”
ipcMain.handle('get-initial-file', async () => {
  if (!pendingFile) return null;
  const r = await readFileSafe(pendingFile);
  pendingFile = null;
  return r.ok ? r : null;
});

// 未保存提示框
ipcMain.handle('confirm-discard', async (e, name) => {
  const { response } = await dialog.showMessageBox(mainWin, {
    type: 'warning',
    buttons: ['保存', '不保存', '取消'],
    defaultId: 0,
    cancelId: 2,
    message: `“${name || '未命名思维导图'}” 有未保存的更改`,
    detail: '是否保存当前思维导图？'
  });
  return response; // 0=保存 1=不保存 2=取消
});

// 单实例：已开着软件时再双击文件，转发给现有窗口，而不是另开一个
const integrationTest = process.env.THOUGHTCANVAS_INTEGRATION_TEST === '1';
let openFileQueue=Promise.resolve();
function enqueueOpenFile(fp){
  openFileQueue=openFileQueue.then(async()=>{const r=await readFileSafe(fp);
    if(r.ok&&mainWin&&!mainWin.isDestroyed())mainWin.webContents.send('open-file-data',r);}).catch(()=>{});
  return openFileQueue;
}
const gotLock = integrationTest || app.requestSingleInstanceLock();
if (!gotLock) {
  app.quit();
} else {
  app.on('second-instance', (event, argv) => {
    const f = fileFromArgv(argv);
    if (mainWin) {
      if (mainWin.isMinimized()) mainWin.restore();
      mainWin.focus();
      if (f) enqueueOpenFile(f);
    }
  });

  // macOS：通过“打开方式”打开文件
  app.on('open-file', (event, fp) => {
    event.preventDefault();
    if (mainWin) enqueueOpenFile(fp);
    else pendingFile = fp;
  });

  app.whenReady().then(() => {
    if (integrationTest) return;
    pendingFile = fileFromArgv(process.argv);   // 首次启动时双击传入的文件
    createWindow();
    app.on('activate', () => {
      if (BrowserWindow.getAllWindows().length === 0) createWindow();
    });
  });

  app.on('window-all-closed', () => {
    if (process.platform !== 'darwin') app.quit();
  });
}
