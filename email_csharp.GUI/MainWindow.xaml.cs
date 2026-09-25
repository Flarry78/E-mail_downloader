using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace email_csharp.GUI
{
    public partial class MainWindow : Window
    {
        private string _unsortedPfad;

        public MainWindow()
        {
            InitializeComponent();

            // Pfad zum unsorted-Ordner ermitteln (gleiches Verzeichnis wie das Backend)
            _unsortedPfad = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "unsorted");

            LadeUnsortedEmails();
        }

        private void LadeUnsortedEmails()
        {
            if (Directory.Exists(_unsortedPfad))
            {
                // Holt alle Unterordner aus "unsorted" (die nach deinem Schema [Datum]_[Hash] benannt sind)
                var ordnerListe = Directory.GetDirectories(_unsortedPfad);

                // Hier kannst du die Liste füllen oder ein kleines Model mappen
                // EmailListView.ItemsSource = ...
            }
        }

        private void EmailListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Wenn der Nutzer eine E-Mail anklickt, liest du die "email.eml" aus dem Ordner
            // und zeigst sie im WebView2 an der rechten Seite an!
        }
    }
}