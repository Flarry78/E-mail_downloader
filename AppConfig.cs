namespace email_csharp
{
    using System;
    using System.IO;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    public class AppConfig
    {
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

        [JsonPropertyName("ImapServer")]
        public string ImapServer { get; set; }

        [JsonPropertyName("ImapPort")]
        public int ImapPort { get; set; } = 993; // Standard-Port für SSL

        [JsonPropertyName("EmailKonto")]
        public string EmailKonto { get; set; }

        [JsonPropertyName("Passwort")]
        public string Passwort { get; set; }

        // NEU: OAuth2 (Microsoft / Azure Entra ID)
        [JsonPropertyName("UseOAuth2")]
        public bool UseOAuth2 { get; set; } = false;

        [JsonPropertyName("OAuthClientId")]
        public string OAuthClientId { get; set; }

        // "common" = Geschäfts-/Schulkonten und private Konten, sonst die konkrete Verzeichnis-(Mandanten-)ID
        [JsonPropertyName("OAuthTenantId")]
        public string OAuthTenantId { get; set; } = "common";

        [JsonPropertyName("TAGE_ZURUECK")]
        public int TAGE_ZURUECK { get; set; } = 7;

        [JsonPropertyName("NUR_UNGELESENE")]
        public bool NUR_UNGELESENE { get; set; } = false;

        [JsonPropertyName("DELAY_MILLISEKUNDEN")]
        public int DELAY_MILLISEKUNDEN { get; set; } = 1000;

        /// <summary>
        /// Prüft, ob die Zugangsdaten für den gewählten Modus (Passwort oder OAuth2) vollständig sind.
        /// </summary>
        public bool ZugangsdatenVollstaendig(out string fehler)
        {
            fehler = null;

            if (string.IsNullOrWhiteSpace(ImapServer) || string.IsNullOrWhiteSpace(EmailKonto))
            {
                fehler = "IMAP-Server oder E-Mail-Konto fehlen in der config.json.";
                return false;
            }

            if (UseOAuth2)
            {
                if (string.IsNullOrWhiteSpace(OAuthClientId))
                {
                    fehler = "OAuth2 ist aktiv, aber die Client-ID fehlt in der config.json.";
                    return false;
                }
            }
            else if (string.IsNullOrWhiteSpace(Passwort))
            {
                fehler = "Das Passwort / der App-Key fehlt in der config.json.";
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
