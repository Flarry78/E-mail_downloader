namespace email_csharp 
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.Json;
    using System.Threading.Tasks;
    using MailKit;
    using MailKit.Net.Imap;
    using MailKit.Search;
    using MimeKit;

    public class EmailService
    {
        private readonly string _jsonCachePath;
        private HashSet<string> _downloadedIds = new HashSet<string>();

        public EmailService()
        {
            string basisOrdner = AppDomain.CurrentDomain.BaseDirectory;
            _jsonCachePath = Path.Combine(basisOrdner, "downloaded_emails.json");
            LoadCache();
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
            else
            {
                Console.WriteLine("[Cache] Keine bestehende JSON-Cache-Datei gefunden. Es wird eine neue erstellt.");
            }
        }

        private void SaveCache()
        {
            try
            {
                string jsonString = JsonSerializer.Serialize(_downloadedIds);
                File.WriteAllText(_jsonCachePath, jsonString);
                Console.WriteLine("[Cache] JSON-Cache erfolgreich aktualisiert und gespeichert.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Cache Fehler] Konnte JSON nicht speichern: {ex.Message}");
            }
        }

        public async Task<List<EmailPreviewModel>> FetchNewEmailsAsync(string imapServer, int port, string email, string appPassword)
        {
            var newEmails = new List<EmailPreviewModel>();

            // Einstellungen aus Environment auslesen
            int.TryParse(Environment.GetEnvironmentVariable("TAGE_ZURUECK"), out int tageZurueck);
            if (tageZurueck <= 0) tageZurueck = 7;

            bool nurUngelesene = true;
            if (bool.TryParse(Environment.GetEnvironmentVariable("NUR_UNGELESENE"), out bool parsedBool))
            {
                nurUngelesene = parsedBool;
            }

            int.TryParse(Environment.GetEnvironmentVariable("DELAY_MILLISEKUNDEN"), out int delayMs);
            if (delayMs <= 0) delayMs = 1000;

            using (var client = new ImapClient())
            {
                try
                {
                    Console.WriteLine($"\n[IMAP] Verbinde zu {imapServer}:{port}...");
                    await client.ConnectAsync(imapServer, port, true);
                    Console.WriteLine("[IMAP] Verbindung erfolgreich. Authentifiziere als " + email + "...");

                    await client.AuthenticateAsync(email, appPassword);
                    Console.WriteLine("[IMAP] Authentifizierung erfolgreich.");

                    var inbox = client.Inbox;
                    await inbox.OpenAsync(FolderAccess.ReadWrite);
                    Console.WriteLine("[IMAP] Posteingang (Inbox) geöffnet.");

                    // Suchabfrage bauen
                    var query = SearchQuery.All;
                    if (nurUngelesene)
                    {
                        query = query.And(SearchQuery.NotSeen);
                    }
                    if (tageZurueck > 0)
                    {
                        var datumGrenze = DateTime.Now.AddDays(-tageZurueck);
                        query = query.And(SearchQuery.SentSince(datumGrenze));
                    }

                    Console.WriteLine($"[Suche] Starte Suche auf dem Server (Filter: Nur Ungelesen = {nurUngelesene}, Letzte {tageZurueck} Tage)...");
                    var uids = await inbox.SearchAsync(query);
                    Console.WriteLine($"[Suche] Server hat {uids.Count} E-Mails gefunden, die den Kriterien entsprechen.");

                    int counter = 1;
                    foreach (var uid in uids)
                    {
                        string uniqueId = uid.Id.ToString();

                        // Prüfen, ob neu
                        if (!_downloadedIds.Contains(uniqueId))
                        {
                            Console.WriteLine($"\n[Download] Lade neue E-Mail {counter} von {uids.Count} (ID: {uniqueId})...");
                            var message = await inbox.GetMessageAsync(uid);

                            Console.WriteLine($"          Von:     {message.From}");
                            Console.WriteLine($"          Betreff: {message.Subject}");
                            Console.WriteLine($"          Datum:   {message.Date}");

                            newEmails.Add(new EmailPreviewModel
                            {
                                UniqueId = uniqueId,
                                Sender = message.From.ToString(),
                                Subject = message.Subject,
                                Date = message.Date.DateTime,
                                BodySnippet = message.TextBody ?? message.HtmlBody ?? "[Kein Textinhalt]"
                            });

                            _downloadedIds.Add(uniqueId);

                            // Verzögerung einhalten
                            if (delayMs > 0)
                            {
                                Console.WriteLine($"[Pause] Warte {delayMs} ms, um den Server zu schonen...");
                                await Task.Delay(delayMs);
                            }
                        }
                        else
                        {
                            Console.WriteLine($"[Übersprungen] E-Mail mit ID {uniqueId} ist bereits im Cache.");
                        }
                        counter++;
                    }

                    if (newEmails.Count > 0)
                    {
                        SaveCache();
                    }
                    else
                    {
                        Console.WriteLine("\n[Info] Keine neuen E-Mails zum Speichern gefunden (alle bereits im Cache).");
                    }

                    await client.DisconnectAsync(true);
                    Console.WriteLine("[IMAP] Verbindung sicher getrennt.");
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