using System.Numerics;
using Rts.Contracts;

namespace Rts.Tests.EditMode
{
    /// <summary>Reference-only arithmetic for tests. This must never be called by simulation code.</summary>
    internal static class TestDistanceReference
    {
        internal static BigInteger Squared(SimPoint a, SimPoint b)
        {
            BigInteger x = new BigInteger(a.X.Raw) - b.X.Raw;
            BigInteger z = new BigInteger(a.Z.Raw) - b.Z.Raw;
            return x * x + z * z;
        }
    }
}
