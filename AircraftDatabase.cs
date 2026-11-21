using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;

namespace SDRSharp.InmarsatAero
{
    // ICAO address -> registration and type, from a CSV (optionally .gz):
    //   tar1090-db aircraft.csv.gz: no header, ';' separated, '\' escapes, columns icao;registration;type;flags;...
    //     flags: one digit per flag, first = military, second = interesting ("10", "0010"...); checked on a 2026-11
    //     download: 23,193 military, e.g. RAAF C-130J A97-440 = "10", Qatar Amiri Flight = "10"
    //   (a flags column holding a number is read as a readsb dbFlags bitfield: military = bit 0)
    //   dump1090-style: header line naming icao24 (or icao / hex), r (or registration / reg), t (or type / typecode)
    // Loads on a thread-pool thread; lookups are lock-free against the last loaded table (sorted arrays, binary search).
    public sealed class AircraftDatabase
    {
        private sealed class Table
        {
            public int[] Keys;
            public string[] Registrations;
            public string[] Types;
            public bool[] Military;
            public int MilitaryCount;
        }

        private volatile Table _table;
        private volatile string _status = "No database";
        private int _loading;

        public string Status => _status;

        public int Count
        {
            get
            {
                var table = _table;
                return table != null ? table.Keys.Length : 0;
            }
        }

        // Starts loading path in the background ("" unloads)
        public void Load(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                _table = null;
                _status = "No database";
                return;
            }
            if (Interlocked.Exchange(ref _loading, 1) == 1)
            {
                return;
            }
            _status = "Loading " + Path.GetFileName(path) + "...";
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var table = Read(path);
                    _table = table;
                    _status = table.Keys.Length.ToString("#,0") + " aircraft, " + table.MilitaryCount.ToString("#,0") + " military (" + Path.GetFileName(path) + ")";
                }
                catch (Exception e)
                {
                    _status = "Failed: " + e.Message;
                }
                finally
                {
                    Interlocked.Exchange(ref _loading, 0);
                }
            });
        }

        public bool Lookup(int address, out string registration, out string type)
        {
            bool military;
            return Lookup(address, out registration, out type, out military);
        }

        public bool Lookup(int address, out string registration, out string type, out bool military)
        {
            var table = _table;
            if (table != null)
            {
                var i = Array.BinarySearch(table.Keys, address);
                if (i >= 0)
                {
                    registration = table.Registrations[i];
                    type = table.Types[i];
                    military = table.Military[i];
                    return true;
                }
            }
            registration = null;
            type = null;
            military = false;
            return false;
        }

        // flags column: digit string (first digit = military) or a dbFlags number (bit 0 = military)
        private static bool IsMilitary(string flags)
        {
            flags = flags.Trim();
            if (flags.Length == 0)
            {
                return false;
            }
            var digits = true;
            for (var i = 0; i < flags.Length; i++)
            {
                if (flags[i] != '0' && flags[i] != '1')
                {
                    digits = false;
                    break;
                }
            }
            if (digits && flags.Length >= 2)
            {
                return flags[0] == '1';
            }
            int value;
            return int.TryParse(flags, out value) && (value & 1) != 0;
        }

        private static Table Read(string path)
        {
            var keys = new List<int>(600000);
            var registrations = new List<string>(600000);
            var types = new List<string>(600000);
            var military = new List<bool>(600000);
            var typePool = new Dictionary<string, string>();
            var fields = new List<string>(16);

            using (var file = File.OpenRead(path))
            using (var stream = path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) ? (Stream)new GZipStream(file, CompressionMode.Decompress) : file)
            using (var reader = new StreamReader(stream))
            {
                int icaoColumn = 0, regColumn = 1, typeColumn = 2, flagsColumn = 3;
                var separator = '\0';
                var first = true;
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.Length == 0)
                    {
                        continue;
                    }
                    if (separator == '\0')
                    {
                        separator = line.IndexOf(';') >= 0 ? ';' : ',';
                    }
                    Split(line, separator, fields);
                    if (first)
                    {
                        first = false;
                        int dummy;
                        if (!TryHex(fields[0], out dummy))
                        {
                            // header: find the columns by name
                            icaoColumn = Find(fields, "icao24", "icao", "hex");
                            regColumn = Find(fields, "r", "registration", "reg");
                            typeColumn = Find(fields, "t", "typecode", "type");
                            flagsColumn = Find(fields, "dbflags", "flags", "f");
                            if (icaoColumn < 0)
                            {
                                throw new InvalidDataException("no icao column in the header");
                            }
                            continue;
                        }
                    }
                    int address;
                    if (icaoColumn >= fields.Count || !TryHex(fields[icaoColumn], out address))
                    {
                        continue;
                    }
                    var registration = regColumn >= 0 && regColumn < fields.Count ? fields[regColumn].Trim() : "";
                    var type = typeColumn >= 0 && typeColumn < fields.Count ? fields[typeColumn].Trim() : "";
                    var isMilitary = flagsColumn >= 0 && flagsColumn < fields.Count && IsMilitary(fields[flagsColumn]);
                    if (registration.Length == 0 && type.Length == 0 && !isMilitary)
                    {
                        continue;
                    }
                    string pooled;
                    if (!typePool.TryGetValue(type, out pooled))
                    {
                        pooled = type;
                        typePool.Add(type, type);
                    }
                    keys.Add(address);
                    registrations.Add(registration);
                    types.Add(pooled);
                    military.Add(isMilitary);
                }
            }

            var keyArray = keys.ToArray();
            var order = new int[keyArray.Length];
            for (var i = 0; i < order.Length; i++)
            {
                order[i] = i;
            }
            Array.Sort((int[])keyArray.Clone(), order);
            var table = new Table
            {
                Keys = new int[order.Length],
                Registrations = new string[order.Length],
                Types = new string[order.Length],
                Military = new bool[order.Length],
            };
            var n = 0;
            for (var i = 0; i < order.Length; i++)
            {
                var k = keyArray[order[i]];
                if (n > 0 && table.Keys[n - 1] == k)
                {
                    table.Registrations[n - 1] = registrations[order[i]]; // duplicate address: one of its lines is kept
                    table.Types[n - 1] = types[order[i]];
                    table.Military[n - 1] = military[order[i]];
                    continue;
                }
                table.Keys[n] = k;
                table.Registrations[n] = registrations[order[i]];
                table.Types[n] = types[order[i]];
                table.Military[n] = military[order[i]];
                n++;
            }
            Array.Resize(ref table.Keys, n);
            Array.Resize(ref table.Registrations, n);
            Array.Resize(ref table.Types, n);
            Array.Resize(ref table.Military, n);
            for (var i = 0; i < n; i++)
            {
                if (table.Military[i])
                {
                    table.MilitaryCount++;
                }
            }
            return table;
        }

        // separator-split with '\' escapes (tar1090-db) and "quoted" fields (plain CSV)
        private static void Split(string line, char separator, List<string> fields)
        {
            fields.Clear();
            var current = new System.Text.StringBuilder();
            var quoted = false;
            for (var i = 0; i < line.Length; i++)
            {
                var ch = line[i];
                if (ch == '\\' && i + 1 < line.Length)
                {
                    current.Append(line[++i]);
                }
                else if (ch == '"')
                {
                    quoted = !quoted;
                }
                else if (ch == separator && !quoted)
                {
                    fields.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(ch);
                }
            }
            fields.Add(current.ToString());
        }

        private static int Find(List<string> header, params string[] names)
        {
            for (var i = 0; i < header.Count; i++)
            {
                var h = header[i].Trim().ToLowerInvariant();
                for (var k = 0; k < names.Length; k++)
                {
                    if (h == names[k])
                    {
                        return i;
                    }
                }
            }
            return -1;
        }

        private static bool TryHex(string text, out int value)
        {
            value = 0;
            text = text.Trim();
            if (text.Length != 6)
            {
                return false;
            }
            for (var i = 0; i < 6; i++)
            {
                var c = text[i];
                int d;
                if (c >= '0' && c <= '9')
                {
                    d = c - '0';
                }
                else if (c >= 'a' && c <= 'f')
                {
                    d = c - 'a' + 10;
                }
                else if (c >= 'A' && c <= 'F')
                {
                    d = c - 'A' + 10;
                }
                else
                {
                    return false;
                }
                value = (value << 4) | d;
            }
            return true;
        }
    }
}
