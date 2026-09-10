# FEE2Container

## Zwei Auslesemodi

Der Reiter zeigt alle FEE-`BasicFrame`-Objekte als auswählbare Hauptknoten. Für einen durch **Container2FEE Visual** erzeugten Root wird weiterhin die gespeicherte Provenienz als exakter Round-Trip verwendet. Fehlt sie, rekonstruiert das Tool ein ContainerFile aus den unterstützten Objekten unterhalb des gewählten Hauptknotens sowie deren aktuellen Variablen- und Slotzuordnungen.

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
4. Einen Root anhand Name, GUID und Quelle auswählen. Bei `FEE-Struktur (Rekonstruktion)` werden Container- und Signalanzahl erst beim Export ermittelt.
5. **ContainerFile exportieren** wählen und den Zielpfad bestätigen.

BasicFrames ohne Provenienz werden nicht mehr ignoriert. Der Export untersucht ausschließlich den ausgewählten Root und seine Nachfahren. Erkannte Container, nicht zuordenbare Objekte und fachliche Mehrdeutigkeiten erscheinen als konkrete Prüfhinweise. Werden keine unterstützten Container gefunden, wird keine leere Datei geschrieben. Der Export erfolgt über eine temporäre Datei und wird anschließend atomar ersetzt; anschließend kann die Datei im Containervergleich als bestehender Stand geladen werden.

## Round-Trip und Grenzen

Der automatische Test prüft `Container → Provenienz → Container` einschließlich Auswahlfilter, Prüfsumme, mehreren Signalen und einer mehrfachen `PLC_IN_`-Belegung. Die XML muss nicht bytegleich sein; relevant ist, dass Container, Einträge und Slotsemantik erhalten bleiben.

Beim Einlesen werden Signalname, Adresse beziehungsweise symbolischer Pfad, Datentyp und die als FEE-Kommentar geführte Signal-ID anhand der gespeicherten Variablen-GUID aus dem aktuellen FEE-Stand überlagert. Zusätzlich wird die aktuelle Slotroute innerhalb der Kinder des ausgewählten Roots gelesen. Direkte Variablenzuordnungen werden über `GetAssignedSceneObjectsAsync` aufgelöst; bei einer `PLC_IN_`-Mehrfachbelegung wird die Kette Variable → `MoveBit/Output 01` → `Input 01` → PLC-Slot über `GetSlotSlotAssignmentAsync` verfolgt. So kann eine eindeutige nachträgliche Slotänderung in das exportierte ContainerFile übernommen werden.

Fehlt eine referenzierte Variable oder ist keine beziehungsweise mehr als eine PLC-Slotroute innerhalb des Roots erkennbar, bleibt der ursprüngliche Generierungswert erhalten. Der Root zeigt getrennt fehlende Signale und ungeklärte Slotrouten; die Diagnose nennt die Variable und bei Mehrdeutigkeit alle Kandidaten. Externe Zuordnungen außerhalb des gewählten Roots werden nicht als Container-Slot übernommen.

Der automatische Test prüft auch, dass zwei `PLC_IN_`-Einträge gemeinsam auf einen geänderten Zielslot projiziert werden und danach wieder als gültiger Mehrfacheingang eingelesen werden. Offen bleibt die Live-Abnahme mit einer laufenden FEE-Instanz: Insbesondere muss bestätigt werden, dass die konkrete installierte FEE-Version die `TagEntries` sowie direkte und Move-basierte Slotverbindungen über Speichern, Schließen und erneutes Öffnen unverändert liefert. Bis dahin ist der SDK-Vertrag kompiliert und die Projektion automatisiert getestet, aber nicht als praktisch ausgeführter FEE-Round-Trip freigegeben.

Für bestehende Modelle erkennt die Rekonstruktion die vom Vorwärtsgenerator unterstützten Grob-Logiken sowie `Button`, `SegmentedLamp`, `BoolNot` und die unterstützten Cabinet-Elemente. Sie übernimmt nur Variablenzuordnungen, die im gewählten Teilbaum liegen, und verfolgt die vom Generator erzeugte `MoveBit`-Route für mehrfache `PLC_IN_`-Belegungen. Einige FEE-Strukturen verlieren jedoch die ursprüngliche fachliche Unterscheidung: `Cylinder`/`FeedSafetyDoor` verwenden dieselbe Logik und `ReturnCircuit`/`SafeArea` dasselbe `BoolNot`. In diesen Fällen wird ein kompatibler Typ exportiert und zwingend ein Prüfhinweis erzeugt. Unbekannte Logiken und fachfremde Objekte werden nicht als erfundene Container ausgegeben.
