// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

using System.Runtime.ExceptionServices;
using Termina.Terminal;

namespace Termina.Tests.Terminal;

/// <summary>
/// A tab moves the cursor to the next tab stop of its row. As in a terminal, the last column of the
/// row is the last tab stop, so a tab never moves the cursor to the next row.
/// </summary>
/// <remarks>
/// Each terminal used to expand a tab in a loop that wrapped to the next row and continued, so a
/// tab after the last tab stop of a row filled every row below it, and on the last row the loop
/// never ended. The tests run the write on a dedicated thread so that a regression fails instead of hanging the run.
/// The thread does not use the thread pool, so a busy pool cannot delay the start of the write.
/// </remarks>
public class TerminalTabTests
{
    private const int Width = 8;
    private const int Height = 2;
    private const int InlineMarginColumns = 1;
    private const int WideWidth = 20;

    private static readonly TimeSpan HangGuard = TimeSpan.FromMinutes(1);

    [Fact]
    public void VirtualTerminal_tab_on_the_last_row_stops_at_the_last_column()
    {
        var terminal = new VirtualTerminal(Width, Height);

        WriteWithHangGuard(terminal, 0, Height - 1, "ab\t");

        Assert.Equal(["a", "b", " ", " ", " ", " ", " ", " "], TerminalCells.Row(terminal, Height - 1));
        Assert.Equal(string.Empty, terminal.GetLine(0));
        Assert.Equal((Width - 1, Height - 1), (terminal.CursorX, terminal.CursorY));
    }

    [Fact]
    public void VirtualTerminal_tab_does_not_move_to_the_next_row()
    {
        var terminal = new VirtualTerminal(Width, Height);

        WriteWithHangGuard(terminal, 5, 0, "\tX");

        Assert.Equal("       X", terminal.GetLine(0));
        Assert.Equal(string.Empty, terminal.GetLine(1));
    }

    [Fact]
    public void VirtualTerminal_tab_before_the_last_tab_stop_moves_to_the_next_tab_stop()
    {
        var terminal = new VirtualTerminal(WideWidth, Height);

        WriteWithHangGuard(terminal, 0, 0, "a\tb\tc");

        Assert.Equal("a       b       c", terminal.GetLine(0));
        Assert.Equal((17, 0), (terminal.CursorX, terminal.CursorY));
    }

    [Fact]
    public void DiffingTerminal_tab_on_the_last_row_stops_at_the_last_column()
    {
        var inner = new VirtualTerminal(Width, Height);
        var terminal = new DiffingTerminal(inner);

        WriteWithHangGuard(terminal, 0, Height - 1, "ab\t");
        terminal.Flush();

        Assert.Equal("ab", inner.GetLine(Height - 1));
        Assert.Equal(string.Empty, inner.GetLine(0));
    }

    [Fact]
    public void DiffingTerminal_tab_does_not_move_to_the_next_row()
    {
        var inner = new VirtualTerminal(Width, Height);
        var terminal = new DiffingTerminal(inner);

        WriteWithHangGuard(terminal, 5, 0, "\tX");
        terminal.Flush();

        Assert.Equal("       X", inner.GetLine(0));
        Assert.Equal(string.Empty, inner.GetLine(1));
    }

    [Fact]
    public void DiffingTerminal_tab_before_the_last_tab_stop_moves_to_the_next_tab_stop()
    {
        var inner = new VirtualTerminal(WideWidth, Height);
        var terminal = new DiffingTerminal(inner);

        WriteWithHangGuard(terminal, 0, 0, "a\tb\tc");
        terminal.Flush();

        Assert.Equal("a       b       c", inner.GetLine(0));
    }

    [Fact]
    public void InlineTerminal_tab_on_the_last_row_stops_at_the_last_column()
    {
        var inner = new VirtualTerminal(Width + InlineMarginColumns, Height);
        var terminal = new InlineTerminal(inner, inner);

        WriteWithHangGuard(terminal, 0, Height - 1, "ab\t");
        terminal.Flush();

        // The inline frame ends each row with a line break, so the primary buffer scrolls one row.
        Assert.Equal(["ab", string.Empty], inner.GetAllLines());
    }

    [Fact]
    public void InlineTerminal_tab_does_not_move_to_the_next_row()
    {
        var inner = new VirtualTerminal(Width + InlineMarginColumns, Height);
        var terminal = new InlineTerminal(inner, inner);

        WriteWithHangGuard(terminal, 5, 0, "\tX");
        terminal.Flush();

        Assert.Equal(["       X", string.Empty], inner.GetAllLines());
    }

    [Fact]
    public void InlineTerminal_tab_before_the_last_tab_stop_moves_to_the_next_tab_stop()
    {
        var inner = new VirtualTerminal(WideWidth + InlineMarginColumns, Height);
        var terminal = new InlineTerminal(inner, inner);

        WriteWithHangGuard(terminal, 0, 0, "a\tb\tc");
        terminal.Flush();

        Assert.Equal("a       b       c", inner.GetAllLines()[0]);
    }

    private static void WriteWithHangGuard(IAnsiTerminal terminal, int x, int y, string text)
    {
        Exception? failure = null;
        var write = new Thread(() =>
        {
            try
            {
                terminal.MoveTo(x, y);
                terminal.Write(text);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        })
        {
            IsBackground = true,
        };
        write.Start();

        Assert.True(write.Join(HangGuard), $"Write of {text.Length} chars did not return.");
        if (failure is not null)
            ExceptionDispatchInfo.Throw(failure);
    }
}
