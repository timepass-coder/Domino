namespace Relay.Sim;

/// <summary>
/// FNV-1a, 64-bit. Chosen because the algorithm is three lines long, has no
/// seed, no table and no endianness of its own, so two machines running it
/// over identical bytes cannot disagree. It is not cryptographic and does
/// not need to be - it only has to answer "did these two runs diverge?".
/// </summary>
public static class Fnv1a64
{
    const ulong OffsetBasis = 14695981039346656037UL;
    const ulong Prime = 1099511628211UL;

    public static ulong Hash(byte[] bytes, int count)
    {
        ulong h = OffsetBasis;

        unchecked
        {
            for (int i = 0; i < count; i++)
            {
                h ^= bytes[i];
                h *= Prime;
            }
        }

        return h;
    }
}