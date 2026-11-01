using System.Net;
using System.Net.Http.Headers;

namespace MediaRenamer.Infrastructure.Http;

/// <summary>
/// Throttles requests and retries 429 / 5xx / network failures with backoff (honouring Retry-After).
/// Only bodiless requests (GET) are supported, because a request message cannot be resent.
/// </summary>
public sealed class RetryingHandler : DelegatingHandler
{
    private readonly TimeSpan _minInterval;
    private readonly int _maxRetries;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTime _nextSlotUtc = DateTime.MinValue;

    public RetryingHandler(TimeSpan? minInterval, int maxRetries, HttpMessageHandler inner,
        Func<TimeSpan, CancellationToken, Task>? delay = null) : base(inner)
    {
        _minInterval = minInterval ?? TimeSpan.Zero;
        _maxRetries = Math.Max(0, maxRetries);
        _delay = delay ?? Task.Delay;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.Content is not null)
            throw new NotSupportedException("RetryingHandler only supports requests without a body.");

        for (var attempt = 0; ; attempt++)
        {
            await WaitForSlotAsync(ct);

            HttpResponseMessage? response = null;
            Exception? failure = null;
            try
            {
                response = await base.SendAsync(attempt == 0 ? request : Clone(request), ct);
            }
            catch (HttpRequestException ex)
            {
                failure = ex;
            }

            var retryable = failure is not null || IsRetryable(response!.StatusCode);
            if (!retryable || attempt >= _maxRetries)
            {
                if (failure is not null) throw failure;
                return response!;
            }

            var wait = response is null ? Backoff(attempt) : RetryAfter(response) ?? Backoff(attempt);
            response?.Dispose();
            await _delay(wait, ct);
        }
    }

    private async Task WaitForSlotAsync(CancellationToken ct)
    {
        if (_minInterval <= TimeSpan.Zero) return;
        await _gate.WaitAsync(ct);
        try
        {
            var wait = _nextSlotUtc - DateTime.UtcNow;
            if (wait > TimeSpan.Zero) await _delay(wait, ct);
            _nextSlotUtc = DateTime.UtcNow + _minInterval;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static bool IsRetryable(HttpStatusCode code) => code == HttpStatusCode.TooManyRequests || (int)code >= 500;

    private static TimeSpan Backoff(int attempt) => TimeSpan.FromMilliseconds(Math.Min(500 * Math.Pow(2, attempt), 30_000));

    private static TimeSpan? RetryAfter(HttpResponseMessage r)
    {
        RetryConditionHeaderValue? h = r.Headers.RetryAfter;
        if (h is null) return null;
        TimeSpan? t = h.Delta ?? (h.Date is { } d ? d - DateTimeOffset.UtcNow : null);
        return t is { } v ? TimeSpan.FromSeconds(Math.Clamp(v.TotalSeconds, 0, 60)) : null;
    }

    private static HttpRequestMessage Clone(HttpRequestMessage r)
    {
        var copy = new HttpRequestMessage(r.Method, r.RequestUri) { Version = r.Version };
        foreach (var h in r.Headers) copy.Headers.TryAddWithoutValidation(h.Key, h.Value);
        return copy;
    }
}
