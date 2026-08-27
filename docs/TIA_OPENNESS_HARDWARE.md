# TIA-Openness-Hardwareauslesung

## Ursache der bisherigen Falschdaten

Die alte Routine lief rekursiv über `DeviceItems`, stellte aber jedes Element als flachen Datensatz dar. Der sichtbare Gerätename wurde mit dem Namen des ersten DeviceItems kombiniert. Die Deduplizierung verwendete nur Geräteindex, Slot, Modulname und Typkennung. Gleichartige Submodule an demselben Slot konnten dadurch kollidieren. `Subslot`, Hierarchiepfad, `NetworkInterface`, Nodes und GSD-Dienste wurden nicht ausgewertet.

## Implementierter Leseweg

`TiaOpennessSession.ListHardware()` delegiert an den read-only `TiaHardwareReader`. Er ändert kein TIA-Objekt.

1. Root-Geräte aus `Project.Devices`, Benutzerordner aus `Project.DeviceGroups` samt Unterordnern und dezentrale Geräte aus `Project.UngroupedDevicesGroup.Devices` werden in stabiler Reihenfolge gelesen.
2. Jede gefundene `DeviceItem.DeviceItems`-Hierarchie wird vollständig durchlaufen; `Items` dient als versionsrobuster Fallback.
3. Pro DeviceItem wird `AddressComposition` gelesen.
4. `Address.IoType`, `StartAddress` und `Length` bilden getrennte E-/A-Bereiche.
5. `PositionNumber` wird abhängig von der Hierarchiestufe als Slot oder Subslot interpretiert.
6. `NetworkInterface.Nodes` liefert `Address` und `PnDeviceName`; `IoControllers`/`IoConnectors` liefern die Rolle.
7. `GsdDevice`/`GsdDeviceItem` liefern GSD-Name und -Typ, soweit das jeweilige Objekt den Dienst anbietet.
8. Dynamische Attribute ergänzen Typname, Hersteller, Bestellnummer und Firmware.

## Ergebnisdaten

`TiaHardwareModuleInfo` enthält:

- DeviceName, DeviceType
- Manufacturer, OrderNumber, FirmwareVersion
- GsdName, GsdType
- ProfinetName, IpAddress, NetworkRole
- Slot, Subslot
- ModuleName, ModulePath, ModuleType, TypeIdentifier
- InputStartByte/InputLength und OutputStartByte/OutputLength

Nicht vorhandene numerische Werte sind `-1`, nicht vorhandene Texte leer. Unter Special Devices werden adressierbare beziehungsweise eindeutig einer Logik zuordenbare Module als Kandidaten angezeigt. Die Geräte- und Modulnamen bleiben gemeinsam sichtbar; Startadressen sind vor der Erzeugung editierbar.

## Siemens-Versionen

Die Bridge akzeptiert V15 bis V22 und lädt die zur gewählten Installation gehörende `Siemens.Engineering.dll` dynamisch. Die App selbst referenziert keine konkrete PublicAPI-Assembly. Für jede installierte Version gelten weiterhin Siemens-Voraussetzungen: Benutzer in der Openness-Gruppe, gestartetes TIA, unterstützter Projekttyp und ein geöffnetes Projekt.

## Live-Abnahme

Für einen PN/PN-Coupler ist mindestens zu prüfen:

| Erwartung | Beispiel |
| --- | --- |
| Gerät | `PNPN-Koppler_1` |
| Typ | `PN/PN Coupler X1` |
| Modul | `PROFIsafe IN/OUT 12Byte/6Byte` |
| Eingang | `62–73`, Länge 12 |
| Ausgang | `62–67`, Länge 6 |
| Struktur | Kopfgerät → Modul → Submodul mit Slot/Subslot |

Zusätzlich sind ein Siemens-Standardmodul, ein GSDML-Gerät, ein Gerät ohne Prozessabbild und ein großes Projekt zu testen. Die Bridge- und UI-Logs müssen bei nicht unterstützten Attributen weiterlaufen und dürfen das TIA-Projekt nicht speichern oder verändern.

Der automatisierte Strukturtest `Tests/Test-TiaHardwareTraversal.ps1` prüft Root-, Gruppen-, Untergruppen- und Ungrouped-Geräte, den `Items`-Fallback sowie synthetische E-/A-Adressen. Er ersetzt nicht die Live-Abnahme mit Siemens Openness.

## Offizielle API-Grundlage

- [Siemens: Adressen eines DeviceItems](https://docs.tia.siemens.cloud/r/en-us/v21/tia-portal-openness-api-for-automation-of-engineering-workflows/tia-portal-openness-api/functions-on-device-items/accessing-addresses): `DeviceItem.Addresses`/`AddressComposition`, `IoType`, `StartAddress`, `Length`.
- [Siemens: Pflichtattribute von DeviceItems](https://docs.tia.siemens.cloud/r/en-us/v20/tia-portal-openness-api-for-automation-of-engineering-workflows/tia-portal-openness-api/functions-on-device-items/mandatory-attributes-of-device-items): modellierte und dynamische Attribute.
- [Siemens: DeviceItem als NetworkInterface](https://docs.tia.siemens.cloud/r/en-us/v20/tia-portal-openness-api-for-automation-of-engineering-workflows/tia-portal-openness-api/functions-on-device-items/accessing-device-item-as-interface) und [Node-Attribute](https://docs.tia.siemens.cloud/r/en-us/v20/tia-portal-openness-api-for-automation-of-engineering-workflows/tia-portal-openness-api/functions-on-networks/accessing-attributes-of-a-node): Nodes, `IoController`, `IoConnector`, IP und PROFINET-Name.
- [Siemens: GSD-DeviceItems](https://docs.tia.siemens.cloud/r/en-us/v21/tia-portal-openness-api-for-automation-of-engineering-workflows/tia-portal-openness-api/functions-on-device-items/accessing-device-items): GSD-Services und GSD-Attribute.
- [Siemens: Geräte enumerieren](https://docs.tia.siemens.cloud/r/en-us/v20/tia-portal-openness-api-for-automation-of-engineering-workflows/tia-portal-openness-api/functions-on-devices/enumerating-devices): Root-Geräte, Geräteordner, Unterordner und `UngroupedDevicesGroup`.
