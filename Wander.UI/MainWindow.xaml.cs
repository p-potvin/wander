using System;
using System.Windows;
using Wander.UI.Theming;

namespace Wander.UI
{
    public partial class MainWindow : Window
    {
        private bool _isDarkTheme = true;
        private bool _isFrench = false;

        public MainWindow()
        {
            InitializeComponent();

            _isDarkTheme = Wander.WindowsAPI.RegistrySettingsManager.IsDarkModeEnabled();

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
            var theme = ThemeManager.GetTheme(_isDarkTheme ? "Golden Slate" : "Solarized Light Revisited");
            WpfThemeApplier.Apply(theme);
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