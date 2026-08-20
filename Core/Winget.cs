using System;
using System.Collections.Generic;
using System.IO;

namespace LHInstaller
{
    // Tutto quello che passa da winget: individuarlo, cercare nel catalogo,
    // leggere cosa e' gia' installato, installare.
    public static class Winget
    {
        private static string _exe;
        private static string _version;

        // winget e' un alias di esecuzione: non sempre e' sul PATH del processo,
        // soprattutto quando l'app gira come amministratore. Lo cerco in tre posti.
        public static string ExePath()
        {
            if (!string.IsNullOrEmpty(_exe)) return _exe;

            string fromPath = FindOnPath("winget.exe");
            if (fromPath != null) { _exe = fromPath; return _exe; }

            string local = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft\\WindowsApps\\winget.exe");
            if (File.Exists(local)) { _exe = local; return _exe; }

            string found = FindInWindowsApps();
            if (found != null) { _exe = found; return _exe; }

            return null;
        }

        public static bool IsAvailable()
        {
            return ExePath() != null;
        }

        public static string Version()
        {
            if (_version != null) return _version;
            string exe = ExePath();
            if (exe == null) return null;
            List<string> lines = new List<string>();
            ProcessRunner r = new ProcessRunner();
            try
            {
                r.Run(exe, "--version", delegate(string l, LineKind k) { lines.Add(l); });
            }
            catch { return null; }
            _version = lines.Count > 0 ? lines[0].Trim() : null;
            return _version;
        }

        private static string FindOnPath(string file)
        {
            string path = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrEmpty(path)) return null;
            foreach (string dir in path.Split(';'))
            {
                if (string.IsNullOrEmpty(dir)) continue;
                string candidate;
                try { candidate = Path.Combine(dir.Trim(), file); }
                catch { continue; }
                if (File.Exists(candidate)) return candidate;
            }
            return null;
        }

        private static string FindInWindowsApps()
        {
            try
            {
                string root = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps");
                if (!Directory.Exists(root)) return null;
                string[] dirs = Directory.GetDirectories(root, "Microsoft.DesktopAppInstaller_*");
                Array.Sort(dirs);
                for (int i = dirs.Length - 1; i >= 0; i--)
                {
                    string c = Path.Combine(dirs[i], "winget.exe");
                    if (File.Exists(c)) return c;
                }
            }
            catch { }
            return null;
        }

        // ---------- ricerca nel catalogo ----------

        public static List<SearchResult> Search(string query, Action<string, LineKind> log)
        {
            List<SearchResult> results = new List<SearchResult>();
            string exe = ExePath();
            if (exe == null) return results;

            List<string> lines = new List<string>();
            string args = "search --query \"" + Escape(query) +
                          "\" --source winget --accept-source-agreements --disable-interactivity";
            if (log != null) log("winget " + args, LineKind.Info);

            ProcessRunner r = new ProcessRunner();
            r.Run(exe, args, delegate(string l, LineKind k)
            {
                lines.Add(l);
                if (log != null) log(l, k);
            });

            List<string[]> rows = Table.Parse(lines);
            foreach (string[] row in rows)
            {
                SearchResult sr = new SearchResult();
                sr.Name = Table.Col(row, 0);
                sr.Id = Table.Col(row, 1);
                sr.Version = Table.Col(row, 2);
                sr.Source = "winget";
                if (sr.Id.Length == 0) continue;
                sr.Score = ScoreOf(sr, query);
                results.Add(sr);
            }

            results.Sort(delegate(SearchResult a, SearchResult b) { return b.Score.CompareTo(a.Score); });
            if (results.Count > 0) results[0].Recommended = true;
            return results;
        }

        // La riga giusta va in cima: corrispondenza esatta prima di tutto, e le varianti
        // beta / nightly / dev spinte in fondo, che non sono quelle che uno vuole installare.
        private static int ScoreOf(SearchResult sr, string query)
        {
            string q = (query == null ? "" : query.Trim().ToLowerInvariant());
            string name = (sr.Name == null ? "" : sr.Name.ToLowerInvariant());
            string id = (sr.Id == null ? "" : sr.Id.ToLowerInvariant());
            int score = 0;

            if (id == q) score += 1000;
            if (name == q) score += 900;
            if (q.Length > 0 && name.StartsWith(q)) score += 400;
            if (q.Length > 0 && id.IndexOf(q, StringComparison.Ordinal) >= 0) score += 200;
            if (q.Length > 0 && name.IndexOf(q, StringComparison.Ordinal) >= 0) score += 200;

            // Il segmento dopo il punto nell'ID e' spesso il nome del prodotto: Brave.Brave
            int dot = id.LastIndexOf('.');
            if (dot >= 0 && dot < id.Length - 1 && id.Substring(dot + 1) == q) score += 500;

            string[] side = { "beta", "nightly", "dev", "alpha", "canary", "insider",
                              "preview", "ptb", "portable", "unstable", "rc", "x86", "arm64" };
            foreach (string s in side)
            {
                if (ContainsToken(name, s) || ContainsToken(id, s)) { score -= 800; break; }
            }

            // A parita' di tutto, il nome piu' corto e' quasi sempre il prodotto principale.
            score -= Math.Min(name.Length, 60);
            return score;
        }

        // "dev" dentro "Devolutions" non conta: cerco la parola isolata.
        private static bool ContainsToken(string text, string token)
        {
            int i = text.IndexOf(token, StringComparison.Ordinal);
            while (i >= 0)
            {
                bool leftOk = i == 0 || !char.IsLetter(text[i - 1]);
                int end = i + token.Length;
                bool rightOk = end >= text.Length || !char.IsLetter(text[end]);
                if (leftOk && rightOk) return true;
                i = text.IndexOf(token, i + 1, StringComparison.Ordinal);
            }
            return false;
        }

        // ---------- cosa e' gia' installato ----------

        // Un'unica chiamata a "winget list" che restituisce, per ogni identificativo
        // riconosciuto, la versione installata. Serve alla colonna "Stato".
        public static Dictionary<string, string> ListInstalled()
        {
            Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string exe = ExePath();
            if (exe == null) return map;

            List<string> lines = new List<string>();
            ProcessRunner r = new ProcessRunner();
            r.Run(exe, "list --accept-source-agreements --disable-interactivity",
                  delegate(string l, LineKind k) { lines.Add(l); });

            List<string[]> rows = Table.Parse(lines);
            foreach (string[] row in rows)
            {
                string id = Table.Col(row, 1);
                if (id.Length == 0) continue;
                if (!map.ContainsKey(id)) map[id] = Table.Col(row, 2);
            }
            return map;
        }

        // Una riga di "winget list": tutto cio' che winget vede sul PC, reinstallabile o no.
        public class FoundProgram
        {
            public string Name = "";
            public string Id = "";
            public string Version = "";
            public string Source = "";
            public string Kind = "";       // winget | store | windows | steam | component | setup

            public bool Reinstallable { get { return Kind == "winget"; } }

            public string KindLabel()
            {
                switch (Kind)
                {
                    case "winget": return Tr.T("catalogo winget", "winget catalog");
                    case "store": return "Microsoft Store";
                    case "windows": return Tr.T("app di Windows", "Windows app");
                    case "steam": return Tr.T("gioco Steam (lo reinstalla Steam)", "Steam game (Steam reinstalls it)");
                    case "component": return Tr.T("driver o componente", "driver or component");
                    default: return Tr.T("installato da un setup", "installed by a setup");
                }
            }
        }

        // Una sola chiamata a "winget list", con tutte le righe classificate.
        public static List<FoundProgram> ListAll(Action<string, LineKind> log)
        {
            List<FoundProgram> found = new List<FoundProgram>();
            string exe = ExePath();
            if (exe == null) return found;

            List<string> lines = new List<string>();
            string args = "list --accept-source-agreements --disable-interactivity";
            if (log != null) log("winget " + args, LineKind.Info);

            ProcessRunner r = new ProcessRunner();
            r.Run(exe, args, delegate(string l, LineKind k) { lines.Add(l); });

            List<string[]> rows = Table.Parse(lines);
            foreach (string[] row in rows)
            {
                // Colonne: Nome, Id, Versione, Disponibile, Origine
                FoundProgram f = new FoundProgram();
                f.Name = Table.Col(row, 0);
                f.Id = Table.Col(row, 1);
                f.Version = Table.Col(row, 2);
                f.Source = Table.Col(row, 4);
                if (f.Name.Length == 0 && f.Id.Length == 0) continue;
                if (f.Name.Length == 0) f.Name = f.Id;
                f.Kind = Classify(f);
                found.Add(f);
            }

            found.Sort(delegate(FoundProgram a, FoundProgram b)
            {
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
            return found;
        }

        private static string Classify(FoundProgram f)
        {
            if (string.Equals(f.Source, "winget", StringComparison.OrdinalIgnoreCase)) return "winget";
            if (string.Equals(f.Source, "msstore", StringComparison.OrdinalIgnoreCase)) return "store";
            string id = f.Id ?? "";
            if (id.StartsWith("MSIX\\", StringComparison.OrdinalIgnoreCase)) return "windows";
            if (id.IndexOf("Steam App", StringComparison.OrdinalIgnoreCase) >= 0) return "steam";
            if (LooksLikeComponent(f.Name)) return "component";
            return "setup";
        }

        // Driver, runtime, aggiornamenti, SDK: cose che non si "salvano", si reinstallano
        // da sole insieme a cio' che le usa. Nascoste di partenza, visibili a richiesta.
        public static bool LooksLikeComponent(string name)
        {
            string n = (name ?? "").ToLowerInvariant();
            string[] marks = {
                "visual c++", "redistributable", "redistribuibile", ".net ", ".net framework", "microsoft .net",
                "windows sdk", "software development kit", "sdk addon", "runtime", "driver", "drivers",
                "update for", "aggiornamento", "hotfix", "security update", "webview2", "vulkan",
                "directx", "physx", "update health", "gameinput", "windowsappruntime", "windows app runtime",
                "system clr types", "vs_", "visual studio installer", "visual studio build tools",
                "intel(r)", "nvidia", "realtek", "amd software", "amd chipset", "microsoft edge",
                "microsoft onedrive", "teams machine-wide", "app installer", "package manager source",
                "bonjour", "apple software update", "apple mobile device", "icloud outlook",
                "service pack", "servizio", "license support", "pace license", "ilok",
                "asio", "usb audio", "usb midi", "midi driver", "audio driver",
                "network block", "r2r", "frameview", "control panel", "common components",
                "tools for", "components", "componenti", "host integration", "ntkdaemon",
                "cloud plugins", "vcredist", "language pack", "pacchetto di esperienze"
            };
            foreach (string m in marks)
                if (n.IndexOf(m, StringComparison.Ordinal) >= 0) return true;

            // "KB5034441" e simili: aggiornamenti di Windows
            int i = n.IndexOf("kb", StringComparison.Ordinal);
            if (i >= 0 && i + 3 < n.Length && char.IsDigit(n[i + 2]) && char.IsDigit(n[i + 3])) return true;
            return false;
        }

        // Per confrontare "Brave" con "Brave", o "Analog Lab V 5.12.4" con "Analog Lab V":
        // minuscolo, via numeri di versione, parentesi, simboli e parole di contorno.
        public static string NormalizeName(string name)
        {
            string n = (name ?? "").ToLowerInvariant();
            string[] drop = { "(x64)", "(x86)", "(64-bit)", "(32-bit)", "64-bit", "32-bit", "x64", "x86",
                              "version", "versione", "edition", "for windows", "per windows", "desktop" };
            foreach (string d in drop) n = n.Replace(d, " ");
            System.Text.StringBuilder sb = new System.Text.StringBuilder(n.Length);
            foreach (char c in n)
            {
                if (char.IsLetterOrDigit(c)) sb.Append(c);
                else sb.Append(' ');
            }
            // via i gruppi che sembrano numeri di versione (2.3.1, 2026, v12)
            string[] parts = sb.ToString().Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            List<string> keep = new List<string>();
            foreach (string p in parts)
            {
                bool allDigits = true;
                foreach (char c in p) if (!char.IsDigit(c)) { allDigits = false; break; }
                if (allDigits) continue;
                if (p.Length >= 2 && p[0] == 'v' && char.IsDigit(p[1])) continue;
                keep.Add(p);
            }
            return string.Join(" ", keep.ToArray()).Trim();
        }

        // Il nome di un programma letto dal PC corrisponde a un risultato del catalogo?
        //   0 = no;  2 = si', netta;  1 = forse (stesso nome, ma l'editore nell'identificativo
        //   non compare nel nome: potrebbe essere un omonimo, decide l'utente).
        // Provato sul campo: la regola "il nome inizia con..." dava un errore su due
        // (DaVinci Resolve -> un plugin "DaVinci Resolve RPC"), e i nomi corti sono ambigui.
        // Meglio nessun suggerimento che uno sbagliato.
        public static int MatchStrength(string programName, string catalogName, string catalogId)
        {
            string a = NormalizeName(programName);
            string b = NormalizeName(catalogName);
            if (a.Length < 4 || b.Length < 4) return 0;
            if (a != b) return 0;

            string publisher = "";
            string product = catalogId ?? "";
            int dot = product.IndexOf('.');
            if (dot > 0) { publisher = NormalizeName(product.Substring(0, dot)); product = NormalizeName(product.Substring(dot + 1)); }
            else product = NormalizeName(product);

            // "Brave.Brave", "Discord.Discord": editore e prodotto coincidono col nome.
            // "Google.Chrome" con nome "Google Chrome": l'editore e' nel nome. Netta.
            if (publisher.Length > 0 && (publisher == a || a.IndexOf(publisher, StringComparison.Ordinal) >= 0)) return 2;
            if (publisher.Length > 0 && publisher == product) return 2;
            // "Vortenia.Overwatch" per "Overwatch": stesso nome, editore sconosciuto. Forse.
            return 1;
        }

        // Legge l'elenco dei programmi presenti sul PC, tenendo solo quelli che winget
        // sa reinstallare: sono gli unici che abbia senso mettere in un profilo.
        public static List<AppItem> ListReinstallable(Action<string, LineKind> log)
        {
            List<AppItem> items = new List<AppItem>();
            foreach (FoundProgram f in ListAll(log))
            {
                if (!f.Reinstallable || f.Id.Length == 0) continue;
                items.Add(ToItem(f));
            }
            return items;
        }

        public static AppItem ToItem(FoundProgram f)
        {
            AppItem it = new AppItem();
            it.Kind = AppItem.KindWinget;
            it.Name = f.Name;
            it.PackageId = f.Id;
            it.Version = f.Version;
            it.Group = GuessGroup(it);
            return it;
        }

        public const string SystemGroup = Groups.System;

        // Smistamento di comodo, cosi' la lista arriva gia' divisa invece che in un mucchio solo.
        public static string GuessGroup(AppItem it)
        {
            string id = (it.PackageId == null ? "" : it.PackageId.ToLowerInvariant());

            string[] system = { "microsoft.vcredist", "microsoft.vclibs", "microsoft.windowsappruntime",
                                "microsoft.ui.xaml", "microsoft.dotnet.native", "microsoft.directx",
                                "microsoft.appinstaller", "nvidia.physx", "microsoft.gameinput",
                                "microsoft.edge", "microsoft.dotnet.desktopruntime",
                                "microsoft.dotnet.runtime", "microsoft.dotnet.aspnetcore" };
            foreach (string s in system) if (id.StartsWith(s)) return Groups.System;

            string[] dev = { "git.", "github.", "openjs.", "python.", "microsoft.dotnet",
                             "microsoft.visualstudio", "kitware.", "eclipseadoptium.",
                             "jrsoftware.", "docker.", "jetbrains.", "microsoft.windowsterminal",
                             "microsoft.powershell", "anthropic.", "oracle.jdk", "rustlang.",
                             "golang.", "postman.", "notepad++" };
            foreach (string d in dev) if (id.StartsWith(d)) return Groups.Dev;

            string[] games = { "valve.steam", "blizzard.", "nexusmods.", "meta.oculus",
                               "virtualdesktop.", "guru3d.", "techpowerup.", "futuremark.",
                               "msi.msicenter", "epicgames.", "goggalaxy", "electronicarts.",
                               "ubisoft." };
            foreach (string g in games) if (id.StartsWith(g)) return Groups.Games;

            string[] media = { "gyan.ffmpeg", "blenderfoundation.", "spitfireaudio",
                               "surgesynth.", "izotope.", "audacity.", "obsproject.",
                               "videolan.", "spotify.", "gimp.", "inkscape.", "handbrake.",
                               "krita.", "obsidian." };
            foreach (string m in media) if (id.StartsWith(m)) return Groups.Media;

            return Groups.Essentials;
        }

        public static bool IsInstalled(string packageId)
        {
            string exe = ExePath();
            if (exe == null || string.IsNullOrEmpty(packageId)) return false;

            List<string> lines = new List<string>();
            ProcessRunner r = new ProcessRunner();
            int code = r.Run(exe,
                "list --id \"" + Escape(packageId) + "\" --exact --accept-source-agreements --disable-interactivity",
                delegate(string l, LineKind k) { lines.Add(l); });
            if (code != 0) return false;

            // Non mi fido del solo codice di uscita: controllo che l'ID compaia davvero.
            foreach (string l in lines)
                if (l.IndexOf(packageId, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        public static string LatestVersion(string packageId)
        {
            string exe = ExePath();
            if (exe == null || string.IsNullOrEmpty(packageId)) return null;

            List<string> lines = new List<string>();
            ProcessRunner r = new ProcessRunner();
            r.Run(exe,
                "search --id \"" + Escape(packageId) + "\" --exact --source winget"
                + " --accept-source-agreements --disable-interactivity",
                delegate(string l, LineKind k) { lines.Add(l); });

            List<string[]> rows = Table.Parse(lines);
            foreach (string[] row in rows)
            {
                if (string.Equals(Table.Col(row, 1), packageId, StringComparison.OrdinalIgnoreCase))
                    return Table.Col(row, 2);
            }
            return null;
        }

        public static string InstallArgs(string packageId, bool interactive)
        {
            return "install --id \"" + Escape(packageId) + "\" --exact"
                 + (interactive ? " --interactive" : " --silent")
                 + " --source winget --accept-package-agreements --accept-source-agreements"
                 + " --disable-interactivity";
        }

        private static string Escape(string s)
        {
            return (s == null ? "" : s.Replace("\"", ""));
        }

        // I codici di uscita documentati di winget, tradotti in qualcosa di leggibile.
        public static string DescribeExitCode(int code)
        {
            uint u = unchecked((uint)code);
            switch (u)
            {
                case 0x00000000: return Tr.T("completato", "completed");
                case 0x8A150001: return Tr.T("errore interno di winget", "internal winget error");
                case 0x8A150002: return Tr.T("argomenti non validi (versione di winget troppo vecchia?)", "invalid arguments (winget too old?)");
                case 0x8A150003: return Tr.T("il comando non e' riuscito", "the command failed");
                case 0x8A150005: return Tr.T("interrotto", "cancelled");
                case 0x8A150006: return Tr.T("l'installer non si e' avviato", "the installer did not start");
                case 0x8A150008: return Tr.T("download non riuscito: controlla la connessione", "download failed: check the connection");
                case 0x8A15000B: return Tr.T("origini del catalogo non valide", "invalid catalog sources");
                case 0x8A150010: return Tr.T("nessun installer compatibile con questo PC", "no installer compatible with this PC");
                case 0x8A150011: return Tr.T("impronta del file scaricato non corrispondente", "downloaded file hash does not match");
                case 0x8A150014: return Tr.T("nessun pacchetto corrisponde nel catalogo", "no matching package in the catalog");
                case 0x8A150016: return Tr.T("piu' pacchetti corrispondono: identificativo ambiguo", "several packages match: ambiguous identifier");
                case 0x8A150019: return Tr.T("servono i permessi di amministratore", "administrator rights are required");
                case 0x8A15002B: return Tr.T("gia' installato nell'ultima versione", "already installed and up to date");
                case 0x8A15002E: return Tr.T("dimensione del download diversa dall'attesa", "download size differs from the expected one");
                case 0x8A150041: return Tr.T("l'installer rifiuta l'esecuzione come amministratore", "the installer refuses to run as administrator");
                case 0x8A15004C: return Tr.T("gia' installato", "already installed");
                case 0x8A150054: return Tr.T("il pacchetto e' un segnaposto dello Store", "the package is a Store placeholder");
                case 0x8A150101: return Tr.T("il programma e' in uso: chiudilo e riprova", "the program is in use: close it and retry");
                case 0x8A150102: return Tr.T("un'altra installazione e' in corso", "another installation is running");
                case 0x8A150103: return Tr.T("un file e' in uso", "a file is in use");
                case 0x8A150104: return Tr.T("manca una dipendenza", "a dependency is missing");
                case 0x8A150105: return Tr.T("disco pieno", "disk full");
                case 0x8A150106: return Tr.T("memoria insufficiente", "not enough memory");
                case 0x8A150107: return Tr.T("nessuna connessione di rete", "no network connection");
                case 0x8A150108: return Tr.T("l'installer chiede di contattare l'assistenza", "the installer asks you to contact support");
                case 0x8A150109: return Tr.T("completato, serve il riavvio per finire", "completed, a restart is needed to finish");
                case 0x8A15010A: return Tr.T("serve il riavvio prima di installare", "a restart is needed before installing");
                case 0x8A15010B: return Tr.T("l'installer ha avviato un riavvio", "the installer started a restart");
                case 0x8A15010C: return Tr.T("installazione annullata", "installation cancelled");
                case 0x8A15010D: return Tr.T("gia' installato", "already installed");
                case 0x8A15010E: return Tr.T("e' installata una versione piu' recente", "a newer version is already installed");
                case 0x8A15010F: return Tr.T("bloccato da un criterio di sistema", "blocked by a system policy");
                case 0x8A150110: return Tr.T("dipendenze non soddisfatte", "unmet dependencies");
                case 0x8A150111: return Tr.T("il programma e' aperto: chiudilo e riprova", "the program is open: close it and retry");
                case 0x8A150112: return Tr.T("parametro non valido per l'installer", "invalid parameter for the installer");
                case 0x8A150113: return Tr.T("sistema non supportato dall'installer", "system not supported by the installer");
                default:
                    return Tr.F("codice {0} (0x{1})", "code {0} (0x{1})", code, code.ToString("X8"));
            }
        }

        // Confronto di versioni "alla buona": segmento per segmento, numerico dove si puo'.
        // Restituisce >0 se a e' piu' recente di b, 0 se uguali o non confrontabili, <0 altrimenti.
        public static int CompareVersions(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return 0;
            if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return 0;
            string[] pa = a.Trim().TrimStart('v', 'V').Split('.', '-', '+', '_');
            string[] pb = b.Trim().TrimStart('v', 'V').Split('.', '-', '+', '_');
            int n = Math.Max(pa.Length, pb.Length);
            for (int i = 0; i < n; i++)
            {
                string xa = i < pa.Length ? pa[i] : "0";
                string xb = i < pb.Length ? pb[i] : "0";
                long na, nb;
                bool okA = long.TryParse(xa, out na);
                bool okB = long.TryParse(xb, out nb);
                if (okA && okB)
                {
                    if (na != nb) return na > nb ? 1 : -1;
                    continue;
                }
                // Segmenti non numerici (es. "g366879e1"): se differiscono non so dire
                // quale sia piu' nuovo, e non mi invento niente.
                if (!string.Equals(xa, xb, StringComparison.OrdinalIgnoreCase)) return 0;
            }
            return 0;
        }

        // Alcuni esiti non sono guasti: il programma c'e' gia', o chiede solo un riavvio.
        public static bool IsAcceptable(int code)
        {
            uint u = unchecked((uint)code);
            return u == 0x00000000 || u == 0x8A15002B || u == 0x8A15004C
                || u == 0x8A15010D || u == 0x8A150109 || u == 0x8A15010A || u == 0x8A15010E;
        }

        public static bool NeedsReboot(int code)
        {
            uint u = unchecked((uint)code);
            return u == 0x8A150109 || u == 0x8A15010A;
        }
    }
}
