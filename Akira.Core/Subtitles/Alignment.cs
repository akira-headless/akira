namespace Akira.Core.Subtitles;

internal static class Alignment
{
    // SSA counts 1 to 3 at the bottom, plus 4 at the top and plus 8 in the middle; ASS uses the numeric keypad
    public static int FromSsa(int value) => value switch
    {
        5 or 6 or 7 => value + 2,
        9 or 10 or 11 => value - 5,
        _ => value
    };
}
