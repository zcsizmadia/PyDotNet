namespace PyDotNet.Marshaling;

/// <summary>
/// Normalizes the four awaitable shapes a delegate can return into a single
/// <see cref="Task{TResult}"/>, so the callback bridge has one thing to attach a
/// continuation to.
/// </summary>
/// <remarks>
/// A separate class because <see cref="DelegateBridge"/> is <see langword="unsafe"/>, and
/// C# does not permit <c>await</c> anywhere in an unsafe context.
/// </remarks>
internal static class TaskAdapters
{
    /// <summary>A <see cref="Task"/> has no result; the future completes with <c>None</c>.</summary>
    internal static async Task<object?> FromTask(object? value)
    {
        await ((Task)value!).ConfigureAwait(false);
        return null;
    }

    /// <summary>As <see cref="FromTask"/>, for <see cref="ValueTask"/>.</summary>
    internal static async Task<object?> FromValueTask(object? value)
    {
        await ((ValueTask)value!).ConfigureAwait(false);
        return null;
    }

    /// <summary>Boxes the result of a <c>Task&lt;T&gt;</c> for marshaling.</summary>
    internal static async Task<object?> FromTaskOf<T>(object? value)
    {
        return await ((Task<T>)value!).ConfigureAwait(false);
    }

    /// <summary>Boxes the result of a <c>ValueTask&lt;T&gt;</c> for marshaling.</summary>
    internal static async Task<object?> FromValueTaskOf<T>(object? value)
    {
        return await ((ValueTask<T>)value!).ConfigureAwait(false);
    }
}
