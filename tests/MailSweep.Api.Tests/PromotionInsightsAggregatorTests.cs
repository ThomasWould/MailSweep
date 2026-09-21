using MailSweep.Api.Mailbox.Scanning;

namespace MailSweep.Api.Tests;

public sealed class PromotionInsightsAggregatorTests
{
    [Theory]
    [InlineData("Retail Team <Offers@EXAMPLE.COM>", "offers@example.com", "example.com")]
    [InlineData("  newsletter@Sub.Example.COM  ", "newsletter@sub.example.com", "sub.example.com")]
    public void NormalizeUsesAddressAndLowercaseDomain(
        string value, string expectedAddress, string expectedDomain)
    {
        var sender = PromotionInsightsAggregator.Normalize(value);

        Assert.NotNull(sender);
        Assert.Equal(expectedAddress, sender.EmailAddress);
        Assert.Equal(expectedDomain, sender.Domain);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-an-address")]
    [InlineData("sender@localhost")]
    public void NormalizeRejectsMissingOrUnparseableSenders(string? value) =>
        Assert.Null(PromotionInsightsAggregator.Normalize(value));

    [Fact]
    public void AggregatesNormalizedSendersDomainsSizesAndUnknowns()
    {
        var aggregator = new PromotionInsightsAggregator();
        aggregator.Add("Store <Deals@Example.COM>", 100);
        aggregator.Add("deals@example.com", 250);
        aggregator.Add("news@example.com", 75);
        aggregator.Add("unparseable", 25);

        var result = aggregator.Snapshot();

        Assert.Equal(4, result.AnalyzedMessageCount);
        Assert.Equal(450, result.AnalyzedMessageBytes);
        Assert.Equal(1, result.UnknownSenderMessageCount);
        Assert.Equal(25, result.UnknownSenderMessageBytes);
        Assert.Collection(result.TopSenders,
            sender =>
            {
                Assert.Equal("deals@example.com", sender.EmailAddress);
                Assert.Equal("example.com", sender.Domain);
                Assert.Equal(2, sender.MessageCount);
                Assert.Equal(350, sender.EstimatedBytes);
            },
            sender =>
            {
                Assert.Equal("news@example.com", sender.EmailAddress);
                Assert.Equal(1, sender.MessageCount);
                Assert.Equal(75, sender.EstimatedBytes);
            });
        var domain = Assert.Single(result.TopDomains);
        Assert.Equal("example.com", domain.Domain);
        Assert.Equal(3, domain.MessageCount);
        Assert.Equal(425, domain.EstimatedBytes);
    }
}
