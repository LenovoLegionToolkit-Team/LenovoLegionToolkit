#include "InnoDependencies\Scripts\install_dotnet.iss"

#define MyAppName "Lenovo Legion Toolkit"
#define MyAppNameCompact "LenovoLegionToolkit"
#define MyAppPublisher "LenovoLegionToolkit-Team"
#define MyAppURL "https://github.com/LenovoLegionToolkit-Team/LenovoLegionToolkit"
#define MyAppExeName "Lenovo Legion Toolkit.exe"
#define MyAppGitHub "https://github.com/LenovoLegionToolkit-Team/LenovoLegionToolkit"
#define MyAppLegionDiscord "https://discord.com/invite/legionseries"
#define MyAppLOQDiscord "https://discord.gg/3GKzQtwdNf"
#define MyAppOfficialDiscord "https://discord.gg/TB3ER8ZVdt"
#define MyAppCopyright "© 2026 Bartosz Cichecki, Kaguya, and Dr. Skinner"

#ifndef MyAppVersion
  #define MyAppVersion "0.0.1"
#endif

[Setup]
UsePreviousAppDir=no
UsedUserAreasWarning=false
AppId={{0C37B9AC-9C3D-4302-8ABB-125C7C7D83D5}
AppMutex=LenovoLegionToolkit_Mutex_6efcc882-924c-4cbc-8fec-f45c25696f98
CloseApplications=force
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppCopyright={#MyAppCopyright}
VersionInfoVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={commonpf}\{#MyAppNameCompact}
DisableProgramGroupPage=yes
LicenseFile=LICENSE
PrivilegesRequired=admin
OutputBaseFilename=LenovoLegionToolkitSetup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
OutputDir=build_installer
ArchitecturesInstallIn64BitMode=x64compatible
WizardSmallImageFile=InnoDependencies\Images\logo.png
SetupIconFile=InnoDependencies\Images\setup_icon.ico
SetupLogging=yes

[Code]
var
  PathWarningBox: TPanel;

function InitializeSetup: Boolean;
begin
  InstallDotNetDesktopRuntime;
  Result := True;
end;

function IsPathSecure(Path: string): Boolean;
var
  PF64, PF32: string;
  PathUpper, PF64Upper, PF32Upper: string;
begin
  PF64 := ExpandConstant('{commonpf}');
  PF32 := ExpandConstant('{commonpf32}');
  PathUpper := Uppercase(Path);
  PF64Upper := Uppercase(PF64);
  PF32Upper := Uppercase(PF32);
  
  Result := (PathUpper = PF64Upper) or 
            (PathUpper = PF32Upper) or 
            (Pos(PF64Upper + '\', PathUpper) = 1) or 
            (Pos(PF32Upper + '\', PathUpper) = 1);
end;

procedure DirEditChange(Sender: TObject);
begin
  PathWarningBox.Visible := not IsPathSecure(WizardForm.DirEdit.Text);
end;

procedure InitializeWizard;
var
  WarningIconImage: TBitmapImage;
  WarningText: TNewStaticText;
  Sizes: TArrayOfInteger;
  DummyResult: Boolean;
begin
  PathWarningBox := TPanel.Create(WizardForm);
  PathWarningBox.Parent := WizardForm.SelectDirPage;
  PathWarningBox.Left := WizardForm.DirEdit.Left;
  PathWarningBox.Top := WizardForm.DirEdit.Top + WizardForm.DirEdit.Height + ScaleY(16);
  PathWarningBox.Width := WizardForm.DirEdit.Width;
  PathWarningBox.Height := ScaleY(72);
  PathWarningBox.BevelOuter := bvNone;
  PathWarningBox.Color := $00F0F0FF;
  PathWarningBox.ParentBackground := False;

  WarningIconImage := TBitmapImage.Create(WizardForm);
  WarningIconImage.Parent := PathWarningBox;
  WarningIconImage.Left := ScaleX(10);
  WarningIconImage.Top := ScaleY(20);
  WarningIconImage.Width := ScaleX(24);
  WarningIconImage.Height := ScaleY(24);
  WarningIconImage.BackColor := $00F0F0FF;

  SetArrayLength(Sizes, 2);
  Sizes[0] := 24;
  Sizes[1] := 32;
  DummyResult := InitializeBitmapImageFromStockIcon(WarningIconImage, 78, $00F0F0FF, Sizes);

  WarningText := TNewStaticText.Create(WizardForm);
  WarningText.Parent := PathWarningBox;
  WarningText.Left := ScaleX(44);
  WarningText.Top := ScaleY(10);
  WarningText.Width := PathWarningBox.Width - ScaleX(54);
  WarningText.Height := PathWarningBox.Height - ScaleY(16);
  WarningText.WordWrap := True;
  WarningText.Font.Color := clBlack;
  WarningText.Caption := ExpandConstant('{cm:SecurePathPageNote}');

  PathWarningBox.Visible := not IsPathSecure(WizardForm.DirEdit.Text);

  WizardForm.DirEdit.OnChange := @DirEditChange;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if CurPageID = wpSelectDir then
  begin
    if not IsPathSecure(WizardDirValue) then
    begin
      if SuppressibleMsgBox(ExpandConstant('{cm:SecurePathWarning}'), mbError, MB_YESNO or MB_DEFBUTTON2, idYes) = idNo then
      begin
        Result := False;
      end;
    end;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
begin
  if CurUninstallStep = usUninstall then
  begin
    // Unregister Package
    Exec('powershell.exe', '-NoProfile -ExecutionPolicy Bypass -Command "Get-AppxPackage -Name ''eef45acd-2cf3-4d7d-9d33-92f37c74cc31'' | Remove-AppxPackage"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    // Remove Certificate from LocalMachine Root (Required for UIAccess)
    Exec('powershell.exe', '-NoProfile -ExecutionPolicy Bypass -Command "Get-ChildItem Cert:\LocalMachine\Root | Where-Object { $_.Subject -match ''LenovoLegionToolkit'' } | Remove-Item -Force -ErrorAction SilentlyContinue"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    // Remove Certificate from LocalMachine TrustedPeople (Required for MSIX)
    Exec('powershell.exe', '-NoProfile -ExecutionPolicy Bypass -Command "Get-ChildItem Cert:\LocalMachine\TrustedPeople | Where-Object { $_.Subject -match ''LenovoLegionToolkit'' } | Remove-Item -Force -ErrorAction SilentlyContinue"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    // Remove Certificate from CurrentUser (just in case, covers dev BAT installs)
    Exec('powershell.exe', '-NoProfile -ExecutionPolicy Bypass -Command "Get-ChildItem Cert:\CurrentUser\TrustedPeople | Where-Object { $_.Subject -match ''LenovoLegionToolkit'' } | Remove-Item -Force -ErrorAction SilentlyContinue"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec('powershell.exe', '-NoProfile -ExecutionPolicy Bypass -Command "Get-ChildItem Cert:\CurrentUser\Root | Where-Object { $_.Subject -match ''LenovoLegionToolkit'' } | Remove-Item -Force -ErrorAction SilentlyContinue"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end
  else if CurUninstallStep = usPostUninstall then
  begin
    if SuppressibleMsgBox(ExpandConstant('{cm:UninstallKeepSettings}'), mbConfirmation, MB_YESNO, idNo) = idYes then
    begin
        DelTree(ExpandConstant('{localappdata}\{#MyAppNameCompact}'), True, True, True);
    end;
  end;
end;

procedure OpenBrowser(Url: string);
var
  ResultCode: Integer;
begin
  ShellExec('open', Url, '', '', SW_SHOWNORMAL, ewNoWait, ResultCode);
end;

procedure OfficialDiscordLinkClick(Sender: TObject);
begin
  OpenBrowser('{#MyAppOfficialDiscord}');
end;

procedure GitHubLinkClick(Sender: TObject);
begin
  OpenBrowser('{#MyAppGitHub}');
end;

procedure LegionDiscordLinkClick(Sender: TObject);
begin
  OpenBrowser('{#MyAppLegionDiscord}');
end;

procedure LOQDiscordLinkClick(Sender: TObject);
begin
  OpenBrowser('{#MyAppLOQDiscord}');
end;

procedure CurPageChanged(CurPageID: Integer);
var
  OfficialDiscordLink, GitHubLink, LegionDiscordLink, LOQDiscordLink: TNewStaticText;
  Offset: Integer;
begin
  if CurPageID = wpFinished then
  begin
    Offset := WizardForm.FinishedLabel.Top + WizardForm.FinishedLabel.Height + ScaleY(4);

    GitHubLink := TNewStaticText.Create(WizardForm);
    GitHubLink.Parent := WizardForm.FinishedPage;
    GitHubLink.Top := Offset;
    GitHubLink.Left := WizardForm.FinishedLabel.Left;
    GitHubLink.Caption := ExpandConstant('{cm:VisitGitHub}');
    GitHubLink.Font.Color := clBlue;
    GitHubLink.Font.Style := [fsUnderline];
    GitHubLink.Cursor := crHand;
    GitHubLink.OnClick := @GitHubLinkClick;

    OfficialDiscordLink := TNewStaticText.Create(WizardForm);
    OfficialDiscordLink.Parent := WizardForm.FinishedPage;
    OfficialDiscordLink.Top := GitHubLink.Top + GitHubLink.Height + ScaleY(8);
    OfficialDiscordLink.Left := WizardForm.FinishedLabel.Left;
    OfficialDiscordLink.Caption := ExpandConstant('{cm:JoinOfficialDiscord}');
    OfficialDiscordLink.Font.Color := clBlue;
    OfficialDiscordLink.Font.Style := [fsUnderline];
    OfficialDiscordLink.Cursor := crHand;
    OfficialDiscordLink.OnClick := @OfficialDiscordLinkClick;

    LegionDiscordLink := TNewStaticText.Create(WizardForm);
    LegionDiscordLink.Parent := WizardForm.FinishedPage;
    LegionDiscordLink.Top := OfficialDiscordLink.Top + OfficialDiscordLink.Height + ScaleY(8);
    LegionDiscordLink.Left := WizardForm.FinishedLabel.Left;
    LegionDiscordLink.Caption := ExpandConstant('{cm:JoinLegionDiscord}');
    LegionDiscordLink.Font.Color := clBlue;
    LegionDiscordLink.Font.Style := [fsUnderline];
    LegionDiscordLink.Cursor := crHand;
    LegionDiscordLink.OnClick := @LegionDiscordLinkClick;

    LOQDiscordLink := TNewStaticText.Create(WizardForm);
    LOQDiscordLink.Parent := WizardForm.FinishedPage;
    LOQDiscordLink.Top := LegionDiscordLink.Top + LegionDiscordLink.Height + ScaleY(8);
    LOQDiscordLink.Left := WizardForm.FinishedLabel.Left;
    LOQDiscordLink.Caption := ExpandConstant('{cm:JoinLOQDiscord}');
    LOQDiscordLink.Font.Color := clBlue;
    LOQDiscordLink.Font.Style := [fsUnderline];
    LOQDiscordLink.Cursor := crHand;
    LOQDiscordLink.OnClick := @LOQDiscordLinkClick;

    if WizardForm.RunList.Visible then
      WizardForm.RunList.Top := LOQDiscordLink.Top + LOQDiscordLink.Height + ScaleY(12);
  end;
end;

[CustomMessages]
JoinLOQDiscord=Join LOQ Series Discord Community
JoinLegionDiscord=Join Legion Series Discord Community
JoinOfficialDiscord=Join Official Discord Community
SecurePathPageNote=Installing outside of a protected folder such as Program Files will disable UIAccess features, including OSD overlay and notification elevation, on Windows 11 23H2 and below. On newer Windows versions, this will cause Windows to block the application from launching entirely.
SecurePathWarning=Installing outside of a protected folder such as Program Files will disable features relying on UIAccess, including OSD overlay and notification elevation, on Windows 11 23H2 and below. On newer Windows versions, this will cause Windows to block the application from launching entirely due to stricter security policies.%n%nAre you sure you want to install to this folder anyway?
UninstallKeepSettings=Do you want to delete all Lenovo Legion Toolkit settings, customizations, and configurations?
VisitGitHub=Visit GitHub Repository

ar.JoinLOQDiscord=الانضمام إلى مجتمع Discord لسلسلة LOQ
ar.JoinLegionDiscord=الانضمام إلى مجتمع Discord لسلسلة Legion
ar.JoinOfficialDiscord=الانضمام إلى مجتمع Discord الرسمي
ar.SecurePathPageNote=التثبيت خارج مجلد محمي مثل Program Files سيؤدي إلى تعطيل ميزات UIAccess، بما في ذلك تراكب OSD وترقية الإشعارات، على Windows 11 23H2 والإصدارات الأقدم. في إصدارات Windows الأحدث، سيؤدي هذا إلى قيام Windows بحظر تشغيل التطبيق بالكامل.
ar.SecurePathWarning=التثبيت خارج مجلد محمي مثل Program Files سيؤدي إلى تعطيل الميزات التي تعتمد على UIAccess، بما في ذلك تراكب OSD وترقية الإشعارات، على Windows 11 23H2 والإصدارات الأقدم. في إصدارات Windows الأحدث, سيؤدي هذا إلى قيام Windows بحظر تشغيل التطبيق بالكامل بسبب سياسات الأمان الأكثر صرامة.%n%nهل أنت متأكد من أنك تريد التثبيت في هذا المجلد على أي حال؟
ar.UninstallKeepSettings=هل تريد حذف جميع إعدادات وتخصيصات وتكوينات Lenovo Legion Toolkit؟
ar.VisitGitHub=زيارة مستودع GitHub

bg.JoinLOQDiscord=Присъединете се към Discord общността на серията LOQ
bg.JoinLegionDiscord=Присъединете се към Discord общността на серията Legion
bg.JoinOfficialDiscord=Присъединете се към официалната Discord общност
bg.SecurePathPageNote=Инсталирането извън защитена папка като Program Files ще деактивира функциите на UIAccess, включително OSD наслагването и повишаването на известията, в Windows 11 23H2 и по-стари версии. При по-нови версии на Windows това ще накара Windows да блокира стартирането на приложението изцяло.
bg.SecurePathWarning=Инсталирането извън защитена папка като Program Files ще деактивира функциите, разчитащи на UIAccess, включително OSD наслагването и повишаването на известията, в Windows 11 23H2 и по-стари версии. При по-нови версии на Windows това ще накара Windows да блокира стартирането на приложението изцяло поради по-строги политики за сигурност.%n%nСигурни ли сте, че искате да инсталирате в тази папка въпреки това?
bg.UninstallKeepSettings=Искате ли да изтриете всички настройки, персонализации и конфигурации на Lenovo Legion Toolkit?
bg.VisitGitHub=Посетете хранилището в GitHub

bs.JoinLOQDiscord=Pridružite se Discord zajednici LOQ serije
bs.JoinLegionDiscord=Pridružite se Discord zajednici Legion serije
bs.JoinOfficialDiscord=Pridružite se zvaničnoj Discord zajednici
bs.SecurePathPageNote=Instalacija izvan zaštićenog direktorija kao što je Program Files onemogućit će UIAccess funkcije, uključujući OSD prikaz i povišenje obavještenja, na Windows 11 23H2 i starijim verzijama. Na novijim verzijama Windowsa, ovo će uzrokovati da Windows potpuno blokira pokretanje aplikacije.
bs.SecurePathWarning=Instalacija izvan zaštićenog direktorija kao što je Program Files onemogućit će funkcije koje se oslanjaju na UIAccess, uključujući OSD prikaz i povišenje obavještenja, na Windows 11 23H2 i starijim verzijama. Na novijim verzijama Windowsa, ovo će uzrokovati da Windows potpuno blokira pokretanje aplikacije zbog strožih sigurnosnih pravila.%n%nJeste li sigurni da ipak želite instalirati u ovaj direktorij?
bs.UninstallKeepSettings=Da li želite obrisati sve postavke, prilagođavanja i konfiguracije za Lenovo Legion Toolkit?
bs.VisitGitHub=Posjetite GitHub repozitorij

cs.JoinLOQDiscord=Připojit se k Discord komunitě řady LOQ
cs.JoinLegionDiscord=Připojit se k Discord komunitě řady Legion
cs.JoinOfficialDiscord=Připojit se k oficiální Discord komunitě
cs.SecurePathPageNote=Instalace mimo chráněnou složku, jako je Program Files, zakáže funkce UIAccess, včetně OSD překryvu a zvýšení oprávnění oznámení, v systému Windows 11 23H2 a starších. Na novějších verzích systému Windows to způsobí, že Windows aplikaci zcela zablokuje.
cs.SecurePathWarning=Instalace mimo chráněnou složku, jako je Program Files, zakáže funkce závislé na UIAccess, včetně OSD překryvu a zvýšení oprávnění oznámení, v systému Windows 11 23H2 a starších. Na novějších verzích systému Windows to způsobí, že Windows aplikaci zcela zablokuje z důvodu přísnějších bezpečnostních zásad.%n%nOpravdu chcete přesto instalovat do této složky?
cs.UninstallKeepSettings=Chcete vymazat všechna nastavení, přizpůsobení a konfigurace Lenovo Legion Toolkit?
cs.VisitGitHub=Navštívit GitHub repozitář

de.JoinLOQDiscord=Discord Community der LOQ Serie beitreten
de.JoinLegionDiscord=Discord Community der Legion Serie beitreten
de.JoinOfficialDiscord=Offizieller Discord Community beitreten
de.SecurePathPageNote=Die Installation außerhalb eines geschützten Ordners wie Program Files deaktiviert unter Windows 11 23H2 und älter UIAccess Funktionen, einschließlich OSD Overlay und Benachrichtigungserhöhung. Auf neueren Windows Versionen blockiert Windows den Start der Anwendung vollständig.
de.SecurePathWarning=Die Installation außerhalb eines geschützten Ordners wie Program Files deaktiviert unter Windows 11 23H2 und älter Funktionen, die auf UIAccess beruhen, einschließlich OSD Overlay und Benachrichtigungserhöhung. Auf neueren Windows Versionen führt dies aufgrund strengerer Sicherheitsrichtlinien dazu, dass Windows den Start der Anwendung vollständig blockiert.%n%nMöchten Sie trotzdem in diesen Ordner installieren?
de.UninstallKeepSettings=Möchten Sie alle Einstellungen, Anpassungen und Konfigurationen von Lenovo Legion Toolkit löschen?
de.VisitGitHub=GitHub Repository besuchen

el.JoinLOQDiscord=Σύνδεση στην κοινότητα Discord της σειράς LOQ
el.JoinLegionDiscord=Σύνδεση στην κοινότητα Discord της σειράς Legion
el.JoinOfficialDiscord=Σύνδεση στην επίσημη κοινότητα Discord
el.SecurePathPageNote=Η εγκατάσταση εκτός προστατευμένου φακέλου όπως το Program Files θα απενεργοποιήσει τις λειτουργίες UIAccess, συμπεριλαμβανομένης της επικάλυψης OSD και της ανύψωσης ειδοποιήσεων, σε Windows 11 23H2 και παλαιότερα. Σε νεότερες εκδόσεις των Windows, αυτό θα αναγκάσει τα Windows να αποκλείσουν πλήρως την εκκίνηση της εφαρμογής.
el.SecurePathWarning=Η εγκατάσταση εκτός προστατευμένου φακέλου όπως το Program Files θα απενεργοποιήσει λειτουργίες που βασίζονται στο UIAccess, συμπεριλαμβανομένης της επικάλυψης OSD και της ανύψωσης ειδοποιήσεων, σε Windows 11 23H2 και παλαιότερα. Σε νεότερες εκδόσεις των Windows, αυτό θα αναγκάσει τα Windows να αποκλείσουν πλήρως την εκκίνηση της εφαρμογής λόγω αυστηρότερων πολιτικών ασφαλείας.%n%nΕίστε βέβαιοι ότι θέλετε να κάνετε εγκατάσταση σε αυτόν τον φάκελο παρόλα αυτά;
el.UninstallKeepSettings=Θέλετε να διαγράψετε όλες τις ρυθμίσεις, τις προσαρμογές και τις παραμέτρους του Lenovo Legion Toolkit;
el.VisitGitHub=Επίσκεψη στο αποθετήριο GitHub

es.JoinLOQDiscord=Unirse a la comunidad de Discord de la serie LOQ
es.JoinLegionDiscord=Unirse a la comunidad de Discord de la serie Legion
es.JoinOfficialDiscord=Unirse a la comunidad oficial de Discord
es.SecurePathPageNote=Instalar fuera de una carpeta protegida como Program Files desactivará las funciones de UIAccess, incluyendo la superposición OSD y la elevación de notificaciones, en Windows 11 23H2 y versiones anteriores. En versiones de Windows más nuevas, esto hará que Windows bloquee el inicio de la aplicación por completo.
es.SecurePathWarning=Instalar fuera de una carpeta protegida como Program Files desactivará las funciones que dependen de UIAccess, incluyendo la superposición OSD y la elevación de notificaciones, en Windows 11 23H2 y versiones anteriores. En versiones de Windows más nuevas, esto hará que Windows bloquee el inicio de la aplicación por completo debido a políticas de seguridad más estrictas.%n%n¿Está seguro de que desea instalar en esta carpeta de todas formas?
es.UninstallKeepSettings=¿Desea eliminar todos los ajustes, personalizaciones y configuraciones de Lenovo Legion Toolkit?
es.VisitGitHub=Visitar el repositorio de GitHub

fr.JoinLOQDiscord=Rejoindre la communauté Discord de la série LOQ
fr.JoinLegionDiscord=Rejoindre la communauté Discord de la série Legion
fr.JoinOfficialDiscord=Rejoindre la communauté officielle Discord
fr.SecurePathPageNote=L'installation en dehors d'un dossier protégé comme Program Files désactivera les fonctionnalités UIAccess, y compris la superposition OSD et l'élévation des notifications, sur Windows 11 23H2 et versions antérieures. Sur les versions plus récentes de Windows, cela obligera Windows à bloquer complètement le lancement de l'application.
fr.SecurePathWarning=L'installation en dehors d'un dossier protégé comme Program Files désactivera les fonctionnalités reposant sur UIAccess, y compris la superposition OSD et l'élévation des notifications, sur Windows 11 23H2 et versions antérieures. Sur les versions plus récentes de Windows, cela obligera Windows à bloquer complètement le lancement de l'application en raison de politiques de sécurité plus strictes.%n%nÊtes-vous sûr de vouloir installer dans ce dossier malgré tout ?
fr.UninstallKeepSettings=Voulez-vous supprimer tous les paramètres, personnalisations et configurations de Lenovo Legion Toolkit ?
fr.VisitGitHub=Visiter le dépôt GitHub

hu.JoinLOQDiscord=Csatlakozás a LOQ sorozat Discord közösségéhez
hu.JoinLegionDiscord=Csatlakozás a Legion sorozat Discord közösségéhez
hu.JoinOfficialDiscord=Csatlakozás a hivatalos Discord közösséghez
hu.SecurePathPageNote=A Program Files-hoz hasonló védett mappán kívüli telepítés letiltja a UIAccess funkciókat, beleértve az OSD átfedést és az értesítések szintjének emelését Windows 11 23H2 és régebbi rendszereken. Újabb Windows verziókon a Windows teljesen blokkolja az alkalmazás indítását.
hu.SecurePathWarning=A Program Files-hoz hasonló védett mappán kívüli telepítés letiltja a UIAccess-re támaszkodó funkciókat, beleértve az OSD átfedést és az értesítések szintjének emelését Windows 11 23H2 és régebbi rendszereken. Újabb Windows verziókon a szigorúbb biztonsági irányelvek miatt a Windows teljesen blokkolja az alkalmazás indítását.%n%nBiztosan ebbe a mappába szeretné telepíteni?
hu.UninstallKeepSettings=Szeretné törölni a Lenovo Legion Toolkit összes beállítását, testreszabását és konfigurációját?
hu.VisitGitHub=GitHub tárhely felkeresése

it.JoinLOQDiscord=Entra nella community Discord della serie LOQ
it.JoinLegionDiscord=Entra nella community Discord della serie Legion
it.JoinOfficialDiscord=Entra nella community ufficiale di Discord
it.SecurePathPageNote=L'installazione al di fuori di una cartella protetta come Program Files disabiliterà le funzionalità UIAccess, tra cui la sovrapposizione OSD e l'elevazione delle notifiche, su Windows 11 23H2 e versioni precedenti. Sulle versioni di Windows più recenti, questo farà sì che Windows blocchi completamente l'avvio dell'applicazione.
it.SecurePathWarning=L'installazione al di fuori di una cartella protetta come Program Files disabiliterà le funzionalità che si affidano a UIAccess, tra cui la sovrapposizione OSD e l'elevazione delle notifiche, su Windows 11 23H2 e versioni precedenti. Sulle versioni di Windows più recenti, questo farà sì che Windows blocchi completamente l'avvio dell'applicazione a causa di criteri di sicurezza più severi.%n%nSei sicuro di voler installare comunque in questa cartella?
it.UninstallKeepSettings=Vuoi eliminare tutte le impostazioni, personalizzazioni e configurazioni di Lenovo Legion Toolkit?
it.VisitGitHub=Visita la repository GitHub

ja.JoinLOQDiscord=LOQシリーズのDiscordコミュニティに参加
ja.JoinLegionDiscord=LegionシリーズのDiscordコミュニティに参加
ja.JoinOfficialDiscord=公式Discordコミュニティに参加
ja.SecurePathPageNote=Program Filesなどの保護されたフォルダー以外にインストールすると、Windows 11 23H2以前ではOSDオーバーレイや通知の昇格などのUIAccess機能が無効になります。新しいWindowsバージョンでは、Windowsがアプリケーション의 起動を完全にブロックします。
ja.SecurePathWarning=Program Filesなどの保護されたフォルダー以外にインストールすると、Windows 11 23H2以前ではOSDオーバーレイや通知の昇格などのUIAccessに依存する機能が無効になります。新しいWindowsバージョンでは、セキュリティポリシーの強化により、Windowsがアプリケーションの起動を完全にブロックします。%n%n本当にこのフォルダーにインストールしますか？
ja.UninstallKeepSettings=Lenovo Legion Toolkitの設定、カスタマイズ、および構成をすべて削除しますか？
ja.VisitGitHub=GitHubリポジトリを表示

ko.JoinLOQDiscord=LOQ 시리즈 Discord 커뮤니티 가입
ko.JoinLegionDiscord=Legion 시리즈 Discord 커뮤니티 가입
ko.JoinOfficialDiscord=공식 Discord 커뮤니티 가입
ko.SecurePathPageNote=Program Files와 같은 보호된 폴더 외부에 설치하면 Windows 11 23H2 이하에서 OSD 오버레이 및 알림 승인을 포함한 UIAccess 기능이 비활성화됩니다. 최신 Windows 버전에서는 Windows가 애플리케이션 실행을 완전히 차단합니다.
ko.SecurePathWarning=Program Files와 같은 보호된 폴더 외부에 설치하면 Windows 11 23H2 이하에서 OSD 오버레이 및 알림 승인을 포함한 UIAccess 의존 기능이 비활성화됩니다. 최신 Windows 버전에서는 강화된 보안 정책으로 인해 Windows가 애플리케이션 실행을 완전히 차단합니다.%n%n그래도 이 폴더에 설치하시겠습니까?
ko.UninstallKeepSettings=Lenovo Legion Toolkit의 모든 설정, 개인화 및 구성을 삭제하시겠습니까?
ko.VisitGitHub=GitHub 리포지토리 방문

lv.JoinLOQDiscord=Pievienoties LOQ sērijas Discord kopienai
lv.JoinLegionDiscord=Pievienoties Legion sērijas Discord kopienai
lv.JoinOfficialDiscord=Pievienoties oficiālajai Discord kopienai
lv.SecurePathPageNote=Instalēšana ārpus aizsargātas mapes, piemēram, Program Files, atspējos UIAccess funkcijas, tostarp OSD pārklājumu un paziņojumu paaugstināšanu, operētājsistēmā Windows 11 23H2 un vecākās versijās. Jaunākās Windows versijās Windows pilnībā bloķēs lietotnes palaišanu.
lv.SecurePathWarning=Instalēšana ārpus aizsargātas mapes, piemēram, Program Files, atspējos funkcijas, kas balstās uz UIAccess, tostarp OSD pārklājumu un paziņojumu paaugstināšanu, operētājsistēmā Windows 11 23H2 un vecākās versijās. Jaunākās Windows versijās stingrāku drošības politiku dēļ Windows pilnībā bloķēs lietotnes palaišanu.%n%nVai tiešām vēlaties instalēt šajā mapē jebkurā gadījumā?
lv.UninstallKeepSettings=Vai vēlaties dzēst visus Lenovo Legion Toolkit iestatījumus, pielāgojumus un konfigurācijas?
lv.VisitGitHub=Apmeklēt GitHub krātuvi

nlnl.JoinLOQDiscord=Lid worden van de LOQ-serie Discord-community
nlnl.JoinLegionDiscord=Lid worden van de Legion-serie Discord-community
nlnl.JoinOfficialDiscord=Lid worden van de offiziële Discord-community
nlnl.SecurePathPageNote=Installeren buiten een beveiligde map zoals Program Files schakelt UIAccess-functies uit, waaronder de OSD-overlay en verhoogde meldingen, in Windows 11 23H2 og ouder. Op nieuwere Windows-versies blokkeert Windows het starten van de applicatie volledig.
nlnl.SecurePathWarning=Installeren buiten een beveiligde map zoals Program Files schakelt functies uit die afhankelijk zijn van UIAccess, waaronder de OSD-overlay en verhoogde meldingen, in Windows 11 23H2 en ouder. Op nieuwere Windows-versies zorgt dit ervoor dat Windows het starten van de applicatie volledig blokkeert vanwege strenger veiligheidsbeleid.%n%nWilt u toch in deze map installeren?
nlnl.UninstallKeepSettings=Wilt u alle instellingen, aanpassingen en configuraties van Lenovo Legion Toolkit verwijderen?
nlnl.VisitGitHub=GitHub-opslagplaats bezoeken

no.JoinLOQDiscord=Bli med i LOQ-seriens Discord-fellesskap
no.JoinLegionDiscord=Bli med i Legion-seriens Discord-fellesskap
no.JoinOfficialDiscord=Bli med i det offisielle Discord-fellesskapet
no.SecurePathPageNote=Installasjon utenfor en beskyttet mappe som Program Files vil deaktivere UIAccess-funksjoner, inkludert OSD-overlegg og varslingsheving, på Windows 11 23H2 og eldre. På nyere Windows-versjoner vil Windows blokkere programmet fra å starte helt.
no.SecurePathWarning=Installasjon utenfor en beskyttet mappe som Program Files vil deaktivere funksjoner som krever UIAccess, inkludert OSD-overlegg og varslingsheving, på Windows 11 23H2 og eldre. På nyere Windows-versjoner vil dette føre to at Windows blokkerer programmet fra å starte helt på grunn av strengere sikkerhetsregler.%n%nEr du sikker på at du vil installere i denne mappen likevel?
no.UninstallKeepSettings=Vil du slette alle Lenovo Legion Toolkit-innstillinger, tilpasninger og konfigurasjoner?
no.VisitGitHub=Besøk GitHub-depotet

pl.JoinLOQDiscord=Dołącz do społeczności Discord serii LOQ
pl.JoinLegionDiscord=Dołącz do społeczności Discord serii Legion
pl.JoinOfficialDiscord=Dołącz do oficjalnej społeczności Discord
pl.SecurePathPageNote=Instalacja poza folderem chronionym, takim jak Program Files, wyłączy funkcje UIAccess, w tym nakładkę OSD i podnoszenie uprawnień powiadomień, w systemie Windows 11 23H2 i starszych. W nowszych wersjach systemu Windows spowoduje to całkowite zablokowanie uruchamiania aplikacji przez system Windows.
pl.SecurePathWarning=Instalacja poza folderem chronionym, takim jak Program Files, wyłączy funkcje zależne od UIAccess, w tym nakładkę OSD i podnoszenie uprawnień powiadomień, w systemie Windows 11 23H2 i starszych. W nowszych wersjach systemu Windows spowoduje to całkowite zablokowanie uruchamiania aplikacji przez system Windows z powodu ostrzejszych zasad bezpieczeństwa.%n%nCzy na pewno chcesz mimo to zainstalować w tym folderze?
pl.UninstallKeepSettings=Czy chcesz usunąć wszystkie ustawienia, dostosowania i konfiguracje Lenovo Legion Toolkit?
pl.VisitGitHub=Odwiedź repozytorium GitHub

pt.JoinLOQDiscord=Aderir à comunidade do Discord da série LOQ
pt.JoinLegionDiscord=Aderir à comunidade do Discord da série Legion
pt.JoinOfficialDiscord=Aderir à comunidade oficial do Discord
pt.SecurePathPageNote=A instalação fora de uma pasta protegida como Program Files desativará as funcionalidades UIAccess, incluindo a sobreposição OSD e a elevação de notificações, no Windows 11 23H2 e anterior. Em versões mais recentes do Windows, isso fará com que o Windows bloqueie o arranque da aplicação por completo.
pt.SecurePathWarning=A instalação fora de uma pasta protegida como Program Files desativará as funcionalidades que dependem de UIAccess, incluindo a sobreposição OSD e a elevação de notificações, no Windows 11 23H2 e anterior. Em versões mais recentes do Windows, isso fará com que o Windows bloqueie o arranque da aplicação por completo devido a políticas de segurança mais rigorosas.%n%nTem a certeza de que deseja instalar nesta pasta de qualquer forma?
pt.UninstallKeepSettings=Deseja eliminar todas as definições, personalizações e configurações do Lenovo Legion Toolkit?
pt.VisitGitHub=Visitar o repositório GitHub

ptbr.JoinLOQDiscord=Participar da comunidade do Discord da série LOQ
ptbr.JoinLegionDiscord=Participar da comunidade do Discord da série Legion
ptbr.JoinOfficialDiscord=Participar da comunidade oficial do Discord
ptbr.SecurePathPageNote=A instalação fora de uma pasta protegida como Program Files desativará os recursos do UIAccess, incluindo a sobreposição OSD e a elevação de notificações, no Windows 11 23H2 e anterior. Em versões mais recentes do Windows, isso fará com que o Windows bloqueie totalmente a inicialização do aplicativo.
ptbr.SecurePathWarning=A instalação fora de uma pasta protegida como Program Files desativará recursos que dependem do UIAccess, incluindo a sobreposição OSD e a elevação de notificações, no Windows 11 23H2 e anterior. Em versões mais recentes do Windows, isso fará com que o Windows bloqueie totalmente a inicialização do aplicativo devido a políticas de segurança mais rígidas.%n%nTem certeza de que deseja instalar nesta pasta mesmo assim?
ptbr.UninstallKeepSettings=Deseja excluir todas as configurações, personalizações e ajustes do Lenovo Legion Toolkit?
ptbr.VisitGitHub=Visitar o repositório do GitHub

ro.JoinLOQDiscord=Alăturați-vă comunității Discord a seriei LOQ
ro.JoinLegionDiscord=Alăturați-vă comunității Discord a seriei Legion
ro.JoinOfficialDiscord=Alăturați-vă comunității oficiale Discord
ro.SecurePathPageNote=Instalarea în afara unui folder protejat cum ar fi Program Files va dezactiva caracteristicile UIAccess, inclusiv suprapunerea OSD și ridicarea notificărilor, pe Windows 11 23H2 și versiunile anterioare. Pe versiunile de Windows mai noi, acest lucru va determina Windows să blocheze complet lansarea aplicației.
ro.SecurePathWarning=Instalarea în afara unui folder protejat cum ar fi Program Files va dezactiva caracteristicile care depind de UIAccess, inclusiv suprapunerea OSD și ridicarea notificărilor, pe Windows 11 23H2 și versiunile anterioare. Pe versiunile de Windows mai noi, acest lucru va determina Windows să blocheze complet lansarea aplicației din cauza politicilor de securitate mai stricte.%n%nSunteți sigur că doriți să instalați în acest folder oricum?
ro.UninstallKeepSettings=Doriți să ștergeți toate setările, personalizările și configurațiile Lenovo Legion Toolkit?
ro.VisitGitHub=Vizitați depozitul GitHub

ru.JoinLOQDiscord=Присоединиться к сообществу Discord серии LOQ
ru.JoinLegionDiscord=Присоединиться к сообществу Discord серии Legion
ru.JoinOfficialDiscord=Присоединиться к официальному сообществу Discord
ru.SecurePathPageNote=Установка вне защищенной папки, такой как Program Files, отключит функции UIAccess, включая экранное меню OSD и повышение уведомлений, в Windows 11 23H2 и более ранних версиях. В более новых версиях Windows это приведет к полной блокировке запуска приложения системой.
ru.SecurePathWarning=Установка вне защищенной папки, такой как Program Files, отключит функции, использующие UIAccess, включая экранное меню OSD и повышение уведомлений, в Windows 11 23H2 и более ранних версиях. В более новых версиях Windows это приведет к полной блокировке запуска приложения системой из-за более строгих политик безопасности.%n%nВы действительно хотите установить программу в эту папку?
ru.UninstallKeepSettings=Вы хотите удалить все настройки, персонализации и конфигурации Lenovo Legion Toolkit?
ru.VisitGitHub=Посетить репозиторий GitHub

sk.JoinLOQDiscord=Pripojiť sa k Discord komunite radu LOQ
sk.JoinLegionDiscord=Pripojiť sa k Discord komunite radu Legion
sk.JoinOfficialDiscord=Pripojiť sa k oficiálnej Discord komunite
sk.SecurePathPageNote=Inštalácia mimo chráneného priečinka, ako je Program Files, zakáže funkcie UIAccess, vrátane OSD prekrytia a zvýšenia oprávnení oznámení, v systéme Windows 11 23H2 a starších. Na novších verziách systému Windows to spôsobí, že Windows aplikáciu úplne zablokuje.
sk.SecurePathWarning=Inštalácia mimo chráneného priečinka, ako je Program Files, zakáže funkcie závislé od UIAccess, vrátane OSD prekrytia a zvýšenia oprávnení oznámení, v systéme Windows 11 23H2 a starších. Na novších verziách systému Windows to spôsobí, že Windows aplikáciu úplne zablokuje z dôvodu prísnejších bezpečnostných pravidiel.%n%nNaozaj chcete napriek tomu inštalovať do tohto priečinka?
sk.UninstallKeepSettings=Chcete vymazať všetky nastavenia, prispôsobenia a konfigurácie Lenovo Legion Toolkit?
sk.VisitGitHub=Navštíviť GitHub repozitár

tr.JoinLOQDiscord=LOQ Serisi Discord Topluluğuna Katıl
tr.JoinLegionDiscord=Legion Serisi Discord Topluluğuna Katıl
tr.JoinOfficialDiscord=Resmi Discord Topluluğuna Katıl
tr.SecurePathPageNote=Program Files gibi korumalı bir klasörün dışına yükleme yapmak, Windows 11 23H2 ve önceki sürümlerde OSD yerleşimi ve bildirim yükseltme gibi UIAccess özelliklerini devre dışı bırakır. Daha yeni Windows sürümlerinde ise bu durum Windows'un uygulamayı başlatmasını tamamen engeller.
tr.SecurePathWarning=Program Files gibi korumalı bir klasörün dışına yükleme yapmak, Windows 11 23H2 ve önceki sürümlerde OSD yerleşimi ve bildirim yükseltme gibi UIAccess gerektiren özellikleri devre dışı bırakır. Daha yeni Windows sürümlerinde ise daha katı güvenlik politikaları nedeniyle Windows'un uygulamayı başlatmasını tamamen engeller.%n%nYine de bu klasöre yüklemek istediğinizden emin misiniz?
tr.UninstallKeepSettings=Tüm Lenovo Legion Toolkit ayarlarını, özelleştirmelerini ve yapılandırmalarını silmek istiyor musunuz?
tr.VisitGitHub=GitHub Deposunu Ziyaret Et

ukr.JoinLOQDiscord=Приєднатися до спільноти Discord серії LOQ
ukr.JoinLegionDiscord=Приєднатися до спільноти Discord серії Legion
ukr.JoinOfficialDiscord=Приєднатися до офіційної спільноти Discord
ukr.SecurePathPageNote=Встановлення поза захищеною папкою, такою як Program Files, вимкне функції UIAccess, включаючи екранне меню OSD та підвищення сповіщень, у Windows 11 23H2 та старіших версіях. У новіших версіях Windows це призведе до повного блокування запуску програми системою.
ukr.SecurePathWarning=Встановлення поза захищеною папкою, такою як Program Files, вимкне функції, що використовують UIAccess, включаючи екранне меню OSD та підвищення сповіщень, у Windows 11 23H2 та старіших версіях. У новіших версіях Windows це призведе до повного блокування запуску програми системою через суворішу політику безпеки.%n%nВи дійсно бажаєте встановити програму в цю папку?
ukr.UninstallKeepSettings=Бажаєте видалити всі налаштування, персоналізації та конфігурації Lenovo Legion Toolkit?
ukr.VisitGitHub=Відвідати репозиторій GitHub

uz.JoinLOQDiscord=LOQ seriyasining Discord hamjamiyatiga qo'shiling
uz.JoinLegionDiscord=Legion seriyasining Discord hamjamiyatiga qo'shiling
uz.JoinOfficialDiscord=Rasmiy Discord hamjamiyatiga qo'shiling
uz.SecurePathPageNote=Program Files kabi himoyalangan jilddan tashqariga o'rnatish Windows 11 23H2 va undan pastroq versiyalarda UIAccess funktsiyalarini, shu jumladan OSD qoplamasi va bildirishnomalarni oshirishni o'chirib qo'yadi. Yangi Windows versiyalarida bu Windows-ning dasturni ishga tushirishini butunlay bloklaydi.
uz.SecurePathWarning=Program Files kabi himoyalangan jilddan tashqariga o'rnatish Windows 11 23H2 va undan pastroq versiyalarda UIAccess-ga tayanadigan funktsiyalarni, shu jumladan OSD qoplamasi va bildirishnomalarni oshirishni o'chirib qo'yadi. Yangi Windows versiyalarida bu qat'iy xavfsizlik siyosati tufayli Windows-ning dasturni ishga tushirishini butunlay bloklaydi.%n%nBaribir ushbu jildga o'rnatmoqchimisiz?
uz.UninstallKeepSettings=Lenovo Legion Toolkit-ning barcha sozlamalari, moslashtirishlari va konfiguratsiyalarini o'chirib tashlamoqchimisiz?
uz.VisitGitHub=GitHub omboriga tashrif buyuring

vi.JoinLOQDiscord=Tham gia cộng đồng Discord của dòng LOQ
vi.JoinLegionDiscord=Tham gia cộng đồng Discord của dòng Legion
vi.JoinOfficialDiscord=Tham gia cộng đồng Discord chính thức
vi.SecurePathPageNote=Việc cài đặt bên ngoài một thư mục được bảo vệ như Program Files sẽ vô hiệu hóa các tính năng UIAccess, bao gồm lớp phủ OSD và nâng cao thông báo, trên Windows 11 23H2 trở xuống. Trên các phiên bản Windows mới hơn, điều này sẽ khiến Windows chặn hoàn toàn việc khởi chạy ứng dụng.
vi.SecurePathWarning=Việc cài đặt bên ngoài một thư mục được bảo vệ như Program Files sẽ vô hiệu hóa các tính năng dựa trên UIAccess, bao gồm lớp phủ OSD và nâng cao thông báo, trên Windows 11 23H2 trở xuống. Trên các phiên bản Windows mới hơn, điều này sẽ khiến Windows chặn hoàn toàn việc khởi chạy ứng dụng do các chính sách bảo mật nghiêm ngặt hơn.%n%nBạn có chắc chắn vẫn muốn cài đặt vào thư mục này không?
vi.UninstallKeepSettings=Bạn có muốn xóa tất cả cài đặt, tùy chỉnh và cấu hình của Lenovo Legion Toolkit không?
vi.VisitGitHub=Tru cập kho lưu trữ GitHub

zhhans.JoinLOQDiscord=加入 LOQ 系列 Discord 社区
zhhans.JoinLegionDiscord=加入 Legion 系列 Discord 社区
zhhans.JoinOfficialDiscord=加入官方 Discord 社区
zhhans.SecurePathPageNote=在 Windows 11 23H2 及更低版本中，若将应用安装到受保护文件夹（如 Program Files）之外，将禁用 UIAccess 功能（包括 OSD 覆盖和通知置顶显示）。而在较新的 Windows 版本中，此举会导致 Windows 完全阻止该应用程序启动。
zhhans.SecurePathWarning=安装在受保护的文件夹例如 Program Files 之外，在 Windows 11 23H2 及以下版本中将禁用依赖 UIAccess 的功能，包括 OSD 覆盖和通知提升。在较新的 Windows 版本中，由于更严格的安全策略，这会导致 Windows 完全阻止应用程序启动。%n%n您确定仍要安装到此文件夹吗？
zhhans.UninstallKeepSettings=您是否要删除所有 Lenovo Legion Toolkit 设置、自定义和配置？
zhhans.VisitGitHub=访问 GitHub 仓库

zhhant.JoinLOQDiscord=加入 LOQ 系列 Discord 社群
zhhant.JoinLegionDiscord=加入 Legion 系列 Discord 社群
zhhant.JoinOfficialDiscord=加入官方 Discord 社群
zhhant.SecurePathPageNote=安裝在受保護的資料夾例如 Program Files 之外，在 Windows 11 23H2 及以下版本中將停用 UIAccess 功能，包括 OSD 覆蓋 and 通知提升。在較新的 Windows 版本中，這會導致 Windows 完全阻止應用程式啟動。
zhhant.SecurePathWarning=安裝在受保護的資料夾例如 Program Files 之外，在 Windows 11 23H2 及以下版本中將停用依賴 UIAccess 的功能，包括 OSD 覆蓋和通知提升。在較新的 Windows 版本中，由於更嚴格的安全策略，這會導致 Windows 完全阻止應用程式啟動。%n%n您確定仍要安裝到此資料夾嗎？
zhhant.UninstallKeepSettings=您是否要刪除所有 Lenovo Legion Toolkit 設定、自訂和配置？
zhhant.VisitGitHub=訪問 GitHub 倉庫

[Languages]
Name: "en";      MessagesFile: "compiler:Default.isl"
Name: "ar";      MessagesFile: "InnoDependencies\Languages\Arabic.isl"
Name: "bs";      MessagesFile: "InnoDependencies\Languages\Bosnian.isl"
Name: "bg";      MessagesFile: "InnoDependencies\Languages\Bulgarian.isl"
Name: "zhhans";  MessagesFile: "InnoDependencies\Languages\ChineseSimplified.isl"
Name: "zhhant";  MessagesFile: "InnoDependencies\Languages\ChineseTraditional.isl"
Name: "cs";      MessagesFile: "InnoDependencies\Languages\Czech.isl"
Name: "nlnl";    MessagesFile: "InnoDependencies\Languages\Dutch.isl"
Name: "fr";      MessagesFile: "InnoDependencies\Languages\French.isl"
Name: "de";      MessagesFile: "InnoDependencies\Languages\German.isl"
Name: "el";      MessagesFile: "InnoDependencies\Languages\Greek.isl"
Name: "hu";      MessagesFile: "InnoDependencies\Languages\Hungarian.isl"
Name: "it";      MessagesFile: "InnoDependencies\Languages\Italian.isl"
Name: "ja";      MessagesFile: "InnoDependencies\Languages\Japanese.isl"
Name: "ko";      MessagesFile: "InnoDependencies\Languages\Korean.isl"
Name: "lv";      MessagesFile: "InnoDependencies\Languages\Latvian.isl"
Name: "no";      MessagesFile: "InnoDependencies\Languages\Norwegian.isl"
Name: "pl";      MessagesFile: "InnoDependencies\Languages\Polish.isl"
Name: "pt";      MessagesFile: "InnoDependencies\Languages\Portuguese.isl"
Name: "ptbr";    MessagesFile: "InnoDependencies\Languages\BrazilianPortuguese.isl"
Name: "ro";      MessagesFile: "InnoDependencies\Languages\Romanian.isl"
Name: "ru";      MessagesFile: "InnoDependencies\Languages\Russian.isl"
Name: "sk";      MessagesFile: "InnoDependencies\Languages\Slovak.isl"
Name: "es";      MessagesFile: "InnoDependencies\Languages\Spanish.isl"
Name: "tr";      MessagesFile: "InnoDependencies\Languages\Turkish.isl"
Name: "ukr";     MessagesFile: "InnoDependencies\Languages\Ukrainian.isl"
Name: "uz";      MessagesFile: "InnoDependencies\Languages\Uzbek.isl"
Name: "vi";      MessagesFile: "InnoDependencies\Languages\Vietnamese.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "build\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[InstallDelete]
; Safely remove legacy installation from %LOCALAPPDATA%\Programs without touching user settings
Type: filesandordirs; Name: "{userpf}\{#MyAppNameCompact}"

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; AppUserModelID: "eef45acd-2cf3-4d7d-9d33-92f37c74cc31_6qs7aha96dxnt!App"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon; AppUserModelID: "eef45acd-2cf3-4d7d-9d33-92f37c74cc31_6qs7aha96dxnt!App"

[Run]
; Unregister existing package identity (required for upgrades)
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -Command ""Get-AppxPackage -Name 'eef45acd-2cf3-4d7d-9d33-92f37c74cc31' | Remove-AppxPackage -ErrorAction SilentlyContinue"""; Flags: runhidden; StatusMsg: "Removing previous identity..."
; Trust the self-signed certificate in Trusted People (required for registration)
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -Command ""Import-Certificate -FilePath '{app}\LenovoLegionToolkit.cer' -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople'"""; Flags: runhidden; StatusMsg: "Trusting application identity..."
; Trust the self-signed certificate in Root (required for UIAccess check)
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -Command ""Import-Certificate -FilePath '{app}\LenovoLegionToolkit.cer' -CertStoreLocation 'Cert:\LocalMachine\Root'"""; Flags: runhidden; StatusMsg: "Trusting application root certificate..."
; Register the Sparse Package identity (conditional: use .msix if present, else raw manifest)
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -Command ""if (Test-Path '{app}\LenovoLegionToolkit.LampArray.msix') {{ Add-AppxPackage -Path '{app}\LenovoLegionToolkit.LampArray.msix' -ExternalLocation '{app}' } else {{ Add-AppxPackage -Register '{app}\AppxManifest.xml' -ExternalLocation '{app}' }"""; Flags: runhidden; StatusMsg: "Registering application identity..."

Filename: "rundll32.exe"; Parameters: "shell32.dll,ShellExec_RunDLL ""{app}\{#MyAppExeName}"""; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: runascurrentuser nowait postinstall skipifsilent

[UninstallRun]
RunOnceId: "DelAutorun"; Filename: "schtasks"; Parameters: "/Delete /TN ""LenovoLegionToolkit_Autorun_6efcc882-924c-4cbc-8fec-f45c25696f98"" /F"; Flags: runhidden
RunOnceId: "CleanPath"; Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -Command ""$p = [Environment]::GetEnvironmentVariable('PATH', 'User'); if ($p) {{ $a = $p -split ';' | Where-Object {{ $_ -and $_ -ne '{app}' -and $_ -ne '{app}\' }; $n = $a -join ';'; if ($n -ne $p) {{ [Environment]::SetEnvironmentVariable('PATH', $n, 'User') } }"""; Flags: runhidden