using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace TradeCopilot.UI;

/// <summary>Maps capture-active state to a status-dot brush. Brushes are pre-created and frozen.</summary>
public sealed class BooleanToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush ActiveBrushValue = CreateFrozen("#4CAF50");
    private static readonly SolidColorBrush InactiveBrushValue = CreateFrozen("#F44336");

    /// <summary>Brush used when capture is active (green).</summary>
    public Brush ActiveBrush { get; set; } = ActiveBrushValue;

    /// <summary>Brush used when capture is paused (red).</summary>
    public Brush InactiveBrush { get; set; } = InactiveBrushValue;

    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? ActiveBrush : InactiveBrush;

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static SolidColorBrush CreateFrozen(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!);
        brush.Freeze();
        return brush;
    }
}

/// <summary>Maps a boolean to Visibility, with an optional inversion.</summary>
public sealed class BooleanToVisibilityConverter : IValueConverter
{
    /// <summary>When true, true maps to Collapsed and false to Visible.</summary>
    public bool Invert { get; set; }

    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool flag = value is true;
        if (Invert)
        {
            flag = !flag;
        }

        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
