; LAN Skin Tester installer (NSIS). build.sh runs:
;   makensis -DVERSION=x.y.z -DEXE=<path to SkinTesterCSGO.exe> -DOUT=<setup.exe> installer/LanSkinTester.nsi
; Per-user install (no admin prompt) into %LocalAppData%\Programs, so the launcher can update itself.

Unicode true
!include "MUI2.nsh"

!ifndef VERSION
  !define VERSION "0.0.0"
!endif
!ifndef EXE
  !define EXE "..\build\SkinTesterCSGO.exe"
!endif
!ifndef OUT
  !define OUT "..\build\SkinTesterCSGOSetup.exe"
!endif

!define APPNAME "Skin Tester CS:GO"
!define UNINSTKEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\SkinTesterCSGO"

Name "${APPNAME}"
OutFile "${OUT}"
InstallDir "$LOCALAPPDATA\Programs\${APPNAME}"
InstallDirRegKey HKCU "${UNINSTKEY}" "InstallLocation"
RequestExecutionLevel user
SetCompressor /SOLID lzma
BrandingText "${APPNAME} ${VERSION}"

VIProductVersion "${VERSION}.0"
VIAddVersionKey "ProductName" "${APPNAME}"
VIAddVersionKey "FileDescription" "${APPNAME} installer"
VIAddVersionKey "FileVersion" "${VERSION}"
VIAddVersionKey "ProductVersion" "${VERSION}"
VIAddVersionKey "LegalCopyright" "Cs-plugin"

!define MUI_ICON "..\src\Launcher\app.ico"
!define MUI_UNICON "..\src\Launcher\app.ico"
!define MUI_WELCOMEFINISHPAGE_BITMAP "welcome.bmp"
!define MUI_UNWELCOMEFINISHPAGE_BITMAP "welcome.bmp"
!define MUI_ABORTWARNING
!define MUI_WELCOMEPAGE_TITLE "Install ${APPNAME}"
!define MUI_WELCOMEPAGE_TEXT "Preview CS2 skins, play with bots and friends on your own local server.$\r$\n$\r$\nInstalls for your Windows user only (no admin needed). The launcher keeps itself up to date."
!define MUI_FINISHPAGE_RUN "$INSTDIR\SkinTesterCSGO.exe"
!define MUI_FINISHPAGE_RUN_TEXT "Start ${APPNAME}"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"

Section "${APPNAME}" SecMain
  SectionIn RO
  SetOutPath "$INSTDIR"
  File "/oname=SkinTesterCSGO.exe" "${EXE}"
  WriteUninstaller "$INSTDIR\Uninstall.exe"

  CreateShortcut "$SMPROGRAMS\${APPNAME}.lnk" "$INSTDIR\SkinTesterCSGO.exe"

  WriteRegStr HKCU "${UNINSTKEY}" "DisplayName" "${APPNAME}"
  WriteRegStr HKCU "${UNINSTKEY}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKCU "${UNINSTKEY}" "Publisher" "Cs-plugin"
  WriteRegStr HKCU "${UNINSTKEY}" "DisplayIcon" "$INSTDIR\SkinTesterCSGO.exe"
  WriteRegStr HKCU "${UNINSTKEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${UNINSTKEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegStr HKCU "${UNINSTKEY}" "URLInfoAbout" "https://github.com/V3rkku1/Cs-plugin"
  WriteRegDWORD HKCU "${UNINSTKEY}" "NoModify" 1
  WriteRegDWORD HKCU "${UNINSTKEY}" "NoRepair" 1
SectionEnd

Section "Desktop shortcut" SecDesktop
  CreateShortcut "$DESKTOP\${APPNAME}.lnk" "$INSTDIR\SkinTesterCSGO.exe"
SectionEnd

!insertmacro MUI_FUNCTION_DESCRIPTION_BEGIN
  !insertmacro MUI_DESCRIPTION_TEXT ${SecMain} "The launcher (installer, skin picker, server, friends)."
  !insertmacro MUI_DESCRIPTION_TEXT ${SecDesktop} "A shortcut on your desktop."
!insertmacro MUI_FUNCTION_DESCRIPTION_END

Section "Uninstall"
  ; Your skins, loadouts and settings (%LocalAppData%\LanSkinTester, %AppData%\LanSkinTester) are kept.
  ; Mods in the CS2 folder are removed with the launcher's Setup page > Uninstall Everything.
  Delete "$INSTDIR\SkinTesterCSGO.exe"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR"
  Delete "$SMPROGRAMS\${APPNAME}.lnk"
  Delete "$DESKTOP\${APPNAME}.lnk"
  DeleteRegKey HKCU "${UNINSTKEY}"
SectionEnd
