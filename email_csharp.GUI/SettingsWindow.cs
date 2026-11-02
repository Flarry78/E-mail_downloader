namespace email_csharp.GUI
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Threading.Tasks;
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Media;
    using email_csharp;

    public class SettingsWindow : Window
    {
        private TextBox _txtZielOrdner;
        private TextBox _txtUnsortedOrdner;
        private TextBox _txtArchivOrdner;
        private TextBox _txtImapServer;
        private TextBox _txtImapPort;
        private TextBox _txtEmailKonto;
        private PasswordBox _pwdPasswort;
        private TextBox _txtTageZurueck;
        private CheckBox _chkNurUngelesene;
        private TextBox _txtDelay;
        private TextBox _txtAnhangEndungen;
        private TextBox _txtGesperrteEndungen;

        // Anmeldeart und OAuth2
        private ComboBox _cmbAnmeldeart;
        private TextBlock _txtAnmeldeHinweis;
        private TextBox _txtOAuthClientId;
        private TextBox _txtOAuthTenantId;
        private Button _btnOAuthLogin;
        private Button _btnOAuthLogout;

        private ListView _lvBlockedSenders;
        private string _configPfad;

        public SettingsWindow(Window owner)
        {
            Owner = owner;
            Title = "Einstellungen & Optionen";
            Width = 600;
            Height = 780;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;

            ErmittleConfigPfad();
            ErstelleUiLayout();
            LadeWerteAusConfig();
            AktualisiereFelder();
            LadeBlockierteAbsender();
        }

        private void ErmittleConfigPfad()
        {
            _configPfad = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
            if (!File.Exists(_configPfad))
            {
                string rootConfig = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..\\..\\..\\..\\config.json");
                if (File.Exists(rootConfig)) { _configPfad = rootConfig; }
            }
        }

        private void ErstelleUiLayout()
        {
            var mainGrid = new Grid { Margin = new Thickness(15) };
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Tabs
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Buttons unten

            // TabControl für Reiter-Struktur
            var tabControl = new TabControl();
            Grid.SetRow(tabControl, 0);

            // --- REITER 1: Allgemeine Einstellungen ---
            var tabSettings = new TabItem { Header = "⚙️ Allgemeine Einstellungen" };

            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var gridSettings = new Grid { Margin = new Thickness(15) };

            for (int i = 0; i < 24; i++)
            {
                gridSettings.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }

            int row = 0;
            FügeEingabeZeile(gridSettings, row++, "Haupt-Zielordner (Firmen):", out _txtZielOrdner, true, WähleZielOrdnerAus);
            FügeEingabeZeile(gridSettings, row++, "Unsorted-Ordner (Eingang):", out _txtUnsortedOrdner, true, WähleUnsortedOrdnerAus);
            FügeEingabeZeile(gridSettings, row++, "Archiv-Ordner (Ablegen):", out _txtArchivOrdner, true, WähleArchivOrdnerAus);

            // --- Anmeldeart: IMAP mit Passwort, IMAP mit OAuth2 oder Microsoft Graph ---
            var stackModus = new StackPanel { Margin = new Thickness(0, 4, 0, 8) };
            stackModus.Children.Add(new TextBlock
            {
                Text = "Anmeldeart:",
                FontWeight = FontWeights.Bold,
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 2)
            });

            _cmbAnmeldeart = new ComboBox { Width = 500, Height = 26, HorizontalAlignment = HorizontalAlignment.Left };
            _cmbAnmeldeart.Items.Add("IMAP mit Passwort / App-Key (z. B. Yahoo)");
            _cmbAnmeldeart.Items.Add("IMAP mit OAuth2 (Microsoft, IMAP muss freigeschaltet sein)");
            _cmbAnmeldeart.Items.Add("Microsoft Graph (Outlook / Microsoft 365, nur lesend)");
            _cmbAnmeldeart.SelectedIndex = 0;
            _cmbAnmeldeart.SelectionChanged += (s, e) => AktualisiereFelder();
            stackModus.Children.Add(_cmbAnmeldeart);

            _txtAnmeldeHinweis = new TextBlock
            {
                FontSize = 11,
                Foreground = Brushes.Gray,
                TextWrapping = TextWrapping.Wrap,
                Width = 500,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 4, 0, 0)
            };
            stackModus.Children.Add(_txtAnmeldeHinweis);

            Grid.SetRow(stackModus, row++);
            gridSettings.Children.Add(stackModus);

            FügeEingabeZeileSimple(gridSettings, row++, "OAuth2 Client-ID (Anwendungs-ID aus Entra ID):", out _txtOAuthClientId);
            FügeEingabeZeileSimple(gridSettings, row++, "OAuth2 Tenant-ID (oder 'common' / 'consumers'):", out _txtOAuthTenantId);

            var stackOAuthButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
            _btnOAuthLogin = new Button { Content = "🔑 Anmelden / Testen", Width = 160, Height = 28 };
            _btnOAuthLogin.Click += BtnOAuthLogin_Click;
            _btnOAuthLogout = new Button { Content = "Abmelden (Token löschen)", Width = 170, Height = 28, Margin = new Thickness(10, 0, 0, 0) };
            _btnOAuthLogout.Click += BtnOAuthLogout_Click;
            stackOAuthButtons.Children.Add(_btnOAuthLogin);
            stackOAuthButtons.Children.Add(_btnOAuthLogout);
            Grid.SetRow(stackOAuthButtons, row++);
            gridSettings.Children.Add(stackOAuthButtons);

            FügeEingabeZeileSimple(gridSettings, row++, "IMAP-Server:", out _txtImapServer);
            FügeEingabeZeileSimple(gridSettings, row++, "IMAP-Port:", out _txtImapPort);
            FügeEingabeZeileSimple(gridSettings, row++, "E-Mail-Konto / Benutzer (bei OAuth2 nur Anmelde-Hinweis):", out _txtEmailKonto);

            // Passwort als PasswordBox (nicht lesbar, wird verschlüsselt gespeichert)
            var stackPw = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            stackPw.Children.Add(new TextBlock
            {
                Text = "Passwort / App-Key (wird verschlüsselt gespeichert):",
                FontWeight = FontWeights.Bold,
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 2)
            });
            _pwdPasswort = new PasswordBox
            {
                Width = 500,
                Height = 26,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(4),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            stackPw.Children.Add(_pwdPasswort);
            Grid.SetRow(stackPw, row++);
            gridSettings.Children.Add(stackPw);

            FügeEingabeZeileSimple(gridSettings, row++, "Erlaubte Anhang-Endungen (Whitelist, mit Komma getrennt):", out _txtAnhangEndungen);
            FügeEingabeZeileSimple(gridSettings, row++, "Immer gesperrte Endungen (Blacklist, hat Vorrang vor der Whitelist):", out _txtGesperrteEndungen);

            var hinweisAnhang = new TextBlock
            {
                Text = "Was nicht in der Whitelist steht, wird nie gespeichert. Die Blacklist ist ein zusätzlicher Schutz. Wer z. B. ZIP-Dateien möchte, trägt 'zip' in die Whitelist ein und nimmt es aus der Blacklist. Blacklist leer = nur die Whitelist gilt. Whitelist leer + Speichern = Standardliste.",
                FontSize = 11,
                Foreground = Brushes.Gray,
                TextWrapping = TextWrapping.Wrap,
                Width = 500,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 10)
            };
            Grid.SetRow(hinweisAnhang, row++);
            gridSettings.Children.Add(hinweisAnhang);
            FügeEingabeZeileSimple(gridSettings, row++, "Tage zurück (Abrufzeitraum):", out _txtTageZurueck);
            FügeEingabeZeileSimple(gridSettings, row++, "Delay zwischen Mails (Millisekunden):", out _txtDelay);

            // Checkbox für NUR_UNGELESENE
            var stackCheck = new StackPanel { Margin = new Thickness(0, 5, 0, 10) };
            _chkNurUngelesene = new CheckBox { Content = "Nur ungelesene E-Mails abrufen", FontWeight = FontWeights.Bold, FontSize = 12 };
            stackCheck.Children.Add(_chkNurUngelesene);
            Grid.SetRow(stackCheck, row++);
            gridSettings.Children.Add(stackCheck);

            scroll.Content = gridSettings;
            tabSettings.Content = scroll;
            tabControl.Items.Add(tabSettings);

            // --- REITER 2: Blacklist / Blockierte Absender ---
            var tabBlacklist = new TabItem { Header = "🚫 Blockierte Absender (Blacklist)" };
            var gridBlacklist = new Grid { Margin = new Thickness(15) };
            gridBlacklist.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            gridBlacklist.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            _lvBlockedSenders = new ListView
            {
                Margin = new Thickness(0, 0, 0, 10),
                Height = 440
            };

            var gv = new GridView();
            gv.Columns.Add(new GridViewColumn { Header = "E-Mail-Adresse", DisplayMemberBinding = new System.Windows.Data.Binding("Email"), Width = 250 });
            gv.Columns.Add(new GridViewColumn { Header = "Name / Absender", DisplayMemberBinding = new System.Windows.Data.Binding("Name"), Width = 230 });
            _lvBlockedSenders.View = gv;
            Grid.SetRow(_lvBlockedSenders, 0);
            gridBlacklist.Children.Add(_lvBlockedSenders);

            var btnUnblock = new Button { Content = "🔓 Ausgewählten Absender entsperren", Width = 240, Height = 30, HorizontalAlignment = HorizontalAlignment.Left };
            btnUnblock.Click += BtnUnblock_Click;
            Grid.SetRow(btnUnblock, 1);
            gridBlacklist.Children.Add(btnUnblock);

            tabBlacklist.Content = gridBlacklist;
            tabControl.Items.Add(tabBlacklist);

            mainGrid.Children.Add(tabControl);

            // --- Globale Buttons Unten (Speichern / Abbrechen) ---
            var stackButtons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 15, 0, 0) };
            Grid.SetRow(stackButtons, 1);

            var btnSave = new Button { Content = "💾 Speichern", Width = 110, Height = 32, Background = new SolidColorBrush(Color.FromRgb(40, 167, 69)), Foreground = Brushes.White, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 10, 0) };
            btnSave.Click += (s, e) => SpeichereKonfiguration();

            var btnCancel = new Button { Content = "Abbrechen", Width = 90, Height = 32 };
            btnCancel.Click += (s, e) => Close();

            stackButtons.Children.Add(btnSave);
            stackButtons.Children.Add(btnCancel);
            mainGrid.Children.Add(stackButtons);

            Content = mainGrid;
        }

        private void FügeEingabeZeile(Grid grid, int row, string labelText, out TextBox textBox, bool mitDurchsuchenButton, RoutedEventHandler browseHandler)
        {
            var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };

            var lbl = new TextBlock { Text = labelText, FontWeight = FontWeights.Bold, FontSize = 12, Margin = new Thickness(0, 0, 0, 2) };
            stack.Children.Add(lbl);

            var innerStack = new StackPanel { Orientation = Orientation.Horizontal };

            textBox = new TextBox { Width = mitDurchsuchenButton ? 380 : 500, Height = 26, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(4) };
            innerStack.Children.Add(textBox);

            if (mitDurchsuchenButton && browseHandler != null)
            {
                var btnBrowse = new Button { Content = "📂 Durchsuchen", Width = 100, Height = 26, Margin = new Thickness(10, 0, 0, 0) };
                btnBrowse.Click += browseHandler;
                innerStack.Children.Add(btnBrowse);
            }

            stack.Children.Add(innerStack);
            Grid.SetRow(stack, row);
            grid.Children.Add(stack);
        }

        private void FügeEingabeZeileSimple(Grid grid, int row, string labelText, out TextBox textBox)
        {
            FügeEingabeZeile(grid, row, labelText, out textBox, false, null);
        }

        private void WähleZielOrdnerAus(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Wähle den Hauptordner für die Firmen-E-Mails aus" };
            if (dialog.ShowDialog() == true) { _txtZielOrdner.Text = dialog.FolderName; }
        }

        private void WähleUnsortedOrdnerAus(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Wähle den Unsorted-Ordner aus" };
            if (dialog.ShowDialog() == true) { _txtUnsortedOrdner.Text = dialog.FolderName; }
        }

        private void WähleArchivOrdnerAus(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Wähle den Archiv-Ordner aus" };
            if (dialog.ShowDialog() == true) { _txtArchivOrdner.Text = dialog.FolderName; }
        }

        /// <summary>
        /// Aktiviert/deaktiviert die Felder je nach gewählter Anmeldeart.
        /// </summary>
        private void AktualisiereFelder()
        {
            if (_cmbAnmeldeart == null || _pwdPasswort == null) return;

            int modus = _cmbAnmeldeart.SelectedIndex; // 0 = IMAP+Passwort, 1 = IMAP+OAuth2, 2 = Graph
            bool oauth = modus == 1 || modus == 2;
            bool imap = modus == 0 || modus == 1;

            _txtOAuthClientId.IsEnabled = oauth;
            _txtOAuthTenantId.IsEnabled = oauth;
            _btnOAuthLogout.IsEnabled = oauth;

            _txtImapServer.IsEnabled = imap;
            _txtImapPort.IsEnabled = imap;
            _pwdPasswort.IsEnabled = modus == 0;

            // Bequemer Standardwert für Outlook / Microsoft 365
            if (modus == 1 && string.IsNullOrWhiteSpace(_txtImapServer.Text))
            {
                _txtImapServer.Text = "outlook.office365.com";
            }

            switch (modus)
            {
                case 1:
                    _txtAnmeldeHinweis.Text = "Anmeldung über Microsoft (OAuth2), Abruf per IMAP. Die IT muss IMAP für das Postfach freischalten. Der Zugriff umfasst auch Schreibrechte.";
                    break;
                case 2:
                    _txtAnmeldeHinweis.Text = "Abruf über Microsoft Graph: nur lesend (Mail.Read), kein IMAP nötig. Je nach Firma muss ein Admin der App einmal zustimmen.";
                    break;
                default:
                    _txtAnmeldeHinweis.Text = "Klassische Anmeldung mit Passwort oder App-Key (z. B. Yahoo). Bei Microsoft-Postfächern nicht möglich.";
                    break;
            }
        }

        private void LadeWerteAusConfig()
        {
            try
            {
                if (File.Exists(_configPfad))
                {
                    string jsonText = File.ReadAllText(_configPfad);
                    var config = JsonSerializer.Deserialize<AppConfig>(jsonText);
                    if (config != null)
                    {
                        _txtZielOrdner.Text = config.ZielOrdner ?? AppDomain.CurrentDomain.BaseDirectory;
                        _txtUnsortedOrdner.Text = config.UnsortedOrdner ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "unsorted");
                        _txtArchivOrdner.Text = config.ArchivOrdner ?? "";
                        _txtImapServer.Text = config.ImapServer ?? "";
                        _txtImapPort.Text = config.ImapPort.ToString();
                        _txtEmailKonto.Text = config.EmailKonto ?? "";
                        _pwdPasswort.Password = config.Passwort ?? "";
                        _cmbAnmeldeart.SelectedIndex = config.IstGraph ? 2 : (config.IstImapOAuth2 ? 1 : 0);
                        _txtOAuthClientId.Text = config.OAuthClientId ?? "";
                        _txtOAuthTenantId.Text = string.IsNullOrWhiteSpace(config.OAuthTenantId) ? "common" : config.OAuthTenantId;
                        _txtAnhangEndungen.Text = string.Join(", ",
                            (config.ErlaubteAnhangEndungen != null && config.ErlaubteAnhangEndungen.Count > 0)
                                ? config.ErlaubteAnhangEndungen
                                : new List<string>(AppConfig.StandardAnhangEndungen));
                        _txtGesperrteEndungen.Text = string.Join(", ", config.GesperrteAnhangEndungen ?? new List<string>());
                        _txtTageZurueck.Text = config.TAGE_ZURUECK.ToString();
                        _chkNurUngelesene.IsChecked = config.NUR_UNGELESENE;
                        _txtDelay.Text = config.DELAY_MILLISEKUNDEN.ToString();
                        return;
                    }
                }

                // Fallback Standardwerte
                _txtZielOrdner.Text = AppDomain.CurrentDomain.BaseDirectory;
                _txtUnsortedOrdner.Text = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "unsorted");
                _txtArchivOrdner.Text = "";
                _txtImapPort.Text = "993";
                _cmbAnmeldeart.SelectedIndex = 0;
                _txtOAuthTenantId.Text = "common";
                _txtAnhangEndungen.Text = string.Join(", ", AppConfig.StandardAnhangEndungen);
                _txtGesperrteEndungen.Text = string.Join(", ", AppConfig.StandardGesperrteEndungen);
                _txtTageZurueck.Text = "7";
                _chkNurUngelesene.IsChecked = false;
                _txtDelay.Text = "1500";
            }
            catch (Exception ex)
            {
                MessageBox.Show("Fehler beim Laden der config.json: " + ex.Message, "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LadeBlockierteAbsender()
        {
            try
            {
                var emailService = new EmailService();
                var blockierteListe = emailService.HoleBlockierteAbsender();
                _lvBlockedSenders.ItemsSource = blockierteListe;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[Fehler beim Laden der Blacklist]: " + ex.Message);
            }
        }

        private void BtnUnblock_Click(object sender, RoutedEventArgs e)
        {
            if (_lvBlockedSenders.SelectedItem is BlockedSenderModel selectedBlocked)
            {
                try
                {
                    var emailService = new EmailService();
                    emailService.AbsenderEntsperren(selectedBlocked.Email);
                    LadeBlockierteAbsender();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Fehler beim Entsperren: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                MessageBox.Show("Bitte wähle zuerst einen Absender aus der Liste aus.", "Hinweis", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// Überträgt die Werte aus den Eingabefeldern in ein Config-Objekt.
        /// </summary>
        private void UebertrageUiInConfig(AppConfig config)
        {
            config.ZielOrdner = _txtZielOrdner.Text.Trim();
            config.UnsortedOrdner = _txtUnsortedOrdner.Text.Trim();
            config.ArchivOrdner = _txtArchivOrdner.Text.Trim();
            config.ImapServer = _txtImapServer.Text.Trim();

            if (int.TryParse(_txtImapPort.Text.Trim(), out int port))
                config.ImapPort = port;

            config.EmailKonto = _txtEmailKonto.Text.Trim();
            config.Passwort = _pwdPasswort.Password.Trim(); // wird beim Setzen verschlüsselt

            config.Anmeldeart = _cmbAnmeldeart.SelectedIndex == 2
                ? AppConfig.ModusGraph
                : (_cmbAnmeldeart.SelectedIndex == 1 ? AppConfig.ModusImapOAuth2 : AppConfig.ModusImapPasswort);
            config.OAuthClientId = _txtOAuthClientId.Text.Trim();
            config.OAuthTenantId = string.IsNullOrWhiteSpace(_txtOAuthTenantId.Text) ? "common" : _txtOAuthTenantId.Text.Trim();

            // Endungen: kommagetrennt, ohne Punkt, klein geschrieben. Leer = Standardliste.
            var endungen = _txtAnhangEndungen.Text
                .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim().TrimStart('.').ToLowerInvariant())
                .Where(x => x.Length > 0)
                .Distinct()
                .ToList();
            config.ErlaubteAnhangEndungen = endungen.Count > 0 ? endungen : new List<string>(AppConfig.StandardAnhangEndungen);

            // Sperrliste: leer bedeutet bewusst "keine zusätzliche Sperre"
            config.GesperrteAnhangEndungen = _txtGesperrteEndungen.Text
                .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim().TrimStart('.').ToLowerInvariant())
                .Where(x => x.Length > 0)
                .Distinct()
                .ToList();

            if (int.TryParse(_txtTageZurueck.Text.Trim(), out int tage))
                config.TAGE_ZURUECK = tage;

            config.NUR_UNGELESENE = _chkNurUngelesene.IsChecked ?? false;

            if (int.TryParse(_txtDelay.Text.Trim(), out int delay))
                config.DELAY_MILLISEKUNDEN = delay;
        }

        private async void BtnOAuthLogin_Click(object sender, RoutedEventArgs e)
        {
            var testConfig = new AppConfig();
            UebertrageUiInConfig(testConfig);

            if (!testConfig.ZugangsdatenVollstaendig(out string fehler))
            {
                MessageBox.Show($"{fehler}\n\nBitte fülle die nötigen Felder aus.", "Hinweis", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _btnOAuthLogin.IsEnabled = false;
            _btnOAuthLogin.Content = "Teste Verbindung...";

            try
            {
                var emailService = new EmailService();
                string ergebnis = await emailService.TesteVerbindungAsync(testConfig);

                string zusatz = testConfig.IstOAuth
                    ? "\n\nDas Token wurde verschlüsselt gespeichert."
                    : string.Empty;

                MessageBox.Show(
                    $"{ergebnis}{zusatz}\n\nVergiss nicht, die Einstellungen mit 'Speichern' zu sichern.",
                    "Verbindungstest", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Anmeldung oder Test fehlgeschlagen:\n{ex.Message}", "Verbindungstest", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _btnOAuthLogin.Content = "🔑 Anmelden / Testen";
                _btnOAuthLogin.IsEnabled = true;
            }
        }

        private async void BtnOAuthLogout_Click(object sender, RoutedEventArgs e)
        {
            var testConfig = new AppConfig();
            UebertrageUiInConfig(testConfig);

            try
            {
                await OAuthService.AbmeldenAsync(testConfig);
                MessageBox.Show("Gespeicherte Anmeldung wurde entfernt.", "Microsoft Graph", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Fehler beim Abmelden:\n{ex.Message}", "Microsoft Graph", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SpeichereKonfiguration()
        {
            try
            {
                AppConfig config = null;
                if (File.Exists(_configPfad))
                {
                    try
                    {
                        string jsonText = File.ReadAllText(_configPfad);
                        config = JsonSerializer.Deserialize<AppConfig>(jsonText);
                    }
                    catch { }
                }

                if (config == null)
                {
                    config = new AppConfig();
                }

                UebertrageUiInConfig(config);

                var options = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(_configPfad, JsonSerializer.Serialize(config, options));

                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Fehler beim Speichern: " + ex.Message, "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
