namespace SonarTask.Core {
/// <summary>
/// Stateless keyed pseudo-randomness. Values are derived from explicit simulation keys,
/// so rendering frame rate does not consume or reorder a mutable PRNG stream.
/// </summary>
public static class DeterministicNoise {
    public static ulong Hash(ulong x) {
        x += 0x9E3779B97F4A7C15UL;
        x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
        x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
        return x ^ (x >> 31);
    }

    public static ulong StringHash(string s) {
        ulong h = 1469598103934665603UL;
        if (s != null) foreach (char c in s) { h ^= c; h *= 1099511628211UL; }
        return h;
    }

    public static double Unit(long seed, int phase, string id, int waterfall, long scan, int channel) {
        ulong x = unchecked((ulong)seed)
                  ^ ((ulong)(uint)phase << 32)
                  ^ StringHash(id)
                  ^ ((ulong)(uint)waterfall * 0xD6E8FEB86659FD93UL)
                  ^ ((ulong)scan * 0xA0761D6478BD642FUL)
                  ^ (uint)channel;
        return (Hash(x) >> 11) * (1.0 / 9007199254740992.0);
    }

    public static double Signed(long seed, int phase, string id, int waterfall, long scan, int channel) =>
        Unit(seed, phase, id, waterfall, scan, channel) * 2.0 - 1.0;

    public static double Smooth(long seed, int phase, string id, int waterfall, long scan, int channel, int period) {
        if (period < 1) period = 1;
        // Use mathematical floor division so historical reconstruction also behaves
        // smoothly for scan indices before phase time zero. Positive indices retain
        // exactly the same behavior as before.
        long a = scan >= 0 ? scan / period : -(((-scan) + period - 1) / period);
        long b = a + 1;
        long remainder = scan - a * period;
        double t = remainder / (double)period;
        t = t * t * (3.0 - 2.0 * t);
        double va = Signed(seed, phase, id, waterfall, a, channel);
        double vb = Signed(seed, phase, id, waterfall, b, channel);
        return va + (vb - va) * t;
    }
}
}
