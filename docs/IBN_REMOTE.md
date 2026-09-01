# IBN Remote – separate Ein-EXE-Ausgabe

## Abgrenzung

`VIBN_Tools_IBN.exe` ist eine separate WPF-Anwendung für Inbetriebnehmer. Sie ist kein Hauptprogramm mit ausgeblendeten Tabs. Der Build enthält nur:

- neutrale ViCo-Modelle und Suche aus `VIBN_Tools.Core`;
- einen minimal kompilierten Read-/RDP-Ausschnitt in `VIBN_Tools.IbnRemote.Infrastructure`;
- Arbeitsplatzliste, Filter, Onlineprüfung und RDP-Sitzungsdiagnose;
- automatische RDP-Anmeldung und RDP mit Windows-Anmeldedialog.

Nicht enthalten sind FEE-SDK, TIA-Bridge, Container-/CAD-/Modellfunktionen, Administration, Dateiübertragung und Kanbanize-Schreiboperationen. Der Kanbanize-Zugriff des IBN-Clients besteht ausschließlich aus GET-Abfragen und einem lokalen Cache.

## Erzeugen und verteilen

Auf dem Buildrechner genügt:

```powershell
.\scripts\Publish-IbnRemote.ps1
```

Das Ergebnis ist:

```text
artifacts\publish\IBN-Remote\VIBN_Tools_IBN.exe
```

Die Datei ist `win-x64`, self-contained und single-file. Auf dem Ziel-PC sind weder Visual Studio noch eine separate .NET-Installation, FEE oder TIA erforderlich. Nur diese EXE wird verteilt. Vor einer breiten Verteilung muss sie wie die Hauptanwendung signiert und auf einem sauberen Unternehmens-PC geprüft werden.

## Benutzerkonfiguration

Für Live-Kanbanize-Daten wird pro Windows-Benutzer optional gesetzt:

```powershell
[Environment]::SetEnvironmentVariable('VIBN_VICO_KANBANIZE_API_KEY', '<API-KEY>', 'User')
```

Ohne Key liest der Client den gemeinsamen ViCo-Cache schreibgeschützt. Für die automatische RDP-Anmeldung wird wie im Haupttool benötigt:

```powershell
[Environment]::SetEnvironmentVariable('VIBN_RDP_PASSWORD', '<RDP-PASSWORT>', 'User')
```

Der Dialog-Button funktioniert ohne hinterlegtes Passwort. Kennwort und API-Key werden nicht in die EXE kompiliert. Logs liegen unter `%LOCALAPPDATA%\GROB\VIBN_Tools_IBN\Logs`.

## Technische Grenze

Eine einzelne EXE verhindert nicht das Kopieren der Anwendung durch einen berechtigten Benutzer. Die Trennung stellt sicher, dass nicht benötigte Toolfunktionen und Hersteller-SDKs gar nicht im IBN-Paket vorhanden sind; sie ersetzt keine Windows-Geräteverwaltung, Code-Signatur oder zentrale Softwareverteilung.
