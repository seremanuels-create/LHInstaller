using System;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace LHInstaller
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // La lingua va decisa prima di costruire qualsiasi finestra: il testo dei
            // controlli si scrive una volta sola, al momento in cui vengono creati.
            Tr.Init(Storage.PeekLanguage());

            // Un errore imprevisto non deve chiudere la finestra senza spiegazioni:
            // meglio un messaggio che si puo' copiare e leggere.
            Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e)
            {
                Report(e.Exception);
            };
            AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
            {
                Report(e.ExceptionObject as Exception);
            };

            // --avvia          parte subito con l'installazione (usato dal riavvio come amministratore)
            // --apri <cosa>    apre all'avvio un dialogo: cerca | pc | indirizzo | aiuto | info
            bool autoStart = false;
            string open = null;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (string.Equals(a, "--avvia", StringComparison.OrdinalIgnoreCase)) autoStart = true;
                else if (string.Equals(a, "--apri", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) open = args[++i];
                else if (a.StartsWith("--apri=", StringComparison.OrdinalIgnoreCase)) open = a.Substring(7);
            }

            Application.Run(new MainForm(autoStart, open));
        }

        private static void Report(Exception ex)
        {
            if (ex == null) return;
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(Tr.T("Qualcosa e' andato storto.", "Something went wrong."));
            sb.AppendLine();
            sb.AppendLine(ex.Message);
            sb.AppendLine();
            sb.AppendLine(ex.ToString());
            try
            {
                MessageBox.Show(sb.ToString(), AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch { }
        }
    }
}
