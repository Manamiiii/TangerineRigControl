; SPDX-License-Identifier: GPL-3.0-only
; Copyright (c) 2026 Manamiiii
Unicode true
RequestExecutionLevel user
SetCompressor /SOLID zlib
!include MUI2.nsh
!include LogicLib.nsh
!include x64.nsh
!include WinVer.nsh
!include WordFunc.nsh
!include Win\COM.nsh

Name "${APP_NAME}"
OutFile "${OUTPUT_FILE}"
!define LEGACY_DIR "$LOCALAPPDATA\Programs\${APP_NAME}"
!define SUITE_DIR "$LOCALAPPDATA\Programs\Tangerine"
!define DEFAULT_DIR "${SUITE_DIR}\${APP_NAME}"
InstallDir "${DEFAULT_DIR}"
VIProductVersion "${NUMERIC_VERSION}"
VIAddVersionKey /LANG=1033 "ProductName" "${APP_NAME}"
VIAddVersionKey /LANG=1033 "FileDescription" "${APP_NAME} per-user Setup"
VIAddVersionKey /LANG=1033 "FileVersion" "${NUMERIC_VERSION}"
VIAddVersionKey /LANG=1033 "LegalCopyright" "Copyright 2026 Manamiiii; GPL-3.0-only"
Icon "${PAYLOAD}\source\src\TangerineRigControl\Assets\app.ico"
!define MUI_ICON "${PAYLOAD}\source\src\TangerineRigControl\Assets\app.ico"
!define MUI_UNICON "${PAYLOAD}\source\src\TangerineRigControl\Assets\app.ico"
!define MUI_ABORTWARNING
!define MUI_WELCOMEPAGE_TEXT "仅为当前 Windows 用户安装，不需要管理员权限。$\r$\n$\r$\n程序位于本用户的 Programs 目录；升级和卸载默认保留 LocalAppData 中的配置。$\r$\n$\r$\n请先从托盘退出 TangerineRigControl，再继续安装。"
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_LICENSE "${LICENSE_PAGE}"
!insertmacro MUI_PAGE_INSTFILES
!define MUI_FINISHPAGE_TEXT "安装完成。请从开始菜单打开程序。升级和卸载默认保留本机配置。"
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "SimpChinese"
!insertmacro MUI_LANGUAGE "English"

!define UNINSTALL_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_GUID}"
Var InstanceMutex
Var DataLock
Var RegisteredDir

!macro Fail CODE MESSAGE
    MessageBox MB_OK|MB_ICONEXCLAMATION "${MESSAGE}" /SD IDOK
    SetErrorLevel ${CODE}
    Quit
!macroend

; Both processes retain the locks until exit. No process termination or reboot deletion.
!macro AcquireLocks
    System::Call 'kernel32::CreateMutexW(p 0, i 0, w "Local\${APP_NAME}.SingleInstance") p .r0 ?e'
    Pop $1
    StrCpy $InstanceMutex $0
    ${If} $0 == 0
    ${OrIf} $1 == 183
        !insertmacro Fail 20 "程序或另一个安装器正在运行。请从托盘退出程序，完成其他安装操作后重试；不会强制结束进程。"
    ${EndIf}
    CreateDirectory "$LOCALAPPDATA\${APP_NAME}"
    System::Call 'kernel32::CreateFileW(w "$LOCALAPPDATA\${APP_NAME}\.lifecycle.lock", i 0xC0000000, i 0, p 0, i 4, i 0x80, p 0) p .r0'
    StrCpy $DataLock $0
    ${If} $0 == -1
        !insertmacro Fail 20 "配置目录正在使用或不可写。请退出本用户其他会话中的程序，再检查目录权限后重试。"
    ${EndIf}
!macroend

!macro CheckDirectory PATH
    System::Call 'kernel32::GetFileAttributesW(w "${PATH}") i .r0'
    ${If} $0 != -1
        IntOp $1 $0 & 0x400
        ${If} $1 != 0
            !insertmacro Fail 22 "安装位置含有目录联接或符号链接，请核对目录。"
        ${EndIf}
    ${EndIf}
!macroend

!macro ResolveInstallPath
    ReadRegStr $RegisteredDir HKCU "${UNINSTALL_KEY}" "InstallLocation"
    ${If} $RegisteredDir == ""
        ClearErrors
        EnumRegValue $0 HKCU "${UNINSTALL_KEY}" 0
        ${IfNot} ${Errors}
            !insertmacro Fail 22 "已有卸载登记缺少安装位置，已停止操作。"
        ${EndIf}
        !ifdef LEGACY_FIXTURE_LAYOUT
            StrCpy $INSTDIR "${LEGACY_DIR}"
        !else
            StrCpy $INSTDIR "${DEFAULT_DIR}"
        !endif
    ${Else}
        ${If} $RegisteredDir != "${LEGACY_DIR}"
        ${AndIf} $RegisteredDir != "${DEFAULT_DIR}"
            !insertmacro Fail 22 "卸载登记指向未知目录，已停止操作。"
        ${EndIf}
        ReadRegStr $0 HKCU "${UNINSTALL_KEY}" "UninstallString"
        ${If} $0 != '$\"$RegisteredDir\Uninstall.exe$\"'
            !insertmacro Fail 22 "卸载命令与登记路径不匹配，已停止操作。"
        ${EndIf}
        StrCpy $INSTDIR $RegisteredDir
    ${EndIf}
    !insertmacro CheckDirectory "$LOCALAPPDATA\Programs"
    ${If} $INSTDIR == "${DEFAULT_DIR}"
        !insertmacro CheckDirectory "${SUITE_DIR}"
    ${EndIf}
    !insertmacro CheckDirectory "$INSTDIR"
!macroend

; Read the link's raw target without resolving or executing it.
!macro CheckShortcut
    StrCpy $R0 ""
    ${If} ${FileExists} "$SMPROGRAMS\${APP_NAME}.lnk"
        !insertmacro ComHlpr_CreateInProcInstance ${CLSID_ShellLink} ${IID_IShellLink} r0 ""
        ${If} $0 P<> 0
            ${IUnknown::QueryInterface} $0 '("${IID_IPersistFile}",.r1)'
            ${If} $1 P<> 0
                ${IPersistFile::Load} $1 '("$SMPROGRAMS\${APP_NAME}.lnk",0).r3'
                ${If} $3 == 0
                    ${IShellLink::GetPath} $0 '(.r4,${NSIS_MAX_STRLEN},0,4).r3'
                    ${If} $3 == 0
                        ${IShellLink::GetArguments} $0 '(.r5,${NSIS_MAX_STRLEN}).r3'
                        ${If} $3 == 0
                        ${AndIf} $5 == ""
                            StrCpy $R0 $4
                        ${EndIf}
                    ${EndIf}
                ${EndIf}
                ${IUnknown::Release} $1 ""
            ${EndIf}
            ${IUnknown::Release} $0 ""
        ${EndIf}
    ${EndIf}
!macroend

Function .onInit
    SetShellVarContext current
    SetRegView 64
    ${IfNot} ${IsNativeAMD64}
        !insertmacro Fail 21 "此安装包需要 Windows 10/11 x64。"
    ${EndIf}
    ${IfNot} ${AtLeastWin10}
        !insertmacro Fail 21 "此安装包需要 Windows 10/11 x64。"
    ${EndIf}
    ReadRegDWORD $0 HKLM "Software\Microsoft\NET Framework Setup\NDP\v4\Full" "Release"
    ${If} $0 < 528040
        !insertmacro Fail 21 "需要 .NET Framework 4.8 或 4.8.1。请先安装 Microsoft 官方运行环境；此安装器不会提权或自动下载组件。"
    ${EndIf}
    !insertmacro AcquireLocks
    !insertmacro ResolveInstallPath
    !insertmacro CheckShortcut
    ${If} ${FileExists} "$SMPROGRAMS\${APP_NAME}.lnk"
    ${AndIf} $R0 != "$INSTDIR\TangerineRigControl.exe"
        !insertmacro Fail 22 "同名开始菜单入口属于其他位置，已停止安装。"
    ${EndIf}
    !include "${DIRECTORY_CHECKS}"
    ReadRegStr $0 HKCU "${UNINSTALL_KEY}" "DisplayVersion"
    ${If} $0 != ""
        ${VersionCompare} $0 "${NUMERIC_VERSION}" $1
        ${If} $1 == 1
            !insertmacro Fail 23 "已安装更新版本。为保护配置，请使用相同或更新版本的安装包。"
        ${EndIf}
    ${EndIf}
FunctionEnd

Section "Install"
    ClearErrors
    SetOutPath "$INSTDIR"
    ${If} ${Errors}
        !insertmacro Fail 24 "安装目录不可写，请检查当前用户的目录权限。"
    ${EndIf}
    File /r "${PAYLOAD}\*"
    ${If} ${Errors}
        !insertmacro Fail 24 "程序文件写入失败。请检查磁盘空间和目录权限，保留配置后重新运行安装器。"
    ${EndIf}
    WriteUninstaller "$INSTDIR\Uninstall.exe"
    CreateShortcut "$SMPROGRAMS\${APP_NAME}.lnk" "$INSTDIR\TangerineRigControl.exe" "" "$INSTDIR\TangerineRigControl.exe" 0
    WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayName" "${APP_NAME}"
    WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayVersion" "${NUMERIC_VERSION}"
    WriteRegStr HKCU "${UNINSTALL_KEY}" "Publisher" "Manamiiii"
    WriteRegStr HKCU "${UNINSTALL_KEY}" "InstallLocation" "$INSTDIR"
    WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayIcon" "$INSTDIR\TangerineRigControl.exe"
    WriteRegStr HKCU "${UNINSTALL_KEY}" "UninstallString" '$\"$INSTDIR\Uninstall.exe$\"'
    WriteRegStr HKCU "${UNINSTALL_KEY}" "QuietUninstallString" '$\"$INSTDIR\Uninstall.exe$\" /S'
    WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoModify" 1
    WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoRepair" 1
    ${If} ${Errors}
        !insertmacro Fail 24 "安装信息写入失败，请检查当前用户的权限后重新安装。"
    ${EndIf}
SectionEnd

Function un.onInit
    SetShellVarContext current
    SetRegView 64
    !insertmacro AcquireLocks
    ReadRegStr $0 HKCU "${UNINSTALL_KEY}" "InstallLocation"
    ${If} $0 != $INSTDIR
        !insertmacro Fail 22 "安装记录与位置不匹配，已停止卸载。"
    ${EndIf}
    !insertmacro ResolveInstallPath
    !include "${DIRECTORY_CHECKS}"
FunctionEnd

Section "Uninstall"
    !insertmacro CheckShortcut
    ; Generated exact file list: never recursively delete user-added files or data.
    !include "${REMOVE_MANIFEST}"
    ReadRegStr $0 HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "${APP_NAME}"
    ${If} $0 == '$\"$INSTDIR\TangerineRigControl.exe$\" --startup'
    ${OrIf} $0 == '$\"$INSTDIR\TangerineRigControl.exe$\"'
        DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "${APP_NAME}"
    ${EndIf}
    ${If} $R0 == "$INSTDIR\TangerineRigControl.exe"
        Delete "$SMPROGRAMS\${APP_NAME}.lnk"
    ${EndIf}
    DeleteRegKey HKCU "${UNINSTALL_KEY}"
    Delete "$INSTDIR\Uninstall.exe"
    RMDir "$INSTDIR"
    ; $LOCALAPPDATA\${APP_NAME}, including settings and backups, is deliberately retained.
SectionEnd
