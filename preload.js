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
  onOpenFile: (cb) => ipcRenderer.on('open-file-data', (e, data) => cb(data)),
  onCloseRequest: (cb) => ipcRenderer.on('app-close-request', () => cb()),
  closeConfirmed: (proceed) => ipcRenderer.send('close-confirmed', proceed)
});
