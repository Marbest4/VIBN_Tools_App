# FEE2SpecialDevices

`FEE2SpecialDevices` ist bewusst ein eigener Hauptreiter. Die Root-Auswahl und der FEE-Lesezugriff entsprechen dem Sicherheitsprinzip von `FEE2Container`; das Zieldokument ist jedoch ein gerätespezifisches JSON statt eines ContainerFiles. Dadurch werden Container- und Hardwaredomäne nicht vermischt.

## Unterstützter Umfang

Nach einer vollständig erfolgreichen Erzeugung durch `SpecialDevices2FEE` schreibt die Anwendung eine versionierte Provenienz in die `TagComponent.TagEntries` des erzeugten `BasicFrame`. Sie enthält:

- Präfix, Hersteller und Gerätetyp;
- optionalen Robotertyp;
- Eingangs- und Ausgangs-Startbyte;
- Signal-GUID, Tag, Adresse, Richtung, Datentyp und Kommentar.

Ein fehlgeschlagener oder nur teilweise ausgeführter Schreibvorgang erhält keine Provenienz. Beim Rücklesen werden die Signale über ihre persistierte Variablen-GUID mit den aktuellen FEE-Werten überlagert. Fehlende Variablen werden gezählt und als Hinweis angezeigt; der gespeicherte Generierungsstand bleibt für die Diagnose erhalten.

## Ablauf

1. Mit FEE verbinden.
2. **FEE-Geräte einlesen** wählen.
3. Einen eindeutig erkannten Root auswählen.
4. Hersteller, Gerätetyp, Adressen sowie die Anzahl aktueller/fehlender Signale prüfen.
5. **Gerät als JSON exportieren** wählen.

Der Export erfolgt atomar als `*.specialdevice.json`. Das JSON ist eine versionierte, maschinenlesbare Momentaufnahme für Vergleich, Archivierung und einen späteren Importworkflow.

## Grenzen

- Nur künftig mit dieser Version vollständig erzeugte Geräte sind erkennbar.
- Ältere oder manuell erstellte BasicFrames werden nicht anhand von Namen oder Logikdefinitionen geraten.
- Der aktuelle Schritt exportiert eine überprüfbare Struktur; ein automatischer Reimport in die SpecialDevices2FEE-Warteschlange und ein semantischer Hardwarevergleich sind noch nicht freigegeben.
- Die Codec- und Exportlogik ist automatisiert getestet. Lesen nach echtem FEE-Save/Reload bleibt eine Live-Abnahme mit der installierten FEE-Laufzeit.
