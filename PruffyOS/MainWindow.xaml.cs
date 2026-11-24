using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;

namespace PruffyOS;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _clockTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    public MainWindow()
    {
        InitializeComponent();
        UpdateClock();
        _clockTimer.Tick += (_, _) => UpdateClock();
        _clockTimer.Start();
    }

    private void UpdateClock() => ClockText.Text = DateTime.Now.ToString("HH:mm");

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        _clockTimer.Stop();
        Close();
    }

    private static void Open(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = target, UseShellExecute = true });
        }
        catch
        {
            // Fail closed: the interface remains running.
        }
    }

    private void Browser_Click(object sender, RoutedEventArgs e) => Open("https://www.google.com");
    private void Files_Click(object sender, RoutedEventArgs e) => Open("explorer.exe");
    private void Settings_Click(object sender, RoutedEventArgs e) => Open("ms-settings:");
    private void Apps_Click(object sender, RoutedEventArgs e) =>
        MessageBox.Show("Раздел приложений будет расширен в следующей версии.", "Pruﬀy OS", MessageBoxButton.OK, MessageBoxImage.Information);
}