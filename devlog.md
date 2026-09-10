# DEVLOG

## 2026-09-05

- Erste Gedanken und Brainstorming was erreicht werden soll und mögliche Probleme


## 2026-09-06

- Recherche zu bereits vorhandenen E-Mail Programmen die genau das erfüllen was man braucht
- Verglichen, Preise, Funktionen, Einfachheit
- Liste mit Alternativen erstellt


## 2026-09-07

- Angefangen mit Hilfe von Ki die mir den Großteil des Codes gibt ein Script zu erstellen
- Passe es jetzt nach und nach an
- E-Mails ab einem bestimmten Datum downloaden
- Jede E-Mail die rein kommt kriegt eine einzigartige ID zugewiesen
- IDs werden in einer Liste separat vom Script gespeichert.
- Delays eingefügt damit es robuster wird und nicht evtl geblockt wird bei Abfragen


## 2026-09-08

- Anhänge werden jetzt zusätzlich seperat gedownloadet
- alle E-Mails werden als Unterordner gespeicheert um die Mail übersichtlich und kompakt zu behalten
- festgestellt das auch unerwünschte dateien mit gedownloadet werden. 
- und Benennung muss anders


## 2026-09-09

- E-Mails werden überprüft ob diese noch "Ungelesen" sind oder max 5 Tage alt um sicher zu gehen das alle E-Mails erfasst werden ohne jedesmal alle E-Mails zu überprüfen
- Nur Anhänge und die eml werden genommen und nicht inline date , Bilder oder so 
- es wird eine log datei erstellt die nach einer gewissen speichergröße überschrieben / gelöscht wird 
- alles in eine exe verwandelt und getestet
- es wird eine kleine meta datei erstellt um E-Mails neu zu sortieren wenn nachträglich der Zielort festgelegt wird
- den betreff aud der SQLLite Bank entfernt
- Das script nach dem downloaden und sortieren der E-Mails noch einmal den "unsorted" Ordner und verschiebt diese wenn möglich
- FastAPI und HTML benutzt um ein 2tes Script zu erstellen und eine Grafische Oberfläche damit man einfacher die E-Mails zu Firmen hinzufügen kann
- Eine exe aus derm 2ten Script erstell damit sich die Html Seite als Anwendung öffnet


## 2026-09-10

- falls kein absender, wiurd die email als absender kopiert um vergleiche zu behalten und leere sql einträge zu verhindern
- projekt ist ersma fertig
- html überabeitet damit alles über die app.exe gesteuert werden kann
- autostart mit anmeldung und zeitabstand idee verworfen
