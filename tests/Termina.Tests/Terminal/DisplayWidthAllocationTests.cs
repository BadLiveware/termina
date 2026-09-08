// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

using Termina.Layout;
using Termina.Rendering;
using Termina.Terminal;

namespace Termina.Tests.Terminal;

/// <summary>
/// Column measurement and the per-cell writers run on every measure and render pass of every
/// text node, so a width query must not allocate, a clip that keeps the whole string must not
/// copy it, and a cell write must not build a string for each glyph.
/// </summary>
/// <remarks>
/// The measurement is the point of the span scan. A per-element substring scan passes every
/// behavior test while it allocates one string for each glyph on the screen for each frame.
/// </remarks>
public class DisplayWidthAllocationTests
{
    private const string MixedText = "mixed \u65E5\u672C \uD83C\uDF89 text";

    private const int Iterations = 64;

    [Fact]
    public void GetColumnCount_allocates_nothing()
    {
        Assert.Equal(0, Measure(() => DisplayWidth.GetColumnCount(MixedText)));
    }

    [Fact]
    public void GetColumnCount_of_a_char_allocates_nothing()
    {
        Assert.Equal(0, Measure(() => DisplayWidth.GetColumnCount('\u65E5')));
    }

    [Fact]
    public void GetStringIndexForColumnCount_allocates_nothing()
    {
        Assert.Equal(0, Measure(() => DisplayWidth.GetStringIndexForColumnCount(MixedText, 8)));
    }

    [Fact]
    public void TruncateToColumns_allocates_nothing_when_the_text_fits()
    {
        var columns = DisplayWidth.GetColumnCount(MixedText);
        Assert.Equal(0, Measure(() => DisplayWidth.TruncateToColumns(MixedText, columns)));
    }

    [Fact]
    public void TruncateStartToColumns_allocates_nothing_when_the_text_fits()
    {
        var columns = DisplayWidth.GetColumnCount(MixedText);
        Assert.Equal(0, Measure(() => DisplayWidth.TruncateStartToColumns(MixedText, columns)));
    }

    [Fact]
    public void SliceByColumns_allocates_nothing_when_the_text_fits()
    {
        var columns = DisplayWidth.GetColumnCount(MixedText);
        Assert.Equal(0, Measure(() => DisplayWidth.SliceByColumns(MixedText, 0, columns)));
    }

    [Fact]
    public void SanitizeTerminalText_allocates_nothing_when_the_text_is_clean()
    {
        Assert.Equal(0, Measure(() => DisplayWidth.SanitizeTerminalText(MixedText)));
    }

    [Fact]
    public void WriteAt_allocates_nothing_when_the_text_fits()
    {
        var context = new RegionRenderContext(new NullTerminal(), 0, 0, 80, 24);
        Assert.Equal(0, Measure(() => context.WriteAt(0, 0, MixedText)));
    }

    [Fact]
    public void TruncateToColumns_allocates_only_the_clipped_string()
    {
        const string ascii = "0123456789";
        var perCall = Measure(() => DisplayWidth.TruncateToColumns(ascii, 4)) / Iterations;
        var stringSize = Measure(() => new string('x', 4)) / Iterations;

        Assert.Equal(stringSize, perCall);
    }

    [Fact]
    public void DiffingTerminal_writes_ascii_cells_without_allocating()
    {
        var terminal = new DiffingTerminal(new NullTerminal());

        Assert.Equal(0, Measure(() =>
        {
            terminal.MoveTo(0, 0);
            terminal.Write("status: ready");
        }));
    }

    private static long Measure(Action action)
    {
        for (var i = 0; i < Iterations; i++)
            action();

        GC.Collect();
        GC.WaitForPendingFinalizers();

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < Iterations; i++)
            action();

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private sealed class NullTerminal : IAnsiTerminal
    {
        public int Width => 80;

        public int Height => 24;

        public void MoveTo(int x, int y)
        {
        }

        public void Write(string text)
        {
        }

        public void Write(char c)
        {
        }

        public void SetForeground(Color color)
        {
        }

        public void SetBackground(Color color)
        {
        }

        public void ResetColors()
        {
        }

        public void SaveCursor()
        {
        }

        public void RestoreCursor()
        {
        }

        public void SetCursorVisible(bool visible)
        {
        }

        public void ClearRegion(int x, int y, int width, int height)
        {
        }

        public void ClearScreen()
        {
        }

        public void Flush()
        {
        }

        public void EnterAlternateScreen()
        {
        }

        public void ExitAlternateScreen()
        {
        }

        public void EnableMouse()
        {
        }

        public void DisableMouse()
        {
        }

        public void EnableWheelScroll()
        {
        }

        public void DisableWheelScroll()
        {
        }

        public void CopyToClipboard(string text)
        {
        }
    }
}
