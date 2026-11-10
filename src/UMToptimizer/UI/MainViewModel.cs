using System.Diagnostics;
using System.Text;
using System.Windows.Shell;
using UMToptimizer.Core;

namespace UMToptimizer.UI;

public enum NoticeKind { Info, Success, Warning, Error }

/// <summary>Sayfanın üstünde görünen satır içi bildirim (hata penceresi yerine).</summary>
public sealed record Notice(NoticeKind Kind, string Text);

public enum AdbState { Missing, Waiting, Unauthorized, Connected }

/// <summary>Pencere içi onay katmanı isteği. Varsayılan odak her zaman "Vazgeç"tedir.</summary>
public sealed class ConfirmRequest(string title, string text, string yes)
{
    public string Title { get; } = title;
    public string Text { get; } = text;
    public string Yes { get; } = yes;
    internal TaskCompletionSource<bool> Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

/// <summary>Görünüm modelinin dış dünyaya (ADB, PowerShell, disk) açılan tek kapısı; testlerde sahtesiyle değiştirilir.</summary>
public class Backend
{
    public virtual string? LocateAdb() => Adb.Refresh();
    public virtual string? CurrentAdb() => Adb.Path;
    public virtual Task WatchDevicesAsync(Action<IReadOnlyList<AdbDevice>> onChange, CancellationToken ct) => Adb.WatchAsync(onChange, ct);
    public virtual List<AndroidSnapshot> Snapshots() => Android.AllSnapshots();
    public virtual WindowsBackupInfo? WindowsBackup() => WindowsTweaks.ReadBackupInfo();
    public virtual Task RunWindowsAsync(string mode, WindowsOptions o, Action<string> log, CancellationToken ct) => WindowsTweaks.RunAsync(mode, o, log, ct);
    public virtual Task<ApplyResult> ApplyAndroidAsync(Profile p, string serial, Action<int, int> progress, Action<string> log, CancellationToken ct) => Android.ApplyAsync(p, serial, progress, log, ct);
    public virtual Task<int> RestoreAndroidAsync(string serial, Action<string> log, CancellationToken ct) => Android.RestoreAsync(serial, log, ct);
    public virtual Task RebootAsync(string serial, CancellationToken ct) => Adb.RebootAsync(serial, ct);
    public virtual Task InstallAdbAsync(IProgress<double> progress, CancellationToken ct) => Adb.InstallAsync(progress, ct);
    public virtual void SaveAdbPath(string file) => Adb.SavePath(file);
    public virtual void OpenFolder(string path) => Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { path } })?.Dispose();
}

/// <summary>
/// Arayüzün tüm durumu ve eylemleri. Kural: hiçbir eylem dışarıya istisna sızdırmaz. Beklenen durumlar
/// (<see cref="UserException"/>, iptal) sakin bir bildirim olur; beklenmeyenler günlüğe yazılır ve tek satırla bildirilir.
/// </summary>
public sealed class MainViewModel : Observable, IDisposable
{
    private readonly Backend _backend;
    private readonly SynchronizationContext? _ui = SynchronizationContext.Current;
    private readonly CancellationTokenSource _life = new();
    private CancellationTokenSource? _op;

    private readonly StringBuilder _pendingLog = new();
    private double _pendingProgress = -1;

    private string? _adbPath;
    private WindowsBackupInfo? _winBackup;

    public MainViewModel(Backend? backend = null)
    {
        _backend = backend ?? new Backend();
        WindowsProfiles = Profiles.For(Platform.Windows).ToList();
        AndroidProfiles = Profiles.For(Platform.Android).ToList();
        _windowsProfile = WindowsProfiles.FirstOrDefault();
        _androidProfile = AndroidProfiles.FirstOrDefault();

        GoCommand = new(p => { if (int.TryParse(Convert.ToString(p), out var page)) Page = page; });
        ApplyWindowsCommand = new(() => Safe(ApplyWindowsAsync));
        ApplyAndroidCommand = new(() => Safe(ApplyAndroidAsync));
        RebootCommand = new(() => Safe(RebootAsync));
        RestoreWindowsCommand = new(() => Safe(RestoreWindowsAsync));
        RestoreAndroidCommand = new(() => Safe(RestoreAndroidAsync));
        InstallAdbCommand = new(() => Safe(InstallAdbAsync));
        CancelCommand = new(() => { try { _op?.Cancel(); } catch (ObjectDisposedException) { } });
        OpenLogsCommand = new(() => Try(() => _backend.OpenFolder(AppPaths.Logs)));
        HideActivityCommand = new(() => ActivityVisible = false);
        DismissNoticeCommand = new(() => Notice = null);
        ConfirmYesCommand = new(() => Answer(true));
        ConfirmNoCommand = new(() => Answer(false));
        UpdateState();
    }

    // ---------------- Komutlar ----------------

    public Command GoCommand { get; }
    public Command ApplyWindowsCommand { get; }
    public Command ApplyAndroidCommand { get; }
    public Command RebootCommand { get; }
    public Command RestoreWindowsCommand { get; }
    public Command RestoreAndroidCommand { get; }
    public Command InstallAdbCommand { get; }
    public Command CancelCommand { get; }
    public Command OpenLogsCommand { get; }
    public Command HideActivityCommand { get; }
    public Command DismissNoticeCommand { get; }
    public Command ConfirmYesCommand { get; }
    public Command ConfirmNoCommand { get; }

    // ---------------- Gezinme ve seçimler ----------------

    private int _page;
    /// <summary>0 Genel bakış · 1 Windows · 2 Android · 3 Geri yükleme</summary>
    public int Page { get => _page; set => Set(ref _page, Math.Clamp(value, 0, 3)); }

    public IReadOnlyList<Profile> WindowsProfiles { get; }
    public IReadOnlyList<Profile> AndroidProfiles { get; }

    private Profile? _windowsProfile;
    public Profile? WindowsProfile { get => _windowsProfile; set { if (Set(ref _windowsProfile, value)) UpdateState(); } }

    private Profile? _androidProfile;
    public Profile? AndroidProfile { get => _androidProfile; set { if (Set(ref _androidProfile, value)) UpdateState(); } }

    private IReadOnlyList<AdbDevice> _devices = [];
    public IReadOnlyList<AdbDevice> Devices { get => _devices; private set => Set(ref _devices, value); }

    private AdbDevice? _device;
    public AdbDevice? Device { get => _device; set { if (Set(ref _device, value)) UpdateState(); } }

    private IReadOnlyList<AndroidSnapshot> _snapshots = [];
    public IReadOnlyList<AndroidSnapshot> Snapshots { get => _snapshots; private set => Set(ref _snapshots, value); }

    private AndroidSnapshot? _snapshot;
    public AndroidSnapshot? Snapshot { get => _snapshot; set { if (Set(ref _snapshot, value)) UpdateState(); } }

    private bool _restorePoint = true;
    public bool RestorePoint { get => _restorePoint; set => Set(ref _restorePoint, value); }

    private bool _allowBitLocker;
    public bool AllowBitLocker { get => _allowBitLocker; set => Set(ref _allowBitLocker, value); }

    // ---------------- Türetilmiş durum (tek yerden: UpdateState) ----------------

    private AdbState _adbState;
    public AdbState AdbState { get => _adbState; private set => Set(ref _adbState, value); }
    private string _adbTitle = "", _adbDetail = "", _deviceHint = "", _homeWindows = "", _homeAdb = "", _homeBackups = "", _restoreWindowsText = "";
    public string AdbTitle { get => _adbTitle; private set => Set(ref _adbTitle, value); }
    public string AdbDetail { get => _adbDetail; private set => Set(ref _adbDetail, value); }
    public string DeviceHint { get => _deviceHint; private set => Set(ref _deviceHint, value); }
    public string HomeWindows { get => _homeWindows; private set => Set(ref _homeWindows, value); }
    public string HomeAdb { get => _homeAdb; private set => Set(ref _homeAdb, value); }
    public string HomeBackups { get => _homeBackups; private set => Set(ref _homeBackups, value); }
    public string RestoreWindowsText { get => _restoreWindowsText; private set => Set(ref _restoreWindowsText, value); }

    private bool _adbMissing, _hasSnapshots, _canApplyWindows, _canApplyAndroid, _canRestoreWindows, _canRestoreAndroid, _idle = true;
    public bool AdbMissing { get => _adbMissing; private set => Set(ref _adbMissing, value); }
    public bool HasSnapshots { get => _hasSnapshots; private set => Set(ref _hasSnapshots, value); }
    public bool CanApplyWindows { get => _canApplyWindows; private set => Set(ref _canApplyWindows, value); }
    public bool CanApplyAndroid { get => _canApplyAndroid; private set => Set(ref _canApplyAndroid, value); }
    public bool CanRestoreWindows { get => _canRestoreWindows; private set => Set(ref _canRestoreWindows, value); }
    public bool CanRestoreAndroid { get => _canRestoreAndroid; private set => Set(ref _canRestoreAndroid, value); }
    /// <summary>Çalışan bir işlem yok (ADB indirme/seçme gibi her zaman yapılabilen eylemler için).</summary>
    public bool Idle { get => _idle; private set => Set(ref _idle, value); }

    // Devre dışı düğmelerin ipucu: "neden basamıyorum?" sorusunun cevabı. Etkinse null (ipucu görünmez).
    private string? _applyAndroidWhy, _restoreWindowsWhy, _restoreAndroidWhy;
    public string? ApplyAndroidWhy { get => _applyAndroidWhy; private set => Set(ref _applyAndroidWhy, value); }
    public string? RestoreWindowsWhy { get => _restoreWindowsWhy; private set => Set(ref _restoreWindowsWhy, value); }
    public string? RestoreAndroidWhy { get => _restoreAndroidWhy; private set => Set(ref _restoreAndroidWhy, value); }

    // ---------------- İşlem paneli, bildirim, onay ----------------

    private bool _isBusy, _activityVisible, _progressIndeterminate, _canCancel;
    public bool IsBusy { get => _isBusy; private set { if (Set(ref _isBusy, value)) Raise(nameof(TaskbarState)); } }
    public bool ActivityVisible { get => _activityVisible; set => Set(ref _activityVisible, value); }
    public bool ProgressIndeterminate { get => _progressIndeterminate; private set { if (Set(ref _progressIndeterminate, value)) Raise(nameof(TaskbarState)); } }
    public bool CanCancel { get => _canCancel; private set => Set(ref _canCancel, value); }

    private string _activityTitle = "";
    public string ActivityTitle { get => _activityTitle; private set => Set(ref _activityTitle, value); }

    private double _progress;
    /// <summary>0..1</summary>
    public double Progress { get => _progress; private set => Set(ref _progress, value); }

    public TaskbarItemProgressState TaskbarState =>
        !IsBusy ? TaskbarItemProgressState.None : ProgressIndeterminate ? TaskbarItemProgressState.Indeterminate : TaskbarItemProgressState.Normal;

    private Notice? _notice;
    public Notice? Notice { get => _notice; private set => Set(ref _notice, value); }

    private ConfirmRequest? _confirm;
    public ConfirmRequest? Confirm { get => _confirm; private set => Set(ref _confirm, value); }

    /// <summary>Günlüğe eklenecek metin parçası (görünüm TextBox'a ekler). Tick() ile toplu gelir.</summary>
    public event Action<string>? LogAppended;
    public event Action? LogCleared;

    // ---------------- Yaşam döngüsü ----------------

    /// <summary>Arka plan izlemeyi başlatır. Hiçbir disk/süreç işi arayüz iş parçacığında yapılmaz.</summary>
    public void Start()
    {
        _ = ReloadAsync();
        _ = Task.Run(async () =>
        {
            try
            {
                await _backend.WatchDevicesAsync(list =>
                {
                    var adb = _backend.CurrentAdb();
                    Post(() => OnDevices(adb, list));
                }, _life.Token);
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { CrashLog.Write("WatchDevices", e); }
        });
    }

    public void Dispose()
    {
        if (_life.IsCancellationRequested) return;
        _life.Cancel();
        Answer(false);
    }

    /// <summary>Görünümün zamanlayıcısı çağırır: biriken günlük satırlarını ve ilerlemeyi tek seferde arayüze aktarır.</summary>
    public void Tick()
    {
        string? chunk = null;
        double progress;
        lock (_pendingLog)
        {
            if (_pendingLog.Length > 0) { chunk = _pendingLog.ToString(); _pendingLog.Clear(); }
            progress = _pendingProgress;
            _pendingProgress = -1;
        }
        if (chunk is not null) LogAppended?.Invoke(chunk);
        if (progress >= 0) { ProgressIndeterminate = false; Progress = Math.Clamp(progress, 0, 1); }
    }

    /// <summary>Her iş parçacığından çağrılabilir; arayüze Tick() ile toplu yansır (1.500 satır = 1.500 arayüz güncellemesi olmasın).</summary>
    private void Log(string line) { lock (_pendingLog) _pendingLog.Append(line).Append('\n'); }
    private void ReportProgress(double value) { lock (_pendingLog) _pendingProgress = value; }

    private void Post(Action action)
    {
        if (_life.IsCancellationRequested) return;
        if (_ui is null || SynchronizationContext.Current == _ui) action();
        else _ui.Post(_ => { if (!_life.IsCancellationRequested) Try(action); }, null);
    }

    // ---------------- Durum ----------------

    private void OnDevices(string? adb, IReadOnlyList<AdbDevice> list)
    {
        if (adb == _adbPath && list.SequenceEqual(Devices)) return;
        _adbPath = adb;
        var keep = Device?.Serial;
        Devices = list;
        _device = list.FirstOrDefault(d => d.Serial == keep) ?? list.FirstOrDefault(d => d.Ready) ?? list.FirstOrDefault();
        Raise(nameof(Device));
        UpdateState();
    }

    /// <summary>Yedek listesi ve ADB konumu diskten arka planda okunur; yalnızca açılışta ve her işlemden sonra.</summary>
    private async Task ReloadAsync()
    {
        try
        {
            var (snaps, win, adb) = await Task.Run(() => (_backend.Snapshots(), _backend.WindowsBackup(), _backend.LocateAdb()));
            if (_life.IsCancellationRequested) return;
            _winBackup = win;
            _adbPath = adb;
            var keep = Snapshot?.Serial ?? Device?.Serial;
            Snapshots = snaps;
            _snapshot = snaps.FirstOrDefault(s => s.Serial == keep) ?? snaps.FirstOrDefault();
            Raise(nameof(Snapshot));
            UpdateState();
        }
        catch (Exception e) { CrashLog.Write("Reload", e); }
    }

    private void UpdateState()
    {
        var ready = Devices.Where(d => d.Ready).ToList();
        var waiting = Devices.FirstOrDefault(d => d.State == "unauthorized");
        var missing = _adbPath is null;

        (AdbState, AdbTitle, AdbDetail) =
            missing ? (AdbState.Missing, "ADB yok", "Android sayfasından tek tıkla indir.") :
            ready.Count > 0 ? (AdbState.Connected, "Cihaz bağlı", ready[0].Name) :
            waiting is not null ? (AdbState.Unauthorized, "İzin bekleniyor", "Telefondaki USB hata ayıklama iznini onayla.") :
            (AdbState.Waiting, "ADB hazır", "Cihaz bekleniyor (USB hata ayıklama açık olmalı).");
        AdbMissing = missing;

        DeviceHint =
            missing ? "ADB (Android Platform Tools) bulunamadı. \"ADB'yi indir\" resmi Google paketini uygulama klasörüne kurar." :
            Devices.Count == 0 ? "Cihaz yok. Veri aktarabilen bir USB kablosu kullan ve Geliştirici seçeneklerinden USB hata ayıklamayı aç." :
            Device is { Ready: false } d ? $"{d.Serial}: telefon ekranındaki \"USB hata ayıklamaya izin ver\" penceresini onayla." :
            $"ADB: {_adbPath}";

        HomeWindows = _winBackup is { } w ? $"Profil uygulanmış. Yedek: {w.Date:g}" : "Henüz profil uygulanmadı. Standart profil çoğu kullanıcı için önerilir.";
        HomeAdb = missing ? "ADB kurulu değil; tek tıkla indirilebilir." : ready.Count > 0 ? $"{ready.Count} cihaz hazır." : "ADB hazır, cihaz bekleniyor.";
        HomeBackups = $"Windows: {(_winBackup is null ? "yok" : "var")}  ·  Android: {Snapshots.Count} cihaz";
        RestoreWindowsText = _winBackup is { } b
            ? $"Yedek tarihi: {b.Date:g}. Kayıt defteri, hizmetler, IPv6, hosts ve güç ayarları geri yüklenir. Kaldırılan uygulamalar ve silinen dosyalar geri gelmez."
            : "Kayıtlı Windows yedeği yok. İlk profil uygulamasında otomatik oluşturulur.";
        HasSnapshots = Snapshots.Count > 0;

        const string busy = "Çalışan işlemin bitmesini bekle.";
        var idle = !IsBusy;
        Idle = idle;
        CanApplyWindows = idle && WindowsProfile is not null;

        ApplyAndroidWhy = !idle ? busy : missing ? "Önce ADB'yi indir." : Device is null ? "Bağlı cihaz yok." :
            !Device.Ready ? "Telefondaki USB hata ayıklama iznini onayla." : null;
        CanApplyAndroid = ApplyAndroidWhy is null && AndroidProfile is not null;

        RestoreWindowsWhy = !idle ? busy : _winBackup is null ? "Kayıtlı Windows yedeği yok." : null;
        CanRestoreWindows = RestoreWindowsWhy is null;

        RestoreAndroidWhy = !idle ? busy : Snapshot is null ? "Kayıtlı Android yedeği yok." :
            !Devices.Any(x => x.Ready && x.Serial == Snapshot.Serial) ? $"{Snapshot.Serial} bağlı ve yetkili olmalı." : null;
        CanRestoreAndroid = RestoreAndroidWhy is null;
    }

    // ---------------- Eylemler ----------------

    private async Task ApplyWindowsAsync()
    {
        if (!CanApplyWindows || WindowsProfile is not { } p) return;
        if (!await ConfirmAsync($"{p.Name} profili uygulansın mı?", $"{p.Description}\n\nDikkat: {p.Risk}\n\nWindows yönetici izni isteyecek.", "Uygula")) return;
        var options = new WindowsOptions(RestorePoint, AllowBitLocker);
        await RunAsync($"Windows · {p.Name}", cancellable: false, async ct =>
        {
            await _backend.RunWindowsAsync(p.Source, options, Log, ct);
            return new(NoticeKind.Success, "Profil uygulandı. Bazı ayarların etkili olması için Windows'u yeniden başlat.");
        });
    }

    private async Task ApplyAndroidAsync()
    {
        if (!CanApplyAndroid || AndroidProfile is not { } p || Device is not { Ready: true } d) return;
        if (!await ConfirmAsync($"{p.Name} profili uygulansın mı?", $"Cihaz: {d.Name}\n{p.Commands.Count:N0} ADB komutu çalışacak; değişen ayarların ilk değerleri yedeklenir.\n\nDikkat: {p.Risk}", "Uygula")) return;
        var ok = await RunAsync($"Android · {p.Name} → {d.Name}", cancellable: true, async ct =>
        {
            var r = await _backend.ApplyAndroidAsync(p, d.Serial, (i, n) => ReportProgress(n > 0 ? i / (double)n : 1), Log, ct);
            Log($"Günlük: {r.LogPath}");
            var done = r.Total - r.Failed.Count;
            return r.Failed.Count == 0
                ? new(NoticeKind.Success, $"{r.Total:N0} komutun tamamı uygulandı; {r.Undoable:N0} ayar yedeklendi.")
                : new(NoticeKind.Success, $"{done:N0}/{r.Total:N0} komut uygulandı; {r.Undoable:N0} ayar yedeklendi. {r.Failed.Count:N0} komutu bu cihaz desteklemiyor, bu normaldir.");
        });
        if (ok && await ConfirmAsync("Cihaz yeniden başlatılsın mı?", "Bazı ayarlar ancak yeniden başlatmadan sonra etkili olur.", "Yeniden başlat"))
            await RunRebootAsync(d);
    }

    private async Task RebootAsync()
    {
        if (!CanApplyAndroid || Device is not { Ready: true } d) return;
        if (await ConfirmAsync("Cihaz yeniden başlatılsın mı?", d.Name, "Yeniden başlat")) await RunRebootAsync(d);
    }

    private Task<bool> RunRebootAsync(AdbDevice d) => RunAsync($"Yeniden başlatılıyor · {d.Name}", cancellable: false, async ct =>
    {
        await _backend.RebootAsync(d.Serial, ct);
        return new(NoticeKind.Success, "Yeniden başlatma komutu gönderildi.");
    });

    private async Task RestoreWindowsAsync()
    {
        if (!CanRestoreWindows || _winBackup is not { } backup) return;
        if (!await ConfirmAsync("Windows ayarları geri yüklensin mi?", "Ayarlar ilk profil uygulamasından önceki hâline döner. Kaldırılan uygulamalar ve silinen dosyalar geri gelmez.\n\nWindows yönetici izni isteyecek.", "Geri yükle")) return;
        var bitLocker = backup.HadBitLocker &&
            await ConfirmAsync("BitLocker yeniden açılsın mı?", "Profil uygulanmadan önce BitLocker açıktı. Yeniden etkinleştirmek sürücüyü tekrar şifreler.", "Yeniden aç");
        await RunAsync("Windows · Geri yükleme", cancellable: false, async ct =>
        {
            await _backend.RunWindowsAsync("Restore", new(false, false, bitLocker), Log, ct);
            return new(NoticeKind.Success, "Geri yükleme tamamlandı. Windows'u yeniden başlat.");
        });
    }

    private async Task RestoreAndroidAsync()
    {
        if (!CanRestoreAndroid || Snapshot is not { } s) return;
        if (!await ConfirmAsync("Android ayarları geri yüklensin mi?", $"{s.Serial} cihazındaki {s.Values.Count:N0} ayar ilk değerlerine döner.", "Geri yükle")) return;
        await RunAsync($"Android · Geri yükleme → {s.Serial}", cancellable: true, async ct =>
        {
            var failed = await _backend.RestoreAndroidAsync(s.Serial, Log, ct);
            return failed == 0
                ? new(NoticeKind.Success, "Geri yükleme tamamlandı. Cihazı yeniden başlatman önerilir.")
                : new(NoticeKind.Warning, $"{failed:N0} ayar geri yüklenemedi; yedekte duruyorlar, tekrar deneyebilirsin.");
        });
    }

    private Task InstallAdbAsync() => RunAsync("ADB indiriliyor (Google Platform Tools)", cancellable: true, async ct =>
    {
        await _backend.InstallAdbAsync(new InlineProgress(ReportProgress), ct);
        return new(NoticeKind.Success, "ADB kuruldu. Telefonu USB ile bağlayabilirsin.");
    });

    /// <summary>Görünüm dosya seçiciyi gösterir, seçilen yolu buraya verir.</summary>
    public void UseAdb(string file) => Safe(async () =>
    {
        try
        {
            _backend.SaveAdbPath(file);
            Notice = new(NoticeKind.Success, "ADB konumu kaydedildi.");
        }
        catch (UserException e) { Notice = new(NoticeKind.Warning, e.Message); }
        await ReloadAsync();
    });

    // ---------------- Ortak işlem çalıştırıcı ----------------

    /// <summary>İşlem panelini açar, düğmeleri kilitler, sonucu bildirime çevirir. Başarılıysa true döner; asla istisna fırlatmaz.</summary>
    private async Task<bool> RunAsync(string title, bool cancellable, Func<CancellationToken, Task<Notice>> work)
    {
        if (IsBusy || _life.IsCancellationRequested) return false;
        using var op = CancellationTokenSource.CreateLinkedTokenSource(_life.Token);
        _op = op;
        IsBusy = true;
        CanCancel = cancellable;
        ActivityVisible = true;
        ActivityTitle = title;
        Progress = 0;
        ProgressIndeterminate = true;
        Notice = null;
        LogCleared?.Invoke();
        UpdateState();

        Notice result;
        var ok = false;
        try
        {
            // İşin tamamı (süreç başlatma, dosya G/Ç, ayrıştırma, UAC bekleme) arka plan iş parçacığında çalışır;
            // arayüz yalnızca Tick() ile toplu güncellenir ve hiçbir aşamada "Yanıt vermiyor" olmaz.
            result = await Task.Run(() => work(op.Token), op.Token);
            ok = true;
            ActivityTitle = title + " · tamamlandı";
        }
        catch (OperationCanceledException e)
        {
            // Kullanıcı iptali ya da (Türkçe iletili) "yönetici izni verilmedi" türü vazgeçmeler: hata değil, bilgi.
            result = new(NoticeKind.Info, op.IsCancellationRequested ? "İşlem iptal edildi. O ana kadar yapılan değişiklikler yedekli." : e.Message);
            ActivityTitle = title + " · iptal edildi";
        }
        catch (UserException e)
        {
            Log(e.Message);
            result = new(NoticeKind.Warning, e.Message);
            ActivityTitle = title + " · tamamlanamadı";
        }
        catch (Exception e)
        {
            CrashLog.Write(title, e);
            result = new(NoticeKind.Error, "Beklenmeyen bir sorun oluştu ve işlem tamamlanamadı. Ayrıntı günlük klasörüne kaydedildi.");
            ActivityTitle = title + " · tamamlanamadı";
        }
        finally
        {
            _op = null;
            IsBusy = false;
            CanCancel = false;
            ProgressIndeterminate = false;
            if (ok) Progress = 1;
            UpdateState(); // düğmeler, aşağıdaki adımlardan biri başarısız olsa bile yeniden açılır
            try { Tick(); } catch (Exception e) { CrashLog.Write("Tick", e); }
        }
        if (_life.IsCancellationRequested) return false; // pencere kapanıyor
        Notice = result;
        UpdateState();
        await ReloadAsync();
        return ok;
    }

    private Task<bool> ConfirmAsync(string title, string text, string yes)
    {
        if (_life.IsCancellationRequested) return Task.FromResult(false);
        Answer(false); // açık kalmış bir istek varsa kapat
        var request = new ConfirmRequest(title, text, yes);
        Confirm = request;
        return request.Done.Task;
    }

    private void Answer(bool value)
    {
        var request = Confirm;
        if (request is null) return;
        Confirm = null;
        request.Done.TrySetResult(value);
    }

    /// <summary>Komut işleyicilerinin son güvenlik ağı: beklenmeyen istisna kullanıcıya pencere olarak değil, bildirim olarak gider.</summary>
    private async void Safe(Func<Task> action)
    {
        try { await action(); }
        catch (Exception e) { Report(e); }
    }

    private void Try(Action action)
    {
        try { action(); }
        catch (Exception e) { Report(e); }
    }

    /// <summary>Uygulama düzeyindeki yakalanmamış istisnalar da buraya yönlendirilir.</summary>
    public void Report(Exception e)
    {
        CrashLog.Write("UI", e);
        if (!_life.IsCancellationRequested)
            Notice = new(NoticeKind.Error, "Beklenmeyen bir sorun oluştu. Uygulama çalışmaya devam ediyor; ayrıntı günlük klasörüne kaydedildi.");
    }

    /// <summary>Progress&lt;T&gt; SynchronizationContext'e gönderir; burada gerek yok, değer zaten Tick() ile toplanıyor.</summary>
    private sealed class InlineProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}
