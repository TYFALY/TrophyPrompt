using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using TrophyPrompt.Core;
using TrophyPrompt.Models;
using Xunit;

namespace TrophyPrompt.Tests
{
    public class ImportOrderTests
    {
        private static List<int> Order(bool hasPlatinum, params (int id, bool unlocked)[] items)
        {
            return ImportOrder.Sort(items, x => x.id, x => x.unlocked, hasPlatinum).Select(x => x.id).ToList();
        }

        [Fact]
        public void PlatinumUnlockIsAppliedLast()
        {
            // Exports list the platinum first; it must be applied after the rest.
            Assert.Equal(new[] { 1, 2, 3, 0 }, Order(true, (0, true), (1, true), (2, true), (3, true)));
        }

        [Fact]
        public void PlatinumLockIsAppliedFirst()
        {
            Assert.Equal(new[] { 0, 2, 1 }, Order(true, (1, true), (2, false), (0, false)));
        }

        [Fact]
        public void LocksComeBeforeUnlocksAndOrderIsStable()
        {
            Assert.Equal(new[] { 4, 2, 3, 1, 0 }, Order(true, (0, true), (3, true), (4, false), (1, true), (2, false)));
            Assert.Equal(new[] { 4, 2, 0, 3, 1 }, Order(false, (0, true), (3, true), (4, false), (1, true), (2, false)));
        }
    }

    public class TrophyMatcherTests
    {
        private static readonly TrophyMatcher Matcher = new TrophyMatcher(new[] { "Platinum", "First Steps", "Collector", "Collector" });

        [Fact]
        public void EntriesWithoutIdMatchByName()
        {
            // A JSON entry with no "Id" deserializes as Id 0 (the platinum).
            TrophyDto dto = JsonConvert.DeserializeObject<TrophyDto>("{\"Name\":\"first steps\",\"Unlocked\":true}");
            Assert.Equal(0, dto.Id);
            Assert.Equal(1, Matcher.Resolve(dto.Id, dto.Name));
        }

        [Theory]
        [InlineData(1, "First Steps", 1)]   // id and name agree
        [InlineData(1, null, 1)]            // no name: trust the id
        [InlineData(1, "First Step!", 1)]   // reworded name: keep the id
        [InlineData(0, "Platinum", 0)]
        [InlineData(2, "First Steps", 1)]   // name points elsewhere: name wins
        [InlineData(3, "Collector", 3)]     // duplicate names: the id picks which
        [InlineData(9, "Collector", 2)]     // out-of-range id: first name match
        [InlineData(-1, "Unknown", -1)]
        [InlineData(4, null, -1)]
        public void ResolvesIdAndNameConsistently(int id, string name, int expected)
        {
            Assert.Equal(expected, Matcher.Resolve(id, name));
        }

        [Fact]
        public void EmptyListMatchesNothing()
        {
            Assert.Equal(-1, new TrophyMatcher(null).Resolve(0, "Platinum"));
        }
    }

    public class CsvFieldTests
    {
        [Theory]
        [InlineData("Plain", "\"Plain\"")]
        [InlineData("Say \"hi\"", "\"Say \"\"hi\"\"\"")]
        [InlineData("=HYPERLINK(\"x\")", "\"'=HYPERLINK(\"\"x\"\")\"")]
        [InlineData("+1", "\"'+1\"")]
        [InlineData("-cmd", "\"'-cmd\"")]
        [InlineData("@SUM", "\"'@SUM\"")]
        [InlineData(null, "\"\"")]
        public void EscapesQuotesAndFormulas(string input, string expected)
        {
            Assert.Equal(expected, CsvField.Escape(input));
        }
    }

    public class TrophyUtilityTests
    {
        [Fact]
        public void LongRandomStaysInRange()
        {
            var rand = new Random(1);
            for (int i = 0; i < 1000; i++)
            {
                long v = TrophyUtility.LongRandom(100, 200, rand);
                Assert.InRange(v, 100, 199);
            }
        }

        [Fact]
        public void LongRandomWithEmptyRangeDoesNotThrow()
        {
            // Used to throw DivideByZeroException.
            Assert.Equal(50, TrophyUtility.LongRandom(50, 50, new Random()));
            Assert.Equal(50, TrophyUtility.LongRandom(50, 10, new Random()));
        }

        [Fact]
        public void UnixTimestampRoundTrips()
        {
            var dt = new DateTime(2012, 3, 4, 5, 6, 7, DateTimeKind.Utc);
            Assert.Equal(dt, dt.DateTimeToTimeStamp().TimeStampToDateTime());
        }

        [Fact]
        public void CopyTrophyDataOnlyCopiesTrophyFiles()
        {
            string src = TrophyUtility.GetTemporaryDirectory();
            string dst = Path.Combine(TrophyUtility.GetTemporaryDirectory(), "out");
            try
            {
                File.WriteAllText(Path.Combine(src, "TROPTRNS.DAT"), "x");
                File.WriteAllText(Path.Combine(src, "PARAM.PFD"), "x");
                File.WriteAllText(Path.Combine(src, "notes.txt"), "x");
                TrophyUtility.CopyTrophyData(src, dst, true);
                Assert.True(File.Exists(Path.Combine(dst, "TROPTRNS.DAT")));
                Assert.True(File.Exists(Path.Combine(dst, "PARAM.PFD")));
                Assert.False(File.Exists(Path.Combine(dst, "notes.txt")));
            }
            finally
            {
                TrophyUtility.DeleteDirectory(src);
                TrophyUtility.DeleteDirectory(Path.GetDirectoryName(dst));
            }
        }

        [Fact]
        public void TryDeleteDirectoryRemovesNestedTree()
        {
            string dir = TrophyUtility.GetTemporaryDirectory();
            Directory.CreateDirectory(Path.Combine(dir, "sub"));
            File.WriteAllText(Path.Combine(dir, "sub", "TROPTRNS.DAT"), "x");
            Assert.True(TrophyUtility.TryDeleteDirectory(dir));
            Assert.False(Directory.Exists(dir));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void TryDeleteDirectoryIgnoresEmptyPaths(string path)
        {
            Assert.True(TrophyUtility.TryDeleteDirectory(path));
        }

        [Fact]
        public void TryDeleteDirectoryIgnoresMissingFolder()
        {
            string dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Assert.True(TrophyUtility.TryDeleteDirectory(dir));
        }

        private static byte[] SfoBytes(int length, int offset)
        {
            byte[] data = new byte[length];
            if (length >= 0x10)
            {
                BitConverter.GetBytes(offset).CopyTo(data, 0xC);
            }
            return data;
        }

        private static MemoryStream Sfo(int length, int offset)
        {
            return new MemoryStream(SfoBytes(length, offset));
        }

        [Fact]
        public void ReadProfileIdReadsSixteenBytesAtOffset()
        {
            byte[] data = SfoBytes(0x40, 0x20);
            for (int i = 0; i < 0x10; i++)
            {
                data[0x20 + i] = (byte)(i + 1);
            }
            using (MemoryStream ms = new MemoryStream(data))
            {
                byte[] id = TrophyUtility.ReadProfileId(ms, "p.sfo");
                Assert.Equal(Enumerable.Range(1, 16).Select(i => (byte)i).ToArray(), id);
                Assert.True(ms.CanRead); // caller owns the stream
            }
        }

        [Theory]
        [InlineData(0, 0)]                // empty file
        [InlineData(8, 0)]                // shorter than the header
        [InlineData(0x40, -1)]            // negative offset
        [InlineData(0x40, 0x38)]          // id would run past the end
        [InlineData(0x40, int.MaxValue)]  // used to overflow past the length check
        public void ReadProfileIdRejectsInvalidFiles(int length, int offset)
        {
            using (MemoryStream ms = Sfo(length, offset))
            {
                Assert.Throws<InvalidDataException>(() => TrophyUtility.ReadProfileId(ms, "p.sfo"));
            }
        }

        [Theory]
        [InlineData(10, 10, 10)]
        [InlineData(10, 12, 11)]
        [InlineData(0, 0, 0)]
        public void EnsureTablesCoverAcceptsMatchingTables(int trophies, int types, int times)
        {
            TrophyUtility.EnsureTablesCover(trophies, types, times);
        }

        [Theory]
        [InlineData(10, 9, 10)]
        [InlineData(10, 10, 9)]
        [InlineData(10, 0, 0)]
        public void EnsureTablesCoverRejectsShortTables(int trophies, int types, int times)
        {
            Assert.Throws<InvalidDataException>(() => TrophyUtility.EnsureTablesCover(trophies, types, times));
        }
    }

    public class TimestampJsonTests
    {
        private static TrophyDto Parse(string ts)
        {
            return JsonConvert.DeserializeObject<TrophyDto>("{\"Id\":1,\"Name\":\"A\",\"Unlocked\":true,\"Timestamp\":" + ts + "}");
        }

        [Theory]
        [InlineData("\"2013-05-06 07:08:09\"")]
        [InlineData("\"2013-05-06T07:08:09\"")]
        [InlineData("\"2013/05/06 07:08:09\"")]
        public void AcceptsCommonLlmFormats(string ts)
        {
            Assert.Equal(new DateTime(2013, 5, 6, 7, 8, 9), Parse(ts).Timestamp);
        }

        [Theory]
        [InlineData("null")]
        [InlineData("\"\"")]
        [InlineData("\"not a date\"")]
        public void InvalidValuesBecomeMinValue(string ts)
        {
            Assert.Equal(DateTime.MinValue, Parse(ts).Timestamp);
        }

        [Fact]
        public void ExportRoundTrips()
        {
            var root = new ExportRootDto
            {
                GameTitle = "Game",
                TitleId = "NPWR00000_00",
                Trophies = { new TrophyDto { Id = 0, Name = "Plat", Unlocked = true, Timestamp = new DateTime(2014, 1, 2, 3, 4, 5) } },
            };
            string json = JsonConvert.SerializeObject(root);
            Assert.Contains("\"2014-01-02 03:04:05\"", json);
            var back = JsonConvert.DeserializeObject<ExportRootDto>(json);
            Assert.Equal(root.Trophies[0].Timestamp, back.Trophies[0].Timestamp);
        }
    }
}
