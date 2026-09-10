import imaplib
import email
from email import policy
from email.header import decode_header
from email.utils import parseaddr
import os
import sys
import time
import json
import hashlib
import re
import shutil
import sqlite3
import logging
from logging.handlers import RotatingFileHandler
from datetime import datetime, timedelta
from dotenv import load_dotenv

# --- 1. Pfade & Konfiguration laden ---
if getattr(sys, 'frozen', False):
    SKRIPT_ORDNER = os.path.dirname(sys.executable)
else:
    SKRIPT_ORDNER = os.path.dirname(os.path.abspath(__file__))

load_dotenv(os.path.join(SKRIPT_ORDNER, ".env"))

# --- 2. Logging einrichten ---
log_pfad = os.path.join(SKRIPT_ORDNER, 'downloader.log')
log_handler = RotatingFileHandler(
    log_pfad, 
    maxBytes=1024 * 1024, 
    backupCount=2, 
    encoding='utf-8'
)

logging.basicConfig(
    handlers=[log_handler],
    level=logging.INFO,
    format='%(asctime)s - %(levelname)s - %(message)s'
)

console_handler = logging.StreamHandler(sys.stdout)
console_handler.setFormatter(logging.Formatter('%(asctime)s - %(levelname)s - %(message)s'))
logging.getLogger().addHandler(console_handler)

# --- 3. SQLite Datenbank Integration ---
DB_PFAD = os.path.join(SKRIPT_ORDNER, "rules.db")

def init_db():
    conn = sqlite3.connect(DB_PFAD)
    cursor = conn.cursor()
    cursor.execute('''
        CREATE TABLE IF NOT EXISTS regeln (
            kennnummer INTEGER PRIMARY KEY AUTOINCREMENT,
            email_adresse TEXT NOT NULL,
            absender_name TEXT NOT NULL,
            firma_ordner TEXT,
            UNIQUE(email_adresse, absender_name)
        )
    ''')
    conn.commit()
    conn.close()

def erfasse_oder_pruefe_email(email_adresse, absender_name):
    conn = sqlite3.connect(DB_PFAD)
    cursor = conn.cursor()

    email_clean = email_adresse.lower().strip()
    name_clean = absender_name.strip() if absender_name and absender_name.strip() else email_clean

    cursor.execute('''
        SELECT firma_ordner FROM regeln 
        WHERE email_adresse = ? AND absender_name = ?
    ''', (email_clean, name_clean))
    ergebnis = cursor.fetchone()
    
    firma_ordner = ergebnis[0] if ergebnis else None

    cursor.execute('''
        INSERT INTO regeln (email_adresse, absender_name, firma_ordner)
        VALUES (?, ?, NULL)
        ON CONFLICT(email_adresse, absender_name) DO NOTHING
    ''', (email_clean, name_clean))
    
    conn.commit()
    conn.close()
    return firma_ordner

def sortiere_unsorted_nach(ziel_ordner_pfad, db_pfad):
    unsorted_pfad = os.path.join(ziel_ordner_pfad, "unsorted")
    if not os.path.exists(unsorted_pfad):
        return

    conn = sqlite3.connect(db_pfad)
    cursor = conn.cursor()

    verarbeitete_ordner = 0
    for ordner_name in os.listdir(unsorted_pfad):
        ordner_pfad = os.path.join(unsorted_pfad, ordner_name)
        meta_datei = os.path.join(ordner_pfad, ".meta.json")

        if os.path.isdir(ordner_pfad) and os.path.exists(meta_datei):
            try:
                with open(meta_datei, "r", encoding="utf-8") as f:
                    meta = json.load(f)

                email_clean = meta.get("email_adresse", "").lower().strip()
                name_clean = meta.get("absender_name", "").strip()

                cursor.execute('''
                    SELECT firma_ordner FROM regeln 
                    WHERE email_adresse = ? AND absender_name = ? 
                      AND firma_ordner IS NOT NULL AND firma_ordner != ''
                ''', (email_clean, name_clean))
                
                ergebnis = cursor.fetchone()

                if ergebnis:
                    ziel_firma = ergebnis[0]
                    firmen_ordner_pfad = os.path.join(ziel_ordner_pfad, ziel_firma)
                    os.makedirs(firmen_ordner_pfad, exist_ok=True)

                    finaler_pfad = os.path.join(firmen_ordner_pfad, ordner_name)
                    shutil.move(ordner_pfad, finaler_pfad)
                    logging.info(f"Nachträglich aus unsorted/ einsortiert nach '{ziel_firma}': {ordner_name}")
                    verarbeitete_ordner += 1

            except Exception as e:
                logging.error(f"Fehler beim Re-Sortieren von {ordner_name}: {e}")

    conn.close()
    if verarbeitete_ordner > 0:
        logging.info(f"Nachtrags-Sortierung abgeschlossen: {verarbeitete_ordner} Ordner verschoben.")

def clean_filename(name):
    if not name:
        return "Unbekannt"
    clean = re.sub(r'[\\/*?:"<>|\r\n]', "_", str(name))
    clean = re.sub(r'\s+', ' ', clean).strip()
    return clean if clean else "Unbekannt"

def decode_mime_header(header_value):
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

IMAP_SERVER = os.getenv("IMAP_SERVER")
EMAIL_KONTO = os.getenv("EMAIL_KONTO")
PASSWORT = os.getenv("PASSWORT")
ZIEL_ORDNER = os.getenv("ZIEL_ORDNER", "Downloads")

if not os.path.isabs(ZIEL_ORDNER):
    ZIEL_ORDNER = os.path.join(SKRIPT_ORDNER, ZIEL_ORDNER)

UNSORTED_ORDNER = os.path.join(ZIEL_ORDNER, "unsorted")
TAGE_RUECKWIRKEND = int(os.getenv("TAGE_RUECKWIRKEND", "2"))
MAX_MAILS = int(os.getenv("MAX_MAILS", "0"))
PAUSE_SEKUNDEN = float(os.getenv("PAUSE_SEKUNDEN", "1.0"))
TIMEOUT_SEKUNDEN = int(os.getenv("TIMEOUT_SEKUNDEN", "60"))
FORTSCHRITT_DATEI = os.path.join(SKRIPT_ORDNER, "fortschritt.json")

def starte_download_und_sortierung():
    """ Kern-Funktion für den E-Mail-Abruf. Generiert Log-Zeilen für den Web-Stream. """
    init_db()

    if not all([IMAP_SERVER, EMAIL_KONTO, PASSWORT]):
        yield "Fehler: Bitte prüfe die .env-Datei auf fehlende Zugangsdaten."
        return

    os.makedirs(UNSORTED_ORDNER, exist_ok=True)

    verarbeitete_hashes = {}
    if os.path.exists(FORTSCHRITT_DATEI):
        try:
            with open(FORTSCHRITT_DATEI, "r", encoding="utf-8") as f:
                verarbeitete_hashes = json.load(f)
        except Exception as e:
            yield f"Hinweis: Fortschrittsdatei neu erstellt ({e})"

    MAX_VERBINDUNGS_VERSUCHE = 3
    WARTEZEITEN = [10, 30, 60]
    mail = None

    for versuch in range(1, MAX_VERBINDUNGS_VERSUCHE + 1):
        try:
            yield f"Verbinde mit {IMAP_SERVER} ({EMAIL_KONTO}) - Versuch {versuch}/{MAX_VERBINDUNGS_VERSUCHE}..."
            mail = imaplib.IMAP4_SSL(IMAP_SERVER, timeout=TIMEOUT_SEKUNDEN)
            mail.login(EMAIL_KONTO, PASSWORT)
            mail.select("INBOX")
            yield "Erfolgreich mit IMAP-Server verbunden!"
            break
        except Exception as e:
            yield f"Verbindungsfehler: {e}"
            if versuch < MAX_VERBINDUNGS_VERSUCHE:
                pause = WARTEZEITEN[versuch - 1]
                yield f"Warte {pause} Sekunden vor nächstem Versuch..."
                time.sleep(pause)
            else:
                yield "Keine Verbindung zum Mailserver möglich."
                return

    try:
        datum_grenze = (datetime.now() - timedelta(days=TAGE_RUECKWIRKEND)).strftime("%d-%b-%Y")
        mail_ids_set = set()

        status, res_unseen = mail.search(None, 'UNSEEN')
        if res_unseen[0]:
            mail_ids_set.update(res_unseen[0].split())

        status, res_recent = mail.search(None, f'SINCE "{datum_grenze}"')
        if res_recent[0]:
            mail_ids_set.update(res_recent[0].split())

        if not mail_ids_set:
            yield "Keine neuen E-Mails gefunden."
        else:
            mail_ids = sorted(list(mail_ids_set), key=lambda x: int(x))
            yield f"{len(mail_ids)} E-Mails gefunden. Starte Abgleich..."

            all_ids_str = b",".join(mail_ids).decode()
            status, header_data = mail.fetch(all_ids_str, "(BODY.PEEK[HEADER.FIELDS (MESSAGE-ID)])")

            neue_mail_ids = []
            for response_part in header_data:
                if isinstance(response_part, tuple):
                    raw_headers = response_part[1].decode("utf-8", errors="ignore")
                    m_id = response_part[0].split()[0]

                    msg_id_line = [line for line in raw_headers.split("\r\n") if line.lower().startswith("message-id:")]
                    raw_msg_id = msg_id_line[0].split(":", 1)[1].strip() if msg_id_line else f"fallback_{m_id.decode()}"

                    hex_hash = hashlib.sha256(raw_msg_id.encode("utf-8")).hexdigest()[:12]
                    if hex_hash not in verarbeitete_hashes:
                        neue_mail_ids.append((m_id, hex_hash, raw_msg_id))

            yield f"{len(neue_mail_ids)} E-Mails sind neu und werden jetzt verarbeitet."

            verarbeitete_in_diesem_lauf = 0
            for m_id, hex_hash, raw_msg_id in neue_mail_ids:
                if MAX_MAILS > 0 and verarbeitete_in_diesem_lauf >= MAX_MAILS:
                    yield f"Maximales Limit von {MAX_MAILS} Mails erreicht."
                    break

                try:
                    status, msg_data = mail.fetch(m_id, "(BODY.PEEK[])")
                    for response_part in msg_data:
                        if isinstance(response_part, tuple):
                            raw_bytes = response_part[1]
                            msg = email.message_from_bytes(raw_bytes, policy=policy.default)

                            raw_from = decode_mime_header(msg.get("From", ""))
                            display_name, email_adresse = parseaddr(raw_from)

                            if not display_name or not display_name.strip():
                                display_name = email_adresse

                            subject = decode_mime_header(msg.get("Subject", "Kein_Betreff"))
                            ziel_firma = erfasse_oder_pruefe_email(email_adresse, display_name)

                            absender_clean = clean_filename(display_name)[:50]
                            ordner_name = f"{absender_clean}_{hex_hash}"
                            temp_ordner_pfad = os.path.join(UNSORTED_ORDNER, ordner_name)
                            os.makedirs(temp_ordner_pfad, exist_ok=True)

                            meta_daten = {
                                "email_adresse": email_adresse.lower().strip(),
                                "absender_name": display_name.strip(),
                                "hash": hex_hash
                            }
                            with open(os.path.join(temp_ordner_pfad, ".meta.json"), "w", encoding="utf-8") as f:
                                json.dump(meta_daten, f, indent=4)

                            for part in msg.walk():
                                if part.get_content_disposition() == "attachment":
                                    filename = part.get_filename()
                                    if filename:
                                        filename = clean_filename(decode_mime_header(filename))
                                        filepath = os.path.join(temp_ordner_pfad, filename)
                                        payload = part.get_payload(decode=True)
                                        if payload:
                                            with open(filepath, "wb") as f:
                                                f.write(payload)

                            txt_pfad = os.path.join(temp_ordner_pfad, "E-Mail_Text.txt")
                            with open(txt_pfad, "w", encoding="utf-8") as f:
                                f.write(f"Von: {raw_from}\nAn: {msg.get('To')}\nDatum: {msg.get('Date')}\nBetreff: {subject}\n{'='*50}\n\n")
                                body = msg.get_body(preferencelist=('plain', 'html'))
                                if body:
                                    f.write(body.get_content())

                            eml_pfad = os.path.join(temp_ordner_pfad, "Mail_Backup.eml")
                            with open(eml_pfad, "wb") as f:
                                f.write(raw_bytes)

                            if ziel_firma:
                                firmen_ordner_pfad = os.path.join(ZIEL_ORDNER, ziel_firma)
                                os.makedirs(firmen_ordner_pfad, exist_ok=True)
                                shutil.move(temp_ordner_pfad, os.path.join(firmen_ordner_pfad, ordner_name))
                                yield f"[{verarbeitete_in_diesem_lauf + 1}] Auto-sortiert zu '{ziel_firma}': {display_name}"
                            else:
                                yield f"[{verarbeitete_in_diesem_lauf + 1}] Abgelegt unter unsorted/: {display_name}"

                            verarbeitete_in_diesem_lauf += 1
                            verarbeitete_hashes[hex_hash] = time.strftime("%Y-%m-%d %H:%M:%S")
                            with open(FORTSCHRITT_DATEI, "w", encoding="utf-8") as f:
                                json.dump(verarbeitete_hashes, f, indent=4)

                    time.sleep(PAUSE_SEKUNDEN)

                except Exception as mail_err:
                    yield f"Fehler bei Mail {m_id.decode()}: {mail_err}"
                    continue

            yield f"Fertig! {verarbeitete_in_diesem_lauf} neue E-Mails verarbeitet."

        yield "Prüfe unsorted/-Ordner auf Nachtrags-Zuordnungen..."
        sortiere_unsorted_nach(ZIEL_ORDNER, DB_PFAD)
        yield "Vorgang vollständig abgeschlossen."

    except Exception as e:
        yield f"Unerwarteter Fehler: {e}"
    finally:
        if mail:
            try:
                mail.logout()
            except Exception:
                pass