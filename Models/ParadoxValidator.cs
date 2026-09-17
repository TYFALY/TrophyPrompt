using System;
using System.Collections.Generic;

namespace TrophyPrompt.Models
{
    // Plain snapshot of one grid row. Kept free of Avalonia/TROPHYParser
    // references so the rules below stay unit-testable in isolation.
    public sealed class ParadoxSnapshot
    {
        public int Id;
        public string Name = string.Empty;
        public bool IsUnlocked;
        public DateTime Timestamp = DateTime.MinValue;
        public bool IsPlatinum;
        public string Group = string.Empty;
        public int Order;
    }

    public sealed class ParadoxResult
    {
        public int Id;
        public string Reason = string.Empty;
    }

    public static class ParadoxValidator
    {
        // PS3 launch date (JP). Nothing legitimately unlocked can predate it.
        public static readonly DateTime Ps3LaunchEpoch = new DateTime(2006, 11, 11, 0, 0, 0);

        public static List<ParadoxResult> Validate(IEnumerable<ParadoxSnapshot> rows)
        {
            List<ParadoxSnapshot> list = new List<ParadoxSnapshot>(rows);
            List<ParadoxResult> results = new List<ParadoxResult>();

            // Condition A: locked trophies must not carry a timestamp.
            for (int i = 0; i < list.Count; i++)
            {
                if (!list[i].IsUnlocked && list[i].Timestamp.Ticks > 0)
                {
                    results.Add(new ParadoxResult
                    {
                        Id = list[i].Id,
                        Reason = "'" + list[i].Name + "' is locked but carries timestamp " + list[i].Timestamp.ToString("yyyy-MM-dd HH:mm:ss") + "."
                    });
                }
            }

            // Condition B: unlocked timestamps must not predate the PS3 epoch.
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].IsUnlocked && list[i].Timestamp < Ps3LaunchEpoch)
                {
                    results.Add(new ParadoxResult
                    {
                        Id = list[i].Id,
                        Reason = "'" + list[i].Name + "' is dated " + list[i].Timestamp.ToString("yyyy-MM-dd HH:mm:ss") + ", before the PS3 launch (2006-11-11)."
                    });
                }
            }

            // Condition C: an unlocked platinum must be the latest unlock overall.
            for (int i = 0; i < list.Count; i++)
            {
                if (!list[i].IsUnlocked || !list[i].IsPlatinum)
                {
                    continue;
                }
                for (int k = 0; k < list.Count; k++)
                {
                    if (k == i || !list[k].IsUnlocked)
                    {
                        continue;
                    }
                    if (list[i].Timestamp < list[k].Timestamp)
                    {
                        results.Add(new ParadoxResult
                        {
                            Id = list[i].Id,
                            Reason = "Platinum '" + list[i].Name + "' is dated before '" + list[k].Name + "' (" + list[k].Timestamp.ToString("yyyy-MM-dd HH:mm:ss") + ")."
                        });
                        break;
                    }
                }
            }

            // Condition D: same-group order inversion. Within one group, an
            // unlocked trophy dated before an earlier-listed unlocked trophy
            // in the same group is out of sequence.
            Dictionary<string, DateTime> groupHigh = new Dictionary<string, DateTime>(StringComparer.Ordinal);
            Dictionary<string, string> groupHighName = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < list.Count; i++)
            {
                if (!list[i].IsUnlocked)
                {
                    continue;
                }
                string group = list[i].Group ?? string.Empty;
                DateTime seen;
                if (groupHigh.TryGetValue(group, out seen) && list[i].Timestamp < seen)
                {
                    results.Add(new ParadoxResult
                    {
                        Id = list[i].Id,
                        Reason = "'" + list[i].Name + "' unlocked before '" + groupHighName[group] + "' in the same group (out of sequence)."
                    });
                }
                else if (!groupHigh.TryGetValue(group, out seen) || list[i].Timestamp > seen)
                {
                    groupHigh[group] = list[i].Timestamp;
                    groupHighName[group] = list[i].Name;
                }
            }

            return results;
        }

        public static bool Validate(IEnumerable<ParadoxSnapshot> rows, out string errorMessage)
        {
            List<ParadoxResult> results = Validate(rows);
            if (results.Count == 0)
            {
                errorMessage = null;
                return true;
            }
            errorMessage = results[0].Reason;
            return false;
        }
    }
}
