#define AppName "LoketPOSSolo"
#define AppVersion "1.0.0"

[Setup]
AppId={{9B2B9C0D-AAC9-4566-B5B8-7E3C8E1B3F10}}
AppName={#AppName}
AppVersion={#AppVersion}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
OutputBaseFilename=LoketPOSSolo-Setup
OutputDir=.\Output
Compression=lzma
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
PrivilegesRequired=admin
DisableDirPage=no
DisableProgramGroupPage=no

[Types]
Name: "adminpc";   Description: "Admin PC"
Name: "tellerpc";  Description: "Teller PC"
Name: "displaypc"; Description: "Display PC"
Name: "serverpc";  Description: "Server PC"

[Components]
Name: "admin";   Description: "Admin App";   Types: adminpc
Name: "teller";  Description: "Teller App";  Types: tellerpc
Name: "display"; Description: "Display App"; Types: displaypc
Name: "server";  Description: "Server API";  Types: serverpc

[Files]
Source: "..\publish\admin\*";   DestDir: "{app}\admin";   Components: admin;   Flags: recursesubdirs ignoreversion
Source: "..\publish\teller\*";  DestDir: "{app}\teller";  Components: teller;  Flags: recursesubdirs ignoreversion
Source: "..\publish\display\*"; DestDir: "{app}\display"; Components: display; Flags: recursesubdirs ignoreversion
Source: "..\publish\server\*";  DestDir: "{app}\server";  Components: server;  Flags: recursesubdirs ignoreversion

[Dirs]
Name: "{app}\server\wwwroot\videos";  Components: server
Name: "{commonappdata}\QueueSystem";  Flags: uninsneveruninstall

[Icons]
Name: "{group}\Admin";    Filename: "{app}\admin\AdminApp.Wpf.exe";   Components: admin
Name: "{group}\Teller";   Filename: "{app}\teller\TellerApp.Wpf.exe"; Components: teller
Name: "{group}\Display";  Filename: "{app}\display\DisplayApp.Wpf.exe"; Components: display
Name: "{group}\Server (jalankan manual)"; Filename: "{app}\server\QueueServer.Api.exe"; Components: server
Name: "{group}\Server (bind otomatis)"; Filename: "{app}\server\QueueServer.Api.exe"; Parameters: "--urls ""{code:GetServerUrls}"""; Components: server
Name: "{commondesktop}\Admin";    Filename: "{app}\admin\AdminApp.Wpf.exe"; Components: admin; Tasks: desktopicons
Name: "{commondesktop}\Teller";   Filename: "{app}\teller\TellerApp.Wpf.exe"; Components: teller; Tasks: desktopicons
Name: "{commondesktop}\Display";  Filename: "{app}\display\DisplayApp.Wpf.exe"; Components: display; Tasks: desktopicons

[Tasks]
Name: "desktopicons"; Description: "Shortcut di Desktop"; Flags: unchecked

[Run]
; Tambah firewall rule hanya bila memasang server
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""QueueServer ({code:GetPort})"" dir=in action=allow protocol=TCP localport={code:GetPort}"; Flags: runhidden; Components: server
; tulis server.json untuk dibaca aplikasi server saat start (berisi Urls)
Filename: "{cmd}"; Parameters: "/C echo {{""Urls"": ""{code:GetServerUrls}""}} > ""{commonappdata}\QueueSystem\server.json"""; Flags: runhidden; Components: server
; jalankan server langsung setelah install dengan binding yang dipilih
Filename: "{app}\server\QueueServer.Api.exe"; Parameters: "--urls ""{code:GetServerUrls}"""; Flags: postinstall nowait skipifsilent; Components: server

[UninstallRun]
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""QueueServer ({code:GetPort})"""; Flags: runhidden; RunOnceId: netshfw1

[Code]
var
  CfgPage: TInputQueryWizardPage;
  UrlValue: string;
  PortValue: string;

procedure InitializeWizard;
begin
  CfgPage := CreateInputQueryPage(wpWelcome,
    'Konfigurasi', 'Masukkan informasi koneksi',
    'Untuk Admin/Teller/Display: isi Server URL. Untuk Server: isi Port jika ingin buka firewall.');
  CfgPage.Add('Server URL (mis. http://192.168.1.50:5000):', False);
  CfgPage.Add('Port Server (mis. 5000):', False);
  CfgPage.Values[0] := 'http://localhost:5000';
  CfgPage.Values[1] := '5000';
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  needClient: Boolean;
  dir, cfg, json: string;
begin
  Result := True;
  if CurPageID = CfgPage.ID then
  begin
    UrlValue := Trim(CfgPage.Values[0]);
    PortValue := Trim(CfgPage.Values[1]);
    if PortValue = '' then PortValue := '5000';

    needClient := WizardIsComponentSelected('admin') or
                  WizardIsComponentSelected('teller') or
                  WizardIsComponentSelected('display');
    if needClient then
    begin
      if UrlValue = '' then
      begin
        MsgBox('Server URL wajib diisi.', mbError, MB_OK);
        Result := False;
        exit;
      end;
      dir := ExpandConstant('{commonappdata}\QueueSystem');
      if (not DirExists(dir)) and (not ForceDirectories(dir)) then
      begin
        MsgBox('Gagal buat folder: ' + dir, mbError, MB_OK);
        Result := False;
        exit;
      end;
      cfg := dir + '\config.json';
      json := '{'#13#10'  "ServerUrl": "' + UrlValue + '"'#13#10'}';
      if not SaveStringToFile(cfg, json, False) then
      begin
        MsgBox('Gagal tulis config: ' + cfg, mbError, MB_OK);
        Result := False;
        exit;
      end;
    end;
  end;
end;

function GetPort(Param: string): string;
begin
  if PortValue = '' then Result := '5000' else Result := PortValue;
end;

function GetServerUrls(Param: string): string;
var
  u, scheme, host, ch: string;
  i, L, startPos: Integer;
begin
  u := LowerCase(UrlValue);
  if Pos('https://', u) = 1 then scheme := 'https' else scheme := 'http';

  // Gunakan UrlValue asli (bukan lower) agar host case-sensitive kalau perlu
  u := UrlValue;
  // Cari awal host (setelah "://")
  startPos := Pos('://', u);
  if startPos > 0 then
    startPos := startPos + 3
  else
    startPos := 1;

  L := Length(u);
  host := '';
  for i := startPos to L do
  begin
    ch := Copy(u, i, 1);
    if (ch = '/') or (ch = '?') or (ch = '#') then Break;
    host := host + ch;
  end;

  // Jika kosong fallback 0.0.0.0
  if host = '' then host := '0.0.0.0';

  // Hilangkan port yang mungkin sudah ada (akan diganti PortValue)
  i := Pos(':', host);
  if i > 0 then
    host := Copy(host, 1, i - 1);

  // Bentuk final
  Result := scheme + '://' + host + ':' + GetPort('');
end;