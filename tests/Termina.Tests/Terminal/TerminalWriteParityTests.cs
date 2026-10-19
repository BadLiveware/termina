// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

using System.Text;
using Termina.Rendering;
using Termina.Terminal;

namespace Termina.Tests.Terminal;

/// <summary>
/// Pins what each terminal does with a write: the cells of a <see cref="VirtualTerminal" />, and the
/// calls that <see cref="DiffingTerminal" /> and <see cref="InlineTerminal" /> send to the terminal
/// below them on a flush.
/// </summary>
/// <remarks>
/// <para>
/// The scenarios write each string of the <see cref="DisplayWidthCorpusTests" /> corpus at column 0,
/// at a column offset, and through a narrow <see cref="RegionRenderContext" />. Other scenarios put a
/// wide cell at the last column of the screen and of a region, cut a wide cell with the region
/// clip, write over one half of a wide cell, and write single chars.
/// </para>
/// <para>
/// The approved snapshots are the parity oracle. A change of the write path that keeps the cells
/// and the flushed output leaves the snapshots untouched.
/// </para>
/// </remarks>
public class TerminalWriteParityTests
{
    private const int ScreenWidth = 8;
    private const int ScreenHeight = 3;
    private const int LastColumn = ScreenWidth - 1;
    private const int LastRow = ScreenHeight - 1;
    private const int ColumnOffset = 5;
    private const int RegionX = 1;
    private const int RegionY = 1;
    private const int RegionWidth = 4;
    private const int RegionHeight = 2;
    private const int InlineMarginColumns = 1;

    private const string Wide = "日";
    private const string WidePair = "日本";
    private const string OtherWide = "中";
    private const string Emoji = "🎉";
    private const string CharWrites = "a日\t́😀\uD83Db\rc\nd";
    private const string Continuation = "~";

    private static readonly Scenario[] Scenarios = [.. CorpusScenarios(), .. EdgeScenarios()];

    [Fact]
    public Task VirtualTerminal_cells_match_the_approved_snapshot()
    {
        var report = new StringBuilder();

        foreach (var scenario in Scenarios)
        {
            report.Append("scenario: ").AppendLine(scenario.Name);
            var terminal = new VirtualTerminal(ScreenWidth, ScreenHeight);

            for (var frame = 0; frame < scenario.Frames.Length; frame++)
            {
                scenario.Frames[frame](terminal);
                report.Append("  frame ").Append(frame + 1)
                    .Append(": cursor ").Append(terminal.CursorX).Append(',').Append(terminal.CursorY)
                    .AppendLine();

                for (var y = 0; y < ScreenHeight; y++)
                    report.Append("    row ").Append(y).Append(": ").AppendLine(DescribeRow(terminal, y));
            }

            report.AppendLine();
        }

        return Verifier.Verify(report.ToString()).UseDirectory("Snapshots");
    }

    [Fact]
    public Task DiffingTerminal_flushed_output_matches_the_approved_snapshot()
    {
        var report = new StringBuilder();

        foreach (var scenario in Scenarios)
        {
            report.Append("scenario: ").AppendLine(scenario.Name);
            var inner = new RecordingTerminal(ScreenWidth, ScreenHeight);
            var terminal = new DiffingTerminal(inner);

            for (var frame = 0; frame < scenario.Frames.Length; frame++)
            {
                scenario.Frames[frame](terminal);
                terminal.Flush();
                report.Append("  frame ").Append(frame + 1).AppendLine(":");
                report.Append(inner.TakeLog());
            }

            report.AppendLine();
        }

        return Verifier.Verify(report.ToString()).UseDirectory("Snapshots");
    }

    [Fact]
    public Task InlineTerminal_flushed_output_matches_the_approved_snapshot()
    {
        var report = new StringBuilder();

        foreach (var scenario in Scenarios)
        {
            report.Append("scenario: ").AppendLine(scenario.Name);
            var inner = new RecordingTerminal(ScreenWidth + InlineMarginColumns, ScreenHeight);
            var terminal = new InlineTerminal(inner, inner);

            for (var frame = 0; frame < scenario.Frames.Length; frame++)
            {
                scenario.Frames[frame](terminal);
                terminal.Flush();
                report.Append("  frame ").Append(frame + 1).AppendLine(":");
                report.Append(inner.TakeLog());
            }

            report.AppendLine();
        }

        return Verifier.Verify(report.ToString()).UseDirectory("Snapshots");
    }

    private static IEnumerable<Scenario> CorpusScenarios()
    {
        foreach (var text in DisplayWidthCorpusTests.Corpus)
        {
            var name = DisplayWidthCorpusTests.Escape(text);

            yield return new Scenario($"write {name}", [t => WriteAt(t, 0, 0, text)]);
            yield return new Scenario(
                $"write at column {ColumnOffset} {name}",
                [t => WriteAt(t, ColumnOffset, 0, text)]);
            yield return new Scenario($"region write {name}", [t => Region(t).WriteAt(0, 0, text)]);
            yield return new Scenario(
                $"region write from column -1 {name}",
                [t => Region(t).WriteAt(-1, 0, text)]);
        }
    }

    private static IEnumerable<Scenario> EdgeScenarios()
    {
        yield return new Scenario("wide char at the last column", [t => WriteAt(t, LastColumn, 0, Wide)]);
        yield return new Scenario(
            "wide char that ends at the last column",
            [t => WriteAt(t, LastColumn - 1, 0, Wide)]);
        yield return new Scenario("emoji at the last column", [t => WriteAt(t, LastColumn, 0, Emoji)]);
        yield return new Scenario(
            "wide char at the last column of the last row",
            [t => WriteAt(t, LastColumn, LastRow, Wide)]);
        yield return new Scenario(
            "text past the end of the last row",
            [t => WriteAt(t, 0, LastRow, "abcdef" + WidePair + "gh")]);
        yield return new Scenario("tab near the last column", [t => WriteAt(t, LastColumn - 1, 0, "\tX")]);
        yield return new Scenario(
            "wide char at the last region column",
            [t => Region(t).WriteAt(RegionWidth - 1, 0, Wide)]);
        yield return new Scenario(
            "wide char that ends at the last region column",
            [t => Region(t).WriteAt(RegionWidth - 2, 0, Wide)]);
        yield return new Scenario(
            "wide text cut by the region end",
            [t => Region(t).WriteAt(1, 0, WidePair)]);
        yield return new Scenario(
            "wide char cut by the region start",
            [t => Region(t).WriteAt(-1, 0, WidePair + "a")]);
        yield return new Scenario(
            "emoji cut by the region start",
            [t => Region(t).WriteAt(-1, 0, Emoji + "ab")]);
        yield return new Scenario(
            "write over the left half of a wide char",
            [t => WriteAt(t, 0, 0, WidePair), t => WriteAt(t, 0, 0, "x")]);
        yield return new Scenario(
            "write over the right half of a wide char",
            [t => WriteAt(t, 0, 0, WidePair), t => WriteAt(t, 1, 0, "x")]);
        yield return new Scenario(
            "wide write over the right half of a wide char",
            [t => WriteAt(t, 0, 0, WidePair), t => WriteAt(t, 1, 0, OtherWide)]);
        yield return new Scenario(
            "char write over the right half of a wide char",
            [t => WriteAt(t, 0, 0, WidePair), t => WriteCharsAt(t, 1, 0, "x")]);
        yield return new Scenario(
            "region write over the right half of a wide char",
            [t => Region(t).WriteAt(0, 0, WidePair), t => Region(t).WriteAt(1, 0, "x")]);
        yield return new Scenario("char writes", [t => WriteCharsAt(t, 0, 0, CharWrites)]);
        yield return new Scenario(
            $"char writes at column {ColumnOffset}",
            [t => WriteCharsAt(t, ColumnOffset, 0, CharWrites)]);
        yield return new Scenario("region char writes", [WriteRegionChars]);
    }

    private static void WriteRegionChars(IAnsiTerminal terminal)
    {
        var region = Region(terminal);
        region.WriteAt(0, 0, 'x');
        region.WriteAt(1, 0, '日');
        region.WriteAt(RegionWidth - 1, 0, '日');
        region.WriteAt(-1, 0, 'y');
        region.WriteAt(0, 1, '́');
        region.WriteAt(1, 1, '\uD83D');
        region.WriteAt(2, 1, '\t');
        region.WriteAt(3, 1, 'z');
    }

    private static void WriteAt(IAnsiTerminal terminal, int x, int y, string text)
    {
        terminal.MoveTo(x, y);
        terminal.Write(text);
    }

    private static void WriteCharsAt(IAnsiTerminal terminal, int x, int y, string chars)
    {
        terminal.MoveTo(x, y);
        foreach (var c in chars)
            terminal.Write(c);
    }

    private static RegionRenderContext Region(IAnsiTerminal terminal) =>
        new(terminal, RegionX, RegionY, RegionWidth, RegionHeight);

    private static string DescribeRow(VirtualTerminal terminal, int y)
    {
        var cells = TerminalCells.Row(terminal, y).Select(DescribeCell);
        return string.Join(" ", cells);
    }

    private static string DescribeCell(string cell)
    {
        if (cell == TerminalCells.Continuation)
            return Continuation;

        return DisplayWidthCorpusTests.Escape(cell);
    }

    private sealed record Scenario(string Name, Action<IAnsiTerminal>[] Frames);

    // Records each call that a buffering terminal sends to the terminal below it. Each cursor move
    // and each line break starts a new line of the log.
    private sealed class RecordingTerminal(int width, int height) : IAnsiTerminal, IInlineTerminalControl
    {
        private const string Indent = "    ";

        private readonly StringBuilder _log = new();
        private bool _lineEnded;

        public int Width => width;

        public int Height => height;

        public string TakeLog()
        {
            if (_log.Length > 0)
                _log.AppendLine();

            var log = _log.ToString();
            _log.Clear();
            _lineEnded = false;
            return log;
        }

        public void MoveTo(int x, int y) => StartLine($"move {x},{y}");

        public void Write(string text) => Append($"write {DisplayWidthCorpusTests.Escape(text)}");

        public void Write(char c) => Append($"char {DisplayWidthCorpusTests.Escape(c.ToString())}");

        public void SetForeground(Color color) => Append($"fg {color}");

        public void SetBackground(Color color) => Append($"bg {color}");

        public void ResetColors() => Append("reset");

        public void SaveCursor() => Append("save");

        public void RestoreCursor() => Append("restore");

        public void SetCursorVisible(bool visible) => Append($"cursor-visible {visible}");

        public void ClearRegion(int x, int y, int w, int h) => StartLine($"clear-region {x},{y},{w},{h}");

        public void ClearScreen() => StartLine("clear-screen");

        public void Flush() => Append("flush");

        public void EnterAlternateScreen() => Append("enter-alternate");

        public void ExitAlternateScreen() => Append("exit-alternate");

        public void EnableMouse() => Append("enable-mouse");

        public void DisableMouse() => Append("disable-mouse");

        public void EnableWheelScroll() => Append("enable-wheel");

        public void DisableWheelScroll() => Append("disable-wheel");

        public void CopyToClipboard(string text) => Append($"copy {DisplayWidthCorpusTests.Escape(text)}");

        public void MoveCursorUp(int rows) => StartLine($"up {rows}");

        public void MoveCursorDown(int rows) => Append($"down {rows}");

        public void MoveCursorToLineStart() => Append("line-start");

        public void EraseLine() => Append("erase-line");

        public void WriteLineBreak()
        {
            Append("line-break");
            _lineEnded = true;
        }

        private void StartLine(string call)
        {
            if (_log.Length > 0)
                _log.AppendLine();

            _log.Append(Indent).Append(call);
            _lineEnded = false;
        }

        private void Append(string call)
        {
            if (_log.Length == 0 || _lineEnded)
            {
                StartLine(call);
                return;
            }

            _log.Append(' ').Append(call);
        }
    }
}
