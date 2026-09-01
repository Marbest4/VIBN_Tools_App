# TIA-Openness-Hardwareauslesung

## Ursache der bisherigen Falschdaten

Die alte Routine lief rekursiv über `DeviceItems`, stellte aber jedes Hierarchieelement als flachen Datensatz dar und übernahm `Address.Length` fälschlich direkt als Bytezahl. Siemens liefert diese Länge in Bits. Dadurch wurden aus 96 Bit scheinbar 96 Byte und aus `E 62` fälschlich `E 62–157`. Adresslose Kopf-, Rack- und Interfaceelemente erzeugten zusätzliche Leerzeilen. Außerdem basierte die Deduplizierung auf Proxyreferenzen beziehungsweise zu groben Identitäten, obwohl Openness dasselbe Engineering-Objekt über mehrere Proxyinstanzen liefern kann.

## Implementierter Leseweg

`TiaOpennessSession.ListHardware()` delegiert an den read-only `TiaHardwareReader`. Er ändert kein TIA-Objekt.

1. Root-Geräte aus `Project.Devices`, Benutzerordner aus `Project.DeviceGroups` samt Unterordnern und dezentrale Geräte aus `Project.UngroupedDevicesGroup.Devices` werden in stabiler Reihenfolge gelesen.
2. Jede gefundene `DeviceItem.DeviceItems`-Hierarchie wird vollständig durchlaufen; `Items` dient als versionsrobuster Fallback.
3. Pro DeviceItem wird `AddressComposition` gelesen.
4. `Address.IoType`, `StartAddress` und die rohe Bitlänge bilden getrennte E-/A-Bereiche. Die Anzeige verwendet `ceil(bits / 8)` Bytes; Bereiche verschiedener Module werden nie zusammengeführt.
5. `PositionNumber` wird abhängig von der Hierarchiestufe als Slot oder Subslot interpretiert.
6. `NetworkInterface.Nodes` liefert `Address` und `PnDeviceName`; `IoControllers`/`IoConnectors` liefern die Rolle. Je nach TIA-Generation wird der Feature-Typ aus der passenden bereits geladenen `Siemens.Engineering.*`-Assembly aufgelöst. Direkte dynamische Attribute am Geräte-/Interfaceknoten dienen als Fallback.
7. `GsdDevice`/`GsdDeviceItem` liefern GSD-Name und -Typ, soweit das jeweilige Objekt den Dienst anbietet.
8. Dynamische Attribute ergänzen Typname, Hersteller, Bestellnummer und Firmware (`FirmwareVersion`, `Firmware` oder `Version`). Geräte-/Netzwerkmetadaten werden bis zum adressführenden Blattmodul vererbt.
9. Adresslose Hierarchieknoten liefern Metadaten an ihre Kinder, erzeugen aber keine eigene Tabellenzeile.
10. Eine semantische Identität aus Gerät, Modultyp, Slot/Subslot und exaktem E-/A-Satz verhindert doppelte Proxyzeilen, ohne zwei reale PROFIsafe-Module zusammenzufassen.

## Ergebnisdaten

`TiaHardwareModuleInfo` enthält:

- DeviceName, DeviceType
- Manufacturer, OrderNumber, FirmwareVersion
- GsdName, GsdType
- ProfinetName, IpAddress, NetworkRole
- Slot, Subslot
- ModuleName, ModulePath, ModuleType, TypeIdentifier
- InputStartByte/InputLengthBits/InputLength und OutputStartByte/OutputLengthBits/OutputLength

Nicht vorhandene numerische Werte sind `-1`, nicht vorhandene Texte leer. Unter Special Devices werden adressierbare beziehungsweise eindeutig einer Logik zuordenbare Module als Kandidaten angezeigt. Zeilen mit demselben Gerätenamen werden in einer aufklappbaren Gerätegruppe zusammengefasst. Sichtbar bleiben nur GSDML, IP-Adresse, Modultyp, Firmware, E-/A-Bereich und -Länge, Präfix, Logik und Status. E-/A-Startadressen sind weiterhin editierbar.

## Gespeicherte Logikzuordnung

`JsonTiaHardwareMappingStore` speichert die geprüfte Auswahl unter
`%LOCALAPPDATA%\GROB\VIBN_Tools\tia-hardware-mappings.json`. Der Schlüssel besteht aus Gerätename, PROFINET-Name, Modulpfad, Slot und Subslot. Adressen gehören bewusst nicht zum Schlüssel, damit manuelle Korrekturen nach einem erneuten Auslesen wiederhergestellt werden können.

Mehrere getrennte Adresssätze desselben DeviceItems erhalten zusätzlich einen stabilen nullbasierten Adresssatzindex. Alte gespeicherte Schlüssel ohne Index werden ausschließlich auf den ersten Satz migriert; dadurch wird eine frühere zusammengefasste Zuordnung nicht versehentlich dupliziert.

Gespeichert werden Übernahmeauswahl, Präfix, E-/A-Start, Logik und gegebenenfalls der Robotertyp. Der Schreibvorgang ersetzt die JSON-Datei atomar. Nach dem nächsten Hardwareauslesen werden passende Zuordnungen automatisch geladen; nicht mehr vorhandene Hardware wird nicht auf neue Zeilen übertragen.

Eine Tabellenzeile entspricht einem TIA-Modul und dessen E-/A-Bereich. Die ausgewählte Special-Device-Logik gilt für die zusammengehörigen Ein- und Ausgangsdaten dieses Moduls. Die bestehende FEE-Factory erzeugt pro Special Device genau eine Logik; zwei verschiedene Logiken für E und A desselben Zielgeräts sind daher kein gültiges Erzeugungsmodell.

## Verbindung trennen und laufenden Attach abbrechen

`TIA trennen / abbrechen` ist auch während eines Verbindungsaufbaus aktiv. Ein normal reagierender Bridge-Prozess erhält das vorhandene `system.close`-Kommando und gibt beim Beenden seine `TiaOpennessSession` frei. Blockiert ein synchroner Openness-Aufruf, wird die ausschließlich für diese Seite gestartete Named-Pipe-Verbindung abgebrochen und nur deren eigener Bridge-Prozess beendet. Beim nächsten Verbinden wird eine neue Bridge samt neuer Openness-Session gestartet. PLC-Auswahl, Hardwareliste und UI-Status werden zurückgesetzt. Fehler laufen über das vorhandene Anwendungslogging; es wird keine MessageBox geöffnet.

Diese harte Abbruchgrenze ist notwendig, weil der net48-Bridge-Server jeweils einen synchronen Siemens-Aufruf bearbeitet und währenddessen kein zweites Abbruchkommando annehmen kann. Der TIA-Portal-Prozess selbst wird dabei nicht beendet.

## Großprojekt: `Projects.Count == 0`

Die Projektgröße allein erklärt eine leere `TiaPortal.Projects`-Collection nicht. Der bisherige Reflection-Fallback hat jedoch Ausnahmen beim Lesen von `Projects` und `LocalSessions` in eine leere Liste umgewandelt. Dadurch waren „noch nicht bereit“, „nicht unterstützt“ und „Zugriff fehlgeschlagen“ im UI nicht unterscheidbar.

Der Attach-Fehler enthält jetzt:

- ausgewählte TIA-Version, Prozess-ID, UI-Modus und gemeldeten `ProjectPath`;
- Anzahl beziehungsweise Lesefehler von `Projects`;
- Anzahl beziehungsweise Lesefehler von `LocalSessions`;
- Fehler beim Zugriff auf `LocalSession.Project`.

Der Leser wartet weiterhin bis zu 90 Sekunden, damit ein Projekt nach Firewall-Freigabe oder während des Ladens sichtbar werden kann. Lässt sich das große Projekt danach nicht auflösen, sind anhand der neuen Diagnose in dieser Reihenfolge zu prüfen:

1. Nur die gewünschte Instanz derselben TIA-Hauptversion geöffnet lassen. `TiaPortalProcess.ProjectPath` ist laut Siemens leer, wenn die Instanz kein geöffnetes Projekt meldet.
2. Bei Multiuser-/Project-Server-Projekten muss eine lokale oder exklusive Session tatsächlich geöffnet sein; diese wird über `TiaPortal.LocalSessions` aufgelöst.
3. Openness-Firewall dauerhaft freigeben, Benutzergruppe `Siemens TIA Openness` prüfen und Windows nach einer Gruppenänderung neu anmelden.
4. TIA und VIBN Tools mit demselben Windows-Benutzer und derselben Erhöhungsebene ausführen.
5. Im Bridge-Prozess `VIBN_Tools.TiaBridge.exe` debuggen; Breakpoints im Reader werden nicht vom Hauptprozess getroffen.
6. Kleines und großes Projekt mit identischer TIA-Version und identischem Projekttyp vergleichen. Erst wenn die Diagnose einen lesbaren Projekt- oder Local-Session-Eintrag zeigt, ist die nachfolgende Gerätetraversierung relevant.
7. Prüfen, ob das geöffnete Fenster nur ein **Referenzprojekt** darstellt. Referenzprojekte erscheinen nicht als geöffnetes Primärprojekt in `TiaPortal.Projects`; das eigentliche Projekt muss geöffnet sein.
8. Für das Projekt erforderliche Optionspakete und HSPs vollständig in genau dieser TIA-Hauptversion installieren. Openness bietet keine Kompatibilitätsansicht, die fehlende Engineering-Pakete ersetzt.
9. Bei UMAC-/geschützten Projekten den aktuellen Projektbenutzer mit den für den benötigten Lese-/Änderungsumfang erforderlichen Rechten anmelden. Windows-Administratorrechte ersetzen keine Projektberechtigung.

Ein automatisches Öffnen, Konvertieren oder Speichern des Projekts wurde bewusst nicht ergänzt, weil dies das Projekt verändern könnte. Für weiterhin nicht exponierte Project-Server-Sessions ist die sichere Alternative, in TIA eine lokale/exklusive Session zu öffnen und danach erneut zu verbinden.

## Siemens-Versionen

Die Bridge akzeptiert V15 bis V22 und lädt die zur gewählten Installation gehörende `Siemens.Engineering.dll` dynamisch. Die App selbst referenziert keine konkrete PublicAPI-Assembly. Für jede installierte Version gelten weiterhin Siemens-Voraussetzungen: Benutzer in der Openness-Gruppe, gestartetes TIA, unterstützter Projekttyp und ein geöffnetes Projekt.

## Live-Abnahme

Für einen PN/PN-Coupler ist mindestens zu prüfen:

| Erwartung | Beispiel |
| --- | --- |
| Gerät | `PNPN-Koppler_1` |
| Typ | `PN/PN Coupler X2` |
| Modul 1 | `PROFIsafe IN/OUT 12 Byte / 6 Byte`: Eingang `62–73` (96 Bit = 12 Byte), Ausgang `62–67` (48 Bit = 6 Byte) |
| Modul 2 | `PROFIsafe IN/OUT 6 Byte / 12 Byte`: Eingang `74–79` (48 Bit = 6 Byte), Ausgang `68–79` (96 Bit = 12 Byte) |
| Struktur | Kopfgerät → Modul → Submodul mit Slot/Subslot |

Zusätzlich sind ein Siemens-Standardmodul, ein GSDML-Gerät, ein Gerät ohne Prozessabbild und ein großes Projekt zu testen. Die Bridge- und UI-Logs müssen bei nicht unterstützten Attributen weiterlaufen und dürfen das TIA-Projekt nicht speichern oder verändern.

Der automatisierte Strukturtest `Tests/Test-TiaHardwareTraversal.ps1` prüft Root-, Gruppen-, Untergruppen- und Ungrouped-Geräte, den `Items`-Fallback, doppelte Proxyobjekte, eine Multiuser-Local-Session, Vererbung von IP/PROFINET-Name/Firmware und exakt die beiden oben genannten PN/PN-Adresszeilen. Er ersetzt nicht die Live-Abnahme mit Siemens Openness.

## Offizielle API-Grundlage

- [Siemens: Adressen eines DeviceItems](https://docs.tia.siemens.cloud/r/en-us/v21/tia-portal-openness-api-for-automation-of-engineering-workflows/tia-portal-openness-api/functions-on-device-items/accessing-addresses): `DeviceItem.Addresses`/`AddressComposition`, `IoType`, `StartAddress`, `Length`.
- [Siemens: Pflichtattribute von DeviceItems](https://docs.tia.siemens.cloud/r/en-us/v20/tia-portal-openness-api-for-automation-of-engineering-workflows/tia-portal-openness-api/functions-on-device-items/mandatory-attributes-of-device-items): modellierte und dynamische Attribute.
- [Siemens: DeviceItem als NetworkInterface](https://docs.tia.siemens.cloud/r/en-us/v20/tia-portal-openness-api-for-automation-of-engineering-workflows/tia-portal-openness-api/functions-on-device-items/accessing-device-item-as-interface) und [Node-Attribute](https://docs.tia.siemens.cloud/r/en-us/v20/tia-portal-openness-api-for-automation-of-engineering-workflows/tia-portal-openness-api/functions-on-networks/accessing-attributes-of-a-node): Nodes, `IoController`, `IoConnector`, IP und PROFINET-Name.
- [Siemens: GSD-DeviceItems](https://docs.tia.siemens.cloud/r/en-us/v21/tia-portal-openness-api-for-automation-of-engineering-workflows/tia-portal-openness-api/functions-on-device-items/accessing-device-items): GSD-Services und GSD-Attribute.
- [Siemens: Geräte enumerieren](https://docs.tia.siemens.cloud/r/en-us/v20/tia-portal-openness-api-for-automation-of-engineering-workflows/tia-portal-openness-api/functions-on-devices/enumerating-devices): Root-Geräte, Geräteordner, Unterordner und `UngroupedDevicesGroup`.
- [Siemens: Diagnoseinformationen eines TIA-Prozesses](https://docs.tia.siemens.cloud/r/en-us/v20/tia-portal-openness-api-for-automation-of-engineering-workflows/tia-portal-openness-api/general-functions/diagnostic-interfaces-on-tia-portal): Prozess-ID, `ProjectPath`, Modus und angefügte Openness-Sessions.
- [Siemens: lokale/exklusive Multiuser-Session öffnen](https://docs.tia.siemens.cloud/r/de-de/v20/tia-portal-openness-api-fur-die-automatisierung-von-engineering-workflows/tia-portal-openness-api/funktionsunterstutzung-fur-mehrbenutzerbetrieb/lokale/exklusive-sitzung-offnen): Project-Server-Projekte werden über eine lokale Session geöffnet.
