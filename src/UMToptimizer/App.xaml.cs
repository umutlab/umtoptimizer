using System.Windows;
using System.Windows.Threading;

namespace UMToptimizer;

public partial class App : Application
{
    public App()
    {
        // Windows 11 Fluent tema (Mica arka plan, modern kontroller) — özel şablon yazmaya gerek kalmaz.
        ThemeMode = ThemeMode.System;
        DispatcherUnhandledException += (_, e) =>
        {
            MessageBox.Show(e.Exception.Message, "Beklenmeyen hata", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        };
    }
}
