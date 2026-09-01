# Release-Abnahmecheckliste

Diese Liste auf einem GROB-Desktop mit Netzwerkzugriff, FEE, Kanbanize-Berechtigung und mindestens einer unterstützten TIA-Installation ausführen.

## Automatische Basis

- [ ] `dotnet build VIBN_Tools_App.sln --configuration Release` hat keine Fehler.
- [ ] `Tests/CoreSmokeTests` ist erfolgreich.
- [ ] `Tests/ContainerGenerationSmokeTests` liest `Interface5.xlsx` und `Interface7.xlsx` und meldet `SixLabors.Fonts 1.0.1.0`.
- [ ] `Tests/UiStartupSmokeTests` ist erfolgreich und meldet keine Binding-Fehler.
- [ ] `Tests/Test-TiaHardwareTraversal.ps1` bestätigt Gerätegruppen, Local Session und exakt `E62–73/A62–67` sowie `E74–79/A68–79`.
- [ ] Anwendung startet ohne XamlParseException.

## Rollen und Navigation

- [ ] Nicht-Level7-Benutzer sehen CAD Wizard, Container Generation und Container2Fee nicht.
- [ ] Level7 sieht genau diese drei Bereiche zusätzlich.
- [ ] Level8 sieht außerdem Kanbanize Karten, AI-Test und ViCo-Verwaltung.
- [ ] Level9 kann Rollen ändern; Level8 kann sie nur ansehen.
- [ ] `lutzma` wird als Level9 erkannt und kann nicht verändert/entfernt werden.
- [ ] Eine Änderung, die weniger als zwei Level9-Benutzer hinterließe, wird abgewiesen.

## Project Settings und ViCo

- [ ] Project Settings zeigt nur erreichbare PCs und der Filter wirkt sofort.
- [ ] Ein fehlgeschlagener FEE-Connect zeigt nicht fälschlich „verbunden“.
- [ ] Project Settings zeigt verwendete SDK- und lokal installierte FEE-Version; eine künstlich abweichende Version wird rot hervorgehoben.
- [ ] Von mehreren lokalen Versionsordnern zählt nur ein Ordner mit `Bin\FS.SDK.dll`; höhere unvollständige Ordner werden ignoriert.
- [ ] Ohne FEE-Verbindung sind alle dokumentierten FEE-Aktionen grau, nicht ausführbar und zeigen den Tooltip „Keine Verbindung zu FEE vorhanden.“.
- [ ] ViCo-Suche findet PC, Benutzer und Projekt mit demselben Suchfeld.
- [ ] Spalten Belegung, Software, Standort, Projekt-IP, Sonstiges, RDP-Sitzung, letzte Anmeldung und Benutzer sind plausibel.
- [ ] Nur Planung/In-Arbeit-Projekte stehen in der aktiven Projektauswahl; Backlog/Abschluss stehen im Detailbereich.
- [ ] Frei ist grün, Belegt rot; Online ist grün, Offline rot.
- [ ] Offline-PCs zeigen keine Remote-/Pfadbuttons.
- [ ] RDP-Sitzungsrechte fehlen: Anzeige lautet „Nicht abrufbar“, nicht „offline“.
- [ ] Automatischer Remote-Button nutzt den Kanbanize-Benutzer; der zweite Button zeigt den Windows-Anmeldedialog.
- [ ] Eine vorhandene KONFIGURATION-Unteraufgabe lässt sich bearbeiten und zurückspeichern; keine andere Karteninformation ändert sich.
- [ ] Enter in einem KONFIGURATION-Wertefeld speichert ohne zusätzlichen Button und aktualisiert die sichtbare Tabellenzeile erst nach erfolgreicher Board-Antwort.

## Kanbanize

- [ ] Vorschau verwendet Quell- und Zielboard, Lane und Spalte korrekt.
- [ ] Start ist Quell-Deadline minus 14 Tage.
- [ ] Ziel-Deadline ist Quell-Deadline plus 56 Tage.
- [ ] Nur in der Vorschau markierte Sync-Zeilen werden erstellt oder aktualisiert.
- [ ] Neue Karten sind nach dem Prüfen markiert; Deadline-Updates nicht. Alle selektieren/deselektieren funktioniert.
- [ ] Unterschiedliche Uhrzeiten am selben lokalen Kalendertag erzeugen kein Termin-Update.
- [ ] Ein zweiter Lauf erzeugt keine Duplikate.
- [ ] Mehrdeutige Zielkarte führt zu Konflikt ohne Änderung.
- [ ] Bestehende generierte Karte ändert nur Startfeld und Deadline, nicht Titel/Position/Beschreibung.
- [ ] Eigene Karte kann unabhängig erstellt werden.

## TIA und Special Devices

- [ ] TIA-Version, Attach und PLC-Auswahl funktionieren.
- [ ] Die einzige Hardwareansicht unter Special Devices gruppiert gleiche Gerätenamen und zeigt GSDML, IP, Modultyp, Firmware, E-/A-Bereich und -Länge, Logik und Status.
- [ ] Eine geänderte Logik-/Adresszuordnung wird gespeichert und nach erneutem Auslesen wiederhergestellt.
- [ ] Der reale PN/PN Coupler X2 zeigt genau zwei PROFIsafe-Zeilen, keine adresslosen Kopf-/Interfacezeilen und Byte-Längen 12/6 sowie 6/12.
- [ ] Geräteüberschrift zeigt realen Gerätenamen und -typ; IP, PROFINET-Name und Firmware werden vom Geräte-/Interfaceknoten auf beide adressführenden Module übernommen.
- [ ] `TIA trennen / abbrechen` beendet Attach/Session, leert Listen und beendet TIA Portal selbst nicht.
- [ ] Special-Device-Hardwaretabelle übernimmt nur bewusst ausgewählte/validierte Zeilen.
- [ ] Geräte erscheinen zuerst in der Warteschlange.
- [ ] Fehlerhafte FEE-Erzeugung bleibt prüfbar in der Warteschlange.

## Bestehende VIBN-Funktionen

- [ ] CAD Wizard, Zuli Converter, Container Generation und Container2Fee funktionieren mit einer bekannten Testvorlage.
- [ ] Der bestehende Container2Fee-Reiter arbeitet unverändert.
- [ ] Container2FEE Visual lädt dieselbe XML ohne FEE, zeigt Container/Signale/Links, speichert und lädt den Sidecar und erlaubt nur kompatible Drag-and-drop-Ziele.
- [ ] Containercheckboxen sowie Alle selektieren/deselektieren begrenzen die Aktion auf vollständige unterstützte Container; abgewählte Container werden nicht erzeugt.
- [ ] Fehlende SimObject-Ziele sind rot, Erzeugungswünsche gelb und vorhandene Zuordnungen auf Ziel- und FEE-Objektseite grün dargestellt.
- [ ] **Nur SimObjects verknüpfen** verbindet nach Model Validation → Update Objects vorhandene SimObjects mit genau einer gleichnamigen vorhandenen Logik und erzeugt kein Modellobjekt neu.
- [ ] Container2FEE Visual erzeugt mit denselben Zuordnungen fachlich dasselbe Ergebnis wie der bestehende Executor; Erzeugen und Überspringen sind geprüft.
- [ ] Model Validation, Model Control und Interface Operation funktionieren mit dem Testmodell; Update Objects protokolliert Objektzahl und Laufzeit und ist gegenüber dem Referenzmodell nicht langsamer.
- [ ] Keine bestehende Funktion wurde durch ViCo-/Kanbanize-Aufrufe verändert.

## Übergabe

- [ ] Diagnoseprotokoll enthält keine sensiblen Werte.
- [ ] Anwenderhandbuch und Screenshots sind Bestandteil des Releasepakets.
- [ ] `scripts/Publish-IbnRemote.ps1` erzeugt nur `VIBN_Tools_IBN.exe`; die EXE startet auf einem sauberen Windows-x64-PC ohne .NET-, FEE- oder TIA-Installation und enthält keine Volltool-Reiter.
- [ ] Bekannte externe SDK-Warnungen bzw. Abhängigkeiten sind dokumentiert und keine neue funktionale Warnung aus den geänderten Integrationsmodulen offen.
