namespace email_csharp.GUI
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Text;
    using System.Threading.Tasks;
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Input;
    using System.Windows.Media;
    using MimeKit;
    using Serilog;
    using email_csharp;

    public partial class MainWindow : Window
    {
        private string _zielOrdner;

        public MainWindow()
        {
            LogInitialisierung.InitLogger();
            InitializeComponent();
            LadeKonfiguration();

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

        private void LadeKonfiguration()
        {
            try
            {
                var config = AppConfig.Laden();
                _zielOrdner = config.ZielOrdner;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Fehler beim Laden der config.json: {Message}", ex.Message);
                MessageBox.Show($"Fehler beim Laden der config.json: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            if (string.IsNullOrEmpty(_zielOrdner))
            {
                _zielOrdner = AppDomain.CurrentDomain.BaseDirectory;
            }
        }

        private void BtnShowLogs_Click(object sender, RoutedEventArgs e)
        {
            var logWindow = new Window
            {
                Title = "Anwendungs-Logs (downloader.log)",
                Width = 700,
                Height = 450,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this
            };

            var textBox = new TextBox
            {
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
                Foreground = new SolidColorBrush(Color.FromRgb(0, 255, 128)),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                Padding = new Thickness(5)
            };

            string logPfad = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", "downloader.log");

            if (File.Exists(logPfad))
            {
                try
                {
                    using (var fs = new FileStream(logPfad, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var reader = new StreamReader(fs))
                    {
                        textBox.Text = reader.ReadToEnd();
                    }
                }
                catch (Exception ex)
                {
                    textBox.Text = $"Fehler beim Lesen der Log-Datei: {ex.Message}";
                }
            }
            else
            {
                textBox.Text = $"Keine Log-Datei gefunden unter:\n{logPfad}\n\nEs wurden noch keine Aktionen protokolliert.";
            }

            logWindow.Content = textBox;
            logWindow.ShowDialog();
        }

        public void LadeUnsortedEmailsInUi()
        {
            try
            {
                var emailService = new EmailService();
                var unsortedList = emailService.GetUnsortedEmailsFromDb();

                // Anhänge direkt für jede E-Mail im Dateisystem einsammeln, damit die Badges in der Linken Liste erscheinen
                foreach (var email in unsortedList)
                {
                    email.Attachments = new List<string>();
                    if (!string.IsNullOrEmpty(email.FilePath) && Directory.Exists(email.FilePath))
                    {
                        var dateien = Directory.GetFiles(email.FilePath);
                        foreach (var datei in dateien)
                        {
                            string fileName = Path.GetFileName(datei);
                            if (!fileName.Equals("email.eml", StringComparison.OrdinalIgnoreCase))
                            {
                                email.Attachments.Add(fileName);
                            }
                        }
                    }
                }

                EmailListView.ItemsSource = unsortedList;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Fehler beim Laden der E-Mails aus der Datenbank: {Message}", ex.Message);
                MessageBox.Show($"Fehler beim Laden der E-Mails aus der Datenbank: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

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
                        if (!ordnerName.Equals("unsorted", StringComparison.OrdinalIgnoreCase))
                        {
                            CmbCompany.Items.Add(ordnerName);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Fehler beim Einlesen der Ordner: {Message}", ex.Message);
            }
        }

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            Log.Information("Öffne Einstellungen-Fenster.");
            var settingsWin = new SettingsWindow(this);
            settingsWin.ShowDialog();

            LadeKonfiguration();
            LadeFirmenOrdnerInComboBox();
        }

        private async void BtnStartDownload_Click(object sender, RoutedEventArgs e)
        {
            Log.Information("📥 Benutzer hat 'E-Mails abrufen' angeklickt.");

            string imapServer;
            string emailKonto;
            string passwort;
            string unsortedOrdner;

            try
            {
                var config = AppConfig.Laden();
                imapServer = config.ImapServer;
                emailKonto = config.EmailKonto;
                passwort = config.Passwort;
                _zielOrdner = config.ZielOrdner;
                unsortedOrdner = config.UnsortedOrdner;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Fehler beim Laden der config.json beim Download: {Message}", ex.Message);
                MessageBox.Show($"Fehler beim Laden der config.json: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (string.IsNullOrEmpty(imapServer) || string.IsNullOrEmpty(emailKonto) || string.IsNullOrEmpty(passwort))
            {
                Log.Warning("Download abgebrochen: Zugangsdaten in config.json fehlen.");
                MessageBox.Show("Bitte überprüfe deine config.json. Zugangsdaten fehlen!", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
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
                var neueEmails = await emailService.FetchNewEmailsAsync(imapServer, 993, emailKonto, passwort, unsortedOrdner);

                Log.Information("📥 E-Mail-Download erfolgreich abgeschlossen. {Count} neue E-Mails heruntergeladen.", neueEmails.Count);
                progressWindow.AppendLog($"\nFertig! {neueEmails.Count} neue E-Mails heruntergeladen.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "❌ Fehler beim IMAP-Download: {Message}", ex.Message);
                progressWindow.AppendLog($"\nFEHLER: {ex.Message}");
            }
            finally
            {
                Console.SetOut(originalOut);
                progressWindow.TaskFinished();
            }

            LadeUnsortedEmailsInUi();
        }

        private void Attachment_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Öffnet den Anhang direkt bei einem Doppelklick (oder hier direkt, was sich oft flüssiger anfühlt)
            if (e.ClickCount == 2)
            {
                if (sender is FrameworkElement element && element.DataContext is string selectedFileName)
                {
                    if (EmailListView.SelectedItem is EmailPreviewModel selectedEmail)
                    {
                        string fullPath = Path.Combine(selectedEmail.FilePath, selectedFileName);

                        if (File.Exists(fullPath))
                        {
                            try
                            {
                                Process.Start(new ProcessStartInfo(fullPath) { UseShellExecute = true });
                                Log.Information("Anhang geöffnet: {Path}", fullPath);
                            }
                            catch (Exception ex)
                            {
                                Log.Error(ex, "Fehler beim Öffnen des Anhangs {File}: {Message}", selectedFileName, ex.Message);
                                MessageBox.Show($"Fehler beim Öffnen der Datei: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                            }
                        }
                        else
                        {
                            MessageBox.Show("Die Datei wurde im Dateisystem nicht gefunden.", "Nicht gefunden", MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                    }
                }
            }
        }

        private async void EmailListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (EmailListView.SelectedItem is EmailPreviewModel selectedEmail)
            {
                TxtSubject.Text = $"Betreff: {selectedEmail.Subject}";
                TxtSender.Text = $"Absender: {selectedEmail.Sender}  |  Datum: {selectedEmail.Date:dd.MM.yyyy HH:mm}";

                // (Die untere Anhangs-Listbox `LbAttachments` ist weggefallen, daher wird sie hier nicht mehr befüllt)

                try
                {
                    var emailService = new EmailService();
                    string gespeicherteFirma = emailService.HoleGespeicherteFirmaFuerAbsender(selectedEmail.SenderEmail);

                    if (!string.IsNullOrEmpty(gespeicherteFirma))
                    {
                        CmbCompany.Text = gespeicherteFirma;
                    }
                    else
                    {
                        CmbCompany.Text = string.Empty;
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Fehler beim Laden der Firmenregel für Absender {SenderEmail}: {Message}", selectedEmail.SenderEmail, ex.Message);
                }

                string emlFile = Path.Combine(selectedEmail.FilePath, "email.eml");
                if (File.Exists(emlFile))
                {
                    try
                    {
                        var message = await MimeMessage.LoadAsync(emlFile);

                        string htmlContent = message.HtmlBody;

                        foreach (var part in message.BodyParts)
                        {
                            if (part is MimePart mimePart && !string.IsNullOrEmpty(mimePart.ContentId))
                            {
                                using (var memoryStream = new MemoryStream())
                                {
                                    mimePart.Content.DecodeTo(memoryStream);
                                    byte[] bytes = memoryStream.ToArray();
                                    string base64Data = Convert.ToBase64String(bytes);

                                    string mimeType = mimePart.ContentType.MimeType ?? "image/png";

                                    if (!string.IsNullOrEmpty(htmlContent))
                                    {
                                        htmlContent = htmlContent.Replace($"cid:{mimePart.ContentId}", $"data:{mimeType};base64,{base64Data}");
                                    }
                                }
                            }
                        }

                        if (string.IsNullOrEmpty(htmlContent))
                        {
                            string textContent = message.TextBody ?? "[Kein Textinhalt]";
                            htmlContent = $"<html><body><pre style='font-family:sans-serif;'>{System.Net.WebUtility.HtmlEncode(textContent)}</pre></body></html>";
                        }
                        else
                        {
                            if (!htmlContent.Contains("background-color") && !htmlContent.Contains("background:"))
                            {
                                htmlContent = "<html><head><style>body { background-color: #ffffff !important; color: #000000 !important; }</style></head><body>" + htmlContent + "</body></html>";
                            }
                        }

                        if (EmailWebView.CoreWebView2 != null)
                        {
                            EmailWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                                "emailassets.local",
                                selectedEmail.FilePath,
                                Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind.Allow);

                            EmailWebView.CoreWebView2.NavigateToString(htmlContent);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Fehler beim Rendern der E-Mail-Vorschau: {Message}", ex.Message);
                        EmailWebView.CoreWebView2?.NavigateToString($"<html><body><h3>Fehler beim Laden der E-Mail-Vorschau:</h3><p>{ex.Message}</p></body></html>");
                    }
                }
            }
        }

        private void BtnAssign_Click(object sender, RoutedEventArgs e)
        {
            if (!(EmailListView.SelectedItem is EmailPreviewModel selectedEmail))
            {
                MessageBox.Show("Bitte wähle zuerst eine E-Mail aus der Liste aus.", "Hinweis", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string auftragsnummer = TxtOrderNumber.Text.Trim();
            string firma = CmbCompany.Text.Trim();

            if (string.IsNullOrEmpty(auftragsnummer))
            {
                MessageBox.Show("Bitte gib eine Auftragsnummer ein.", "Fehler", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtOrderNumber.Focus();
                return;
            }

            if (string.IsNullOrEmpty(firma))
            {
                MessageBox.Show("Bitte wähle oder gib einen Firmennamen an.", "Fehler", MessageBoxButton.OK, MessageBoxImage.Warning);
                CmbCompany.Focus();
                return;
            }

            if (!Directory.Exists(selectedEmail.FilePath))
            {
                Log.Warning("Einsortieren fehlgeschlagen: Ordner existiert nicht mehr unter {Path}", selectedEmail.FilePath);
                MessageBox.Show("Der originale E-Mail-Ordner wurde im Dateisystem nicht gefunden.", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                var emailService = new EmailService();
                emailService.OrdnerEinsortierenUndLoeschen(selectedEmail.Id, selectedEmail.UniqueId, selectedEmail.FilePath, auftragsnummer, firma);

                Log.Information("🚀 E-Mail erfolgreich einsortiert: Betreff='{Subject}', Absender='{Sender}', Firma/Ordner='{Firma}', Auftragsnummer='{Order}', Quellpfad='{Path}'",
                    selectedEmail.Subject,
                    selectedEmail.SenderEmail,
                    firma,
                    auftragsnummer,
                    selectedEmail.FilePath);

                LeereDetailAnsichtUndAktualisiere();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "❌ Fehler beim Einsortieren der E-Mail (ID: {Id}): {Message}", selectedEmail.Id, ex.Message);
                MessageBox.Show($"Fehler beim Einsortieren: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnDeleteOnly_Click(object sender, RoutedEventArgs e)
        {
            if (!(EmailListView.SelectedItem is EmailPreviewModel selectedEmail))
            {
                MessageBox.Show("Bitte wähle zuerst eine E-Mail aus der Liste aus, die du löschen möchtest.", "Hinweis", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show($"Möchtest du diese E-Mail von '{selectedEmail.Sender}' wirklich unwiderruflich löschen?",
                "E-Mail löschen", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    var emailService = new EmailService();

                    if (Directory.Exists(selectedEmail.FilePath))
                    {
                        Directory.Delete(selectedEmail.FilePath, true);
                    }

                    emailService.LoescheUnsortedEintrag(selectedEmail.Id);

                    Log.Information("🗑️ E-Mail gelöscht (ohne Einsortieren): Betreff='{Subject}', Absender='{Sender}', Pfad='{Path}'",
                        selectedEmail.Subject,
                        selectedEmail.SenderEmail,
                        selectedEmail.FilePath);

                    LeereDetailAnsichtUndAktualisiere();
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "❌ Fehler beim Löschen der E-Mail (ID: {Id}): {Message}", selectedEmail.Id, ex.Message);
                    MessageBox.Show($"Fehler beim Löschen der E-Mail: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnBlockSender_Click(object sender, RoutedEventArgs e)
        {
            if (!(EmailListView.SelectedItem is EmailPreviewModel selectedEmail))
            {
                MessageBox.Show("Bitte wähle zuerst eine E-Mail aus der Liste aus, deren Absender du blockieren möchtest.", "Hinweis", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show($"Möchtest du den Absender '{selectedEmail.Sender} ({selectedEmail.SenderEmail})' wirklich auf die Blacklist setzen?\n\nZukünftige E-Mails dieses Absenders werden automatisch übersprungen.",
                "Absender blockieren", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    var emailService = new EmailService();

                    emailService.AbsenderBlockieren(selectedEmail.SenderEmail, selectedEmail.Sender);

                    if (Directory.Exists(selectedEmail.FilePath))
                    {
                        Directory.Delete(selectedEmail.FilePath, true);
                    }

                    emailService.LoescheUnsortedEintrag(selectedEmail.Id);

                    Log.Warning("🚫 Absender blockiert und E-Mail entfernt: Absender='{SenderEmail}' ({SenderName}), Betreff='{Subject}'",
                        selectedEmail.SenderEmail,
                        selectedEmail.Sender,
                        selectedEmail.Subject);

                    LeereDetailAnsichtUndAktualisiere();
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "❌ Fehler beim Blockieren des Absenders {SenderEmail}: {Message}", selectedEmail.SenderEmail, ex.Message);
                    MessageBox.Show($"Fehler beim Blockieren des Absenders: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void LeereDetailAnsichtUndAktualisiere()
        {
            LadeUnsortedEmailsInUi();
            LadeFirmenOrdnerInComboBox();
            TxtOrderNumber.Clear();
            CmbCompany.Text = string.Empty;
            TxtSubject.Text = "Betreff: (Keine E-Mail ausgewählt)";
            TxtSender.Text = "Absender: -";
            if (EmailWebView.CoreWebView2 != null)
            {
                EmailWebView.CoreWebView2.NavigateToString("<html><body></body></html>");
            }
        }

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
                            mw.LadeUnsortedEmailsInUi();
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
}