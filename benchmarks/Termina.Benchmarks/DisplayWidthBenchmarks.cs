// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

using System.Text;
using BenchmarkDotNet.Attributes;
using Termina.Layout;
using Termina.Rendering;
using Termina.Terminal;

namespace Termina.Benchmarks;

/// <summary>
/// Benchmarks the column operations of <see cref="DisplayWidth"/> on one screen row of text, and
/// one full frame of text rows through a <see cref="DiffingTerminal"/>.
/// </summary>
[MemoryDiagnoser]
public class DisplayWidthBenchmarks
{
    private const int Width = 120;
    private const int Height = 30;
    private const int SliceStartColumn = 10;
    private const int SliceColumns = 60;

    private string _line = null!;
    private DiffingTerminal _terminal = null!;
    private ILayoutNode _frame = null!;
    private RegionRenderContext _context = null!;
    private Rect _bounds;

    public enum RowContent
    {
        Ascii,
        Cjk,
        Emoji,
    }

    [ParamsAllValues]
    public RowContent Content { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _line = BuildRow(Content);
        _terminal = new DiffingTerminal(new BenchmarkTerminal(Width, Height));
        _frame = new VerticalLayout(Enumerable.Range(0, Height)
            .Select(_ => new TextNode(_line) as ILayoutNode)
            .ToArray());
        _context = new RegionRenderContext(_terminal, 0, 0, Width, Height);
        _bounds = new Rect(0, 0, Width, Height);
    }

    [Benchmark]
    public int GetColumnCount() => DisplayWidth.GetColumnCount(_line);

    [Benchmark]
    public string SliceByColumns() => DisplayWidth.SliceByColumns(_line, SliceStartColumn, SliceColumns);

    [Benchmark]
    public string TruncateToColumns() => DisplayWidth.TruncateToColumns(_line, SliceColumns);

    [Benchmark]
    public string TruncateStartToColumns() => DisplayWidth.TruncateStartToColumns(_line, SliceColumns);

    [Benchmark]
    public void TerminalWriteRow()
    {
        _terminal.MoveTo(0, 0);
        _terminal.Write(_line);
    }

    [Benchmark]
    public void RenderFrame()
    {
        _terminal.ClearScreen();
        _frame.Measure(new Size(Width, Height));
        _frame.Render(_context, _bounds);
        _terminal.Flush();
    }

    private static string BuildRow(RowContent content)
    {
        var unit = content switch
        {
            RowContent.Ascii => "The quick brown fox jumps over the lazy dog. ",
            RowContent.Cjk => "終端の幅を測る文字列テスト한국어",
            RowContent.Emoji => "ok \U0001F44D ship \U0001F680 team \U0001F468\u200D\U0001F469\u200D\U0001F467 flag \U0001F1F8\U0001F1EA ",
            _ => throw new ArgumentOutOfRangeException(nameof(content), content, null),
        };

        var sb = new StringBuilder();
        while (DisplayWidth.GetColumnCount(sb.ToString() + unit) <= Width)
            sb.Append(unit);

        foreach (var cell in DisplayWidth.EnumerateCells(unit))
        {
            if (DisplayWidth.GetColumnCount(sb.ToString() + cell.Text) > Width)
                break;
            sb.Append(cell.Text);
        }

        return sb.ToString();
    }
}
