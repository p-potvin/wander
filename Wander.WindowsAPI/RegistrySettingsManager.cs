using System;
using Microsoft.Win32;

namespace Wander.WindowsAPI
{
    public class RegistrySettingsManager
    {
        private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

        public static bool IsDarkModeEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
                if (key != null)
                {
                    var appsUseLightTheme = key.GetValue("AppsUseLightTheme");
                    // 0 = Dark, 1 = Light
                    if (appsUseLightTheme is int value)
                    {
                        return value == 0;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading registry: {ex.Message}");
            }
            return false; // Default to false if unknown
        }

        public static void SetDarkMode(bool enableDarkMode)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(PersonalizeKey);
                if (key != null)
                {
                    int value = enableDarkMode ? 0 : 1;
                    key.SetValue("AppsUseLightTheme", value, RegistryValueKind.DWord);
                    key.SetValue("SystemUsesLightTheme", value, RegistryValueKind.DWord); // Applies to taskbar/start menu
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error writing registry: {ex.Message}");
            }
        }
    }
}
