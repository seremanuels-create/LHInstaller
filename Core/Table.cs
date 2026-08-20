using System;
using System.Collections.Generic;

namespace LHInstaller
{
    // winget stampa tabelle a colonne di larghezza fissa e le intestazioni sono tradotte
    // nella lingua di Windows. Per non dipendere dalla lingua, ricavo la posizione delle
    // colonne dalla riga di intestazione e leggo i valori per posizione, non per nome.
    public static class Table
    {
        public static List<string[]> Parse(List<string> lines)
        {
            List<string[]> rows = new List<string[]>();
            if (lines == null || lines.Count == 0) return rows;

            int sep = -1;
            for (int i = 0; i < lines.Count; i++)
            {
                if (IsSeparator(lines[i])) { sep = i; break; }
            }
            if (sep <= 0) return rows;

            int[] starts = ColumnStarts(lines[sep - 1]);
            if (starts.Length == 0) return rows;

            for (int i = sep + 1; i < lines.Count; i++)
            {
                string line = lines[i];
                if (line == null) continue;
                if (line.Trim().Length == 0) continue;
                if (IsSeparator(line)) continue;

                string[] cells = new string[starts.Length];
                for (int c = 0; c < starts.Length; c++)
                {
                    int from = starts[c];
                    int to = (c + 1 < starts.Length) ? starts[c + 1] : line.Length;
                    if (from >= line.Length) { cells[c] = ""; continue; }
                    if (to > line.Length) to = line.Length;
                    cells[c] = line.Substring(from, to - from).Trim();
                }

                // Una riga senza nulla nella prima colonna non e' un dato.
                if (cells[0].Length == 0 && (cells.Length < 2 || cells[1].Length == 0)) continue;
                rows.Add(cells);
            }
            return rows;
        }

        public static string Col(string[] row, int index)
        {
            if (row == null || index < 0 || index >= row.Length) return "";
            return row[index] ?? "";
        }

        private static bool IsSeparator(string line)
        {
            if (line == null) return false;
            string t = line.Trim();
            if (t.Length < 8) return false;
            foreach (char c in t)
            {
                if (c != '-' && c != '\u2500' && c != '\u2014') return false;
            }
            return true;
        }

        // Una colonna comincia a ogni parola dell'intestazione. Le intestazioni di winget
        // sono parole singole in tutte le lingue (Name, Id, Version... / Nome, ID,
        // Versione...), e quando una colonna e' stretta quanto il suo titolo la separa
        // dalla successiva un solo spazio: contare due spazi, come facevo prima, la
        // faceva sparire.
        private static int[] ColumnStarts(string header)
        {
            List<int> starts = new List<int>();
            if (string.IsNullOrEmpty(header)) return starts.ToArray();

            bool inGap = true;
            for (int i = 0; i < header.Length; i++)
            {
                if (header[i] == ' ')
                {
                    inGap = true;
                    continue;
                }
                if (inGap) starts.Add(i);
                inGap = false;
            }
            return starts.ToArray();
        }
    }
}
