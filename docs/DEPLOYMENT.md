# Deploymentkonzept

## Vergleich

| Variante | Größe | Stabilität mit FEE/TIA | Updates | Benutzerkomfort | Empfehlung |
| --- | --- | --- | --- | --- | --- |
| Framework-dependent | Klein | Mittel; .NET muss vorhanden sein | Extern | Mittel | Für Entwickler |
| Self-contained Ordner | Groß | Sehr hoch | Paket ersetzen | Hoch | Portable Standardbasis |
| Single-file | Mittel/groß | Risiko bei dynamisch geladenen/native DLLs | Paket ersetzen | Hoch | Derzeit nicht verwenden |
| MSIX | Mittel | Gut, aber Installations-/Signaturregeln | Sehr gut | Hoch | Spätere verwaltete Verteilung |
| ClickOnce | Mittel | Gut | Sehr gut | Hoch | Alternative bei einfacher Updatequelle |
| WiX | Mittel | Sehr hoch | Eigene Logik | Hoch | Enterprise-MSI, hoher Aufwand |
| Inno Setup | Mittel | Sehr hoch | Eigene Logik | Sehr hoch | Empfohlenes Setup jetzt |
| Portable ZIP | Groß | Sehr hoch | ZIP ersetzen | Hoch | Support-/Offline-Fallback |

## Entscheidung

Die technische Basis ist ein self-contained, mehrteiliges `win-x64`-Publish. Single-file ist wegen FEE-SDK, ML-native Runtimes, Plugins und separat gestarteter TIA-Bridge nicht die stabile Wahl. Für Endanwender wird dieser Ordner mit Inno Setup zu einer einzigen `VIBN_Tools_Setup.exe` verpackt. Nach der Installation startet der Benutzer ausschließlich `VIBN_Tools.exe`; Visual Studio und eine separate .NET-Installation sind nicht nötig.

Grundlagen: [Microsoft – Windows-Verteilungswege](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/choose-distribution-path), [Microsoft – self-contained veröffentlichen](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/publish-first-app) und [Microsoft – Single-file Deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview).

TIA Portal/Openness und fe.screen-sim bleiben fachliche Laufzeitvoraussetzungen, sofern die jeweilige Funktion genutzt wird.

Das Publish-Skript prüft vorab die transitive `FS.*`-Assemblymenge. Ein reines Compile-Test-SDK wird abgelehnt, statt ein Paket zu erzeugen, das erst beim Benutzerstart mit `FileNotFoundException` endet. Alle DLLs im freigegebenen FEE-`Bin` sowie Plugin-Dateien werden automatisch in das Paket übernommen.

## Befehle

Eine vollständige Schritt-für-Schritt-Anleitung für Build- und Zielrechner befindet sich in [INSTALLATION_UND_INSTALLER.md](INSTALLATION_UND_INSTALLER.md).

Portable Paket:

```powershell
.\scripts\Publish-Portable.ps1 -FeeScreenSimRoot 'C:\Program Files\fe.screen-sim V5\<Version>'
```

Setup-EXE mit installiertem Inno Setup 6:

```powershell
.\scripts\Build-Installer.ps1 -FeeScreenSimRoot 'C:\Program Files\fe.screen-sim V5\<Version>'
```

Ergebnisse:

- `artifacts\publish\VIBN_Tools-win-x64.zip`
- `artifacts\publish\VIBN_Tools-win-x64.zip.sha256.txt`
- `artifacts\installer\VIBN_Tools_Setup.exe`

## Release-Checkliste

1. Release-Build und beide Smoke-Tests grün.
2. Reale TIA- und FEE-Abnahme durchgeführt.
3. Setup und Binärdateien signiert.
4. SHA-256 veröffentlicht und geprüft.
5. Installation auf sauberem Windows-x64-PC getestet.
6. Start, Konfiguration, ViCo/Kanbanize, TIA-Bridge und Deinstallation getestet.
