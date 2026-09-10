# Container2FEE Visual

## Zweck und Abgrenzung

Der Reiter **Container2FEE Visual** ist ein zusätzlicher, levelgeschützter Arbeitsbereich. Der bestehende Reiter **Container2Fee** und dessen Ablauf bleiben unverändert. Beide Wege verwenden am Ende dieselben Containerklassen und denselben `ContainerToFeeService`; dadurch entsteht kein zweiter Generator mit abweichendem Verhalten.

Die visuelle Seite kann eine Container-XML bereits ohne FEE-Verbindung lesen und als Plan darstellen. Erst das Laden vorhandener SimObjects und **Start Generation** benötigen die in Project Settings bestätigte FEE-Verbindung.

![Visueller Container2FEE-Plan mit synthetischen Testdaten](screenshots/container2fee-visual.png)

## Bedienablauf

1. **XML öffnen** wählen. Die Quelldatei wird nur gelesen und nicht verändert.
2. Links Container, Logiken, Signale, technische Hilfsobjekte und mögliche SimObject-Ziele prüfen.
3. Nach erfolgreicher FEE-Verbindung **FEE aktualisieren** drücken. Noch freie Ziele werden wie im bisherigen Ablauf anhand von identischem Komponentennamen und kompatiblem Typ automatisch zugeordnet.
4. Ein FEE-SimObject von rechts auf ein kompatibles Ziel in der Mitte ziehen. Ein Einzelziel wird ersetzt, ein Mehrfachziel ergänzt. Dasselbe FEE-Objekt kann nie gleichzeitig mehreren Containern gehören.
5. In der linken Struktur pro vollständigem Container festlegen, ob er verarbeitet wird. **Alle selektieren** und **Alle deselektieren** ändern diese Auswahl gemeinsam. Kindobjekte erben die Containerentscheidung, weil Logik, Signale und technische Hilfsobjekte keine unabhängig ausführbaren Legacy-Einheiten sind.
6. **Fehlende SimObjects bei der Generierung erzeugen** ist standardmäßig aktiv. Die Einstellung kann je Container oder über **Alle/Keine** für alle erzeugbaren Container geändert werden. Ein ausgewählter Container mit SimObjectTarget benötigt entweder eine grüne Zuordnung oder diese Erzeugungsoption; andernfalls bleibt das Ziel dunkelrot und die Validierung erklärt den Fehler.
7. Änderungen mit **Rückgängig/Wiederholen** korrigieren und über **Plan speichern** sichern.
8. Für eine vollständige Neuerzeugung **Start Generation** drücken. Wenn Container/Logiken bereits existieren, kann stattdessen **Nur SimObjects verknüpfen** verwendet werden.

Technische Objekte sind im Baum standardmäßig eingeklappt. **Alles aufklappen/Alles zuklappen** wirkt auf die kombinierte Container- und Objektstruktur. Die Suchfelder filtern Plan beziehungsweise FEE-Objekte. **Nur kompatible Objekte** bezieht sich auf das aktuell ausgewählte Ziel.

## Sidecar-Datei

Benutzeränderungen werden nicht in die Container-XML geschrieben. Standardmäßig entsteht daneben:

```text
Container.xml.container2fee.visual.json
```

Gespeichert werden ausschließlich Quellfingerabdruck, Ziel-/FEE-Zuordnungen, ausdrücklich deaktivierte SimObject-Erzeugung und abgewählte Container. Schema 4 liest weiterhin Sidecars aus Schema 1–3 und migriert deren frühere Positivliste auf den neuen sicheren Standard. Die entfernte Einstellung **Signale erzeugen** wird beim Laden alter Sidecars ignoriert und als Information ausgewiesen. Der Schreibvorgang erfolgt über eine temporäre Datei und anschließendes Ersetzen. Beim erneuten Öffnen wird der Sidecar automatisch angewendet, sofern der SHA-256-Fingerabdruck der XML noch stimmt. Nach einer XML-Änderung werden alte Zuordnungen nicht stillschweigend übernommen.

## Drag-and-drop-Regeln

- Zulässig sind nur vorhandene FEE-SimObjects, deren Wrapper-Typ dem `AllowedType` des unveränderten Legacy-Containers entspricht.
- Einzelziele besitzen höchstens eine, Mehrfachziele mehrere Zuordnungen.
- Eine Objekt-GUID ist im gesamten Plan höchstens einmal zugeordnet.
- Signal-/Slot- und Parent-/Child-Verknüpfungen werden sichtbar gemacht, aber nicht frei umverdrahtet. Diese Grenze verhindert einen Plan, den der bestehende Generator nicht identisch ausführen könnte.
- Das Entfernen einer Zuordnung löscht kein Objekt in FEE.

## Statusfarben und Link-only

- Ein SimObjectTarget ist **grün**, wenn ein aktuell vorhandenes, typkompatibles FEE-SimObject zugeordnet ist.
- Es ist **hellrot**, wenn das fehlende SimObject bei der vollständigen Generierung erzeugt werden soll.
- Es ist **dunkelrot**, wenn weder Zuordnung noch Erzeugungswunsch vorliegt. Die Validierung nennt das konkrete Ziel und mögliche Korrekturen.
- Ein Eintrag unter **Verfügbare FEE-SimObjects** wird grün, sobald er zugeordnet ist, und nennt das Ziel.

**Nur SimObjects verknüpfen** erzeugt keine BasicFrames, Interfaces, Signale, Logiken oder Container. Der Befehl verwendet die in **Model Validation → Update Objects** eingelesenen `FeeLogic`-Objekte. Für jeden ausgewählten Container muss genau ein vorhandenes LogicObject mit identischem Komponentennamen existieren. Fehlende oder doppelte Logiknamen sowie nicht mehr verfügbare SimObjects brechen vor dem ersten Schreibzugriff mit einer präzisen Fehlermeldung ab. Der Vorgang ist auf `ILogicSimObjectOwner` begrenzt; reine SimObject-Container besitzen keine bestehende Logik, an die in diesem Modus verknüpft werden könnte.

Dieser Link-only-Modus benötigt keine Interface-Auswahl, weil er weder Signale erzeugt noch verändert. Er wird erst aktiv, wenn ein Plan, eine FEE-Verbindung, mindestens ein ausgewählter Container und mindestens eine vorhandene Zielzuordnung vorliegen. Die Tooltips von **FEE aktualisieren**, **Start Generation** und **Nur SimObjects verknüpfen** nennen jeweils die erste konkret fehlende Voraussetzung oder den ersten blockierenden Validierungsfehler.

## ModelValidation-Vertrag

Vor der vollständigen Erzeugung prüft Container2FEE Visual die aus dem ContainerFile eindeutig ableitbaren Pflichtbeziehungen der vorhandenen `ModelValidation`. Fehlt eine erforderliche Signal- oder SimObject-Beziehung, wird vor dem ersten FEE-Schreibzugriff abgebrochen.

Alle geschriebenen Variablen- und Slotverknüpfungen werden über die FEE-API zurückgelesen. Eine nicht übernommene Verbindung gilt als Fehler. Beim Stopper wird `Floor.CollisionSlot` für neue und vorhandene Floors vor dem Verbinden aktiviert und ebenfalls zurückgelesen. Die Größen bleiben die Werte des bisherigen Container2FEE-Generators: Floor `0,01 × 0,2 × 0,05`, Sensor `0,01 × 0,03 × 0,01`, Surface `2 × 0,5 × 0,05`, MotionJoint/Button `0,5 × 0,5 × 0,5` und PickAndPlace `0,1 × 0,1 × 0,1`. Fehlende Bewegungsparameter erhalten prüfbare Startwerte.

Nicht aus dem ContainerFile ableitbar sind reale Positionen, Pick-/Drop-Marks und die konkrete BeltControl-Achsbeziehung. Diese werden nicht erfunden. Nach deren fachlicher Festlegung ist **Model Validation → Update Objects** als Live-Abnahme auszuführen.

## Codeaufteilung

| Bereich | Verantwortung |
| --- | --- |
| `ContainerToFeeVisual/Domain` | stabile Plan-, Knoten-, Kanten-, Ziel- und Zuordnungsmodelle |
| `ContainerToFeeVisual/Planning` | sichere XML-Auswertung und Metadaten der bestehenden Containerklassen |
| `ContainerToFeeVisual/Persistence` | versionierter JSON-Sidecar mit Fingerabdruckprüfung |
| `ContainerToFeeVisual/Discovery` | FEE-Objekterkennung ohne SDK-Objekte an die View weiterzugeben |
| `ContainerToFeeVisual/Execution` | gemeinsame Runtime-Bindung, vollständige Legacy-Generierung und getrennte Link-only-Ausführung |
| `ContainerToFeeVisual/Services` | Orchestrierung, Validierung und Undo/Redo |
| `Application/VM/ContainerToFeeVisualPageVM.cs` | UI-Zustand, Commands, Filter und Status |
| `Application/View/ContainerToFeeVisualPage.xaml` | dreigeteilte WPF-Ansicht und Drag-and-drop-Ziele |

## Bewusste technische Grenzen

Vor einer vollständigen Generierung durchsucht `SignalResolutionPlanner` alle eingelesenen Interfaces. Ein vorhandenes Signal wird nur bei eindeutiger, widerspruchsfreier Identität wiederverwendet und niemals aktualisiert. Nur wenn Signale fehlen, prüft der Executor den installierten Provider über dessen stabile GUID `a6222164-be37-49de-b760-9b1c97c320bb` und erzeugt – wie der bestehende Container2FEE-Ablauf – eine neue zeitgestempelte Interfaceinstanz dieses Providers. Der frei benennbare Instanzname und ein lokalisierter Providertext sind kein Ablehnungsgrund mehr. Fehlt der Provider oder widersprechen sich Providername und GUID, wird vor BasicFrame-, Logik- und SimObject-Erzeugung abgebrochen. Das rechts auswählbare bevorzugte Interface ist optional; **Keins** ist ein expliziter Eintrag.

Der bestehende FEE-Executor unterstützt keinen transaktionalen Rollback. Wird eine laufende SDK-Schreiboperation abgebrochen, kann bereits erzeugter Inhalt bestehen bleiben und muss in FEE geprüft werden. Die neue Pipeline führt deshalb zuerst alle read-only Prüfungen und danach die fehlenden Signalvariablen aus; erst anschließend entstehen BasicFrame, Logiken und SimObjects. Scheitert die SDK-Anlage einer späteren Variablen, können zuvor angelegte Variablen bestehen bleiben. Eine freie grafische Neuverdrahtung oder unabhängige Auswahl einzelner Signale/Hilfsobjekte ist nicht Bestandteil dieser Version.

Die Legacy-Bezeichnungen `PLC_IN_PartPresent` und `PLC_IN_NoPartPresent` werden für `GrobSensor` kompatibel auf Kanal 1 abgebildet; bei zwei gleichnamigen Einträgen erfolgt die Zuordnung auf Kanal 1/2. Die Validierung nennt bei einem wirklich unbekannten Slot jetzt zusätzlich alle zulässigen Slotnamen.

Mehrere Einträge auf demselben `PLC_IN_`-Slot sind zulässig. Bei einem einfachen Einzel-Slot erzeugt Container2FEE je Signal ein unsichtbares `FeeSimpleMove` und führt dessen Eingang gemeinsam mit dem Logik-Slot zusammen; so wird kein Signal überschrieben. Containerklassen, die bereits eine Signalliste für diesen Eingang besitzen, verwenden weiterhin ihre eigene gleichwertige Move-Abbildung. Mehrfach belegte `PLC_OUT_`-Slots sowie sonstige doppelte Slots werden mit Slotname und Anzahl vor dem ersten FEE-Schreibzugriff abgewiesen.
