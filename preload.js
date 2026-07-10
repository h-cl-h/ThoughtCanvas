const { contextBridge, ipcRenderer } = require('electron');

contextBridge.exposeInMainWorld('api', {
  isDesktop: true,
  saveAs: (content, defaultName) => ipcRenderer.invoke('save-as', { content, defaultName }),
  save: (content, filePath) => ipcRenderer.invoke('save', { content, filePath }),
  open: () => ipcRenderer.invoke('open'),
  openPath: (filePath) => ipcRenderer.invoke('open-path', filePath),
  exportSave: (base64, defaultName, ext) => ipcRenderer.invoke('export-save', { base64, defaultName, ext }),
  confirmDiscard: (name) => ipcRenderer.invoke('confirm-discard', name),
  getInitialFile: () => ipcRenderer.invoke('get-initial-file'),
  loadSkins: () => ipcRenderer.invoke('skins-load'),
  saveSkins: (json) => ipcRenderer.invoke('skins-save', json),
  loadAIConf: () => ipcRenderer.invoke('aiconf-load'),
  saveAIConf: (json) => ipcRenderer.invoke('aiconf-save', json),
  aiProbe: (payload) => ipcRenderer.invoke('ai-probe', payload),
  aiModels: (payload) => ipcRenderer.invoke('ai-models', payload),
  aiStream: {
    start: (payload) => ipcRenderer.send('ai-chat-start', payload),
    abort: (reqId) => ipcRenderer.send('ai-chat-abort', { reqId }),
    onRaw: (cb) => ipcRenderer.on('ai-chat-raw', (e, d) => cb(d)),
    onEnd: (cb) => ipcRenderer.on('ai-chat-end', (e, d) => cb(d)),
    onError: (cb) => ipcRenderer.on('ai-chat-error', (e, d) => cb(d))
  },
  onOpenFile: (cb) => ipcRenderer.on('open-file-data', (e, data) => cb(data)),
  onCloseRequest: (cb) => ipcRenderer.on('app-close-request', () => cb()),
  closeConfirmed: (proceed) => ipcRenderer.send('close-confirmed', proceed)
});
