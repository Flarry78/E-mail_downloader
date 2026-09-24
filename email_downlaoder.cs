namespace email_csharp 
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Security.Cryptography;
    using System.Text;
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

            using (var client = new ImapClient())
            {
                try
                {
                    Console.WriteLine($"\n[IMAP] Verbinde zu {imapServer}:{port}...");
                    await client.ConnectAsync(imapServer, port, true);
                    Console.WriteLine("[IMAP] Authentifiziere als " + email + "...");

                    await client.AuthenticateAsync(email, appPassword);
                    var inbox = client.Inbox;
                    await inbox.OpenAsync(FolderAccess.ReadWrite);
                    Console.WriteLine("[IMAP] Posteingang erfolgreich geöffnet.");

                    var query = SearchQuery.All;
                    if (nurUngelesene) query = query.And(SearchQuery.NotSeen);
                    if (tageZurueck > 0)
                    {
                        var datumGrenze = DateTime.Now.AddDays(-tageZurueck);
                        query = query.And(SearchQuery.SentSince(datumGrenze));
                    }

                    Console.WriteLine($"[Suche] Suche E-Mails (Filter: Nur Ungelesen = {nurUngelesene}, Letzte {tageZurueck} Tage)...");
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

                            string absenderRoh = message.From.ToString();
                            string betreff = message.Subject ?? "Kein Betreff";
                            DateTime datum = message.Date.DateTime;

                            Console.WriteLine($"  Von:     {absenderRoh}");
                            Console.WriteLine($"  Betreff: {betreff}");
                            Console.WriteLine($"  Datum:   {datum}");

                            // Ordnerstruktur: [Datum]_[ID-Hash]
                            string datumString = datum.ToString("yyyy-MM-dd_HHmmss");
                            string idHash = GenerateShortHash(uniqueId);
                            string ordnerName = $"{datumString}_{idHash}";

                            string emailOrdnerPfad = Path.Combine(zielHauptOrdner, ordnerName);
                            Directory.CreateDirectory(emailOrdnerPfad);

                            // 1. Die .eml-Datei speichern
                            string emlPfad = Path.Combine(emailOrdnerPfad, "email.eml");
                            await message.WriteToAsync(emlPfad);
                            Console.WriteLine($"  [Gespeichert] E-Mail (.eml) abgelegt.");

                            // 2. Anhänge durchgehen (Nur echte Anhänge via Content-Disposition)
                            foreach (var attachment in message.Attachments)
                            {
                                if (attachment is MimePart mimePart)
                                {
                                    var disposition = mimePart.ContentDisposition?.Disposition;
                                    if (disposition != null && disposition.Equals("attachment", StringComparison.OrdinalIgnoreCase))
                                    {
                                        string originalFileName = mimePart.FileName ?? "unbenannt.dat";
                                        string anhangPfad = Path.Combine(emailOrdnerPfad, originalFileName);

                                        using (var memoryStream = new MemoryStream())
                                        {
                                            await mimePart.Content.DecodeToAsync(memoryStream);
                                            await File.WriteAllBytesAsync(anhangPfad, memoryStream.ToArray());
                                        }

                                        Console.WriteLine($"    [Anhang Gespeichert] -> {originalFileName}");
                                    }
                                    else
                                    {
                                        Console.WriteLine($"    [Übersprungen] Kein echter Anhang (Inline/Logo)");
                                    }
                                }
                            }

                            newEmails.Add(new EmailPreviewModel
                            {
                                UniqueId = uniqueId,
                                Sender = absenderRoh,
                                Subject = betreff,
                                Date = datum,
                                BodySnippet = message.TextBody ?? message.HtmlBody ?? "[Kein Textinhalt]"
                            });

                            _downloadedIds.Add(uniqueId);

                            // Rate Limiting / Pause
                            if (delayMs > 0)
                            {
                                Console.WriteLine($"  [Pause] Warte {delayMs} ms...");
                                await Task.Delay(delayMs);
                            }
                        }
                        else
                        {
                            Console.WriteLine($"[Übersprungen] E-Mail ID {uniqueId} ist bereits im Cache.");
                        }
                        counter++;
                    }

                    if (newEmails.Count > 0)
                    {
                        SaveCache();
                    }
                    else
                    {
                        Console.WriteLine("\n[Info] Keine neuen E-Mails zum Herunterladen gefunden.");
                    }

                    await client.DisconnectAsync(true);
                    Console.WriteLine("\n[IMAP] Verbindung getrennt.");
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