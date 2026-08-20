using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;

namespace LHInstaller
{
    // Controllo della versione di LHInstaller stesso.
    //
    // Chiede a GitHub qual e' l'ultima release pubblicata nella repo del progetto. E'
    // il posto giusto per un'app portatile: basta pubblicare una release con il tag
    // della versione (v1.1, v1.2...) e tutte le copie in giro se ne accorgono da sole
    // al primo avvio, senza server da tenere acceso.
    //
    // Finche' la repo non esiste, o non ha release, il controllo fallisce in silenzio:
    // non ha senso disturbare l'utente per una cosa che non dipende da lui.
    public static class UpdateCheck
    {
        // Non costanti: se un giorno la repo cambia nome o proprietario, si tocca una
        // riga sola. Serve anche a poter puntare il controllo altrove durante le prove.
        public static string Owner = "seremanuels-create";
        public static string Repo = "LHInstaller";

        public static string ReleasesPage
        {
            get { return "https://github.com/" + Owner + "/" + Repo + "/releases"; }
        }

        private const string Api = "https://api.github.com/repos/{0}/{1}/releases/latest";

        static UpdateCheck()
        {
            try
            {
                // TLS 1.2 e 1.3: GitHub non accetta niente di piu' vecchio.
                ServicePointManager.SecurityProtocol =
                    (SecurityProtocolType)3072 | (SecurityProtocolType)12288;
            }
            catch
            {
                try { ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; }
                catch { }
            }
        }

        public class Result
        {
            public bool Ok;                 // la richiesta e' andata a buon fine
            public bool Newer;              // ...e c'e' una versione piu' recente di questa
            public string Version = "";     // "1.2"
            public string Title = "";       // il nome della release
            public string Notes = "";       // il testo della release, per il "novita'"
            public string PageUrl = "";     // pagina da aprire nel browser
            public string PublishedOn = "";
            public string Error = "";
            public bool NoReleases;         // repo raggiungibile ma senza release pubblicate
        }

        public static Result Check()
        {
            Result r = new Result();
            try
            {
                string json = Get(string.Format(Api, Owner, Repo));
                if (json == null)
                {
                    r.Error = Tr.T("nessuna risposta da GitHub", "no response from GitHub");
                    return r;
                }

                Dictionary<string, object> o = Json.Deserialize<Dictionary<string, object>>(json);
                if (o == null)
                {
                    r.Error = Tr.T("risposta non leggibile", "unreadable response");
                    return r;
                }

                string tag = Str(o, "tag_name");
                if (tag.Length == 0)
                {
                    r.NoReleases = true;
                    r.Error = Tr.T("nessuna release pubblicata", "no release published yet");
                    return r;
                }

                r.Ok = true;
                r.Version = tag.TrimStart('v', 'V').Trim();
                r.Title = Str(o, "name");
                r.Notes = Str(o, "body");
                r.PageUrl = Str(o, "html_url");
                if (r.PageUrl.Length == 0) r.PageUrl = ReleasesPage;

                string published = Str(o, "published_at");
                if (published.Length >= 10)
                {
                    DateTime d;
                    if (DateTime.TryParse(published, null,
                            System.Globalization.DateTimeStyles.AdjustToUniversal, out d))
                        r.PublishedOn = d.ToLocalTime().ToString("dd/MM/yyyy");
                    else
                        r.PublishedOn = published.Substring(0, 10);
                }

                // Confronto per segmenti numerici, lo stesso usato per i pacchetti:
                // "1.10" e' piu' recente di "1.9", che come stringhe non lo sarebbe.
                r.Newer = Winget.CompareVersions(r.Version, AppInfo.Version) > 0;
            }
            catch (WebException wex)
            {
                HttpWebResponse resp = wex.Response as HttpWebResponse;
                if (resp != null && resp.StatusCode == HttpStatusCode.NotFound)
                {
                    // 404: o la repo non c'e' ancora, o non ha release. Stessa reazione.
                    r.NoReleases = true;
                    r.Error = Tr.T("nessuna release pubblicata", "no release published yet");
                }
                else
                {
                    r.Error = wex.Message;
                }
            }
            catch (Exception ex)
            {
                r.Error = ex.Message;
            }
            return r;
        }

        private static string Get(string url)
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "GET";
            // GitHub rifiuta le richieste senza User-Agent, e vuole questo Accept per
            // avere la versione stabile dell'API.
            req.UserAgent = AppInfo.Name + "/" + AppInfo.Version;
            req.Accept = "application/vnd.github+json";
            req.Timeout = 15000;
            req.ReadWriteTimeout = 15000;

            using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
            using (Stream s = resp.GetResponseStream())
            {
                if (s == null) return null;
                using (StreamReader sr = new StreamReader(s, Encoding.UTF8))
                    return sr.ReadToEnd();
            }
        }

        private static string Str(Dictionary<string, object> o, string key)
        {
            object v;
            if (!o.TryGetValue(key, out v) || v == null) return "";
            return v.ToString();
        }

        // Le note di una release sono in Markdown: qui serve solo testo leggibile in una
        // finestra semplice, quindi tolgo i segni piu' invadenti e lascio stare il resto.
        public static string PlainNotes(string markdown, int maxLines)
        {
            if (string.IsNullOrEmpty(markdown)) return "";
            string[] lines = markdown.Replace("\r\n", "\n").Split('\n');
            StringBuilder sb = new StringBuilder();
            int used = 0;
            foreach (string raw in lines)
            {
                if (used >= maxLines) { sb.AppendLine("..."); break; }
                string l = raw.TrimEnd();
                while (l.StartsWith("#")) l = l.Substring(1).TrimStart();
                if (l.StartsWith("* ") || l.StartsWith("- ")) l = "  \u2022 " + l.Substring(2);
                l = l.Replace("**", "").Replace("`", "");
                sb.AppendLine(l);
                used++;
            }
            return sb.ToString().TrimEnd();
        }
    }
}
