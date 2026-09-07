# FEE2Container

## Garantierter Scope

Die erste verifizierbare Version exportiert ausschließlich FEE-`BasicFrame`-Roots, die künftig durch **Container2FEE Visual** erzeugt wurden. Historische oder manuell aufgebaute Modelle werden bewusst nicht anhand veränderlicher Objekt- oder Signalnamen geraten.

Beim Generieren legt Container2FEE auf dem neuen `BasicFrame` eine versionierte Provenienz im persistenten `FS.SDK.Components.TagComponent.TagEntries` ab. Die FEE-SDK-Dateien unter `SDK/` werden dabei weder verändert noch ersetzt. Namespaced Tags enthalten:

- Schema- und Formatversion,
- SHA-256-Prüfsumme,
- Fingerprint der Container-Quelldatei,
- gzip-komprimierte und in begrenzte Teile zerlegte XML der tatsächlich berücksichtigten Container,
- stabile Zuordnungen jedes bekannten Container-Eintrags zur erzeugten oder wiederverwendeten FEE-Variablen-GUID.

Marks und sichtbare FEE-Objektnamen werden nicht als Metadatenspeicher missbraucht.

## Bedienung

1. In **Project Settings** eine FEE-Verbindung herstellen.
2. Den Hauptreiter **FEE2Container** öffnen.
3. **FEE-Roots einlesen** wählen.
4. Einen Root anhand Name, GUID, Container- und Signalanzahl auswählen.
5. **ContainerFile exportieren** wählen und den Zielpfad bestätigen.

BasicFrames ohne Provenienz werden gezählt und ignoriert. Beschädigte oder unbekannte Provenienz wird mit Root und Ursache angezeigt. Der Export erfolgt über eine temporäre Datei und wird anschließend atomar ersetzt.

## Round-Trip und Grenzen

Der automatische Test prüft `Container → Provenienz → Container` einschließlich Auswahlfilter, Prüfsumme, mehreren Signalen und einer mehrfachen `PLC_IN_`-Belegung. Die XML muss nicht bytegleich sein; relevant ist, dass Container, Einträge und Slotsemantik erhalten bleiben.

Beim Einlesen werden Signalname, Adresse beziehungsweise symbolischer Pfad, Datentyp und die als FEE-Kommentar geführte Signal-ID anhand der gespeicherten Variablen-GUID aus dem aktuellen FEE-Stand überlagert. Fehlt eine referenzierte Variable, bleibt der ursprüngliche Generierungswert erhalten und der Root zeigt eine konkrete Diagnose.

Nachträgliche Slot-Umbauten werden noch nicht zurückprojiziert. Besonders bei mehrfachen `PLC_IN_`-Belegungen führt der Pfad über Move-Objekte und Slot-zu-Slot-Verbindungen; dafür muss der Live-Extractor diese Kette zuverlässig auflösen, bevor eine geänderte kanonische Container-Slotbezeichnung geschrieben werden darf.

Noch offen ist außerdem die Live-Abnahme mit einer laufenden FEE-Instanz: Insbesondere muss bestätigt werden, dass die konkrete installierte FEE-Version die `TagEntries` über Speichern, Schließen und erneutes Öffnen unverändert persistiert und dass `GetAllVariablesAsync` die erwarteten aktuellen Daten liefert. Solange Slot-Rückmapping und Live-Test fehlen, ist die SDK-Anbindung statisch verifiziert und der Codec automatisiert getestet, aber nicht als vollständiger aktueller FEE-Round-Trip freigegeben.
