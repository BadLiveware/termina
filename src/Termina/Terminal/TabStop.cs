// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

namespace Termina.Terminal;

/// <summary>
/// Expands a tab into the spaces that move the cursor to the next tab stop of its row.
/// </summary>
internal static class TabStop
{
    private const int Interval = 8;

    /// <summary>
    /// Returns the number of spaces from <paramref name="column"/> to the next tab stop. As in a
    /// terminal, the last column of the row is the last tab stop, so a tab never moves the cursor
    /// to the next row.
    /// </summary>
    public static int GetSpaceCount(int column, int rowWidth)
    {
        var nextStop = Math.Min(((column / Interval) + 1) * Interval, rowWidth - 1);
        return Math.Max(0, nextStop - column);
    }
}
