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
