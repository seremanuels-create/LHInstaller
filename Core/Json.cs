using System;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace LHInstaller
{
    // JSON senza librerie esterne: usa il serializzatore gia' presente in .NET Framework
    // e ci aggiunge un'indentazione, cosi' il file resta leggibile e modificabile a mano.
    public static class Json
    {
        private static JavaScriptSerializer NewSerializer()
        {
            JavaScriptSerializer s = new JavaScriptSerializer();
            s.MaxJsonLength = int.MaxValue;
            s.RecursionLimit = 200;
            return s;
        }

        public static string Serialize(object value)
        {
            return Indent(NewSerializer().Serialize(value));
        }

        public static T Deserialize<T>(string text)
        {
            return NewSerializer().Deserialize<T>(text);
        }

        public static void Write(string path, object value)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            // Scrittura atomica: prima un file temporaneo, poi lo sostituisco.
            // Cosi' un'interruzione non lascia il profilo a meta'.
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, Serialize(value), new UTF8Encoding(false));
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        public static T Read<T>(string path)
        {
            string text = File.ReadAllText(path, Encoding.UTF8);
            if (text.Length > 0 && text[0] == '\uFEFF') text = text.Substring(1);
            return Deserialize<T>(text);
        }

        // Indentatore minimale: attraversa il testo e va a capo sui delimitatori,
        // ignorando quelli che si trovano dentro una stringa.
        public static string Indent(string json)
        {
            StringBuilder sb = new StringBuilder(json.Length * 2);
            int depth = 0;
            bool inString = false;
            bool escaped = false;

            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];

                if (inString)
                {
                    sb.Append(c);
                    if (escaped) escaped = false;
                    else if (c == '\\') escaped = true;
                    else if (c == '"') inString = false;
                    continue;
                }

                switch (c)
                {
                    case '"':
                        inString = true;
                        sb.Append(c);
                        break;
                    case '{':
                    case '[':
                        sb.Append(c);
                        // Collezione vuota: la lascio sulla stessa riga.
                        if (i + 1 < json.Length && (json[i + 1] == '}' || json[i + 1] == ']'))
                        {
                            sb.Append(json[i + 1]);
                            i++;
                            break;
                        }
                        depth++;
                        NewLine(sb, depth);
                        break;
                    case '}':
                    case ']':
                        depth--;
                        NewLine(sb, depth);
                        sb.Append(c);
                        break;
                    case ',':
                        sb.Append(c);
                        NewLine(sb, depth);
                        break;
                    case ':':
                        sb.Append(": ");
                        break;
                    default:
                        sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        private static void NewLine(StringBuilder sb, int depth)
        {
            sb.Append("\r\n");
            sb.Append(' ', depth * 2);
        }
    }
}
