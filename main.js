const { app, BrowserWindow, Menu, ipcMain, dialog } = require('electron');
const path = require('path');
const fs = require('fs');

const FILTERS = [
  { name: 'ThoughtCanvas 思维导图', extensions: ['bmap'] },
  { name: '所有文件', extensions: ['*'] }
];

let mainWin = null;
let pendingFile = null;   // 启动时通过双击文件传入、等待渲染层读取的路径
let allowClose = false;   // 是否已确认可以关闭

// 从命令行参数里找出 .bmap 文件路径（双击文件时由系统传入）
function fileFromArgv(argv) {
  if (!argv) return null;
  const hit = argv.find(a => typeof a === 'string' && /\.bmap$/i.test(a) && fs.existsSync(a));
  return hit || null;
}
function readFileSafe(fp) {
  try { return { ok: true, path: fp, name: path.basename(fp), content: fs.readFileSync(fp, 'utf8') }; }
  catch (err) { return { ok: false, error: err.message }; }
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

// 另存：弹出保存对话框选路径与文件名，再写入
ipcMain.handle('save-as', async (e, { content, defaultName }) => {
  const { canceled, filePath } = await dialog.showSaveDialog(mainWin, {
    title: '另存为',
    defaultPath: (defaultName || '未命名思维导图') + '.bmap',
    filters: FILTERS
  });
  if (canceled || !filePath) return { canceled: true };
  fs.writeFileSync(filePath, content, 'utf8');
  return { canceled: false, path: filePath, name: path.basename(filePath) };
});

// 保存到已知路径
ipcMain.handle('save', async (e, { content, filePath }) => {
  fs.writeFileSync(filePath, content, 'utf8');
  return { ok: true, path: filePath, name: path.basename(filePath) };
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
  const content = fs.readFileSync(filePath, 'utf8');
  return { canceled: false, path: filePath, name: path.basename(filePath), content };
});

// 按路径直接打开（用于“最近使用”）
ipcMain.handle('open-path', async (e, filePath) => {
  try {
    const content = fs.readFileSync(filePath, 'utf8');
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
  fs.writeFileSync(filePath, Buffer.from(base64, 'base64'));
  return { canceled: false, path: filePath };
});

// UI 皮肤持久化到 userData 下的文件（localStorage 万一被清也能恢复，导入后无需重复导入）
const skinsFile = () => path.join(app.getPath('userData'), 'ui-skins.json');
ipcMain.handle('skins-load', () => { try { return fs.readFileSync(skinsFile(), 'utf8'); } catch (err) { return null; } });
ipcMain.handle('skins-save', (e, json) => { try { fs.writeFileSync(skinsFile(), json, 'utf8'); return { ok: true }; } catch (err) { return { ok: false, error: err.message }; } });

// AI 设置（后端/地址/模型名/API Key）持久化到 userData 下的文件——同 UI 皮肤，重启不丢；密钥只落本地，绝不上传
const aiConfFile = () => path.join(app.getPath('userData'), 'ai-config.json');
ipcMain.handle('aiconf-load', () => { try { return fs.readFileSync(aiConfFile(), 'utf8'); } catch (err) { return null; } });
ipcMain.handle('aiconf-save', (e, json) => { try { fs.writeFileSync(aiConfFile(), json, 'utf8'); return { ok: true }; } catch (err) { return { ok: false, error: err.message }; } });

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
ipcMain.handle('get-initial-file', () => {
  if (!pendingFile) return null;
  const r = readFileSafe(pendingFile);
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
const gotLock = app.requestSingleInstanceLock();
if (!gotLock) {
  app.quit();
} else {
  app.on('second-instance', (event, argv) => {
    const f = fileFromArgv(argv);
    if (mainWin) {
      if (mainWin.isMinimized()) mainWin.restore();
      mainWin.focus();
      if (f) {
        const r = readFileSafe(f);
        if (r.ok) mainWin.webContents.send('open-file-data', r);
      }
    }
  });

  // macOS：通过“打开方式”打开文件
  app.on('open-file', (event, fp) => {
    event.preventDefault();
    if (mainWin) { const r = readFileSafe(fp); if (r.ok) mainWin.webContents.send('open-file-data', r); }
    else pendingFile = fp;
  });

  app.whenReady().then(() => {
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
