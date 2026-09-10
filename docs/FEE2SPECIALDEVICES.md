# FEE2SpecialDevices

`FEE2SpecialDevices` ist bewusst ein eigener Hauptreiter. Die Root-Auswahl und der FEE-Lesezugriff entsprechen dem Sicherheitsprinzip von `FEE2Container`; das Zieldokument ist jedoch ein gerätespezifisches JSON statt eines ContainerFiles. Dadurch werden Container- und Hardwaredomäne nicht vermischt.

## Unterstützter Umfang

Nach einer vollständig erfolgreichen Erzeugung durch `SpecialDevices2FEE` schreibt die Anwendung eine versionierte Provenienz in die `TagComponent.TagEntries` des erzeugten `BasicFrame`. Sie enthält:

- Präfix, Hersteller und Gerätetyp;
- optionalen Robotertyp;
- Eingangs- und Ausgangs-Startbyte;
- Signal-GUID, Tag, Adresse, Richtung, Datentyp und Kommentar.

Ein fehlgeschlagener oder nur teilweise ausgeführter Schreibvorgang erhält keine gültige Provenienz. Nach der vollständigen Geräteerzeugung wird der Tag-Datensatz geschrieben, der vorhandene `BasicFrame` erneut an FEE gesendet und anschließend zurückgelesen sowie per Prüfsumme validiert. Erst dann meldet `SpecialDevices2FEE` die Erstellung als erfolgreich und entfernt das Gerät aus der Warteschlange. Beim Rücklesen werden die Signale über ihre persistierte Variablen-GUID mit den aktuellen FEE-Werten überlagert. Fehlende Variablen werden gezählt und als Hinweis angezeigt; der gespeicherte Generierungsstand bleibt für die Diagnose erhalten.

## Ablauf

1. Mit FEE verbinden.
2. **FEE-Geräte einlesen** wählen.
3. Einen eindeutig erkannten Root auswählen.
4. Hersteller, Gerätetyp, Adressen sowie die Anzahl aktueller/fehlender Signale prüfen.
5. **Gerät als JSON exportieren** wählen.

Der Export erfolgt atomar als `*.specialdevice.json`. Das JSON ist eine versionierte, maschinenlesbare Momentaufnahme für Vergleich und Archivierung. Unter **SpecialDevices2FEE → FEE2-JSON laden** kann die Datei geprüft und über den bestehenden `DeviceFactory`-Katalog in die Warteschlange übernommen werden. Unbekannte Hersteller, Gerätetypen, Versionen oder fehlende Pflichtangaben werden abgewiesen. Ein bereits vorhandenes Präfix desselben Herstellers wird nicht doppelt eingereiht.

Der Gerätekatalog bleibt bei einer erneuten Erzeugung die autoritative Quelle für Signale. Weichen die aus FEE exportierten Tags, Adressen, Datentypen oder Richtungen von dieser Definition ab, wird das Gerät zwar zur bewussten Prüfung eingereiht, die Oberfläche warnt aber ausdrücklich: Die manuell veränderten Signalwerte werden nicht stillschweigend als neue Generierungsregel verwendet. Der JSON-Snapshot bleibt der Soll-Ist-Nachweis.

## Grenzen

- Nur künftig mit dieser Version vollständig erzeugte Geräte sind erkennbar.
- Ältere oder manuell erstellte BasicFrames werden nicht anhand von Namen oder Logikdefinitionen geraten.
- Der kontrollierte Reimport stellt Hersteller, Gerätetyp, Präfix, Robotertyp und Startadressen wieder her. Manuell abweichende Signale werden diagnostiziert, nicht ungeprüft in den Gerätekatalog geschrieben. Ein eigenständiger semantischer Hardwarevergleich ist noch nicht freigegeben.
- Die Codec- und Exportlogik ist automatisiert getestet. Lesen nach echtem FEE-Save/Reload bleibt eine Live-Abnahme mit der installierten FEE-Laufzeit.
