namespace LTR.Catalogue;

/// <summary>
/// Collects progress reports on the calling thread.
/// </summary>
/// <remarks>
/// <see cref="Progress{T}"/> posts to a synchronisation context, which in a test means the callbacks may
/// not have run by the time the assertion does. This reports inline so an order can be asserted
/// deterministically.
/// </remarks>
internal sealed class SynchronousProgress<T> : IProgress<T>
{
    private readonly Action<T> _report;

    public SynchronousProgress(Action<T> report)
    {
        _report = report;
    }

    public void Report(T value)
    {
        _report(value);
    }
}
