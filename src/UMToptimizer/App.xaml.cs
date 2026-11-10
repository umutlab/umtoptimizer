using System.Windows;
using UMToptimizer.Core;
using UMToptimizer.UI;

namespace UMToptimizer;

public partial class App : Application
{
    public App()
    {
        // Windows 11 Fluent tema: sistem açık/koyu ayarını izler, Mica arka plan.
        ThemeMode = ThemeMode.System;

        // Son güvenlik ağı: yakalanmamış hiçbir istisna kullanıcıya ham hata penceresi olarak gösterilmez ve uygulamayı kapatmaz.
        DispatcherUnhandledException += (_, e) =>
        {
            e.Handled = true;
            if (MainWindow is { IsLoaded: true, DataContext: MainViewModel vm }) vm.Report(e.Exception);
            else if (_windowShown) CrashLog.Write("Shutdown", e.Exception); // pencere kapanırken: sessizce günlüğe
            else StartupFailed(e.Exception);
        };
        Activated += (_, _) => _windowShown = true;
        TaskScheduler.UnobservedTaskException += (_, e) => { CrashLog.Write("UnobservedTask", e.Exception); e.SetObserved(); };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => { if (e.ExceptionObject is Exception ex) CrashLog.Write("AppDomain", ex); };
    }

    private bool _windowShown;

    /// <summary>Pencere hiç açılamadıysa bildirim gösterecek yer yoktur: tek, anlaşılır bir ileti verilir ve çıkılır.</summary>
    internal static void StartupFailed(Exception ex)
    {
        CrashLog.Write("Startup", ex);
        string where;
        try { where = CrashLog.File; } catch (Exception) { where = "%LOCALAPPDATA%\\UMToptimizer\\logs"; }
        MessageBox.Show($"UMToptimizer başlatılamadı. Lütfen yeniden kurmayı dene.\n\nAyrıntı: {where}", "UMToptimizer", MessageBoxButton.OK, MessageBoxImage.Warning);
        Current?.Shutdown(1);
    }
}
