using UMToptimizer.Core;

namespace UMToptimizer.UI;

/// <summary>Debloat listesindeki bir satır: cihazdaki paket + (varsa) UAD açıklaması + seçim durumu.</summary>
public sealed class PackageRow : Observable
{
    private readonly Action _changed;
    private bool _isSelected;

    internal PackageRow(DevicePackage package, UadEntry? entry, DateTimeOffset? removedAt, Action changed)
    {
        _changed = changed;
        Name = package.Name;
        Removed = package.Removed;
        RemovedAt = package.Removed ? removedAt : null;
        Level = entry?.Removal ?? Removal.Unlisted;
        LevelText = LevelName(Level);
        ListText = entry?.List switch { "Oem" => "Üretici", "Google" => "Google", "Carrier" => "Operatör", "Aosp" => "Android (AOSP)", "Misc" => "Diğer", _ => "" };
        Description = entry is null ? "Bu paket Universal Android Debloater listesinde yok; ne işe yaradığı bilinmiyor. Emin değilsen dokunma."
            : entry.Description.Length > 0 ? entry.Description : "Bu paket için açıklama yazılmamış.";
        Short = Description.Split('\n', 2)[0].Trim();
        DependsText = string.Join('\n', entry?.Dependencies ?? []);
        NeededByText = string.Join('\n', entry?.NeededBy ?? []);
        StateText = !Removed ? ""
            : RemovedAt is { } at ? $"UMToptimizer ile kaldırıldı · {at.LocalDateTime:g}"
            : "Bu kullanıcıda kurulu değil (üretici ya da başka bir araç kaldırmış).";
    }

    public string Name { get; }
    public bool Removed { get; }
    public DateTimeOffset? RemovedAt { get; }
    public Removal Level { get; }
    public string LevelText { get; }
    public string ListText { get; }
    public string Description { get; }
    /// <summary>Açıklamanın ilk satırı (liste satırında tek satır gösterilir).</summary>
    public string Short { get; }
    public string DependsText { get; }
    public string NeededByText { get; }
    public string StateText { get; }

    public bool IsSelected { get => _isSelected; set { if (Set(ref _isSelected, value)) _changed(); } }

    internal static string LevelName(Removal level) => level switch
    {
        Removal.Recommended => "Önerilen",
        Removal.Advanced => "Gelişmiş",
        Removal.Expert => "Uzman",
        Removal.Unsafe => "Güvensiz",
        _ => "Listede yok",
    };
}

/// <summary>Güvenlik düzeyi süzgeci. Level=null → tümü.</summary>
public sealed record LevelFilter(string Name, string Hint, Removal? Level);

/// <summary>Debloat sayfası: sistem paketlerini tara, seçilenleri kaldır, kaldırılmışları geri getir.</summary>
public sealed partial class MainViewModel
{
    private List<PackageRow> _allRows = [];
    private string? _debloatSerial;
    private bool _bulkSelect;

    public Command ScanPackagesCommand { get; }
    public Command DebloatCommand { get; }
    public Command SelectVisibleCommand { get; }
    public Command ClearSelectionCommand { get; }

    public IReadOnlyList<LevelFilter> DebloatLevels { get; } =
    [
        new("Önerilen", "Kaldırılması güvenli, çoğu kullanıcının ihtiyaç duymadığı paketler.", Removal.Recommended),
        new("Gelişmiş", "Kaldırılabilir ama bazı özellikler (ör. üretici uygulamaları) çalışmayabilir.", Removal.Advanced),
        new("Uzman", "Önemli işlevleri bozabilir; yalnızca ne yaptığını biliyorsan kaldır.", Removal.Expert),
        new("Güvensiz", "Telefonun açılmamasına (bootloop) yol açabilir. Kaldırma.", Removal.Unsafe),
        new("Listede yok", "UAD listesinde bulunmayan, ne işe yaradığı bilinmeyen paketler.", Removal.Unlisted),
        new("Tümü", "Bütün sistem paketleri.", null),
    ];

    private LevelFilter _debloatLevel;
    public LevelFilter DebloatLevel { get => _debloatLevel; set { if (value is not null && Set(ref _debloatLevel, value)) RefreshRows(); } }

    private int _debloatView;
    /// <summary>0 Yüklü paketler (kaldırma) · 1 Kaldırılmış paketler (geri getirme)</summary>
    public int DebloatView
    {
        get => _debloatView;
        set
        {
            if (!Set(ref _debloatView, Math.Clamp(value, 0, 1))) return;
            ClearSelection(); // görünümler farklı işlem yapar; diğer görünümde gizli seçim kalmasın
            RefreshRows();
        }
    }

    private string _debloatSearch = "";
    public string DebloatSearch { get => _debloatSearch; set { if (Set(ref _debloatSearch, value ?? "")) RefreshRows(); } }

    private IReadOnlyList<PackageRow> _debloatRows = [];
    public IReadOnlyList<PackageRow> DebloatRows { get => _debloatRows; private set => Set(ref _debloatRows, value); }

    private PackageRow? _debloatFocus;
    /// <summary>Ayrıntı panelinde gösterilen satır (işaretli olmasından bağımsız).</summary>
    public PackageRow? DebloatFocus { get => _debloatFocus; set => Set(ref _debloatFocus, value); }

    private bool _debloatScanned, _debloatEmpty, _canScan, _canDebloat;
    public bool DebloatScanned { get => _debloatScanned; private set => Set(ref _debloatScanned, value); }
    /// <summary>Tarama yapıldı ama süzgeçle eşleşen paket yok.</summary>
    public bool DebloatEmpty { get => _debloatEmpty; private set => Set(ref _debloatEmpty, value); }
    public bool CanScan { get => _canScan; private set => Set(ref _canScan, value); }
    public bool CanDebloat { get => _canDebloat; private set => Set(ref _canDebloat, value); }

    private string? _scanWhy, _debloatWhy;
    public string? ScanWhy { get => _scanWhy; private set => Set(ref _scanWhy, value); }
    public string? DebloatWhy { get => _debloatWhy; private set => Set(ref _debloatWhy, value); }

    private string _installedTab = "Yüklü", _removedTab = "Kaldırılmış", _debloatAction = "Seçilenleri kaldır", _selectionText = "", _debloatEmptyText = "";
    public string InstalledTab { get => _installedTab; private set => Set(ref _installedTab, value); }
    public string RemovedTab { get => _removedTab; private set => Set(ref _removedTab, value); }
    public string DebloatAction { get => _debloatAction; private set => Set(ref _debloatAction, value); }
    public string SelectionText { get => _selectionText; private set => Set(ref _selectionText, value); }
    public string DebloatEmptyText { get => _debloatEmptyText; private set => Set(ref _debloatEmptyText, value); }

    // ---------------- Durum ----------------

    /// <summary>UpdateState'in parçası: tarama/eylem düğmelerinin etkinliği ve nedenleri.</summary>
    private void UpdateDebloatState()
    {
        // Tarama sonucu tek bir cihaza aittir; başka cihaz seçilirse (veya cihaz çıkarılırsa) eski liste gösterilmez.
        if (_debloatSerial is not null && !IsBusy && Device?.Serial != _debloatSerial) ClearDebloat();

        ScanWhy = ApplyAndroidWhy;
        CanScan = ScanWhy is null;

        var restore = DebloatView == 1;
        var selected = _allRows.Count(r => r.IsSelected && r.Removed == restore);
        DebloatWhy = ScanWhy ?? (!DebloatScanned ? "Önce paketleri tara." : selected == 0 ? "Listeden en az bir paket işaretle." : null);
        CanDebloat = DebloatWhy is null;

        InstalledTab = DebloatScanned ? $"Yüklü ({_allRows.Count(r => !r.Removed):N0})" : "Yüklü";
        RemovedTab = DebloatScanned ? $"Kaldırılmış ({_allRows.Count(r => r.Removed):N0})" : "Kaldırılmış";
        DebloatAction = (restore ? "Seçilenleri geri getir" : "Seçilenleri kaldır") + (selected > 0 ? $" ({selected:N0})" : "");
        SelectionText = selected > 0 ? $"{selected:N0} paket seçili" : $"{DebloatRows.Count:N0} paket listeleniyor";
        DebloatEmpty = DebloatScanned && DebloatRows.Count == 0;
        DebloatEmptyText = DebloatSearch.Trim().Length > 0 ? "Aramayla eşleşen paket yok."
            : restore ? "Bu cihazda kaldırılmış sistem paketi yok. Kaldırdığın paketler burada görünür ve tek tıkla geri gelir."
            : "Bu düzeyde paket yok. Başka bir güvenlik düzeyi seç.";
    }

    private void ClearDebloat()
    {
        _debloatSerial = null;
        _allRows = [];
        DebloatScanned = false;
        DebloatFocus = null;
        DebloatRows = [];
    }

    private void SetRows(string serial, List<PackageRow> rows)
    {
        var focus = DebloatFocus?.Name;
        _debloatSerial = serial;
        _allRows = rows;
        DebloatScanned = true;
        RefreshRows();
        DebloatFocus = DebloatRows.FirstOrDefault(r => r.Name == focus);
    }

    /// <summary>Görünüm, düzey ve aramaya göre görünen satırları yeniden üretir.</summary>
    private void RefreshRows()
    {
        var restore = DebloatView == 1;
        var query = DebloatSearch.Trim();
        var rows = _allRows.Where(r => r.Removed == restore
            && (restore || DebloatLevel.Level is null || r.Level == DebloatLevel.Level)
            && (query.Length == 0 || r.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || r.Description.Contains(query, StringComparison.OrdinalIgnoreCase)));
        // Geri getirme görünümünde bu uygulamayla kaldırılanlar (en yenisi üstte) önce gelir.
        var focus = DebloatFocus;
        DebloatRows = restore
            ? rows.OrderByDescending(r => r.RemovedAt ?? DateTimeOffset.MinValue).ThenBy(r => r.Name, StringComparer.Ordinal).ToList()
            : rows.ToList();
        DebloatFocus = focus is not null && DebloatRows.Contains(focus) ? focus : null;
        UpdateDebloatState();
    }

    private void OnRowSelectionChanged()
    {
        if (!_bulkSelect) UpdateDebloatState();
    }

    /// <summary>Görünen satırları işaretler. Güvensiz ve listede olmayan paketler toplu seçime asla girmez; tek tek işaretlenmeleri gerekir.</summary>
    private void SelectVisible()
    {
        _bulkSelect = true;
        try
        {
            foreach (var r in DebloatRows)
                if (r.Removed || r.Level is not (Removal.Unsafe or Removal.Unlisted)) r.IsSelected = true;
        }
        finally { _bulkSelect = false; }
        UpdateDebloatState();
    }

    private void ClearSelection()
    {
        _bulkSelect = true;
        try { foreach (var r in _allRows) r.IsSelected = false; }
        finally { _bulkSelect = false; }
        UpdateDebloatState();
    }

    // ---------------- Eylemler ----------------

    /// <summary>Arka planda çalışır: paketleri okur, UAD açıklamaları ve kaldırma geçmişiyle birleştirir.</summary>
    private async Task<List<PackageRow>> LoadRowsAsync(string serial, CancellationToken ct)
    {
        var packages = await _backend.ScanPackagesAsync(serial, ct);
        var history = _backend.DebloatHistory(serial);
        return packages.Select(p => new PackageRow(p, _backend.DescribePackage(p.Name),
            history.TryGetValue(p.Name, out var at) ? at : null, OnRowSelectionChanged)).ToList();
    }

    private async Task ScanPackagesAsync()
    {
        if (!CanScan || Device is not { Ready: true } d) return;
        List<PackageRow>? rows = null;
        var ok = await RunAsync($"Paketler taranıyor · {d.Name}", cancellable: true, async ct =>
        {
            rows = await LoadRowsAsync(d.Serial, ct);
            var installed = rows.Where(r => !r.Removed).ToList();
            return new(NoticeKind.Success,
                $"{installed.Count:N0} sistem paketi bulundu  ·  kaldırılması önerilen: {installed.Count(r => r.Level == Removal.Recommended):N0}  ·  kaldırılmış: {rows.Count - installed.Count:N0}");
        });
        if (!ok || rows is null || Device?.Serial != d.Serial) return;
        SetRows(d.Serial, rows);
        ActivityVisible = false; // tarama günlüğü boş; panel listeye yer açsın
    }

    private async Task DebloatActAsync()
    {
        if (!CanDebloat || Device is not { Ready: true } d) return;
        var restore = DebloatView == 1;
        var picked = _allRows.Where(r => r.IsSelected && r.Removed == restore).ToList();
        if (picked.Count == 0) return;

        var preview = string.Join('\n', picked.Take(6).Select(r => "•  " + r.Name)) + (picked.Count > 6 ? $"\n…ve {picked.Count - 6:N0} paket daha" : "");
        bool confirmed;
        if (restore)
            confirmed = await ConfirmAsync($"{picked.Count:N0} paket geri getirilsin mi?",
                $"Cihaz: {d.Name}\n{preview}\n\nUygulamalar telefondaki fabrika sürümüyle geri gelir; kaldırmadan önceki verileri geri gelmez.", "Geri getir");
        else
        {
            var risky = picked.Where(r => r.Level is Removal.Expert or Removal.Unsafe or Removal.Unlisted)
                .GroupBy(r => r.LevelText).Select(g => $"{g.Count():N0} {g.Key}").ToList();
            var chosen = picked.Select(r => r.Name).ToHashSet(StringComparer.Ordinal);
            var needed = picked.Count(r => r.NeededByText.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Any(n => !chosen.Contains(n) && _allRows.Any(x => x.Name == n && !x.Removed)));
            confirmed = await ConfirmAsync($"{picked.Count:N0} paket kaldırılsın mı?",
                $"Cihaz: {d.Name}\n{preview}\n\nPaketler yalnızca senin kullanıcından kaldırılır ve uygulama verileri silinir. APK telefonda kalır; \"Kaldırılmış\" görünümünden istediğin zaman geri getirebilirsin." +
                (needed > 0 ? $"\n\n{needed:N0} paket, telefonda kurulu başka uygulamalar için gerekli; o uygulamalar çalışmayabilir." : "") +
                (risky.Count > 0 ? $"\n\nDikkat: seçimde {string.Join(", ", risky)} düzeyinde paket var. Bunlar telefonun açılmamasına yol açabilir; açılmayan telefonda geri getirme yapılamaz ve fabrika ayarlarına dönmek gerekebilir." : ""),
                "Kaldır");
        }
        if (!confirmed) return;

        var names = picked.Select(r => r.Name).ToList();
        List<PackageRow>? rows = null;
        var allDone = false;
        await RunAsync($"Debloat · {(restore ? "geri getirme" : "kaldırma")} → {d.Name}", cancellable: true, async ct =>
        {
            try
            {
                var r = await _backend.DebloatAsync(d.Serial, names, restore, (i, n) => ReportProgress(n > 0 ? i / (double)n : 1), Log, ct);
                if (r.LogPath.Length > 0) Log($"Günlük: {r.LogPath}");
                allDone = r.Failed.Count == 0;
                return (restore, allDone) switch
                {
                    (false, true) => new(NoticeKind.Success, $"{r.Done.Count:N0} paket kaldırıldı. Fikrini değiştirirsen \"Kaldırılmış\" görünümünden geri getirebilirsin."),
                    (true, true) => new(NoticeKind.Success, $"{r.Done.Count:N0} paket geri getirildi."),
                    (false, false) => new Notice(NoticeKind.Warning, $"{r.Done.Count:N0}/{r.Total:N0} paket kaldırıldı; {r.Failed.Count:N0} paketi cihaz reddetti. Nedenleri işlem günlüğünde."),
                    (true, false) => new(NoticeKind.Warning, $"{r.Done.Count:N0}/{r.Total:N0} paket geri getirildi; {r.Failed.Count:N0} paket geri gelmedi. Nedenleri işlem günlüğünde."),
                };
            }
            finally
            {
                // İptal/hata olsa bile liste cihazın gerçek durumunu göstersin. Okunamazsa liste temizlenir (yeniden tarama istenir).
                try { rows = await LoadRowsAsync(d.Serial, _life.Token); }
                catch (Exception e) when (e is UserException or OperationCanceledException) { rows = null; }
            }
        });
        if (_life.IsCancellationRequested) return;
        if (rows is not null && Device?.Serial == d.Serial) SetRows(d.Serial, rows); else ClearDebloat();
        if (allDone) ActivityVisible = false;
        UpdateState();
    }
}
