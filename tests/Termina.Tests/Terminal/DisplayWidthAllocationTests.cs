// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

using System.Diagnostics;
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

    private const int CallsPerRound = 64;

    private const int ClippedLength = 4;

    private static readonly TimeSpan SteadyStateBudget = TimeSpan.FromSeconds(10);

    [Fact]
    public void GetColumnCount_allocates_nothing()
    {
        AssertSteadyStateAllocation(0, () => DisplayWidth.GetColumnCount(MixedText));
    }

    [Fact]
    public void GetColumnCount_of_a_char_allocates_nothing()
    {
        AssertSteadyStateAllocation(0, () => DisplayWidth.GetColumnCount('\u65E5'));
    }

    [Fact]
    public void GetStringIndexForColumnCount_allocates_nothing()
    {
        AssertSteadyStateAllocation(0, () => DisplayWidth.GetStringIndexForColumnCount(MixedText, 8));
    }

    [Fact]
    public void TruncateToColumns_allocates_nothing_when_the_text_fits()
    {
        var columns = DisplayWidth.GetColumnCount(MixedText);
        AssertSteadyStateAllocation(0, () => DisplayWidth.TruncateToColumns(MixedText, columns));
    }

    [Fact]
    public void TruncateStartToColumns_allocates_nothing_when_the_text_fits()
    {
        var columns = DisplayWidth.GetColumnCount(MixedText);
        AssertSteadyStateAllocation(0, () => DisplayWidth.TruncateStartToColumns(MixedText, columns));
    }

    [Fact]
    public void SliceByColumns_allocates_nothing_when_the_text_fits()
    {
        var columns = DisplayWidth.GetColumnCount(MixedText);
        AssertSteadyStateAllocation(0, () => DisplayWidth.SliceByColumns(MixedText, 0, columns));
    }

    [Fact]
    public void SanitizeTerminalText_allocates_nothing_when_the_text_is_clean()
    {
        AssertSteadyStateAllocation(0, () => DisplayWidth.SanitizeTerminalText(MixedText));
    }

    [Fact]
    public void WriteAt_allocates_nothing_when_the_text_fits()
    {
        var context = new RegionRenderContext(new NullTerminal(), 0, 0, 80, 24);
        AssertSteadyStateAllocation(0, () => context.WriteAt(0, 0, MixedText));
    }

    [Fact]
    public void TruncateToColumns_allocates_only_the_clipped_string()
    {
        const string ascii = "0123456789";
        AssertSteadyStateAllocation(
            StringSize(ClippedLength),
            () => DisplayWidth.TruncateToColumns(ascii, ClippedLength));
    }

    [Fact]
    public void DiffingTerminal_writes_ascii_cells_without_allocating()
    {
        var terminal = new DiffingTerminal(new NullTerminal());

        AssertSteadyStateAllocation(0, () =>
        {
            terminal.MoveTo(0, 0);
            terminal.Write("status: ready");
        });
    }

    [Fact]
    public void DiffingTerminal_keeps_the_string_of_a_single_text_element()
    {
        var terminal = new DiffingTerminal(new NullTerminal());
        const string wide = "\u65E5";

        AssertSteadyStateAllocation(0, () =>
        {
            terminal.MoveTo(0, 0);
            terminal.Write(wide);
        });
    }

    [Fact]
    public void WriteAt_of_an_ascii_char_allocates_nothing()
    {
        var context = new RegionRenderContext(new NullTerminal(), 0, 0, 80, 24);
        AssertSteadyStateAllocation(0, () => context.WriteAt(0, 0, 'x'));
    }

    [Fact]
    public void SliceByColumns_allocates_only_the_clipped_string()
    {
        AssertSteadyStateAllocation(
            StringSize(ClippedLength),
            () => DisplayWidth.SliceByColumns(MixedText, 2, ClippedLength));
    }

    /// <summary>
    /// Asserts the bytes that one call allocates once the runtime runs optimized code.
    /// </summary>
    /// <remarks>
    /// With tiered compilation, the runtime first runs unoptimized code and replaces it with
    /// optimized code on a background thread later. Unoptimized code can allocate where optimized
    /// code does not: the generic <c>ContainsAnyExceptInRange</c> allocated 96 bytes on each call
    /// until the JIT optimized it. When the replacement happens depends on the test order and on
    /// the other tests that run in parallel. So the test measures rounds of calls until one round
    /// allocates the expected bytes, or until the budget ends. A regression allocates in every
    /// round, so the test fails with the lowest round it measured.
    /// </remarks>
    private static void AssertSteadyStateAllocation(long expectedBytesPerCall, Action action)
    {
        var expectedBytesPerRound = expectedBytesPerCall * CallsPerRound;
        var budget = Stopwatch.StartNew();
        var lowestBytesPerRound = long.MaxValue;

        while (lowestBytesPerRound > expectedBytesPerRound && budget.Elapsed < SteadyStateBudget)
            lowestBytesPerRound = Math.Min(lowestBytesPerRound, MeasureRound(action));

        Assert.True(
            lowestBytesPerRound == expectedBytesPerRound,
            $"Expected {expectedBytesPerCall} bytes for each call. The lowest round of {CallsPerRound} calls in "
            + $"{SteadyStateBudget.TotalSeconds} s allocated {lowestBytesPerRound} bytes.");
    }

    private static long MeasureRound(Action action)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < CallsPerRound; i++)
            action();

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static long StringSize(int length)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        var text = new string('x', length);
        var size = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(text);
        return size;
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
