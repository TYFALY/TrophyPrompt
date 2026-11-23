using System;
using System.Collections.Generic;
using System.Linq;

namespace TrophyPrompt.Core
{
    // Decides the order in which imported trophy edits are applied.
    //
    // The PS3 trophy model rejects a platinum unlock while any base-game trophy
    // is still locked, and rejects locking a base-game trophy while the platinum
    // is unlocked. Exports list the platinum first (id 0), so applying edits in
    // file order silently dropped the platinum. Order used here:
    //   1. lock platinum   2. other locks   3. other unlocks   4. unlock platinum
    // The sort is stable, so the file's own order is kept inside each group.
    public static class ImportOrder
    {
        public static IEnumerable<T> Sort<T>(IEnumerable<T> items, Func<T, int> index, Func<T, bool> unlocked, bool hasPlatinum)
        {
            return items.OrderBy(item =>
            {
                bool isPlatinum = hasPlatinum && index(item) == 0;
                bool u = unlocked(item);
                if (isPlatinum)
                {
                    return u ? 3 : 0;
                }
                return u ? 2 : 1;
            });
        }

        // Trophy ids in the order batch timestamp edits should walk them: the
        // platinum (id 0) last, so it never ends up dated before the trophies
        // it requires. Without a platinum, plain id order.
        public static IEnumerable<int> PlatinumLast(int count, bool hasPlatinum)
        {
            if (count <= 0)
            {
                return Enumerable.Empty<int>();
            }
            return hasPlatinum
                ? Enumerable.Range(1, count - 1).Append(0)
                : Enumerable.Range(0, count);
        }
    }

    // Maps an imported trophy entry to its TROPCONF index.
    //
    // A JSON entry without "Id" deserializes as Id 0, so trusting any in-range
    // Id sent every such entry to the platinum and dropped the rest as
    // duplicates. The Id wins only when its trophy's name agrees (or no name
    // was given); otherwise an exact name match is used, and an in-range Id is
    // still the last resort for a slightly reworded name.
    public sealed class TrophyMatcher
    {
        private readonly IReadOnlyList<string> _names;
        private readonly Dictionary<string, int> _byName;

        public TrophyMatcher(IReadOnlyList<string> names)
        {
            _names = names ?? Array.Empty<string>();
            _byName = new Dictionary<string, int>(_names.Count, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < _names.Count; i++)
            {
                if (_names[i] != null)
                {
                    // First occurrence wins, like the old linear search.
                    _byName.TryAdd(_names[i], i);
                }
            }
        }

        public int Resolve(int id, string name)
        {
            bool idInRange = id >= 0 && id < _names.Count;
            if (idInRange && (string.IsNullOrEmpty(name) || string.Equals(_names[id], name, StringComparison.OrdinalIgnoreCase)))
            {
                return id;
            }
            if (!string.IsNullOrEmpty(name) && _byName.TryGetValue(name, out int byName))
            {
                return byName;
            }
            return idInRange ? id : -1;
        }
    }

    public static class CsvField
    {
        // RFC 4180 quoting plus a guard against spreadsheet formula injection:
        // a cell starting with = + - @ (or tab/CR) is executed by Excel/Sheets.
        public static string Escape(string value)
        {
            value = value ?? string.Empty;
            if (value.Length > 0 && "=+-@\t\r".IndexOf(value[0]) >= 0)
            {
                value = "'" + value;
            }
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }
}
