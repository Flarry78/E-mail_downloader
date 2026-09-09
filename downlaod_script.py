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
from datetime import datetime, timedelta
from dotenv import load_dotenv


def clean_filename(name):
    """ Entfernt ungültige Zeichen für Ordner- und Dateinamen """
    if not name:
        return "Unbekannt"
    clean = re.sub(r'[\\/*?:"<>|\r\n]', "_", str(name))
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

# Anzahl Tage, die rückwirkend auch GELESENE Mails geprüft werden sollen (Fallback für angeklickte Mails)
TAGE_RUECKWIRKEND = int(os.getenv("TAGE_RUECKWIRKEND", "2"))

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

# 3. IMAP-Verbindung mit Retry-Mechanismus aufbauen
MAX_VERBINDUNGS_VERSUCHE = 3
WARTEZEITEN = [10, 30, 60]
mail = None

for versuch in range(1, MAX_VERBINDUNGS_VERSUCHE + 1):
    try:
        print(f"Verbinde mit {IMAP_SERVER} für Konto {EMAIL_KONTO} (Versuch {versuch}/{MAX_VERBINDUNGS_VERSUCHE})...")
        mail = imaplib.IMAP4_SSL(IMAP_SERVER, timeout=TIMEOUT_SEKUNDEN)
        mail.login(EMAIL_KONTO, PASSWORT)
        mail.select("INBOX")
        print("Erfolgreich verbunden!")
        break
    except Exception as e:
        print(f"Verbindungsfehler bei Versuch {versuch}: {e}")
        if versuch < MAX_VERBINDUNGS_VERSUCHE:
            pause = WARTEZEITEN[versuch - 1]
            print(f"Warte {pause} Sekunden vor dem nächsten Versuch...")
            time.sleep(pause)
        else:
            print("Keine Verbindung zum Mailserver möglich. Skript bricht ab.")
            sys.exit(1)

# 4. Hauptverarbeitung mit erweiterter Kombi-Suche
try:
    # Datum für den Rückblick berechnen (z.B. Format für IMAP: "07-Sep-2026")
    datum_grenze = (datetime.now() - timedelta(days=TAGE_RUECKWIRKEND)).strftime("%d-%b-%Y")

    mail_ids_set = set()

    # A) Suche 1: Alle UNGELESENEN Mails finden
    status, res_unseen = mail.search(None, 'UNSEEN')
    if res_unseen[0]:
        mail_ids_set.update(res_unseen[0].split())

    # B) Suche 2: Alle Mails der letzten X Tage finden (egal ob gelesen oder ungelesen)
    status, res_recent = mail.search(None, f'SINCE "{datum_grenze}"')
    if res_recent[0]:
        mail_ids_set.update(res_recent[0].split())

    if not mail_ids_set:
        print("Keine relevanten E-Mails im Postfach gefunden.")
        mail.logout()
        sys.exit(0)

    # IDs numerisch sortieren
    mail_ids = sorted(list(mail_ids_set), key=lambda x: int(x))
    print(f"{len(mail_ids)} relevante E-Mail(s) (ungelesen oder aus den letzten {TAGE_RUECKWIRKEND} Tagen) gefunden. Prüfe schnell...")

    # BULK-FETCH: Alle Message-IDs in EINER Server-Anfrage abrufen
    all_ids_str = b",".join(mail_ids).decode()
    status, header_data = mail.fetch(all_ids_str, "(BODY.PEEK[HEADER.FIELDS (MESSAGE-ID)])")

    neue_mail_ids = []
    
    for response_part in header_data:
        if isinstance(response_part, tuple):
            raw_headers = response_part[1].decode("utf-8", errors="ignore")
            m_id = response_part[0].split()[0]  # Server Mail-ID

            msg_id_line = [line for line in raw_headers.split("\r\n") if line.lower().startswith("message-id:")]
            if msg_id_line:
                raw_msg_id = msg_id_line[0].split(":", 1)[1].strip()
            else:
                raw_msg_id = f"fallback_{m_id.decode()}"

            hex_hash = hashlib.sha256(raw_msg_id.encode("utf-8")).hexdigest()[:12]

            if hex_hash not in verarbeitete_hashes:
                neue_mail_ids.append((m_id, hex_hash, raw_msg_id))

    print(f"Prüfung fertig! {len(neue_mail_ids)} E-Mail(s) sind noch nicht verarbeitet und werden jetzt heruntergeladen.")

    verarbeitete_in_diesem_lauf = 0

    # 5. Nur noch die echten NEUEN Mails herunterladen
    for m_id, hex_hash, raw_msg_id in neue_mail_ids:
        if MAX_MAILS > 0 and verarbeitete_in_diesem_lauf >= MAX_MAILS:
            print(f"Maximales Limit von {MAX_MAILS} Mails pro Durchlauf erreicht.")
            break

        try:
            status, msg_data = mail.fetch(m_id, "(BODY.PEEK[])")

            for response_part in msg_data:
                if isinstance(response_part, tuple):
                    raw_bytes = response_part[1]
                    msg = email.message_from_bytes(raw_bytes, policy=policy.default)

                    # Ordnernamen aufbereiten
                    subject_raw = msg.get("Subject", "Kein_Betreff")
                    subject = decode_mime_header(subject_raw)
                    subject_clean = clean_filename(subject)[:50]

                    ordner_name = f"{subject_clean}_{hex_hash}"
                    email_ordner_pfad = os.path.join(ZIEL_ORDNER, ordner_name)
                    os.makedirs(email_ordner_pfad, exist_ok=True)

                    # --- A) Nur ECHTE Anhänge extrahieren (Keine Inline-Bilder) ---
                    anzahl_anhaenge = 0
                    for part in msg.walk():
                        if part.get_content_disposition() != "attachment":
                            continue

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

                    # Fortschritt sichern
                    verarbeitete_hashes[hex_hash] = {
                        "downloaded_at": time.strftime("%Y-%m-%d %H:%M:%S"),
                        "raw_msg_id": raw_msg_id,
                        "folder": ordner_name,
                        "attachments_count": anzahl_anhaenge
                    }

                    with open(FORTSCHRITT_DATEI, "w", encoding="utf-8") as f:
                        json.dump(verarbeitete_hashes, f, indent=4)

            time.sleep(PAUSE_SEKUNDEN)

        except Exception as mail_err:
            print(f"Fehler bei E-Mail ID {m_id.decode()}: {mail_err}. Überspringe E-Mail.")
            continue

    print(f"Fertig! Es wurden {verarbeitete_in_diesem_lauf} neue E-Mails verarbeitet.")

except Exception as e:
    print(f"Ein unerwarteter Fehler ist aufgetreten: {e}")

finally:
    if mail:
        try:
            mail.logout()
            print("IMAP-Verbindung sauber getrennt.")
        except Exception:
            pass