using System;
using System.Diagnostics;
using System.Text;
using System.Threading;

namespace LHInstaller
{
    public enum LineKind { Normal, Error, Info, Good, Warn }

    // Avvia un processo esterno e ne riversa l'output riga per riga sulla console
    // incorporata, mentre gira. E' il pezzo che fa vedere "cosa succede sotto il cofano".
    public class ProcessRunner
    {
        private const char Esc = (char)27;

        private Process _proc;
        private readonly object _lock = new object();
        private volatile bool _killed;

        public bool Killed { get { return _killed; } }

        public int Run(string exe, string args, Action<string, LineKind> onLine)
        {
            _killed = false;

            ProcessStartInfo psi = new ProcessStartInfo(exe, args);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.StandardOutputEncoding = new UTF8Encoding(false);
            psi.StandardErrorEncoding = new UTF8Encoding(false);

            using (Process p = new Process())
            {
                p.StartInfo = psi;
                p.EnableRaisingEvents = true;

                using (ManualResetEvent outDone = new ManualResetEvent(false))
                using (ManualResetEvent errDone = new ManualResetEvent(false))
                {
                    p.OutputDataReceived += delegate(object s, DataReceivedEventArgs e)
                    {
                        if (e.Data == null) { outDone.Set(); return; }
                        Emit(e.Data, LineKind.Normal, onLine);
                    };
                    p.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e)
                    {
                        if (e.Data == null) { errDone.Set(); return; }
                        Emit(e.Data, LineKind.Error, onLine);
                    };

                    p.Start();
                    lock (_lock) { _proc = p; }

                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    p.WaitForExit();
                    outDone.WaitOne(2000);
                    errDone.WaitOne(2000);

                    lock (_lock) { _proc = null; }
                    return p.ExitCode;
                }
            }
        }

        public void Kill()
        {
            lock (_lock)
            {
                _killed = true;
                if (_proc == null) return;
                try
                {
                    if (!_proc.HasExited) _proc.Kill();
                }
                catch { }
            }
        }

        private static void Emit(string raw, LineKind kind, Action<string, LineKind> onLine)
        {
            // Le barre di avanzamento di winget si riscrivono con \r sulla stessa riga:
            // tengo solo l'ultimo segmento, altrimenti la console si riempie di doppioni.
            int cr = raw.LastIndexOf('\r');
            string line = cr >= 0 ? raw.Substring(cr + 1) : raw;
            line = StripAnsi(line).TrimEnd();
            if (IsNoise(line)) return;
            onLine(line, kind);
        }

        // Scarto solo la grafica: lo spinner (un carattere solo che ruota) e le barre
        // di riempimento. La riga di trattini che separa l'intestazione di una tabella
        // deve invece passare: e' quella che dice dove cominciano le colonne.
        private static bool IsNoise(string line)
        {
            string t = line.Trim();
            if (t.Length == 0) return true;

            foreach (char c in t)
                if (c == '\u2588' || c == '\u2592' || c == '\u2591' || c == '\u2580') return true;

            if (t.Length <= 3)
            {
                foreach (char c in t)
                {
                    if (c == '-' || c == '\\' || c == '/' || c == '|' || c == '.') continue;
                    return false;
                }
                return true;
            }

            foreach (char c in t)
                if (c != '.' && c != ' ') return false;
            return true;
        }

        private static string StripAnsi(string s)
        {
            if (s.IndexOf(Esc) < 0) return s;
            StringBuilder sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == Esc)
                {
                    // Salta la sequenza di escape fino alla lettera che la chiude.
                    i++;
                    if (i < s.Length && s[i] == '[')
                    {
                        i++;
                        while (i < s.Length && !char.IsLetter(s[i])) i++;
                    }
                    continue;
                }
                sb.Append(s[i]);
            }
            return sb.ToString();
        }
    }
}
