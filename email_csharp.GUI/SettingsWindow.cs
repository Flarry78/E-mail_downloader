using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DotNetEnv;

namespace email_csharp.GUI
{
    public class SettingsWindow : Window
    {
        private TextBox _txtZielOrdner;
        private TextBox _txtImapServer;
        private TextBox _txtEmailKonto;
        private TextBox _txtPasswort;
        private string _envPfad;

        public SettingsWindow(Window owner)
        {
            Owner = owner;
            Title = "Einstellungen & Optionen";
            Width = 550;
            Height = 420;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;

            ErmittleEnvPfad();
            ErstelleUiLayout();
            LadeWerteAusEnv();
        }

        private void ErmittleEnvPfad()
        {
            _envPfad = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".env");
            if (!File.Exists(_envPfad))
            {
                string rootEnv = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..\\..\\..\\..\\.env");
                if (File.Exists(rootEnv)) { _envPfad = rootEnv; }
            }
        }

        private void ErstelleUiLayout()
        {
            var grid = new Grid { Margin = new Thickness(20) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 0: Zielordner
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 1: IMAP
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 2: Konto
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 3: Passwort
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Spacer
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 4: Buttons

            int row = 0;

            // --- 1. Zielordner für Firmen ---
            FügeEingabeZeile(grid, row, "Haupt-Zielordner (Firmen & Unsorted):", out _txtZielOrdner, true);
            row++;

            // --- 2. IMAP Server ---
            FügeEingabeZeileHinzuEinfach(grid, row, "IMAP-Server:", out _txtImapServer);
            row++;

            // --- 3. E-Mail Konto ---
            FügeEingabeZeileHinzuEinfach(grid, row, "E-Mail-Konto / Benutzer:", out _txtEmailKonto);
            row++;

            // --- 4. Passwort / App-Key ---
            FügeEingabeZeileHinzuEinfach(grid, row, "Passwort / App-Key:", out _txtPasswort);
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

        private void FügeEingabeZeile(Grid grid, int row, string labelText, out TextBox textBox, bool mitDurchsuchenButton)
        {
            var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };

            var lbl = new TextBlock { Text = labelText, FontWeight = FontWeights.Bold, FontSize = 12, Margin = new Thickness(0, 0, 0, 4) };
            stack.Children.Add(lbl);

            var innerStack = new StackPanel { Orientation = Orientation.Horizontal };

            textBox = new TextBox { Width = mitDurchsuchenButton ? 360 : 480, Height = 28, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(4) };
            innerStack.Children.Add(textBox);

            if (mitDurchsuchenButton)
            {
                var btnBrowse = new Button { Content = "📂 Durchsuchen", Width = 110, Height = 28, Margin = new Thickness(10, 0, 0, 0) };
                btnBrowse.Click += (s, e) => WähleOrdnerAus();
                innerStack.Children.Add(btnBrowse);
            }

            stack.Children.Add(innerStack);
            Grid.SetRow(stack, row);
            grid.Children.Add(stack);
        }

        private void FügeEingabeZeileHinzuEinfach(Grid grid, int row, string labelText, out TextBox textBox)
        {
            FügeEingabeZeile(grid, row, labelText, out textBox, false);
        }

        private void WähleOrdnerAus()
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog();
            dialog.Title = "Wähle den Hauptordner für die Firmen-E-Mails aus";

            if (dialog.ShowDialog() == true)
            {
                _txtZielOrdner.Text = dialog.FolderName;
            }
        }

        private void LadeWerteAusEnv()
        {
            try
            {
                if (File.Exists(_envPfad))
                {
                    Env.Load(_envPfad);
                }
                _txtZielOrdner.Text = Environment.GetEnvironmentVariable("ZIEL_ORDNER") ?? AppDomain.CurrentDomain.BaseDirectory;
                _txtImapServer.Text = Environment.GetEnvironmentVariable("IMAP_SERVER") ?? "";
                _txtEmailKonto.Text = Environment.GetEnvironmentVariable("EMAIL_KONTO") ?? "";
                _txtPasswort.Text = Environment.GetEnvironmentVariable("PASSWORT") ?? "";
            }
            catch (Exception ex)
            {
                MessageBox.Show("Fehler beim Laden der .env: " + ex.Message, "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SpeichereKonfiguration()
        {
            try
            {
                // Wir stellen sicher, dass Backslashes doppelt geschrieben werden, damit sie in der .env nicht als Befehl gelten
                string saubererPfad = _txtZielOrdner.Text.Trim().Replace("\\", "\\\\");

                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"ZIEL_ORDNER={saubererPfad}");
                sb.AppendLine($"IMAP_SERVER={_txtImapServer.Text.Trim()}");
                sb.AppendLine($"EMAIL_KONTO={_txtEmailKonto.Text.Trim()}");
                sb.AppendLine($"PASSWORT={_txtPasswort.Text.Trim()}");
                sb.AppendLine("TAGE_ZURUECK=7");
                sb.AppendLine("NUR_UNGELESENE=true");
                sb.AppendLine("DELAY_MILLISEKUNDEN=1000");

                File.WriteAllText(_envPfad, sb.ToString());

                MessageBox.Show($"Einstellungen erfolgreich gespeichert!\n\nPfad:\n{_envPfad}", "Erfolg", MessageBoxButton.OK, MessageBoxImage.Information);
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Fehler beim Speichern: " + ex.Message, "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}