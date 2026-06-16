using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Questionable.Utils;

internal class CacheUtils
{
    public struct CachedValue<T>(double ttlSeconds)
    {
        private T _value;
        private long _timestamp;
        private readonly long _ttl = (long)(Stopwatch.Frequency * ttlSeconds);

        public T Get(Func<T> compute)
        {
            var now = Stopwatch.GetTimestamp();
            if (now - _timestamp > _ttl)
            {
                _value = compute();
                _timestamp = now;
            }
            return _value;
        }
    }
}
