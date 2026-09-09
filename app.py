import os
import sys
import sqlite3
import threading
import uvicorn
import webview
from fastapi import FastAPI, Request, Form
from fastapi.responses import RedirectResponse
from fastapi.templating import Jinja2Templates

# --- Absolute Pfade auflösen (Funktioniert auch als gebündelte .exe) ---
if getattr(sys, 'frozen', False):
    BASE_DIR = sys._MEIPASS
    EXECUTIVE_DIR = os.path.dirname(sys.executable)
else:
    BASE_DIR = os.path.dirname(os.path.abspath(__file__))
    EXECUTIVE_DIR = BASE_DIR

TEMPLATES_DIR = os.path.join(BASE_DIR, "templates")

# Importiere Konfigurationen & Datenbank-Methoden
from download_script import DB_PFAD, ZIEL_ORDNER, init_db, sortiere_unsorted_nach

app = FastAPI(title="E-Mail Absender Zuordnungen")

# Datenbank initialisieren
init_db()

templates = Jinja2Templates(directory=TEMPLATES_DIR)

def get_db_connection():
    conn = sqlite3.connect(DB_PFAD)
    conn.row_factory = sqlite3.Row
    return conn

@app.get("/")
def index(request: Request):
    conn = get_db_connection()
    cursor = conn.cursor()
    
    # 1. Unzugeordnete Absender holen
    cursor.execute("""
        SELECT kennnummer, email_adresse, absender_name, firma_ordner 
        FROM regeln 
        WHERE firma_ordner IS NULL OR firma_ordner = ''
        ORDER BY absender_name ASC
    """)
    offene_absender = cursor.fetchall()
    conn.close()

    # 2. Ordner aus dem Zielverzeichnis (Dateisystem) laden
    existierende_firmen = []
    if os.path.exists(ZIEL_ORDNER):
        for eintrag in os.listdir(ZIEL_ORDNER):
            vollstaendiger_pfad = os.path.join(ZIEL_ORDNER, eintrag)
            if os.path.isdir(vollstaendiger_pfad) and eintrag.lower() != "unsorted":
                existierende_firmen.append(eintrag)
    
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

    return RedirectResponse(url="/", status_code=303)


# --- Hilfsfunktionen für den Exe-Start ---
def start_fastapi():
    """ Startet den FastAPI-Server im Hintergrund. """
    uvicorn.run(app, host="127.0.0.1", port=8000, log_level="warning")


if __name__ == "__main__":
    # Server in einem separaten Thread starten
    server_thread = threading.Thread(target=start_fastapi, daemon=True)
    server_thread.start()

    # Eigenes Anwendungsfenster öffnen
    webview.create_window(
        title="E-Mail Zuordnung Manager", 
        url="http://127.0.0.1:8000",
        width=1000,
        height=700
    )
    webview.start()
    
    # Sobald das Fenster geschlossen wird, beendet Python das komplette Skript
    sys.exit(0)