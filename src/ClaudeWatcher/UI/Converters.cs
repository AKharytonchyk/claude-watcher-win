using ClaudeWatcher.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace ClaudeWatcher.UI;

/// <summary>bool → Visibility. Pass "invert" as parameter to flip.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var flag = value is bool b && b;
        if (parameter as string == "invert") flag = !flag;
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>AgentState → the traffic-light brush (matches the tray dot colors).</summary>
public sealed class StateToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var color = value is AgentState s ? s.Color() : StateColor.Green;
        var c = color switch
        {
            StateColor.Red    => Color.FromArgb(255, 0xE5, 0x48, 0x4D),
            StateColor.Yellow => Color.FromArgb(255, 0xF5, 0xA6, 0x23),
            _                 => Color.FromArgb(255, 0x30, 0xA4, 0x6C),
        };
        return new SolidColorBrush(c);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>Context fraction (0..1) → "ctx 62%", or empty until it's worth showing.</summary>
public sealed class ContextPctConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is double p && p >= 0.6) return $"ctx {Math.Round(p * 100)}%";
        return string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
