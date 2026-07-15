Unicode true
Name "BMAP 文本框样式编辑器"
OutFile "..\安装包\BMAP-Text-Style-Editor-Setup-0.0.1.exe"
Icon "${__FILEDIR__}\assets\icon.ico"
UninstallIcon "${__FILEDIR__}\assets\icon.ico"
InstallDir "$LOCALAPPDATA\Programs\BMAP Text Style Editor"
RequestExecutionLevel user
SetCompressor /SOLID lzma
Page directory
Page instfiles
UninstPage uninstConfirm
UninstPage instfiles

Section "安装"
  SetOutPath "$INSTDIR"
  File "bin\Release\net8.0-windows\win-x64\publish\BMAP文本框样式编辑器.exe"
  File "LICENSE"
  File "THIRD_PARTY_NOTICES.md"
  SetOutPath "$INSTDIR\THIRD_PARTY_LICENSES"
  File /r "THIRD_PARTY_LICENSES\*.*"
  SetOutPath "$INSTDIR"
  WriteUninstaller "$INSTDIR\卸载.exe"
  CreateDirectory "$SMPROGRAMS\BMAP 文本框样式编辑器"
  CreateShortcut "$SMPROGRAMS\BMAP 文本框样式编辑器\BMAP 文本框样式编辑器.lnk" "$INSTDIR\BMAP文本框样式编辑器.exe"
  CreateShortcut "$DESKTOP\BMAP 文本框样式编辑器.lnk" "$INSTDIR\BMAP文本框样式编辑器.exe"
SectionEnd

Section "Uninstall"
  Delete "$DESKTOP\BMAP 文本框样式编辑器.lnk"
  Delete "$SMPROGRAMS\BMAP 文本框样式编辑器\BMAP 文本框样式编辑器.lnk"
  RMDir "$SMPROGRAMS\BMAP 文本框样式编辑器"
  Delete "$INSTDIR\BMAP文本框样式编辑器.exe"
  Delete "$INSTDIR\LICENSE"
  Delete "$INSTDIR\THIRD_PARTY_NOTICES.md"
  RMDir /r "$INSTDIR\THIRD_PARTY_LICENSES"
  Delete "$INSTDIR\卸载.exe"
  RMDir "$INSTDIR"
SectionEnd
