namespace email_csharp
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Text.Json;
    using System.Threading.Tasks;

    public class GraphMailInfo
    {
        public string Id { get; set; }
        public DateTimeOffset Empfangen { get; set; }
        public string AbsenderEmail { get; set; }
        public string AbsenderName { get; set; }
    }

    /// <summary>
    /// Schlanker, rein lesender Client für Microsoft Graph (nur GET-Anfragen, nur "Mail.Read").
    /// Das Token wird nie geloggt und nur an graph.microsoft.com geschickt.
    /// </summary>
    public class GraphMailClient : IDisposable
    {
        private const string GraphHost = "https://graph.microsoft.com/";
        private const string GraphBasis = "https://graph.microsoft.com/v1.0";
        private const int MaxNachrichten = 5000;

        private readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        private readonly Func<Task<string>> _tokenProvider;

        public GraphMailClient(Func<Task<string>> tokenProvider)
        {
            _tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
        }

        /// <summary>
        /// Listet die Nachrichten im Posteingang seit dem angegebenen Zeitpunkt (älteste zuerst).
        /// Es werden nur ID, Empfangszeit und Absender angefragt, nicht der Inhalt.
        /// </summary>
        public async Task<List<GraphMailInfo>> HoleNachrichtenAsync(DateTime seitUtc, bool nurUngelesene)
        {
            var ergebnis = new List<GraphMailInfo>();

            string filter = "receivedDateTime ge " +
                            seitUtc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
            if (nurUngelesene) filter += " and isRead eq false";

            string url = GraphBasis + "/me/mailFolders/inbox/messages?$select=id,receivedDateTime,from&$top=50&$filter="
                         + Uri.EscapeDataString(filter);

            while (!string.IsNullOrEmpty(url) && ergebnis.Count < MaxNachrichten)
            {
                using (var response = await SendeAsync(url))
                {
                    string json = await response.Content.ReadAsStringAsync();
                    using (var doc = JsonDocument.Parse(json))
                    {
                        var root = doc.RootElement;

                        if (root.TryGetProperty("value", out var werte))
                        {
                            foreach (var item in werte.EnumerateArray())
                            {
                                var info = new GraphMailInfo
                                {
                                    Id = item.TryGetProperty("id", out var id) ? id.GetString() : null,
                                    AbsenderEmail = string.Empty,
                                    AbsenderName = string.Empty
                                };

                                if (item.TryGetProperty("receivedDateTime", out var empf) &&
                                    DateTimeOffset.TryParse(empf.GetString(), CultureInfo.InvariantCulture,
                                        DateTimeStyles.AssumeUniversal, out var zeit))
                                {
                                    info.Empfangen = zeit;
                                }

                                if (item.TryGetProperty("from", out var from) &&
                                    from.ValueKind == JsonValueKind.Object &&
                                    from.TryGetProperty("emailAddress", out var adr) &&
                                    adr.ValueKind == JsonValueKind.Object)
                                {
                                    if (adr.TryGetProperty("address", out var a)) info.AbsenderEmail = a.GetString() ?? string.Empty;
                                    if (adr.TryGetProperty("name", out var n)) info.AbsenderName = n.GetString() ?? string.Empty;
                                }

                                if (!string.IsNullOrEmpty(info.Id)) ergebnis.Add(info);
                            }
                        }

                        url = root.TryGetProperty("@odata.nextLink", out var next) ? next.GetString() : null;
                    }
                }
            }

            ergebnis.Sort((a, b) => a.Empfangen.CompareTo(b.Empfangen));
            return ergebnis;
        }

        /// <summary>
        /// Lädt die komplette Mail im Original-MIME-Format (wie eine .eml-Datei).
        /// Das Abrufen markiert die Mail nicht als gelesen.
        /// </summary>
        public async Task<byte[]> HoleRohdatenAsync(string messageId)
        {
            string url = GraphBasis + "/me/messages/" + Uri.EscapeDataString(messageId) + "/$value";
            using (var response = await SendeAsync(url))
            {
                return await response.Content.ReadAsByteArrayAsync();
            }
        }

        /// <summary>
        /// Verbindungstest: liefert die Anzahl der Nachrichten im Posteingang.
        /// </summary>
        public async Task<int> HoleAnzahlPosteingangAsync()
        {
            string url = GraphBasis + "/me/mailFolders/inbox?$select=totalItemCount";
            using (var response = await SendeAsync(url))
            {
                string json = await response.Content.ReadAsStringAsync();
                using (var doc = JsonDocument.Parse(json))
                {
                    return doc.RootElement.TryGetProperty("totalItemCount", out var count) ? count.GetInt32() : 0;
                }
            }
        }

        private async Task<HttpResponseMessage> SendeAsync(string url)
        {
            // Sicherheitsnetz: Das Token darf nur an Microsoft Graph gehen, auch bei manipulierten nextLinks.
            if (!url.StartsWith(GraphHost, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Unerwartete Adresse in der Graph-Antwort, Abbruch.");
            }

            for (int versuch = 1; ; versuch++)
            {
                string token = await _tokenProvider();

                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                // Stabile IDs, auch wenn die Mail im Postfach verschoben wird
                request.Headers.TryAddWithoutValidation("Prefer", "IdType=\"ImmutableId\"");

                HttpResponseMessage response = await _http.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    return response;
                }

                int code = (int)response.StatusCode;

                if ((code == 429 || code == 503 || code == 504) && versuch < 4)
                {
                    int warte = 5;
                    var retry = response.Headers.RetryAfter?.Delta;
                    if (retry.HasValue)
                    {
                        warte = (int)Math.Min(60, Math.Max(1, retry.Value.TotalSeconds));
                    }
                    response.Dispose();
                    Console.WriteLine($"[Graph] Server ausgelastet (HTTP {code}), warte {warte} s...");
                    await Task.Delay(warte * 1000);
                    continue;
                }

                string body = await response.Content.ReadAsStringAsync();
                response.Dispose();
                throw new Exception(BaueFehlertext(code, body));
            }
        }

        private static string BaueFehlertext(int code, string body)
        {
            string detail = string.Empty;
            try
            {
                using (var doc = JsonDocument.Parse(body))
                {
                    if (doc.RootElement.TryGetProperty("error", out var err))
                    {
                        string c = err.TryGetProperty("code", out var cp) ? cp.GetString() : "";
                        string m = err.TryGetProperty("message", out var mp) ? mp.GetString() : "";
                        detail = $"{c}: {m}";
                        if (detail.Length > 200) detail = detail.Substring(0, 200);
                    }
                }
            }
            catch
            {
                // Antwort war kein JSON, Details weglassen
            }

            string hinweis = string.Empty;
            if (code == 401 || code == 403)
            {
                hinweis = " Die Berechtigung Mail.Read fehlt oder wurde von der Firma noch nicht freigegeben.";
            }

            return $"Microsoft Graph (HTTP {code}): {detail}{hinweis}".Trim();
        }

        public void Dispose()
        {
            _http.Dispose();
        }
    }
}