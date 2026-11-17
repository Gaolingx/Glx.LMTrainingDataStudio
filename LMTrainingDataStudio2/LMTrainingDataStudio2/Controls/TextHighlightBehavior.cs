using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using LMTrainingDataStudio2.Services;

namespace LMTrainingDataStudio2.Controls;

/// <summary>
/// Renders text through <see cref="TextBlock.Inlines"/> and highlights every search match.
/// Used instead of binding <c>TextBlock.Text</c> so the inline runs are not reset by Text.
/// </summary>
public static class TextHighlightBehavior
{
    public static readonly AttachedProperty<string?> HighlightTextProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, string?>("HighlightText", typeof(TextHighlightBehavior));

    public static readonly AttachedProperty<string?> HighlightQueryProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, string?>("HighlightQuery", typeof(TextHighlightBehavior));

    public static readonly AttachedProperty<bool> HighlightUseRegexProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, bool>("HighlightUseRegex", typeof(TextHighlightBehavior));

    public static readonly AttachedProperty<bool> HighlightCaseSensitiveProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, bool>("HighlightCaseSensitive", typeof(TextHighlightBehavior));

    private static readonly IBrush MatchBackground = new SolidColorBrush(Color.Parse("#66FFC400"));
    private static readonly IBrush MatchForeground = new SolidColorBrush(Color.Parse("#FFFFFF"));

    static TextHighlightBehavior()
    {
        HighlightTextProperty.Changed.AddClassHandler<TextBlock>((block, _) => Refresh(block));
        HighlightQueryProperty.Changed.AddClassHandler<TextBlock>((block, _) => Refresh(block));
        HighlightUseRegexProperty.Changed.AddClassHandler<TextBlock>((block, _) => Refresh(block));
        HighlightCaseSensitiveProperty.Changed.AddClassHandler<TextBlock>((block, _) => Refresh(block));
    }

    public static string? GetHighlightText(TextBlock block) => block.GetValue(HighlightTextProperty);

    public static void SetHighlightText(TextBlock block, string? value) =>
        block.SetValue(HighlightTextProperty, value);

    public static string? GetHighlightQuery(TextBlock block) => block.GetValue(HighlightQueryProperty);

    public static void SetHighlightQuery(TextBlock block, string? value) =>
        block.SetValue(HighlightQueryProperty, value);

    public static bool GetHighlightUseRegex(TextBlock block) => block.GetValue(HighlightUseRegexProperty);

    public static void SetHighlightUseRegex(TextBlock block, bool value) =>
        block.SetValue(HighlightUseRegexProperty, value);

    public static bool GetHighlightCaseSensitive(TextBlock block) =>
        block.GetValue(HighlightCaseSensitiveProperty);

    public static void SetHighlightCaseSensitive(TextBlock block, bool value) =>
        block.SetValue(HighlightCaseSensitiveProperty, value);

    private static void Refresh(TextBlock block)
    {
        var text = GetHighlightText(block) ?? string.Empty;
        var segments = HighlightMatcher.Split(
            text,
            GetHighlightQuery(block),
            GetHighlightUseRegex(block),
            GetHighlightCaseSensitive(block));

        block.Inlines ??= new InlineCollection();
        block.Inlines.Clear();

        foreach (var segment in segments)
        {
            var run = new Run(segment.Text);
            if (segment.IsMatch)
            {
                run.Background = MatchBackground;
                run.Foreground = MatchForeground;
            }

            block.Inlines.Add(run);
        }
    }
}
