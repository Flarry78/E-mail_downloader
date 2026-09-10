import os
import sys
import sqlite3
import threading
import socket
import time
import urllib.request
import subprocess
import uvicorn
import webview
from fastapi import FastAPI, Request, Form
from fastapi.responses import RedirectResponse, StreamingResponse
from fastapi.templating import Jinja2Templates

# --- Absolute Pfade auflösen (PyInstaller-kompatibel) ---
if getattr(sys, 'frozen', False):
    BASE_DIR = sys._MEIPASS
    EXECUTIVE_DIR = os.path.dirname(sys.executable)
else:
    BASE_DIR = os.path.dirname(os.path.abspath(__file__))
    EXECUTIVE_DIR = BASE_DIR

TEMPLATES_DIR = os.path.join(BASE_DIR, "templates")

from download_script import DB_PFAD, ZIEL_ORDNER, init_db, sortiere_unsorted_nach, starte_download_und_sortierung

if not os.path.exists(ZIEL_ORDNER):
    try:
        os.makedirs(ZIEL_ORDNER, exist_ok=True)
    except Exception as e:
        print(f"Hinweis: Zielordner konnte nicht erstellt werden: {e}")

app = FastAPI(title="E-Mail Absender Zuordnungen")

try:
    init_db()
except Exception as e:
    print(f"Fehler bei Datenbank-Initialisierung: {e}")

templates = Jinja2Templates(directory=TEMPLATES_DIR)

def get_db_connection():
    conn = sqlite3.connect(DB_PFAD)
    conn.row_factory = sqlite3.Row
    return conn

@app.get("/")
def index(request: Request):
    offene_absender = []
    try:
        conn = get_db_connection()
        cursor = conn.cursor()
        cursor.execute("""
            SELECT kennnummer, email_adresse, absender_name, firma_ordner 
            FROM regeln 
            WHERE firma_ordner IS NULL OR firma_ordner = ''
            ORDER BY absender_name ASC
        """)
        offene_absender = cursor.fetchall()
        conn.close()
    except Exception as e:
        print(f"Fehler beim Laden der Absender aus der Datenbank: {e}")

    existierende_firmen = []
    if os.path.exists(ZIEL_ORDNER):
        try:
            for eintrag in os.listdir(ZIEL_ORDNER):
                vollstaendiger_pfad = os.path.join(ZIEL_ORDNER, eintrag)
                if os.path.isdir(vollstaendiger_pfad) and eintrag.lower() != "unsorted":
                    existierende_firmen.append(eintrag)
        except Exception as e:
            print(f"Fehler beim Lesen des Ziel-Ordners: {e}")
    
    existierende_firmen.sort()

    return templates.TemplateResponse(
        request=request, 
        name="index.html", 
        context={
            "offene_absender": offene_absender,
            "existierende_firmen": existierende_firmen
        }
    )

@app.post("/speichern")
def speichern(
    kennnummer: int = Form(...),
    firma_ordner: str = Form(...)
):
    firma_clean = firma_ordner.strip()
    if firma_clean:
        try:
            conn = get_db_connection()
            cursor = conn.cursor()
            cursor.execute("""
                UPDATE regeln 
                SET firma_ordner = ? 
                WHERE kennnummer = ?
            """, (firma_clean, kennnummer))
            conn.commit()
            conn.close()

            sortiere_unsorted_nach(ZIEL_ORDNER, DB_PFAD)
        except Exception as e:
            print(f"Fehler beim Speichern der Regel: {e}")

    return RedirectResponse(url="/", status_code=303)

@app.get("/stream-download")
def stream_download():
    """ Live-Stream aller Output-Zeilen des Mail-Downloads per EventSource. """
    def event_generator():
        for zeile in starte_download_und_sortierung():
            yield f"data: {zeile}\n\n"
            time.sleep(0.05)
        yield "data: [DONE]\n\n"

    return StreamingResponse(event_generator(), media_type="text/event-stream")

def find_free_port():
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as s:
        s.bind(('127.0.0.1', 0))
        return s.getsockname()[1]

def wait_for_server(port, timeout=10):
    start_time = time.time()
    while time.time() - start_time < timeout:
        try:
            with socket.create_connection(("127.0.0.1", port), timeout=1):
                return True
        except (socket.error, ConnectionRefusedError, OSError):
            time.sleep(0.2)
    return False

def ensure_webview2_installed():
    cmd = r'reg query "HKLM\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}" /v pv'
    try:
        res = subprocess.run(cmd, shell=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
        if res.returncode == 0:
            return
    except Exception:
        pass

    try:
        bootstrapper_url = "https://go.microsoft.com/fwlink/p/?LinkId=2124703"
        temp_installer = os.path.join(os.environ.get("TEMP", EXECUTIVE_DIR), "MicrosoftEdgeWebview2Setup.exe")
        urllib.request.urlretrieve(bootstrapper_url, temp_installer)
        subprocess.run([temp_installer, "/silent", "/install"], check=True)
    except Exception as e:
        print(f"Hinweis: WebView2 Auto-Install fehlgeschlagen oder keine Admin-Rechte: {e}")

def start_fastapi_server(port):
    uvicorn.run(app, host="127.0.0.1", port=port, log_level="warning")

if __name__ == "__main__":
    from dotenv import load_dotenv
    
    env_path = os.path.join(EXECUTIVE_DIR, ".env")
    if os.path.exists(env_path):
        load_dotenv(env_path)

    SERVER_PORT = find_free_port()

    server_thread = threading.Thread(target=start_fastapi_server, args=(SERVER_PORT,), daemon=True)
    server_thread.start()

    if not wait_for_server(SERVER_PORT):
        print("Fehler: Localhost Server konnte nicht gestartet werden.")
        sys.exit(1)

    ensure_webview2_installed()

    webview.create_window(
        title="E-Mail Zuordnung Manager", 
        url=f"http://127.0.0.1:{SERVER_PORT}",
        width=1000,
        height=700
    )
    
    try:
        webview.start(gui='edgechromium')
    except Exception as e1:
        try:
            webview.start(gui='winforms')
        except Exception as e2:
            webview.start(gui='mshtml')

    sys.exit(0)
    