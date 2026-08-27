# Software-Audit VIBN Tools App

Stand: 27. August 2026. Prüfobjekt ist die vollständige Solution `VIBN_Tools_App.sln` am Branch `codex/architecture-quality-deployment-audit`.

## 1. Executive Summary

Die Anwendung ist eine funktionsreiche WPF-Desktop-Suite mit gewachsenen Legacy-Anteilen und bereits gut abgetrennten ViCo-, Kanbanize- und TIA-Komponenten. Die wichtigste technische Stärke ist die Prozessisolation der Siemens-TIA-Openness-Anbindung. Das größte Buildrisiko war die nicht reproduzierbare Bindung an eine konkrete fe.screen-sim-Version. Dieses Risiko ist reduziert: Paketversionen sind explizit fixiert, der FEE-SDK-Pfad wird automatisch ermittelt und Build sowie Veröffentlichung prüfen die SDK-Vollständigkeit.

Die TIA-Hardwareauslesung berücksichtigt nun Root-Geräte, Geräteordner, verschachtelte Geräteordner und die Systemgruppe für nicht gruppierte dezentrale Geräte. Sie liefert Gerät, Gerätetyp, Hersteller, Bestellnummer, Firmware, GSD-Metadaten, PROFINET-Name, IP-Adresse, Slot, Subslot, Modulpfad und getrennte Ein-/Ausgangsbereiche. Die doppelte Hardwareansicht unter ViCo/TIA Portal wurde entfernt; die Zuordnung befindet sich ausschließlich unter Special Devices.

Validierter Zustand:

- Debug-Build der WPF-Anwendung (`win-x64`, self-contained): 0 Warnungen, 0 Fehler.
- Core-Smoke-Tests: vollständig bestanden.
- WPF-Startup-Test: alle integrierten Views ohne XAML- oder Bindingfehler initialisiert.
- TIA-Bridge `net48`: separat mit 0 Warnungen, 0 Fehlern gebaut.
- Synthetischer TIA-Test: Root-, Gruppen-, Untergruppen- und Ungrouped-Geräte einschließlich Adressen bestanden.
- Bekannte Ausnahme: `ContainerGenerationPageVM` besitzt wieder 2.387 Zeilen, weil die nicht ausreichend getestete Aufteilung den ZULI-Import beeinträchtigte und deshalb funktionssicher zurückgenommen wurde.

## 2. Priorisierte Befunde

| ID | Befund | Priorität | Risiko | Aufwand | Nutzen | Status |
| --- | --- | --- | --- | --- | --- | --- |
| A-01 | FEE-DLL-Pfade waren auf `5.0.9.44419` fest verdrahtet | Kritisch | Build/Start auf anderen PCs scheitert | Mittel | Sehr hoch | Behoben |
| A-02 | Hardwarebaum wurde abgeflacht; Slot/Subslot, GSD und Netzwerkdienste fehlten | Kritisch | Falsche Special Devices und E/A-Adressen | Hoch | Sehr hoch | Implementiert und synthetisch geprüft; Live-Abnahme offen |
| A-03 | `ContainerGenerationPageVM` bündelt 2.387 Zeilen | Hoch | Regressionen, geringe Testbarkeit | Hoch | Hoch | Aufteilung wegen ZULI-Regression zurückgerollt; Golden-Master-Test zuerst |
| A-04 | Paketversionen waren nicht reproduzierbar fixiert | Hoch | Versionsdrift/transitive Konflikte | Niedrig | Hoch | Explizit fixiert; zentrale Verwaltung erst nach VS-Vereinheitlichung |
| A-05 | Zwei veraltete, vom Build ausgeschlossene Sensorimplementierungen | Mittel | Verwirrung und falsche Erweiterungspunkte | Niedrig | Mittel | Entfernt |
| A-06 | Zweite Solution verwies außerhalb des Repositories | Hoch | Falscher Build-Einstieg | Niedrig | Hoch | Entfernt |
| A-07 | Service-Locator `Services` koppelt ältere ViewModels an globale Zustände | Hoch | Isolierte Unit-Tests schwierig | Hoch | Hoch | Geplanter Folgeschritt |
| A-08 | Einige allgemeine `catch (Exception)` an Legacy-Grenzen | Mittel | Ursachen können zu grob behandelt werden | Mittel | Mittel | Bericht/gezielte Migration |
| A-09 | Benutzer-Secrets liegen in Benutzer-Umgebungsvariablen | Hoch | Für lokale Prozesse auslesbar | Mittel | Hoch | Sicherheits-Folgeschritt: Credential Manager/DPAPI |
| A-10 | Event-Abonnements langlebiger ViewModels haben keinen einheitlichen Lifecycle | Mittel | Speicherbindung nach Viewwechsel möglich | Mittel | Mittel | `IDisposable`/Activation-Pattern empfohlen |
| A-11 | Nur Smoke-/Policy-Tests, geringe Abdeckung der FEE-Legacylogik | Hoch | SDK-Regressionsrisiko | Hoch | Hoch | Testpyramide erweitern |
| A-12 | Live-TIA-Verifikation benötigt reale TIA-Projekte und Openness-Rechte | Hoch | Mock-Test deckt Siemens-Runtime nicht ab | Mittel | Hoch | Live-Abnahmecheckliste |
| A-13 | Project Settings verwendet weiterhin die bestehenden festen FEE-Anmeldedaten `admin/admin` | Hoch | Zugangsdaten im Quelltext, keine Rotation | Mittel | Hoch | Bewusst nicht in diesem Funktionsfix geändert; Sicherheits-Folgeschritt |

## 3. Architekturprüfung

### SOLID und Clean Architecture

`VIBN_Tools.Core` ist plattformneutral und enthält Richtlinien, Modelle und Ports. `VIBN_Tools.Infrastructure` implementiert Windows-, Dateisystem-, HTTP- und Kanbanize-Adapter. `VIBN_Tools.Tia.Client` hängt nur von den serialisierbaren Contracts ab; die Siemens-Assembly bleibt im separaten `net48`-Bridgeprozess. Diese Richtung entspricht Dependency Inversion.

Der ältere FEE-/WPF-Bereich verwendet teilweise weiterhin globale Services. Ein Big-Bang-Umbau wäre mit der Vorgabe der Funktionsidentität nicht vertretbar. Neue Funktionen sollten ausschließlich über Konstruktorinjektion und kleine Interfaces eingebunden werden; bestehende ViewModels werden strangweise migriert.

### MVVM und Separation of Concerns

Die Views verwenden Commands und OneWay-Bindings für reine Statuswerte. Code-behind besteht überwiegend aus WPF-Lifecycle-Ereignissen. Der Versuch, den Container-Generator vor vollständigen fachlichen Regressionstests in Zustands-, Workflow- und UI-Klassen aufzuteilen, beeinträchtigte den ZULI-Import. Deshalb entspricht `ContainerGenerationPageVM` wieder exakt dem zuvor funktionierenden Stand. Eine erneute Zerlegung ist weiterhin sinnvoll, darf aber erst nach Golden-Master-Tests mit realen ZULI-/Containerdateien erfolgen.

### Thread-Sicherheit und Speicher

Kanbanize-/Dateisystemaufrufe sind asynchron; CPU- bzw. blockierende SDK-Arbeit wird gezielt ausgelagert. UI-Collections werden im ViewModel-Kontext geändert. Periodische ViCo-Aktualisierung verwendet Cancellation Tokens. Potenzielle Restpunkte sind ein einheitlicher View-Lifecycle für Events sowie Cancellation bis in den langen TIA-Lesevorgang.

## 4. Code-Quality-Bericht

### Vorher/Nachher

| Vorher | Nachher | Begründung |
| --- | --- | --- |
| Feste relative `Program Files/.../5.0.9...`-HintPaths | `$(FeeScreenSimRoot)` mit explizitem, Umgebungs-, Repository- und Versionsfallback | Rechner- und versionsunabhängiger Build |
| Nicht reproduzierbare Paketstände | Explizit fixierte Version an jedem `PackageReference` | Stabiler Restore auch mit den vorhandenen Visual-Studio-/NuGet-Versionen |
| Dutzende einzelne XML-Copy-Einträge | `Content\**\*.xml` mit `PreserveNewest` | Neue Definitionen werden automatisch ausgeliefert; weniger Build-I/O |
| Hardwarelogik in der allgemeinen TIA-Session | `TiaHardwareReader` als read-only Adapter | Single Responsibility und isolierbare Versionsgrenze |
| Modulidentität ohne Hierarchie/Subslot | Identität aus Gerät, Modulpfad, Slot, Subslot und Typkennung | Keine falsche Deduplizierung gleichartiger Submodule |
| Nicht abgesicherte Aufteilung des 2.387-Zeilen-ViewModels | Exakter Rollback auf den funktionierenden ZULI-/Container-Stand | Funktionsidentität vor Strukturziel; erneute Aufteilung erst testgetrieben |
| Dekorative Leer-/Kommentarblöcke im Model-Control-VM | kompakte, inhaltlich identische Datei | Lesbarkeit; kein Laufzeitunterschied |
| Ausgeschlossene Sensor-Prototypen und tote Solution | entfernt | Nur erreichbarer, buildbarer Code bleibt |
| `field` als lokale Variable kollidierte mit C# 14 | `configurationField` | Zukunftsfester Build ohne Semantikänderung |

### Naming, Fehlerbehandlung und Logging

Neue Typen und Properties folgen PascalCase; private Felder verwenden `_camelCase`. Legacy-Methoden mit Unterstrichen bleiben bestehen, weil Commands und bestehende Dokumentation darauf verweisen. Neue Grenzen loggen Kontext und zeigen dem Benutzer eine handlungsfähige Meldung. Allgemeine Exceptions in Reflection-/Interop-Grenzen sind vertretbar, müssen dort aber bewusst auf „Attribut nicht unterstützt“ begrenzt bleiben.

## 5. Performanceanalyse

- WPF-DataGrids nutzen Row- und Column-Virtualisierung.
- FEE-Content wird nur bei Änderungen kopiert (`PreserveNewest`).
- TIA wird einmal pro Hardwareanforderung traversiert; Sortierung erfolgt erst nach der Erfassung.
- ViCo-Netzwerkabfragen sind begrenzt parallelisiert und abbrechbar.
- Container- und Dateiparsing blockieren den UI-Thread nicht.

Noch zu messen: reale TIA-Großprojekte, Container2FEE gegen produktive SDK-Assemblies und Kanbanize-Latenzen. Optimierungen ohne Profildaten würden die Funktionsidentität unnötig gefährden.

## 6. Sicherheitsbericht

Im Repository wurde kein produktiver Kanbanize-API-Key und kein RDP-Passwort gefunden. Die aktuelle Ersteinrichtung legt diese Werte als Benutzer-Umgebungsvariablen ab. Das verhindert Quellcode-Secrets, ist aber kein sicherer Secretspeicher. Die bestehende FEE-Verbindung in `SettingsPageVM` verwendet dagegen noch `admin/admin`; diese Altlast wurde im aktuellen Funktionsfix nicht verändert. Empfohlen ist als separater Sicherheitsschritt:

1. Windows Credential Manager oder DPAPI-geschützte Datei pro Benutzer.
2. Protokoll-Redaction für Header, Tokens und Kennwörter.
3. Signierung von Setup und Binärdateien.
4. Least-Privilege-Kanbanize-Key und dokumentierte Rotation.
5. SBOM und Dependency-/Secret-Scan in CI.

## 7. Modernisierung

Die WPF-Anwendung bleibt vorerst auf .NET 8, die TIA-Bridge auf .NET Framework 4.8. Das ist die risikoärmste Kombination für bestehende FEE- und Siemens-Abhängigkeiten. Eine Umstellung auf .NET 10 LTS wird erst nach Herstellerfreigabe und einem vollständigen FEE-/TIA-Regressionstest empfohlen. Die Prozessgrenze erlaubt, die WPF-App später unabhängig von der Siemens-Bridge zu modernisieren.

## 8. Priorisierte To-do-Liste

1. Reale TIA-Abnahme mit PN/PN Coupler, dezentraler IO und GSD-Geräten aus V15–V22.
2. Produktives FEE-SDK in einem privaten, versionierten NuGet-Feed bereitstellen.
3. Setup signieren und über eine definierte Updatequelle verteilen.
4. Secret-Speicherung auf Credential Manager/DPAPI migrieren.
5. Legacy-Service-Locator strangweise durch Konstruktorinjektion ersetzen.
6. Unit-/Contract-Tests für Container2FEE und Hardware-Snapshots ergänzen.
7. Event-Lifecycle mit `IDisposable` oder View-Aktivierung vereinheitlichen.
8. ZULI-Import und Container-Generierung mit freigegebenen Referenzdateien als Golden-Master-Test absichern und erst danach `ContainerGenerationPageVM` erneut aufteilen.
