// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

using Termina.Clipboard;
using Termina.Input;
using Termina.Layout;
using Termina.Rendering;
using Termina.Terminal;
using Termina.Tests.Terminal;

namespace Termina.Tests.Layout;

/// <summary>
/// Cell-level render tests for the text nodes with wide text: the cell of the cursor, a wide cell
/// that the horizontal scroll cuts, and the wrap of a long CJK or emoji line.
/// </summary>
/// <remarks>
/// Each row is compared cell by cell. <see cref="TerminalCells.Continuation" /> is the right half of
/// a wide cell, so a test shows where each wide cell starts and that no cell holds half of one.
/// </remarks>
public class TextNodeWideTextRenderTests
{
    private const string Wide = TerminalCells.Continuation;
    private const string Blank = " ";
    private const string Family = "👨‍👩‍👧";

    [Fact]
    public void TextInputNode_cursor_on_a_wide_char_covers_both_cells()
    {
        using var node = new TextInputNode();
        node.HandlePaste(new PasteEvent("你好"));
        node.CursorPosition = 0;

        var terminal = Render(node, width: 6, height: 1);

        Assert.Equal(["你", Wide, "好", Wide, Blank, Blank], TerminalCells.Row(terminal, 0));
        Assert.Equal(node.CursorColor, terminal.GetBackground(0, 0));
        Assert.Equal(node.CursorColor, terminal.GetBackground(1, 0));
        Assert.NotEqual(node.CursorColor, terminal.GetBackground(2, 0));
    }

    [Fact]
    public void TextInputNode_horizontal_scroll_leaves_a_cut_wide_char_blank()
    {
        using var node = new TextInputNode();
        node.HandlePaste(new PasteEvent("ab你好世界"));

        var terminal = Render(node, width: 6, height: 1);

        // The scroll starts at column 5, the right half of 好. The node does not draw half of a
        // wide char, so the first cell stays blank.
        Assert.Equal([Blank, "世", Wide, "界", Wide, Blank], TerminalCells.Row(terminal, 0));
        Assert.Equal(node.CursorColor, terminal.GetBackground(5, 0));
    }

    [Fact]
    public void TextInputNode_scrolls_by_columns_past_an_emoji()
    {
        using var node = new TextInputNode();
        node.HandlePaste(new PasteEvent("a😀b"));
        node.CursorPosition = "a😀".Length;

        var terminal = Render(node, width: 3, height: 1);

        Assert.Equal(["😀", Wide, "b"], TerminalCells.Row(terminal, 0));
        Assert.Equal(node.CursorColor, terminal.GetBackground(2, 0));
    }

    [Fact]
    public void TextInputNode_selection_of_a_wide_char_covers_both_cells()
    {
        using var node = new TextInputNode();
        node.HandlePaste(new PasteEvent("你好A"));
        node.CursorPosition = 0;
        node.HandleInput(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, true, false, false));

        var terminal = Render(node, width: 6, height: 1);

        Assert.Equal(["你", Wide, "好", Wide, "A", Blank], TerminalCells.Row(terminal, 0));
        Assert.Equal(node.SelectionColor, terminal.GetBackground(0, 0));
        Assert.Equal(node.SelectionColor, terminal.GetBackground(1, 0));
        Assert.Equal(node.CursorColor, terminal.GetBackground(2, 0));
        Assert.NotEqual(node.SelectionColor, terminal.GetBackground(4, 0));
    }

    [Fact]
    public void TextAreaNode_wraps_a_long_cjk_line_on_whole_chars()
    {
        using var node = new TextAreaNode();
        node.Text = "你好世界你好";
        node.MoveCursorToEnd();

        var terminal = Render(node, width: 5, height: 3);

        Assert.Equal(["你", Wide, "好", Wide, Blank], TerminalCells.Row(terminal, 0));
        Assert.Equal(["世", Wide, "界", Wide, Blank], TerminalCells.Row(terminal, 1));
        Assert.Equal(["你", Wide, "好", Wide, Blank], TerminalCells.Row(terminal, 2));
        Assert.Equal(node.CursorColor, terminal.GetBackground(4, 2));
    }

    [Fact]
    public void TextAreaNode_wraps_an_emoji_line_on_whole_text_elements()
    {
        using var node = new TextAreaNode();
        node.Text = Family + "ab🎉cd";
        node.MoveCursorToEnd();

        var terminal = Render(node, width: 3, height: 3);

        Assert.Equal([Family, Wide, "a"], TerminalCells.Row(terminal, 0));
        Assert.Equal(["b", "🎉", Wide], TerminalCells.Row(terminal, 1));
        Assert.Equal(["c", "d", Blank], TerminalCells.Row(terminal, 2));
        Assert.Equal(node.CursorColor, terminal.GetBackground(2, 2));
    }

    [Fact]
    public void TextAreaNode_cursor_on_a_wide_char_covers_both_cells()
    {
        using var node = new TextAreaNode();
        node.Text = "a你b";
        node.CursorPosition = 1;

        var terminal = Render(node, width: 5, height: 1);

        Assert.Equal(["a", "你", Wide, "b", Blank], TerminalCells.Row(terminal, 0));
        Assert.NotEqual(node.CursorColor, terminal.GetBackground(0, 0));
        Assert.Equal(node.CursorColor, terminal.GetBackground(1, 0));
        Assert.Equal(node.CursorColor, terminal.GetBackground(2, 0));
        Assert.NotEqual(node.CursorColor, terminal.GetBackground(3, 0));
    }

    [Fact]
    public void CopyableTextNode_wraps_emoji_on_whole_text_elements()
    {
        var node = new CopyableTextNode(new NoClipboard(), "😀😀😀");
        node.OnFocused();

        var terminal = Render(node, width: 4, height: 3);

        Assert.Equal(["😀", Wide, "😀", Wide], TerminalCells.Row(terminal, 0));
        Assert.Equal(["😀", Wide, Blank, Blank], TerminalCells.Row(terminal, 1));
        Assert.Equal(node.FocusedBackground, terminal.GetBackground(0, 0));
        Assert.Equal(node.FocusedBackground, terminal.GetBackground(1, 0));
        Assert.NotEqual(node.FocusedBackground, terminal.GetBackground(2, 0));
    }

    [Fact]
    public void CopyableTextNode_moves_a_wide_char_that_does_not_fit_to_the_next_row()
    {
        var node = new CopyableTextNode(new NoClipboard(), "你好世");

        var terminal = Render(node, width: 3, height: 4);

        Assert.Equal(["你", Wide, Blank], TerminalCells.Row(terminal, 0));
        Assert.Equal(["好", Wide, Blank], TerminalCells.Row(terminal, 1));
        Assert.Equal(["世", Wide, Blank], TerminalCells.Row(terminal, 2));
    }

    [Fact]
    public void CopyableTextNode_selection_of_an_emoji_covers_both_cells()
    {
        var node = new CopyableTextNode(new NoClipboard(), "😀A");
        node.OnFocused();
        node.HandleInput(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, true, false, false));

        var terminal = Render(node, width: 4, height: 2);

        Assert.Equal(["😀", Wide, "A", Blank], TerminalCells.Row(terminal, 0, 0, 4));
        Assert.Equal(node.SelectionBackground, terminal.GetBackground(0, 0));
        Assert.Equal(node.SelectionBackground, terminal.GetBackground(1, 0));
        Assert.NotEqual(node.SelectionBackground, terminal.GetBackground(2, 0));
    }

    private static VirtualTerminal Render(ILayoutNode node, int width, int height)
    {
        var terminal = new VirtualTerminal(width, height);
        var context = new RegionRenderContext(terminal, 0, 0, width, height);
        node.Measure(new Size(width, height));
        node.Render(context, new Rect(0, 0, width, height));
        return terminal;
    }

    private sealed class NoClipboard : IClipboardService
    {
        public bool Copy(string text) => true;
    }
}
