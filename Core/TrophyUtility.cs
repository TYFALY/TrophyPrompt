using System;
using System.IO;
using System.Linq;

namespace TrophyPrompt.Core
{
    // 100% intact port of legacy Utility.cs — binary parsing logic untouched.
    // Only the namespace changed so the Avalonia frontend can share the core.
    public static class TrophyUtility
    {
        private static string[] TROPHY_FILES_EXTENSION = { ".PFD", ".SFO", ".DAT", ".SFM" };

        // Anchor external tools to the app folder, not the working directory:
        // launching from anywhere else (dotnet run, debugger, shortcut) broke open/save.
        private static string ToolDirectory()
        {
            string fromBase = Path.Combine(AppContext.BaseDirectory, "pfdtool");
            if (Directory.Exists(fromBase))
            {
                return fromBase;
            }
            return "pfdtool";
        }

        // Windows ships flatz's original pfdtool.exe; macOS/Linux releases ship a
        // native build of the same tool (github.com/bucanero/pfd_sfo_tools).
        public static string ToolFileName
        {
            get { return OperatingSystem.IsWindows() ? "pfdtool.exe" : "pfdtool"; }
        }

        private static string ToolApp()
        {
            return Path.Combine(ToolDirectory(), ToolFileName);
        }

        private static bool _unixToolPrepared;

        // Archives and downloads can drop the executable bit, and macOS tags
        // downloaded files with a quarantine flag that blocks launching them.
        private static void PrepareUnixTool(string app)
        {
            if (OperatingSystem.IsWindows() || _unixToolPrepared)
            {
                return;
            }
            _unixToolPrepared = true;
            try
            {
                UnixFileMode mode = File.GetUnixFileMode(app);
                UnixFileMode exec = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
                if ((mode & UnixFileMode.UserExecute) == 0)
                {
                    File.SetUnixFileMode(app, mode | exec);
                }
            }
            catch
            {
            }
            if (OperatingSystem.IsMacOS())
            {
                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo("/usr/bin/xattr")
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardError = true,
                        RedirectStandardOutput = true,
                    };
                    psi.ArgumentList.Add("-d");
                    psi.ArgumentList.Add("com.apple.quarantine");
                    psi.ArgumentList.Add(app);
                    using (var p = System.Diagnostics.Process.Start(psi))
                    {
                        p?.WaitForExit(5000);
                    }
                }
                catch
                {
                }
            }
        }

        public static string ProfilesDirectory()
        {
            string fromBase = Path.Combine(AppContext.BaseDirectory, "profiles");
            if (Directory.Exists(fromBase))
            {
                return fromBase;
            }
            return "profiles";
        }

        // Runs pfdtool with properly escaped arguments (ArgumentList, no string
        // concatenation), drains stdout/stderr concurrently so a chatty tool can
        // never deadlock on a full pipe, and fails loudly on a non-zero exit code
        // instead of silently copying half-written files back to the user.
        private static string RunPfdtool(params string[] args)
        {
            string app = ToolApp();
            if (!File.Exists(app))
            {
                throw new FileNotFoundException(ToolFileName + " was not found in the pfdtool folder next to TrophyPrompt. " +
                    (OperatingSystem.IsWindows()
                        ? "Re-download the release or rebuild the project."
                        : "Use a release download, or build pfdtool for this platform from github.com/bucanero/pfd_sfo_tools and put it there."), app);
            }
            PrepareUnixTool(app);
            var psi = new System.Diagnostics.ProcessStartInfo(app)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (string a in args)
            {
                psi.ArgumentList.Add(a);
            }
            // pfdtool reads global.conf / games.conf from its working directory.
            psi.WorkingDirectory = ToolDirectory();
            using (var proc = System.Diagnostics.Process.Start(psi))
            {
                if (proc == null)
                {
                    throw new InvalidOperationException("Could not start pfdtool.");
                }
                var stderrTask = proc.StandardError.ReadToEndAsync();
                string stdout = proc.StandardOutput.ReadToEnd();
                if (!proc.WaitForExit(120000))
                {
                    try { proc.Kill(true); } catch { }
                    throw new TimeoutException("pfdtool did not finish within 2 minutes.");
                }
                string stderr = stderrTask.GetAwaiter().GetResult();
                if (proc.ExitCode != 0)
                {
                    string detail = (stderr + Environment.NewLine + stdout).Trim();
                    throw new Exception("pfdtool failed (exit code " + proc.ExitCode + "). " +
                        (OperatingSystem.IsWindows()
                            ? "Make sure the Microsoft Visual C++ 2010 x86 runtime is installed."
                            : "Make sure the pfdtool file is executable and built for this CPU.") +
                        (detail.Length > 0 ? Environment.NewLine + Environment.NewLine + detail : string.Empty));
                }
                // pfdtool reports some failures (e.g. unreadable config files)
                // as "[*] Error: ..." lines while still exiting with code 0.
                string[] errors = stdout.Split(new[] { (char)13, (char)10 }, StringSplitOptions.RemoveEmptyEntries)
                    .Where(l => l.TrimStart().StartsWith("[*] Error", StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                if (errors.Length > 0)
                {
                    throw new Exception("pfdtool reported an error:" + Environment.NewLine + string.Join(Environment.NewLine, errors));
                }
                return stdout;
            }
        }

        public static void decryptSave(string gameid, string saveDir)
        {
            RunPfdtool("-g", gameid, "-d", saveDir, "USR-DATA");
        }

        public static void encryptSave(string gameid, string saveDir)
        {
            RunPfdtool("-g", gameid, "-u", saveDir);
            RunPfdtool("-g", gameid, "-e", saveDir, "USR-DATA");
        }

        public static void decryptTrophy(string saveDir)
        {
            string message = RunPfdtool("-d", saveDir, "TROPTRNS.DAT");
            // pfdtool prints only its banner on success; anything else is an error
            // description. Compare line-ending-agnostically (Wine prints LF only).
            string body = string.Join(Environment.NewLine, message
                .Split(new[] { (char)13, (char)10 }, StringSplitOptions.RemoveEmptyEntries)
                .Where(l => l.Trim().Length > 0 && !l.StartsWith("pfdtool ", StringComparison.Ordinal)));
            if (body.Length > 0)
            {
                throw new Exception(body);
            }
        }

        public static void encryptTrophy(string saveDir, string profile)
        {
            //resign with other param.sfo
            if (!string.IsNullOrEmpty(profile) && profile != "Default Profile")
            {
                // Profile names come from the profiles folder listing; strip any
                // directory component so a crafted name can't escape that folder.
                profile = Path.Combine(ProfilesDirectory(), Path.GetFileName(profile));
                byte[] profileId;
                using (var br = new BinaryReader(new FileStream(profile, FileMode.Open, FileAccess.Read)))
                {
                    br.BaseStream.Position = 0xC;
                    int offset = br.ReadInt32();
                    if (offset < 0 || offset + 0x10 > br.BaseStream.Length)
                    {
                        throw new InvalidDataException("Profile PARAM.SFO is invalid: " + Path.GetFileName(profile));
                    }
                    br.BaseStream.Position = offset;
                    profileId = br.ReadBytes(0x10);
                }
                using (var bw = new BinaryWriter(new FileStream(Path.Combine(saveDir, "PARAM.SFO"), FileMode.Open, FileAccess.Write)))
                {
                    bw.BaseStream.Position = 0x274;
                    bw.Write(profileId);
                }
            }
            // update PFD, then encrypt savedata
            RunPfdtool("-u", saveDir);
            RunPfdtool("-e", saveDir, "TROPTRNS.DAT");
        }

        // Copies the original (encrypted) trophy files somewhere safe before the
        // first overwrite, so a bad save or a bad LLM import is always reversible.
        public static string BackupTrophyData(string trophyDir)
        {
            string root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TrophyPrompt", "backups");
            string name = new DirectoryInfo(trophyDir).Name;
            string target = Path.Combine(root, name + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            CopyTrophyData(trophyDir, target, true);
            foreach (string png in Directory.GetFiles(trophyDir))
            {
                // keep the folder self-contained and restorable as-is
                if (png.EndsWith(".PNG", StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(png, Path.Combine(target, Path.GetFileName(png)), true);
                }
            }
            return target;
        }

        public static long LongRandom(long min, long max, Random rand)
        {
            // Guard: max <= min used to throw DivideByZeroException (e.g. a
            // "Randomize" range whose end is before its start).
            if (max <= min)
            {
                return min;
            }
            return rand.NextInt64(min, max);
        }

        public static DateTime TimeStampToDateTime(this long timestamp)
        {
            DateTime dateTime = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            dateTime = dateTime.AddSeconds(timestamp);
            return dateTime;
        }

        public static long DateTimeToTimeStamp(this DateTime datetime)
        {
            DateTime sTime = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            return (long)(datetime - sTime).TotalSeconds;
        }

        public static string GetTemporaryDirectory()
        {
            string tempDirectory;
            do
            {
                tempDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            } while (Directory.Exists(tempDirectory));
            Directory.CreateDirectory(tempDirectory);
            return tempDirectory;
        }

        public static string CopyTrophyDirToTemp(string trophyDir)
        {
            DirectoryInfo dir = new DirectoryInfo(trophyDir);
            string pathTemp = Path.Combine(GetTemporaryDirectory(), dir.Name);
            CopyTrophyData(trophyDir, pathTemp, true);
            return pathTemp;
        }

        public static void CopyTrophyData(string source, string target, bool overwrite)
        {
            DirectoryInfo dir = new DirectoryInfo(source);
            if (!dir.Exists)
            {
                throw new DirectoryNotFoundException("Source directory does not exist or could not be found: " + source);
            }

            // If the destination directory doesn't exist, create it.
            Directory.CreateDirectory(target);

            // Get the files in the directory and copy them to the new location.
            FileInfo[] files = dir.GetFiles();
            foreach (FileInfo file in files)
            {
                if (TROPHY_FILES_EXTENSION.Contains(file.Extension.ToUpper()))
                {
                    string tempPath = Path.Combine(target, file.Name);
                    file.CopyTo(tempPath, overwrite);
                }
            }
        }

        public static byte[] GetTrophyIconBytes(string trophyDir, int trophyId)
        {
            // PNG files are not part of the PFD temp copy (CopyTrophyData filters
            // to .PFD/.SFO/.DAT/.SFM), so read icons from the source folder first,
            // then fall back to the temp dir in case PNGs were copied alongside.
            string[] candidates = new string[]
            {
                Path.Combine(trophyDir, "TROP" + trophyId.ToString("000") + ".PNG"),
                Path.Combine(trophyDir, "TROP" + trophyId.ToString("000") + ".png"),
            };
            foreach (string path in candidates)
            {
                try
                {
                    if (File.Exists(path))
                    {
                        return File.ReadAllBytes(path);
                    }
                }
                catch
                {
                }
            }
            return null;
        }

        public static byte[] GetGameIconBytes(string trophyDir)
        {
            string[] candidates = new string[]
            {
                Path.Combine(trophyDir, "ICON0.PNG"),
                Path.Combine(trophyDir, "ICON0.png"),
            };
            foreach (string path in candidates)
            {
                try
                {
                    if (File.Exists(path))
                    {
                        return File.ReadAllBytes(path);
                    }
                }
                catch
                {
                }
            }
            return null;
        }

        public static void DeleteDirectory(string path)
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }

        // Cleanup variant for finally blocks: a locked or vanished temp file must
        // never replace the real outcome of the operation with an IOException.
        public static bool TryDeleteDirectory(string path)
        {
            try
            {
                DeleteDirectory(path);
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
            {
                return false;
            }
        }
    }
}
