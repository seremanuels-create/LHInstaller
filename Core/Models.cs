using System;
using System.Collections.Generic;
using System.Web.Script.Serialization;

namespace LHInstaller
{
    // Una voce della lista: o un pacchetto del catalogo winget, o un indirizzo diretto.
    public class AppItem
    {
        public string Kind = KindWinget;          // "winget" oppure "url"
        public string Name = "";
        public string PackageId = "";             // solo winget
        public string Version = "";               // versione registrata quando l'hai aggiunto
        public string Url = "";                   // solo url
        public string SilentArgs = "";            // argomenti per l'installazione silenziosa
        public string Group = "Generale";
        public bool Enabled = true;
        public string Note = "";

        // Firma del file remoto, per accorgersi che l'indirizzo punta a qualcosa di nuovo.
        public string ETag = "";
        public string LastModified = "";
        public long ContentLength = 0;
        public string LastChecked = "";

        // Esito dell'ultimo controllo aggiornamenti.
        public bool UpdateAvailable = false;
        public string LatestSeen = "";

        // Com'e' andata l'ultima volta che si e' provato a installarla.
        public string LastOutcome = "";           // OK, ERRORE, INTERROTTO, GIA' PRESENTE
        public string LastRunOn = "";

        // Stato di adesso, letto dal PC: non si salva, si ricalcola a ogni avvio.
        [ScriptIgnore] public bool InstalledKnown = false;
        [ScriptIgnore] public bool Installed = false;
        [ScriptIgnore] public string InstalledVersion = "";
        [ScriptIgnore] public string LiveStatus = "";     // "in corso...", "scarico..." durante l'esecuzione

        public const string KindWinget = "winget";
        public const string KindUrl = "url";

        public bool IsWinget()
        {
            return string.Equals(Kind, KindWinget, StringComparison.OrdinalIgnoreCase);
        }

        // Un promemoria: un programma letto dal PC che winget non sa reinstallare, messo
        // in lista senza indirizzo perche' non ci si dimentichi di aggiungerlo.
        public bool IsPlaceholder()
        {
            return !IsWinget() && string.IsNullOrEmpty(Url);
        }

        public string SourceLabel()
        {
            return IsWinget() ? "winget" : Tr.T("indirizzo", "address");
        }

        // C'e' una versione a catalogo piu' nuova di quella installata?
        public bool Upgradable()
        {
            return IsWinget() && InstalledKnown && Installed
                && Winget.CompareVersions(Version, InstalledVersion) > 0;
        }

        // La colonna "Dettaglio": l'identificativo del pacchetto, o il sito da cui scarica.
        public string DetailLabel()
        {
            if (IsWinget()) return PackageId;
            if (IsPlaceholder()) return Tr.T("(manca l'indirizzo)", "(address missing)");
            try { return new Uri(Url).Host; }
            catch { return Url; }
        }

        public string VersionLabel()
        {
            if (IsWinget()) return Version;
            return ContentLength > 0 ? DirectUrl.Human(ContentLength) : "";
        }

        public AppItem Clone()
        {
            return (AppItem)MemberwiseClone();
        }
    }

    // Il profilo completo: gruppi, voci, preferenze. E' questo che finisce nel file JSON
    // e nel backup completo.
    public class Profile
    {
        public int SchemaVersion = 2;
        public string AppVersion = "";
        public string CreatedOn = "";
        public string SavedOn = "";
        public string MachineName = "";
        public List<string> Groups = new List<string>();
        public List<AppItem> Items = new List<AppItem>();

        public bool SkipInstalled = true;
        public bool ContinueOnError = true;
        public bool ShowInstallerWindows = false;

        // "auto" (come Windows), "it", "en".
        public string Language = Tr.Auto;

        // Aggiornamenti di LHInstaller stesso.
        public bool CheckUpdatesOnStart = true;
        public string SkipUpdateVersion = "";     // versione che l'utente ha detto di ignorare
        public string LastUpdateCheckOn = "";

        // Com'era la finestra l'ultima volta: si riapre uguale.
        public int WindowWidth = 0;
        public int WindowHeight = 0;
        public bool WindowMaximized = false;
        public int SplitTop = 0;        // altezza della zona liste sopra la console
        public int SplitLeft = 0;       // larghezza del pannello gruppi

        public static Profile CreateEmpty()
        {
            Profile p = new Profile();
            p.CreatedOn = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            p.MachineName = Environment.MachineName;
            p.Groups.Add("Essenziali");
            p.Groups.Add("Sviluppo");
            p.Groups.Add("Multimedia");
            p.Groups.Add("Giochi");
            p.Groups.Add("Generale");
            return p;
        }

        public void EnsureGroup(string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            foreach (string g in Groups)
                if (string.Equals(g, name, StringComparison.OrdinalIgnoreCase)) return;
            Groups.Add(name);
        }

        public string CanonicalGroup(string name)
        {
            foreach (string g in Groups)
                if (string.Equals(g, name, StringComparison.OrdinalIgnoreCase)) return g;
            return name;
        }

        // Evita doppioni: stesso PackageId, o stesso indirizzo.
        public bool Contains(AppItem item)
        {
            return Find(item) != null;
        }

        public AppItem Find(AppItem item)
        {
            foreach (AppItem it in Items)
            {
                if (item.IsWinget() && it.IsWinget()
                    && string.Equals(it.PackageId, item.PackageId, StringComparison.OrdinalIgnoreCase))
                    return it;
                if (!item.IsWinget() && !it.IsWinget())
                {
                    // Due promemoria con lo stesso nome sono lo stesso promemoria; due indirizzi
                    // uguali sono la stessa voce. Un promemoria e un indirizzo no.
                    if (item.IsPlaceholder() && it.IsPlaceholder()
                        && string.Equals(it.Name, item.Name, StringComparison.OrdinalIgnoreCase))
                        return it;
                    if (!item.IsPlaceholder() && !it.IsPlaceholder()
                        && string.Equals(it.Url, item.Url, StringComparison.OrdinalIgnoreCase))
                        return it;
                }
            }
            return null;
        }

        public const string TodoGroup = "Da completare";

        public int CountPlaceholders()
        {
            int n = 0;
            foreach (AppItem it in Items) if (it.IsPlaceholder()) n++;
            return n;
        }

        public List<AppItem> ItemsIn(string group)
        {
            List<AppItem> list = new List<AppItem>();
            foreach (AppItem it in Items)
                if (string.Equals(it.Group, group, StringComparison.OrdinalIgnoreCase)) list.Add(it);
            return list;
        }

        public int CountEnabled()
        {
            int n = 0;
            foreach (AppItem it in Items) if (it.Enabled) n++;
            return n;
        }
    }

    // Un gruppo predefinito porta un nome diverso a seconda della lingua. Il profilo
    // pero' e' un file che viaggia fra PC: dentro ci finisce sempre il nome italiano,
    // e la traduzione avviene solo quando il nome viene mostrato. Cosi' un profilo
    // preparato in inglese resta leggibile aprendolo in italiano, e viceversa.
    public static class Groups
    {
        public const string Essentials = "Essenziali";
        public const string Dev = "Sviluppo";
        public const string Media = "Multimedia";
        public const string Games = "Giochi";
        public const string General = "Generale";
        public const string System = "Componenti di sistema";
        public const string Todo = "Da completare";

        public static string Show(string name)
        {
            if (!Tr.IsEnglish) return name;
            switch (name)
            {
                case Essentials: return "Essentials";
                case Dev: return "Development";
                case Media: return "Media";
                case Games: return "Games";
                case General: return "General";
                case System: return "System components";
                case Todo: return "To complete";
                default: return name;
            }
        }
    }

    // Riga restituita dalla ricerca nel catalogo.
    public class SearchResult
    {
        public string Name = "";
        public string Id = "";
        public string Version = "";
        public string Source = "";
        public bool Recommended = false;
        public int Score = 0;
    }

    // Esito di una singola installazione.
    public class InstallOutcome
    {
        public AppItem Item;
        public string Status = "";     // OK, GIA' PRESENTE, INTERROTTO, ERRORE
        public string Detail = "";
        public bool Success = false;
    }
}
