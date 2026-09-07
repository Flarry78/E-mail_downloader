import imaplib
import email
import os
import sys
import time
import json
import hashlib
from dotenv import load_dotenv

# 1. Pfade & Konfiguration laden
SKRIPT_ORDNER = os.path.dirname(os.path.abspath(sys.argv[0]))
load_dotenv(os.path.join(SKRIPT_ORDNER, ".env"))

IMAP_SERVER = os.getenv("IMAP_SERVER")
EMAIL_KONTO = os.getenv("EMAIL_KONTO")
PASSWORT = os.getenv("PASSWORT")
ZIEL_ORDNER = os.getenv("ZIEL_ORDNER", "Downloads")
START_DATUM = os.getenv("START_DATUM", "01-Jan-2026")

MAX_MAILS = int(os.getenv("MAX_MAILS", "0"))
PAUSE_SEKUNDEN = float(os.getenv("PAUSE_SEKUNDEN", "1.0"))
TIMEOUT_SEKUNDEN = int(os.getenv("TIMEOUT_SEKUNDEN", "60"))

# Fortschrittsdatei liegt im selben Ordner wie das Skript/die EXE
FORTSCHRITT_DATEI = os.path.join(SKRIPT_ORDNER, "fortschritt.json")

if not all([IMAP_SERVER, EMAIL_KONTO, PASSWORT]):
    raise ValueError("Fehler: Bitte prüfe die .env-Datei auf fehlende Zugangsdaten.")

if not os.path.exists(ZIEL_ORDNER):
    os.makedirs(ZIEL_ORDNER)

# 2. Bisherigen Fortschritt (JSON-Dictionary) laden
verarbeitete_hashes = {}
if os.path.exists(FORTSCHRITT_DATEI):
    try:
        with open(FORTSCHRITT_DATEI, "r", encoding="utf-8") as f:
            verarbeitete_hashes = json.load(f)
    except Exception:
        verarbeitete_hashes = {}

print(f"Verbinde mit {IMAP_SERVER} für Konto {EMAIL_KONTO}...")

try:
    # 3. IMAP-Verbindung aufbauen
    mail = imaplib.IMAP4_SSL(IMAP_SERVER, timeout=TIMEOUT_SEKUNDEN)
    mail.login(EMAIL_KONTO, PASSWORT)
    mail.select("INBOX")

    # 4. Suche ab Datums-Grenze
    print(f"Suche nach Mails seit dem {START_DATUM}...")
    status, messages = mail.search(None, f'SINCE "{START_DATUM}"')
    
    if not messages[0]:
        print("Keine Mails im angegebenen Datumsbereich gefunden.")
        mail.logout()
        sys.exit()

    mail_ids = messages[0].split()
    print(f"{len(mail_ids)} E-Mail(s) im Zeitraum gefunden. Prüfe auf neue Mails...")

    verarbeitete_in_diesem_lauf = 0

    # 5. Durch alle Mails iterieren
    for m_id in mail_ids:
        # Falls MAX_MAILS gesetzt ist und das Limit erreicht wurde -> Abbrechen
        if MAX_MAILS > 0 and verarbeitete_in_diesem_lauf >= MAX_MAILS:
            print(f"Maximales Limit von {MAX_MAILS} Mails pro Durchlauf erreicht.")
            break

        # Nur Header laden, um schnell die Message-ID zu prüfen
        status, header_data = mail.fetch(m_id, "(BODY.PEEK[HEADER.FIELDS (MESSAGE-ID)])")
        raw_header = header_data[0][1].decode("utf-8", errors="ignore")
        
        # Message-ID extrahieren oder Fallback generieren
        msg_id_line = [line for line in raw_header.split("\r\n") if line.lower().startswith("message-id:")]
        if msg_id_line:
            raw_msg_id = msg_id_line[0].split(":", 1)[1].strip()
        else:
            # Fallback falls keine Message-ID existiert
            raw_msg_id = f"fallback_{m_id.decode()}"

        # Eindeutigen 12-stelligen Hex-Hash erzeugen
        hex_hash = hashlib.sha256(raw_msg_id.encode("utf-8")).hexdigest()[:12]

        # PRÜFUNG: Wurde diese Hex-ID schon einmal verarbeitet?
        if hex_hash in verarbeitete_hashes:
            continue  # Mail überspringen

        # 6. Neue Mail herunterladen (BODY.PEEK belässt Ungelesen-Status)
        status, msg_data = mail.fetch(m_id, "(BODY.PEEK[])")

        for response_part in msg_data:
            if isinstance(response_part, tuple):
                # Dateiname mit eindeutigem Hex-Code
                dateiname = f"Mail_{hex_hash}.eml"
                dateipfad = os.path.join(ZIEL_ORDNER, dateiname)

                with open(dateipfad, "wb") as f:
                    f.write(response_part[1])

                verarbeitete_in_diesem_lauf += 1
                print(f"[{verarbeitete_in_diesem_lauf}] Neu gespeichert: {dateiname} (Hex-ID: {hex_hash})")

                # Hex-ID im Fortschritt speichern
                verarbeitete_hashes[hex_hash] = {
                    "downloaded_at": time.strftime("%Y-%m-%d %H:%M:%S"),
                    "raw_msg_id": raw_msg_id
                }

                # JSON-Fortschrittsdatei direkt aktualisieren
                with open(FORTSCHRITT_DATEI, "w", encoding="utf-8") as f:
                    json.dump(verarbeitete_hashes, f, indent=4)

        # Schonende Pause
        time.sleep(PAUSE_SEKUNDEN)

    mail.logout()
    print(f"Fertig! Es wurden {verarbeitete_in_diesem_lauf} neue E-Mails heruntergeladen.")

except Exception as e:
    print(f"Ein Fehler ist aufgetreten: {e}")