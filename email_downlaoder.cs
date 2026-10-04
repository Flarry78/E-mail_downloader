namespace email_csharp
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Microsoft.Data.Sqlite;
    using MailKit;
    using MailKit.Net.Imap;
    using MailKit.Search;
    using MailKit.Security;
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

                string createRulesTable = @"
                CREATE TABLE IF NOT EXISTS regeln (
                    kennnummer INTEGER PRIMARY KEY AUTOINCREMENT,
                    email_adresse TEXT NOT NULL,
                    absender_name TEXT NOT NULL,
                    firma_ordner TEXT,
                    ist_blockiert INTEGER DEFAULT 0,
                    UNIQUE(email_adresse, absender_name)
                );";

                string createUnsortedTable = @"
                CREATE TABLE IF NOT EXISTS unsorted_emails (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    unique_id TEXT UNIQUE,
                    sender TEXT,
                    sender_email TEXT,
                    subject TEXT,
                    date TEXT,
                    file_path TEXT
                );";

                using (var command = new SqliteCommand($"{createRulesTable} {createUnsortedTable}", connection))
                {
                    command.ExecuteNonQuery();
                }
            }
            Console.WriteLine("[Datenbank] SQLite-Tabellen initialisiert.");
        }

        private void ErfasseOderPruefeEmail(string emailAdresse, string absenderName)
        {
            string emailClean = emailAdresse.ToLower().Trim();
            string nameClean = string.IsNullOrWhiteSpace(absenderName) ? emailClean : absenderName.Trim();

            using (var connection = new SqliteConnection($"Data Source={_dbPath}"))
            {
                connection.Open();

                string insertQuery = @"
                INSERT INTO regeln (email_adresse, absender_name, firma_ordner, ist_blockiert)
                VALUES (@email, @name, NULL, 0)
                ON CONFLICT(email_adresse, absender_name) DO NOTHING;";

                using (var insertCmd = new SqliteCommand(insertQuery, connection))
                {
                    insertCmd.Parameters.AddWithValue("@email", emailClean);
                    insertCmd.Parameters.AddWithValue("@name", nameClean);
                    insertCmd.ExecuteNonQuery();
                }
            }
        }

        public bool IstAbsenderBlockiert(string senderEmail)
        {
            if (string.IsNullOrWhiteSpace(senderEmail)) return false;

            using (var connection = new SqliteConnection($"Data Source={_dbPath}"))
            {
                connection.Open();
                string query = "SELECT ist_blockiert FROM regeln WHERE email_adresse = @email AND ist_blockiert = 1 LIMIT 1;";
                using (var cmd = new SqliteCommand(query, connection))
                {
                    cmd.Parameters.AddWithValue("@email", senderEmail.ToLower().Trim());
                    var result = cmd.ExecuteScalar();
                    return result != null && result != DBNull.Value && Convert.ToInt32(result) == 1;
                }
            }
        }

        public void AbsenderBlockieren(string senderEmail, string senderName)
        {
            if (string.IsNullOrWhiteSpace(senderEmail)) return;

            string emailClean = senderEmail.ToLower().Trim();
            string nameClean = string.IsNullOrWhiteSpace(senderName) ? emailClean : senderName.Trim();

            using (var connection = new SqliteConnection($"Data Source={_dbPath}"))
            {
                connection.Open();
                string query = @"
                INSERT INTO regeln (email_adresse, absender_name, firma_ordner, ist_blockiert)
                VALUES (@email, @name, NULL, 1)
                ON CONFLICT(email_adresse, absender_name) DO UPDATE SET ist_blockiert = 1;";

                using (var cmd = new SqliteCommand(query, connection))
                {
                    cmd.Parameters.AddWithValue("@email", emailClean);
                    cmd.Parameters.AddWithValue("@name", nameClean);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public List<BlockedSenderModel> HoleBlockierteAbsender()
        {
            var list = new List<BlockedSenderModel>();
            using (var connection = new SqliteConnection($"Data Source={_dbPath}"))
            {
                connection.Open();
                string query = "SELECT email_adresse, absender_name FROM regeln WHERE ist_blockiert = 1;";
                using (var cmd = new SqliteCommand(query, connection))
                {
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            list.Add(new BlockedSenderModel
                            {
                                Email = reader.GetString(0),
                                Name = reader.IsDBNull(1) ? "" : reader.GetString(1)
                            });
                        }
                    }
                }
            }
            return list;
        }

        public void AbsenderEntsperren(string senderEmail)
        {
            if (string.IsNullOrWhiteSpace(senderEmail)) return;

            using (var connection = new SqliteConnection($"Data Source={_dbPath}"))
            {
                connection.Open();
                string query = "UPDATE regeln SET ist_blockiert = 0 WHERE email_adresse = @email;";
                using (var cmd = new SqliteCommand(query, connection))
                {
                    cmd.Parameters.AddWithValue("@email", senderEmail.ToLower().Trim());
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public void LoescheUnsortedEintrag(int dbId)
        {
            using (var connection = new SqliteConnection($"Data Source={_dbPath}"))
            {
                connection.Open();
                string query = "DELETE FROM unsorted_emails WHERE id = @id;";
                using (var cmd = new SqliteCommand(query, connection))
                {
                    cmd.Parameters.AddWithValue("@id", dbId);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        private void SpeichereUnsortedEmailInDb(string uniqueId, string sender, string senderEmail, string subject, DateTime date, string ordnerPfad)
        {
            using (var connection = new SqliteConnection($"Data Source={_dbPath}"))
            {
                connection.Open();
                string query = @"
                INSERT OR REPLACE INTO unsorted_emails (unique_id, sender, sender_email, subject, date, file_path)
                VALUES (@uid, @sender, @email, @subject, @date, @path);";

                using (var cmd = new SqliteCommand(query, connection))
                {
                    cmd.Parameters.AddWithValue("@uid", uniqueId);
                    cmd.Parameters.AddWithValue("@sender", sender);
                    cmd.Parameters.AddWithValue("@email", senderEmail);
                    cmd.Parameters.AddWithValue("@subject", subject);
                    cmd.Parameters.AddWithValue("@date", date.ToString("o"));
                    cmd.Parameters.AddWithValue("@path", ordnerPfad);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public List<EmailPreviewModel> GetUnsortedEmailsFromDb()
        {
            var list = new List<EmailPreviewModel>();
            var idsToDelete = new List<int>();

            using (var connection = new SqliteConnection($"Data Source={_dbPath}"))
            {
                connection.Open();
                string query = "SELECT id, unique_id, sender, sender_email, subject, date, file_path FROM unsorted_emails ORDER BY date DESC;";

                using (var cmd = new SqliteCommand(query, connection))
                {
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            int id = reader.GetInt32(0);
                            string filePath = reader.GetString(6);

                            if (Directory.Exists(filePath))
                            {
                                // Anhänge direkt aus dem Ordner ermitteln (ohne email.eml)
                                var alleDateien = Directory.GetFiles(filePath);
                                var anhaenge = alleDateien
                                    .Select(Path.GetFileName)
                                    .Where(f => !f.Equals("email.eml", StringComparison.OrdinalIgnoreCase))
                                    .ToList();

                                list.Add(new EmailPreviewModel
                                {
                                    Id = id,
                                    UniqueId = reader.GetString(1),
                                    Sender = reader.GetString(2),
                                    SenderEmail = reader.IsDBNull(3) ? "" : reader.GetString(3),
                                    Subject = reader.GetString(4),
                                    Date = DateTime.Parse(reader.GetString(5)),
                                    FilePath = filePath,
                                    Attachments = anhaenge
                                });
                            }
                            else
                            {
                                idsToDelete.Add(id);
                            }
                        }
                    }
                }

                if (idsToDelete.Count > 0)
                {
                    foreach (var id in idsToDelete)
                    {
                        string deleteQuery = "DELETE FROM unsorted_emails WHERE id = @id;";
                        using (var deleteCmd = new SqliteCommand(deleteQuery, connection))
                        {
                            deleteCmd.Parameters.AddWithValue("@id", id);
                            deleteCmd.ExecuteNonQuery();
                        }
                    }
                }
            }
            return list;
        }

        public void OrdnerEinsortierenUndLoeschen(int dbId, string uniqueId, string aktuellerOrdnerPfad, string auftragsnummer, string firmenName)
        {
            try
            {
                var config = AppConfig.Laden();
                string zielHauptOrdner = config.ZielOrdner;

                if (string.IsNullOrWhiteSpace(zielHauptOrdner))
                {
                    throw new Exception("In der Konfiguration ist kein 'ZielOrdner' angegeben!");
                }

                if (!Directory.Exists(aktuellerOrdnerPfad))
                {
                    throw new DirectoryNotFoundException($"Der Quell-Ordner wurde nicht gefunden: {aktuellerOrdnerPfad}");
                }

                string saubererFirmenName = string.Join("_", firmenName.Split(Path.GetInvalidFileNameChars()));
                string saubereAuftragsnummer = string.Join("_", auftragsnummer.Split(Path.GetInvalidFileNameChars()));

                string hash = GenerateShortHash(uniqueId);

                string firmenUnterOrdnerPfad = Path.Combine(zielHauptOrdner, saubererFirmenName);
                Directory.CreateDirectory(firmenUnterOrdnerPfad);

                string neuerOrdnerName = $"{saubereAuftragsnummer} {saubererFirmenName} {hash}";
                string zielOrdnerPfad = Path.Combine(firmenUnterOrdnerPfad, neuerOrdnerName);

                if (Directory.Exists(zielOrdnerPfad))
                {
                    zielOrdnerPfad = Path.Combine(firmenUnterOrdnerPfad, $"{neuerOrdnerName}_{DateTime.Now:HHmmss}");
                }

                Directory.Move(aktuellerOrdnerPfad, zielOrdnerPfad);

                using (var connection = new SqliteConnection($"Data Source={_dbPath}"))
                {
                    connection.Open();

                    string senderEmailToUpdate = "";
                    string getSenderQuery = "SELECT sender_email FROM unsorted_emails WHERE id = @id;";
                    using (var getCmd = new SqliteCommand(getSenderQuery, connection))
                    {
                        getCmd.Parameters.AddWithValue("@id", dbId);
                        var result = getCmd.ExecuteScalar();
                        if (result != null && result != DBNull.Value)
                        {
                            senderEmailToUpdate = result.ToString().ToLower().Trim();
                        }
                    }

                    if (!string.IsNullOrEmpty(senderEmailToUpdate))
                    {
                        string updateRuleQuery = @"
                        UPDATE regeln 
                        SET firma_ordner = @firma 
                        WHERE email_adresse = @email;";

                        using (var updateCmd = new SqliteCommand(updateRuleQuery, connection))
                        {
                            updateCmd.Parameters.AddWithValue("@firma", firmenName.Trim());
                            updateCmd.Parameters.AddWithValue("@email", senderEmailToUpdate);
                            updateCmd.ExecuteNonQuery();
                        }
                    }

                    string deleteQuery = "DELETE FROM unsorted_emails WHERE id = @id;";
                    using (var deleteCmd = new SqliteCommand(deleteQuery, connection))
                    {
                        deleteCmd.Parameters.AddWithValue("@id", dbId);
                        deleteCmd.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Fehler beim Einsortieren]: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Verschiebt den E-Mail-Ordner aus dem Unsorted-Ordner in den Archiv-Ordner
        /// und benennt ihn in "Absender Hash" um. Kein Firmenordner, keine Auftragsnummer,
        /// kein Blacklist-Eintrag. Gibt den neuen Pfad zurück.
        /// </summary>
        public string OrdnerInArchivVerschieben(int dbId, string uniqueId, string aktuellerOrdnerPfad, string absenderName)
        {
            try
            {
                var config = AppConfig.Laden();
                string archivOrdner = config.ArchivOrdner;

                if (string.IsNullOrWhiteSpace(archivOrdner))
                {
                    throw new Exception("In den Einstellungen ist kein 'Archiv-Ordner' angegeben!");
                }

                if (!Directory.Exists(aktuellerOrdnerPfad))
                {
                    throw new DirectoryNotFoundException($"Der Quell-Ordner wurde nicht gefunden: {aktuellerOrdnerPfad}");
                }

                Directory.CreateDirectory(archivOrdner);

                string sauberAbsender = string.Join("_", (absenderName ?? "").Split(Path.GetInvalidFileNameChars())).Trim();
                if (sauberAbsender.Length > 60) sauberAbsender = sauberAbsender.Substring(0, 60).Trim();
                if (string.IsNullOrEmpty(sauberAbsender)) sauberAbsender = "Unbekannt";

                string hash = GenerateShortHash(uniqueId);
                string neuerName = $"{sauberAbsender} {hash}";
                string zielPfad = Path.Combine(archivOrdner, neuerName);

                if (Directory.Exists(zielPfad))
                {
                    zielPfad = Path.Combine(archivOrdner, $"{neuerName}_{DateTime.Now:HHmmss}");
                }

                Directory.Move(aktuellerOrdnerPfad, zielPfad);
                LoescheUnsortedEintrag(dbId);

                return zielPfad;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Fehler beim Ablegen]: {ex.Message}");
                throw;
            }
        }

        public string HoleGespeicherteFirmaFuerAbsender(string senderEmail)
        {
            if (string.IsNullOrWhiteSpace(senderEmail)) return string.Empty;

            string cleanEmail = senderEmail.ToLower().Trim();

            using (var connection = new SqliteConnection($"Data Source={_dbPath}"))
            {
                connection.Open();
                string query = "SELECT firma_ordner FROM regeln WHERE email_adresse = @email AND firma_ordner IS NOT NULL AND firma_ordner != '' LIMIT 1;";

                using (var cmd = new SqliteCommand(query, connection))
                {
                    cmd.Parameters.AddWithValue("@email", cleanEmail);
                    var result = cmd.ExecuteScalar();
                    if (result != null && result != DBNull.Value)
                    {
                        return result.ToString();
                    }
                }
            }
            return string.Empty;
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
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Cache Fehler]: {ex.Message}");
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
                Console.WriteLine($"[Cache Fehler]: {ex.Message}");
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
            var config = AppConfig.Laden();

            int tageZurueck = config.TAGE_ZURUECK > 0 ? config.TAGE_ZURUECK : 7;
            bool nurUngelesene = config.NUR_UNGELESENE;
            int delayMs = config.DELAY_MILLISEKUNDEN > 0 ? config.DELAY_MILLISEKUNDEN : 1000;

            string unsortedOrdnerPfad = config.UnsortedOrdner;
            Directory.CreateDirectory(unsortedOrdnerPfad);

            using (var client = new ImapClient())
            {
                try
                {
                    Console.WriteLine("[IMAP] Verbinde und authentifiziere...");
                    await client.ConnectAsync(imapServer, port, true);

                    if (config.UseOAuth2)
                    {
                        var tokenResult = await OAuthService.HoleAccessTokenAsync(config);
                        string loginName = string.IsNullOrWhiteSpace(tokenResult.Account?.Username)
                            ? email
                            : tokenResult.Account.Username;
                        Console.WriteLine($"[OAuth] Token erhalten für Konto: {loginName}");
                        var oauth2 = new SaslMechanismOAuth2(loginName, tokenResult.AccessToken);
                        await client.AuthenticateAsync(oauth2);
                        Console.WriteLine("[IMAP] Mit OAuth2 angemeldet.");
                    }
                    else
                    {
                        await client.AuthenticateAsync(email, appPassword);
                    }

                    var inbox = client.Inbox;
                    await inbox.OpenAsync(FolderAccess.ReadWrite);

                    var query = SearchQuery.All;
                    if (nurUngelesene) query = query.And(SearchQuery.NotSeen);
                    if (tageZurueck > 0)
                    {
                        query = query.And(SearchQuery.SentSince(DateTime.Now.AddDays(-tageZurueck)));
                    }

                    Console.WriteLine("[IMAP] Suche nach E-Mails auf dem Server...");
                    var uids = await inbox.SearchAsync(query);
                    Console.WriteLine($"[IMAP] Gefundene E-Mails im Postfach: {uids.Count}");

                    int counter = 1;
                    foreach (var uid in uids)
                    {
                        string uniqueId = uid.Id.ToString();

                        if (!_downloadedIds.Contains(uniqueId))
                        {
                            var summary = await inbox.FetchAsync(new[] { uid }, MessageSummaryItems.Envelope);
                            var envelope = summary.FirstOrDefault()?.Envelope;

                            string absenderEmail = envelope?.Sender.Mailboxes.FirstOrDefault()?.Address
                                                   ?? envelope?.From.Mailboxes.FirstOrDefault()?.Address
                                                   ?? string.Empty;

                            if (!string.IsNullOrEmpty(absenderEmail) && IstAbsenderBlockiert(absenderEmail))
                            {
                                Console.WriteLine($"[Blacklist] Überspringe blockierten Absender: {absenderEmail} (UID: {uniqueId})");
                                _downloadedIds.Add(uniqueId);
                                counter++;
                                continue;
                            }

                            Console.WriteLine($"[Download] Lade E-Mail {counter} von {uids.Count} (UID: {uniqueId})...");
                            var message = await inbox.GetMessageAsync(uid);

                            var mailbox = message.From.Mailboxes.FirstOrDefault();
                            absenderEmail = mailbox?.Address ?? message.From.ToString();

                            string absenderName = !string.IsNullOrWhiteSpace(mailbox?.Name)
                                ? mailbox.Name.Trim()
                                : absenderEmail;

                            string betreff = message.Subject ?? "Kein Betreff";
                            DateTime datum = message.Date.DateTime;

                            ErfasseOderPruefeEmail(absenderEmail, absenderName);

                            string datumString = datum.ToString("yyyy-MM-dd_HHmmss");
                            string idHash = GenerateShortHash(uniqueId);
                            string ordnerName = $"{datumString}_{idHash}";

                            string zielOrdnerPfad = Path.Combine(unsortedOrdnerPfad, ordnerName);
                            Directory.CreateDirectory(zielOrdnerPfad);

                            string emlPfad = Path.Combine(zielOrdnerPfad, "email.eml");
                            await message.WriteToAsync(emlPfad);

                            var anhaengeNamen = new List<string>();

                            foreach (var attachment in message.Attachments)
                            {
                                if (attachment is MimePart mimePart)
                                {
                                    var disposition = mimePart.ContentDisposition?.Disposition;
                                    if (disposition != null && disposition.Equals("attachment", StringComparison.OrdinalIgnoreCase))
                                    {
                                        string originalFileName = mimePart.FileName ?? "unbenannt.dat";
                                        string anhangPfad = Path.Combine(zielOrdnerPfad, originalFileName);
                                        anhaengeNamen.Add(originalFileName);

                                        using (var memoryStream = new MemoryStream())
                                        {
                                            await mimePart.Content.DecodeToAsync(memoryStream);
                                            await File.WriteAllBytesAsync(anhangPfad, memoryStream.ToArray());
                                        }
                                    }
                                }
                            }

                            SpeichereUnsortedEmailInDb(uniqueId, absenderName, absenderEmail, betreff, datum, zielOrdnerPfad);

                            newEmails.Add(new EmailPreviewModel
                            {
                                UniqueId = uniqueId,
                                Sender = absenderName,
                                SenderEmail = absenderEmail,
                                Subject = betreff,
                                Date = datum,
                                FilePath = zielOrdnerPfad,
                                Attachments = anhaengeNamen // Anhänge für die Vorschau übergeben
                            });

                            _downloadedIds.Add(uniqueId);
                            Console.WriteLine($"[Erfolg] E-Mail gespeichert.\n");

                            if (delayMs > 0)
                            {
                                await Task.Delay(delayMs);
                            }
                        }
                        else
                        {
                            Console.WriteLine($"[Info] E-Mail {counter}/{uids.Count} (UID: {uniqueId}) ist bereits im Cache. Überspringe...");
                        }
                        counter++;
                    }

                    if (newEmails.Count > 0)
                    {
                        SaveCache();
                    }

                    await client.DisconnectAsync(true);
                    Console.WriteLine("[IMAP] Verbindung getrennt.");
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
        public int Id { get; set; }
        public string UniqueId { get; set; }
        public string Sender { get; set; }
        public string SenderEmail { get; set; }
        public string Subject { get; set; }
        public DateTime Date { get; set; }
        public string FilePath { get; set; }
        public List<string> Attachments { get; set; } = new List<string>();
    }

    public class BlockedSenderModel
    {
        public string Email { get; set; }
        public string Name { get; set; }
    }
}
