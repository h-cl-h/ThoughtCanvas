Unicode true
Name "ThoughtCanvas"
OutFile "..\安装包\ThoughtCanvas-Setup-0.0.9.exe"
InstallDir "$LOCALAPPDATA\Programs\ThoughtCanvas"
RequestExecutionLevel user
SetCompressor /SOLID lzma
Icon "build\icon.ico"
UninstallIcon "build\icon.ico"
VIProductVersion "0.0.9.0"
VIAddVersionKey "ProductName" "ThoughtCanvas"
VIAddVersionKey "FileDescription" "ThoughtCanvas 思维导图"
VIAddVersionKey "FileVersion" "0.0.9.0"
VIAddVersionKey "ProductVersion" "0.0.9.0"
VIAddVersionKey "LegalCopyright" "GPL-3.0-only"
Page directory
Page instfiles
UninstPage uninstConfirm
UninstPage instfiles

Section "安装"
  SetOutPath "$INSTDIR"
  File /r "..\实时预览\ThoughtCanvas V0.0.9 外观与UI\*.*"
  WriteUninstaller "$INSTDIR\卸载.exe"
  CreateDirectory "$SMPROGRAMS\ThoughtCanvas"
  CreateShortcut "$SMPROGRAMS\ThoughtCanvas\ThoughtCanvas.lnk" "$INSTDIR\ThoughtCanvas V0.0.9.exe" "" "$INSTDIR\ThoughtCanvas V0.0.9.exe" 0
  CreateShortcut "$DESKTOP\ThoughtCanvas.lnk" "$INSTDIR\ThoughtCanvas V0.0.9.exe" "" "$INSTDIR\ThoughtCanvas V0.0.9.exe" 0
  WriteRegStr HKCU "Software\Classes\.bmap" "" "ThoughtCanvas.bmap"
  WriteRegStr HKCU "Software\Classes\ThoughtCanvas.bmap" "" "ThoughtCanvas 思维导图"
  WriteRegStr HKCU "Software\Classes\ThoughtCanvas.bmap\DefaultIcon" "" "$INSTDIR\ThoughtCanvas V0.0.9.exe,0"
  WriteRegStr HKCU "Software\Classes\ThoughtCanvas.bmap\shell\open\command" "" "$\"$INSTDIR\ThoughtCanvas V0.0.9.exe$\" $\"%1$\""
SectionEnd

Section "Uninstall"
  Delete "$DESKTOP\ThoughtCanvas.lnk"
  Delete "$SMPROGRAMS\ThoughtCanvas\ThoughtCanvas.lnk"
  RMDir "$SMPROGRAMS\ThoughtCanvas"
  DeleteRegKey HKCU "Software\Classes\ThoughtCanvas.bmap"
  DeleteRegValue HKCU "Software\Classes\.bmap" ""
  RMDir /r "$INSTDIR"
SectionEnd
