namespace MailSweep.Api.Mailbox.Scanning;

internal static class MailboxScanSampling
{
    /// <summary>
    /// Visits positions in a base-two bisection order. Every position is yielded once, but
    /// any bounded prefix is spread across the source order instead of being concentrated
    /// at its beginning.
    /// </summary>
    public static IEnumerable<string> DistributedOrder(IReadOnlyList<string> ids)
    {
        if (ids.Count == 0) yield break;

        var gridSize = 1;
        var bitCount = 0;
        while (gridSize < ids.Count)
        {
            gridSize <<= 1;
            bitCount++;
        }

        var yielded = new bool[ids.Count];
        for (var ordinal = 1; ordinal < gridSize; ordinal++)
        {
            var position = (int)((long)ReverseBits(ordinal, bitCount) * ids.Count / gridSize);
            if (yielded[position]) continue;

            yielded[position] = true;
            yield return ids[position];
        }

        // The zero position is last in the bisection sequence.
        if (!yielded[0]) yield return ids[0];
    }

    private static int ReverseBits(int value, int bitCount)
    {
        var reversed = 0;
        for (var bit = 0; bit < bitCount; bit++)
        {
            reversed = (reversed << 1) | (value & 1);
            value >>= 1;
        }

        return reversed;
    }
}
