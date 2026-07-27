using System.Drawing;
using System.Drawing.Drawing2D;

namespace Storava.Agent.Tray;

/// <summary>
/// Draws the tray icon rather than shipping one.
/// <para>
/// It has to say two things at a glance — that the Agent is there, and whether it is listening —
/// which a single static image cannot. Drawing it means the two states are the same mark in two
/// colours instead of two files that could drift apart, and it scales to whatever size the shell
/// asks for on a high-DPI display.
/// </para>
/// </summary>
internal static class AgentIcon
{
    /// <summary>Matches the product's own greens rather than the shell's default palette.</summary>
    private static readonly Color Listening = Color.FromArgb(0xBA, 0xF3, 0x6B);
    private static readonly Color Idle = Color.FromArgb(0x8C, 0x9A, 0x96);
    private static readonly Color Ink = Color.FromArgb(0x07, 0x1A, 0x1C);

    public static Icon Create(bool listening, int size = 32)
    {
        using var bitmap = new Bitmap(size, size);
        using (var canvas = Graphics.FromImage(bitmap))
        {
            canvas.SmoothingMode = SmoothingMode.AntiAlias;
            canvas.Clear(Color.Transparent);

            float inset = size * 0.08f;
            var bounds = new RectangleF(inset, inset, size - (inset * 2), size - (inset * 2));

            using var background = new SolidBrush(Ink);
            canvas.FillEllipse(background, bounds);

            // A single bar whose height reads as "how full", which is what the product is about.
            using var mark = new SolidBrush(listening ? Listening : Idle);
            float barWidth = size * 0.16f;
            float gap = size * 0.09f;
            float left = (size - ((barWidth * 3) + (gap * 2))) / 2f;
            float bottom = size - (size * 0.28f);

            foreach (float height in (float[])[size * 0.20f, size * 0.34f, size * 0.26f])
            {
                canvas.FillRectangle(mark, left, bottom - height, barWidth, height);
                left += barWidth + gap;
            }
        }

        // GetHicon hands out an unmanaged handle; cloning lets it be released immediately rather
        // than leaking one icon per state change for the life of the process.
        nint handle = bitmap.GetHicon();
        try
        {
            using var borrowed = Icon.FromHandle(handle);
            return (Icon)borrowed.Clone();
        }
        finally
        {
            NativeIcon.Destroy(handle);
        }
    }
}

internal static class NativeIcon
{
    public static void Destroy(nint handle)
    {
        if (handle != 0)
            DestroyIcon(handle);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint handle);
}
