// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

namespace Termina.Tests.Utility;

/// <summary>
/// Time limits for tests that wait for work on other threads.
/// </summary>
internal static class TestTimeouts
{
    /// <summary>
    /// The time after which a wait counts as a hang. Wait for the work to end and then check its result.
    /// Do not wait for a short time and fail when the work is late.
    /// </summary>
    /// <remarks>
    /// A test that runs the real event loop waits for thread-pool continuations. When the full suite
    /// runs on a busy machine, the thread-pool queue holds hundreds of work items, and one continuation
    /// can wait for more than 10 seconds. A short limit then fails a correct test.
    /// </remarks>
    public static readonly TimeSpan Hang = TimeSpan.FromMinutes(1);
}
