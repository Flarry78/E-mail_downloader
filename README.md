## 💡 Vorweg-Info

Dieses Projekt ist als kleiner Gefallen entstanden, um ein nerviges Alltagsproblem beim E-Mail-Sortieren zu lösen. 

Ich habe dabei **stark auf KI-Unterstützung** gesetzt, um den Code schreiben zu lassen. Mein Hauptziel war ein Experiment aus Neugier: Ich wollte testen, wie schnell, verlässlich und robust man mit KI ein praxistaugliches Tool bauen kann, das im Alltag spürbar Arbeit abnimmt.

# 📩 E-Mail Auto-Sorter & Attachment Manager

Ein Desktop-Tool zur automatisierten Erfassung und Sortierung von E-Mail-Anhängen im `.eml`-Format. Das System nutzt eine **manuelle Erstkontrolle mit automatischer Weiterverarbeitung** für Klassifizierungsgenauigkeit bei maximaler Zeitersparnis.

## 🌟 Key Features
* **Automatisierter IMAP-Download:** Liest ungelesene/aktuelle Mails ab und speichert sie lokal.
* **Plattformunabhängig (`.eml`):** Standardisiertes E-Mail-Format inklusive automatischer Anhang-Dekodierung.
* **Hybride Kontrolle:** Unbekannte Absender landen in `unsorted/` und werden einmalig manuell per GUI zugewiesen.
* **Set Once, Automate Forever:** Absender-Regeln werden dauerhaft in einer SQLite-Datenbank (`rules.db`) gespeichert.
* **Moderne Desktop-UI:** Eigenständige App via **FastAPI**, **Jinja2** & **PyWebView** mit SSE-Realtime-Logs.
* **Standalone EXE:** Vollständig verpackbar via **PyInstaller** inklusive Auto-Portsuche und WebView2-Fallback.

## 🛠️ Tech Stack
* **Backend:** Python 3.x, FastAPI, Uvicorn, SQLite3
* **Frontend:** HTML5, CSS3, Jinja2, JavaScript (SSE)
* **Desktop Wrapper:** PyWebView | **Protokolle:** IMAP, MIME

## 📝 Verbesserungspotenziale und geplante Änderungen

* **Erweiterung der Zuweisung (Firma + Auftrag):** Bisher wurde jede E-Mail nur starr einem festen Firmennamen zugeteilt. Geplant ist, dass jede E-Mail zusätzlich eine Auftragsnummer erhalten muss, da eine Firma mehrere verschiedene Aufträge haben kann.
* **Zentraler Postfach-Look (UI/UX im Outlook-Stil):** Die Weboberfläche bekommt ein zweigeteiltes Layout.
* **Lernende Datenbank (Zeitersparnis):** Bekannte Absender sollen automatisch wiedererkannt und die zugehörige Firma vorausgefüllt werden, um die Tipparbeit auf ein Minimum zu reduzieren.
* **Performance & Geschwindigkeit:** Das Tool soll extrem schnell und schlank laufen (auch auf schwächerer Hardware), damit das Abarbeiten und Verschieben/Umbenennen im Minutentakt flüssig von der Hand geht.

