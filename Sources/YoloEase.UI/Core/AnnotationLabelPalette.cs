using System;
using System.Collections.Generic;
using System.Linq;

namespace YoloEase.UI.Core;

/// <summary>
/// Provides deterministic fallback colors for offline annotation labels.
/// </summary>
public static class AnnotationLabelPalette
{
    private static readonly string[] Palette =
    {
        "#4F7CFF",
        "#FF3B7F",
        "#22C55E",
        "#F59E0B",
        "#A855F7",
        "#06B6D4",
        "#EF4444",
        "#84CC16",
        "#F97316",
        "#14B8A6",
        "#EAB308",
        "#EC4899",
        "#38BDF8",
        "#C084FC",
        "#10B981",
        "#F43F5E",
    };

    public static IReadOnlyList<string> Colors => Palette;

    public static string PickByLabelId(int labelId)
    {
        var index = Math.Max(0, labelId - 1) % Palette.Length;
        return Palette[index];
    }

    public static string PickNext(IEnumerable<string?> existingColors)
    {
        var usedColors = existingColors
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return Palette.FirstOrDefault(x => !usedColors.Contains(x))
               ?? Palette[usedColors.Count % Palette.Length];
    }
}
