using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;

namespace UMToptimizer.UI;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }

    protected void Raise(params string[] names)
    {
        foreach (var n in names) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}

/// <summary>Her zaman çalıştırılabilir komut; etkinlik durumu düğmenin IsEnabled bağlamasıyla verilir (nedeni ipucunda görünsün diye).</summary>
public sealed class Command(Action<object?> run) : ICommand
{
    public Command(Action run) : this(_ => run()) { }
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => run(parameter);
}

/// <summary>
/// Değer == parametre karşılaştırması. Hedef Visibility ise Visible/Collapsed, değilse bool üretir.
/// Geri dönüşte (RadioButton işaretlenince) parametreyi kaynak türüne çevirip yazar.
/// </summary>
public sealed class EqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var equal = string.Equals(System.Convert.ToString(value, CultureInfo.InvariantCulture), System.Convert.ToString(parameter, CultureInfo.InvariantCulture), StringComparison.Ordinal);
        return targetType == typeof(Visibility) ? (equal ? Visibility.Visible : Visibility.Collapsed) : equal;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true && parameter is not null ? System.Convert.ChangeType(parameter, targetType, CultureInfo.InvariantCulture) : Binding.DoNothing;
}

/// <summary>bool/null/boş metin → Visibility. Parametre "!" ise tersine çevirir.</summary>
public sealed class VisibleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var on = value switch { bool b => b, string s => s.Length > 0, null => false, _ => true };
        return on != (parameter as string == "!") ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}
