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
    """ Erstellt die Tabelle. firma_ordner ist optional (NULL erlaubt). """
    conn = sqlite3.connect(DB_PFAD)
    cursor = conn.cursor()
    cursor.execute('''
        CREATE TABLE IF NOT EXISTS regeln (
            kennnummer INTEGER PRIMARY KEY AUTOINCREMENT,
            email_adresse TEXT NOT NULL,
            absender_name TEXT NOT NULL,
            betreff TEXT,
            firma_ordner TEXT,
            UNIQUE(email_adresse, absender_name)
        )
    ''')
    conn.commit()
    conn.close()

def erfasse_oder_pruefe_email(email_adresse, absender_name, betreff):
    """
    Trägt die Mail-Daten in SQLite ein, falls noch nicht vorhanden.
    Gibt den zugewiesenen firma_ordner zurück (falls der Chef ihn schon gesetzt hat).
    """
    conn = sqlite3.connect(DB_PFAD)
    cursor = conn.cursor()
    
    email_clean = email_adresse.lower().strip()
    name_clean = absender_name.strip()

    # 1. Prüfen, ob für diese Kombi bereits ein Firmenordner existiert
    cursor.execute('''
        SELECT firma_ordner FROM regeln 
        WHERE email_adresse = ? AND absender_name = ?
    ''', (email_clean, name_clean))
    ergebnis = cursor.fetchone()
    
    firma_ordner = ergebnis[0] if ergebnis else None

    # 2. In DB eintragen (Falls neu -> firma_ordner bleibt NULL / None)
    cursor.execute('''
        INSERT INTO regeln (email_adresse, absender_name, betreff, firma_ordner)
        VALUES (?, ?, ?, ?)
        ON CONFLICT(email_adresse, absender_name) 
        DO UPDATE SET betreff = excluded.betreff
    ''', (email_clean, name_clean, betreff, firma_ordner))
    
    conn.commit()
    conn.close()
    
    return firma_ordner

init_db()


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

if not all([IMAP_SERVER, EMAIL_KONTO, PASSWORT]):
    logging.critical("Fehler: Bitte prüfe die .env-Datei auf fehlende Zugangsdaten.")
    raise ValueError("Fehler: Bitte prüfe die .env-Datei auf fehlende Zugangsdaten.")

os.makedirs(UNSORTED_ORDNER, exist_ok=True)

# --- 4. Schlanke Fortschritts-Datei laden (Nur Hashes/IDs) ---
verarbeitete_hashes = {}
if os.path.exists(FORTSCHRITT_DATEI):
    try:
        with open(FORTSCHRITT_DATEI, "r", encoding="utf-8") as f:
            verarbeitete_hashes = json.load(f)
    except Exception as e:
        logging.warning(f"Konnte Fortschrittsdatei nicht lesen, erstelle neu: {e}")
        verarbeitete_hashes = {}

# --- 5. IMAP-Verbindung ---
MAX_VERBINDUNGS_VERSUCHE = 3
WARTEZEITEN = [10, 30, 60]
mail = None

for versuch in range(1, MAX_VERBINDUNGS_VERSUCHE + 1):
    try:
        logging.info(f"Verbinde mit {IMAP_SERVER} für Konto {EMAIL_KONTO} (Versuch {versuch}/{MAX_VERBINDUNGS_VERSUCHE})...")
        mail = imaplib.IMAP4_SSL(IMAP_SERVER, timeout=TIMEOUT_SEKUNDEN)
        mail.login(EMAIL_KONTO, PASSWORT)
        mail.select("INBOX")
        logging.info("Erfolgreich mit IMAP-Server verbunden!")
        break
    except Exception as e:
        logging.error(f"Verbindungsfehler bei Versuch {versuch}: {e}")
        if versuch < MAX_VERBINDUNGS_VERSUCHE:
            pause = WARTEZEITEN[versuch - 1]
            logging.info(f"Warte {pause} Sekunden vor dem nächsten Versuch...")
            time.sleep(pause)
        else:
            logging.critical("Keine Verbindung zum Mailserver möglich. Skript bricht ab.")
            sys.exit(1)

# --- 6. Hauptverarbeitung ---
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
        logging.info("Keine relevanten E-Mails im Postfach gefunden.")
        mail.logout()
        sys.exit(0)

    mail_ids = sorted(list(mail_ids_set), key=lambda x: int(x))
    logging.info(f"{len(mail_ids)} E-Mail(s) gefunden. Starte Bulk-Check...")

    all_ids_str = b",".join(mail_ids).decode()
    status, header_data = mail.fetch(all_ids_str, "(BODY.PEEK[HEADER.FIELDS (MESSAGE-ID)])")

    neue_mail_ids = []
    
    for response_part in header_data:
        if isinstance(response_part, tuple):
            raw_headers = response_part[1].decode("utf-8", errors="ignore")
            m_id = response_part[0].split()[0]

            msg_id_line = [line for line in raw_headers.split("\r\n") if line.lower().startswith("message-id:")]
            if msg_id_line:
                raw_msg_id = msg_id_line[0].split(":", 1)[1].strip()
            else:
                raw_msg_id = f"fallback_{m_id.decode()}"

            hex_hash = hashlib.sha256(raw_msg_id.encode("utf-8")).hexdigest()[:12]

            # Nur prüfen, ob Hash bereits bekannt ist
            if hex_hash not in verarbeitete_hashes:
                neue_mail_ids.append((m_id, hex_hash, raw_msg_id))

    logging.info(f"Prüfung fertig! {len(neue_mail_ids)} E-Mail(s) sind neu und werden jetzt heruntergeladen.")

    verarbeitete_in_diesem_lauf = 0

    for m_id, hex_hash, raw_msg_id in neue_mail_ids:
        if MAX_MAILS > 0 and verarbeitete_in_diesem_lauf >= MAX_MAILS:
            logging.info(f"Maximales Limit von {MAX_MAILS} Mails pro Durchlauf erreicht.")
            break

        try:
            status, msg_data = mail.fetch(m_id, "(BODY.PEEK[])")

            for response_part in msg_data:
                if isinstance(response_part, tuple):
                    raw_bytes = response_part[1]
                    msg = email.message_from_bytes(raw_bytes, policy=policy.default)

                    raw_from = decode_mime_header(msg.get("From", ""))
                    display_name, email_adresse = parseaddr(raw_from)
                    
                    subject_raw = msg.get("Subject", "Kein_Betreff")
                    subject = decode_mime_header(subject_raw)
                    subject_clean = clean_filename(subject)[:50]

                    # --- SQLite Update & Check ---
                    # Speichert die Mail-Daten direkt in SQLite & gibt Ordner zurück (falls bekannt)
                    ziel_firma = erfasse_oder_pruefe_email(email_adresse, display_name, subject)

                    # Erstmal in unsorted anlegen
                    ordner_name = f"{subject_clean}_{hex_hash}"
                    temp_ordner_pfad = os.path.join(UNSORTED_ORDNER, ordner_name)
                    os.makedirs(temp_ordner_pfad, exist_ok=True)

                    # A) Anhänge
                    anzahl_anhaenge = 0
                    for part in msg.walk():
                        if part.get_content_disposition() != "attachment":
                            continue

                        filename = part.get_filename()
                        if filename:
                            filename = clean_filename(decode_mime_header(filename))
                            filepath = os.path.join(temp_ordner_pfad, filename)

                            payload = part.get_payload(decode=True)
                            if payload:
                                with open(filepath, "wb") as f:
                                    f.write(payload)
                                anzahl_anhaenge += 1

                    # B) E-Mail-Text
                    txt_pfad = os.path.join(temp_ordner_pfad, "E-Mail_Text.txt")
                    with open(txt_pfad, "w", encoding="utf-8") as f:
                        f.write(f"Von: {raw_from}\n")
                        f.write(f"An: {msg.get('To')}\n")
                        f.write(f"Datum: {msg.get('Date')}\n")
                        f.write(f"Betreff: {subject}\n")
                        f.write("="*50 + "\n\n")

                        body = msg.get_body(preferencelist=('plain', 'html'))
                        if body:
                            f.write(body.get_content())

                    # C) EML Backup
                    eml_pfad = os.path.join(temp_ordner_pfad, "Mail_Backup.eml")
                    with open(eml_pfad, "wb") as f:
                        f.write(raw_bytes)

                    # D) Sortierung anwenden (falls in SQLite bereits zugewiesen)
                    if ziel_firma:
                        firmen_ordner_pfad = os.path.join(ZIEL_ORDNER, ziel_firma)
                        os.makedirs(firmen_ordner_pfad, exist_ok=True)
                        
                        finaler_pfad = os.path.join(firmen_ordner_pfad, ordner_name)
                        shutil.move(temp_ordner_pfad, finaler_pfad)
                        
                        logging.info(f"[{verarbeitete_in_diesem_lauf + 1}] AUTO-SORTIERT zu '{ziel_firma}': {ordner_name}")
                    else:
                        logging.info(f"[{verarbeitete_in_diesem_lauf + 1}] Gespeichert in unsorted/: {ordner_name}")

                    verarbeitete_in_diesem_lauf += 1

                    # --- Nur schlichte Hash-Protokollierung in fortschritt.json ---
                    verarbeitete_hashes[hex_hash] = time.strftime("%Y-%m-%d %H:%M:%S")

                    with open(FORTSCHRITT_DATEI, "w", encoding="utf-8") as f:
                        json.dump(verarbeitete_hashes, f, indent=4)

            time.sleep(PAUSE_SEKUNDEN)

        except Exception as mail_err:
            logging.error(f"Fehler bei E-Mail ID {m_id.decode()}: {mail_err}. Überspringe E-Mail.")
            continue

    logging.info(f"Durchlauf fertig! Es wurden {verarbeitete_in_diesem_lauf} neue E-Mails verarbeitet.")

except Exception as e:
    logging.error(f"Ein unerwarteter Fehler ist aufgetreten: {e}")

finally:
    if mail:
        try:
            mail.logout()
            logging.info("IMAP-Verbindung sauber getrennt.")
        except Exception:
            pass