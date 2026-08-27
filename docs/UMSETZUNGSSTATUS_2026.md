# Umsetzungsstatus des Gesamtauftrags

Stand: 27. August 2026. „Automatisch geprüft“ bezeichnet lokale, externe Systeme nicht verändernde Tests. FEE-, TIA- und Kanbanize-Schreibzugriffe benötigen weiterhin eine Abnahme in der jeweiligen Produktivumgebung.

## Aktueller Änderungsumfang

| Nr. | Anforderung | Status | Nachweis / technische Grenze |
| ---: | --- | --- | --- |
| 1 | Container Generation / Open Interface File | Implementiert und automatisch geprüft | `SixLabors.Fonts` ist explizit auf die mit ClosedXML und NPOI gemeinsam kompatible Version `1.0.1` festgelegt. Der bisherige Kopiervorgang des kompletten FEE-`Bin`-/Pluginbaums konnte diese NuGet-DLL nach dem Build überschreiben und verursachte den `FontMetrics.TryGetGlyphMetrics`-Fehler. Der Releasepfad übernimmt nur noch die aus direkten Referenzen rekursiv ermittelte `FS.*`-Closure und die explizite `ReadingUnitPlugin.dll`. `Interface5.xlsx` wurde mit 420, `Interface7.xlsx` mit 345 Signalen über den echten `ZuLiDefault`-Import und anschließend den `ContainerGenerator` verarbeitet. |
| 2 | Kanbanize-Datum ohne Uhrzeit vergleichen | Implementiert und automatisch geprüft | `VibnWorkplaceSynchronizationPolicy.HasEquivalentDeadline` vergleicht den lokalen Kalendertag. `27.08.2026 08:00` und `27.08.2026 16:30` lösen keine Änderung aus; verschiedene Tage weiterhin schon. |
| 3 | Neue Karten vorauswählen / Alle selektieren | Implementiert und automatisch geprüft | Nur Vorschauzeilen mit Aktion `Create` starten mit `Sync=true`. „Alle selektieren“ und „Alle deselektieren“ ändern ausschließlich synchronisierbare Zeilen. Vorhandene Deadline-Updates bleiben aus Sicherheitsgründen zunächst unmarkiert. |
| 4 | FEE-abhängige Funktionen sperren | Implementiert, FEE-Live-Abnahme offen | Ein zentrales Gate folgt dem tatsächlich vom SDK bestätigten Verbindungszustand. CAD-Joints/Sensors/Templates/Delete, Container2Fee-Start, Special-Device-Erzeugung, beide vollständigen Modellmodule sowie Interface-Merge/-Connect sind deaktiviert und zeigen „Keine Verbindung zu FEE vorhanden.“. Zusätzlich verhindern Guards einen Aufruf über Commands. XAML und Commandpfade bauen fehlerfrei; der Wechsel gegen eine reale vollständige FEE-Laufzeit steht in der Abnahmecheckliste. |
| 5 | TIA-Hardware übersichtlich / Logik speichern | Implementiert, Live-Abnahme offen | Module werden nach Gerätename gruppiert. Angezeigt werden GSDML, IP, Modultyp, Firmware, E-/A-Bereich und -Länge, Präfix, Logik und Status. E-/A-Starts und Logik sind editierbar; die Zuordnung wird atomar unter `%LOCALAPPDATA%\GROB\VIBN_Tools\tia-hardware-mappings.json` gespeichert. Eine Zeile repräsentiert das zusammengehörige E-/A-Modul, weil die bestehende Special-Device-Factory genau eine Logik pro Gerät erzeugt. |
| 6 | Statusdokument fortführen | Umgesetzt | Dieses Dokument enthält den Implementierungs-, Test- und Abnahmestatus aller elf Punkte. |
| 7 | Setup-Erstellung vereinfachen | Implementiert und dokumentiert | `Build-Installer.ps1` erzeugt den Publish-Ordner direkt und überspringt das für den Installer unnötige ZIP. Der vollständige FEE-Ordner und fremde Plugins werden nicht mehr kopiert; die benötigte `FS.*`-Closure wird vor dem Publish rekursiv ermittelt und auf Lücken geprüft. Inno Setup 6 verpackt anschließend den self-contained Ordner in eine Setup-EXE. Ein reales Setup kann lokal erst mit einem vollständigen redistributierbaren FEE-SDK gebaut und auf einem sauberen PC abgenommen werden. |
| 8 | Projekt auf anderem Entwicklungsrechner | Implementiert und dokumentiert | `.vsconfig` beschreibt die Visual-Studio-Komponenten. `Prepare-Development.cmd` erkennt eine vollständige FEE-SDK-Version, überspringt unvollständige Installationen, setzt optional `FEE_SCREEN_SIM_ROOT` und restauriert die gesamte Solution. Visual Studio übernimmt danach die normale NuGet-Wiederherstellung. Zugriff auf Grob.UX beziehungsweise den internen Feed und ein vollständiges FEE-SDK bleiben notwendige, nicht eincheckbare Herstellerabhängigkeiten. |
| 9 | FEE-SDK-/Installationsversion anzeigen | Implementiert, automatisch teilgeprüft | Project Settings zeigt die tatsächlich neben der Anwendung verwendete `FS.SDK.dll`-Version sowie die höchste lokal erkannte fe.screen-sim-Version. Der Test erkennt die verwendete SDK-Version aus dem Build; unterschiedliche numerische Versionen werden rot hervorgehoben und protokolliert. Die Anzeige gegen reale Side-by-Side-Installationen bleibt Live-Abnahme. |
| 10 | FEE-Version auf Remote-PCs | Bewusst nicht direkt implementiert | Registry, Admin-Share, WMI/CIM und Dateiversionsabfragen sind remote nicht zuverlässig: Rechte, Firewall/Dienste, Side-by-Side-Installationen und portable Deployments liefern unterschiedliche oder keine Ergebnisse. Die ViCo-Seite weist darauf hin. Sichere Alternative ist ein verwalteter Inventardienst/Agent; bis dahin kann die betriebliche Angabe in `KONFIGURATION → SW` gepflegt werden. |
| 11 | TIA-Verbindung / Großprojekt / Abbruch | Implementiert, Großprojekt-Live-Abnahme offen | `TIA trennen / abbrechen` beendet die seitenexklusive Bridge, gibt die Openness-Session frei, leert PLC-/Hardwarelisten und setzt die UI zurück. Ein blockierter synchroner Openness-Aufruf kann durch Beenden ausschließlich des eigenen Bridge-Prozesses abgebrochen werden; TIA selbst bleibt geöffnet. Die Attach-Diagnose unterscheidet jetzt `Projects`, `LocalSessions` und Lesefehler und nennt PID, Modus und `ProjectPath`, statt Ausnahmen als `Count=0` zu verschlucken. |

## Automatische Verifikation

- Release-Build der gesamten `VIBN_Tools_App.sln`: 0 Warnungen, 0 Fehler.
- `Tests/ContainerGenerationSmokeTests`: `Interface5.xlsx` (420 Signale) und `Interface7.xlsx` (345 Signale), geladene `SixLabors.Fonts`-Dateiversion `1.0.1.0`.
- `Tests/CoreSmokeTests`: unter anderem Kanbanize-Datumsvergleich, Standardauswahl, Sammelauswahl und TIA-Clientvertrag.
- `Tests/UiStartupSmokeTests`: ViCo-, Kanbanize-, TIA-/Special-Device-Views und Bindings initialisiert; Zuordnungspersistenz und SDK-Versionsleser geprüft. FEE-Views benötigen für den Laufzeittest die vollständige Hersteller-Assemblyclosure.
- `Tests/Test-TiaHardwareTraversal.ps1`: Root-, gruppierte, verschachtelte und ungruppierte TIA-Geräte traversiert.

## Geänderte Komponenten

| Dateien / Bereich | Änderung |
| --- | --- |
| `VIBN_Tools.csproj`, `ContainerGeneration/Utils/ExcelReader.cs` | kompatible Font-Abhängigkeit, kuratierter FEE-Runtime-Copy und verständliche Konfliktdiagnose |
| `Tests/ContainerGenerationSmokeTests/*`, `Interface5.xlsx`, `Interface7.xlsx`, `VIBN_Tools_App.sln` | reproduzierbarer ZuLi-/Generator-Smoke-Test |
| `VIBN_Tools.Core/Kanbanize/VibnWorkplaceSynchronization.cs`, `Application/VM/VibnWorkplaceSynchronizationVM.cs`, `Application/View/KanbanizeCardPage.xaml` | datumsgleicher Vergleich, neue Karten markiert, Sammelbuttons |
| `Settings/ConnectionService.cs`, `Settings/FeeVersionInfoProvider.cs`, `Application/VM/SettingsPageVM.cs`, `Application/View/SettingsPage.xaml` | zentrales FEE-Gate und Versionsanzeige |
| FEE-abhängige ViewModels/Views und `MainWindow*` | Sperren, Tooltips und defensive Command-Guards |
| `Application/VM/SpecialDeviceHardwareImportVM.cs`, `Application/VM/SpecialDevicePageVM.cs`, `Application/TiaHardwareMappingStore.cs`, `Application/View/SpecialDevicePage.xaml` | gruppierte Hardwareansicht, editierbare Zuordnung, Persistenz, Abbruch/Trennen |
| `VIBN_Tools.Tia.Client/*`, `VIBN_Tools.TiaBridge/Openness/TiaOpennessSession.cs` | sauberer Bridge-Abbruch und aussagekräftige Großprojekt-Diagnose |
| `scripts/Build-Common.ps1`, `scripts/Publish-Portable.ps1`, `scripts/Build-Installer.ps1` | kuratiertes Deployment und einmalige Installer-Publishstufe |
| `.vsconfig`, `Prepare-Development.cmd`, `scripts/Prepare-Development.ps1` | reproduzierbare Entwickler-Ersteinrichtung |
| `Application/View/ViCoSearchPage.xaml` und Dokumentation unter `docs/` | ehrlicher Remote-FEE-Hinweis sowie Bedien-, Build- und Fehlersuchanleitung |

## Noch erforderliche Live-Abnahmen

1. Den großen TIA-Projekttyp anhand der neuen PID-/Projects-/LocalSessions-Diagnose prüfen und reale PN/PN-/GSDML-Adressen abgleichen.
2. FEE-Gates, SDK-Versionsabweichung und Special-Device-Erzeugung gegen die freigegebene vollständige FEE-Installation prüfen.
3. `VIBN_Tools_Setup.exe` mit dem vollständigen freigegebenen SDK bauen, signieren und auf einem sauberen Windows-x64-PC installieren.
4. Erst nach diesen fachlichen Referenztests weitere Legacy-Aufteilungen durchführen; bestehendes Verhalten hat Vorrang vor einer rein strukturellen Änderung.
