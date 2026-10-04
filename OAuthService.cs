namespace email_csharp
{
    using Microsoft.Identity.Client;
    using MimeKit.Cryptography;
    using System;
    using System.IO;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Security.Principal;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Holt OAuth2-Access-Tokens für Microsoft (Outlook / Microsoft 365) über Entra ID.
    /// Der Token-Cache (inkl. Refresh-Token) wird verschlüsselt (Windows DPAPI) in msal_cache.bin abgelegt,
    /// dadurch ist nach der ersten Anmeldung kein Browser-Login mehr nötig.
    /// </summary>
    public static class OAuthService
    {
        public static readonly string[] Scopes = new[]
        {
            "https://outlook.office.com/IMAP.AccessAsUser.All"
        };

        private static readonly string CachePfad =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "msal_cache.bin");

        private static readonly object CacheLock = new object();

        private static IPublicClientApplication ErstelleApp(AppConfig config)
        {
            if (string.IsNullOrWhiteSpace(config.OAuthClientId))
            {
                throw new Exception("Für OAuth2 fehlt die Client-ID (Anwendungs-ID) aus Azure Entra ID.");
            }

            string tenant = string.IsNullOrWhiteSpace(config.OAuthTenantId) ? "common" : config.OAuthTenantId.Trim();

            var app = PublicClientApplicationBuilder
                .Create(config.OAuthClientId.Trim())
                .WithAuthority($"https://login.microsoftonline.com/{tenant}")
                .WithRedirectUri("http://localhost")
                .Build();

            app.UserTokenCache.SetBeforeAccess(args =>
            {
                lock (CacheLock)
                {
                    try
                    {
                        if (File.Exists(CachePfad))
                        {
                            byte[] geschuetzt = File.ReadAllBytes(CachePfad);
                            byte[] daten = ProtectedData.Unprotect(geschuetzt, null, DataProtectionScope.CurrentUser);
                            args.TokenCache.DeserializeMsalV3(daten);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[OAuth] Token-Cache konnte nicht gelesen werden: {ex.Message}");
                    }
                }
            });

            app.UserTokenCache.SetAfterAccess(args =>
            {
                if (!args.HasStateChanged) return;
                lock (CacheLock)
                {
                    try
                    {
                        byte[] daten = args.TokenCache.SerializeMsalV3();
                        byte[] geschuetzt = ProtectedData.Protect(daten, null, DataProtectionScope.CurrentUser);
                        File.WriteAllBytes(CachePfad, geschuetzt);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[OAuth] Token-Cache konnte nicht gespeichert werden: {ex.Message}");
                    }
                }
            });

            return app;
        }

        /// <summary>
        /// Liefert ein gültiges Access-Token. Zuerst lautlos (Cache / Refresh-Token),
        /// nur wenn nötig öffnet sich der Browser zur Anmeldung.
        /// </summary>
        public static async Task<AuthenticationResult> HoleAccessTokenAsync(AppConfig config)
        {
            var app = ErstelleApp(config);
            var konten = await app.GetAccountsAsync();

            IAccount konto = konten.FirstOrDefault(k =>
                                 k.Username.Equals(config.EmailKonto ?? "", StringComparison.OrdinalIgnoreCase))
                             ?? konten.FirstOrDefault();

            if (konto != null)
            {
                try
                {
                    Console.WriteLine("[OAuth] Hole Token lautlos aus dem Cache...");
                    return await app.AcquireTokenSilent(Scopes, konto).ExecuteAsync();
                }
                catch (MsalUiRequiredException)
                {
                    Console.WriteLine("[OAuth] Stilles Token nicht möglich, Anmeldung im Browser nötig.");
                }
            }

            Console.WriteLine("[OAuth] Öffne Browser zur Microsoft-Anmeldung...");
            using (var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3)))
            {
                var builder = app.AcquireTokenInteractive(Scopes);
                if (!string.IsNullOrWhiteSpace(config.EmailKonto))
                {
                    builder = builder.WithLoginHint(config.EmailKonto.Trim());
                }
                return await builder.ExecuteAsync(cts.Token);
            }
        }

        /// <summary>
        /// Entfernt alle gespeicherten Anmeldungen und löscht den Token-Cache.
        /// </summary>
        public static async Task AbmeldenAsync(AppConfig config)
        {
            if (!string.IsNullOrWhiteSpace(config.OAuthClientId))
            {
                var app = ErstelleApp(config);
                foreach (var konto in await app.GetAccountsAsync())
                {
                    await app.RemoveAsync(konto);
                }
            }

            lock (CacheLock)
            {
                if (File.Exists(CachePfad)) File.Delete(CachePfad);
            }
        }
    }
}
