# Container-Workflow: Korrekturen November 2026

## Container2FEEVisual

- **GrobStandard-Logiken** zeigt die mitgelieferten XML-Definitionen und markiert die im Plan verwendeten Definitionen. Importiert wird weiterhin erst bei der Generierung und nur, wenn die Definition in FEE fehlt.
- Das Vorgangsprotokoll kann geleert und auf offene/fehlgeschlagene oder nur fehlgeschlagene Einträge gefiltert werden. Warnungen und Fehler enthalten ihre Diagnosecodes; das separate Anwendungslog bleibt erhalten.
- Ein kompatibles MotionJoint kann einem Container im Plan zugeordnet werden, bevor dessen Logik existiert. Der bisherige Fehler `CONTAINER_LOGIC_MISSING` war eine zu frühe Prüfung beim Planen. **Nur verknüpfen** benötigt weiterhin eine vorhandene, passende Logik; **Generieren** kann eine fehlende Containerlogik erzeugen.
- Die Logiksuche berücksichtigt eindeutige Live-Verbindungen unabhängig von unterschiedlichen CAD-/Logiknamen. Mehrdeutige Verbindungen bleiben prüfpflichtig. Objekt-GUIDs und Definitions-GUIDs werden getrennt behandelt.
- Ein Zylinder benötigt Positionssteuerung und die Verbindungen `InTarget`, `InVelocity` und eine gemeinsame Rückmeldung über `OutValue`. `InValue` ist bei Positionssteuerung kein Pflichtanschluss. Bei mehreren Joints bleibt die tatsächlich vorhandene Rückmeldungsquelle erhalten, unabhängig von deren Sortierreihenfolge.
- Bereits über ein Signal verbundene BoolNot-/MoveBit-Hilfsobjekte werden bei erneuter Generierung wiederverwendet. Für PLC_IN-Mehrfachbelegungen wird zuerst die bereits passende MoveBit-Route gesucht. Ein mit einem anderen Treiber belegter Eingang wird nicht umgehängt; Mehrdeutigkeiten werden gemeldet.
- A-/Q-Adressen auf `PLC_IN_` sowie E-/I-Adressen auf `PLC_OUT_` werden erkannt, einschließlich `%`, Peripherie- und Wortadressierung. ContainerGeneration zeigt eine Warnung. Container2FEEVisual blockiert aktive Richtungsfehler auch bei erzwungener Generierung. Abgewählte Container behalten einen Hinweis, blockieren jedoch den gültigen Teilplan nicht.
- Die beiden Gripper-Addon-Typen wurden in den visuellen und den Legacy-Typkatalog aufgenommen. Die übergeordnete GripperBasic-Logik wird vor dem Addon erzeugt; eine bereits vorhandene Parent-Logik kann wiederverwendet werden.

## FEE2Container und Rechnerübersicht

- Die aktuelle Definitions-GUID wird sowohl aus XML-Attributen als auch aus XML-Elementen gelesen. Dadurch können bisher als unbekannt angezeigte Logiknamen wieder aufgelöst werden.
- Bekannte aktuelle Logik-/Objekttypen werden nicht mehr durch ein bloßes Req.xml-Slotmuster ersetzt.
- Die gerichtete Hilfslogik-Zuordnung verwendet die Eingangsseite; `InValue`/`OutValue` werden ebenfalls erkannt. Objektverknüpfungen und Signale aus mehreren Interfaces bleiben dem gemeinsamen Owner zugeordnet. Die vorhandene Gruppierung wurde mit einer Helper-Kette und umgekehrter API-Kantenreihenfolge getestet.
- Unterschiedlich benannte physische Objekte können anhand eindeutiger aktueller Objektverbindungen zugeordnet werden. Historische Provenienz allein reicht dafür nicht.
- CabinetElemente werden unter dem zugehörigen Cabinet geführt. Doppelte Darstellungen desselben physischen Elements werden zusammengeführt; Signale beider Interfaces bleiben erhalten. Die Cabinet-Zuordnung wird auch dann aufgebaut, wenn vorher noch keine Objektzuordnungsliste existierte.
- Die E-/A-Byteanzeige berücksichtigt auch Einträge ohne gespeicherte Variablen-GUID. Sie verwendet weiterhin die kleinste verknüpfte Byteadresse gemäß der gemeinsamen SpecialDevices-Auswertung.
- Der vorhandene **N/A-Logiken exportieren**-Button schreibt JSON mit Logikdefinition, allen bekannten Slots, Signalname, Adresse, Datentyp, Interface und Objektverbindungen. Der Export wurde mit direkten und indirekten Signalen aus zwei Interfaces inklusive tatsächlich geschriebener JSON-Datei geprüft.
- **Daten aktualisieren** umgeht Ping- und RDP-Sitzungscaches und leert den Dokumentcache. Der Datenstand wird sichtbar angezeigt. Nach einem erfolgreichen Online-Abruf wird dessen Zeit übernommen; ein fehlgeschlagener Online-Abruf wird nicht als neue Aktualisierung alter Quelldaten ausgegeben.

## Gelieferte XML-Beispiele

Die Quelldateien wurden unverändert gelassen. Die portable Regression liest alle fünf Dateien (776 Container) und prüft ihre Signalrichtungen:

| Datei | Container | A/Q auf PLC_IN |
| --- | ---: | ---: |
| Container_Zuli_GFTX-BT4-MOD1-71000.xml | 140 | 0 |
| Container_PLC-Interface_Zuli_GFTX-BT4-MOD1-73000.xml | 222 | 0 |
| Container_PLC-Interface_Zuli_GFTX-BT4-MOD1-74000_1040.xml | 110 | 3 |
| Container_PLC-Interface_Zuli_GFTX-RM1-FAS4-21000.xml | 238 | 3 |
| Container_PLC-Interface_Zuli_GFTX-RM1-FAS4-40000.xml | 66 | 1 |

Beispielsweise ist `A4500.0` auf `PLC_IN_NO1` ein tatsächlicher Fehler in der Beispieldatei. Er wird nicht stillschweigend auf einen geratenen anderen Slot verschoben. Die fünf als `unknown` deklarierten Container bleiben ausdrücklich unbekannte Signalcontainer.

## Setup-Erstellung

Die Inno-Suche prüft den expliziten Pfad, vorhandene ProgramFiles-Verzeichnisse und PATH. Fehlende Compiler werden vor dem Publish gemeldet. Der Runtime-Restore verwendet dieselbe Release-/SelfContained-Konfiguration wie Publish. Vor der Paketerstellung werden Hauptprogramm, Laufzeitmetadaten, TIA-Bridge und Logikdateien geprüft. Ein altes Setup wird vor dem Compilerlauf entfernt; ein erfolgreicher Compiler-Exit ohne neue Datei wird als Fehler behandelt. Die Versionsnummer muss aus dem Buildskript kommen. Das Setup prüft .NET Framework 4.8 für die TIA-Bridge; die .NET-8-Hauptanwendung wird selbstenthaltend veröffentlicht.

## Verifikation und Grenzen

In der Linux-Cloud erfolgreich ausgeführt:

- Core-Smoke einschließlich neuer Prüfung des manuellen Rechner-Refreshs;
- ContainerFileFormat einschließlich Live-Graph, Helper-Wiederverwendungsauswahl, Cabinet-Gruppierung, N/A-JSON-Export und XML-Beispielen;
- ReverseData und MiniTools;
- Release-Build von ContainerGeneration mit Windows-Targeting, ohne Warnungen oder Fehler;
- PowerShell-Tests für Compilererkennung, Ablehnung unvollständiger Publish-Verzeichnisse und Syntax sämtlicher Skripte.

Der vollständige Solution-Build scheitert bei der Paketwiederherstellung an **Grob.UX 1.8.93 (NU1101)**. Die WPF-Oberfläche, reale FEE-Schreiboperationen und die erzeugte Windows-Setup-EXE konnten hier nicht ausgeführt werden. Die Änderungen benötigen daher noch einen Windows-/FEE-Abnahmelauf mit dem privaten Grob.UX-Paket. Ein erfolgreiches Offline-Testergebnis ersetzt diese Live-Abnahme nicht.

Auf einem vollständig eingerichteten Windows-Rechner zusätzlich ausführen:

```powershell
.\Build.ps1
# Der neue gezielte Test verwendet den tatsächlichen visuellen Parser:
dotnet run --project Tests/ContainerGenerationSmokeTests/VIBN_Tools.ContainerGeneration.SmokeTests.csproj --configuration Release -- --visual-fixtures-only
dotnet run --project Tests/UiStartupSmokeTests/VIBN_Tools.UiStartup.SmokeTests.csproj --configuration Release
.\Tests\Test-InstallerPrerequisites.ps1
.\scripts\Build-Installer.ps1
```

Danach in einem Test-FEE-Projekt: vorhandenes unterschiedlich benanntes Joint zuordnen, erneut verknüpfen/generieren, Helper-GUIDs auf Wiederverwendung prüfen, beide Interfaces rücklesen und den N/A-JSON-Export kontrollieren.
