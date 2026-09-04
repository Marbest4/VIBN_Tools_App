# Anforderungsmatrix Refactoring 2026

Stand: 4. September 2026  
Status: **vorhanden**, **teilweise**, **offen**, **fachlich blockiert** oder **Live-Abnahme offen**.

Die Matrix trennt Implementierung, automatische Verifikation und externe Abnahme. Ein erfolgreicher Build ist kein Nachweis für korrektes Verhalten in FEE, TIA oder Kanbanize.

| Bereich / Anforderung | Ist-Stand | Betroffener Code | Geplanter Scope | Verifikation / Definition of Done |
| --- | --- | --- | --- | --- |
| Linke Navigation einklappbar | offen | `MainWindow.xaml`, `MainWindowVM` | datengetriebener Symbol/Text-Modus, Tastaturbedienung und persistierte Nutzerpräferenz | UI-Startup, Bindingtest, manuell bei kleiner/großer Auflösung |
| Transfer als Hauptnavigation | vorhanden | `MainWindow.xaml`, `ViCoWorkspacePage.xaml`, Bootstrapper | bestehende View/VM umgehängt; gemeinsamer WorkspaceContext bleibt erhalten | UI-Smoke initialisiert Transfer; manueller Navigationstest bleibt Release-Abnahme |
| TIA als Hauptnavigation | vorhanden, Live-Abnahme offen | gleiche Bereiche, `TiaPortalPage*` | View umgehängt; Bridge-Composition unverändert | UI-Smoke; Bridge-Connect/Disconnect weiter synthetisch und live abnehmen |
| Administration als Hauptnavigation, nur Level 9 | vorhanden | `MainWindowVM`, Rollen-Policy, `MainWindow.xaml` | zentrales Level-9-Gate; redundantes Workspace-Gate entfernt | Core-Test Level8/Level9 und XAML-/UI-Smoke; reale Rollenliste abnehmen |
| Tote Altlogik entfernen | offen | gesamte Solution, u. a. kommentierte MiniTools-/Legacy-Usings | nur nach Referenzsuche und Tests; keine pauschale Löschung | Build 0/0, alle Smokes, Diff-Review; keine unnötigen Dateien |
| Einheitliche Deaktivierungsgründe | teilweise | MainWindow, ViCo-, FEE-, TIA- und Generator-VMs | `Availability`/Prerequisite-Modell mit Grund, Tooltip und Command-Gate | Policy-Tests je Voraussetzung, UI-Bindingtests |
| Dynamische installierte Komponenten | teilweise; FEE und begrenzte TIA-Suche, kein Gesamtinventar | `FeeVersionInfoProvider`, `ViCoFeatureBootstrapper`, Settings | read-only Discovery für FEE, TIA/Openness, Siemens/WinCC, TwinCAT; genaue Pfad-/Versionsanzeige | Fixture-Tests, danach reale Side-by-Side-Abnahme |
| API-Key/RDP verdeckt binden | vorhanden | `SettingsPage`, `PasswordBoxBindingBehavior`, `SettingsPageVM` | Verhalten beibehalten; Fehlermeldungen vereinheitlichen | bestehende Core-/UI-Smokes |
| Zugangsdaten sicher persistent | teilweise; Benutzerumgebungsvariablen | `IUserCredentialConfigurationService`, `UserEnvironmentCredentialConfigurationService` | optionaler Credential-Manager/DPAPI-Adapter; Migration ohne Klartextlog | Fake-Store-Tests, manueller Windows-Profiltest; kein Secret in Log/Repo |
| Kanbanize Grundinbetriebnahme + Nachpflege | teilweise; nur Grundinbetriebnahme | Core Kanbanize Policy/Service, VM/UI | Quellkartentyp explizit modellieren und testen | Parser-/Policy-Tests mit anonymisierten Karten |
| Kanbanize Konfliktregeln Rollen/Zusätze | offen, fachlich blockiert durch Titelgrammatik | gleiche Bereiche | strukturierter Parser, deterministische Gruppierung und Konfliktgründe | Matrix aus Einzel-/Mehrfachtreffern und idempotenter zweiter Ausführung |
| Kanbanize Farbgruppen | teilweise; blau/gelb/hellgrün/rot | `VibnWorkplaceSynchronizationVM`, `KanbanizeCardPage.xaml` | dunkel-/hellgrün plus Text/Icon/Tooltip, nicht nur Farbe | UI-Zustandstests und Kontrastprüfung |
| Kanbanize Datum ohne Uhrzeit | Policy vorhanden, Anzeige noch mit Uhrzeit | Core Policy, `VibnWorkplaceSynchronizationVM` | Anzeige `dd.MM.yyyy`, Datumsvergleich beibehalten | bestehender Policy-Test plus Format-Test |
| Kanbanize Planansicht öffnen | offen | Kanbanize UI/Adapter, PathLauncher | konfigurierte Board-/Plan-URL mit prüfbarem Availability-Grund | URI-Unit-Test, manueller Launcher-Test |
| ViCo Projektstart/-ende | offen im Zeilenmodell | Core `Workstations`, Cache-/Kanbanize-Adapter, Zeilen-VM/UI | strukturierte Datumsfelder ohne Uhrzeit | Parser-Fixture und UI-Format-Test |
| ViCo exakte Spaltenreihenfolge | offen | `ViCoSearchPage.xaml`, RowVM | Zielreihenfolge, Projektspalte breiter, Virtualisierung erhalten | UI-Smoke plus manuelle Breitenprüfung |
| ViCo optionale Zusatzspalten | offen | View/VM, Preferences | erweiterte Informationen ein-/ausblendbar und persistent | Preference-Roundtrip und UI-Test |
| RDP/Login/Konfiguration aus Haupttabelle | offen | `ViCoSearchPage.xaml` | in Detail-/Kontextbereich verschieben, Daten nicht verlieren | UI-Test und Bedienprüfung |
| ViCo Zeilen-Kontextmenü | offen | `ViCoSearchPage.xaml`, bestehende Commands | bestehende Commands wiederverwenden, keine doppelte Logik | Command-Routing-/CanExecute-Test |
| Offline-Aktionen granular | offen; derzeit gemeinsames grobes Gate | `ViCoSearchPageVM`, UI | RDP/Prompt/PC-Ordner separat sperren; serverbasierte Pfade weiter nutzbar, genaue Gründe | Core-VM-Tests offline/online/kein Pfad |
| Pfade/Links auswählbar und kopierbar | offen | ViCo Views | read-only selektierbare Controls und Kopieraktion | UI-Smoke, manueller Copy/Paste-Test |
| Nur relevante Kanbanize-Infos | offen | Workstation-Projektion/Details-UI | strukturierte Feldauswahl statt Textfilter in der View | Adapter-Fixture und Snapshot-Test |
| TIA Achsen nur lesen | offen | TIA Contracts/Client/Bridge, `TiaPortalPageVM` | neues read-only Kommando mit stabiler Achsenidentität | Fake-Bridge-Contract-Test, TIA-Live-Abnahme |
| TIA Achsen Alle/Keine + selektiv konfigurieren | offen | gleiche Bereiche und UI | Auswahlmodell; Mutation nur für Auswahl | Unit-/Contract-Tests; reale Achsenwerte vor/nach dokumentieren |
| TIA tatsächliche Änderungen dokumentieren | teilweise im Code, nicht verständlich in UI/Doku | `TiaOpennessSession`, Benutzerhandbuch | Parameterliste, Voraussetzungen und Ergebnisprotokoll | Doku-Review und Live-Protokoll |
| TIA Save/Libraries erklären | teilweise | `TiaPortalPage.xaml`, `TiaPortalPageVM`, Doku | Tooltips/Infobox für Projekt-Save, VICOBIB-Ordner, Blocks/Types laden | UI-Test und manueller TIA-Ablauf |
| ContainerFile A/B vergleichen | offen | neue Container-Diff-Domäne, Generator-VM/UI | semantischer Vergleich, selektive Feld-/Zeilenübernahme, Vorschau | Unit-Tests Add/Remove/Change/Unchanged, Roundtrip/Golden Master |
| PLC_OUT nie doppelt | offen; generische Slot-Duplikatprüfung | `ContainerData.Validate`, neue SlotPolicy | explizite PLC_OUT-Regel und verständlicher Fehler | Policy-Tests inkl. unterschiedlicher IDs/Adressen |
| PLC_IN definierte Mehrfachbelegung | fachlich blockiert | SlotPolicy, `ContainerBaseClass`, FEE-Mapping | erlaubte Fälle explizit modellieren; alle Signale deterministisch abbilden | Sollfixture mit mehreren Signalen und erwarteten Moves |
| Container2FEE Bäume expand/collapse | offen | `ContainerToFeeVisualPage.xaml`, TreeNodeVM | beide Bäume global auf-/zuklappen | UI-Command-Test |
| Fehlende Ziele Alle/Keine | offen | Visual Plan/VM/UI | Auswahl nur für fehlende erzeugbare Objekte | Plan-Tests und UI-Test |
| Visual Layout Größen | offen | Visual XAML | Außenbereiche größer, Mitte kleiner, Resize erhalten | manuell bei 1366×768 und 1920×1080 |
| Separaten Signal-erzeugen-Schalter entfernen | offen | VisualPlan, VM/UI, Executor | deterministische Resolve-or-Create-Pipeline | Plan-/Executor-Tests mit vorhanden/fehlend/Duplikat |
| Fehlende SimObjects standardmäßig ausgewählt | offen; aktuell false | Planning/Persistence/VM | Default true nur für tatsächlich fehlende, erzeugbare Ziele; Sidecar-Entscheidung respektieren | Plan- und Sidecar-Roundtrip-Tests |
| Rot-/Grün-Zustände differenzieren | teilweise | TreeNodeVM/XAML | dunkel/hell rot/grün plus Text/Icon/Tooltip | Zustandsmatrix und Kontrastprüfung |
| Grob Generation Interface verwenden | fachlich blockiert durch Identität | Discovery/Executor/FEE wrappers | vorhandenes Interface eindeutig finden; passende Signale wiederverwenden, fehlende dort erstellen | Fake-/Integrationstest, FEE-Live-Abnahme |
| Kein Interface auswählbar | teilweise technisch null, UX offen | Visual Plan/VM/UI | sichtbare Option „Keines“, präzise Auswirkung | Planvalidation- und UI-Test |
| Präzise Disabled Reasons | teilweise | Visual VM/UI | FEE, Plan, Interface, Ziel, Sidecar getrennt erklären | Policy-Tests aller Kombinationen |
| Missing SimObjects nur regelgerecht warnen/blockieren | teilweise | PlanValidator/Executor | Warnung bei erzeugbar/ausgewählt; Blocker nur wenn erforderlich und nicht erzeugbar/gewählt | Validierungsmatrix |
| Special Devices umbenennen | offen | MainWindow, Doku | `SpecialDevices2FEE` konsistent | UI-Smoke und Textsuche |
| TIA-Hardwarediagnose vor Namensregel | offen | TIA DTO/Reader, SpecialDevice VM/UI | Index, Tiefe, Name, Parent, Typ, Objektklasse, Pfad, Hardware-ID und Zuordnungskandidaten anzeigen | synthetischer Baumtest plus reales Projekt |
| AI-Reiter Regelvorschläge | offen | AI View/VM, neue Core-Domäne/Persistenz | eigener Subtab mit Filter, Status, Accept/Reject | Aggregations-, Persistenz- und UI-Tests |
| Strukturierte manuelle Änderungen | teilweise; Slot/Add/Remove-JSONL | `ActionLogger`, Generatoränderungen | versioniertes Schema mit stabiler Signal-/Projekt-/Input-Identität und Property before/after | Schema-/Migrationstest, kein personenbezogener Inhalt |
| Aktionslog-Pfad öffnen | teilweise; Trainingsordner vorhanden | AI UI/VM, PathLauncher | expliziter Logpfad, auswähl-/kopierbar und öffnen | Pfad-/Launcher-Test |
| Vorschläge mit Häufigkeit/Fällen/Confidence | offen | neue RuleSuggestion Policy | deterministische Aggregation zuerst; ML nur ergänzend und evaluiert | Golden fixtures, Precision/Recall bzw. nachvollziehbare Confidence |
| Sichere Regelübernahme in XML | offen | Requirements Writer, Backup/Patch-Preview | manuell ausgewählte Vorschläge, Backup, atomarer Write, Diff-Vorschau | Tempfile-Tests inkl. Fehler/Rollback; reale Kopie, nie Original ohne Bestätigung |
| FEE2Container | offen | neue Reverse-Domäne, FEE Discovery, Container Writer/UI | Root-Auswahl, kanonischer Extractor, Validierung, Preview, Export | semantische Round-Trips und FEE-Live-Abnahme |
| FEE2SpecialDevices | offen; eigener Reiter empfohlen | gemeinsamer Reverse-Extractor plus eigene Hardwaredomäne/UI | eigener Hauptreiter, gemeinsame Root-/Discovery-Dienste | Hardware-Fixtures, semantischer Exporttest, Live-Abnahme |
| Dokumentation je Änderung | teilweise vorhanden | `docs/*` | Matrix und Status bei jedem Commit aktualisieren | Review: keine Funktion ohne Status/Limit |
| Tests/CI | gute lokale Smokes, kein vollständiger externer CI-Nachweis | `Tests/*`, Buildskripte, ggf. `.github/workflows` | Tests je Policy; Windows-Agent mit internen Abhängigkeiten; Live-Checkliste getrennt | Release-Build 0/0, alle Smokes, Live-Ergebnisse nicht vortäuschen |

## Geplante Commit-Reihenfolge

1. `docs: establish refactoring baseline and requirement traceability`
2. `refactor: introduce navigation capability and availability model`
3. `feat: add installed component discovery to project settings`
4. `feat: restructure ViCo navigation and workstation presentation`
5. `feat: extend Kanbanize synchronization policy`
6. `test: add container golden masters and slot policy fixtures`
7. `feat: add semantic container comparison and slot policies`
8. `feat: stabilize visual Container2FEE resolution and UX`
9. `feat: add read-only TIA axis and hardware diagnostics`
10. `feat: add rule suggestion review workflow`
11. `feat: add FEE reverse mapping and round-trip verification`

Jeder Commit soll einzeln bauen und die zu seinem Scope gehörenden Tests bestehen. Externe Schreibzugriffe werden nicht in automatischen Tests gegen Produktivsysteme ausgeführt.
