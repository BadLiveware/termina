// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

using System.Text;
using Termina.Terminal;

namespace Termina.Tests.Terminal;

/// <summary>
/// Pins the terminal-column semantics of every <see cref="DisplayWidth" /> operation against a
/// corpus of ASCII, CJK, fullwidth, Hangul, emoji, ZWJ sequences, variation selectors, keycaps,
/// regional indicators, combining marks, ANSI escape sequences, control characters, surrogate
/// pairs, and lone surrogates.
/// </summary>
/// <remarks>
/// The approved snapshot is the parity oracle. An implementation change that keeps the same
/// results — for example a move from a string-per-element scan to a span scan — leaves the
/// snapshot untouched. A change of the width or slice rules shows up as a snapshot diff.
/// </remarks>
public class DisplayWidthCorpusTests
{
    private static readonly string[] Corpus =
    [
        "",
        "hello",
        "\u65E5\u672C\u8A9E",
        "a\u65E5b",
        "\uD55C\uAD6D\uC5B4",
        "\uFF46\uFF55\uFF4C\uFF4C",
        "\uD83C\uDF89",
        "\uD83D\uDC68\u200D\uD83D\uDC69\u200D\uD83D\uDC67",
        "\uD83C\uDDF8\uD83C\uDDEA",
        "\u26A1\uFE0F",
        "1\uFE0F\u20E3",
        "e\u0301",
        "\u0301abc",
        "abc\u0301",
        "\u001b[31mred\u001b[0m",
        "\u001b]0;title\u0007ok",
        "\uD83D",
        "\uDE00",
        "\uD835\uDD4Fy",
        "tab\there",
        "line\nbreak",
        "a\r\nb",
        "e\u0301\u0302",
        "mixed \u65E5\u672C \uD83C\uDF89 text",
    ];

    private static readonly int[] ColumnLimits = [0, 1, 2, 3, 5, 10];

    private static readonly int[] StartColumns = [0, 1, 2, 4];

    [Fact]
    public Task Column_operations_match_the_approved_corpus()
    {
        var report = new StringBuilder();

        foreach (var text in Corpus)
        {
            report.Append("input: ").AppendLine(Escape(text));
            report.Append("  columns: ").Append(DisplayWidth.GetColumnCount(text)).AppendLine();
            report.Append("  cells: ").AppendLine(DescribeCells(text));

            foreach (var limit in ColumnLimits)
            {
                report.Append("  truncate(").Append(limit).Append("): ")
                    .AppendLine(Escape(DisplayWidth.TruncateToColumns(text, limit)));
                report.Append("  truncateStart(").Append(limit).Append("): ")
                    .AppendLine(Escape(DisplayWidth.TruncateStartToColumns(text, limit)));
                report.Append("  index(").Append(limit).Append("): ")
                    .Append(DisplayWidth.GetStringIndexForColumnCount(text, limit)).AppendLine();
            }

            foreach (var start in StartColumns)
            {
                foreach (var limit in ColumnLimits)
                {
                    report.Append("  slice(").Append(start).Append(", ").Append(limit).Append("): ")
                        .AppendLine(Escape(DisplayWidth.SliceByColumns(text, start, limit)));
                }
            }

            for (var index = 0; index <= text.Length; index++)
            {
                report.Append("  cursor(").Append(index).Append("): ")
                    .Append(DisplayWidth.CursorPositionToColumn(text, index))
                    .Append(" clamp=").Append(DisplayWidth.ClampToTextElementBoundary(text, index))
                    .Append(" previous=").Append(DisplayWidth.GetPreviousTextElementIndex(text, index))
                    .Append(" next=").Append(DisplayWidth.GetNextTextElementIndex(text, index))
                    .Append(" element=").AppendLine(Escape(DisplayWidth.GetTextElementAt(text, index)));
            }

            report.Append("  sanitized: ").AppendLine(Escape(DisplayWidth.SanitizeTerminalText(text)));
            report.AppendLine();
        }

        foreach (var c in "a\u65E5\t\u0301")
        {
            report.Append("char ").Append(Escape(c.ToString())).Append(": ")
                .Append(DisplayWidth.GetColumnCount(c)).AppendLine();
        }

        return Verifier.Verify(report.ToString()).UseDirectory("Snapshots");
    }

    [Theory]
    [InlineData("", 0)]
    [InlineData("hello", 5)]
    [InlineData("\u65E5\u672C\u8A9E", 6)]
    [InlineData("\uD83C\uDF89", 2)]
    [InlineData("e\u0301", 1)]
    [InlineData("1\uFE0F\u20E3", 2)]
    [InlineData("\u26A1\uFE0F", 2)]
    public void GetColumnCount_measures_terminal_columns(string text, int expected)
    {
        Assert.Equal(expected, DisplayWidth.GetColumnCount(text));
    }

    private static string DescribeCells(string text)
    {
        var cells = DisplayWidth.EnumerateCells(text)
            .Select(cell => $"{cell.StartIndex}+{cell.Length}={cell.ColumnWidth}");
        return string.Join(" | ", cells);
    }

    private static string Escape(string text)
    {
        var builder = new StringBuilder(text.Length + 2);
        builder.Append('"');

        foreach (var c in text)
        {
            if (c is >= ' ' and <= '~' && c != '"' && c != '\\')
            {
                builder.Append(c);
                continue;
            }

            builder.Append("\\u").Append(((int)c).ToString("X4"));
        }

        return builder.Append('"').ToString();
    }
}
