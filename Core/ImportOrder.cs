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
