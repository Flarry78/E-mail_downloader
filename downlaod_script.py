import imaplib
import email
from email import policy
from email.header import decode_header
import os
import sys
import time
import json
import hashlib
import re
from dotenv import load_dotenv

def clean_filename(name):
    """ Entfernt ungültige Zeichen für Ordner- und Dateinamen """
    if not name:
        return "Unbekannt"
    # Ungültige Zeichen durch Unterstrich ersetzen
    clean = re.sub(r'[\\/*?:"<>|\r\n]', "_", str(name))
    # Mehrfache Leerzeichen/Unterstriche bereinigen
    clean = re.sub(r'\s+', ' ', clean).strip()
    return clean if clean else "Unbekannt"

def decode_mime_header(header_value):
    """ Dekodiert Sonderzeichen/Umlaute im Betreff oder Absender """
    if not header_value:
        return ""
    decoded_fragments = decode_header(header_value)
    text = ""
    for fragment, encoding in decoded_fragments:
        if isinstance(fragment, bytes):
            text += fragment.decode(encoding or "utf-8", errors="ignore")
        else:
            text += str(fragment)
    return text

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

FORTSCHRITT_DATEI = os.path.join(SKRIPT_ORDNER, "fortschritt.json")

if not all([IMAP_SERVER, EMAIL_KONTO, PASSWORT]):
    raise ValueError("Fehler: Bitte prüfe die .env-Datei auf fehlende Zugangsdaten.")

if not os.path.exists(ZIEL_ORDNER):
    os.makedirs(ZIEL_ORDNER)

# 2. Bisherigen Fortschritt laden
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
        if MAX_MAILS > 0 and verarbeitete_in_diesem_lauf >= MAX_MAILS:
            print(f"Maximales Limit von {MAX_MAILS} Mails pro Durchlauf erreicht.")
            break

        # Nur Header laden für Message-ID
        status, header_data = mail.fetch(m_id, "(BODY.PEEK[HEADER.FIELDS (MESSAGE-ID)])")
        raw_header = header_data[0][1].decode("utf-8", errors="ignore")
        
        msg_id_line = [line for line in raw_header.split("\r\n") if line.lower().startswith("message-id:")]
        if msg_id_line:
            raw_msg_id = msg_id_line[0].split(":", 1)[1].strip()
        else:
            raw_msg_id = f"fallback_{m_id.decode()}"

        hex_hash = hashlib.sha256(raw_msg_id.encode("utf-8")).hexdigest()[:12]

        if hex_hash in verarbeitete_hashes:
            continue  # Bereits verarbeitet

        # 6. E-Mail herunterladen
        status, msg_data = mail.fetch(m_id, "(BODY.PEEK[])")

        for response_part in msg_data:
            if isinstance(response_part, tuple):
                raw_bytes = response_part[1]
                # E-Mail Objekt mit neuerer Policy parsen
                msg = email.message_from_bytes(raw_bytes, policy=policy.default)
                
                # Betreff & Datum für Ordnernamen aufbereiten
                subject_raw = msg.get("Subject", "Kein_Betreff")
                subject = decode_mime_header(subject_raw)
                subject_clean = clean_filename(subject)[:50]  # Auf 50 Zeichen kürzen
                
                # Ordnername erstellen: z.B. "2026-09-08_Angebot_Server_a1b2c3d4e5f6"
                ordner_name = f"{subject_clean}_{hex_hash}"
                email_ordner_pfad = os.path.join(ZIEL_ORDNER, ordner_name)
                
                os.makedirs(email_ordner_pfad, exist_ok=True)

                # --- A) Anhänge extrahieren ---
                anzahl_anhaenge = 0
                for part in msg.walk():
                    filename = part.get_filename()
                    if filename:
                        filename = clean_filename(decode_mime_header(filename))
                        filepath = os.path.join(email_ordner_pfad, filename)
                        
                        payload = part.get_payload(decode=True)
                        if payload:
                            with open(filepath, "wb") as f:
                                f.write(payload)
                            anzahl_anhaenge += 1

                # --- B) E-Mail-Text als TXT abspeichern ---
                txt_pfad = os.path.join(email_ordner_pfad, "E-Mail_Text.txt")
                with open(txt_pfad, "w", encoding="utf-8") as f:
                    f.write(f"Von: {msg.get('From')}\n")
                    f.write(f"An: {msg.get('To')}\n")
                    f.write(f"Datum: {msg.get('Date')}\n")
                    f.write(f"Betreff: {subject}\n")
                    f.write("="*50 + "\n\n")
                    
                    body = msg.get_body(preferencelist=('plain', 'html'))
                    if body:
                        f.write(body.get_content())

                # --- C) E-Mail als EML sichern ---
                eml_pfad = os.path.join(email_ordner_pfad, "Mail_Backup.eml")
                with open(eml_pfad, "wb") as f:
                    f.write(raw_bytes)

                verarbeitete_in_diesem_lauf += 1
                print(f"[{verarbeitete_in_diesem_lauf}] Gespeichert in: {ordner_name} ({anzahl_anhaenge} Anhang/Anhänge)")

                # Fortschritt protokollieren
                verarbeitete_hashes[hex_hash] = {
                    "downloaded_at": time.strftime("%Y-%m-%d %H:%M:%S"),
                    "raw_msg_id": raw_msg_id,
                    "folder": ordner_name,
                    "attachments_count": anzahl_anhaenge
                }

                with open(FORTSCHRITT_DATEI, "w", encoding="utf-8") as f:
                    json.dump(verarbeitete_hashes, f, indent=4)

        time.sleep(PAUSE_SEKUNDEN)

    mail.logout()
    print(f"Fertig! Es wurden {verarbeitete_in_diesem_lauf} neue E-Mails verarbeitet.")

except Exception as e:
    print(f"Ein Fehler ist aufgetreten: {e}")