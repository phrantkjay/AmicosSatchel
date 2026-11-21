using System.Collections.Generic;
using System.Numerics;

namespace AmicosSatchel.Windows;

/// <summary>
/// Saved choice of theme for the satchel window.
/// </summary>
public enum SatchelThemeId {
    CrystalBoard,
    WaxAndParchment,
}

/// <summary>
/// Colours and shapes for the satchel window.
/// </summary>
public sealed record SatchelTheme {
    public required SatchelThemeId Id { get; init; }
    public required string Name { get; init; }

    public required Vector4 WindowBg { get; init; }
    public required Vector4 TitleBg { get; init; }
    public required Vector4 TitleBgActive { get; init; }
    public required Vector4 Border { get; init; }

    public required Vector4 Text { get; init; }
    public required Vector4 Muted { get; init; }
    public required Vector4 Heading { get; init; }

    public required Vector4 Card { get; init; }
    public required Vector4 CardBorder { get; init; }
    public required Vector4 Input { get; init; }
    public required Vector4 InputBorder { get; init; }

    /// <summary>Switches and other "on" states.</summary>
    public required Vector4 Accent { get; init; }

    /// <summary>Accent used for text, such as the Settings link.</summary>
    public required Vector4 AccentText { get; init; }

    public required Vector4 ChipBg { get; init; }
    public required Vector4 ChipText { get; init; }

    public required Vector4 ToggleOff { get; init; }
    public required Vector4 KnobOff { get; init; }
    public required Vector4 KnobOn { get; init; }
    public required Vector4 Error { get; init; }

    /// <summary>Line under the header.</summary>
    public required Vector4 Rule { get; init; }

    /// <summary>Whether the header line fades out to the right.</summary>
    public required bool RuleFades { get; init; }

    public required float WindowRounding { get; init; }
    public required float ControlRounding { get; init; }

    /// <summary>Deep navy, gold frame and crystal blue, matching the plugin icon.</summary>
    public static SatchelTheme CrystalBoard { get; } = new() {
        Id = SatchelThemeId.CrystalBoard,
        Name = "Crystal Board",
        WindowBg = Hex(0x0F1735),
        TitleBg = Hex(0x0A1029),
        TitleBgActive = Hex(0x141F4D),
        Border = Hex(0xC9A24A),
        Text = Hex(0xEEF1FB),
        Muted = Hex(0xA6AED0),
        Heading = Hex(0xF0D68F),
        Card = Hex(0x16204A),
        CardBorder = Hex(0x2C3870),
        Input = Hex(0x0A1029),
        InputBorder = Hex(0x2C3870),
        Accent = Hex(0x4FB4F0),
        AccentText = Hex(0x8FD2FF),
        ChipBg = Hex(0xC9A24A, 0.15f),
        ChipText = Hex(0xF0D68F),
        ToggleOff = Hex(0x0A1029),
        KnobOff = Hex(0x5B6696),
        KnobOn = Hex(0xFFFFFF),
        Error = Hex(0xFF7B7B),
        Rule = Hex(0xC9A24A),
        RuleFades = true,
        WindowRounding = 10f,
        ControlRounding = 8f,
    };

    /// <summary>Light parchment like the journal, with wax red and ribbon blue.</summary>
    public static SatchelTheme WaxAndParchment { get; } = new() {
        Id = SatchelThemeId.WaxAndParchment,
        Name = "Wax & Parchment",
        WindowBg = Hex(0xEFE0BD),
        TitleBg = Hex(0xD9C49C),
        TitleBgActive = Hex(0xCDB384),
        Border = Hex(0x5B3A24),
        Text = Hex(0x2F2015),
        Muted = Hex(0x6C533D),
        Heading = Hex(0x7E1A22),
        Card = Hex(0xF8EED6),
        CardBorder = Hex(0xD4BD94),
        Input = Hex(0xF8EED6),
        InputBorder = Hex(0xC8B089),
        Accent = Hex(0xA3202B),
        AccentText = Hex(0x24448A),
        ChipBg = Hex(0x24448A, 0.12f),
        ChipText = Hex(0x24448A),
        ToggleOff = Hex(0xE4D1AA),
        KnobOff = Hex(0xA68C66),
        KnobOn = Hex(0xFFF6E3),
        Error = Hex(0xA3202B),
        Rule = Hex(0xA3202B),
        RuleFades = false,
        WindowRounding = 6f,
        ControlRounding = 6f,
    };

    public static IReadOnlyList<SatchelTheme> All { get; } = [CrystalBoard, WaxAndParchment];

    public static SatchelTheme For(SatchelThemeId id) => id == SatchelThemeId.WaxAndParchment ? WaxAndParchment : CrystalBoard;

    /// <summary>Blends two colours; amount 0 gives <paramref name="from"/>, 1 gives <paramref name="to"/>.</summary>
    public static Vector4 Mix(Vector4 from, Vector4 to, float amount) => Vector4.Lerp(from, to, amount);

    private static Vector4 Hex(uint rgb, float alpha = 1f) =>
        new(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, alpha);
}
