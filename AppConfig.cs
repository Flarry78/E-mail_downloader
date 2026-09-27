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

        [JsonPropertyName("ImapServer")]
        public string ImapServer { get; set; }

        [JsonPropertyName("ImapPort")]
        public int ImapPort { get; set; } = 993; // Standard-Port für SSL direkt ergänzt

        [JsonPropertyName("EmailKonto")]
        public string EmailKonto { get; set; }

        [JsonPropertyName("Passwort")]
        public string Passwort { get; set; }

        [JsonPropertyName("TAGE_ZURUECK")]
        public int TAGE_ZURUECK { get; set; } = 7;

        [JsonPropertyName("NUR_UNGELESENE")]
        public bool NUR_UNGELESENE { get; set; } = false;

        [JsonPropertyName("DELAY_MILLISEKUNDEN")]
        public int DELAY_MILLISEKUNDEN { get; set; } = 1000;

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