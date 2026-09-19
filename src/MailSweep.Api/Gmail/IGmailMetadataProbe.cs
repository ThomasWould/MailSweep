namespace MailSweep.Api.Gmail;

internal interface IGmailMetadataProbe
{
    Task<GmailMetadataProbeResponse> ProbeAsync(CancellationToken cancellationToken);
}
