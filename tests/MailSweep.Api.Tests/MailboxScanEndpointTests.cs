using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MailSweep.Api.Mailbox.Contracts;
using MailSweep.Api.Mailbox.Scanning;

namespace MailSweep.Api.Tests;

public sealed class MailboxScanEndpointTests
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    [Fact]
    public async Task RoutesRequireAuthenticationAndTrustedOriginAndScopeJobsToOwner()
    {
        using var factory = new MailSweepApiFactory();
        using var anonymous = factory.CreateHttpsClient();
        using var anonymousStart = await PostAsync(anonymous, "/api/mailbox/scans", trustedOrigin: true);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousStart.StatusCode);
        AssertNoStore(anonymousStart);

        using var owner = factory.CreateHttpsClient();
        owner.DefaultRequestHeaders.Add("X-Test-User", "owner@example.com");
        using var missingOrigin = await owner.PostAsync("/api/mailbox/scans", null);
        Assert.Equal(HttpStatusCode.Forbidden, missingOrigin.StatusCode);
        AssertNoStore(missingOrigin);

        using var start = await PostAsync(owner, "/api/mailbox/scans", trustedOrigin: true);
        Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);
        AssertNoStore(start);
        var queued = await ReadAsync<MailboxScanProgress>(start);
        Assert.Equal($"/api/mailbox/scans/{queued.ScanId}", start.Headers.Location?.ToString());
        Assert.Equal(MailboxScanStatus.Queued, queued.Status);
        Assert.Contains("\"status\":\"queued\"", await start.Content.ReadAsStringAsync());

        using var other = factory.CreateHttpsClient();
        other.DefaultRequestHeaders.Add("X-Test-User", "other@example.com");
        using var hidden = await other.GetAsync($"/api/mailbox/scans/{queued.ScanId}");
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        AssertNoStore(hidden);

        var completed = await WaitForTerminalAsync(owner, queued.ScanId);
        Assert.Equal(MailboxScanStatus.Completed, completed.Status);
        using var summaryResponse = await owner.GetAsync($"/api/mailbox/scans/{queued.ScanId}/summary");
        Assert.Equal(HttpStatusCode.OK, summaryResponse.StatusCode);
        AssertNoStore(summaryResponse);
        var summary = await ReadAsync<MailboxScanSummary>(summaryResponse);
        Assert.Equal(123, summary.ProfileMessageCount);
        Assert.Equal(0, summary.PromotionInsights.AnalyzedMessageCount);
        Assert.Empty(summary.PromotionInsights.TopSenders);

        using var hiddenSummary = await other.GetAsync($"/api/mailbox/scans/{queued.ScanId}/summary");
        Assert.Equal(HttpStatusCode.NotFound, hiddenSummary.StatusCode);
    }

    [Fact]
    public async Task CancellationRequiresOwnerAndTrustedOriginAndDisposesJobSource()
    {
        var source = new FakeScanSource
        {
            List = async (_, token) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return new MailboxIdPage([], null);
            }
        };
        using var factory = new MailSweepApiFactory(scanSourceFactory: () => source);
        using var owner = factory.CreateHttpsClient();
        owner.DefaultRequestHeaders.Add("X-Test-User", "owner@example.com");
        using var start = await PostAsync(owner, "/api/mailbox/scans", trustedOrigin: true);
        var queued = await ReadAsync<MailboxScanProgress>(start);

        using var forged = await owner.PostAsync($"/api/mailbox/scans/{queued.ScanId}/cancel", null);
        Assert.Equal(HttpStatusCode.Forbidden, forged.StatusCode);

        using var other = factory.CreateHttpsClient();
        other.DefaultRequestHeaders.Add("X-Test-User", "other@example.com");
        using var hidden = await PostAsync(other, $"/api/mailbox/scans/{queued.ScanId}/cancel", trustedOrigin: true);
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);

        using var cancelledResponse = await PostAsync(owner,
            $"/api/mailbox/scans/{queued.ScanId}/cancel", trustedOrigin: true);
        Assert.Equal(HttpStatusCode.Accepted, cancelledResponse.StatusCode);
        AssertNoStore(cancelledResponse);
        var cancelled = await ReadAsync<MailboxScanProgress>(cancelledResponse);
        Assert.Equal(MailboxScanStatus.Cancelled, cancelled.Status);
        Assert.True(source.Disposed);
    }

    [Fact]
    public async Task SourceFailureModelIsReturnedAsSafeScanStatus()
    {
        var source = new FakeScanSource
        {
            List = (_, _) => Task.FromException<MailboxIdPage>(
                new MailboxSourceException(MailboxSourceFailure.Authentication))
        };
        using var factory = new MailSweepApiFactory(scanSourceFactory: () => source);
        using var client = factory.CreateHttpsClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "owner@example.com");
        using var start = await PostAsync(client, "/api/mailbox/scans", trustedOrigin: true);
        Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);
        var queued = await ReadAsync<MailboxScanProgress>(start);

        var failed = await WaitForTerminalAsync(client, queued.ScanId);

        Assert.Equal(MailboxScanStatus.Failed, failed.Status);
        Assert.Equal("authentication_required", failed.StatusReason);
        Assert.True(source.Disposed);
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string path, bool trustedOrigin)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path);
        if (trustedOrigin)
            request.Headers.Add("Origin", "https://localhost:5173");
        return await client.SendAsync(request);
    }

    private static async Task<MailboxScanProgress> WaitForTerminalAsync(HttpClient client, Guid scanId)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true)
        {
            using var response = await client.GetAsync($"/api/mailbox/scans/{scanId}", timeout.Token);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            AssertNoStore(response);
            var progress = await ReadAsync<MailboxScanProgress>(response);
            if (progress.Status is MailboxScanStatus.Completed or MailboxScanStatus.Cancelled or MailboxScanStatus.Failed)
                return progress;
            await Task.Delay(5, timeout.Token);
        }
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response) where T : class =>
        Assert.IsType<T>(await response.Content.ReadFromJsonAsync<T>(JsonOptions));

    private static void AssertNoStore(HttpResponseMessage response) =>
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
