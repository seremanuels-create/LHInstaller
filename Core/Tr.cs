using System;
using System.Globalization;
using System.Threading;

namespace LHInstaller
{
    // Traduzione. La regola scelta: l'italiano e l'inglese stanno UNO ACCANTO ALL'ALTRO
    // nel punto in cui il testo viene usato, invece che in una tabella di chiavi a parte.
    // Costa qualche carattere in piu' per riga, ma toglie il problema peggiore di questi
    // impianti: una chiave che cambia da un lato e resta vecchia dall'altro, e stringhe
    // orfane che nessuno si accorge di aver perso.
    //
    //   Tr.T("Salva", "Save")                                testo fisso
    //   Tr.F("{0} voci", "{0} entries", n)                   testo con parti variabili
    //
    // I segnaposti sono quelli di string.Format, cosi' l'ordine delle parole puo'
    // cambiare fra le due lingue senza toccare il codice che chiama.
    public static class Tr
    {
        public const string Auto = "auto";
        public const string Italian = "it";
        public const string English = "en";

        private static string _choice = Auto;
        private static bool _english;

        public static string Choice { get { return _choice; } }
        public static bool IsEnglish { get { return _english; } }

        // "auto" segue la lingua di Windows: italiano se il sistema e' italiano,
        // inglese in tutti gli altri casi.
        public static void Init(string choice)
        {
            _choice = Normalize(choice);
            if (_choice == Italian) _english = false;
            else if (_choice == English) _english = true;
            else _english = !SystemIsItalian();

            // Numeri e date seguono la lingua scelta, non quella del sistema: altrimenti
            // un'interfaccia in inglese mostrerebbe "1,6 MB" e date all'italiana.
            try
            {
                CultureInfo c = CultureInfo.GetCultureInfo(_english ? "en-US" : "it-IT");
                Thread.CurrentThread.CurrentCulture = c;
                CultureInfo.DefaultThreadCurrentCulture = c;
            }
            catch { }
        }

        public static string Normalize(string choice)
        {
            if (string.IsNullOrEmpty(choice)) return Auto;
            string c = choice.Trim().ToLowerInvariant();
            if (c == Italian || c == English) return c;
            return Auto;
        }

        public static bool SystemIsItalian()
        {
            try
            {
                return string.Equals(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName,
                                     "it", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        public static string T(string it, string en)
        {
            return _english ? en : it;
        }

        public static string F(string it, string en, params object[] args)
        {
            string fmt = _english ? en : it;
            try { return string.Format(CultureInfo.CurrentCulture, fmt, args); }
            catch { return fmt; }
        }

        // Il nome della lingua, mostrato nel menu. Sempre nella lingua stessa: chi apre
        // l'app in una lingua che non capisce deve comunque riconoscere la propria.
        public static string NameOf(string choice)
        {
            switch (Normalize(choice))
            {
                case Italian: return "Italiano";
                case English: return "English";
                default: return T("Automatica (come Windows)", "Automatic (follow Windows)");
            }
        }

        public static string CurrentName()
        {
            return _english ? "English" : "Italiano";
        }
    }
}
