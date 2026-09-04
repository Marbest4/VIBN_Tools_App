# Refactoring Phase A: Bestandsaufnahme und Zielarchitektur

Stand: 4. September 2026  
Branch: `feature/vibn-tools-refactor`

Dieses Dokument ist die technische Ausgangsbasis für die schrittweise Weiterentwicklung. Phase A verändert bewusst noch keine produktive Fachlogik. Aussagen mit externen Abhängigkeiten werden nicht als vollständig verifiziert dargestellt.

## 1. Verifizierte Ausgangslage

| Prüfung | Ergebnis | Grenze |
| --- | --- | --- |
| `dotnet build VIBN_Tools_App.sln -c Release` | erfolgreich, 0 Warnungen, 0 Fehler | Build benötigte die separat bereitgestellte FEE-SDK-Testlaufzeit und einen internen NuGet-Paketordner |
| `Tests/CoreSmokeTests` | erfolgreich | keine Live-Zugriffe auf Kanbanize, TIA oder FEE |
| `Tests/ContainerGenerationSmokeTests` | erfolgreich; Interface5: 420, Interface7: 345 Signale | bekannte Beispieldaten, kein vollständiger fachlicher Golden Master |
| `Tests/UiStartupSmokeTests` | erfolgreich | prüft Initialisierung und Bindings, keine vollständigen Benutzerabläufe |

Das Repository enthält weder das proprietäre FEE-SDK noch eine eigenständig nutzbare interne Paketquelle. Ein frischer Rechner kann die Solution daher nicht ausschließlich aus dem Git-Stand reproduzieren. Das SDK wurde für die Prüfung nur über `FEE_SCREEN_SIM_ROOT` eingebunden und nicht verändert.

## 2. Solution und Abhängigkeitsrichtung

| Projekt | Ziel | Verantwortung | Wesentliche Abhängigkeiten |
| --- | --- | --- | --- |
| `VIBN_Tools` | .NET 8 Windows/WPF | Hauptanwendung, Views, ViewModels, Container-/FEE-Funktionen, Composition | Core, Infrastructure, TIA Client, FEE-SDK, Grob.UX |
| `VIBN_Tools.Core` | .NET 8 | testbare ViCo-/Kanbanize-Domäne, Ports und Policies | keine UI-/Herstellerabhängigkeit |
| `VIBN_Tools.Infrastructure` | .NET 8 | HTTP-, Datei-, Cache-, Rollen-, RDP- und Windows-Adapter | Core |
| `VIBN_Tools.Tia.Contracts` | .NET Standard 2.0 | serialisierbare Named-Pipe-Kommandos und DTOs | keine UI-Abhängigkeit |
| `VIBN_Tools.Tia.Client` | .NET 8 | typisierter Client zum isolierten TIA-Prozess | TIA Contracts |
| `VIBN_Tools.TiaBridge` | .NET Framework 4.8 | Siemens Openness in separatem Prozess | TIA Contracts, Siemens Engineering |
| `VIBN_Tools.IbnRemote` | .NET 8 Windows/WPF | reduzierte IBN-Remote-Anwendung | Core, IBN Infrastructure |
| `VIBN_Tools.IbnRemote.Infrastructure` | .NET 8 | bewusst begrenzte Read-/RDP-Adapter | Core |
| drei Smoke-Test-Projekte | Console | Core-, Generator- und WPF-Startup-Regressionen | jeweils gezielte Produktprojekte |

Die Trennung von Core, Adaptern und TIA-Bridge ist eine tragfähige Grundlage. Im WPF-Hauptprojekt liegen jedoch noch Fachlogik, Herstellerzugriffe und UI-Zustand eng zusammen. Insbesondere `ContainerGenerationPageVM` ist eine große Legacy-Klasse. Ein mechanisches Aufteilen ohne zusätzliche Golden-Master-Tests wäre regressionsgefährlich.

## 3. Composition und Laufzeit

1. `App.xaml.cs` initialisiert die statische Service-Fassade und protokolliert unbehandelte Dispatcher-Fehler.
2. `GlobalClasses/Services.cs` erstellt `CoreApi`, `FeeConnectionService`, `FeeObjectService` und `ProjectSettings`. Fehlt die FEE-Laufzeit, bleibt die Anwendung startbar und sperrt FEE-Funktionen.
3. `MainWindow.xaml.cs` erzeugt `MainWindowVM` direkt. Ein DI-Container wird nicht verwendet.
4. `Application/ViCoFeatureBootstrapper.cs` ist eine manuelle Composition Root für ViCo, Kanbanize, TIA und Special Devices. Einige Instanzen werden geteilt, andere pro View erzeugt.
5. TIA läuft aus Stabilitätsgründen über eine eigene .NET-Framework-Bridge je TIA-View. Das ist beizubehalten.

Ziel ist zunächst keine vollständige DI-Migration. Sinnvoller ist eine schrittweise Composition-Root-Bereinigung: gemeinsame Fähigkeiten als Interfaces, eindeutige Lebensdauern und keine neuen direkten statischen Zugriffe in ViewModels.

## 4. Navigation: Tab zu View, ViewModel und Services

| Navigation | View | ViewModel | Zentrale Dienste/Abhängigkeiten | Aktuelle Freigabe |
| --- | --- | --- | --- | --- |
| Project Settings | `SettingsPage` | `SettingsPageVM` | ProjectSettings, FEE-Verbindung/-Objekte, WorkstationDirectory, CredentialConfiguration, Versionsinfo | immer sichtbar |
| Kanbanize Karten | `KanbanizeCardPage` | `KanbanizeCardPageVM`, `VibnWorkplaceSynchronizationVM` | KanbanizeCardApiService, VibnWorkplaceSynchronizationService | Level 8 |
| ViCo | `ViCoWorkspacePage` | untergeordnete ViewModels | PC-/Projektsuche und Projekte/Favoriten | immer sichtbar |
| ViCo → PC-/Projektsuche | `ViCoSearchPage` | `ViCoSearchPageVM` | WorkstationCatalog/Search, Kanbanize Refresh/Configuration, RDP, Session, Netzwerk, Pfadauflösung, Preferences | innerhalb ViCo |
| ViCo → Projekte/Favoriten | `ViCoPage` | `ViCoPageVM` | ProjectCatalog/Search, Favorites, PathLauncher | innerhalb ViCo |
| Transfer | `ViCoCopyPage` | `ViCoCopyPageVM` | FileCopy, FolderSelection, WorkspaceContext, ProjectStructure | immer sichtbar |
| TIA Portal | `TiaPortalPage` | `TiaPortalPageVM` | NamedPipeTiaBridgeClient, TiaLibraryService, FolderSelection | immer sichtbar |
| Administration | `ViCoAdministrationPage` | `ViCoAdministrationPageVM` | RoleStore, Outlook Meetings, UpdateService, PathLauncher | Level 9 |
| CAD Wizard | `CadWizardPage` | `CadWizardPageVM` | ProjectSettings und bestehende CAD/FEE-Hilfen | Level 7 |
| Zuli Converter | `ZuliConverterPage` | `ZuliConverterPageVM` | Excel-/ZuLi-Konvertierung | immer sichtbar |
| Container Generation | `ContainerGenerationPage` | `ContainerGenerationPageVM` | ZuLi/Requirements-Reader, Generator, Reimport/Reconciliation, Persistenz, ActionLog | Level 7 |
| Container2Fee | `ContainerToFeePage` | `ContainerToFeePageVM` | Container-Reader, FEE-Factories/-Wrapper | Level 7, Ausführung zusätzlich FEE-Gate |
| Container2FEE Visual | `ContainerToFeeVisualPage` | `ContainerToFeeVisualPageVM` | Planning, Discovery, Sidecar, Binder, Executor | Level 7, Ausführung zusätzlich FEE-Gate |
| Special Devices | `SpecialDevicePage` | `SpecialDevicePageVM` | eigene TIA-Bridge, HardwareMappingStore, FEE-Import | sichtbar; FEE-Aktion gegated |
| Model Validation | `ModelValidationPage` | `ModelValidationPageVM` | statische FEE-Services/Wrapper | nur mit FEE-Verbindung bedienbar |
| Model Control | `ModelControlPage` | `ModelControlPageVM` | statische FEE-Services/Wrapper | nur mit FEE-Verbindung bedienbar |
| Interface Operation | `InterfaceOperationPage` | `InterfaceOperationPageVM` | FEE-Interfaces und Verbindungslogik | sichtbar; einzelne Aktionen gegated |
| AI-Test | `AITrainingTestPage` | `AITrainingTestPageVM` | ActionLogger, ML.NET-Training/Evaluation, Containerdaten | Level 8 |

Abweichungen zum Zielbild:

- Die linke Navigation kann nicht zwischen Symbol- und Symbol/Text-Modus wechseln.
- Verfügbarkeitsgründe sind nicht einheitlich modelliert; häufig existieren nur `bool`-Gates oder pauschale Tooltips.

Transfer, TIA und Administration wurden im ersten kleinen Umsetzungsschritt nach Phase A in die Hauptnavigation verschoben. Das redundante Workspace-Level-8-Gate wurde entfernt; Administration folgt jetzt dem zentral berechneten Level-9-Gate.

## 5. Fachmodelle und Datenflüsse

### 5.1 Container Generation

`ComponentContainer` bildet einen Container mit ID, Komponente, Typ, Min/Max und `DataList` ab. `ContainerEntry` enthält unter anderem Laufzeit-`SignalId`, XML-ID, Adresse, Datentyp, Signal, Slot, Notiz sowie Prüf- und Änderungszustand. `ContainerData` ergänzt Slots, Gültigkeit, `ManuallyChecked` und Validierungsinformationen.

Der aktuelle Importfluss besitzt bereits wichtige Stabilitätsbausteine:

- `GenerationWorkspaceSnapshot` als persistierbarer Arbeitsstand,
- `GenerationWorkspaceReconciler` für den erneuten ZuLi-/Requirements-Import,
- feldgenaue `ReimportDifference`-Einträge und selektive Entscheidungen,
- Undo/Redo für bis zu 20 Aktionen,
- Review-Markierungen, Signal-IDs, Zusammenfassung und ActionLog.

Offene Kernpunkte:

- Der Vergleich zweier fertiger ContainerFiles mit selektiver Übernahme existiert nicht.
- Die Validierung behandelt doppelte Slots derzeit pauschal als Fehler. Die geforderte unterschiedliche Semantik für `PLC_OUT` und `PLC_IN` ist noch nicht als Domänen-Policy modelliert.
- Erzeugung und FEE-Abbildung verteilen Typwissen über Switches, Factories, Slot-Reflection und einen separaten Metadatenkatalog.

### 5.2 FEE und Container2FEE

Die FEE-Seite verwendet Wrapper wie `FeeAbstractObject`, `FeeLogic`, `FeeInterface`, `FeeInterfaceSignal` und Logiktypen wie `FeeSimpleMove`, `FeeNot`, `FeeAnd` und `FeeOr`. Die visuelle Variante besitzt bereits getrennte Bereiche für Planning, Discovery, Persistence und Execution sowie einen fingerprintgeschützten Sidecar.

Der aktuelle Plan unterscheidet Container, Logiken, Signale, technische Ziele, Erzeugungsauswahl, Interface-Auswahl und Kanten. Die Ausführung kennt jedoch getrennte Modi „Signale erzeugen“ und „vorhandenes Interface wiederverwenden“. Das Zielverhalten – alle vorhandenen Interfaces durchsuchen, passende Signale wiederverwenden und nur fehlende Signale in der Grob Generation Interface erzeugen – ist noch nicht implementiert.

Für eine Rückrichtung FEE → Container fehlt ein kanonisches Zwischenmodell. In bestehenden FEE-Modellen sind ursprüngliche Container-ID, Typ und Slot nicht überall eindeutig als Provenienz hinterlegt. Eine verlustfreie Rückabbildung beliebiger historischer Modelle kann deshalb nicht zugesagt werden.

Empfehlung: zuerst ein gemeinsames semantisches Mapping-Modell und eine Mapping-Policy für beide Richtungen einführen. Neue Generationen erhalten zusätzlich stabile Provenienz. Historische Modelle werden heuristisch eingelesen und müssen Unsicherheiten explizit anzeigen.

### 5.3 TIA

`VIBN_Tools.Tia.Contracts` transportiert Prozess-, Projekt-, PLC-, Bibliotheks- und Hardwareinformationen. `TiaHardwareModuleInfo` enthält bereits Geräteindex, Slot/Subslot, Geräte-/Modulname, Typen, Hersteller-/Bestelldaten, Netzwerkdaten und Ein-/Ausgangsadressen. Tiefe, Elternknoten, konkrete TIA-Objektklasse, Hardware-ID sowie Kandidaten-/Zuordnungsdiagnose fehlen.

Die Achsenkonfiguration ist aktuell ein einzelner mutierender Ablauf: Achsen werden gesucht und sofort alle konfiguriert. Es gibt noch keinen read-only Schritt „Achsen lesen“, keine stabile Auswahlidentität und keine Auswahl Alle/Keine. Die Namensheuristik X/Y/Z für linear/rotatorisch ist fachlich riskant und darf nicht ohne Live-Abnahme erweitert werden.

`Save` speichert das angehängte TIA-Projekt. Bibliotheksimport/-export arbeitet mit VICOBIB-Blöcken und Datentypen, aber die UI erklärt Wirkung, Voraussetzungen und Speicherverhalten nicht ausreichend.

### 5.4 Special Devices

Der Hardware-Reader traversiert die TIA-Hierarchie und projiziert derzeit überwiegend adressführende Blätter. Die automatische Namenswahl enthält bereits Heuristiken zwischen Geräte-, PROFINET- und Modulnamen. Vor weiteren Änderungen ist die geforderte Diagnoseansicht nötig, damit Geräteindex, Hierarchie und echte Openness-Typen an realen Projekten sichtbar werden.

### 5.5 ViCo und Kanbanize

ViCo besitzt testbare Core-Interfaces und Infrastrukturadapter. Tabelle und Commands verwenden aber noch ein zu grobes gemeinsames Offline-Gate. Dadurch verschwinden oder sperren auch Aktionen, die nicht zwingend einen erreichbaren Remote-PC benötigen. Datum, Spaltenmodell, Kontextmenü, kopierbare Pfade und gefilterte Detailinformationen fehlen im Zielumfang.

Die Arbeitsplatz-Synchronisation ist idempotent und vergleicht Kalendertage ohne Uhrzeit. Aktuell werden nur Grundinbetriebnahme-Karten berücksichtigt; Nachpflege, strukturierte Rollen-/Zusatzbezeichnungen, die differenzierten Mehrfachtrefferregeln und Planansicht-URL fehlen. Die UI formatiert Termine weiterhin mit Uhrzeit.

### 5.6 Zugangsdaten

Project Settings verwendet bereits verdeckte `PasswordBox`-Eingaben mit Two-Way-Behavior. API-Key und RDP-Passwort werden als Windows-Benutzerumgebungsvariablen gespeichert und zur Laufzeit aktualisiert. Damit müssen sie pro Benutzer/Rechner nicht bei jedem Start eingegeben werden, aber sie sind kein dedizierter Secret Vault und werden nicht automatisch zwischen Rechnern synchronisiert.

Zieloption: Windows Credential Manager oder DPAPI-geschützter lokaler Store hinter dem bestehenden `IUserCredentialConfigurationService`. Für mehrere Rechner ist weiterhin eine einmalige Einrichtung je Windows-Profil oder eine zentral verwaltete, organisationskonforme Secret-Verteilung erforderlich. Eine öffentliche oder repositorybasierte Speicherung ist ausgeschlossen.

## 6. Zielarchitektur in kleinen Schritten

1. **Sicherheitsnetz:** bestehende Smoke-Tests beibehalten, Golden Master für Requirements + Container ergänzen, fachliche Slot-Policies isoliert testen.
2. **Navigation und Capability-Modell:** Navigationseinträge als Datenmodell, Level-/FEE-/Online-/Konfigurationsvoraussetzungen mit einheitlichem `Availability`-Objekt aus Grund und Tooltip.
3. **Systemerkennung:** read-only `IInstalledComponentDiscovery` für FEE, TIA/Openness, WinCC/Siemens-Komponenten und TwinCAT; keine Änderung der SDK-Auswahl zur Laufzeit.
4. **ViCo/Kanbanize:** Core-Modelle erweitern, dann ViewModels und UI. Live-Schreibzugriffe erst nach Preview- und Contract-Tests.
5. **Container-Domäne:** Slot-Policy, semantischer Containervergleich und Golden Master vor weiterer Zerlegung des großen ViewModels.
6. **Container2FEE:** kanonisches Mapping-Modell, deterministische Signalauflösung, präzise Planvalidierung, anschließend UI-Vereinfachung.
7. **TIA/Special Devices:** Protokoll zuerst um read-only Diagnose und Achsenliste erweitern; Schreibkommandos separat und selektiv.
8. **KI-Regelvorschläge:** versioniertes Ereignisschema und deterministische Aggregation vor ML-Modellen; niemals automatische XML-Änderung ohne Vorschau, Backup und Auswahl.
9. **FEE2Container:** Reverse-Extractor auf demselben Mapping-Modell, Provenienz für neue Modelle, Unsicherheitsdiagnose für Altmodelle, semantische Round-Trip-Tests.

`FEE2SpecialDevices` sollte als eigener Hauptreiter umgesetzt werden, aber denselben FEE-Root-Selektor und Reverse-Extractor wie `FEE2Container` verwenden. Hardware-/TIA-Zielmodell, Validierung und Ausgabe unterscheiden sich stark genug, dass eine Integration in dieselbe Arbeitsfläche die Bedienung und Testbarkeit verschlechtern würde.

## 7. Hauptrisiken

| Risiko | Auswirkung | Gegenmaßnahme |
| --- | --- | --- |
| Proprietäres FEE-SDK und interner NuGet-Feed fehlen im Repository | nicht reproduzierbarer Build/CI | dokumentierte Bootstrap-Prüfung, interner Windows-Agent, SDK niemals kopieren oder verändern |
| Container-Golden-Master deckt Requirements noch nicht vollständig ab | unbemerkte Generatorregression | freigegebene Requirements- und erwartete Containerdatei versionieren oder intern referenzieren |
| Großes `ContainerGenerationPageVM` | hohe Kopplung und UI-Regressionen | erst Policies/Services extrahieren, dann UI; jeder Schritt mit Golden Master |
| Uneindeutige PLC_IN-/PLC_OUT-Fachregel | falsche FEE-Verknüpfungen | Regel vor Implementierung mit konkreten XML-/FEE-Beispielen festlegen |
| TIA-Openness-Versionen und Proxytypen | Laufzeitfehler trotz erfolgreichem Build | Bridge isoliert lassen, DTO-kompatibel erweitern, reale Projekte versionenweise abnehmen |
| FEE-Rückabbildung ohne Provenienz | Datenverlust oder falsche Container | Altmodelle nur mit Confidence/Diagnose, neue Modelle mit stabilen IDs |
| Kanbanize-Titel als implizites Datenmodell | falsche Konflikte/Duplikate | Titelgrammatik und Rollen als explizite Parser-/Policy-Tests |
| UI-Farben allein als Status | schlecht zugänglich und missverständlich | zusätzlich Text, Icon, Tooltip und Filter; Farbkontrast testen |
| Umgebungsvariablen für Secrets | lokal auslesbarer als Vault | Credential-Store-Adapter; keine Logs, Exporte oder Repositorywerte |
| Live-Schreibzugriffe auf FEE/TIA/Kanbanize | externe Seiteneffekte | Preview, selektive Bestätigung, Backup/Idempotenz und getrennte Live-Abnahme |

## 8. Tatsächlich blockierende Fachfragen

Diese Fragen blockieren nicht Navigation, Diagnose, read-only Discovery oder Testausbau. Sie blockieren jeweils die genannte mutierende Fachfunktion:

1. **PLC_IN/PLC_OUT:** Welche konkrete Mehrfachbelegung ist bei `PLC_IN` erlaubt, und wie müssen Quelle, Ziel, Richtung und Benennung der `FeeSimpleMove`-Verknüpfung für jedes Signal aussehen? Ein minimales Sollbeispiel mit zwei Signalen auf demselben Slot wird benötigt. Für `PLC_OUT` ist zu bestätigen, ob jede zweite Belegung unabhängig von Signal-ID/Adresse zwingend ein Fehler ist.
2. **Grob Generation Interface:** Woran wird dieses Interface stabil erkannt – exakter Name, Typ, Provider/GUID oder anderes SDK-Merkmal? Wie wird bei mehreren Treffern entschieden?
3. **Kanbanize-Rollenlogik:** Welche verbindliche Titel-/Feldgrammatik kennzeichnet Rolle (`CLIENT`, `CORE`) und Zusatzbezeichnung? Benötigt werden anonymisierte Beispiele für „gleicher Termin“, „unterschiedliche Rolle“ und „CORE doppelt“.
4. **FEE2Container-Altbestand:** Muss die erste Version beliebige historische FEE-Modelle verlustfrei rückwandeln, oder dürfen nur künftig von VIBN Tools erzeugte Modelle mit Provenienz vollständig round-trip-fähig sein? Für Altmodelle kann realistisch nur eine diagnostizierte heuristische Zuordnung zugesagt werden.

Für die selektive Achsenkonfiguration wird zusätzlich ein reales, nicht sensibles Beispielprojekt oder eine exportierte Achsenstruktur für die Live-Abnahme benötigt. Die read-only Protokollerweiterung kann vorher implementiert werden.
