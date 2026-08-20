using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace LHInstaller
{
    // Il formato nativo di "winget export" / "winget import": un elenco di
    // identificativi raggruppati per origine. Leggerlo e scriverlo rende il profilo
    // scambiabile con chi usa winget dalla riga di comando, senza passare da noi.
    public static class WingetFormat
    {
        public static List<AppItem> Import(string path)
        {
            List<AppItem> items = new List<AppItem>();
            string text = File.ReadAllText(path, Encoding.UTF8);
            if (text.Length > 0 && text[0] == '\uFEFF') text = text.Substring(1);

            Dictionary<string, object> root = Json.Deserialize<Dictionary<string, object>>(text);
            if (root == null) throw new Exception(Tr.T("Il file non e' un JSON leggibile.",
                                                        "The file is not readable JSON."));

            object sourcesObj;
            if (!root.TryGetValue("Sources", out sourcesObj))
                throw new Exception(Tr.T("Il file non contiene la sezione \"Sources\" del formato winget.",
                                         "The file has no \"Sources\" section of the winget format."));

            // Il serializzatore restituisce gli array ora come ArrayList ora come object[]:
            // li tratto tutti come sequenze generiche.
            System.Collections.IEnumerable sources = sourcesObj as System.Collections.IEnumerable;
            if (sources == null) return items;

            foreach (object s in sources)
            {
                Dictionary<string, object> src = s as Dictionary<string, object>;
                if (src == null) continue;

                string sourceName = "";
                object detailsObj;
                if (src.TryGetValue("SourceDetails", out detailsObj))
                {
                    Dictionary<string, object> details = detailsObj as Dictionary<string, object>;
                    object n;
                    if (details != null && details.TryGetValue("Name", out n) && n != null) sourceName = n.ToString();
                }
                // Solo il catalogo pubblico: i pacchetti dello Store hanno identificativi
                // che "winget install --source winget" non conosce.
                if (sourceName.Length > 0 && !string.Equals(sourceName, "winget", StringComparison.OrdinalIgnoreCase))
                    continue;

                object pkgsObj;
                if (!src.TryGetValue("Packages", out pkgsObj)) continue;
                System.Collections.IEnumerable pkgs = pkgsObj as System.Collections.IEnumerable;
                if (pkgs == null) continue;

                foreach (object p in pkgs)
                {
                    Dictionary<string, object> pkg = p as Dictionary<string, object>;
                    if (pkg == null) continue;
                    object idObj;
                    if (!pkg.TryGetValue("PackageIdentifier", out idObj) || idObj == null) continue;
                    string id = idObj.ToString().Trim();
                    if (id.Length == 0) continue;

                    AppItem it = new AppItem();
                    it.Kind = AppItem.KindWinget;
                    it.PackageId = id;
                    it.Name = NameFromId(id);
                    object v;
                    if (pkg.TryGetValue("Version", out v) && v != null) it.Version = v.ToString();
                    it.Group = Winget.GuessGroup(it);
                    items.Add(it);
                }
            }
            return items;
        }

        // "Brave.Brave" -> "Brave"; "Microsoft.VisualStudioCode" -> "VisualStudioCode".
        // Un nome di ripiego finche' non lo si aggiorna da catalogo.
        private static string NameFromId(string id)
        {
            int dot = id.LastIndexOf('.');
            if (dot >= 0 && dot < id.Length - 1) return id.Substring(dot + 1);
            return id;
        }

        public static void Export(IList<AppItem> items, string path)
        {
            List<object> packages = new List<object>();
            foreach (AppItem it in items)
            {
                if (!it.IsWinget() || string.IsNullOrEmpty(it.PackageId)) continue;
                Dictionary<string, object> p = new Dictionary<string, object>();
                p["PackageIdentifier"] = it.PackageId;
                packages.Add(p);
            }

            Dictionary<string, object> details = new Dictionary<string, object>();
            details["Argument"] = "https://cdn.winget.microsoft.com/cache";
            details["Identifier"] = "Microsoft.Winget.Source_8wekyb3d8bbwe";
            details["Name"] = "winget";
            details["Type"] = "Microsoft.PreIndexed.Package";

            Dictionary<string, object> source = new Dictionary<string, object>();
            source["Packages"] = packages;
            source["SourceDetails"] = details;

            Dictionary<string, object> root = new Dictionary<string, object>();
            root["$schema"] = "https://aka.ms/winget-packages.schema.2.0.json";
            root["CreationDate"] = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fff") + "-00:00";
            root["Sources"] = new object[] { source };
            string v = Winget.Version();
            if (!string.IsNullOrEmpty(v)) root["WinGetVersion"] = v.TrimStart('v');

            Json.Write(path, root);
        }
    }
}
