using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;

namespace LHInstaller
{
    // Cio' che il catalogo winget non copre: si incolla l'indirizzo, l'app scarica
    // il file e prova a installarlo senza finestre. Se non ci riesce, apre l'installer
    // e lo dice chiaramente, invece di far finta di aver funzionato.
    public static class DirectUrl
    {
        private const string UserAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) LHInstaller/1.0";

        static DirectUrl()
        {
            try
            {
                // Molti siti hanno spento TLS 1.0 e 1.1: senza questa riga il download
                // fallisce con un errore di connessione poco comprensibile.
                ServicePointManager.SecurityProtocol =
                    (SecurityProtocolType)3072 | (SecurityProtocolType)12288;
            }
            catch
            {
                try { ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; }
                catch { }
            }
            ServicePointManager.DefaultConnectionLimit = 8;
        }

        // ---------- firma del file remoto ----------

        public class RemoteInfo
        {
            public bool Ok = false;
            public string ETag = "";
            public string LastModified = "";
            public long ContentLength = 0;
            public string FileName = "";
            public string Error = "";

            // Due firme diverse vogliono dire che al di la' dell'indirizzo c'e' un file
            // nuovo. Non e' il numero di versione, ma dice che qualcosa e' cambiato.
            public bool DiffersFrom(AppItem item)
            {
                if (!Ok) return false;
                if (!string.IsNullOrEmpty(ETag) && !string.IsNullOrEmpty(item.ETag))
                    return !string.Equals(ETag, item.ETag, StringComparison.Ordinal);
                if (!string.IsNullOrEmpty(LastModified) && !string.IsNullOrEmpty(item.LastModified))
                    return !string.Equals(LastModified, item.LastModified, StringComparison.Ordinal);
                if (ContentLength > 0 && item.ContentLength > 0)
                    return ContentLength != item.ContentLength;
                return false;
            }
        }

        public static RemoteInfo Probe(string url)
        {
            RemoteInfo info = new RemoteInfo();
            try
            {
                HttpWebResponse resp = Head(url);
                if (resp == null) resp = RangeGet(url);
                if (resp == null)
                {
                    info.Error = Tr.T("nessuna risposta dal server", "no response from the server");
                    return info;
                }
                using (resp)
                {
                    info.Ok = true;
                    info.ETag = resp.Headers["ETag"] ?? "";
                    info.LastModified = resp.Headers["Last-Modified"] ?? "";
                    string cr = resp.Headers["Content-Range"];
                    if (!string.IsNullOrEmpty(cr))
                    {
                        int slash = cr.LastIndexOf('/');
                        long total;
                        if (slash >= 0 && long.TryParse(cr.Substring(slash + 1), out total))
                            info.ContentLength = total;
                    }
                    if (info.ContentLength == 0 && resp.ContentLength > 1)
                        info.ContentLength = resp.ContentLength;
                    info.FileName = GuessFileName(url, resp);
                }
            }
            catch (Exception ex)
            {
                info.Error = ex.Message;
            }
            return info;
        }

        private static HttpWebResponse Head(string url)
        {
            try
            {
                HttpWebRequest req = NewRequest(url);
                req.Method = "HEAD";
                return (HttpWebResponse)req.GetResponse();
            }
            catch { return null; }
        }

        // Certi server rifiutano HEAD: chiedo un solo byte, ottengo gli stessi dati.
        private static HttpWebResponse RangeGet(string url)
        {
            try
            {
                HttpWebRequest req = NewRequest(url);
                req.Method = "GET";
                req.AddRange(0, 0);
                return (HttpWebResponse)req.GetResponse();
            }
            catch { return null; }
        }

        private static HttpWebRequest NewRequest(string url)
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = UserAgent;
            req.AllowAutoRedirect = true;
            req.Timeout = 30000;
            req.ReadWriteTimeout = 120000;
            return req;
        }

        private static string GuessFileName(string url, HttpWebResponse resp)
        {
            string cd = resp.Headers["Content-Disposition"];
            if (!string.IsNullOrEmpty(cd))
            {
                int i = cd.IndexOf("filename=", StringComparison.OrdinalIgnoreCase);
                if (i >= 0)
                {
                    string name = cd.Substring(i + 9).Trim().Trim('"', '\'');
                    int semi = name.IndexOf(';');
                    if (semi >= 0) name = name.Substring(0, semi);
                    name = Sanitize(name);
                    if (name.Length > 0) return name;
                }
            }

            try
            {
                Uri final = resp.ResponseUri != null ? resp.ResponseUri : new Uri(url);
                string name = Sanitize(Path.GetFileName(final.LocalPath));
                if (name.Length > 0) return name;
            }
            catch { }

            return "installer.exe";
        }

        private static string Sanitize(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name.Trim();
        }

        // ---------- scaricamento ----------

        public static string Download(string url, string destFolder,
                                      Action<long, long> onProgress,
                                      Action<string, LineKind> log,
                                      Func<bool> cancelled)
        {
            if (!Directory.Exists(destFolder)) Directory.CreateDirectory(destFolder);

            HttpWebRequest req = NewRequest(url);
            req.Method = "GET";

            using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
            {
                string fileName = GuessFileName(url, resp);
                string path = Path.Combine(destFolder, fileName);
                long total = resp.ContentLength;

                if (log != null)
                {
                    log("  " + Tr.F("file: {0}{1}", "file: {0}{1}", fileName,
                        total > 0 ? "  (" + Human(total) + ")" : ""), LineKind.Normal);
                    if (resp.ResponseUri != null && resp.ResponseUri.ToString() != url)
                        log("  " + Tr.F("reindirizzato a: {0}", "redirected to: {0}", resp.ResponseUri), LineKind.Normal);
                }

                using (Stream input = resp.GetResponseStream())
                using (FileStream output = new FileStream(path, FileMode.Create, FileAccess.Write))
                {
                    byte[] buffer = new byte[65536];
                    long done = 0;
                    int read;
                    while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        if (cancelled != null && cancelled())
                        {
                            output.Close();
                            try { File.Delete(path); } catch { }
                            return null;
                        }
                        output.Write(buffer, 0, read);
                        done += read;
                        if (onProgress != null) onProgress(done, total);
                    }
                }
                return path;
            }
        }

        public static string Human(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            double v = bytes / 1024.0;
            if (v < 1024) return v.ToString("0.#") + " KB";
            v /= 1024.0;
            if (v < 1024) return v.ToString("0.#") + " MB";
            v /= 1024.0;
            return v.ToString("0.##") + " GB";
        }

        // ---------- riconoscimento del tipo di installer ----------

        // Questi due valori finiscono nel profilo, quindi NON vanno tradotti: un profilo
        // salvato in italiano deve funzionare aperto in inglese. Si traduce solo cio' che
        // si mostra (SilentAutoLabel / SilentNoneLabel).
        public const string SilentAuto = "(automatico)";
        public const string SilentNone = "(mostra la finestra)";

        public static string SilentAutoLabel { get { return Tr.T("(automatico)", "(automatic)"); } }
        public static string SilentNoneLabel { get { return Tr.T("(mostra la finestra)", "(show the window)"); } }

        public class InstallerKind
        {
            public string Label = "";
            public string Args = "";
            public bool Known = false;
        }

        // Ogni famiglia di installer ha il suo argomento per l'installazione muta.
        // Riconosco la famiglia leggendo la firma dentro il file, invece di tirare a
        // indovinare provando un argomento dopo l'altro.
        public static InstallerKind Detect(string path)
        {
            InstallerKind k = new InstallerKind();
            k.Label = Tr.T("sconosciuto", "unknown");
            string ext = Path.GetExtension(path).ToLowerInvariant();

            if (ext == ".msi")
            {
                k.Label = "Windows Installer (MSI)";
                k.Args = "/qn /norestart";
                k.Known = true;
                return k;
            }
            if (ext == ".msix" || ext == ".appx" || ext == ".msixbundle" || ext == ".appxbundle")
            {
                k.Label = Tr.T("pacchetto MSIX", "MSIX package");
                k.Known = true;
                return k;
            }

            string blob = ReadMarkers(path);
            if (blob.IndexOf("Inno Setup", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                k.Label = "Inno Setup";
                k.Args = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-";
                k.Known = true;
            }
            else if (blob.IndexOf("Nullsoft", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                k.Label = "NSIS";
                k.Args = "/S";
                k.Known = true;
            }
            else if (blob.IndexOf("wixburn", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                k.Label = "WiX Burn";
                k.Args = "/quiet /norestart";
                k.Known = true;
            }
            else if (blob.IndexOf("InstallShield", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                k.Label = "InstallShield";
                k.Args = "/s /v\"/qn\"";
                k.Known = true;
            }
            return k;
        }

        // Le firme stanno all'inizio o in fondo al file: leggo solo quelle due fette,
        // non ha senso caricare in memoria un installer da centinaia di megabyte.
        private static string ReadMarkers(string path)
        {
            const int slice = 2 * 1024 * 1024;
            StringBuilder sb = new StringBuilder();
            try
            {
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read))
                {
                    sb.Append(ReadAscii(fs, 0, (int)Math.Min(slice, fs.Length)));
                    if (fs.Length > slice * 2L)
                        sb.Append(ReadAscii(fs, fs.Length - slice, slice));
                }
            }
            catch { }
            return sb.ToString();
        }

        private static string ReadAscii(FileStream fs, long offset, int count)
        {
            fs.Seek(offset, SeekOrigin.Begin);
            byte[] buf = new byte[count];
            int read = fs.Read(buf, 0, count);
            StringBuilder sb = new StringBuilder(read);
            for (int i = 0; i < read; i++)
            {
                byte b = buf[i];
                // Salto i byte nulli, cosi' anche il testo in UTF-16 diventa leggibile.
                if (b == 0) continue;
                sb.Append(b >= 32 && b < 127 ? (char)b : ' ');
            }
            return sb.ToString();
        }

        // ---------- esecuzione ----------

        public static int RunInstaller(string path, string silentArgs, Action<string, LineKind> log)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();

            if (ext == ".msi")
            {
                string args = "/i \"" + path + "\" " +
                              (string.IsNullOrEmpty(silentArgs) ? "/qn /norestart" : silentArgs);
                if (log != null) log("  msiexec " + args, LineKind.Info);
                return RunAndWait("msiexec.exe", args);
            }

            if (ext == ".msix" || ext == ".appx" || ext == ".msixbundle" || ext == ".appxbundle")
            {
                string ps = "-NoProfile -ExecutionPolicy Bypass -Command \"Add-AppxPackage -Path '"
                            + path.Replace("'", "''") + "'\"";
                if (log != null) log("  powershell Add-AppxPackage", LineKind.Info);
                return RunAndWait("powershell.exe", ps);
            }

            if (log != null)
            {
                log("  " + Path.GetFileName(path) + " " + silentArgs, LineKind.Info);
                if (string.IsNullOrEmpty(silentArgs))
                    log("  " + Tr.T("nessun argomento silenzioso: si aprira' la finestra dell'installer",
                                    "no silent argument: the installer window will open"), LineKind.Warn);
            }
            return RunAndWait(path, silentArgs);
        }

        private static int RunAndWait(string exe, string args)
        {
            ProcessStartInfo psi = new ProcessStartInfo(exe, args);
            psi.UseShellExecute = true;
            using (Process p = Process.Start(psi))
            {
                if (p == null) return -1;
                p.WaitForExit();
                return p.ExitCode;
            }
        }
    }
}
