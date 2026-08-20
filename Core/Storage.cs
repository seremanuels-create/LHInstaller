using System;
using System.IO;
using System.Reflection;
using System.Text;

namespace LHInstaller
{
    // Dove finiscono i file. L'app e' portatile: per prima cosa prova a scrivere
    // accanto a se stessa, cosi' la chiavetta si porta dietro anche il profilo.
    // Se la chiavetta e' protetta da scrittura, ripiega su AppData senza fermarsi.
    public static class Storage
    {
        public const string ProfileFileName = "LHInstaller.json";

        private static string _dataDir;

        public static string AppDir()
        {
            string exe = Assembly.GetExecutingAssembly().Location;
            return Path.GetDirectoryName(exe);
        }

        public static string DataDir()
        {
            if (_dataDir != null) return _dataDir;

            string beside = AppDir();
            if (IsWritable(beside)) { _dataDir = beside; return _dataDir; }

            string appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LHInstaller");
            try { Directory.CreateDirectory(appData); }
            catch { }
            _dataDir = appData;
            return _dataDir;
        }

        public static bool DataDirIsPortable()
        {
            return string.Equals(DataDir(), AppDir(), StringComparison.OrdinalIgnoreCase);
        }

        public static string ProfilePath()
        {
            return Path.Combine(DataDir(), ProfileFileName);
        }

        public static string DownloadDir()
        {
            string d = Path.Combine(DataDir(), "Download");
            try { Directory.CreateDirectory(d); }
            catch
            {
                d = Path.Combine(Path.GetTempPath(), "LHInstaller");
                Directory.CreateDirectory(d);
            }
            return d;
        }

        public static string LogDir()
        {
            string d = Path.Combine(DataDir(), "Log");
            try { Directory.CreateDirectory(d); }
            catch { }
            return d;
        }

        private static bool IsWritable(string dir)
        {
            try
            {
                string probe = Path.Combine(dir, ".lhi-prova");
                File.WriteAllText(probe, "x");
                File.Delete(probe);
                return true;
            }
            catch { return false; }
        }

        // La lingua serve PRIMA che la finestra venga costruita, mentre il profilo
        // completo si carica dopo. Quindi la sbircio dal file con una lettura minima,
        // che non puo' fallire in modo rumoroso: se qualcosa non va, "auto".
        public static string PeekLanguage()
        {
            try
            {
                string path = ProfilePath();
                if (!File.Exists(path)) return Tr.Auto;
                string text = File.ReadAllText(path, Encoding.UTF8);
                int i = text.IndexOf("\"Language\"", StringComparison.OrdinalIgnoreCase);
                if (i < 0) return Tr.Auto;
                int colon = text.IndexOf(':', i);
                if (colon < 0) return Tr.Auto;
                int q1 = text.IndexOf('"', colon);
                if (q1 < 0) return Tr.Auto;
                int q2 = text.IndexOf('"', q1 + 1);
                if (q2 <= q1) return Tr.Auto;
                return Tr.Normalize(text.Substring(q1 + 1, q2 - q1 - 1));
            }
            catch { return Tr.Auto; }
        }

        // ---------- profilo ----------

        public static Profile Load()
        {
            string path = ProfilePath();
            if (!File.Exists(path)) return Profile.CreateEmpty();
            try
            {
                Profile p = Json.Read<Profile>(path);
                if (p == null) return Profile.CreateEmpty();
                if (p.Groups == null) p.Groups = new System.Collections.Generic.List<string>();
                if (p.Items == null) p.Items = new System.Collections.Generic.List<AppItem>();
                foreach (AppItem it in p.Items) p.EnsureGroup(it.Group);
                if (p.Groups.Count == 0) p.EnsureGroup(Groups.General);
                return p;
            }
            catch (Exception ex)
            {
                // Un profilo illeggibile non va sovrascritto in silenzio: lo metto da parte.
                try
                {
                    string bad = path + ".illeggibile-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                    File.Move(path, bad);
                }
                catch { }
                throw new Exception(Tr.F(
                    "Il profilo non e' leggibile ({0}). L'ho messo da parte e riparto da una lista vuota.",
                    "The profile could not be read ({0}). I set it aside and started from an empty list.",
                    ex.Message), ex);
            }
        }

        public static void Save(Profile p)
        {
            Save(p, ProfilePath());
        }

        public static void Save(Profile p, string path)
        {
            p.SavedOn = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            p.MachineName = Environment.MachineName;
            p.AppVersion = AppInfo.Version;
            if (string.IsNullOrEmpty(p.CreatedOn)) p.CreatedOn = p.SavedOn;
            Json.Write(path, p);
        }

        public static Profile Import(string path)
        {
            Profile p = Json.Read<Profile>(path);
            if (p == null) throw new Exception(Tr.T("Il file non contiene un profilo valido.",
                                                   "The file does not contain a valid profile."));
            if (p.Groups == null) p.Groups = new System.Collections.Generic.List<string>();
            if (p.Items == null) p.Items = new System.Collections.Generic.List<AppItem>();
            foreach (AppItem it in p.Items) p.EnsureGroup(it.Group);
            return p;
        }

        public static string DefaultBackupName()
        {
            return "LHInstaller-backup-" + Environment.MachineName + "-"
                 + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".json";
        }
    }

    public static class AppInfo
    {
        public const string Name = "LHInstaller";
        public const string Version = "1.1";
    }
}
