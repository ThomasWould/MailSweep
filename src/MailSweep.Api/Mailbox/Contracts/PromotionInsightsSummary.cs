namespace MailSweep.Api.Mailbox.Contracts;

public sealed record PromotionInsightsSummary(
    int AnalyzedMessageCount,
    long AnalyzedMessageBytes,
    int UnknownSenderMessageCount,
    long UnknownSenderMessageBytes,
    IReadOnlyList<PromotionSenderInsight> TopSenders,
    IReadOnlyList<PromotionDomainInsight> TopDomains);

public sealed record PromotionSenderInsight(
    string EmailAddress,
    string Domain,
    int MessageCount,
    long EstimatedBytes);

public sealed record PromotionDomainInsight(
    string Domain,
    int MessageCount,
    long EstimatedBytes);
