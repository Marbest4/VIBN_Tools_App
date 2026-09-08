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

Beim Einlesen werden Signalname, Adresse beziehungsweise symbolischer Pfad, Datentyp und die als FEE-Kommentar geführte Signal-ID anhand der gespeicherten Variablen-GUID aus dem aktuellen FEE-Stand überlagert. Zusätzlich wird die aktuelle Slotroute innerhalb der Kinder des ausgewählten Roots gelesen. Direkte Variablenzuordnungen werden über `GetAssignedSceneObjectsAsync` aufgelöst; bei einer `PLC_IN_`-Mehrfachbelegung wird die Kette Variable → `MoveBit/Output 01` → `Input 01` → PLC-Slot über `GetSlotSlotAssignmentAsync` verfolgt. So kann eine eindeutige nachträgliche Slotänderung in das exportierte ContainerFile übernommen werden.

Fehlt eine referenzierte Variable oder ist keine beziehungsweise mehr als eine PLC-Slotroute innerhalb des Roots erkennbar, bleibt der ursprüngliche Generierungswert erhalten. Der Root zeigt getrennt fehlende Signale und ungeklärte Slotrouten; die Diagnose nennt die Variable und bei Mehrdeutigkeit alle Kandidaten. Externe Zuordnungen außerhalb des gewählten Roots werden nicht als Container-Slot übernommen.

Der automatische Test prüft auch, dass zwei `PLC_IN_`-Einträge gemeinsam auf einen geänderten Zielslot projiziert werden und danach wieder als gültiger Mehrfacheingang eingelesen werden. Offen bleibt die Live-Abnahme mit einer laufenden FEE-Instanz: Insbesondere muss bestätigt werden, dass die konkrete installierte FEE-Version die `TagEntries` sowie direkte und Move-basierte Slotverbindungen über Speichern, Schließen und erneutes Öffnen unverändert liefert. Bis dahin ist der SDK-Vertrag kompiliert und die Projektion automatisiert getestet, aber nicht als praktisch ausgeführter FEE-Round-Trip freigegeben.
