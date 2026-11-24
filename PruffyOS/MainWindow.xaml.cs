using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace PruﬀyOS;

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

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            try { DragMove(); } catch { }
        }
    }

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
            // Keep the shell alive if a Windows target cannot be opened.
        }
    }

    private void Browser_Click(object sender, RoutedEventArgs e) => Open("https://www.google.com");
    private void Files_Click(object sender, RoutedEventArgs e) => Open("explorer.exe");
    private void Settings_Click(object sender, RoutedEventArgs e) => Open("ms-settings:");
    private void TaskManager_Click(object sender, RoutedEventArgs e) => Open("taskmgr.exe");
    private void Calculator_Click(object sender, RoutedEventArgs e) => Open("calc.exe");
    private void Notepad_Click(object sender, RoutedEventArgs e) => Open("notepad.exe");

    private void Apps_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            "Pruﬀy пока не заменяет меню Windows: он открывает только безопасные системные приложения через обычный Windows shell.",
            "Приложения",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (SearchBox.Text.Trim().Equals("taskmgr", StringComparison.OrdinalIgnoreCase) ||
            SearchBox.Text.Trim().Equals("диспетчер", StringComparison.OrdinalIgnoreCase))
        {
            SearchBox.ToolTip = "Нажми «Диспетчер задач» слева.";
        }
        else
        {
            SearchBox.ToolTip = null;
        }
    }
}