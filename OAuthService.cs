namespace email_csharp
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Identity.Client;

    /// <summary>
    /// Holt OAuth2-Access-Tokens über Microsoft Entra ID, entweder für Microsoft Graph
    /// (nur "Mail.Read": nur lesen, nur das Postfach des angemeldeten Nutzers) oder für IMAP mit OAuth2.
    /// Der Token-Cache (inkl. Refresh-Token) wird mit Windows DPAPI verschlüsselt in msal_cache.bin abgelegt
    /// und ist nur für dieses Windows-Benutzerkonto auf diesem Rechner lesbar.
    /// </summary>
    public static class OAuthService
    {
        // Microsoft Graph: nur lesend
        public static readonly string[] ScopesGraph = new[]
        {
            "https://graph.microsoft.com/Mail.Read"
        };

        // IMAP mit OAuth2 (muss im Postfach von der IT freigeschaltet sein)
        public static readonly string[] ScopesImap = new[]
        {
            "https://outlook.office.com/IMAP.AccessAsUser.All"
        };

        public static string[] ScopesFuer(AppConfig config)
        {
            return config.IstGraph ? ScopesGraph : ScopesImap;
        }

        private static readonly string CachePfad =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "msal_cache.bin");

        private static readonly object CacheLock = new object();
        private static readonly object AppLock = new object();
        private static IPublicClientApplication _app;
        private static string _appSchluessel;

        private static IPublicClientApplication HoleApp(AppConfig config)
        {
            if (string.IsNullOrWhiteSpace(config.OAuthClientId))
            {
                throw new Exception("Für Microsoft Graph fehlt die Client-ID (Anwendungs-ID) aus Azure Entra ID.");
            }

            string tenant = string.IsNullOrWhiteSpace(config.OAuthTenantId) ? "common" : config.OAuthTenantId.Trim();
            string schluessel = config.OAuthClientId.Trim() + "|" + tenant;

            lock (AppLock)
            {
                if (_app != null && _appSchluessel == schluessel)
                {
                    return _app;
                }

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

                _app = app;
                _appSchluessel = schluessel;
                return app;
            }
        }

        /// <summary>
        /// Liefert ein gültiges Access-Token. Zuerst lautlos (Cache / Refresh-Token),
        /// nur wenn nötig (und erlaubt) öffnet sich der Browser zur Anmeldung.
        /// </summary>
        public static async Task<AuthenticationResult> HoleAccessTokenAsync(AppConfig config, bool interaktivErlaubt = true)
        {
            var app = HoleApp(config);
            var scopes = ScopesFuer(config);
            var konten = await app.GetAccountsAsync();

            IAccount konto = konten.FirstOrDefault(k =>
                                 k.Username.Equals(config.EmailKonto ?? "", StringComparison.OrdinalIgnoreCase))
                             ?? konten.FirstOrDefault();

            if (konto != null)
            {
                try
                {
                    return await app.AcquireTokenSilent(scopes, konto).ExecuteAsync();
                }
                catch (MsalUiRequiredException)
                {
                    if (!interaktivErlaubt)
                    {
                        throw new Exception("Die Microsoft-Anmeldung ist abgelaufen. Bitte in den Einstellungen unter 'Anmelden / Testen' neu anmelden.");
                    }
                    Console.WriteLine("[OAuth] Stilles Token nicht möglich, Anmeldung im Browser nötig.");
                }
            }
            else if (!interaktivErlaubt)
            {
                throw new Exception("Es ist noch kein Microsoft-Konto angemeldet. Bitte in den Einstellungen unter 'Anmelden / Testen' anmelden.");
            }

            Console.WriteLine("[OAuth] Öffne Browser zur Microsoft-Anmeldung...");
            using (var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3)))
            {
                var builder = app.AcquireTokenInteractive(scopes);
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
                var app = HoleApp(config);
                foreach (var konto in await app.GetAccountsAsync())
                {
                    await app.RemoveAsync(konto);
                }
            }

            lock (CacheLock)
            {
                if (File.Exists(CachePfad)) File.Delete(CachePfad);
            }

            lock (AppLock)
            {
                _app = null;
                _appSchluessel = null;
            }
        }
    }
}
