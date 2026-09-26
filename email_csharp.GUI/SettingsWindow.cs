namespace email_csharp.GUI
{
    using System;
    using System.IO;
    using System.Text.Json;
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Media;
    using email_csharp;

    public class SettingsWindow : Window
    {
        private TextBox _txtZielOrdner;
        private TextBox _txtUnsortedOrdner;
        private TextBox _txtImapServer;
        private TextBox _txtEmailKonto;
        private TextBox _txtPasswort;
        private string _configPfad;

        public SettingsWindow(Window owner)
        {
            Owner = owner;
            Title = "Einstellungen & Optionen";
            Width = 550;
            Height = 480;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;

            ErmittleConfigPfad();
            ErstelleUiLayout();
            LadeWerteAusConfig();
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
            var grid = new Grid { Margin = new Thickness(20) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 0: Zielordner
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 1: Unsorted-Ordner
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 2: IMAP
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 3: Konto
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 4: Passwort
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Spacer
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 5: Buttons

            int row = 0;

            // --- 1. Haupt-Zielordner ---
            FügeEingabeZeile(grid, row, "Haupt-Zielordner (Firmen):", out _txtZielOrdner, true, WähleZielOrdnerAus);
            row++;

            // --- 2. Unsorted-Ordner ---
            FügeEingabeZeile(grid, row, "Unsorted-Ordner (Eingang):", out _txtUnsortedOrdner, true, WähleUnsortedOrdnerAus);
            row++;

            // --- 3. IMAP Server ---
            FügeEingabeZeileSimple(grid, row, "IMAP-Server:", out _txtImapServer);
            row++;

            // --- 4. E-Mail Konto ---
            FügeEingabeZeileSimple(grid, row, "E-Mail-Konto / Benutzer:", out _txtEmailKonto);
            row++;

            // --- 5. Passwort / App-Key ---
            FügeEingabeZeileSimple(grid, row, "Passwort / App-Key:", out _txtPasswort);
            row++;

            // --- Speichern & Abbrechen Buttons ---
            var stackButtons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            Grid.SetRow(stackButtons, 5);

            var btnSave = new Button { Content = "💾 Speichern", Width = 110, Height = 32, Background = new SolidColorBrush(Color.FromRgb(40, 167, 69)), Foreground = Brushes.White, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 10, 0) };
            btnSave.Click += (s, e) => SpeichereKonfiguration();

            var btnCancel = new Button { Content = "Abbrechen", Width = 90, Height = 32 };
            btnCancel.Click += (s, e) => Close();

            stackButtons.Children.Add(btnSave);
            stackButtons.Children.Add(btnCancel);
            grid.Children.Add(stackButtons);

            Content = grid;
        }

        private void FügeEingabeZeile(Grid grid, int row, string labelText, out TextBox textBox, bool mitDurchsuchenButton, RoutedEventHandler browseHandler)
        {
            var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };

            var lbl = new TextBlock { Text = labelText, FontWeight = FontWeights.Bold, FontSize = 12, Margin = new Thickness(0, 0, 0, 4) };
            stack.Children.Add(lbl);

            var innerStack = new StackPanel { Orientation = Orientation.Horizontal };

            textBox = new TextBox { Width = mitDurchsuchenButton ? 360 : 480, Height = 28, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(4) };
            innerStack.Children.Add(textBox);

            if (mitDurchsuchenButton && browseHandler != null)
            {
                var btnBrowse = new Button { Content = "📂 Durchsuchen", Width = 110, Height = 28, Margin = new Thickness(10, 0, 0, 0) };
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
                        _txtImapServer.Text = config.ImapServer ?? "";
                        _txtEmailKonto.Text = config.EmailKonto ?? "";
                        _txtPasswort.Text = config.Passwort ?? "";
                        return;
                    }
                }

                _txtZielOrdner.Text = AppDomain.CurrentDomain.BaseDirectory;
                _txtUnsortedOrdner.Text = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "unsorted");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Fehler beim Laden der config.json: " + ex.Message, "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
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
                    config = new AppConfig { TAGE_ZURUECK = 7, NUR_UNGELESENE = false, DELAY_MILLISEKUNDEN = 1500 };
                }

                config.ZielOrdner = _txtZielOrdner.Text.Trim();
                config.UnsortedOrdner = _txtUnsortedOrdner.Text.Trim();
                config.ImapServer = _txtImapServer.Text.Trim();
                config.EmailKonto = _txtEmailKonto.Text.Trim();
                config.Passwort = _txtPasswort.Text.Trim();

                var options = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(_configPfad, JsonSerializer.Serialize(config, options));

                MessageBox.Show("Einstellungen erfolgreich gespeichert!", "Erfolg", MessageBoxButton.OK, MessageBoxImage.Information);
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Fehler beim Speichern: " + ex.Message, "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}