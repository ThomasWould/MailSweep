using System.Globalization;
using System.Net.Mail;
using MailSweep.Api.Mailbox.Contracts;

namespace MailSweep.Api.Mailbox.Scanning;

internal sealed class PromotionInsightsAggregator
{
    internal const int RankedResultLimit = 5;

    private readonly Dictionary<string, SenderTotals> senders = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Totals> domains = new(StringComparer.Ordinal);
    private int analyzedMessageCount;
    private long analyzedMessageBytes;
    private int unknownSenderMessageCount;
    private long unknownSenderMessageBytes;

    public void Add(string? from, long estimatedBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(estimatedBytes);
        analyzedMessageCount++;
        analyzedMessageBytes = checked(analyzedMessageBytes + estimatedBytes);

        var sender = Normalize(from);
        if (sender is null)
        {
            unknownSenderMessageCount++;
            unknownSenderMessageBytes = checked(unknownSenderMessageBytes + estimatedBytes);
            return;
        }

        if (!senders.TryGetValue(sender.EmailAddress, out var senderTotals))
        {
            senderTotals = new(sender.Domain);
            senders.Add(sender.EmailAddress, senderTotals);
        }
        senderTotals.Add(estimatedBytes);

        if (!domains.TryGetValue(sender.Domain, out var domainTotals))
        {
            domainTotals = new();
            domains.Add(sender.Domain, domainTotals);
        }
        domainTotals.Add(estimatedBytes);
    }

    public PromotionInsightsSummary Snapshot()
    {
        var topSenders = senders
            .Select(pair => new PromotionSenderInsight(
                pair.Key, pair.Value.Domain, pair.Value.MessageCount, pair.Value.EstimatedBytes))
            .OrderByDescending(sender => sender.MessageCount)
            .ThenByDescending(sender => sender.EstimatedBytes)
            .ThenBy(sender => sender.EmailAddress, StringComparer.Ordinal)
            .Take(RankedResultLimit)
            .ToArray();
        var topDomains = domains
            .Select(pair => new PromotionDomainInsight(
                pair.Key, pair.Value.MessageCount, pair.Value.EstimatedBytes))
            .OrderByDescending(domain => domain.MessageCount)
            .ThenByDescending(domain => domain.EstimatedBytes)
            .ThenBy(domain => domain.Domain, StringComparer.Ordinal)
            .Take(RankedResultLimit)
            .ToArray();

        return new(analyzedMessageCount, analyzedMessageBytes,
            unknownSenderMessageCount, unknownSenderMessageBytes,
            Array.AsReadOnly(topSenders), Array.AsReadOnly(topDomains));
    }

    internal static NormalizedPromotionSender? Normalize(string? from)
    {
        if (string.IsNullOrWhiteSpace(from) || !MailAddress.TryCreate(from.Trim(), out var address))
            return null;

        var emailAddress = address.Address.Trim();
        var at = emailAddress.LastIndexOf('@');
        if (at <= 0 || at == emailAddress.Length - 1)
            return null;

        var localPart = emailAddress[..at];
        var domain = emailAddress[(at + 1)..].TrimEnd('.');
        if (localPart.Any(char.IsWhiteSpace) || string.IsNullOrWhiteSpace(domain))
            return null;

        try
        {
            domain = new IdnMapping().GetAscii(domain).ToLowerInvariant();
        }
        catch (ArgumentException)
        {
            return null;
        }

        if (!domain.Contains('.') || Uri.CheckHostName(domain) != UriHostNameType.Dns)
            return null;

        return new($"{localPart.ToLowerInvariant()}@{domain}", domain);
    }

    internal sealed record NormalizedPromotionSender(string EmailAddress, string Domain);

    private class Totals
    {
        public int MessageCount { get; private set; }
        public long EstimatedBytes { get; private set; }

        public void Add(long estimatedBytes)
        {
            MessageCount++;
            EstimatedBytes = checked(EstimatedBytes + estimatedBytes);
        }
    }

    private sealed class SenderTotals(string domain) : Totals
    {
        public string Domain { get; } = domain;
    }
}
