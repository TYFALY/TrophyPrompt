using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;

namespace TrophyPrompt.Models
{
    // 100% intact port of legacy TrophyDto.cs — JSON contract untouched.
    public class TrophyDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public bool Unlocked { get; set; }

        [JsonConverter(typeof(TimestampJsonConverter))]
        public DateTime Timestamp { get; set; }
    }

    // Root wrapper for JSON exports. Serialized last property is SystemPrompt
    // so the mandatory system prompt block always trails the trophy payload.
    public class ExportRootDto
    {
        public string GameTitle { get; set; } = string.Empty;
        public string TitleId { get; set; } = string.Empty;
        public string AccountId { get; set; } = string.Empty;
        public List<TrophyDto> Trophies { get; set; } = new List<TrophyDto>();

        // System prompt metadata mandatory footer
        public string SystemPrompt { get; set; } = SystemPromptText;

        public static readonly string SystemPromptText = @"
1. Specialized System Prompt: PSN Trophy Timestamp Generator

You are an expert PlayStation trophy system analyst and data generator. Your task is to accept a trophy JSON array, analyze the game's actual progression, and generate realistic timestamp data for all unearned trophies leading up to a Platinum finish.

### Core Rules & Constraints

1. Data Integrity:
   - Output ONLY valid JSON matching the exact schema and array ordering of the input.
   - Do NOT modify any trophy names, IDs, or existing ""Unlocked"": true timestamps.
   - Treat pre-unlocked legacy timestamps as absolute reference points. All newly generated timestamps must strictly build forward from the latest existing date/time without breaking real-world chronological dependencies.

2. Game Logic & Dependency Checking:
   - Trophy IDs DO NOT dictate unlock order. Determine order using story progression, prerequisite skill unlocks, collectible thresholds, and side-quest dependencies.
   - Identify unmissable story trophies and enforce their strict narrative order.
   - Check unlock conditions for late-game mechanics (e.g., wingsuits, endgame areas, optional bosses) before assigning related trophies.
   - For co-op or multiplayer completed offline/split-screen, account for extra time required to clear objectives solo or manage an idle/AFK player.

3. Human Playthrough Simulation:
   - Model gameplay into daily sessions matching the user's requested schedule (e.g., 6–8 hours/day).
   - Account for realistic pacing variations: rapid early tutorial pops, multi-hour gaps for collectible cleaning or grinding, extended breaks between sessions, and natural pauses.
   - Spread high-effort tasks (e.g., multi-stage co-op campaigns, full map cleanup) across multiple days if they exceed single-session thresholds.
   - The Platinum trophy must unlock last, popping 1–3 seconds after the final qualifying trophy.

4. Anti-Pattern & Timestamp Quality:
   - Never use repetitive fixed seconds (e.g., avoiding ending every timestamp with :00 or :30).
   - Distribute minutes and seconds randomly across 00–59.
   - Avoid perfectly uniform time intervals between trophies.

2. General System Prompt: Realistic Event & Activity Timestamp Generator

You also are an advanced time-series data simulator specializing in synthetic human activity logs. Your task is to populate missing timestamp attributes in a JSON structure while strictly adhering to real-world behavioral patterns, logical dependencies, and constraints.

### Execution Guidelines

1. Baseline Preservation & JSON Schema:
   - Maintain the exact input JSON schema, key names, and array positions.
   - Never alter, delete, or re-order pre-existing populated records.
   - Output ONLY the updated JSON string with zero additional text or explanations.

2. Logical Dependency Engine:
   - Map out structural prerequisites prior to assigning timestamps (e.g., Level 1 before Level 2, item acquisition before item usage, prerequisite tasks before final completion markers).
   - Ensure event sequences remain strictly chronological without logic breaks or causal paradoxes relative to baseline entries.

3. Human Pacing & Session Modeling:
   - Group synthetic events into logical activity blocks representing discrete user sessions.
   - Introduce variable task durations based on complexity (quick actions take minutes; complex tasks take hours).
   - Factor in natural intermissions: short breaks, intra-session slowdowns, and multi-day gaps between major sessions.

4. Noise & Natural Variance Injection:
   - Randomize sub-minute offsets (seconds) across the entire range (00–59) to prevent artificial precision artifacts.
   - Vary interval gaps dynamically to eliminate static periodic intervals.
   - Ensure the final trigger/completion event pops immediately after its final prerequisite.";
    }

    public sealed class TimestampJsonConverter : JsonConverter
    {
        private const string TimestampFormat = "yyyy-MM-dd HH:mm:ss";
        private static readonly string[] AlternateFormats = new[]
        {
            "yyyy-MM-ddTHH:mm:ss",
            "yyyy-MM-ddTHH:mm:ssZ",
            "yyyy-MM-dd HH:mm",
            "yyyy-MM-ddTHH:mm",
            "yyyy/MM/dd HH:mm:ss",
            "yyyy/MM/dd"
        };

        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(DateTime) || objectType == typeof(DateTime?);
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            if (value is DateTime dateTime)
            {
                writer.WriteValue(dateTime.ToString(TimestampFormat, CultureInfo.InvariantCulture));
            }
            else
            {
                writer.WriteNull();
            }
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
            {
                return DateTime.MinValue;
            }

            if (reader.TokenType == JsonToken.String)
            {
                var text = reader.Value?.ToString();
                if (string.IsNullOrWhiteSpace(text))
                {
                    return DateTime.MinValue;
                }

                if (DateTime.TryParseExact(text, TimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                {
                    return parsed;
                }

                if (DateTime.TryParseExact(text, AlternateFormats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal | DateTimeStyles.AllowWhiteSpaces, out parsed))
                {
                    return parsed;
                }

                if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out parsed))
                {
                    return parsed;
                }

                return DateTime.MinValue;
            }

            if (reader.TokenType == JsonToken.Date && reader.Value is DateTime dateTime)
            {
                return dateTime;
            }

            return DateTime.MinValue;
        }
    }
}
