namespace email_csharp
{
    using System;
    using System.IO;
    using System.Threading.Tasks;
    using DotNetEnv;
    using Serilog;

    class Program
    {
        static async Task Main(string[] args)
        {
            // 1. Serilog Logger konfigurieren (Rotation bei 1 MB, max. 2 Backups im Ordner "logs")
            string logPfad = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", "downloader.log");

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
                .WriteTo.File(
                    logPfad,
                    fileSizeLimitBytes: 1_000_000, // 1 MB Limit
                    rollOnFileSizeLimit: true,     // Bei Erreichen rotieren
                    retainedFileCountLimit: 2,     // Max. 2 alte Log-Dateien behalten
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                .CreateLogger();

            Log.Information("=== E-Mail Downloader gestartet ===");

            try
            {
                // .env-Pfad laden
                Env.Load();

                string imapServer = Environment.GetEnvironmentVariable("IMAP_SERVER");
                string emailKonto = Environment.GetEnvironmentVariable("EMAIL_KONTO");
                string passwort = Environment.GetEnvironmentVariable("PASSWORT");
                string zielOrdner = Environment.GetEnvironmentVariable("ZIEL_ORDNER");

                if (string.IsNullOrEmpty(imapServer) || string.IsNullOrEmpty(emailKonto) || string.IsNullOrEmpty(passwort))
                {
                    Log.Error("Fehler: Bitte überprüfe deine .env-Datei. Es fehlen Zugangsdaten!");
                    Console.WriteLine("Fehler: Bitte überprüfe deine .env-Datei. Es fehlen Zugangsdaten!");
                    return;
                }

                Log.Information("Verbinde mit {Server} für Konto {Konto} mit Zielordner {Ordner}...", imapServer, emailKonto, zielOrdner);
                Console.WriteLine($"Verbinde mit {imapServer} für Konto {emailKonto}...");
                Console.WriteLine($"Zielordner: {zielOrdner}");
                Console.WriteLine(new string('-', 50));

                var emailService = new EmailService();

                int port = 993;
                // E-Mails abrufen (übergibt nun auch den zielOrdner an den Service)
                var neueEmails = await emailService.FetchNewEmailsAsync(imapServer, port, emailKonto, passwort, zielOrdner);

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