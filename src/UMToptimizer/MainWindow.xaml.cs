using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using Microsoft.Win32;
using UMToptimizer.Core;

namespace UMToptimizer;

public partial class MainWindow : Window
{
    private readonly CancellationTokenSource _life = new();
    private List<AdbDevice> _devices = [];
    private bool _busy;

    private Button[] Actions => [WinApply, AndroidApply, Reboot, RestoreWin, RestoreAndroid, InstallAdb];
    private AdbDevice? Device => Devices.SelectedItem as AdbDevice;

    public MainWindow()
    {
        InitializeComponent();
        WinProfiles.ItemsSource = Profiles.For(Platform.Windows).ToList();
        AndroidProfiles.ItemsSource = Profiles.For(Platform.Android).ToList();
        WinProfiles.SelectedIndex = AndroidProfiles.SelectedIndex = 0;
        Devices.SelectionChanged += (_, _) => Refresh();
        Snapshots.SelectionChanged += (_, _) => UpdateButtons();

        // Klavye: Ctrl+1..4 sayfalar arasında gezinir
        RadioButton[] nav = [NavHome, NavWindows, NavAndroid, NavRestore];
        for (var i = 0; i < nav.Length; i++)
        {
            var target = nav[i];
            InputBindings.Add(new KeyBinding(new Cmd(() => { target.IsChecked = true; target.Focus(); }), Key.D1 + i, ModifierKeys.Control));
        }

        Loaded += (_, _) => { NavHome.Focus(); Refresh(); _ = Adb.WatchAsync(d => Dispatcher.InvokeAsync(() => OnDevices(d)), _life.Token); };
        Closed += (_, _) => _life.Cancel();
    }

    // ---------------- Gezinme ----------------

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (Pages is null) return; // InitializeComponent sırasında
        var page = ((FrameworkElement)sender).Name[3..] + "Page";
        foreach (FrameworkElement p in Pages.Children) p.Visibility = p.Name == page ? Visibility.Visible : Visibility.Collapsed;
        Refresh();
    }

    private void Go_Click(object sender, RoutedEventArgs e) => ((RadioButton)FindName((string)((Button)sender).Tag)).IsChecked = true;

    // ---------------- Durum ----------------

    private void OnDevices(List<AdbDevice> devices)
    {
        var keep = Device?.Serial;
        var isNew = devices.Any(d => d.Ready && !_devices.Any(o => o.Serial == d.Serial && o.Ready));
        _devices = devices;
        Devices.ItemsSource = devices;
        Devices.SelectedItem = devices.FirstOrDefault(d => d.Serial == keep) ?? devices.FirstOrDefault(d => d.Ready) ?? devices.FirstOrDefault();
        if (isNew && NavHome.IsChecked == true) NavAndroid.IsChecked = true; // telefon takılınca Android sayfasına geç
        Refresh();
    }

    /// <summary>Tüm durum metinlerini tek yerden günceller (eski sürümde 6 farklı yerde dağınıktı).</summary>
    private void Refresh()
    {
        var adb = Adb.Path;
        var ready = _devices.Where(d => d.Ready).ToList();
        var waiting = _devices.FirstOrDefault(d => d.State == "unauthorized");

        (AdbStatus.Text, AdbDetail.Text, AdbDot.Fill) =
            adb is null ? ("ADB yok", "Android sayfasından tek tıkla indir.", Brushes.IndianRed) :
            ready.Count > 0 ? ("Cihaz bağlı", ready[0].Model.Replace('_', ' ') is { Length: > 0 } m ? m : ready[0].Serial, Brushes.MediumSeaGreen) :
            waiting is not null ? ("İzin bekleniyor", "Telefondaki USB hata ayıklama iznini onayla.", Brushes.Goldenrod) :
            ("ADB hazır", "Cihaz bekleniyor (USB hata ayıklama açık olmalı).", Brushes.SteelBlue);

        DeviceHint.Text = adb is null
            ? "ADB (Android Platform Tools) bulunamadı. 'ADB'yi indir' resmi Google paketini uygulama klasörüne kurar."
            : _devices.Count == 0 ? "Cihaz yok. Veri aktarabilen bir USB kablosu kullan ve Geliştirici seçeneklerinden USB hata ayıklamayı aç."
            : Device is { Ready: false } d ? $"{d.Serial}: telefon ekranındaki 'USB hata ayıklamaya izin ver' penceresini onayla."
            : $"ADB: {adb}";
        InstallAdb.Visibility = adb is null ? Visibility.Visible : Visibility.Collapsed;

        var winBackup = WindowsTweaks.BackupDate;
        var snaps = Adb.AllSnapshots().ToList();
        HomeWindows.Text = winBackup is { } w ? $"Profil uygulanmış. Yedek: {w:g}" : "Henüz profil uygulanmadı. Standart profil çoğu kullanıcı için önerilir.";
        HomeAdb.Text = adb is null ? "ADB kurulu değil — tek tıkla indirilebilir." : ready.Count > 0 ? $"{ready.Count} cihaz hazır." : "ADB hazır, cihaz bekleniyor.";
        HomeBackups.Text = $"Windows: {(winBackup is null ? "yok" : "var")}  ·  Android: {snaps.Count} cihaz";
        RestoreWinText.Text = winBackup is { } b
            ? $"Yedek tarihi: {b:g}. Kayıt defteri, hizmetler, IPv6, hosts ve güç ayarları geri yüklenir. Kaldırılan uygulamalar ve silinen dosyalar geri gelmez."
            : "Kayıtlı Windows yedeği yok.";
        var keep = (Snapshots.SelectedItem as AndroidSnapshot)?.Serial ?? Device?.Serial;
        Snapshots.ItemsSource = snaps;
        Snapshots.SelectedItem = snaps.FirstOrDefault(s => s.Serial == keep) ?? snaps.FirstOrDefault();
        NoSnapshots.Visibility = snaps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        foreach (var a in Actions) a.IsEnabled = !_busy;
        if (_busy) return;
        AndroidApply.IsEnabled = Reboot.IsEnabled = Device?.Ready == true;
        RestoreWin.IsEnabled = WindowsTweaks.HasBackup;
        RestoreAndroid.IsEnabled = Snapshots.SelectedItem is AndroidSnapshot s && _devices.Any(d => d.Ready && d.Serial == s.Serial);
    }

    // ---------------- Eylemler ----------------

    private async void WinApply_Click(object sender, RoutedEventArgs e)
    {
        if (WinProfiles.SelectedItem is not Profile p || !Confirm($"'{p.Name}' profili uygulansın mı?\n\n{p.Description}\n\nDikkat: {p.Risk}\n\nYönetici izni istenecek.")) return;
        var o = new WindowsOptions(OptRestorePoint.IsChecked == true, OptBitLocker.IsChecked == true);
        await RunAsync($"Windows · {p.Name}", log => WindowsTweaks.RunAsync(p.Source, o, log, _life.Token),
            "Tamamlandı. Bazı ayarlar için Windows'u yeniden başlatın.");
    }

    private async void AndroidApply_Click(object sender, RoutedEventArgs e)
    {
        if (AndroidProfiles.SelectedItem is not Profile p || Device is not { Ready: true } d) return;
        if (!Confirm($"'{p.Name}' profili {d.Display} cihazına uygulansın mı?\n\n{p.Commands.Count:N0} ADB komutu çalışacak.\n\nDikkat: {p.Risk}")) return;
        ApplyResult? r = null;
        await RunAsync($"Android · {p.Name} → {d.Serial}", async log =>
        {
            r = await Adb.ApplyAsync(p, d.Serial, (i, n) => Dispatcher.InvokeAsync(() => { Progress.IsIndeterminate = false; Progress.Maximum = n; Progress.Value = i; }), log, _life.Token);
            log($"\n{r.Total - r.Failed.Count:N0}/{r.Total:N0} komut başarılı · {r.Undoable:N0} ayar yedeklendi\nGünlük: {r.LogPath}");
        }, null);
        if (r is null) return;
        var msg = r.Failed.Count == 0 ? "Tüm komutlar başarıyla uygulandı." : $"{r.Failed.Count:N0} komut cihaz tarafından reddedildi (bu cihaza/Android sürümüne özgü olabilir; normaldir).";
        if (MessageBox.Show(this, msg + "\n\nDeğişikliklerin etkili olması için cihaz şimdi yeniden başlatılsın mı?", "Tamamlandı", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
            await RunAsync("Yeniden başlatılıyor", _ => Adb.RebootAsync(d.Serial), null);
    }

    private async void Reboot_Click(object sender, RoutedEventArgs e)
    {
        if (Device is { Ready: true } d && Confirm($"{d.Display} yeniden başlatılsın mı?"))
            await RunAsync("Yeniden başlatılıyor", _ => Adb.RebootAsync(d.Serial), "Komut gönderildi.");
    }

    private async void RestoreWin_Click(object sender, RoutedEventArgs e)
    {
        if (!Confirm("Windows ayarları yedekteki hâline döndürülsün mü? Yönetici izni istenecek.")) return;
        var reBitLocker = WindowsTweaks.BackupHadBitLocker &&
            MessageBox.Show(this, "BitLocker önceden açıktı. Yeniden etkinleştirilsin mi?", "BitLocker", MessageBoxButton.YesNo) == MessageBoxResult.Yes;
        await RunAsync("Windows · Geri yükleme", log => WindowsTweaks.RunAsync("Restore", new(false, false, reBitLocker), log, _life.Token),
            "Geri yükleme tamamlandı. Windows'u yeniden başlatın.");
    }

    private async void RestoreAndroid_Click(object sender, RoutedEventArgs e)
    {
        if (Snapshots.SelectedItem is not AndroidSnapshot s || !Confirm($"{s.Serial} cihazındaki {s.Values.Count:N0} ayar ilk değerlerine döndürülsün mü?")) return;
        await RunAsync($"Android · Geri yükleme → {s.Serial}", async log => await Adb.RestoreAsync(s.Serial, log, _life.Token),
            "Geri yükleme tamamlandı. Cihazı yeniden başlatman önerilir.");
    }

    private async void InstallAdb_Click(object sender, RoutedEventArgs e)
    {
        await RunAsync("ADB indiriliyor (Google Platform Tools)", async log =>
        {
            await Adb.InstallPlatformToolsAsync(new Progress<double>(v => { Progress.IsIndeterminate = false; Progress.Maximum = 1; Progress.Value = v; }), _life.Token);
            log($"Kuruldu: {Adb.Path}");
        }, null);
    }

    private void PickAdb_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Title = "adb.exe dosyasını seç", Filter = "ADB|adb.exe", FileName = "adb.exe" };
        if (dlg.ShowDialog(this) != true) return;
        try { Adb.SavePath(dlg.FileName); Refresh(); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "ADB", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void OpenLogs_Click(object sender, RoutedEventArgs e) => Store.OpenFolder(Store.Logs);
    private void HideActivity_Click(object sender, RoutedEventArgs e) => Activity.Visibility = Visibility.Collapsed;

    // ---------------- Ortak işlem çalıştırıcı ----------------

    /// <summary>İşlem panelini açar, düğmeleri kilitler, günlüğü akıtır, hatayı tek yerde gösterir.</summary>
    private async Task RunAsync(string title, Func<Action<string>, Task> work, string? doneMessage)
    {
        _busy = true;
        Refresh();
        Activity.Visibility = Visibility.Visible;
        ActivityTitle.Text = title;
        Progress.IsIndeterminate = true;
        TaskbarItemInfo.ProgressState = TaskbarItemProgressState.Indeterminate; // uzun işlemlerde görev çubuğunda da görünür
        Progress.ValueChanged += SyncTaskbar;
        LogBox.Clear();
        void Log(string line) => Dispatcher.InvokeAsync(() => { LogBox.AppendText(line + "\n"); LogBox.ScrollToEnd(); });
        try
        {
            await work(Log);
            ActivityTitle.Text = "✓ " + title;
            if (doneMessage is not null) MessageBox.Show(this, doneMessage, "Tamamlandı", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception) when (_life.IsCancellationRequested) { return; } // pencere kapanıyor: kapalı pencereye mesaj gösterme
        catch (OperationCanceledException ex)
        {
            ActivityTitle.Text = "İptal edildi · " + title;
            Log(ex.Message);
        }
        catch (Exception ex)
        {
            ActivityTitle.Text = "✗ " + title;
            Log("HATA: " + ex.Message);
            MessageBox.Show(this, ex.Message, "İşlem tamamlanamadı", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Progress.ValueChanged -= SyncTaskbar;
            TaskbarItemInfo.ProgressState = TaskbarItemProgressState.None;
            Progress.IsIndeterminate = false;
            Progress.Value = Progress.Maximum;
            _busy = false;
            Refresh();
        }
    }

    private void SyncTaskbar(object? s, RoutedPropertyChangedEventArgs<double> e)
    {
        if (Progress.IsIndeterminate || Progress.Maximum <= 0) return;
        TaskbarItemInfo.ProgressState = TaskbarItemProgressState.Normal;
        TaskbarItemInfo.ProgressValue = Progress.Value / Progress.Maximum;
    }

    // Riskli işlemlerde varsayılan düğme "Hayır": yanlışlıkla Enter'a basmak sistemi değiştirmez.
    private bool Confirm(string text) =>
        MessageBox.Show(this, text, "Onay", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;

    private sealed class Cmd(Action run) : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? p) => true;
        public void Execute(object? p) => run();
    }
}
