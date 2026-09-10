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

## 📝 Verbesserungspotenziale
* **Ordner-Synchronisation:** Automatisches Update der SQLite-Datenbank, wenn Firmenordner im Dateisystem umbenannt werden.
* **Dateityp-Erkennung & Filter:** Gezielter Download bestimmter Dateiendungen (z. B. nur `.pdf`, `.jpeg`) sowie Verwerfen unerwünschter Formate.
* **White- & Blacklisting:** Einfache GUI-Verwaltung zum Blockieren oder Bevorzugen spezifischer Absenderadressen.
* **Spam-Ordner-Durchsuchung:** Optionale Überprüfung des Spam-Ordners, um fälschlicherweise sortierte E-Mails von Whitelist-Absendern trotzdem zu erfassen.