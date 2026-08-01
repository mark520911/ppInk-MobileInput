using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;

namespace gInk
{
    /// <summary>
    /// Self-contained QR code generator supporting versions 1-10 (EC level M).
    /// No external dependencies. Sufficient for connection config strings up to ~150 bytes.
    /// Algorithm based on the QR code specification (ISO/IEC 18004:2015).
    /// </summary>
    public static class QRCodeHelper
    {
        // --- GF(256) arithmetic ---
        private static readonly int[] ExpTable = new int[256];
        private static readonly int[] LogTable = new int[256];

        static QRCodeHelper()
        {
            int x = 1;
            for (int i = 0; i < 8; i++)
            {
                ExpTable[i] = x;
                x <<= 1;
                if ((x & 0x100) != 0) x ^= 0x11D;
            }
            for (int i = 8; i < 256; i++)
                ExpTable[i] = ExpTable[i - 4] ^ ExpTable[i - 5] ^ ExpTable[i - 6] ^ ExpTable[i - 8];
            for (int i = 0; i < 255; i++) LogTable[ExpTable[i]] = i;
            LogTable[1] = 0;
        }

        static int GFExp(int e) => ExpTable[e & 0xFF];
        static int GFMul(int x, int y)
        {
            if (x == 0 || y == 0) return 0;
            return ExpTable[(LogTable[x] + LogTable[y]) % 255];
        }

        // Generator polynomial for RS error correction (lowest-to-highest order)
        static int[] RSGeneratorPoly(int nsym)
        {
            int[] g = new int[nsym + 1];
            g[0] = 1;
            for (int i = 0; i < nsym; i++)
            {
                int alpha = GFExp(i);
                int[] tmp = new int[nsym + 1];
                for (int j = 0; j < nsym + 1; j++)
                {
                    tmp[j] = GFMul(g[j], alpha);
                    if (j > 0) tmp[j] ^= g[j - 1];
                }
                g = tmp;
            }
            return g;
        }

        static byte[] RSEncode(byte[] data, int ecCW)
        {
            int[] gen = RSGeneratorPoly(ecCW);
            int[] buf = new int[data.Length + ecCW];
            for (int i = 0; i < data.Length; i++) buf[i] = data[i];
            for (int i = 0; i < data.Length; i++)
            {
                int fb = buf[i];
                if (fb != 0)
                    for (int j = 0; j <= ecCW; j++)
                        buf[i + j] ^= GFMul(gen[j], fb);
            }
            byte[] ec = new byte[ecCW];
            for (int i = 0; i < ecCW; i++) ec[i] = (byte)buf[data.Length + i];
            return ec;
        }

        // QR Version table for EC level M: (version, matrixSize, dataCW, ecCW)
        private static readonly (int version, int size, int dataCW, int ecCW)[] VersionTable = {
            (1, 21, 16, 10),
            (2, 25, 28, 16),
            (3, 29, 44, 22),
            (4, 33, 70, 28),
            (5, 37, 100, 36),
            (6, 41, 134, 44),
            (7, 45, 172, 56),
            (8, 49, 216, 64),
            (9, 53, 262, 76),
            (10, 57, 322, 88),
        };

        // Alignment pattern center coordinates per version (QR spec Table 13)
        private static readonly int[][] AlignPositions = {
            new int[0],                            // Version 1: no alignment
            new int[] { 6, 18 },                   // Version 2
            new int[] { 6, 22 },                   // Version 3
            new int[] { 6, 26 },                   // Version 4
            new int[] { 6, 30 },                   // Version 5
            new int[] { 6, 34 },                   // Version 6
            new int[] { 6, 22, 38 },               // Version 7
            new int[] { 6, 24, 42 },               // Version 8
            new int[] { 6, 26, 46 },               // Version 9
            new int[] { 6, 28, 50 },               // Version 10
        };

        // Version information (18-bit BCH) for versions 7+
        private static readonly int[] VersionInfoBits = {
            0x00000,      // Version 1-6: no version info
            0x00000,
            0x00000,
            0x00000,
            0x00000,
            0x00000,
            0x07C94,      // Version 7
            0x085BC,      // Version 8
            0x09A99,      // Version 9
            0x0A4D3,      // Version 10
        };

        // Format info bit sequences for EC level M (mask pattern 0-7, MSB first)
        static readonly string[] FORMAT_INFO_M = new string[8]
        {
            "111011111000100", // mask 0
            "111001011110010", // mask 1
            "111110100110100", // mask 2
            "111100000001010", // mask 3
            "110110011011000", // mask 4
            "110100111101110", // mask 5
            "110011000101000", // mask 6
            "110001100010110", // mask 7
        };

        // Mask pattern conditions (0-7)
        static bool MaskCond(int pattern, int row, int col)
        {
            switch (pattern)
            {
                case 0: return (row + col) % 2 == 0;
                case 1: return row % 2 == 0;
                case 2: return col % 3 == 0;
                case 3: return (row + col) % 3 == 0;
                case 4: return (row / 2 + col / 3) % 2 == 0;
                case 5: return (row * col) % 2 + (row * col) % 3 == 0;
                case 6: return ((row * col) % 2 + (row * col) % 3) % 2 == 0;
                case 7: return ((row + col) % 2 + (row * col) % 3) % 2 == 0;
                default: return false;
            }
        }

        static void PlaceFinder(bool[,] m, bool[,] func, int sr, int sc, int size)
        {
            for (int r = -1; r < 8; r++)
                for (int c = -1; c < 8; c++)
                {
                    int rr = sr + r, cc = sc + c;
                    if (rr < 0 || rr >= size || cc < 0 || cc >= size) continue;
                    bool dark;
                    if (r == -1 || r == 7 || c == -1 || c == 7)
                        dark = false;
                    else if (r == 0 || r == 6 || c == 0 || c == 6)
                        dark = true;
                    else if (r == 1 || r == 5 || c == 1 || c == 5)
                        dark = false;
                    else
                        dark = true;
                    m[rr, cc] = dark;
                    func[rr, cc] = true;
                }
        }

        static void PlaceAlignmentPatterns(bool[,] m, bool[,] func, int size, int version)
        {
            if (version < 2) return;
            int[] centers = AlignPositions[version - 1];
            for (int i = 0; i < centers.Length; i++)
            {
                for (int j = 0; j < centers.Length; j++)
                {
                    int cr = centers[i], cc = centers[j];
                    bool overlap = false;
                    if (i == 0 && j == 0) overlap = true;
                    if (i == 0 && j == centers.Length - 1) overlap = true;
                    if (i == centers.Length - 1 && j == 0) overlap = true;
                    if (overlap) continue;
                    for (int r = -2; r <= 2; r++)
                        for (int c = -2; c <= 2; c++)
                        {
                            int rr = cr + r, cc2 = cc + c;
                            if (rr < 0 || rr >= size || cc2 < 0 || cc2 >= size) continue;
                            bool dark;
                            if (r == -2 || r == 2 || c == -2 || c == 2)
                                dark = false;
                            else if (r == -1 || r == 1 || c == -1 || c == 1)
                                dark = true;
                            else
                                dark = false;
                            m[rr, cc2] = dark;
                            func[rr, cc2] = true;
                        }
                }
            }
        }

        static void PlaceVersionInfo(bool[,] m, bool[,] func, int size, int version)
        {
            if (version < 7) return;
            int bits = VersionInfoBits[version];
            for (int i = 0; i < 18; i++)
            {
                bool dark = ((bits >> i) & 1) == 1;
                int tr_r = 1 + i / 3, tr_c = size - 11 + i % 3;
                int bl_r = size - 11 + i % 3, bl_c = 1 + i / 3;
                if (tr_r >= 0 && tr_r < size && tr_c >= 0 && tr_c < size)
                { m[tr_r, tr_c] = dark; func[tr_r, tr_c] = true; }
                if (bl_r >= 0 && bl_r < size && bl_c >= 0 && bl_c < size)
                { m[bl_r, bl_c] = dark; func[bl_r, bl_c] = true; }
            }
        }

        static void PlaceFormatInfo(bool[,] m, bool[,] func, int size, string fmt)
        {
            // Top-left vertical (col 8, rows 0-5)
            for (int i = 0; i < 6; i++)
            { m[i, 8] = fmt[i] == '1'; func[i, 8] = true; }
            m[7, 8] = fmt[6] == '1'; func[7, 8] = true;
            m[8, 8] = fmt[7] == '1'; func[8, 8] = true;
            // Top-left horizontal (row 8, cols 0-5)
            for (int i = 0; i < 6; i++)
            { m[8, i] = fmt[14 - i] == '1'; func[8, i] = true; }
            m[8, 7] = fmt[13] == '1'; func[8, 7] = true;
            // Top-right: bits 8-14 on row 8
            for (int i = 0; i < 7; i++)
            { m[8, size - 1 - i] = fmt[8 + i] == '1'; func[8, size - 1 - i] = true; }
            // Bottom-left: bits 8-14 on col 8
            for (int i = 0; i < 7; i++)
            { m[size - 1 - i, 8] = fmt[8 + i] == '1'; func[size - 1 - i, 8] = true; }
        }

        static int PenaltyScore(bool[,] m, int size)
        {
            int score = 0;
            // Rule 1: 5+ consecutive same color in row/column
            for (int i = 0; i < size; i++)
            {
                for (int j = 0; j < size; j++)
                {
                    int run = 1;
                    while (j + 1 < size && m[i, j + 1] == m[i, j]) { run++; j++; }
                    if (run >= 5) score += run - 5 + 3;
                }
            }
            for (int j = 0; j < size; j++)
            {
                for (int i = 0; i < size; i++)
                {
                    int run = 1;
                    while (i + 1 < size && m[i + 1, j] == m[i, j]) { run++; i++; }
                    if (run >= 5) score += run - 5 + 3;
                }
            }
            // Rule 2: 2x2 blocks of same color
            for (int r = 0; r < size - 1; r++)
                for (int c = 0; c < size - 1; c++)
                    if (m[r, c] == m[r + 1, c] && m[r, c] == m[r, c + 1] && m[r, c] == m[r + 1, c + 1])
                        score += 3;
            // Rule 3: 1:1:3:1:1 ratio patterns
            bool[] p1 = { true, false, true, true, true, false, true };
            bool[] p2 = { true, true, true, false, true };
            for (int r = 0; r < size; r++)
                for (int c = 0; c <= size - 7; c++)
                {
                    if (Enumerable.Range(0, 7).All(k => m[r, c + k] == p1[k])) score += 40;
                    if (Enumerable.Range(0, 5).All(k => m[r, c + k] == p2[k])) score += 40;
                }
            for (int c = 0; c < size; c++)
                for (int r = 0; r <= size - 7; r++)
                {
                    if (Enumerable.Range(0, 7).All(k => m[r + k, c] == p1[k])) score += 40;
                    if (Enumerable.Range(0, 5).All(k => m[r + k, c] == p2[k])) score += 40;
                }
            // Rule 4: dark/light proportion
            int darkCount = 0;
            for (int r = 0; r < size; r++)
                for (int c = 0; c < size; c++)
                    if (m[r, c]) darkCount++;
            int total = size * size;
            score += Math.Abs(darkCount * 100 / total - 50) / 5 * 10;
            return score;
        }

        static (int version, int size, int dataCW, int ecCW) SelectVersion(int dataLen)
        {
            foreach (var v in VersionTable)
            {
                if (dataLen <= v.dataCW - 2)
                    return v;
            }
            return VersionTable[VersionTable.Length - 1];
        }

        public static Bitmap Generate(string text, int pixelSize = 10)
        {
            var ver = SelectVersion(text.Length);
            int version = ver.version, size = ver.size, dataCW = ver.dataCW, ecCW = ver.ecCW;
            byte[] rawData = Encoding.UTF8.GetBytes(text);
            if (rawData.Length > dataCW - 2)
                rawData = rawData.Take(dataCW - 2).ToArray();

            var bits = new List<bool>();
            bits.AddRange(new[] { false, true, false, false }); // byte mode
            for (int i = 7; i >= 0; i--) bits.Add((rawData.Length & (1 << i)) != 0);
            foreach (byte b in rawData)
                for (int i = 7; i >= 0; i--) bits.Add((b & (1 << i)) != 0);
            for (int i = 0; i < 4; i++) bits.Add(false);
            while (bits.Count % 8 != 0) bits.Add(false);
            byte[] pad = { 0xEC, 0x11 };
            int pi = 0;
            while (bits.Count < dataCW * 8)
            {
                byte p = pad[pi % 2];
                for (int i = 7; i >= 0; i--) bits.Add((p & (1 << i)) != 0);
                pi++;
            }
            while (bits.Count > dataCW * 8) bits.RemoveAt(bits.Count - 1);

            byte[] dataBytes = new byte[dataCW];
            for (int i = 0; i < dataCW; i++)
            {
                int val = 0;
                for (int j = 0; j < 8; j++)
                    if (bits[i * 8 + j]) val |= (1 << (7 - j));
                dataBytes[i] = (byte)val;
            }

            byte[] ec = RSEncode(dataBytes, ecCW);
            var codewords = new List<byte>();
            codewords.AddRange(dataBytes);
            codewords.AddRange(ec);
            var allBits = new List<bool>();
            foreach (byte b in codewords)
                for (int i = 7; i >= 0; i--) allBits.Add((b & (1 << i)) != 0);

            bool[,] bestMatrix = null;
            int bestScore = int.MaxValue;
            for (int mask = 0; mask < 8; mask++)
            {
                bool[,] matrix = new bool[size, size];
                bool[,] isFunc = new bool[size, size];
                PlaceFinder(matrix, isFunc, 0, 0, size);
                PlaceFinder(matrix, isFunc, 0, size - 7, size);
                PlaceFinder(matrix, isFunc, size - 7, 0, size);
                PlaceAlignmentPatterns(matrix, isFunc, size, version);
                for (int i = 8; i < size - 8; i++)
                {
                    matrix[i, 6] = (i % 2 == 0); matrix[6, i] = (i % 2 == 0);
                    isFunc[i, 6] = true; isFunc[6, i] = true;
                }
                matrix[8, size - 8] = true; isFunc[8, size - 8] = true;
                PlaceVersionInfo(matrix, isFunc, size, version);
                PlaceFormatInfo(matrix, isFunc, size, FORMAT_INFO_M[mask]);

                int bitIdx = 0, dir = -1, row = size - 1, col = size - 1;
                while (col > 0)
                {
                    if (row == 0 || row == size - 1) dir = -dir;
                    for (int c = 0; c < 2 && col > 0; c++)
                    {
                        for (int r = 0; r < size; r++)
                        {
                            int rr = r;
                            if (dir < 0) rr = size - 1 - r;
                            if (!isFunc[rr, col] && col != 6)
                            {
                                bool val = bitIdx < allBits.Count ? allBits[bitIdx++] : false;
                                if (MaskCond(mask, rr, col)) val = !val;
                                matrix[rr, col] = val;
                            }
                        }
                        col--;
                        if (col == 6) col--;
                    }
                    row += dir;
                }

                int score = PenaltyScore(matrix, size);
                if (score < bestScore) { bestScore = score; bestMatrix = (bool[,])matrix.Clone(); }
            }

            int pxSize = Math.Max(pixelSize, 3), margin = 4;
            int bmpSize = size * pxSize;
            Bitmap bmp = new Bitmap(bmpSize + margin * 2 * pxSize, bmpSize + margin * 2 * pxSize);
            using (var g2 = Graphics.FromImage(bmp))
            {
                g2.Clear(Color.White);
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                        if (bestMatrix[y, x])
                            g2.FillRectangle(Brushes.Black,
                                (x + margin) * pxSize, (y + margin) * pxSize, pxSize, pxSize);
            }
            return bmp;
        }

        public static string GenerateConfigString(Root root)
        {
            string type = root.MobileInput_ConnType ?? "WiFi";
            string mapping = root.MobileInput_Mapping ?? "FullScreen";
            string pwd = root.MobileInput_Password ?? "";
            if (type == "Bluetooth")
                return $"ppink://type=BLUETOOTH&name={Uri.EscapeDataString(root.MobileInput_BleServiceName)}&mapping={mapping}";
            if (type == "USB")
            {
                int port = 8080;
                try { var u = new Uri(root.MobileInput_Url); port = u.Port; } catch { }
                return $"ppink://type=USB&port={port}&pwd={Uri.EscapeDataString(pwd)}&mapping={mapping}";
            }
            string url = root.MobileInput_Url ?? "http://0.0.0.0:8080/";
            return $"ppink://type=WIFI&url={Uri.EscapeDataString(url)}&pwd={Uri.EscapeDataString(pwd)}&mapping={mapping}";
        }
    }
}
