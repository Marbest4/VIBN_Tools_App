# Entwicklerhandbuch

## Architekturregel

Neue ViCo-, Kanbanize- und TIA-Funktionen folgen dieser Richtung:

```text
View (XAML) → ViewModel → Core-Modell/Interface → Infrastructure-Adapter
TIA: ViewModel → Tia.Client → Named Pipe → TiaBridge → Siemens Openness
```

`VIBN_Tools.Core` darf keine WPF-, HTTP-, Windows- oder Siemens-Abhängigkeit erhalten. `Infrastructure` implementiert Core-Verträge. `Application/VM` koordiniert Bedienung und Status, enthält aber keine Transportformate oder Geschäftsregeln. Die bestehende VIBN-Logik wird nur dort angefasst, wo eine neue Integration sie benötigt.

## Einstieg in den Code

1. `Application/View/MainWindow.xaml` zeigt alle Hauptreiter und die Rollen-Sichtbarkeit.
2. `Application/VM/MainWindowVM.cs` lädt dynamische Arbeitsplätze und die zentrale Rollenliste.
3. `Application/ViCoFeatureBootstrapper.cs` verbindet Core-Interfaces mit konkreten Infrastrukturdiensten.
4. Für den gewünschten Funktionsbereich die Tabelle in [KLASSENREFERENZ.md](KLASSENREFERENZ.md) verwenden.
5. Vor einer Änderung die korrespondierenden Smoke-Tests in `Tests/CoreSmokeTests/Program.cs` lesen.

## Erweiterungsmuster

### Neue ViCo-Arbeitsplatzinformation

1. Feld als neutrales Modell oder Vertrag in `VIBN_Tools.Core/ViCo` definieren.
2. Cache-/Kanbanize-Parsing in `LegacyWorkstationCatalog` bzw. `KanbanizeRefreshService` ergänzen.
3. Nur wenn editierbar: vorhandene Feld-/Subtask-ID im Core-Modell bewahren und einen eng begrenzten Infrastruktur-Write implementieren.
4. Anzeige in `ViCoWorkstationRowVM` und XAML ergänzen.
5. Parser- und Write-Scope-Test hinzufügen.

Die `KONFIGURATION`-Bearbeitung ist das Referenzmuster: vorhandene Unteraufgaben werden per PATCH geändert, fehlende Standard-Unteraufgaben per POST ergänzt. Eine fehlende Karte wird nur nach dem expliziten UI-Befehl standardisiert erstellt; normale Karten bleiben unberührt.

### Neue RDP-/Windows-Aktion

Zuerst ein Interface in `Workstations.cs` ergänzen. Danach eine konkrete Implementierung in `DesktopWorkstationServices.cs` schreiben und sie im Bootstrapper registrieren. Keine `Process.Start`-Aufrufe direkt aus einem ViewModel einfügen. Offline-Schutz und Fehlerprotokoll gehören in das ViewModel.

Ein RDP-Profil darf ausschließlich Ziel-PC, Benutzer, Monitorwahl und Abfragemodus enthalten. Der einzige Kennwortprovider ist `VIBN_RDP_PASSWORD`; `WindowsTemporaryRemoteCredentialStore` reicht den Wert über `ProcessStartInfo.ArgumentList` an `cmdkey`, protokolliert ihn nie und entfernt den Zieleintrag verzögert. Keine zweite Passwortquelle und kein Literal ergänzen.

`quser /server:<PC>` besitzt keinen sicheren Rechte-Bypass. Fehler 5 wird als Berechtigungsdiagnose an die Oberfläche gereicht. Alternative Implementierungen dürfen keine Credentials auslesen oder Berechtigungen umgehen.

### Neue Kanbanize-Funktion

1. Modell, Validierung und Fachregel in `VIBN_Tools.Core/Kanbanize`.
2. Eventuelle HTTP-Operation als schmalen Member von `IKanbanizeCardService` formulieren.
3. Den v2-Adapter in `VIBN_Tools.Infrastructure/Kanbanize/KanbanizeCardApiService.cs` umsetzen.
4. Payload auf das fachlich erlaubte Minimum begrenzen.
5. Einen `RecordingHttpMessageHandler`-Test hinzufügen, der Methode, URL und JSON-Felder prüft.

Keine generische „Update alles“-Methode einführen: Gerade beim Arbeitsplatz-Board ist der eng begrenzte Write-Scope Teil der Fachanforderung.

### Neue TIA-Operation

Die Reihenfolge ist verbindlich:

1. DTO und Kommandoname in `VIBN_Tools.Tia.Contracts`.
2. Member in `ITiaBridgeClient` und `NamedPipeTiaBridgeClient`.
3. Dispatch im `TiaCommandDispatcher`.
4. Implementierung in `ITiaOpennessSession` und `TiaOpennessSession`.
5. ViewModel-Command, Status und XAML.
6. Protocol-/Fake-Test in `Tests/CoreSmokeTests`.

Die Hauptanwendung darf keine Siemens-Openness-Assembly direkt laden. TIA-Fehler sind im ViewModel zu fangen und über `IApplicationLog` zu dokumentieren.

`TiaHardwareReader` ist kein unerreichbarer Code: Er wird über Named Pipe im separaten Prozess `VIBN_Tools.TiaBridge.exe` ausgeführt. Ein Breakpoint dort wird bei normalem F5 im WPF-Prozess nicht automatisch getroffen. Zum Debuggen nach dem Start der Hardwareabfrage in Visual Studio **Debuggen → An Prozess anfügen** wählen und `VIBN_Tools.TiaBridge.exe` auswählen. Die Bridge-Quellen sind als `UpToDateCheckInput` registriert, damit F5 nach einer Änderung keine alte kopierte Bridge startet.

`Address.Length` ist im Bridge-Modell eine Bitlänge. Nur `TiaHardwareReader` konvertiert mit Aufrundung in die zusätzlichen Bytefelder. UI oder Special-Device-Code dürfen die rohe Länge nicht ein zweites Mal umrechnen. Adresslose Hierarchieknoten liefern nur geerbte Metadaten; eine Tabellenzeile entsteht ausschließlich für einen konkreten E-/A-Adresssatz.

### Container2FEE Visual erweitern

Der alte Reiter und `ContainerToFeePageVM` bleiben die Verhaltensreferenz. Neue Planfunktionen gehören unter `ContainerToFeeVisual/`:

1. reine Struktur in `Domain` ergänzen;
2. XML-Metadaten in `Planning` erweitern, ohne FEE-Aufrufe auszuführen;
3. persistente, versionierte Nutzerdaten ausschließlich in `Persistence` ändern;
4. SDK-Objekte in `Discovery` kapseln;
5. tatsächliche Erzeugung weiterhin über `LegacyContainerToFeeExecutionAdapter` und `ContainerToFeeService` ausführen;
6. Bindings auf schreibgeschützte Eigenschaften explizit `Mode=OneWay` setzen und den UI-Smoke-Test erweitern.

`RuntimeVisualPlanBinder` ist der einzige Übergang vom visuellen Plan zu den Legacy-Containern. Vollständige Generierung und `ExistingSimObjectLinkAdapter` dürfen keine zweite Zuordnungslogik aufbauen. Die Auswahlgrenze ist ein vollständiger unterstützter Container: Logik, Signale und Hilfsobjekte bilden im bisherigen Executor eine Abhängigkeitseinheit. Beliebige Signal-/Slot-Neuverdrahtung darf erst eingeführt werden, wenn der Executor dieselbe Änderung deterministisch anwenden und testen kann. Der Sidecar darf die Quell-XML nie überschreiben.

Der Link-only-Adapter darf keine Erzeugungsmethode aufrufen. Er verlangt den aktuellen Objektbestand aus **Model Validation → Update Objects**, genau ein vorhandenes gleichnamiges `FeeLogic` je ausgewähltem `ILogicSimObjectOwner` und validiert alle Arbeitseinträge vor dem ersten Slot-Schreibzugriff.

### Neues Special Device

1. konkrete Geräteklasse unter `SpecialDevices/Devices` ergänzen;
2. in `DeviceCatalog` und `DeviceFactory` registrieren;
3. optional eine konservative TIA-Erkennung in `SpecialDeviceLogicOption.Suggest` hinzufügen;
4. zuerst nur in die Warteschlange übernehmen, FEE-Erzeugung erst nach Benutzerprüfung starten.

### Neue Rolle oder Reiterberechtigung

Rollenlogik liegt allein in `ViCoRolePolicy`. Sichtbarkeiten liegen in `MainWindowVM`/`MainWindow.xaml` bzw. `ViCoWorkspacePageVM`. Die Regel darf nicht als Zeichenvergleich in mehreren XAML-Dateien dupliziert werden.

## Nebenläufigkeit und UI-Stabilität

- Netzwerk-, Datei-, Kanbanize- und TIA-Arbeit niemals im UI-Thread ausführen.
- Fan-out begrenzen: Arbeitsplatz-Pings sind auf acht, RDP-Sitzungsabfragen auf vier parallele Anfragen begrenzt.
- Bei Benutzerfiltern Abbruchtokens/Debounce einsetzen.
- Schreiboperationen, die dieselbe externe Ressource betreffen, serialisieren oder idempotent machen.
- Fehler einer optionalen Detailabfrage dürfen nie den gesamten Tabellen-Refresh abbrechen.
- Beim Binden von WPF-Eigenschaften `OneWay` einsetzen, wenn keine Quelle geschrieben werden darf. Das verhindert die früheren schreibgeschützten `PropertyPathWorker`-Fehler.

`FeeInterface.GetAllInterfacesAsync` darf `GetAllVariablesAsync` nur einmal je Gesamtsnapshot aufrufen und gruppiert anschließend nach `InterfaceGuid`. `LoadSignalsAsync` bleibt als gezielte Einzelobjekt-API bestehen, darf aber nicht wieder in die Schleife des vollständigen Model-Validation-Refreshs eingebaut werden.

## IBN-Remote-Variante erweitern

Die IBN-Variante ist ein separates Produktartefakt. Neue IBN-Funktionen dürfen nur aufgenommen werden, wenn sie für Arbeitsplatzsuche oder RDP notwendig und schreibgeschützt sind. Wiederverwendete Adapter werden im Projekt `VIBN_Tools.IbnRemote.Infrastructure` explizit einzeln verlinkt; eine Referenz auf `VIBN_Tools`, die vollständige Infrastructure oder TIA-/FEE-Projekte ist unzulässig. Das Präprozessorsymbol `IBN_REMOTE_MINIMAL` entfernt aus gemeinsam genutzten Windows-Adaptern nicht benötigte Pfadfunktionen.

Nach einer Änderung immer `scripts/Publish-IbnRemote.ps1` ausführen und prüfen, dass der Zielordner ausschließlich `VIBN_Tools_IBN.exe` enthält. Eine versteckte Hauptnavigation ist kein Ersatz für diese Abhängigkeitsgrenze.

## Tests

| Test | Ziel |
| --- | --- |
| `Tests/CoreSmokeTests` | Modelle, Parser, Rollen, RDP-Profil, Kanbanize-Idempotenz, schmale HTTP-Payloads, TIA-Library und Named-Pipe-Protokoll |
| `Tests/ContainerGenerationSmokeTests` | echter ClosedXML-/ZuLi-Import von `Interface5.xlsx` und `Interface7.xlsx`, erwartete Fonts-Assembly und Übergabe an den fachlichen Container-Generator |
| `Tests/UiStartupSmokeTests` | integrierte WPF-Views, deferred Tabs, DataGrid-/ComboBox-Bindings, visueller XML-Plan, Sidecar, Undo/Redo und Screenshot-Erzeugung |
| `Tests/Test-TiaHardwareTraversal.ps1` | Gerätegruppen, Proxy-Deduplizierung, Local Session und exakte PN/PN-Bit-/Bytebereiche |
| `scripts/Publish-IbnRemote.ps1` plus kurzer Starttest | minimale, selbstständige IBN-Einzeldatei ohne zusätzliche Publish-Dateien |
| manuelle Abnahme | reale UNC-Pfade, echte Kanbanize-Berechtigung, FEE, Outlook, RDP und TIA Openness |

Vor dem Commit mindestens Core-Smoke, WPF-UI-Smoke und einen Release-Build ausführen. Für reale Systeme zusätzlich [ACCEPTANCE_CHECKLIST.md](ACCEPTANCE_CHECKLIST.md) abarbeiten.

## Kommentare und Lesbarkeit

XML-Kommentare erklären öffentliche Modelle, Grenzen und Invarianten. Kommentare innerhalb einer Methode erklären ausschließlich nicht offensichtliche Entscheidungen, beispielsweise Timeout-, Cache- oder Datenintegritätsgründe. Sie dürfen keinen Code in eigenen Worten wiederholen.

Neue Klassen sollen eine eng abgegrenzte Aufgabe haben. Wenn eine ViewModel-Datei mehrere eigenständige Präsentationsmodelle enthält, diese in getrennte Dateien auslagern – beispielsweise `ViCoWorkstationRowVM` gegenüber `ViCoSearchPageVM`.

`ContainerGenerationPageVM` ist derzeit eine dokumentierte Ausnahme. Die frühere Aufteilung hat den ZULI-Import verändert und wurde deshalb zurückgenommen. Die Referenzdateien sichern jetzt den Import und die Übergabe an `ContainerGenerator`; sie enthalten jedoch keine freigegebene Requirements-Datei samt erwarteter vollständiger Ausgabe. Die UI-Klasse daher erst weiter aufteilen, wenn zusätzlich dieser fachliche Golden Master vorliegt.
