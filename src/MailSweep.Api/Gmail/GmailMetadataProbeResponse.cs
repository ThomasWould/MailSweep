namespace MailSweep.Api.Gmail;

public sealed record GmailMetadataProbeResponse(
    bool MessageFound,
    bool InternalDatePresent,
    bool SizeEstimatePresent,
    bool FromHeaderPresent,
    bool SubjectHeaderPresent);
