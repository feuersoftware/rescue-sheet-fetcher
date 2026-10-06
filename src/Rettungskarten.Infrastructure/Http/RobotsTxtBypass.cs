namespace Rettungskarten.Infrastructure.Http;

/// <summary>
/// Opt-in exception to the robots.txt check (<c>fetch rescue-cards --ignore-robots-txt &lt;brand...&gt;</c>):
/// requests made inside an <see cref="Enable"/> scope skip <see cref="RobotsTxtDelegatingHandler"/> - no
/// robots.txt fetch, no refusal. Everything else in the pipeline (rate limiter, timeouts, redirects)
/// still applies.
///
/// The scope is an <see cref="AsyncLocal{T}"/>, i.e. it covers exactly the async flow that opened it:
/// the CLI opens it around one brand's run (<see cref="RunAsync{T}"/>), so only that brand's own requests
/// are affected even while every other brand runs in parallel and keeps honouring robots.txt - including
/// brands on the same host.
///
/// Ambient state has one catch: work shared between brands runs in whichever brand's flow started it.
/// Anything that caches or shares a response across brands must therefore key on <see cref="IsActive"/>
/// (as <see cref="DiscoveryResponseCache"/> does), or a bypassed response reaches a brand that honours
/// robots.txt. Passing the bypass explicitly instead would mean threading it through every source.
/// </summary>
public static class RobotsTxtBypass
{
    private static readonly AsyncLocal<bool> Active = new();

    public static bool IsActive => Active.Value;

    public static IDisposable Enable()
    {
        var previous = Active.Value;
        Active.Value = true;
        return new Scope(previous);
    }

    /// <summary>Runs <paramref name="action"/> inside a bypass scope if <paramref name="enabled"/>. Being
    /// an async method is what makes this safe to call for several brands from one synchronous loop: the
    /// scope covers the whole action, and the caller's flow - where the next brand starts - gets its
    /// previous state back as soon as the action first awaits.</summary>
    public static async Task<T> RunAsync<T>(bool enabled, Func<Task<T>> action)
    {
        using var scope = enabled ? Enable() : null;
        return await action();
    }

    private sealed class Scope(bool previous) : IDisposable
    {
        public void Dispose() => Active.Value = previous;
    }
}
