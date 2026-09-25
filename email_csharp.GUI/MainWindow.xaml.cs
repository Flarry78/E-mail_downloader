using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DotNetEnv;
using MimeKit;
using email_csharp;

namespace email_csharp.GUI
{
    public partial class MainWindow : Window
    {
        private string _zielOrdner;

        public MainWindow()
        {
            InitializeComponent();
            LadeUmgebungsvariablen();

            // Initialisiere WebView2 vorab
            _ = InitializeWebViewAsync();

            // Daten beim Start laden
            LadeUnsortedEmailsInUi();
            LadeFirmenOrdnerInComboBox();
        }

        private async Task InitializeWebViewAsync()
        {
            await EmailWebView.EnsureCoreWebView2Async();
        }

        private void LadeUmgebungsvariablen()
        {
            try { Env.Load(); }
            catch
            {
                try
                {
                    string rootEnv = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..\\..\\..\\..\\.env");
                    if (File.Exists(rootEnv)) { Env.Load(rootEnv); }
                }
                catch { }
            }

            _zielOrdner = Environment.GetEnvironmentVariable("ZIEL_ORDNER");
            if (string.IsNullOrEmpty(_zielOrdner))
            {
                _zielOrdner = AppDomain.CurrentDomain.BaseDirectory;
            }
        }

        // Lädt die unbestätigten E-Mails aus der SQLite-Datenbank in die linke Liste
        public void LadeUnsortedEmailsInUi()
        {
            try
            {
                var emailService = new EmailService();
                var unsortedList = emailService.GetUnsortedEmailsFromDb();
                EmailListView.ItemsSource = unsortedList;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Fehler beim Laden der E-Mails aus der Datenbank: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Liest alle bereits existierenden Firmen-Ordner im Zielordner aus für die Auto-Suggest ComboBox
        private void LadeFirmenOrdnerInComboBox()
        {
            try
            {
                CmbCompany.Items.Clear();
                if (Directory.Exists(_zielOrdner))
                {
                    var ordner = Directory.GetDirectories(_zielOrdner);
                    foreach (var ordnerPfad in ordner)
                    {
                        var ordnerName = Path.GetFileName(ordnerPfad);
                        // "unsorted" ignorieren, da das kein Firmenordner ist
                        if (!ordnerName.Equals("unsorted", StringComparison.OrdinalIgnoreCase))
                        {
                            CmbCompany.Items.Add(ordnerName);
                        }
                    }
                }
            }
            catch (Exception ex) { Console.WriteLine("Fehler beim Einlesen der Ordner: " + ex.Message); }
        }
        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            var settingsWin = new SettingsWindow(this);
            settingsWin.ShowDialog();

            // Nach dem Schließen der Einstellungen neu laden (z. B. falls sich der ZIEL_ORDNER geändert hat)
            LadeUmgebungsvariablen();
            LadeFirmenOrdnerInComboBox();
        }
        private async void BtnStartDownload_Click(object sender, RoutedEventArgs e)
        {
            string imapServer = Environment.GetEnvironmentVariable("IMAP_SERVER");
            string emailKonto = Environment.GetEnvironmentVariable("EMAIL_KONTO");
            string passwort = Environment.GetEnvironmentVariable("PASSWORT");

            if (string.IsNullOrEmpty(imapServer) || string.IsNullOrEmpty(emailKonto) || string.IsNullOrEmpty(passwort))
            {
                MessageBox.Show("Bitte überprüfe deine .env-Datei. Zugangsdaten fehlen!", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            this.IsEnabled = false;
            var progressWindow = new ProgressDialog(this);
            progressWindow.Show();

            var logWriter = new TextBoxWriter(progressWindow.TxtLog);
            TextWriter originalOut = Console.Out;
            Console.SetOut(logWriter);

            try
            {
                progressWindow.AppendLog("Starte Verbindung zum IMAP-Server...\n");

                var emailService = new EmailService();
                var neueEmails = await emailService.FetchNewEmailsAsync(imapServer, 993, emailKonto, passwort, _zielOrdner);

                progressWindow.AppendLog($"\nFertig! {neueEmails.Count} neue E-Mails heruntergeladen.");
            }
            catch (Exception ex)
            {
                progressWindow.AppendLog($"\nFEHLER: {ex.Message}");
            }
            finally
            {
                Console.SetOut(originalOut);
                progressWindow.TaskFinished();
            }

            // Nach dem Download die Ansicht aktualisieren
            LadeUnsortedEmailsInUi();
        }

        // Wenn der Chef links eine E-Mail anklickt
        private async void EmailListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (EmailListView.SelectedItem is EmailPreviewModel selectedEmail)
            {
                // Header-Infos voll ausschreiben
                TxtSubject.Text = $"Betreff: {selectedEmail.Subject}";
                TxtSender.Text = $"Absender: {selectedEmail.Sender}  |  Datum: {selectedEmail.Date:dd.MM.yyyy HH:mm}";

                // E-Mail Inhalt über WebView2 anzeigen (.eml Datei einlesen via MimeKit)
                if (File.Exists(selectedEmail.FilePath))
                {
                    try
                    {
                        var message = await MimeMessage.LoadAsync(selectedEmail.FilePath);
                        string htmlContent = message.HtmlBody;

                        if (string.IsNullOrEmpty(htmlContent))
                        {
                            // Fallback auf Text, wenn kein HTML vorhanden ist (als einfaches HTML formatiert)
                            string textContent = message.TextBody ?? "[Kein Textinhalt]";
                            htmlContent = $"<html><body><pre style='font-family:sans-serif;'>{System.Net.WebUtility.HtmlEncode(textContent)}</pre></body></html>";
                        }

                        if (EmailWebView.CoreWebView2 != null)
                        {
                            EmailWebView.CoreWebView2.NavigateToString(htmlContent);
                        }
                    }
                    catch (Exception ex)
                    {
                        EmailWebView.CoreWebView2?.NavigateToString($"<html><body><h3>Fehler beim Laden der E-Mail-Vorschau:</h3><p>{ex.Message}</p></body></html>");
                    }
                }
            }
        }

        private void BtnAssign_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Zuordnungs-Logik wird im nächsten Schritt aktiviert!", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }


    // ==========================================
    // FORTSCHRITTS-FENSTER (POP-UP)
    // ==========================================
    public class ProgressDialog : Window
    {
        public TextBox TxtLog { get; private set; }
        private Button _btnOk;
        private bool _isFinished = false;

        public ProgressDialog(Window owner)
        {
            Owner = owner;
            Title = "E-Mail Download läuft...";
            Width = 500;
            Height = 350;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;

            Closing += (s, e) =>
            {
                if (!_isFinished)
                {
                    e.Cancel = true;
                }
                else
                {
                    if (Owner is MainWindow mw)
                    {
                        mw.IsEnabled = true;
                        mw.LadeUnsortedEmailsInUi(); // Aktualisiert die Liste im Hauptfenster beim Schließen
                    }
                }
            };

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition() { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });

            TxtLog = new TextBox
            {
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
                Foreground = new SolidColorBrush(Color.FromRgb(0, 255, 128)),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 13,
                Margin = new Thickness(10),
                Padding = new Thickness(5)
            };
            Grid.SetRow(TxtLog, 0);
            grid.Children.Add(TxtLog);

            _btnOk = new Button
            {
                Content = "OK (Bitte warten...)",
                IsEnabled = false,
                Height = 35,
                Margin = new Thickness(10, 0, 10, 10),
                FontWeight = FontWeights.Bold,
                Background = new SolidColorBrush(Color.FromRgb(0, 122, 204)),
                Foreground = Brushes.White
            };
            _btnOk.Click += (s, e) =>
            {
                _isFinished = true;
                if (Owner is MainWindow mw)
                {
                    mw.IsEnabled = true;
                    mw.LadeUnsortedEmailsInUi();
                }
                Close();
            };
            Grid.SetRow(_btnOk, 1);
            grid.Children.Add(_btnOk);

            Content = grid;
        }

        public void AppendLog(string text)
        {
            Dispatcher.Invoke(() =>
            {
                TxtLog.AppendText(text);
                TxtLog.ScrollToEnd();
            });
        }

        public void TaskFinished()
        {
            Dispatcher.Invoke(() =>
            {
                _isFinished = true;
                _btnOk.IsEnabled = true;
                _btnOk.Content = "OK - Schließen";
                Title = "Download abgeschlossen!";
            });
        }
    }


    // ==========================================
    // HILFSKLASSE FÜR KONSOLEN-UMLEITUNG
    // =сон
    // ==========================================
    public class TextBoxWriter : TextWriter
    {
        private readonly TextBox _outputTextBox;

        public TextBoxWriter(TextBox outputTextBox)
        {
            _outputTextBox = outputTextBox;
        }

        public override void Write(char value)
        {
            _outputTextBox.Dispatcher.Invoke(() =>
            {
                _outputTextBox.AppendText(value.ToString());
                _outputTextBox.ScrollToEnd();
            });
        }

        public override void Write(string value)
        {
            if (value != null)
            {
                _outputTextBox.Dispatcher.Invoke(() =>
                {
                    _outputTextBox.AppendText(value);
                    _outputTextBox.ScrollToEnd();
                });
            }
        }

        public override Encoding Encoding => Encoding.UTF8;
    }
}