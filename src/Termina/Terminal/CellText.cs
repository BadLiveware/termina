// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Termina.Terminal;

/// <summary>
/// Materializes one Unicode text element as the string a terminal cell holds.
/// </summary>
/// <remarks>
/// A frame writes one text element for every occupied cell, so ASCII elements come from a
/// shared table instead of a new string for each cell of each frame.
/// </remarks>
internal static class CellText
{
    private const int AsciiCacheLength = 128;

    private static readonly string[] AsciiCache = CreateAsciiCache();

    public static string From(ReadOnlySpan<char> element)
    {
        if (element.Length == 1 && element[0] < AsciiCacheLength)
            return AsciiCache[element[0]];

        return new string(element);
    }

    private static string[] CreateAsciiCache()
    {
        var cache = new string[AsciiCacheLength];
        for (var i = 0; i < AsciiCacheLength; i++)
            cache[i] = ((char)i).ToString();

        return cache;
    }
}
