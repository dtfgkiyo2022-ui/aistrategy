using System;
using System.Collections.Generic;

namespace Rts.Contracts
{
    public readonly struct SimPoint
    {
        public Fix64 X { get; }
        public Fix64 Z { get; }

        public SimPoint(
            Fix64 x,
            Fix64 z)
        {
            X = x;
            Z = z;
        }
    }

}
