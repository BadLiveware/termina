// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

using System.Reflection;
using Termina.Terminal;

namespace Termina.Tests.Terminal;

/// <summary>
/// Reads the cells of a <see cref="VirtualTerminal" /> as the terminal holds them: the text of each
/// cell, and a marker for the right half of a wide cell.
/// </summary>
/// <remarks>
/// <see cref="VirtualTerminal.GetChar" /> returns a space for both a space cell and the right half
/// of a wide cell, and <see cref="VirtualTerminal.GetLine" /> drops the right half, so neither
/// shows the width of a cell. The helper reads the private cell arrays instead. A renamed field
/// fails the type initializer with a clear message.
/// </remarks>
internal static class TerminalCells
{
    /// <summary>
    /// The marker for the right half of a wide cell.
    /// </summary>
    public const string Continuation = "<wide>";

    private static readonly FieldInfo TextField = Field("_textBuffer");
    private static readonly FieldInfo ContinuationField = Field("_continuation");

    /// <summary>
    /// Returns the cells of one row from left to right. The right half of a wide cell is
    /// <see cref="Continuation" />.
    /// </summary>
    public static IReadOnlyList<string> Row(VirtualTerminal terminal, int y)
    {
        var text = (string[,])TextField.GetValue(terminal)!;
        var continuation = (bool[,])ContinuationField.GetValue(terminal)!;
        var cells = new string[terminal.Width];

        for (var x = 0; x < terminal.Width; x++)
        {
            if (continuation[y, x])
            {
                cells[x] = Continuation;
                continue;
            }

            cells[x] = text[y, x];
        }

        return cells;
    }

    /// <summary>
    /// Returns the cells of one row from <paramref name="x" />, <paramref name="count" /> cells long.
    /// </summary>
    public static IReadOnlyList<string> Row(VirtualTerminal terminal, int y, int x, int count) =>
        Row(terminal, y).Skip(x).Take(count).ToArray();

    private static FieldInfo Field(string name) =>
        typeof(VirtualTerminal).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException($"VirtualTerminal has no field '{name}'. Update this helper.");
}
