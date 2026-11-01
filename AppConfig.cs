namespace email_csharp
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    public class AppConfig
    {
        /// <summary>Standard-Whitelist: PDF, gängige Bildformate, reine Textdateien und gängige CAD-Formate.</summary>
        public static readonly string[] StandardAnhangEndungen =
        {
            "pdf", "jpg", "jpeg", "png", "gif", "bmp", "tif", "tiff", "webp", "txt",
            "dwg", "dxf", "dwf", "step", "stp", "iges", "igs", "sat", "stl"
        };

        /// <summary>
        /// Diese Endungen werden IMMER blockiert, selbst wenn jemand sie in die Whitelist einträgt.
        /// </summary>
        private static readonly HashSet<string> GesperrteEndungen = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "exe", "com", "bat", "cmd", "scr", "pif", "msi", "msp", "dll", "sys",
            "js", "jse", "vbs", "vbe", "wsf", "wsh", "ps1", "psm1", "hta", "jar", "lnk", "reg", "cpl",
            "zip", "rar", "7z", "gz", "tar", "cab", "iso", "img",
            "docm", "xlsm", "pptm", "dotm", "xlam"
        };

        [JsonPropertyName("ZielOrdner")]
        public string ZielOrdner { get; set; }

        private string _unsortedOrdner;

        [JsonPropertyName("UnsortedOrdner")]
        public string UnsortedOrdner
        {
            get
            {
                if (string.IsNullOrWhiteSpace(_unsortedOrdner))
                {
                    return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "unsorted");
                }

                // Wenn der Pfad nicht ohnehin schon auf "unsorted" endet, hängen wir es dran
                if (!_unsortedOrdner.EndsWith("unsorted", StringComparison.OrdinalIgnoreCase))
                {
                    return Path.Combine(_unsortedOrdner, "unsorted");
                }

                return _unsortedOrdner;
            }
            set
            {
                _unsortedOrdner = value;
            }
        }

        // Ordner für E-Mails, die weder einsortiert noch gelöscht/blockiert werden sollen
        [JsonPropertyName("ArchivOrdner")]
        public string ArchivOrdner { get; set; }

        // --- IMAP (z. B. Yahoo) ---------------------------------------------------------
        [JsonPropertyName("ImapServer")]
        public string ImapServer { get; set; }

        [JsonPropertyName("ImapPort")]
        public int ImapPort { get; set; } = 993; // Standard-Port für SSL

        [JsonPropertyName("EmailKonto")]
        public string EmailKonto { get; set; }

        /// <summary>
        /// Das Passwort liegt in der config.json nur verschlüsselt (Windows DPAPI, nur für dieses
        /// Windows-Benutzerkonto auf diesem Rechner lesbar).
        /// </summary>
        [JsonPropertyName("PasswortGeschuetzt")]
        public string PasswortGeschuetzt { get; set; }

        [JsonIgnore]
        public string Passwort
        {
            get
            {
                if (string.IsNullOrEmpty(PasswortGeschuetzt)) return string.Empty;
                try
                {
                    byte[] daten = ProtectedData.Unprotect(
                        Convert.FromBase64String(PasswortGeschuetzt), null, DataProtectionScope.CurrentUser);
                    return Encoding.UTF8.GetString(daten);
                }
                catch
                {
                    // Config wurde auf einem anderen Rechner/Benutzer erstellt -> neu eingeben
                    return string.Empty;
                }
            }
            set
            {
                if (string.IsNullOrEmpty(value))
                {
                    PasswortGeschuetzt = null;
                    return;
                }

                byte[] geschuetzt = ProtectedData.Protect(
                    Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser);
                PasswortGeschuetzt = Convert.ToBase64String(geschuetzt);
            }
        }

        // Alte config.json mit Klartext-Passwort: wird beim Laden eingelesen und beim nächsten
        // Speichern nur noch verschlüsselt geschrieben (wird nie selbst serialisiert).
        [JsonPropertyName("Passwort")]
        public string PasswortAlt
        {
            set
            {
                if (!string.IsNullOrEmpty(value)) Passwort = value;
            }
        }

        // --- Anmeldeart -----------------------------------------------------------------
        public const string ModusImapPasswort = "ImapPasswort"; // IMAP mit Passwort / App-Key (z. B. Yahoo)
        public const string ModusImapOAuth2 = "ImapOAuth2";     // IMAP mit OAuth2 (Microsoft, IMAP muss freigeschaltet sein)
        public const string ModusGraph = "Graph";               // Microsoft Graph (nur lesend, kein IMAP nötig)

        [JsonPropertyName("Anmeldeart")]
        public string Anmeldeart { get; set; } = ModusImapPasswort;

        [JsonIgnore]
        public bool IstGraph => string.Equals(Anmeldeart, ModusGraph, StringComparison.OrdinalIgnoreCase);

        [JsonIgnore]
        public bool IstImapOAuth2 => string.Equals(Anmeldeart, ModusImapOAuth2, StringComparison.OrdinalIgnoreCase);

        [JsonIgnore]
        public bool IstOAuth => IstGraph || IstImapOAuth2;

        // Alte config.json-Dateien mit "UseOAuth2": true / "UseGraph": true werden automatisch übernommen.
        [JsonPropertyName("UseOAuth2")]
        public bool UseOAuth2Alt
        {
            set
            {
                if (value && !IstGraph) Anmeldeart = ModusImapOAuth2;
            }
        }

        [JsonPropertyName("UseGraph")]
        public bool UseGraphAlt
        {
            set
            {
                if (value) Anmeldeart = ModusGraph;
            }
        }

        [JsonPropertyName("OAuthClientId")]
        public string OAuthClientId { get; set; }

        // "common" = Geschäfts-/Schulkonten und private Konten, sonst die konkrete Verzeichnis-(Mandanten-)ID
        [JsonPropertyName("OAuthTenantId")]
        public string OAuthTenantId { get; set; } = "common";

        // --- Anhänge --------------------------------------------------------------------
        [JsonPropertyName("ErlaubteAnhangEndungen")]
        public List<string> ErlaubteAnhangEndungen { get; set; } = new List<string>(StandardAnhangEndungen);

        [JsonPropertyName("TAGE_ZURUECK")]
        public int TAGE_ZURUECK { get; set; } = 7;

        [JsonPropertyName("NUR_UNGELESENE")]
        public bool NUR_UNGELESENE { get; set; } = false;

        [JsonPropertyName("DELAY_MILLISEKUNDEN")]
        public int DELAY_MILLISEKUNDEN { get; set; } = 1000;

        /// <summary>
        /// Prüft, ob ein Anhang gespeichert werden darf. Nur Endungen aus der Whitelist,
        /// und niemals ausführbare Dateien oder Archive.
        /// </summary>
        public bool IstAnhangErlaubt(string dateiname)
        {
            if (string.IsNullOrWhiteSpace(dateiname)) return false;

            string endung = Path.GetExtension(dateiname.Trim()).TrimStart('.').ToLowerInvariant();
            if (string.IsNullOrEmpty(endung)) return false;
            if (GesperrteEndungen.Contains(endung)) return false;

            var erlaubt = (ErlaubteAnhangEndungen != null && ErlaubteAnhangEndungen.Count > 0)
                ? ErlaubteAnhangEndungen
                : new List<string>(StandardAnhangEndungen);

            return erlaubt.Any(e => string.Equals(e?.Trim().TrimStart('.'), endung, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Prüft, ob die Zugangsdaten für die gewählte Anmeldeart vollständig sind.
        /// </summary>
        public bool ZugangsdatenVollstaendig(out string fehler)
        {
            fehler = null;

            if (IstGraph)
            {
                if (string.IsNullOrWhiteSpace(OAuthClientId))
                {
                    fehler = "Für Microsoft Graph fehlt die Client-ID.";
                    return false;
                }
                return true;
            }

            if (string.IsNullOrWhiteSpace(ImapServer) || string.IsNullOrWhiteSpace(EmailKonto))
            {
                fehler = "IMAP-Server oder E-Mail-Konto fehlen.";
                return false;
            }

            if (IstImapOAuth2)
            {
                if (string.IsNullOrWhiteSpace(OAuthClientId))
                {
                    fehler = "Für IMAP mit OAuth2 fehlt die Client-ID.";
                    return false;
                }
                return true;
            }

            if (string.IsNullOrWhiteSpace(Passwort))
            {
                fehler = "Das Passwort / der App-Key fehlt (oder wurde auf einem anderen Rechner gespeichert und muss neu eingegeben werden).";
                return false;
            }

            return true;
        }

        public static AppConfig Laden()
        {
            string configPfad = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");

            if (File.Exists(configPfad))
            {
                try
                {
                    string jsonString = File.ReadAllText(configPfad);
                    var config = JsonSerializer.Deserialize<AppConfig>(jsonString);
                    if (config != null)
                    {
                        return config;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Config Fehler] Konnte config.json nicht lesen: {ex.Message}");
                }
            }

            return new AppConfig();
        }

        // Methode zum Speichern der Einstellungen
        public void Speichern()
        {
            try
            {
                string configPfad = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
                var options = new JsonSerializerOptions { WriteIndented = true };
                string jsonString = JsonSerializer.Serialize(this, options);
                File.WriteAllText(configPfad, jsonString);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Config Fehler] Konnte config.json nicht speichern: {ex.Message}");
            }
        }
    }
}