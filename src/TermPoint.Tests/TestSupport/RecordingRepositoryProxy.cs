using System.Reflection;
using System.Runtime.ExceptionServices;

namespace TermPoint.Tests.TestSupport;

/// <summary>
/// A forwarding decorator for any repository interface, built with <see cref="DispatchProxy"/> so one class
/// covers all of them. It forwards every call to a real repository, records the name of every method called,
/// and can be told to throw on the Nth call to a method (to simulate a failure partway through a write).
/// Used by the section import tests to prove the writer only ever calls <c>Insert</c>, and to make a real
/// insert fail in the middle of a transaction.
/// </summary>
/// <typeparam name="T">The repository interface.</typeparam>
public class RecordingRepositoryProxy<T> : DispatchProxy where T : class
{
    private readonly Dictionary<string, int> _callCounts = new();

    /// <summary>The real repository the calls are forwarded to.</summary>
    public T Target { get; set; } = null!;

    /// <summary>The name of every method called, in order (an overloaded method repeats its name).</summary>
    public List<string> Calls { get; } = new();

    /// <summary>
    /// When set, called before each forward with the method name and how many times that name has now been
    /// called (1 for the first); returning true makes the call throw <see cref="InjectedFailure"/> instead.
    /// </summary>
    public Func<string, int, bool>? FailWhen { get; set; }

    /// <summary>Thrown in place of a call that <see cref="FailWhen"/> selected.</summary>
    public sealed class InjectedFailure : Exception
    {
        /// <summary>Creates the exception for the named call.</summary>
        /// <param name="method">The method that was made to fail.</param>
        public InjectedFailure(string method) : base($"Injected failure in {method}.") { }
    }

    /// <summary>
    /// Wraps a repository.
    /// </summary>
    /// <param name="target">The real repository.</param>
    /// <param name="failWhen">Optional failure trigger; see <see cref="FailWhen"/>.</param>
    /// <returns>The decorated repository and the proxy that records its calls.</returns>
    public static (T Repository, RecordingRepositoryProxy<T> Proxy) Wrap(T target, Func<string, int, bool>? failWhen = null)
    {
        var repository = Create<T, RecordingRepositoryProxy<T>>();
        var proxy = (RecordingRepositoryProxy<T>)(object)repository;
        proxy.Target = target;
        proxy.FailWhen = failWhen;
        return (repository, proxy);
    }

    /// <inheritdoc/>
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        string name = targetMethod!.Name;
        Calls.Add(name);
        _callCounts[name] = _callCounts.GetValueOrDefault(name) + 1;

        if (FailWhen?.Invoke(name, _callCounts[name]) == true)
            throw new InjectedFailure(name);

        try
        {
            return targetMethod.Invoke(Target, args);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            // Reflection wraps the real exception; callers must see the original type.
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }
}
