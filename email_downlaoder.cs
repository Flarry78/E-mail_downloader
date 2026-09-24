namespace email_csharp 
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Microsoft.Data.Sqlite;
    using MailKit;
    using MailKit.Net.Imap;
    using MailKit.Search;
    using MimeKit;

    public class EmailService
    {
        private readonly string _jsonCachePath;
        private readonly string _dbPath;
        private HashSet<string> _downloadedIds = new HashSet<string>();

        public EmailService()
        {
            string basisOrdner = AppDomain.CurrentDomain.BaseDirectory;
            _jsonCachePath = Path.Combine(basisOrdner, "downloaded_emails.json");
            _dbPath = Path.Combine(basisOrdner, "rules.db");

            LoadCache();
            InitDatabase();
        }



        private void InitDatabase()
        {
            using (var connection = new SqliteConnection($"Data Source={_dbPath}"))
            {
                connection.Open();
                string createTableQuery = @"
                CREATE TABLE IF NOT EXISTS regeln (
                    kennnummer INTEGER PRIMARY KEY AUTOINCREMENT,
                    email_adresse TEXT NOT NULL,
                    absender_name TEXT NOT NULL,
                    firma_ordner TEXT,
                    UNIQUE(email_adresse, absender_name)
                );";
                using (var command = new SqliteCommand(createTableQuery, connection))
                {
                    command.ExecuteNonQuery();
                }
            }
            Console.WriteLine("[Datenbank] SQLite-Datenbank 'rules.db' initialisiert.");
        }

        private void ErfasseOderPruefeEmail(string emailAdresse, string absenderName)
        {
            string emailClean = emailAdresse.ToLower().Trim();
            string nameClean = string.IsNullOrWhiteSpace(absenderName) ? emailClean : absenderName.Trim();

            using (var connection = new SqliteConnection($"Data Source={_dbPath}"))
            {
                connection.Open();

                // Nur prüfen, ob es schon da ist (Abgleich), und falls neu, in die DB eintragen (firma_ordner = NULL)
                string insertQuery = @"
                INSERT INTO regeln (email_adresse, absender_name, firma_ordner)
                VALUES (@email, @name, NULL)
                ON CONFLICT(email_adresse, absender_name) DO NOTHING;";

                using (var insertCmd = new SqliteCommand(insertQuery, connection))
                {
                    insertCmd.Parameters.AddWithValue("@email", emailClean);
                    insertCmd.Parameters.AddWithValue("@name", nameClean);
                    insertCmd.ExecuteNonQuery();
                }
            }
        }

        private void LoadCache()
        {
            if (File.Exists(_jsonCachePath))
            {
                try
                {
                    string jsonString = File.ReadAllText(_jsonCachePath);
                    var list = JsonSerializer.Deserialize<List<string>>(jsonString);
                    if (list != null)
                    {
                        _downloadedIds = new HashSet<string>(list);
                    }
                    Console.WriteLine($"[Cache] {_downloadedIds.Count} bereits bekannte E-Mail-IDs aus JSON geladen.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Cache Fehler] Konnte JSON nicht laden: {ex.Message}");
                }
            }
        }

        private void SaveCache()
        {
            try
            {
                string jsonString = JsonSerializer.Serialize(_downloadedIds);
                File.WriteAllText(_jsonCachePath, jsonString);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Cache Fehler] Konnte JSON nicht speichern: {ex.Message}");
            }
        }

        private string GenerateShortHash(string input)
        {
            using (var sha256 = SHA256.Create())
            {
                byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < 4; i++)
                {
                    sb.Append(bytes[i].ToString("x2"));
                }
                return sb.ToString();
            }
        }

        public async Task<List<EmailPreviewModel>> FetchNewEmailsAsync(string imapServer, int port, string email, string appPassword, string zielHauptOrdner)
        {
            var newEmails = new List<EmailPreviewModel>();

            int.TryParse(Environment.GetEnvironmentVariable("TAGE_ZURUECK"), out int tageZurueck);
            if (tageZurueck <= 0) tageZurueck = 7;

            bool nurUngelesene = true;
            if (bool.TryParse(Environment.GetEnvironmentVariable("NUR_UNGELESENE"), out bool parsedBool))
            {
                nurUngelesene = parsedBool;
            }

            int.TryParse(Environment.GetEnvironmentVariable("DELAY_MILLISEKUNDEN"), out int delayMs);
            if (delayMs <= 0) delayMs = 1000;

            Directory.CreateDirectory(zielHauptOrdner);
            string unsortedOrdnerPfad = Path.Combine(zielHauptOrdner, "unsorted");
            Directory.CreateDirectory(unsortedOrdnerPfad);

            using (var client = new ImapClient())
            {
                try
                {
                    Console.WriteLine($"\n[IMAP] Verbinde zu {imapServer}:{port}...");
                    await client.ConnectAsync(imapServer, port, true);
                    await client.AuthenticateAsync(email, appPassword);
                    var inbox = client.Inbox;
                    await inbox.OpenAsync(FolderAccess.ReadWrite);
                    Console.WriteLine("[IMAP] Posteingang erfolgreich geöffnet.");

                    var query = SearchQuery.All;
                    if (nurUngelesene) query = query.And(SearchQuery.NotSeen);
                    if (tageZurueck > 0)
                    {
                        query = query.And(SearchQuery.SentSince(DateTime.Now.AddDays(-tageZurueck)));
                    }

                    var uids = await inbox.SearchAsync(query);
                    Console.WriteLine($"[Suche] Server hat {uids.Count} passende E-Mails gemeldet.");

                    int counter = 1;
                    foreach (var uid in uids)
                    {
                        string uniqueId = uid.Id.ToString();

                        if (!_downloadedIds.Contains(uniqueId))
                        {
                            Console.WriteLine($"\n--- Verarbeite E-Mail {counter} von {uids.Count} (ID: {uniqueId}) ---");
                            var message = await inbox.GetMessageAsync(uid);

                            var mailbox = message.From.Mailboxes.FirstOrDefault();
                            string absenderName = mailbox?.Name ?? message.From.ToString();
                            string absenderEmail = mailbox?.Address ?? message.From.ToString();
                            string betreff = message.Subject ?? "Kein Betreff";
                            DateTime datum = message.Date.DateTime;

                            // Absender in die DB eintragen / prüfen (ohne automatische Sortierung)
                            ErfasseOderPruefeEmail(absenderEmail, absenderName);

                            string datumString = datum.ToString("yyyy-MM-dd_HHmmss");
                            string idHash = GenerateShortHash(uniqueId);
                            string ordnerName = $"{datumString}_{idHash}";

                            // WICHTIG: Jede E-Mail landet JETZT IMMER im "unsorted"-Ordner
                            string zielOrdnerPfad = Path.Combine(unsortedOrdnerPfad, ordnerName);
                            Directory.CreateDirectory(zielOrdnerPfad);
                            Console.WriteLine($"  [Info] Abgelegt unter unsorted/: {absenderName}");

                            // 1. Die .eml-Datei speichern
                            string emlPfad = Path.Combine(zielOrdnerPfad, "email.eml");
                            await message.WriteToAsync(emlPfad);

                            // 2. Anhänge filtern und speichern (Nur echte Anhänge)
                            foreach (var attachment in message.Attachments)
                            {
                                if (attachment is MimePart mimePart)
                                {
                                    var disposition = mimePart.ContentDisposition?.Disposition;
                                    if (disposition != null && disposition.Equals("attachment", StringComparison.OrdinalIgnoreCase))
                                    {
                                        string originalFileName = mimePart.FileName ?? "unbenannt.dat";
                                        string anhangPfad = Path.Combine(zielOrdnerPfad, originalFileName);

                                        using (var memoryStream = new MemoryStream())
                                        {
                                            await mimePart.Content.DecodeToAsync(memoryStream);
                                            await File.WriteAllBytesAsync(anhangPfad, memoryStream.ToArray());
                                        }
                                        Console.WriteLine($"    [Anhang Gespeichert] -> {originalFileName}");
                                    }
                                }
                            }

                            newEmails.Add(new EmailPreviewModel
                            {
                                UniqueId = uniqueId,
                                Sender = message.From.ToString(),
                                Subject = betreff,
                                Date = datum,
                                BodySnippet = message.TextBody ?? message.HtmlBody ?? "[Kein Textinhalt]"
                            });

                            _downloadedIds.Add(uniqueId);

                            if (delayMs > 0)
                            {
                                await Task.Delay(delayMs);
                            }
                        }
                        counter++;
                    }

                    if (newEmails.Count > 0)
                    {
                        SaveCache();
                    }

                    await client.DisconnectAsync(true);
                }
                catch (Exception ex)
                {
                    throw new Exception($"IMAP-Fehler: {ex.Message}");
                }
            }

            return newEmails;
        }
    }

    public class EmailPreviewModel
    {
        public string UniqueId { get; set; }
        public string Sender { get; set; }
        public string Subject { get; set; }
        public DateTime Date { get; set; }
        public string BodySnippet { get; set; }
    }
}