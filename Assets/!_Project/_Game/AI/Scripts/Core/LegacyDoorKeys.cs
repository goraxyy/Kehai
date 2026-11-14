using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Kehai.Karen
{
    // Door ordinals before sorted map construction cannot be mapped back to identities.
    // Match a complete token, including compound placement keys, never an ordinary tile key.
    public static class LegacyDoorKeys
    {
        static readonly Regex Ordinal = new Regex(@"(^|[^A-Za-z0-9_])door_[0-9]+(?=$|[^A-Za-z0-9_])");
        public static bool Contains(string key) => key != null && Ordinal.IsMatch(key);
        public static int Remove<T>(List<T> rows, Func<T, string> key) =>
            rows == null ? 0 : rows.RemoveAll(row => row != null && Contains(key(row)));
    }
}
