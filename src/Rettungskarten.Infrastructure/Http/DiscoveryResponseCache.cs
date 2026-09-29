using System.Collections.Concurrent;

namespace Rettungskarten.Infrastructure.Http;

/// <summary>
/// Process-wide cache for discovery responses that several brand sources share - one portal page or
/// API response serving several brands (Mercedes' rk.mb-qr.com overview for Mercedes-Benz, AMG, EQ,
/// Maybach and smart; IFZ Berlin's list for Opel, Chevrolet, Cadillac and Saab). With every brand
/// running in parallel, each brand instance fetching the same (up to ~1.5MB) response again would
/// only add load on the manufacturer's server and wait time behind its per-host rate limit. The first
/// caller fetches, everyone else awaits the same task; a failed fetch is evicted so a later caller
/// retries instead of inheriting the failure.
/// </summary>
public sealed class DiscoveryResponseCache
{
    private readonly ConcurrentDictionary<string, Lazy<Task<string>>> _responses = new(StringComparer.Ordinal);

    public async Task<string> GetStringAsync(
        HttpClient client, string url, CancellationToken ct, Action<HttpRequestMessage>? configureRequest = null)
    {
        // The shared fetch isn't tied to the first caller's token (its cancellation would otherwise
        // fail every other brand awaiting the same response); each caller only stops waiting.
        var lazy = _responses.GetOrAdd(url, u => new Lazy<Task<string>>(() => FetchAsync(client, u, configureRequest, CancellationToken.None)));
        try
        {
            return await lazy.Value.WaitAsync(ct);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            _responses.TryRemove(new KeyValuePair<string, Lazy<Task<string>>>(url, lazy));
            throw;
        }
    }

    private static async Task<string> FetchAsync(
        HttpClient client, string url, Action<HttpRequestMessage>? configureRequest, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        configureRequest?.Invoke(request);
        using var response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }
}
