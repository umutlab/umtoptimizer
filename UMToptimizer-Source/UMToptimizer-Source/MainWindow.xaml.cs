using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Diagnostics;
using System.IO;
using System.Collections.Generic;
using Microsoft.Win32;
using System.Windows.Threading;
using UMToptimizer.Models;
using UMToptimizer.Services;

namespace UMToptimizer;

public partial class MainWindow : Window
{
    private readonly OptimizerService _service = new();
    private OptimizationProfile? _selectedProfile;
    private AndroidDevice? _selectedDevice;
    private readonly Brush _text;
    private readonly Brush _muted;
    private readonly Brush _accent;
    private readonly Brush _panel;
    private readonly Brush _panel2;
    private readonly Brush _line;
    private readonly DispatcherTimer _adbMonitor = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly HashSet<string> _knownConnectedDevices = new(StringComparer.Ordinal);
    private ComboBox? _androidDevicePicker;
    private TextBlock? _androidConnectionBanner;
    private string? _currentSection;
    private string _lastDeviceSignature = "";
    private string? _completedAndroidSerial;
    private bool _adbMonitorBusy;
    private bool _androidProfileRunning;

    public MainWindow()
    {
        InitializeComponent();
        _text = (Brush)FindResource("Text"); _muted = (Brush)FindResource("Muted"); _accent = (Brush)FindResource("Accent");
        _panel = (Brush)FindResource("Panel"); _panel2 = (Brush)FindResource("Panel2"); _line = (Brush)FindResource("Line");
        _adbMonitor.Tick += async (_, _) => await MonitorAdbAsync();
        Loaded += async (_, _) => { ShowOverview(); await ScanAdbOnStartupAsync(); _adbMonitor.Start(); };
        Closed += (_, _) => _adbMonitor.Stop();
    }
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (IsInsideButton(e.OriginalSource as DependencyObject)) return;
        if (e.ClickCount == 2) WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        else if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private static bool IsInsideButton(DependencyObject? element)
    {
        while (element is not null)
        {
            if (element is ButtonBase) return true;
            element = element is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(element)
                : LogicalTreeHelper.GetParent(element);
        }
        return false;
    }
    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void MainScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        TopGlassEdge.Opacity = e.VerticalOffset > 1 ? 1 : 0;
        BottomGlassEdge.Opacity = e.VerticalOffset + e.ViewportHeight < e.ExtentHeight - 1 ? 0.65 : 0;
    }
    private void Overview_Click(object sender, RoutedEventArgs e) => ShowOverview();
    private void Windows_Click(object sender, RoutedEventArgs e) => ShowPlatform(PlatformKind.Windows);
    private void Android_Click(object sender, RoutedEventArgs e) => ShowPlatform(PlatformKind.Android);
    private void Restore_Click(object sender, RoutedEventArgs e) => ShowRestore();

    private void ActivateNav(Button active)
    {
        foreach (var item in new[] { OverviewNav, WindowsNav, AndroidNav, RestoreNav }) { item.Background = Brushes.Transparent; item.BorderBrush = Brushes.Transparent; }
        active.Background = _panel2; active.BorderBrush = _line;
    }

    private void ShowOverview()
    {
        _currentSection = "Overview";
        _androidDevicePicker = null;
        _androidConnectionBanner = null;
        ActivateNav(OverviewNav);
        var root = new StackPanel();
        root.Children.Add(Eyebrow("SİSTEM DURUMU"));
        var titleRow = new DockPanel { Margin = new Thickness(0, 5, 0, 22) };
        var ready = new Border { BorderBrush = _line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16), Padding = new Thickness(14, 7, 14, 7), VerticalAlignment = VerticalAlignment.Center };
        ready.Child = new TextBlock { Text = "●   HAZIR", Foreground = _accent, FontFamily = new FontFamily("Consolas"), FontSize = 10 };
        DockPanel.SetDock(ready, Dock.Right); titleRow.Children.Add(ready); titleRow.Children.Add(new TextBlock { Text = "Her şey yolunda.", Foreground = _text, FontSize = 27, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        root.Children.Add(titleRow);

        var cards = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 0, 25) };
        cards.Children.Add(SummaryCard("PROFİL", "Standard", "Agresif seçenekli Windows profilleri"));
        cards.Children.Add(SummaryCard("PLATFORM", "Windows", "Android ADB de kullanılabilir"));
        cards.Children.Add(SummaryCard("KONTROL", "Senin elinde", "Önizleme ve geri alma mevcut"));
        root.Children.Add(cards);
        root.Children.Add(Separator());
        root.Children.Add(CheckRow("✓", "Geri yükleme kaydı", "Uygulama öncesi değerler saklanır", "HAZIR"));
        root.Children.Add(CheckRow("✓", "Profil önizlemesi", "Her profilde değişecek ayarları incele", "HAZIR"));
        root.Children.Add(CheckRow("•", "Android bağlantısı", "ADB cihazını Android ADB bölümünden seç", "USB HATA AYIKLAMA"));
        var action = new Button { Content = "Önce ayarları gözden geçir     →", HorizontalContentAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 18, 0, 0), Padding = new Thickness(17, 14, 17, 14) };
        action.Click += (_, _) => ShowPlatform(PlatformKind.Windows); root.Children.Add(action);
        SetPage(root);
    }

    private void ShowPlatform(PlatformKind platform)
    {
        _currentSection = platform == PlatformKind.Windows ? "Windows" : "Android";
        _androidDevicePicker = null;
        _androidConnectionBanner = null;
        ActivateNav(platform == PlatformKind.Windows ? WindowsNav : AndroidNav);
        var root = new StackPanel();
        var label = platform == PlatformKind.Windows ? "WINDOWS OPTİMİZASYONU" : "ANDROID ADB OPTİMİZASYONU";
        root.Children.Add(Eyebrow(label));
        root.Children.Add(new TextBlock { Text = platform == PlatformKind.Windows ? "Windows güç profili" : "Android cihaz profili", Foreground = _text, FontSize = 27, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 5, 0, 6) });
        root.Children.Add(new TextBlock { Text = platform == PlatformKind.Windows ? "Standart veya agresif Windows profilini incele; uygulama öncesinde yönetici onayı istenir." : "ADB ile bağlı Android cihazda ayar profillerini uygula.", Foreground = _muted, FontSize = 13, Margin = new Thickness(0, 0, 0, 22) });

        ComboBox? devicePicker = null;
        if (platform == PlatformKind.Android)
        {
            var connectionBanner = new TextBlock
            {
                Text = "ADB cihazları 5 saniyede bir otomatik taranıyor.", Foreground = _accent,
                FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12)
            };
            _androidConnectionBanner = connectionBanner;
            root.Children.Add(PanelCard("Canlı ADB bağlantısı", "Cihaz bağlandığında liste otomatik yenilenir. Profil çalışırken komutlar aşağıdaki işlem alanında görünür."));
            root.Children.Add(connectionBanner);
            var deviceRow = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
            var deviceButtons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var chooseAdb = new Button { Content = "adb.exe seç", Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 8, 0) };
            var refresh = new Button { Content = "Cihazları yenile", Padding = new Thickness(13, 8, 13, 8) };
            deviceButtons.Children.Add(chooseAdb); deviceButtons.Children.Add(refresh);
            DockPanel.SetDock(deviceButtons, Dock.Right); deviceRow.Children.Add(deviceButtons);
            devicePicker = new ComboBox { MinWidth = 260, VerticalAlignment = VerticalAlignment.Center, ItemTemplate = (DataTemplate)FindResource("DeviceLabelTemplate") };
            _androidDevicePicker = devicePicker;
            deviceRow.Children.Add(devicePicker); root.Children.Add(deviceRow);
            var picker = devicePicker;
            refresh.Click += async (_, _) => await RefreshDevicesAsync(picker, showErrors: true);
            chooseAdb.Click += async (_, _) =>
            {
                var sdkAdb = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Android", "Sdk", "platform-tools");
                var dialog = new OpenFileDialog
                {
                    Title = "Android Platform Tools içinden adb.exe dosyasını seç",
                    Filter = "ADB (adb.exe)|adb.exe|Uygulamalar (*.exe)|*.exe",
                    CheckFileExists = true,
                    Multiselect = false,
                    FileName = "adb.exe",
                    InitialDirectory = Directory.Exists(sdkAdb) ? sdkAdb : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                };
                if (dialog.ShowDialog(this) != true) return;
                try
                {
                    OptimizerService.SaveAdbPath(dialog.FileName);
                    await RefreshDevicesAsync(picker, showErrors: true);
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "ADB yolu kaydedilemedi", MessageBoxButton.OK, MessageBoxImage.Error); }
            };
            _ = RefreshDevicesAsync(devicePicker);
            devicePicker.SelectionChanged += (_, _) => { _selectedDevice = devicePicker.SelectedItem as AndroidDevice; if (_selectedDevice?.State == "device") UpdateHeader("ADB CİHAZI HAZIR"); };
        }

        var profileRow = new DockPanel { Margin = new Thickness(0, 0, 0, 17) };
        profileRow.Children.Add(Eyebrow("OPTİMİZASYON PROFİLİ"));
        var profilePicker = new ComboBox { ItemsSource = ProfileCatalog.For(platform).ToList(), ItemTemplate = (DataTemplate)FindResource("ProfileLabelTemplate"), MinWidth = 230, HorizontalAlignment = HorizontalAlignment.Right };
        profilePicker.SelectionChanged += (_, _) => { _selectedProfile = profilePicker.SelectedItem as OptimizationProfile; UpdatePreview(root, _selectedProfile); };
        DockPanel.SetDock(profilePicker, Dock.Right); profileRow.Children.Insert(0, profilePicker); root.Children.Add(profileRow);
        profilePicker.SelectedIndex = 0;

        var previewHost = new StackPanel { Name = "PreviewHost" };
        previewHost.Tag = "preview";
        root.Children.Add(previewHost);
        if (_selectedProfile is not null) PopulatePreview(previewHost, _selectedProfile);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var previewButton = new Button { Content = "Önizlemeyi yenile", Margin = new Thickness(0, 0, 10, 0) };
        previewButton.Click += (_, _) => { if (_selectedProfile is not null) PopulatePreview(previewHost, _selectedProfile); };
        var applyButton = new Button { Content = "Profili uygula", Background = new SolidColorBrush(Color.FromRgb(65, 91, 76)), BorderBrush = new SolidColorBrush(Color.FromRgb(95, 130, 108)), Padding = new Thickness(21, 11, 21, 11) };
        var executionPanel = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 15, 0, 0) };
        var executionOutput = new TextBox
        {
            IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MinHeight = 145, MaxHeight = 250,
            FontFamily = new FontFamily("Consolas"), FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(190, 205, 195)), Background = Brushes.Transparent,
            BorderThickness = new Thickness(0), Padding = new Thickness(4)
        };
        var rebootButton = new Button
        {
            Content = "Cihazı yeniden başlat", Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0),
            Padding = new Thickness(17, 10, 17, 10),
            Background = new SolidColorBrush(Color.FromRgb(65, 91, 76)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(95, 130, 108))
        };
        var consoleBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(16, 18, 19)), BorderBrush = _line,
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), Padding = new Thickness(10),
            Child = executionOutput
        };
        executionPanel.Children.Add(PanelCard("ADB işlem alanı", "Komut ilerlemesi ve cihazdan dönen çıktılar burada gösterilir."));
        executionPanel.Children.Add(consoleBorder);
        executionPanel.Children.Add(rebootButton);
        root.Children.Add(executionPanel);
        rebootButton.Click += async (_, _) => await RebootCompletedDeviceAsync(rebootButton, executionOutput);
        applyButton.Click += async (_, _) => await ApplySelectedAsync(platform, devicePicker, applyButton, executionPanel, executionOutput, rebootButton);
        buttons.Children.Add(previewButton); buttons.Children.Add(applyButton); root.Children.Add(buttons);
        root.Children.Add(new TextBlock { Text = platform == PlatformKind.Android ? "Not: Seçili profil kaynak Android betiğindeki tüm adb shell komutlarını gönderir. Bazı komutlar üretici/Android sürümüne özgüdür ve otomatik geri alma desteklenmez." : "Windows profilleri gömülü PowerShell betiğini yönetici izniyle çalıştırır. Bazı dosya ve uygulama kaldırmaları geri alınamaz; performans artışı garanti edilmez.", Foreground = _muted, TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 17, 0, 0) });
        SetPage(root);
    }

    private void UpdatePreview(StackPanel root, OptimizationProfile? profile)
    {
        if (profile is null) return;
        var host = root.Children.OfType<StackPanel>().LastOrDefault(x => Equals(x.Tag, "preview"));
        if (host is not null) PopulatePreview(host, profile);
    }

    private void PopulatePreview(StackPanel host, OptimizationProfile profile)
    {
        host.Children.Clear();
        host.Children.Add(PanelCard(profile.Name, profile.Description));
        if (profile.Commands.Count > 0)
        {
            host.Children.Add(PanelCard("Tam komut önizlemesi", $"{profile.Commands.Count:N0} ADB komutu. Cihaz ve Android sürümüne göre bazı komutlar hata verebilir veya yok sayılabilir."));
            host.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(16, 18, 19)), BorderBrush = _line, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9), Padding = new Thickness(10), Margin = new Thickness(0, 0, 0, 12),
                Child = new TextBox
                {
                    Text = string.Join(Environment.NewLine, profile.Commands), IsReadOnly = true, AcceptsReturn = true,
                    TextWrapping = TextWrapping.NoWrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, MinHeight = 190, MaxHeight = 310,
                    FontFamily = new FontFamily("Consolas"), FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromRgb(190, 205, 195)), Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0), Padding = new Thickness(3)
                }
            });
            host.Children.Add(PanelCard("Önemli uyarı", profile.RiskNotice));
        }
        if (!string.IsNullOrWhiteSpace(profile.WindowsScriptMode))
        {
            host.Children.Add(PanelCard("Profilin yapacakları", string.Join("\n\n", profile.Actions.Select(a => $"• {a.Title}: {a.Detail}"))));
            host.Children.Add(PanelCard("Önemli uyarı", profile.RiskNotice));
        }
        foreach (var action in profile.Actions)
        {
            var row = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = action.Title + "\n" + action.Detail, Foreground = _text, FontSize = 13, Margin = new Thickness(0, 8, 8, 8), TextWrapping = TextWrapping.Wrap });
            var value = new TextBlock { Text = action.Value, Foreground = _accent, FontFamily = new FontFamily("Consolas"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 3, 0) };
            Grid.SetColumn(value, 1); row.Children.Add(value); host.Children.Add(row);
        }
        host.Children.Add(Separator());
    }

    private async Task RefreshDevicesAsync(ComboBox picker, bool showErrors = false)
    {
        try
        {
            var devices = await _service.GetAndroidDevicesAsync();
            RememberDevices(devices);
            picker.ItemsSource = devices;
            picker.SelectedItem = devices.FirstOrDefault(d => d.State == "device");
            if (_androidConnectionBanner is not null)
            {
                var readyDevice = devices.FirstOrDefault(d => d.State == "device");
                var waitingDevice = devices.FirstOrDefault(d => d.State == "unauthorized");
                _androidConnectionBanner.Text = readyDevice is not null ? $"ADB bağlı: {readyDevice.Serial}"
                    : waitingDevice is not null ? $"{waitingDevice.Serial} bulundu; telefon ekranındaki ADB iznini onayla."
                    : "ADB cihazları 5 saniyede bir otomatik taranıyor.";
            }
            if (picker.SelectedItem is null)
            {
                var unauthorized = devices.Any(d => d.State == "unauthorized");
                UpdateHeader(unauthorized ? "TELEFONDA ADB İZNİ VER" : devices.Count == 0 ? "ADB HAZIR · CİHAZ YOK" : "ADB CİHAZI HAZIR DEĞİL");
                if (showErrors)
                {
                    var message = unauthorized
                        ? "Telefon ADB tarafından görüldü ancak yetkilendirme bekliyor. Telefonun kilidini açın ve ekrandaki 'USB hata ayıklamaya izin ver' penceresinde İzin ver'e dokunun."
                        : devices.Count == 0
                            ? "ADB çalışıyor ancak bağlı cihaz listesi boş. Veri aktarabilen bir USB kablosu kullanın, telefonda USB hata ayıklamayı açın ve Cihazları yenile'ye basın. ADB farklı bir klasördeyse 'adb.exe seç' ile konumunu gösterebilirsiniz."
                            : "ADB cihaz buldu ancak hiçbiri 'device' durumunda değil. Telefon ekranındaki izin isteğini onaylayıp yeniden deneyin.";
                    MessageBox.Show(this, message, "Android cihazı hazır değil", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }
        catch (Exception ex)
        {
            picker.ItemsSource = Array.Empty<AndroidDevice>(); picker.SelectedItem = null;
            UpdateHeader(OptimizerService.FindAdbPath() is null ? "ADB YÜKLÜ DEĞİL" : "ADB BAŞLATILAMADI");
            if (showErrors)
            {
                var adbPath = OptimizerService.FindAdbPath() ?? "Bulunamadı";
                MessageBox.Show(this, $"ADB cihaz listesi alınamadı.\n\nADB yolu: {adbPath}\nHata: {ex.Message}\n\nADB başka bir klasördeyse 'adb.exe seç' düğmesiyle doğru dosyayı gösterin.", "ADB bağlantı hatası", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private async Task MonitorAdbAsync()
    {
        if (_adbMonitorBusy || _androidProfileRunning || OptimizerService.FindAdbPath() is null) return;
        _adbMonitorBusy = true;
        try
        {
            var devices = await _service.GetAndroidDevicesAsync();
            var connectedNow = devices.Where(d => d.State == "device").Select(d => d.Serial).ToHashSet(StringComparer.Ordinal);
            var newlyConnected = connectedNow.Except(_knownConnectedDevices, StringComparer.Ordinal).ToList();
            var changed = RememberDevices(devices);

            if (_androidDevicePicker is not null && changed)
            {
                var selectedSerial = (_androidDevicePicker.SelectedItem as AndroidDevice)?.Serial;
                _androidDevicePicker.ItemsSource = devices;
                _androidDevicePicker.SelectedItem = devices.FirstOrDefault(d => d.Serial == selectedSerial && d.State == "device")
                    ?? devices.FirstOrDefault(d => d.State == "device")
                    ?? devices.FirstOrDefault(d => d.State == "unauthorized");
                _selectedDevice = _androidDevicePicker.SelectedItem as AndroidDevice;
            }

            if (newlyConnected.Count > 0)
            {
                if (_currentSection == "Overview") ShowPlatform(PlatformKind.Android);
                if (_androidConnectionBanner is not null)
                {
                    _androidConnectionBanner.Text = $"Yeni ADB cihazı algılandı: {string.Join(", ", newlyConnected)}. Cihaz listesi güncellendi; bir profil seçebilirsin.";
                    _androidConnectionBanner.Foreground = _accent;
                }
                UpdateHeader("ANDROID CİHAZI BAĞLANDI");
            }
            else if (changed)
            {
                if (_androidConnectionBanner is not null)
                {
                    var ready = devices.FirstOrDefault(d => d.State == "device");
                    var unauthorized = devices.FirstOrDefault(d => d.State == "unauthorized");
                    _androidConnectionBanner.Text = ready is not null
                        ? $"ADB bağlı: {ready.Serial}"
                        : unauthorized is not null ? $"{unauthorized.Serial} bulundu; telefondaki USB hata ayıklama iznini onayla."
                        : "ADB taraması açık; Android cihaz bekleniyor.";
                }
                if (devices.Any(d => d.State == "device")) UpdateHeader("ADB CİHAZI HAZIR");
                else if (devices.Any(d => d.State == "unauthorized")) UpdateHeader("TELEFONDA ADB İZNİ VER");
                else if (_knownConnectedDevices.Count == 0) UpdateHeader("ADB HAZIR · CİHAZ YOK");
            }
        }
        catch
        {
            // Keep the monitor quiet during transient USB disconnects; manual refresh shows detailed errors.
        }
        finally { _adbMonitorBusy = false; }
    }

    private bool RememberDevices(IReadOnlyList<AndroidDevice> devices)
    {
        var signature = string.Join("|", devices.OrderBy(d => d.Serial, StringComparer.Ordinal).Select(d => $"{d.Serial}:{d.State}"));
        if (signature == _lastDeviceSignature) return false;
        _lastDeviceSignature = signature;
        _knownConnectedDevices.Clear();
        foreach (var device in devices.Where(d => d.State == "device")) _knownConnectedDevices.Add(device.Serial);
        return true;
    }

    private async Task RebootCompletedDeviceAsync(Button rebootButton, TextBox executionOutput)
    {
        var serial = _completedAndroidSerial;
        if (string.IsNullOrWhiteSpace(serial)) return;
        if (MessageBox.Show(this, $"{serial} cihazına yeniden başlatma komutu gönderilsin mi? Açık uygulamalar kapanabilir.", "Cihazı yeniden başlat", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        rebootButton.IsEnabled = false;
        try
        {
            UpdateHeader("CİHAZ YENİDEN BAŞLATILIYOR…");
            executionOutput.AppendText($"\r\n{serial} için yeniden başlatma komutu gönderiliyor…\r\n");
            await _service.RebootAndroidAsync(serial);
            executionOutput.AppendText("Yeniden başlatma komutu cihaza ulaştı. Bağlantı kısa süreliğine kesilebilir.\r\n");
            executionOutput.ScrollToEnd();
            UpdateHeader("YENİDEN BAŞLATMA İSTEĞİ GÖNDERİLDİ");
        }
        catch (Exception ex)
        {
            executionOutput.AppendText($"Yeniden başlatma hatası: {ex.Message}\r\n");
            executionOutput.ScrollToEnd();
            MessageBox.Show(this, ex.Message, "Yeniden başlatılamadı", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { rebootButton.IsEnabled = true; }
    }

    private async Task ApplySelectedAsync(PlatformKind platform, ComboBox? devicePicker, Button applyButton, StackPanel executionPanel, TextBox executionOutput, Button rebootButton)
    {
        var profile = _selectedProfile;
        if (profile is null) return;
        var serial = platform == PlatformKind.Android ? (devicePicker?.SelectedItem as AndroidDevice)?.Serial : null;
        if (platform == PlatformKind.Android && (devicePicker?.SelectedItem as AndroidDevice)?.State != "device")
        { MessageBox.Show(this, "Önce ADB ile yetkilendirilmiş bir Android cihaz seç.", "Cihaz gerekli", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        var summary = !string.IsNullOrWhiteSpace(profile.WindowsScriptMode)
            ? $"'{profile.Name}' profili cihazınızda önemli değişiklikler yapabilir.\n\n{profile.Description}\n\n{string.Join("\n", profile.Actions.Select(a => $"• {a.Title}: {a.Detail}"))}\n\nDikkat: {profile.RiskNotice}\n\nDevam ederseniz yönetici izni istenir ve PowerShell penceresindeki ek onayları da yanıtlamanız gerekir. Devam etmek istiyor musunuz?"
            : profile.Commands.Count > 0
            ? $"Bu profil cihazınızı önemli ölçüde değiştirebilir.\n\n{profile.Description}\n\n{profile.Commands.Count:N0} ADB komutunun tamamı seçili cihaza gönderilecek.\n\nDikkat: {profile.RiskNotice}\n\nİşleme devam etmek istiyor musunuz?"
            : $"'{profile.Name}' profilini uygulamak istiyor musun?\n\n{string.Join("\n", profile.Actions.Select(a => $"• {a.Title}: {a.Detail}"))}\n\nÖnceki ayarlar geri yüklenmek üzere kaydedilecek.";
        var isScript = profile.Commands.Count > 0;
        var isWindowsScript = !string.IsNullOrWhiteSpace(profile.WindowsScriptMode);
        if (MessageBox.Show(this, summary, isScript || isWindowsScript ? "Önemli sistem değişikliği" : "Uygulamayı onayla", MessageBoxButton.YesNo, isScript || isWindowsScript ? MessageBoxImage.Warning : MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        applyButton.IsEnabled = false;
        _androidProfileRunning = isScript;
        try
        {
            if (isScript)
            {
                executionOutput.Clear();
                executionPanel.Visibility = Visibility.Visible;
                rebootButton.Visibility = Visibility.Collapsed;
                _completedAndroidSerial = null;
                executionOutput.AppendText($"Cihaz: {serial}\r\nProfil: {profile.Name}\r\n{profile.Commands.Count:N0} komut sırayla çalıştırılıyor…\r\n\r\n");
                executionOutput.ScrollToEnd();
            }
            UpdateHeader("PROFİL UYGULANIYOR…");
            var progress = isScript ? new Progress<string>(line =>
            {
                executionOutput.AppendText(line + Environment.NewLine);
                executionOutput.ScrollToEnd();
            }) : null;
            var applyWarning = await _service.ApplyAsync(profile, serial, progress);
            if (isScript)
            {
                executionOutput.AppendText(applyWarning is null ? "\r\nTüm komutlar tamamlandı.\r\n" : "\r\nProfil tamamlandı; bazı komutlar başarısız oldu. Uyarı ve günlük yolunu kontrol edin.\r\n");
                executionOutput.ScrollToEnd();
                _completedAndroidSerial = serial;
                rebootButton.Visibility = Visibility.Visible;
            }
            UpdateHeader(applyWarning is null ? "PROFİL UYGULANDI" : "KISMEN UYGULANDI · UYARI");
            var result = isWindowsScript
                ? "Windows profil betiği tamamlandı. Ayrıntılar ve yedekler %ProgramData%\\UMToptimizer konumunda tutulur. Geri alma için uygulamanın Geri yükleme bölümünü kullanın."
                : isScript
                ? applyWarning ?? "Betikteki tüm komutlar çalıştırıldı. Bu profil otomatik olarak geri alınamaz."
                : "Profil uygulandı. Geri yükleme bölümünden son değişikliği geri alabilirsin.";
            var hasWarning = applyWarning is not null;
            MessageBox.Show(this, result, hasWarning ? "Profil kısmen uygulandı" : "Tamamlandı", MessageBoxButton.OK, hasWarning ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }
        catch (OperationCanceledException ex) { UpdateHeader("İŞLEM İPTAL EDİLDİ"); MessageBox.Show(this, ex.Message, "İşlem iptal edildi", MessageBoxButton.OK, MessageBoxImage.Information); }
        catch (Exception ex) { if (isScript) executionOutput.AppendText($"\r\nHATA: {ex.Message}\r\n"); UpdateHeader("İŞLEM TAMAMLANAMADI"); MessageBox.Show(this, ex.Message, "İşlem hatası", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { _androidProfileRunning = false; applyButton.IsEnabled = true; }
    }

    private async void ShowRestore()
    {
        _currentSection = "Restore";
        _androidDevicePicker = null;
        _androidConnectionBanner = null;
        ActivateNav(RestoreNav);
        var record = await new UndoStore().LoadAsync();
        var root = new StackPanel(); root.Children.Add(Eyebrow("KONTROL SENDE"));
        root.Children.Add(new TextBlock { Text = "Geri yükleme", Foreground = _text, FontSize = 27, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 5, 0, 6) });
        root.Children.Add(new TextBlock { Text = "Son uygulanan profilin öncesindeki ayarları geri yükle.", Foreground = _muted, FontSize = 13, Margin = new Thickness(0, 0, 0, 21) });
        if (record is null) root.Children.Add(PanelCard("Geri alınacak işlem yok", "Uygulanan ilk profilin ayarları burada görünür."));
        else
        {
            var recordDescription = record.WindowsScriptMode is not null
                ? "PowerShell geri yükleme betiği mevcut. Bazı silinen dosyalar/uygulamalar ve hosts ekleri geri getirilemeyebilir."
                : record.CanUndo ? $"Saklanan Android ayarı: {record.AndroidSettings.Count}" : "Bu profilin komutları otomatik geri alınamaz.";
            root.Children.Add(PanelCard(record.Profile, $"Platform: {record.Platform}   •   {record.CreatedAt.LocalDateTime:g}\n{recordDescription}"));
            if (!record.CanUndo)
                root.Children.Add(PanelCard("Otomatik geri alma yok", record.Notes ?? "Bu profil bazı cihaz ayarlarını kalıcı olarak değiştirebilir."));
            ComboBox? devicePicker = null;
            if (record.Platform == "Android" && record.CanUndo)
            {
                devicePicker = new ComboBox { ItemsSource = await TryGetDevicesAsync(), ItemTemplate = (DataTemplate)FindResource("DeviceLabelTemplate"), Margin = new Thickness(0, 12, 0, 12), MinWidth = 280, HorizontalAlignment = HorizontalAlignment.Left };
                devicePicker.SelectedItem = (devicePicker.ItemsSource as IEnumerable<AndroidDevice>)?.FirstOrDefault(d => d.State == "device"); root.Children.Add(devicePicker);
            }
            if (record.CanUndo)
            {
                var undo = new Button { Content = "Son değişikliği geri yükle", HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0), Padding = new Thickness(20, 12, 20, 12) };
                var picker = devicePicker;
                undo.Click += async (_, _) =>
                {
                    var serial = (picker?.SelectedItem as AndroidDevice)?.Serial;
                    if (record.Platform == "Android" && serial is null) { MessageBox.Show(this, "Geri yüklemek için cihaz seç.", "Cihaz gerekli", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
                    var restorePrompt = record.WindowsScriptMode is not null
                        ? "Windows betiğinin yedekleyebildiği kayıt defteri ve hizmet ayarları geri yüklenecek. Silinen uygulamalar/dosyalar ve hosts listesi tam geri alınamayabilir. Yönetici izni istenir. Devam edilsin mi?"
                        : "Kaydedilen önceki değerler geri yüklensin mi?";
                    if (MessageBox.Show(this, restorePrompt, "Geri yüklemeyi onayla", MessageBoxButton.YesNo, record.WindowsScriptMode is not null ? MessageBoxImage.Warning : MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                    try { await _service.UndoAsync(serial); UpdateHeader("AYARLAR GERİ YÜKLENDİ"); ShowRestore(); }
                    catch (Exception ex) { MessageBox.Show(this, ex.Message, "Geri yükleme hatası", MessageBoxButton.OK, MessageBoxImage.Error); }
                };
                root.Children.Add(undo);
            }
        }
        SetPage(root);
    }

    private async Task<IReadOnlyList<AndroidDevice>> TryGetDevicesAsync() { try { return await _service.GetAndroidDevicesAsync(); } catch { return []; } }
    private async Task ScanAdbOnStartupAsync()
    {
        if (OptimizerService.FindAdbPath() is null)
        {
            UpdateHeader("ADB YÜKLÜ DEĞİL");
            var choice = MessageBox.Show(this,
                "Android optimizasyonu için ADB (Android Platform Tools) gerekli; bu bilgisayarda bulunamadı.\n\nResmi ADB indirme sayfasını şimdi açmak ister misiniz?",
                "ADB bulunamadı", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (choice == MessageBoxResult.Yes)
            {
                try { Process.Start(new ProcessStartInfo("https://developer.android.com/tools/releases/platform-tools") { UseShellExecute = true }); }
                catch (Exception ex) { MessageBox.Show(this, $"İndirme sayfası açılamadı: {ex.Message}", "Tarayıcı açılamadı", MessageBoxButton.OK, MessageBoxImage.Error); }
            }
            return;
        }

        try
        {
            var devices = await _service.GetAndroidDevicesAsync();
            RememberDevices(devices);
            UpdateHeader(devices.Any(d => d.State == "device") ? "ADB VE CİHAZ HAZIR"
                : devices.Any(d => d.State == "unauthorized") ? "TELEFONDA ADB İZNİ VER"
                : "ADB HAZIR · CİHAZ YOK");
        }
        catch (Exception ex)
        {
            UpdateHeader("ADB BAŞLATILAMADI");
            MessageBox.Show(this, $"ADB bulundu ancak kontrol sırasında başlatılamadı.\n\n{ex.Message}", "ADB bağlantısı", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void UpdateHeader(string text)
    {
        HeaderStatus.Text = text;
        var unavailable = text.Contains("BULUNAMADI", StringComparison.OrdinalIgnoreCase)
            || text.Contains("YÜKLÜ DEĞİL", StringComparison.OrdinalIgnoreCase)
            || text.Contains("BAŞLATILAMADI", StringComparison.OrdinalIgnoreCase);
        ReadyDot.Fill = unavailable ? new SolidColorBrush(Color.FromRgb(218, 143, 124)) : _accent;
    }

    private void SetPage(UIElement content)
    {
        PageHost.BeginAnimation(UIElement.OpacityProperty, null);
        if (PageHost.RenderTransform is TranslateTransform previousSlide)
            previousSlide.BeginAnimation(TranslateTransform.YProperty, null);
        PageHost.Content = content;
        MainScroll.ScrollToTop();
        if (!SystemParameters.ClientAreaAnimation)
        {
            PageHost.Opacity = 1;
            PageHost.RenderTransform = Transform.Identity;
            return;
        }
        var slide = new TranslateTransform(0, 5);
        PageHost.RenderTransform = slide;
        PageHost.Opacity = 0.88;
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        var motion = new Duration(TimeSpan.FromMilliseconds(150));
        PageHost.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.88, 1, motion) { EasingFunction = ease });
        slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(5, 0, motion) { EasingFunction = ease });
    }

    private Border SummaryCard(string eyebrow, string title, string description)
    {
        var content = new StackPanel(); content.Children.Add(Eyebrow(eyebrow)); content.Children.Add(new TextBlock { Text = title, Foreground = _text, FontSize = 17, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 4) }); content.Children.Add(new TextBlock { Text = description, Foreground = _muted, FontSize = 12, TextWrapping = TextWrapping.Wrap });
        return Card(content, new Thickness(13, 12, 13, 12), new Thickness(0, 0, 11, 0));
    }
    private Border PanelCard(string title, string description)
    {
        var content = new StackPanel(); content.Children.Add(new TextBlock { Text = title, Foreground = _text, FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) }); content.Children.Add(new TextBlock { Text = description, Foreground = _muted, FontSize = 12, TextWrapping = TextWrapping.Wrap, LineHeight = 19 });
        return Card(content, new Thickness(17, 15, 17, 15), new Thickness(0, 0, 0, 12));
    }
    private Border Card(UIElement child, Thickness padding, Thickness margin)
    {
        var glassFill = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0), EndPoint = new Point(1, 1),
            GradientStops = new GradientStopCollection
            {
                new(Color.FromArgb(232, 34, 39, 39), 0),
                new(Color.FromArgb(222, 23, 26, 27), 0.58),
                new(Color.FromArgb(232, 28, 32, 31), 1)
            }
        };
        var glassLine = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0), EndPoint = new Point(1, 1),
            GradientStops = new GradientStopCollection
            {
                new(Color.FromArgb(105, 166, 197, 177), 0),
                new(Color.FromArgb(58, 121, 138, 128), 0.38),
                new(Color.FromArgb(75, 97, 108, 101), 1)
            }
        };
        var layers = new Grid();
        layers.Children.Add(child);
        layers.Children.Add(new Border
        {
            Height = 1, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false,
            Background = new LinearGradientBrush(Color.FromArgb(40, 218, 239, 226), Color.FromArgb(0, 218, 239, 226), 0)
        });
        return new Border { Background = glassFill, BorderBrush = glassLine, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(11), Padding = padding, Margin = margin, Child = layers };
    }
    private TextBlock Eyebrow(string text) => new() { Text = text, Foreground = _muted, FontFamily = new FontFamily("Consolas"), FontSize = 10, Margin = new Thickness(0, 0, 0, 5) };
    private Border Separator() => new() { Height = 1, Background = _line, Margin = new Thickness(0, 3, 0, 8) };
    private Border CheckRow(string icon, string title, string detail, string status)
    {
        var grid = new Grid { Margin = new Thickness(0, 5, 0, 5) }; grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) }); grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new TextBlock { Text = icon, Foreground = _accent, FontSize = 16, VerticalAlignment = VerticalAlignment.Center });
        var text = new StackPanel(); text.Children.Add(new TextBlock { Text = title, Foreground = _text, FontSize = 13 }); text.Children.Add(new TextBlock { Text = detail, Foreground = _muted, FontSize = 11, Margin = new Thickness(0, 2, 0, 0) }); Grid.SetColumn(text, 1); grid.Children.Add(text);
        var tag = new TextBlock { Text = status, Foreground = _muted, FontFamily = new FontFamily("Consolas"), FontSize = 9, VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(tag, 2); grid.Children.Add(tag);
        return new Border { BorderBrush = _line, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 12, 0, 12), Child = grid };
    }
}
