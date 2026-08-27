# Installation und Installer

Diese Anleitung unterscheidet zwischen dem Build-Rechner, auf dem das Setup erzeugt wird, und dem Zielrechner, auf dem ein Benutzer VIBN Tools verwendet.

## 1. Bereits erzeugtes Setup auf einem anderen Rechner verwenden

Wenn `VIBN_Tools_Setup.exe` bereits vorliegt:

1. Nur `VIBN_Tools_Setup.exe` auf den Zielrechner übertragen.
2. Setup mit einem Benutzer ausführen, der Software installieren darf.
3. Installationsordner bestätigen und optional die Desktopverknüpfung auswählen.
4. Einmal pro Windows-Benutzer `Configure-VIBN-Tools.cmd` aus dem Installationsordner starten. API-Key und RDP-Kennwort werden verdeckt abgefragt.
5. `VIBN_Tools.exe` beziehungsweise die Desktopverknüpfung starten.

Visual Studio und eine separate .NET-Installation werden nicht benötigt. Für TIA-Funktionen müssen eine passende TIA-/Openness-Version, .NET Framework 4.8 und die Siemens-Openness-Benutzergruppe vorhanden sein. FEE-Funktionen benötigen die freigegebene FEE-Laufzeit beziehungsweise die zugehörigen betrieblichen Dienste und Lizenzen.

## 2. Setup auf dem Build-Rechner erzeugen

Voraussetzungen:

- Windows x64
- vollständige fe.screen-sim-SDK-Installation
- Zugriff auf das private Grob.UX-NuGet-Paket beziehungsweise den internen Feed
- Inno Setup 6
- .NET SDK

Aus dem Repository-Stamm:

```powershell
.\Build.ps1
.\Build-Installer.bat
```

Falls Grob.UX aus einem zusätzlichen lokalen Feed kommt:

```powershell
.\Build-Installer.bat -AdditionalPackageSource "D:\InternerNuGetFeed"
```

Ein explizites SDK ist nur erforderlich, wenn mehrere vollständige Installationen vorhanden sind oder eine bestimmte Version verwendet werden soll:

```powershell
.\Build-Installer.bat -FeeScreenSimRoot "C:\Program Files\fe.screen-sim V5\5.0.11.48415"
```

Das fertige Setup liegt anschließend unter:

```text
artifacts\installer\VIBN_Tools_Setup.exe
```

Die Datei `installer\VIBN_Tools.iss` ist nicht das fertige Setup, sondern das Inno-Setup-Rezept. Normalerweise wird sie nicht manuell geöffnet; `Build-Installer.bat` veröffentlicht zuerst die Anwendung und ruft danach den Inno-Compiler auf.

## 3. SDK-Auswahl

`Build.ps1`, der Portable Publisher und der Installer prüfen nacheinander:

1. `-FeeScreenSimRoot`
2. `FEE_SCREEN_SIM_ROOT`
3. `external\fe-screen-sim`
4. installierte Unterordner von `C:\Program Files\fe.screen-sim V5`

Installationsordner ohne `Bin\FS.SDK.dll` werden mit einer Warnung übersprungen. Bei installierten Versionen wird die höchste vollständige Version gewählt und als

```text
FEE SDK erkannt: Version ... unter '...'
```

ausgegeben. Visual Studio erkennt automatisch genau eine vollständige Installation. Sind mehrere vollständige SDKs vorhanden, `Build.ps1` verwenden oder `FEE_SCREEN_SIM_ROOT` einmalig setzen.

### Direktes Debuggen in Visual Studio

`VIBN_Tools_App.sln` öffnen, in der Startauswahl das geteilte Profil **VIBN Tools** wählen und F5 drücken. Falls eine ältere Visual-Studio-Version `.slnLaunch` noch nicht anzeigt, im Projektmappen-Explorer `VIBN_Tools` per Rechtsklick als Startprojekt festlegen. Die Debug-Konfiguration erzeugt eine selbstenthaltende x64-Anwendung unter `artifacts\build\Debug\net8.0-windows\win-x64`. Damit hängt F5 nicht von einer global installierten .NET-8-Patchversion ab. Benötigte NuGet-Runtime-Pakete werden von Visual Studio beim üblichen Restore bezogen; es ist keine separate Runtime-Konfiguration erforderlich.

## 4. Fehler `Metadata file ... VIBN_Tools.dll could not be found`

Diese Meldung kommt normalerweise aus einem nachgelagerten Projekt, beispielsweise dem UI-Startup-Test. Sie bedeutet, dass das Hauptprojekt `VIBN_Tools` vorher nicht erfolgreich gebaut wurde. Die Metadatenmeldung ist nicht die eigentliche Ursache.

Vorgehen:

1. In der Fehlerliste nach dem ersten Fehler des Projekts `VIBN_Tools` suchen.
2. SDK-Ausgabe kontrollieren.
3. Solution bereinigen und über das Skript bauen:

```powershell
dotnet clean VIBN_Tools_App.sln
.\Build.ps1
```

Bei einer vollständigen SDK-Version muss der Build mit `VIBN_Tools.dll` unter `artifacts\build\Release\net8.0-windows` enden.

## 5. Portable ZIP statt Setup

Für Support- oder Offlinefälle kann ein self-contained ZIP erzeugt werden:

```powershell
.\Publish-Portable.bat
```

Das ZIP vollständig entpacken und anschließend `VIBN_Tools.exe` starten. Dateien innerhalb des ZIPs dürfen nicht einzeln herauskopiert werden, weil FEE-, Plugin-, TIA-Bridge- und .NET-Laufzeitdateien gemeinsam benötigt werden.
