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

                // Spalte für nicht gespeicherte Anhänge nachrüsten (alte Datenbanken haben sie noch nicht)
                bool spalteVorhanden = false;
                using (var pragma = new SqliteCommand("PRAGMA table_info(unsorted_emails);", connection))
                using (var reader = pragma.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        if (string.Equals(reader.GetString(1), "blocked_attachments", StringComparison.OrdinalIgnoreCase))
                        {
                            spalteVorhanden = true;
                        }
                    }
                }

                if (!spalteVorhanden)
                {
                    using (var alter = new SqliteCommand("ALTER TABLE unsorted_emails ADD COLUMN blocked_attachments TEXT;", connection))
                    {
                        alter.ExecuteNonQuery();
                    }
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

        private void SpeichereUnsortedEmailInDb(string uniqueId, string sender, string senderEmail, string subject, DateTime date, string ordnerPfad, string blockierteAnhaenge)
        {
            using (var connection = new SqliteConnection($"Data Source={_dbPath}"))
            {
                connection.Open();
                string query = @"
                INSERT OR REPLACE INTO unsorted_emails (unique_id, sender, sender_email, subject, date, file_path, blocked_attachments)
                VALUES (@uid, @sender, @email, @subject, @date, @path, @blocked);";

                using (var cmd = new SqliteCommand(query, connection))
                {
                    cmd.Parameters.AddWithValue("@uid", uniqueId);
                    cmd.Parameters.AddWithValue("@sender", sender);
                    cmd.Parameters.AddWithValue("@email", senderEmail);
                    cmd.Parameters.AddWithValue("@subject", subject);
                    cmd.Parameters.AddWithValue("@date", date.ToString("o"));
                    cmd.Parameters.AddWithValue("@path", ordnerPfad);
                    cmd.Parameters.AddWithValue("@blocked", (object)blockierteAnhaenge ?? DBNull.Value);
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
                string query = "SELECT id, unique_id, sender, sender_email, subject, date, file_path, blocked_attachments FROM unsorted_emails ORDER BY date DESC;";

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
                                    Attachments = anhaenge,
                                    BlockedAttachments = ParseBlockierte(reader.IsDBNull(7) ? null : reader.GetString(7))
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

        // =====================================================================================
        //  Abruf (Microsoft Graph oder IMAP)
        // =====================================================================================

        public async Task<List<EmailPreviewModel>> FetchNewEmailsAsync(string imapServer, int port, string email, string appPassword, string zielHauptOrdner)
        {
            var config = AppConfig.Laden();

            int tageZurueck = config.TAGE_ZURUECK > 0 ? config.TAGE_ZURUECK : 7;
            bool nurUngelesene = config.NUR_UNGELESENE;
            int delayMs = config.DELAY_MILLISEKUNDEN > 0 ? config.DELAY_MILLISEKUNDEN : 1000;

            string unsortedOrdnerPfad = config.UnsortedOrdner;
            Directory.CreateDirectory(unsortedOrdnerPfad);

            try
            {
                List<EmailPreviewModel> ergebnis;

                if (config.IstGraph)
                {
                    ergebnis = await FetchViaGraphAsync(config, unsortedOrdnerPfad, tageZurueck, nurUngelesene, delayMs);
                }
                else
                {
                    ergebnis = await FetchViaImapAsync(config, imapServer, port, email, appPassword, unsortedOrdnerPfad, tageZurueck, nurUngelesene, delayMs);
                }

                SchreibeZusammenfassung(ergebnis);
                return ergebnis;
            }
            finally
            {
                // Auch bei einem Fehler mittendrin merken, was schon heruntergeladen wurde
                SaveCache();
            }
        }

        private async Task<List<EmailPreviewModel>> FetchViaGraphAsync(AppConfig config, string unsortedOrdnerPfad, int tageZurueck, bool nurUngelesene, int delayMs)
        {
            var newEmails = new List<EmailPreviewModel>();

            try
            {
                Console.WriteLine("[Graph] Melde bei Microsoft an...");
                // Erster Aufruf darf den Browser öffnen, alle weiteren nur lautlos (Cache / Refresh-Token)
                var ersterToken = await OAuthService.HoleAccessTokenAsync(config, true);
                Console.WriteLine($"[Graph] Angemeldet als: {ersterToken.Account?.Username}");

                bool ersterAufruf = true;
                Func<Task<string>> tokenProvider = async () =>
                {
                    if (ersterAufruf)
                    {
                        ersterAufruf = false;
                        return ersterToken.AccessToken;
                    }
                    var r = await OAuthService.HoleAccessTokenAsync(config, false);
                    return r.AccessToken;
                };

                using (var graph = new GraphMailClient(tokenProvider))
                {
                    Console.WriteLine("[Graph] Suche nach E-Mails im Posteingang...");
                    var liste = await graph.HoleNachrichtenAsync(DateTime.UtcNow.AddDays(-tageZurueck), nurUngelesene);
                    Console.WriteLine($"[Graph] Gefundene E-Mails im Postfach: {liste.Count}");

                    int counter = 1;
                    foreach (var info in liste)
                    {
                        string uniqueId = info.Id;

                        if (!_downloadedIds.Contains(uniqueId))
                        {
                            if (!string.IsNullOrEmpty(info.AbsenderEmail) && IstAbsenderBlockiert(info.AbsenderEmail))
                            {
                                Console.WriteLine($"[Blacklist] Überspringe blockierten Absender: {info.AbsenderEmail}");
                                _downloadedIds.Add(uniqueId);
                                counter++;
                                continue;
                            }

                            Console.WriteLine($"[Download] Lade E-Mail {counter} von {liste.Count}...");
                            byte[] rohdaten = await graph.HoleRohdatenAsync(info.Id);

                            MimeMessage message;
                            using (var ms = new MemoryStream(rohdaten))
                            {
                                message = await MimeMessage.LoadAsync(ms);
                            }

                            var model = await SpeichereNachrichtAsync(message, uniqueId, unsortedOrdnerPfad, config, info.Empfangen.LocalDateTime);
                            newEmails.Add(model);

                            _downloadedIds.Add(uniqueId);
                            Console.WriteLine("[Erfolg] E-Mail gespeichert.\n");

                            if (delayMs > 0)
                            {
                                await Task.Delay(delayMs);
                            }
                        }
                        else
                        {
                            Console.WriteLine($"[Info] E-Mail {counter}/{liste.Count} ist bereits im Cache. Überspringe...");
                        }
                        counter++;
                    }
                }

                Console.WriteLine("[Graph] Fertig.");
            }
            catch (Exception ex)
            {
                throw new Exception($"Graph-Fehler: {ex.Message}");
            }

            return newEmails;
        }

        private async Task<List<EmailPreviewModel>> FetchViaImapAsync(AppConfig config, string imapServer, int port, string email, string appPassword,
            string unsortedOrdnerPfad, int tageZurueck, bool nurUngelesene, int delayMs)
        {
            var newEmails = new List<EmailPreviewModel>();

            using (var client = new ImapClient())
            {
                try
                {
                    Console.WriteLine("[IMAP] Verbinde und authentifiziere...");
                    await client.ConnectAsync(imapServer, port, true);
                    await ImapAnmeldenAsync(client, config, email, appPassword);

                    var inbox = client.Inbox;
                    // Nur lesend öffnen: es wird nichts im Postfach verändert (auch kein "gelesen"-Markieren)
                    await inbox.OpenAsync(FolderAccess.ReadOnly);

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

                            var model = await SpeichereNachrichtAsync(message, uniqueId, unsortedOrdnerPfad, config, null);
                            newEmails.Add(model);

                            _downloadedIds.Add(uniqueId);
                            Console.WriteLine("[Erfolg] E-Mail gespeichert.\n");

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

        /// <summary>
        /// Meldet den IMAP-Client an: entweder mit Passwort / App-Key oder mit OAuth2-Token.
        /// Bei OAuth2 wird der Benutzername aus dem Token verwendet, damit er sicher zum Konto passt.
        /// Gibt den verwendeten Anmeldenamen zurück.
        /// </summary>
        private async Task<string> ImapAnmeldenAsync(ImapClient client, AppConfig config, string email, string appPassword)
        {
            if (config.IstImapOAuth2)
            {
                var tokenResult = await OAuthService.HoleAccessTokenAsync(config, true);
                string loginName = string.IsNullOrWhiteSpace(tokenResult.Account?.Username)
                    ? email
                    : tokenResult.Account.Username;

                Console.WriteLine($"[OAuth] Token erhalten für Konto: {loginName}");
                await client.AuthenticateAsync(new SaslMechanismOAuth2(loginName, tokenResult.AccessToken));
                Console.WriteLine("[IMAP] Mit OAuth2 angemeldet.");
                return loginName;
            }

            await client.AuthenticateAsync(email, appPassword);
            return email;
        }

        /// <summary>
        /// Verbindungstest für alle drei Anmeldearten. Liefert einen Text für die Anzeige
        /// und wirft bei einem Fehler eine Exception.
        /// </summary>
        public async Task<string> TesteVerbindungAsync(AppConfig config)
        {
            if (config.IstGraph)
            {
                var token = await OAuthService.HoleAccessTokenAsync(config, true);

                int anzahlGraph;
                using (var graph = new GraphMailClient(() => Task.FromResult(token.AccessToken)))
                {
                    anzahlGraph = await graph.HoleAnzahlPosteingangAsync();
                }

                return $"Angemeldet als {token.Account?.Username}.\nPosteingang über Microsoft Graph erreichbar ({anzahlGraph} Nachrichten).";
            }

            int port = config.ImapPort > 0 ? config.ImapPort : 993;

            using (var client = new ImapClient())
            {
                await client.ConnectAsync(config.ImapServer, port, true);
                string konto = await ImapAnmeldenAsync(client, config, config.EmailKonto, config.Passwort);

                var inbox = client.Inbox;
                await inbox.OpenAsync(FolderAccess.ReadOnly);
                int anzahlImap = inbox.Count;
                await client.DisconnectAsync(true);

                return $"Angemeldet als {konto}.\nPosteingang über IMAP erreichbar ({anzahlImap} Nachrichten).";
            }
        }

        // =====================================================================================
        //  Gemeinsames Speichern (Graph und IMAP) inkl. Anhang-Filter
        // =====================================================================================

        private async Task<EmailPreviewModel> SpeichereNachrichtAsync(MimeMessage message, string uniqueId, string unsortedOrdnerPfad, AppConfig config, DateTime? ersatzDatum)
        {
            var mailbox = message.From.Mailboxes.FirstOrDefault();
            string absenderEmail = mailbox?.Address ?? message.From.ToString();
            string absenderName = !string.IsNullOrWhiteSpace(mailbox?.Name) ? mailbox.Name.Trim() : absenderEmail;

            string betreff = message.Subject ?? "Kein Betreff";
            DateTime datum = message.Date == DateTimeOffset.MinValue
                ? (ersatzDatum ?? DateTime.Now)
                : message.Date.DateTime;

            ErfasseOderPruefeEmail(absenderEmail, absenderName);

            string datumString = datum.ToString("yyyy-MM-dd_HHmmss");
            string idHash = GenerateShortHash(uniqueId);
            string ordnerName = $"{datumString}_{idHash}";

            string zielOrdnerPfad = Path.Combine(unsortedOrdnerPfad, ordnerName);
            Directory.CreateDirectory(zielOrdnerPfad);

            // 1) Anhänge einsammeln (mit ihrem übergeordneten Multipart, um sie notfalls entfernen zu können)
            var kandidaten = new List<MimePart>();
            var eltern = new List<Multipart>();
            var iter = new MimeIterator(message);
            while (iter.MoveNext())
            {
                if (iter.Parent is Multipart multipart && iter.Current is MimePart part && IstAnhang(part))
                {
                    kandidaten.Add(part);
                    eltern.Add(multipart);
                }
            }

            // 2) Nur erlaubte Anhänge speichern, alle anderen verwerfen (und festhalten)
            var anhaengeNamen = new List<string>();
            var belegteNamen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "email.eml" };
            var zuEntfernen = new List<int>();
            var blockierte = new List<BlockedAttachmentModel>();

            for (int i = 0; i < kandidaten.Count; i++)
            {
                var part = kandidaten[i];
                string sauberName = SaubererDateiname(part.FileName);
                string grund = null;
                byte[] daten = null;

                // 1) Sperrliste hat immer Vorrang (z. B. exe), da wird nicht einmal der Inhalt gelesen
                if (config.IstEndungGesperrt(sauberName))
                {
                    grund = "Dateityp gesperrt (Blacklist)";
                }
                else
                {
                    using (var memoryStream = new MemoryStream())
                    {
                        await part.Content.DecodeToAsync(memoryStream);
                        daten = memoryStream.ToArray();
                    }

                    // 2) Ein Windows-Programm wird immer verworfen, egal wie die Datei heißt
                    //    (außer exe ist in den Einstellungen ausdrücklich freigegeben)
                    if (IstProgrammdatei(daten) && !config.IstAnhangErlaubt("datei.exe"))
                    {
                        grund = "Inhalt ist ein Windows-Programm";
                    }
                    else if (config.IstAnhangErlaubt(sauberName))
                    {
                        // 3) Endung steht in der Whitelist: Der Inhalt muss dazu passen
                        if (!InhaltPasstZurEndung(Path.GetExtension(sauberName), daten))
                        {
                            grund = "Inhalt passt nicht zur Endung";
                        }
                    }
                    else
                    {
                        // 4) Endung fehlt oder ist unbekannt (z. B. "Rechnung" oder "Rechnung 01.10.2026"):
                        //    den echten Typ am Inhalt erkennen und die richtige Endung ergänzen
                        string erkannt = ErkenneEndungAnInhalt(daten);
                        if (erkannt != null && config.IstAnhangErlaubt("datei." + erkannt))
                        {
                            Console.WriteLine($"[Anhang] Endung ergänzt: {sauberName} -> .{erkannt}");
                            sauberName = sauberName + "." + erkannt;
                        }
                        else
                        {
                            grund = "Dateityp nicht freigegeben (Whitelist)";
                        }
                    }
                }

                if (grund != null)
                {
                    Console.WriteLine($"[Anhang] NICHT gespeichert: {sauberName} ({grund})");
                    Serilog.Log.Warning("Anhang nicht gespeichert: Absender='{Absender}', Datei='{Datei}', Grund='{Grund}'",
                        absenderEmail, sauberName, grund);

                    blockierte.Add(new BlockedAttachmentModel { Name = sauberName, Reason = grund });
                    zuEntfernen.Add(i);
                    continue;
                }

                string finalerName = EindeutigerName(sauberName, belegteNamen);
                await File.WriteAllBytesAsync(Path.Combine(zielOrdnerPfad, finalerName), daten);
                anhaengeNamen.Add(finalerName);
            }

            // 3) Verworfene Anhänge auch aus der gespeicherten email.eml entfernen,
            //    damit keine gesperrten Dateien in irgendeiner Form auf der Platte landen
            foreach (int index in zuEntfernen)
            {
                eltern[index].Remove(kandidaten[index]);
            }

            string emlPfad = Path.Combine(zielOrdnerPfad, "email.eml");
            await message.WriteToAsync(emlPfad);

            SpeichereUnsortedEmailInDb(uniqueId, absenderName, absenderEmail, betreff, datum, zielOrdnerPfad, SerialisiereBlockierte(blockierte));

            return new EmailPreviewModel
            {
                UniqueId = uniqueId,
                Sender = absenderName,
                SenderEmail = absenderEmail,
                Subject = betreff,
                Date = datum,
                FilePath = zielOrdnerPfad,
                Attachments = anhaengeNamen,
                BlockedAttachments = blockierte
            };
        }

        /// <summary>
        /// Erkennt den echten Dateityp am Inhalt (nicht am Namen). Nur eindeutige Formate:
        /// PDF, JPEG, PNG, GIF, TIFF und WebP. Alles andere (z. B. CAD ohne Endung, Text) gilt als
        /// nicht erkennbar und liefert null. BMP ist bewusst nicht dabei, weil der Header nur 2 Zeichen lang ist.
        /// </summary>
        private static string ErkenneEndungAnInhalt(byte[] daten)
        {
            string[] kandidaten = { "pdf", "jpg", "png", "gif", "tif", "webp" };
            foreach (string endung in kandidaten)
            {
                if (InhaltPasstZurEndung(endung, daten))
                {
                    return endung;
                }
            }
            return null;
        }

        /// <summary>
        /// Erkennt ein Windows-Programm (exe, dll, scr ...) am Inhalt: Die Datei beginnt mit "MZ" und
        /// enthält an der im Kopf angegebenen Stelle die Kennung "PE". Der Dateiname spielt keine Rolle.
        /// </summary>
        private static bool IstProgrammdatei(byte[] daten)
        {
            if (daten == null || daten.Length < 64) return false;
            if (daten[0] != 0x4D || daten[1] != 0x5A) return false; // "MZ"

            int peOffset = BitConverter.ToInt32(daten, 0x3C);
            if (peOffset < 0 || peOffset > daten.Length - 4) return false;

            return daten[peOffset] == 0x50 && daten[peOffset + 1] == 0x45
                   && daten[peOffset + 2] == 0x00 && daten[peOffset + 3] == 0x00; // "PE\0\0"
        }

        // Nicht gespeicherte Anhänge: pro Eintrag "Name<TAB>Grund", Einträge durch Zeilenumbruch getrennt.
        // Dateinamen sind bereits bereinigt und enthalten weder Tab noch Zeilenumbruch.
        private static string SerialisiereBlockierte(List<BlockedAttachmentModel> liste)
        {
            if (liste == null || liste.Count == 0) return null;
            return string.Join("\n", liste.Select(b => b.Name + "\t" + b.Reason));
        }

        private static List<BlockedAttachmentModel> ParseBlockierte(string text)
        {
            var ergebnis = new List<BlockedAttachmentModel>();
            if (string.IsNullOrEmpty(text)) return ergebnis;

            foreach (string zeile in text.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] teile = zeile.Split('\t');
                ergebnis.Add(new BlockedAttachmentModel
                {
                    Name = teile[0],
                    Reason = teile.Length > 1 ? teile[1] : string.Empty
                });
            }
            return ergebnis;
        }

        /// <summary>
        /// Schreibt am Ende des Abrufs auf, wie viele Anhänge nicht gespeichert wurden und bei welchen E-Mails.
        /// Die Ausgabe erscheint im Fortschrittsfenster und (als Warnung) im Logfile.
        /// </summary>
        private static void SchreibeZusammenfassung(List<EmailPreviewModel> neu)
        {
            int anzahl = neu.Sum(m => m.BlockedAttachments.Count);
            if (anzahl == 0)
            {
                Console.WriteLine("[Zusammenfassung] Alle Anhänge wurden gespeichert.");
                return;
            }

            int mails = neu.Count(m => m.BlockedAttachments.Count > 0);
            Console.WriteLine($"\n[Zusammenfassung] Nicht gespeicherte Anhänge: {anzahl} (aus {mails} E-Mails)");

            foreach (var mail in neu.Where(m => m.BlockedAttachments.Count > 0))
            {
                Console.WriteLine($"  E-Mail von {mail.Sender} ({mail.SenderEmail}):");
                foreach (var b in mail.BlockedAttachments)
                {
                    Console.WriteLine($"    - {b.Name}: {b.Reason}");
                }
            }

            Serilog.Log.Warning("Zusammenfassung: {Anzahl} Anhänge aus {Mails} E-Mails wurden nicht gespeichert.", anzahl, mails);
        }

        private static bool IstAnhang(MimePart part)
        {
            var disposition = part.ContentDisposition?.Disposition;
            if (disposition != null && disposition.Equals("attachment", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Datei ohne Disposition-Angabe, aber mit Dateiname und ohne Content-ID (also kein Inline-Bild)
            return !(part is TextPart)
                   && !string.IsNullOrEmpty(part.FileName)
                   && string.IsNullOrEmpty(part.ContentId);
        }

        private static string SaubererDateiname(string originalName)
        {
            string name = string.IsNullOrWhiteSpace(originalName) ? "unbenannt" : originalName;

            // Pfadanteile entfernen (Schutz vor "..\..\datei")
            name = Path.GetFileName(name.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar));

            foreach (char c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }

            name = name.Trim().TrimEnd('.', ' ');
            if (string.IsNullOrEmpty(name)) name = "unbenannt";

            if (name.Length > 150)
            {
                string endung = Path.GetExtension(name);
                string basis = Path.GetFileNameWithoutExtension(name);
                int max = Math.Max(1, 150 - endung.Length);
                name = basis.Substring(0, Math.Min(basis.Length, max)) + endung;
            }

            return name;
        }

        private static string EindeutigerName(string name, HashSet<string> belegt)
        {
            string basis = Path.GetFileNameWithoutExtension(name);
            string endung = Path.GetExtension(name);
            string kandidat = name;
            int nr = 2;

            while (belegt.Contains(kandidat))
            {
                kandidat = $"{basis} ({nr}){endung}";
                nr++;
            }

            belegt.Add(kandidat);
            return kandidat;
        }

        /// <summary>
        /// Zusätzlicher Schutz gegen umbenannte Dateien: Eine ".pdf" muss wirklich mit einem PDF-Header
        /// beginnen, eine ".jpg" mit einem JPEG-Header. CAD-Formate haben keinen einheitlichen Header
        /// und werden nur über die Endung geprüft.
        /// </summary>
        private static bool InhaltPasstZurEndung(string endung, byte[] daten)
        {
            string e = (endung ?? "").TrimStart('.').ToLowerInvariant();

            if (e == "pdf")
            {
                // "%PDF" muss in den ersten 1024 Bytes vorkommen
                int grenze = Math.Min(daten.Length - 4, 1024);
                for (int i = 0; i <= grenze; i++)
                {
                    if (daten[i] == 0x25 && daten[i + 1] == 0x50 && daten[i + 2] == 0x44 && daten[i + 3] == 0x46)
                    {
                        return true;
                    }
                }
                return false;
            }

            if (e == "jpg" || e == "jpeg")
            {
                return daten.Length >= 3 && daten[0] == 0xFF && daten[1] == 0xD8 && daten[2] == 0xFF;
            }

            if (e == "png")
            {
                return daten.Length >= 8 && daten[0] == 0x89 && daten[1] == 0x50 && daten[2] == 0x4E && daten[3] == 0x47
                       && daten[4] == 0x0D && daten[5] == 0x0A && daten[6] == 0x1A && daten[7] == 0x0A;
            }

            if (e == "gif")
            {
                // "GIF8"
                return daten.Length >= 4 && daten[0] == 0x47 && daten[1] == 0x49 && daten[2] == 0x46 && daten[3] == 0x38;
            }

            if (e == "bmp")
            {
                // "BM"
                return daten.Length >= 2 && daten[0] == 0x42 && daten[1] == 0x4D;
            }

            if (e == "tif" || e == "tiff")
            {
                // "II*\0" (little endian) oder "MM\0*" (big endian)
                if (daten.Length < 4) return false;
                bool little = daten[0] == 0x49 && daten[1] == 0x49 && daten[2] == 0x2A && daten[3] == 0x00;
                bool big = daten[0] == 0x4D && daten[1] == 0x4D && daten[2] == 0x00 && daten[3] == 0x2A;
                return little || big;
            }

            if (e == "webp")
            {
                // "RIFF" .... "WEBP"
                return daten.Length >= 12
                       && daten[0] == 0x52 && daten[1] == 0x49 && daten[2] == 0x46 && daten[3] == 0x46
                       && daten[8] == 0x57 && daten[9] == 0x45 && daten[10] == 0x42 && daten[11] == 0x50;
            }

            if (e == "txt")
            {
                // UTF-16 mit Byte-Order-Mark ist ein normaler Text (Windows-Editor "Unicode")
                if (daten.Length >= 2 && ((daten[0] == 0xFF && daten[1] == 0xFE) || (daten[0] == 0xFE && daten[1] == 0xFF)))
                {
                    return true;
                }

                // Programme beginnen mit "MZ"
                if (daten.Length >= 2 && daten[0] == 0x4D && daten[1] == 0x5A)
                {
                    return false;
                }

                // Echter Text enthält keine Null-Bytes; Binärdateien meistens schon
                int grenze = Math.Min(daten.Length, 4096);
                for (int i = 0; i < grenze; i++)
                {
                    if (daten[i] == 0x00) return false;
                }
                return true;
            }

            // Falls jemand Archive oder Office-Dateien in der Whitelist freigibt: Header muss passen
            if (e == "zip" || e == "docx" || e == "xlsx" || e == "pptx")
            {
                // "PK\x03\x04" (oder leeres Archiv "PK\x05\x06")
                return daten.Length >= 4 && daten[0] == 0x50 && daten[1] == 0x4B
                       && ((daten[2] == 0x03 && daten[3] == 0x04) || (daten[2] == 0x05 && daten[3] == 0x06));
            }

            if (e == "rar")
            {
                // "Rar!"
                return daten.Length >= 4 && daten[0] == 0x52 && daten[1] == 0x61 && daten[2] == 0x72 && daten[3] == 0x21;
            }

            if (e == "7z")
            {
                return daten.Length >= 4 && daten[0] == 0x37 && daten[1] == 0x7A && daten[2] == 0xBC && daten[3] == 0xAF;
            }

            return true;
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

        // Anhänge, die NICHT gespeichert wurden (mit Grund), für die rote Markierung in der Liste
        public List<BlockedAttachmentModel> BlockedAttachments { get; set; } = new List<BlockedAttachmentModel>();

        public string BlockedHinweis
        {
            get
            {
                int n = BlockedAttachments?.Count ?? 0;
                if (n == 0) return string.Empty;
                return n == 1 ? "⚠ 1 Anhang nicht gespeichert" : $"⚠ {n} Anhänge nicht gespeichert";
            }
        }
    }

    public class BlockedAttachmentModel
    {
        public string Name { get; set; }
        public string Reason { get; set; }
    }

    public class BlockedSenderModel
    {
        public string Email { get; set; }
        public string Name { get; set; }
    }
}