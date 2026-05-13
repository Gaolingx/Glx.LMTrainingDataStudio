using Avalonia.Data.Converters;
using Avalonia.Media;
using LMTrainingDataStudio2.Models;
using System.Globalization;

namespace LMTrainingDataStudio2.Converters;

/// <summary>
/// Converts BlockType to its corresponding header color brush.
/// </summary>
public sealed class BlockTypeToColorConverter : IValueConverter
{
    public static readonly BlockTypeToColorConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is BlockType type)
        {
            var color = type switch
            {
                BlockType.Seed => "#4A90D9",
                BlockType.Llm => "#7B61FF",
                BlockType.Expression => "#F5A623",
                BlockType.Validator => "#D0021B",
                BlockType.Sampler => "#417505",
                BlockType.ToolProfile => "#9B9B9B",
                _ => "#4A90D9"
            };
            return new SolidColorBrush(Color.Parse(color));
        }
        return Brushes.Gray;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

/// <summary>
/// Converts a boolean to visibility (inverse).
/// </summary>
public sealed class InverseBoolConverter : IValueConverter
{
    public static readonly InverseBoolConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b) return !b;
        return true;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b) return !b;
        return true;
    }
}
