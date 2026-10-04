namespace email_csharp
{
    using System;
    using System.IO;
    using System.Threading.Tasks;
    using Serilog;

    class Program
    {
        static async Task Main(string[] args)
        {
            // Absoluten Pfad erzwingen und direkt auf der Konsole ausgeben
            string basisOrdner = AppDomain.CurrentDomain.BaseDirectory;
            string logPfad = Path.Combine(basisOrdner, "logs", "downloader.log");

            Console.WriteLine($"[DEBUG] Versuche Log-Datei zu speichern unter: {logPfad}");

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
                .WriteTo.File(
                    logPfad,
                    fileSizeLimitBytes: 1_000_000,
                    rollOnFileSizeLimit: true,
                    retainedFileCountLimit: 2,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                .CreateLogger();

            Log.Information("=== E-Mail Downloader gestartet ===");

            try
            {
                // Konfiguration aus der config.json laden
                var config = AppConfig.Laden();

                if (!config.ZugangsdatenVollstaendig(out string fehler))
                {
                    Log.Error("Fehler: {Fehler}", fehler);
                    Console.WriteLine($"Fehler: {fehler}");
                    return;
                }

                string modus = config.UseOAuth2 ? "OAuth2" : "Passwort";
                Log.Information("Verbinde mit {Server} für Konto {Konto} (Anmeldung: {Modus})...", config.ImapServer, config.EmailKonto, modus);
                Console.WriteLine($"Verbinde mit {config.ImapServer} für Konto {config.EmailKonto} (Anmeldung: {modus})...");
                Console.WriteLine($"Zielordner: {config.ZielOrdner}");
                Console.WriteLine(new string('-', 50));

                var emailService = new EmailService();

                int port = config.ImapPort > 0 ? config.ImapPort : 993;
                // E-Mails abrufen
                var neueEmails = await emailService.FetchNewEmailsAsync(config.ImapServer, port, config.EmailKonto, config.Passwort, config.ZielOrdner);

                Log.Information("Prozess erfolgreich abgeschlossen. {Count} neue E-Mails verarbeitet.", neueEmails.Count);
                Console.WriteLine($"\n[Fertig] Prozess abgeschlossen. {neueEmails.Count} neue E-Mails verarbeitet.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Ein schwerwiegender Fehler ist aufgetreten: {Message}", ex.Message);
                Console.WriteLine($"\n[Fehler aufgetreten]: {ex.Message}");
            }
            finally
            {
                Log.Information("=== E-Mail Downloader beendet ===");
                Log.CloseAndFlush(); // Wichtig, damit alle Logs sauber in die Datei geschrieben werden
            }

            Console.WriteLine("\nDrücke eine beliebige Taste zum Beenden...");
            Console.ReadKey();
        }
    }
}
