namespace email_csharp
{
    using System;
    using System.IO;
    using Serilog;

    public static class LogInitialisierung
    {
        private static bool _isInitialized = false;

        public static void InitLogger()
        {
            if (_isInitialized) return;

            string basisOrdner = AppDomain.CurrentDomain.BaseDirectory;
            string logOrdner = Path.Combine(basisOrdner, "logs");

            // Ordner erzwingen, falls nicht vorhanden
            if (!Directory.Exists(logOrdner))
            {
                Directory.CreateDirectory(logOrdner);
            }

            string logPfad = Path.Combine(logOrdner, "downloader.log");

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
                .WriteTo.File(
                    logPfad,
                    fileSizeLimitBytes: 1_000_000, // 1 MB Limit pro Datei
                    rollOnFileSizeLimit: true,     // Ersetzt/Rollt die Datei bei Größe X
                    retainedFileCountLimit: 3,     // Behält maximal 3 Log-Dateien
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}",
                    shared: true)                  // Erlaubt gleichzeitigen Zugriff
                .CreateLogger();

            _isInitialized = true;
            Log.Information("=== Zentrale Log-Initialisierung erfolgreich ===");
        }
    }
}