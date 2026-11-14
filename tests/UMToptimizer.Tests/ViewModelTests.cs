using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Shell;
using System.Windows.Threading;
using UMToptimizer.Core;
using UMToptimizer.UI;
using Xunit;

namespace UMToptimizer.Tests;

/// <summary>Dış dünyaya dokunmayan sahte arka uç: her işlem testin verdiği davranışı sergiler.</summary>
internal sealed class FakeBackend : Backend
{
    public string? AdbPath = @"C:\fake\adb.exe";
    public List<AdbDevice> DeviceList = [];
    public List<AndroidSnapshot> SnapshotList = [];
    public WindowsBackupInfo? Backup;
    public Func<Task> Windows = () => Task.CompletedTask;
    public Func<Action<int, int>, Action<string>, CancellationToken, Task<ApplyResult>> Apply = (_, _, _) => Task.FromResult(new ApplyResult(3, [], 3, "log"));
    public List<string> Calls = [];

    public override string? LocateAdb() => AdbPath;
    public override string? CurrentAdb() => AdbPath;
    public override async Task WatchDevicesAsync(Action<IReadOnlyList<AdbDevice>> onChange, CancellationToken ct)
    {
        onChange(DeviceList);
        await Task.Delay(Timeout.Infinite, ct);
    }
    public override List<AndroidSnapshot> Snapshots() => SnapshotList;
    public override WindowsBackupInfo? WindowsBackup() => Backup;
    public override Task RunWindowsAsync(string mode, WindowsOptions o, Action<string> log, CancellationToken ct) { Calls.Add("windows:" + mode); log("sahte günlük satırı"); return Windows(); }
    public override Task<ApplyResult> ApplyAndroidAsync(Profile p, string serial, Action<int, int> progress, Action<string> log, CancellationToken ct) { Calls.Add("apply:" + serial); return Apply(progress, log, ct); }
    public override Task<int> RestoreAndroidAsync(string serial, Action<string> log, CancellationToken ct) { Calls.Add("restore:" + serial); return Task.FromResult(0); }
    public override Task RebootAsync(string serial, CancellationToken ct) { Calls.Add("reboot:" + serial); return Task.CompletedTask; }
    public override Task InstallAdbAsync(IProgress<double> progress, CancellationToken ct) { Calls.Add("install"); return Task.CompletedTask; }
    public override void SaveAdbPath(string file) => Calls.Add("save:" + file);
    public override void OpenFolder(string path) => Calls.Add("open");

    public override Task<List<AdbDevice>> ReconnectAsync(Action<string> log, CancellationToken ct) { Calls.Add("reconnect"); return Task.FromResult(DeviceList.ToList()); }

    // Debloat: sahte cihaz paket listesini bellekte tutar; kaldırma/geri getirme listeyi günceller.
    public List<DevicePackage> Packages = [];
    public Dictionary<string, UadEntry> Uad = [];
    public HashSet<string> Rejected = [];
    public override Task<List<DevicePackage>> ScanPackagesAsync(string serial, CancellationToken ct) => Task.FromResult(Packages.ToList());
    public override Task<DebloatResult> DebloatAsync(string serial, IReadOnlyList<string> packages, bool restore, Action<int, int> progress, Action<string> log, CancellationToken ct)
    {
        Calls.Add($"{(restore ? "bring-back" : "remove")}:{string.Join(',', packages)}");
        var done = packages.Where(p => !Rejected.Contains(p)).ToList();
        Packages = Packages.Select(p => done.Contains(p.Name) ? p with { Removed = !restore } : p).ToList();
        return Task.FromResult(new DebloatResult(packages.Count, done, packages.Where(Rejected.Contains).ToList(), ""));
    }
    public override IReadOnlyDictionary<string, DateTimeOffset> DebloatHistory(string serial) => new Dictionary<string, DateTimeOffset>();
    public override UadEntry? DescribePackage(string package) => Uad.GetValueOrDefault(package);
}

public sealed class ViewModelTests
{
    private static readonly AdbDevice Phone = new("R58M12", "device", "SM_A525F");

    private static async Task Until(Func<bool> condition, string what)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), "Zaman aşımı: " + what);
            await Task.Delay(10);
        }
    }

    /// <summary>Onay katmanı açılana kadar bekler, sonra cevaplar.</summary>
    private static async Task Answer(MainViewModel vm, bool yes)
    {
        await Until(() => vm.Confirm is not null, "onay katmanı");
        (yes ? vm.ConfirmYesCommand : vm.ConfirmNoCommand).Execute(null);
    }

    private static async Task<MainViewModel> Started(FakeBackend backend)
    {
        var vm = new MainViewModel(backend);
        vm.Start();
        await Until(() => vm.Devices.Count == backend.DeviceList.Count && vm.Snapshots.Count == backend.SnapshotList.Count && vm.AdbMissing == (backend.AdbPath is null), "ilk durum");
        await Task.Delay(50); // listeler atandıktan sonra türetilmiş durumun (UpdateState) da tamamlanması için
        return vm;
    }

    /// <summary>
    /// Ekran görüntüsündeki hatanın sınıfı: işlem sırasında beklenmeyen bir istisna (NullReferenceException) çıkarsa
    /// dışarı sızmamalı, düğmeler kilitli kalmamalı, kullanıcı ham .NET iletisi görmemeli.
    /// </summary>
    [Fact]
    public async Task UnexpectedException_BecomesCalmNotice_AndUnlocksButtons()
    {
        var backend = new FakeBackend { Windows = () => throw new NullReferenceException("Object reference not set to an instance of an object.") };
        using var vm = await Started(backend);

        vm.ApplyWindowsCommand.Execute(null);
        await Answer(vm, yes: true);
        await Until(() => vm.Notice is not null, "bildirim");

        Assert.Equal(NoticeKind.Error, vm.Notice!.Kind);
        Assert.DoesNotContain("Object reference", vm.Notice.Text);
        Assert.False(vm.IsBusy);
        Assert.True(vm.CanApplyWindows);
        Assert.Equal(TaskbarItemProgressState.None, vm.TaskbarState);
        Assert.Contains("NullReferenceException", File.ReadAllText(CrashLog.File)); // ayrıntı günlükte
    }

    [Fact]
    public async Task UserException_IsShownAsWarning_WithItsOwnMessage()
    {
        var backend = new FakeBackend { Windows = () => throw new UserException("Windows PowerShell bulunamadı.") };
        using var vm = await Started(backend);

        vm.ApplyWindowsCommand.Execute(null);
        await Answer(vm, yes: true);
        await Until(() => vm.Notice is not null, "bildirim");

        Assert.Equal(new Notice(NoticeKind.Warning, "Windows PowerShell bulunamadı."), vm.Notice);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task DeclinedUac_IsInformation_NotAnError()
    {
        var backend = new FakeBackend { Windows = () => throw new OperationCanceledException("Yönetici izni verilmedi; hiçbir değişiklik yapılmadı.") };
        using var vm = await Started(backend);

        vm.ApplyWindowsCommand.Execute(null);
        await Answer(vm, yes: true);
        await Until(() => vm.Notice is not null, "bildirim");

        Assert.Equal(new Notice(NoticeKind.Info, "Yönetici izni verilmedi; hiçbir değişiklik yapılmadı."), vm.Notice);
    }

    [Fact]
    public async Task Success_ShowsSuccessNotice_AndPassesOptions()
    {
        var backend = new FakeBackend();
        using var vm = await Started(backend);
        vm.WindowsProfile = vm.WindowsProfiles.Single(p => p.Id == "aggressive");

        vm.ApplyWindowsCommand.Execute(null);
        await Answer(vm, yes: true);
        await Until(() => vm.Notice is not null, "bildirim");

        Assert.Equal(NoticeKind.Success, vm.Notice!.Kind);
        Assert.Equal(["windows:Aggressive"], backend.Calls);
        Assert.Equal(1, vm.Progress);
    }

    [Fact]
    public async Task DecliningConfirmation_ChangesNothing()
    {
        var backend = new FakeBackend();
        using var vm = await Started(backend);

        vm.ApplyWindowsCommand.Execute(null);
        await Answer(vm, yes: false);
        await Task.Delay(50);

        Assert.Empty(backend.Calls);
        Assert.Null(vm.Notice);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task AndroidApply_IsDisabledWithReason_UntilDeviceIsReady()
    {
        using (var noAdb = await Started(new FakeBackend { AdbPath = null }))
        {
            Assert.False(noAdb.CanApplyAndroid);
            Assert.Equal("Önce ADB'yi indir.", noAdb.ApplyAndroidWhy);
            Assert.Equal(AdbState.Missing, noAdb.AdbState);
            noAdb.ApplyAndroidCommand.Execute(null); // kapalı düğmenin komutu çağrılsa bile hiçbir şey olmaz
            Assert.Null(noAdb.Confirm);
        }
        using (var waiting = await Started(new FakeBackend { DeviceList = [new("X1", "unauthorized", "")] }))
        {
            Assert.False(waiting.CanApplyAndroid);
            Assert.Equal(AdbState.Unauthorized, waiting.AdbState);
            Assert.NotNull(waiting.ApplyAndroidWhy);
        }
        using (var ready = await Started(new FakeBackend { DeviceList = [Phone] }))
        {
            Assert.True(ready.CanApplyAndroid);
            Assert.Null(ready.ApplyAndroidWhy);
            Assert.Equal(AdbState.Connected, ready.AdbState);
        }
    }

    [Fact]
    public async Task AndroidApply_BatchesLogAndProgress_ThenOffersReboot()
    {
        var release = new TaskCompletionSource();
        var backend = new FakeBackend
        {
            DeviceList = [Phone],
            Apply = async (progress, log, _) =>
            {
                for (var i = 1; i <= 500; i++) { log("satır " + i); progress(i, 1000); }
                await release.Task;
                return new ApplyResult(1000, ["cmd power set-mode 1"], 990, "log");
            },
        };
        using var vm = await Started(backend);
        var chunks = new List<string>();
        vm.LogAppended += chunks.Add;

        vm.ApplyAndroidCommand.Execute(null);
        await Answer(vm, yes: true);
        await Until(() => backend.Calls.Count == 1, "uygulama başladı");
        Assert.True(vm.IsBusy);
        Assert.True(vm.CanCancel);
        Assert.False(vm.CanApplyWindows); // işlem sürerken diğer eylemler kilitli

        vm.Tick();
        Assert.Single(chunks);            // 500 satır tek arayüz güncellemesi
        Assert.Equal(0.5, vm.Progress);
        Assert.Equal(TaskbarItemProgressState.Normal, vm.TaskbarState);

        release.SetResult();
        await Answer(vm, yes: true);      // "yeniden başlatılsın mı?"
        await Until(() => backend.Calls.Contains("reboot:R58M12") && !vm.IsBusy, "yeniden başlatma");
        Assert.Equal(NoticeKind.Success, vm.Notice!.Kind);
    }

    [Fact]
    public async Task Cancel_StopsOperation_AndReportsInfo()
    {
        var backend = new FakeBackend
        {
            DeviceList = [Phone],
            Apply = async (_, _, ct) => { await Task.Delay(Timeout.Infinite, ct); return null!; },
        };
        using var vm = await Started(backend);

        vm.ApplyAndroidCommand.Execute(null);
        await Answer(vm, yes: true);
        await Until(() => vm.IsBusy, "çalışıyor");
        vm.CancelCommand.Execute(null);
        await Until(() => vm.Notice is not null, "bildirim");

        Assert.Equal(NoticeKind.Info, vm.Notice!.Kind);
        Assert.False(vm.IsBusy);
        Assert.Null(vm.Confirm); // iptal edilen işlemden sonra yeniden başlatma sorulmaz
    }

    [Fact]
    public async Task Restore_RequiresBackup_AndMatchingDevice()
    {
        var snapshot = new AndroidSnapshot { Serial = "R58M12", Profiles = ["Balanced"] };
        using (var empty = await Started(new FakeBackend()))
        {
            Assert.False(empty.CanRestoreWindows);
            Assert.False(empty.CanRestoreAndroid);
            Assert.False(empty.HasSnapshots);
        }
        using (var unplugged = await Started(new FakeBackend { SnapshotList = [snapshot], Backup = new(DateTime.Now, false) }))
        {
            Assert.True(unplugged.CanRestoreWindows);
            Assert.False(unplugged.CanRestoreAndroid);
            Assert.Contains("R58M12", unplugged.RestoreAndroidWhy);
        }
        var backend = new FakeBackend { SnapshotList = [snapshot], DeviceList = [Phone] };
        using var vm = await Started(backend);
        Assert.True(vm.CanRestoreAndroid);
        vm.RestoreAndroidCommand.Execute(null);
        await Answer(vm, yes: true);
        await Until(() => vm.Notice is not null, "bildirim");
        Assert.Equal(["restore:R58M12"], backend.Calls);
    }

    /// <summary>İzleyici yeni takılan cihazı bildirmese bile "Cihazı bağla" onu bulur ve düğmeleri açar.</summary>
    [Fact]
    public async Task ConnectDevice_FindsPhoneTheWatcherMissed()
    {
        var backend = new FakeBackend();
        using var vm = await Started(backend);
        Assert.False(vm.CanApplyAndroid);

        vm.ConnectDeviceCommand.Execute(null);
        await Until(() => vm.Notice is not null && !vm.IsBusy, "cihaz yok bildirimi");
        Assert.Equal(NoticeKind.Warning, vm.Notice!.Kind);
        Assert.Contains("USB hata ayıklama", vm.Notice.Text);

        backend.DeviceList = [new("R58M12", "unauthorized", "")];
        vm.ConnectDeviceCommand.Execute(null);
        await Until(() => vm.AdbState == AdbState.Unauthorized && !vm.IsBusy, "izin bekleniyor");
        Assert.Contains("izin", vm.Notice!.Text);

        backend.DeviceList = [Phone]; // izleyici bunu hiç bildirmedi
        vm.ConnectDeviceCommand.Execute(null);
        await Until(() => vm.AdbState == AdbState.Connected && !vm.IsBusy, "cihaz bağlandı");
        Assert.Equal(new Notice(NoticeKind.Success, "SM A525F bağlandı."), vm.Notice);
        Assert.Equal(Phone, vm.Device);
        Assert.True(vm.CanApplyAndroid);
        Assert.Equal(["reconnect", "reconnect", "reconnect"], backend.Calls);
    }

    [Fact]
    public void GoCommand_ClampsPage()
    {
        using var vm = new MainViewModel(new FakeBackend());
        vm.GoCommand.Execute("2");
        Assert.Equal(2, vm.Page);
        vm.GoCommand.Execute("99");
        Assert.Equal(4, vm.Page);
        vm.GoCommand.Execute("çöp");
        Assert.Equal(4, vm.Page);
    }
}

public sealed class DebloatViewModelTests
{
    private static readonly AdbDevice Phone = new("R58M12", "device", "SM_A525F");

    private static UadEntry Entry(Removal level, params string[] neededBy) => new("Oem", "Test açıklaması.\nİkinci satır.", level, [], neededBy);

    private static FakeBackend Device() => new()
    {
        DeviceList = [Phone],
        Packages = [new("com.oem.bloat", false), new("com.oem.core", false), new("com.oem.gone", true), new("com.oem.mystery", false), new("com.oem.tips", false)],
        Uad = new()
        {
            ["com.oem.bloat"] = Entry(Removal.Recommended),
            ["com.oem.tips"] = Entry(Removal.Recommended),
            ["com.oem.core"] = Entry(Removal.Unsafe),
            ["com.oem.gone"] = Entry(Removal.Advanced),
        },
    };

    private static async Task Until(Func<bool> condition, string what)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), "Zaman aşımı: " + what);
            await Task.Delay(10);
        }
    }

    private static async Task<MainViewModel> Scanned(FakeBackend backend)
    {
        var vm = new MainViewModel(backend);
        vm.Start();
        await Until(() => vm.CanScan, "cihaz hazır");
        vm.ScanPackagesCommand.Execute(null);
        await Until(() => vm.DebloatScanned && !vm.IsBusy, "tarama");
        return vm;
    }

    private static async Task Confirm(MainViewModel vm, bool yes)
    {
        await Until(() => vm.Confirm is not null, "onay katmanı");
        (yes ? vm.ConfirmYesCommand : vm.ConfirmNoCommand).Execute(null);
    }

    [Fact]
    public async Task Scan_DefaultsToRecommended_AndSeparatesRemoved()
    {
        using var vm = await Scanned(Device());

        Assert.Equal(["com.oem.bloat", "com.oem.tips"], vm.DebloatRows.Select(r => r.Name)); // varsayılan süzgeç: Önerilen
        Assert.Equal("Test açıklaması.", vm.DebloatRows[0].Short);
        Assert.False(vm.CanDebloat); // hiçbir şey işaretli değil
        Assert.NotNull(vm.DebloatWhy);

        vm.DebloatLevel = vm.DebloatLevels.Single(l => l.Level is null);
        Assert.Equal(4, vm.DebloatRows.Count);
        Assert.Equal(Removal.Unlisted, vm.DebloatRows.Single(r => r.Name == "com.oem.mystery").Level);

        vm.DebloatView = 1;
        Assert.Equal(["com.oem.gone"], vm.DebloatRows.Select(r => r.Name)); // düzey süzgeci geri getirme görünümünü daraltmaz

        vm.DebloatView = 0;
        vm.DebloatSearch = "TIPS";
        Assert.Equal(["com.oem.tips"], vm.DebloatRows.Select(r => r.Name));
    }

    [Fact]
    public async Task SelectVisible_NeverPicksUnsafeOrUnlisted()
    {
        using var vm = await Scanned(Device());
        vm.DebloatLevel = vm.DebloatLevels.Single(l => l.Level is null);

        vm.SelectVisibleCommand.Execute(null);

        Assert.Equal(["com.oem.bloat", "com.oem.tips"], vm.DebloatRows.Where(r => r.IsSelected).Select(r => r.Name));
        Assert.True(vm.CanDebloat);
        vm.ClearSelectionCommand.Execute(null);
        Assert.False(vm.CanDebloat);
    }

    [Fact]
    public async Task Remove_ThenRestore_RoundTrips()
    {
        var backend = Device();
        using var vm = await Scanned(backend);

        vm.DebloatRows.Single(r => r.Name == "com.oem.bloat").IsSelected = true;
        vm.DebloatCommand.Execute(null);
        await Until(() => vm.Confirm is not null, "onay katmanı");
        Assert.DoesNotContain("Dikkat", vm.Confirm!.Text); // önerilen paket için korkutucu uyarı yok
        await Confirm(vm, yes: true);
        await Until(() => vm.Notice is not null && vm.DebloatRows.Count == 1, "kaldırma"); // liste, bildirimden hemen sonra yenilenir

        Assert.Equal(["remove:com.oem.bloat"], backend.Calls);
        Assert.Equal(NoticeKind.Success, vm.Notice!.Kind);
        Assert.Equal(["com.oem.tips"], vm.DebloatRows.Select(r => r.Name)); // liste cihazın yeni durumunu gösterir

        vm.DebloatView = 1;
        Assert.Contains(vm.DebloatRows, r => r.Name == "com.oem.bloat");
        vm.DebloatRows.Single(r => r.Name == "com.oem.bloat").IsSelected = true;
        vm.DebloatCommand.Execute(null);
        await Confirm(vm, yes: true);
        await Until(() => backend.Calls.Count == 2 && vm.DebloatRows.All(r => r.Name != "com.oem.bloat"), "geri getirme");

        Assert.Equal("bring-back:com.oem.bloat", backend.Calls[1]);
        Assert.DoesNotContain(vm.DebloatRows, r => r.Name == "com.oem.bloat");
    }

    [Fact]
    public async Task RiskyRemoval_WarnsLoudly_AndDecliningChangesNothing()
    {
        var backend = Device();
        using var vm = await Scanned(backend);
        vm.DebloatLevel = vm.DebloatLevels.Single(l => l.Level == Removal.Unsafe);
        vm.DebloatRows.Single().IsSelected = true;

        vm.DebloatCommand.Execute(null);
        await Until(() => vm.Confirm is not null, "onay katmanı");
        Assert.Contains("Dikkat", vm.Confirm!.Text);
        Assert.Contains("Güvensiz", vm.Confirm.Text);
        await Confirm(vm, yes: false);
        await Task.Delay(50);

        Assert.Empty(backend.Calls);
    }

    [Fact]
    public async Task RejectedPackages_AreReportedAsWarning()
    {
        var backend = Device();
        backend.Rejected.Add("com.oem.tips");
        using var vm = await Scanned(backend);
        vm.SelectVisibleCommand.Execute(null);

        vm.DebloatCommand.Execute(null);
        await Confirm(vm, yes: true);
        await Until(() => vm.Notice is not null && vm.DebloatRows.Count == 1, "kaldırma");

        Assert.Equal(NoticeKind.Warning, vm.Notice!.Kind);
        Assert.Contains("1/2", vm.Notice.Text);
        Assert.Equal(["com.oem.tips"], vm.DebloatRows.Select(r => r.Name));
    }

    [Fact]
    public async Task UnpluggingDevice_ClearsStaleList()
    {
        using var vm = await Scanned(Device());
        vm.Device = null;
        Assert.False(vm.DebloatScanned);
        Assert.Empty(vm.DebloatRows);
        Assert.False(vm.CanScan);
    }
}

public sealed class WindowSmokeTests
{
    /// <summary>
    /// Gerçek pencereyi açar (XAML ayrıştırma, kaynak anahtarları, bağlamalar). Eski sürümdeki hatanın doğrudan
    /// regresyon testi: TaskbarItemInfo tanımlı olmalı; aksi hâlde her işlem NullReferenceException ile düşüyordu.
    /// </summary>
    [Fact]
    public void MainWindow_Opens_WithTaskbarInfo_AndNoDispatcherErrors()
    {
        Exception? failure = null;
        var errors = new List<Exception>();
        var thread = new Thread(() =>
        {
            try
            {
                var app = new App();
                app.InitializeComponent();
                app.DispatcherUnhandledException += (_, e) => { errors.Add(e.Exception); e.Handled = true; };
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
                var backend = new FakeBackend
                {
                    Windows = () => Task.Delay(400),
                    DeviceList = [new("R58M12", "device", "SM_A525F")],
                    Packages = [new("com.oem.bloat", false), new("com.oem.core", false), new("com.oem.gone", true)],
                    Uad = new()
                    {
                        ["com.oem.bloat"] = new("Oem", "Açıklama.", Removal.Recommended, ["com.oem.core"], []),
                        ["com.oem.core"] = new("Aosp", "Çekirdek.", Removal.Recommended, [], ["com.oem.bloat"]),
                    },
                };
                var window = new MainWindow(new MainViewModel(backend));
                window.Show();
                Assert.NotNull(window.TaskbarItemInfo);
                // XAML'de kullanılan tema anahtarları gerçekten var mı? (Eksik anahtar sessizce saydam/renksiz çizilir.)
                string[] keys =
                [
                    "CardBackgroundFillColorDefaultBrush", "CardStrokeColorDefaultBrush", "TextFillColorPrimaryBrush", "TextFillColorSecondaryBrush",
                    "SubtleFillColorSecondaryBrush", "AccentFillColorDefaultBrush", "AccentTextFillColorPrimaryBrush", "AccentButtonStyle",
                    "SystemFillColorSuccessBrush", "SystemFillColorCautionBrush", "SystemFillColorCriticalBrush", "SystemFillColorAttentionBrush",
                    "SystemFillColorSuccessBackgroundBrush", "SystemFillColorCautionBackgroundBrush", "SystemFillColorCriticalBackgroundBrush",
                    "SystemFillColorAttentionBackgroundBrush", "SolidBackgroundFillColorBaseBrush", "SmokeFillColorDefaultBrush", "SurfaceStrokeColorDefaultBrush",
                ];
                Assert.DoesNotContain(keys, k => window.TryFindResource(k) is null);
                var vm = Assert.IsType<MainViewModel>(window.DataContext);
                for (var page = 0; page <= 4; page++) { vm.Page = page; Pump(); } // her sayfa bir kez çizilsin
                Assert.Equal(TaskbarItemProgressState.None, window.TaskbarItemInfo.ProgressState);

                // Kullanıcının bildirdiği akışın aynısı: Windows → Profili uygula → onayla → işlem çalışır.
                vm.Page = 1;
                vm.ApplyWindowsCommand.Execute(null);
                PumpUntil(() => vm.Confirm is not null, "onay katmanı");
                vm.ConfirmYesCommand.Execute(null);
                PumpUntil(() => vm.IsBusy, "işlem başladı");
                Assert.Equal(TaskbarItemProgressState.Indeterminate, window.TaskbarItemInfo.ProgressState); // eski sürüm tam burada düşüyordu
                PumpUntil(() => vm.Notice is not null, "işlem bitti");
                Assert.Equal(NoticeKind.Success, vm.Notice!.Kind);
                Assert.Equal(["windows:Standard"], backend.Calls);
                Assert.Contains("sahte günlük satırı", window.LogBox.Text);
                Assert.Equal(TaskbarItemProgressState.None, window.TaskbarItemInfo.ProgressState);
                Assert.True(vm.CanApplyWindows); // düğmeler kilitli kalmadı

                // Debloat sayfası gerçek satırlarla çizilir: satır şablonu, düzey rozeti ve ayrıntı paneli bağlamaları.
                vm.Page = 4;
                PumpUntil(() => vm.CanScan, "cihaz hazır");
                vm.ScanPackagesCommand.Execute(null);
                PumpUntil(() => vm.DebloatScanned && !vm.IsBusy, "tarama");
                Assert.Equal(2, vm.DebloatRows.Count);
                vm.DebloatFocus = vm.DebloatRows[0];
                vm.DebloatRows[0].IsSelected = true;
                Pump();
                vm.DebloatView = 1;
                Pump();
                Assert.Single(vm.DebloatRows);

                window.Close();
                Pump();
            }
            catch (Exception e) { failure = e; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(60)), "Pencere testi zaman aşımına uğradı.");
        Assert.Null(failure);
        Assert.Empty(errors);
    }

    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    private static void PumpUntil(Func<bool> condition, string what)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(20), "Zaman aşımı: " + what);
            Pump();
            Thread.Sleep(5);
        }
    }
}
