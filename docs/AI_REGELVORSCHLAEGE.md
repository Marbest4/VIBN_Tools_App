# AI-Test – Regelvorschläge

## Datenbasis

ContainerGeneration protokolliert direkte Änderungen als JSONL im Ordner `vibn_ai_data/actions`. Schema 2 enthält mindestens Zeitstempel, Aktionstyp, Eigenschaft, Vorher-/Nachherwert, stabile Signal-ID sowie einen SHA-256-basierten Quellschlüssel. Der Quellschlüssel enthält keine Klartextpfade. Alte Schema-1-Zeilen bleiben lesbar.

Erfasst werden Slotwechsel, Verschieben/Hinzufügen und direkte Änderungen an Signal, ID, Adresse, Datentyp, Notiz sowie Containername/-typ. Das sichtbare Arbeitsbereichsprotokoll bleibt davon getrennt: JSONL ist die strukturierte Auswertungsquelle.

## Vorschlagslogik

Die erste Stufe ist absichtlich deterministisch und nicht generativ. Sie gruppiert tatsächliche Slotkorrekturen nach:

- Komponententyp,
- exaktem Signaltext,
- bisherigem Slot,
- neuem Slot.

`Häufigkeit` ist die Anzahl beobachteter Änderungen. `Fälle` zählt unterschiedliche relevante Kombinationen aus Quelle und Signal-ID für denselben Typ/Signal/alten Slot. Die Konfidenz ist:

`unterstützende unterschiedliche Fälle / alle unterschiedlichen relevanten Fälle`

Damit führt ein mehrfaches Klicken im selben Fall nicht künstlich zu hoher Sicherheit. Gegensätzliche Zielslots senken die Konfidenz sichtbar. Die exakte Signalregel ist konservativ; Regex-Verallgemeinerungen werden erst dann sinnvoll, wenn genügend fachlich freigegebene Fälle und eine messbare Evaluierung vorliegen.

## Prüfung

Im Unterreiter **Regelvorschläge** können Vorschläge aktualisiert, angenommen oder abgelehnt werden. Der Status wird atomar in `rule_suggestion_reviews.json` gespeichert. **Annehmen verändert die Requirements-XML noch nicht.** Das verhindert, dass eine statistische Beobachtung ungeprüft produktive Regeln verändert.

Noch offen ist der sichere Requirements-Writer mit konkreter XML-Vorschau, Backup, Schema-Validierung und bestätigter atomarer Übernahme. Bis dahin ist `Accepted` eine fachliche Freigabe zur späteren Umsetzung, keine bereits aktive Generatorregel.

