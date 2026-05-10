using System;
using System.Windows;
using System.Windows.Media;

namespace Wander.UI
{
    public partial class MainWindow : Window
    {
        private bool _isDarkTheme = true;
        private bool _isFrench = false;

        public MainWindow()
        {
            InitializeComponent();
            
            // Read OS default theme from our WindowsAPI library (mocked directly here if not referenced yet)
            // _isDarkTheme = Wander.WindowsAPI.RegistrySettingsManager.IsDarkModeEnabled();
            
            ApplyTheme();
            ApplyLanguage();
        }

        private void ThemeToggle_Click(object sender, RoutedEventArgs e)
        {
            _isDarkTheme = !_isDarkTheme;
            ApplyTheme();
        }

        private void LangToggle_Click(object sender, RoutedEventArgs e)
        {
            _isFrench = !_isFrench;
            ApplyLanguage();
        }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            // In a full implementation, this opens a FolderBrowserDialog
            MessageBox.Show(_isFrench ? "Sélecteur de dossier (à implémenter)" : "Folder picker (to be implemented)", 
                            "VaultWares");
        }

        private void StartSync_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(_isFrench ? "Moteur démarré." : "Engine started.", 
                            "VaultWares");
        }

        private void ApplyTheme()
        {
            var app = Application.Current;
            if (_isDarkTheme)
            {
                app.Resources["BackgroundBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#002B36"));
                app.Resources["SurfaceBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#073642"));
                app.Resources["TextBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FDF6E3"));
            }
            else
            {
                // Light mode (Codex Solar Light Revisited / Paper)
                app.Resources["BackgroundBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FDF6E3"));
                app.Resources["SurfaceBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FDFCF7"));
                app.Resources["TextBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#002B36"));
            }
        }

        private void ApplyLanguage()
        {
            if (_isFrench)
            {
                SettingsTitle.Text = "Paramètres de synchronisation";
                FolderLabel.Text = "Répertoire local :";
                BrowseBtn.Content = "Parcourir...";
                StartSyncBtn.Content = "Démarrer le moteur Wander";
                NetworkLabel.Text = "État du réseau : Vérification...";
            }
            else
            {
                SettingsTitle.Text = "Synchronization Settings";
                FolderLabel.Text = "Local Sync Directory:";
                BrowseBtn.Content = "Browse...";
                StartSyncBtn.Content = "Start Wander Engine";
                NetworkLabel.Text = "Network Status: Checking...";
            }
        }
    }
}