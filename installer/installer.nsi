; NexGet Download Manager 2 - Windows installer (NSIS, Modern UI 2, 64-bit, per-user: no admin prompt)
; Build:   makensis installer.nsi                 (expects ..\dist with NexGet.exe, tools\, extension\, licenses\)
; Signed:  makensis /DSIGN=sign.cmd installer.nsi  (sign.cmd "<file>" signs the uninstaller and the installer itself)
Target amd64-unicode
!include "MUI2.nsh"
!include "FileFunc.nsh"
!include "LogicLib.nsh"

!define APP      "NexGet Download Manager"
!define VERSION  "2.0.0"
!define EXE      "NexGet.exe"
!define UNKEY    "Software\Microsoft\Windows\CurrentVersion\Uninstall\NexGet"
!define DIST     "..\dist"

!ifdef SIGN
  !finalize '"${SIGN}" "%1"'
  !uninstfinalize '"${SIGN}" "%1"'
!endif

Name "${APP}"
OutFile "out\NexGet-Setup-${VERSION}.exe"
InstallDir "$LOCALAPPDATA\Programs\NexGet"
InstallDirRegKey HKCU "Software\NexGet" "InstallDir"
RequestExecutionLevel user
SetCompressor /SOLID lzma
SetCompressorDictSize 64
BrandingText "${APP} ${VERSION}"
VIProductVersion "${VERSION}.0"
VIAddVersionKey "ProductName" "${APP}"
VIAddVersionKey "FileDescription" "${APP} Setup"
VIAddVersionKey "FileVersion" "${VERSION}"
VIAddVersionKey "ProductVersion" "${VERSION}"
VIAddVersionKey "CompanyName" "NexGet"
VIAddVersionKey "LegalCopyright" "NexGet contributors"

Var LaunchApp

!define MUI_ICON   "..\res\nexget.ico"
!define MUI_UNICON "..\res\nexget.ico"
!define MUI_WELCOMEFINISHPAGE_BITMAP "installer-welcome.bmp"
!define MUI_UNWELCOMEFINISHPAGE_BITMAP "installer-welcome.bmp"
!define MUI_HEADERIMAGE
!define MUI_HEADERIMAGE_RIGHT
!define MUI_HEADERIMAGE_BITMAP "installer-header.bmp"
!define MUI_ABORTWARNING
!define MUI_WELCOMEPAGE_TITLE "Install ${APP}"
!define MUI_WELCOMEPAGE_TEXT "Fast multi-part downloads, pause and resume, a download queue with scheduler, and video downloads from your browser.$\r$\n$\r$\nIf NexGet is running, setup closes it first. Your download list and settings are kept.$\r$\n$\r$\nClick Next to continue."
!define MUI_COMPONENTSPAGE_SMALLDESC
!define MUI_FINISHPAGE_TITLE "NexGet is installed"
!define MUI_FINISHPAGE_TEXT "Next, connect NexGet to your browsers so downloads and videos go to NexGet automatically."
!define MUI_FINISHPAGE_RUN
!define MUI_FINISHPAGE_RUN_TEXT "Start NexGet and connect my browsers"
!define MUI_FINISHPAGE_RUN_FUNCTION FinishRun

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"

Function .onInit
  ; .NET Framework 4.7.2+ is part of Windows 10 (April 2018 update and later) and Windows 11
  ReadRegDWORD $0 HKLM "SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full" "Release"
  ${If} $0 < 461808
    MessageBox MB_YESNO|MB_ICONEXCLAMATION "NexGet needs Microsoft .NET Framework 4.7.2 or later, which this PC doesn't have yet.$\r$\n$\r$\nOpen Microsoft's download page now?" /SD IDNO IDNO +2
      ExecShell "open" "https://dotnet.microsoft.com/download/dotnet-framework/net48"
    Abort
  ${EndIf}
FunctionEnd

Function FinishRun
  StrCpy $LaunchApp 1
FunctionEnd
Function .onGUIEnd
  ${If} $LaunchApp == 1
    Exec '"$INSTDIR\${EXE}" --setup-extension'
  ${EndIf}
FunctionEnd
Function .onInstSuccess
  IfSilent 0 +2
    Exec '"$INSTDIR\${EXE}" --tray'
FunctionEnd

Section "NexGet (required)" SecCore
  SectionIn RO
  nsExec::Exec 'taskkill /F /T /IM ${EXE}'
  Sleep 800
  ; remove everything from older versions (incl. the 1.x Python build); settings live in %APPDATA%\NexGet and stay
  RMDir /r "$INSTDIR\tools"
  RMDir /r "$INSTDIR\extension"
  RMDir /r "$INSTDIR\licenses"
  RMDir /r "$INSTDIR\app"
  RMDir /r "$INSTDIR\Lib"
  RMDir /r "$INSTDIR\DLLs"
  Delete "$INSTDIR\python312.dll"
  Delete "$INSTDIR\python3.dll"
  Delete "$INSTDIR\vcruntime140.dll"
  Delete "$INSTDIR\vcruntime140_1.dll"
  SetOutPath "$INSTDIR"
  File /r "${DIST}\*.*"

  Delete "$SMSTARTUP\NexGet Download Manager.lnk"
  CreateShortCut "$SMPROGRAMS\NexGet Download Manager.lnk" "$INSTDIR\${EXE}" "" "$INSTDIR\${EXE}" 0

  WriteRegStr HKCU "Software\NexGet" "InstallDir" "$INSTDIR"
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  WriteRegStr HKCU "${UNKEY}" "DisplayName" "${APP}"
  WriteRegStr HKCU "${UNKEY}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKCU "${UNKEY}" "Publisher" "NexGet"
  WriteRegStr HKCU "${UNKEY}" "DisplayIcon" "$INSTDIR\${EXE}"
  WriteRegStr HKCU "${UNKEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${UNKEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegStr HKCU "${UNKEY}" "QuietUninstallString" '"$INSTDIR\Uninstall.exe" /S'
  WriteRegDWORD HKCU "${UNKEY}" "NoModify" 1
  WriteRegDWORD HKCU "${UNKEY}" "NoRepair" 1
  ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
  WriteRegDWORD HKCU "${UNKEY}" "EstimatedSize" $0
SectionEnd

Section "Desktop shortcut" SecDesktop
  CreateShortCut "$DESKTOP\NexGet Download Manager.lnk" "$INSTDIR\${EXE}" "" "$INSTDIR\${EXE}" 0
SectionEnd

Section "Start with Windows (in the tray)" SecStartup
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "NexGet" '"$INSTDIR\${EXE}" --tray'
SectionEnd

!insertmacro MUI_FUNCTION_DESCRIPTION_BEGIN
  !insertmacro MUI_DESCRIPTION_TEXT ${SecCore}    "NexGet, its download engines and the browser add-on."
  !insertmacro MUI_DESCRIPTION_TEXT ${SecDesktop} "Put a NexGet icon on the desktop."
  !insertmacro MUI_DESCRIPTION_TEXT ${SecStartup} "Start NexGet in the tray when you sign in, so browser downloads are always caught."
!insertmacro MUI_FUNCTION_DESCRIPTION_END

Section "Uninstall"
  nsExec::Exec 'taskkill /F /T /IM ${EXE}'
  Sleep 800
  nsExec::Exec '"$INSTDIR\${EXE}" --cleanup'
  Delete "$SMPROGRAMS\NexGet Download Manager.lnk"
  Delete "$DESKTOP\NexGet Download Manager.lnk"
  DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "NexGet"
  DeleteRegKey HKCU "${UNKEY}"
  DeleteRegKey HKCU "Software\NexGet"
  RMDir /r "$INSTDIR"
  IfSilent done
  MessageBox MB_YESNO|MB_ICONQUESTION "Also delete NexGet's settings and download list?$\r$\n(Downloaded files are not touched.)" /SD IDNO IDNO done
    RMDir /r "$APPDATA\NexGet"
  done:
SectionEnd
