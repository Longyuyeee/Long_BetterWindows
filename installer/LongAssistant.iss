#ifndef SourceDir
  #error SourceDir must point to the self-contained publish directory.
#endif

#ifndef AppVersion
  #define AppVersion "1.11.0-rc.5"
#endif

#ifndef NumericVersion
  #define NumericVersion "1.11.0.0"
#endif

[Setup]
AppId={{7B95AC62-8C5A-45E3-B0F0-A77EA8CF318A}
AppName=Long助手
AppVersion={#AppVersion}
AppVerName=Long助手 {#AppVersion}
AppPublisher=Long
AppPublisherURL=https://github.com/Longyuyeee/Long_BetterWindows
AppSupportURL=https://github.com/Longyuyeee/Long_BetterWindows/issues
AppUpdatesURL=https://github.com/Longyuyeee/Long_BetterWindows/releases
DefaultDirName={localappdata}\Programs\LongAssistant
DefaultGroupName=Long助手
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputBaseFilename=LongAssistant-Setup-v{#AppVersion}
SetupIconFile=..\Assets\app.ico
UninstallDisplayIcon={app}\LongBetterWindows.Host.exe
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
UsePreviousAppDir=yes
UsePreviousTasks=yes
SetupLogging=yes
VersionInfoVersion={#NumericVersion}
VersionInfoCompany=Long
VersionInfoDescription=Long助手安装程序
VersionInfoProductName=Long助手
VersionInfoProductVersion={#NumericVersion}
VersionInfoTextVersion={#AppVersion}

[Languages]
Name: "default"; MessagesFile: "compiler:Default.isl"

[Messages]
SetupWindowTitle=安装 - %1
UninstallAppTitle=卸载
UninstallAppFullTitle=卸载 %1
InformationTitle=提示
ConfirmTitle=确认
ErrorTitle=错误
ButtonBack=< 上一步
ButtonNext=下一步 >
ButtonInstall=安装
ButtonOK=确定
ButtonCancel=取消
ButtonFinish=完成
ButtonBrowse=浏览...
ButtonWizardBrowse=浏览...
ClickNext=单击“下一步”继续，或单击“取消”退出安装程序。
WelcomeLabel1=欢迎使用 [name] 安装向导
WelcomeLabel2=将在你的电脑上安装 [name/ver]。%n%n建议在继续前关闭其他应用程序。
WizardSelectDir=选择安装位置
SelectDirDesc=[name] 应安装到哪里？
SelectDirLabel3=安装程序将把 [name] 安装到以下文件夹。
SelectDirBrowseLabel=单击“下一步”继续；如需更改位置，请单击“浏览”。
WizardSelectTasks=选择附加任务
SelectTasksDesc=需要执行哪些附加任务？
SelectTasksLabel2=选择安装 [name] 时需要执行的附加任务，然后单击“下一步”。
WizardReady=准备安装
ReadyLabel1=安装程序已准备好在你的电脑上安装 [name]。
ReadyLabel2a=单击“安装”继续，或单击“上一步”检查或更改设置。
ReadyLabel2b=单击“安装”继续。
ReadyMemoDir=安装位置：
ReadyMemoGroup=开始菜单文件夹：
ReadyMemoTasks=附加任务：
WizardInstalling=正在安装
InstallingLabel=请稍候，安装程序正在安装 [name]。
FinishedHeadingLabel=[name] 安装完成
FinishedLabelNoIcons=安装程序已在你的电脑上安装 [name]。
FinishedLabel=安装程序已在你的电脑上安装 [name]。你可以通过已创建的快捷方式启动它。
ClickFinish=单击“完成”退出安装程序。
ExitSetupTitle=退出安装
ExitSetupMessage=安装尚未完成。如果现在退出，程序将不会被安装。%n%n以后可以重新运行安装程序。%n%n确定退出吗？

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加快捷方式："; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Long助手"; Filename: "{app}\LongBetterWindows.Host.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\Long助手"; Filename: "{app}\LongBetterWindows.Host.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\LongBetterWindows.Host.exe"; Description: "启动 Long助手"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}\LongBetterWindows.Host.exe.WebView2"

[Code]
function InitializeSetup(): Boolean;
begin
  if not IsWin64 then
  begin
    MsgBox('Long助手当前仅支持 64 位 Windows。', mbError, MB_OK);
    Result := False;
    exit;
  end;

  Result := True;
end;
