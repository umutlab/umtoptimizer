using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;

namespace UMToptimizer.UI;

/// <summary>Yalnızca görünüme özgü işler: günlük kutusu, odak yönetimi, dosya seçici. Mantığın tamamı MainViewModel'de.</summary>
public partial class MainWindow : Window
{
    private const int MaxLogChars = 400_000;

    private readonly MainViewModel _vm;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private IInputElement? _focusBeforeConfirm;
    private int _logLength;

    public MainWindow() : this(new MainViewModel()) { }

    /// <summary>Testler sahte arka uçlu bir görünüm modeli verebilir.</summary>
    internal MainWindow(MainViewModel vm)
    {
        DataContext = _vm = vm;
        InitializeComponent();

        _vm.LogCleared += () => { LogBox.Clear(); _logLength = 0; };
        _vm.LogAppended += AppendLog;
        _vm.PropertyChanged += OnViewModelChanged;
        _timer.Tick += (_, _) => _vm.Tick(); // günlük ve ilerleme saniyede en çok 10 kez arayüze yansır

        Loaded += (_, _) => { NavHome.Focus(); _vm.Start(); };
        Closed += (_, _) => { _timer.Stop(); _vm.Dispose(); };
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.IsBusy):
                if (_vm.IsBusy) _timer.Start(); else _timer.Stop();
                break;

            case nameof(MainViewModel.Confirm) when _vm.Confirm is not null:
                // Odak onay katmanına, güvenli seçeneğe taşınır: yanlışlıkla Enter'a basmak sistemi değiştirmez.
                _focusBeforeConfirm = Keyboard.FocusedElement;
                Dispatcher.BeginInvoke(() => ConfirmNo.Focus(), DispatcherPriority.Input);
                break;

            case nameof(MainViewModel.Confirm):
                var back = _focusBeforeConfirm;
                _focusBeforeConfirm = null;
                Dispatcher.BeginInvoke(() => { if (back is UIElement { IsVisible: true, IsEnabled: true }) back.Focus(); else NavHome.Focus(); }, DispatcherPriority.Input);
                break;
        }
    }

    private void AppendLog(string chunk)
    {
        // Uzunluk ayrı sayılır: LogBox.Text her okunuşta tüm metni (yüz binlerce karakter) yeniden üretir.
        if (_logLength + chunk.Length > MaxLogChars)
        {
            var text = LogBox.Text;
            LogBox.Text = text[^Math.Min(text.Length, MaxLogChars / 2)..]; // bellek sınırı: yalnızca son yarı kalır
            _logLength = LogBox.Text.Length;
        }
        _logLength += chunk.Length;
        LogBox.AppendText(chunk);
        LogBox.ScrollToEnd();
    }

    /// <summary>Paket listesinde odak satırdadır; Boşluk o satırın onay kutusunu değiştirir (klavyeyle toplu seçim).</summary>
    private void PackageList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space || sender is not System.Windows.Controls.ListBox { SelectedItem: PackageRow row }) return;
        row.IsSelected = !row.IsSelected;
        e.Handled = true;
    }

    private void PickAdb_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "adb.exe dosyasını seç", Filter = "ADB|adb.exe", FileName = "adb.exe" };
        if (dialog.ShowDialog(this) == true) _vm.UseAdb(dialog.FileName);
    }
}
