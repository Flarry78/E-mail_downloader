namespace email_csharp
{

    using System;
    using System.Threading.Tasks;
    using DotNetEnv;

    class Program
    {
        // Das Main muss hier "async Task" sein, damit wir "await" nutzen können
        static async Task Main(string[] args)
        {
            // .env-Pfad direkt über BaseDirectory absolut bestimmen
            string envPfad = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".env");

            // Falls die .env während der Entwicklung im Hauptordner liegt:
            // (Alternativ einfach Env.Load() nutzen, wenn sie im Ausgabeordner liegt)
            Env.Load();

            string imapServer = Environment.GetEnvironmentVariable("IMAP_SERVER");
            string emailKonto = Environment.GetEnvironmentVariable("EMAIL_KONTO");
            string passwort = Environment.GetEnvironmentVariable("PASSWORT");
            string zielOrdner = Environment.GetEnvironmentVariable("ZIEL_ORDNER");

            if (string.IsNullOrEmpty(imapServer) || string.IsNullOrEmpty(emailKonto) || string.IsNullOrEmpty(passwort))
            {
                Console.WriteLine("Fehler: Bitte überprüfe deine .env-Datei. Es fehlen Zugangsdaten!");
                return;
            }

            Console.WriteLine($"Verbinde mit {imapServer} für Konto {emailKonto}...");
            Console.WriteLine($"Zielordner: {zielOrdner}");
            Console.WriteLine(new string('-', 50));


            var emailService = new EmailService();

            try
            {
                int port = 993;

                // Übergabe des zielOrdners als letzter Parameter:
                var neueEmails = await emailService.FetchNewEmailsAsync(imapServer, port, emailKonto, passwort, zielOrdner);

                Console.WriteLine($"\n[Fertig] Prozess abgeschlossen. {neueEmails.Count} neue E-Mails verarbeitet.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n[Fehler aufgetreten]: {ex.Message}");
            }

        }
    }





}