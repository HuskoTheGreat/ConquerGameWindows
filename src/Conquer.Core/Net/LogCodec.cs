using System.Collections.Generic;

namespace Conquer.Core.Net
{
    /// <summary>Wire format for the public game log lines sent alongside each state update.</summary>
    public static class LogCodec
    {
        public const int MaxLines = 24;
        public const int MaxLineChars = 160;
        public const int MaxBytes = 8 * 1024;

        public static byte[] Encode(IReadOnlyList<string> lines)
        {
            var w = new WireWriter();
            int n = System.Math.Min(lines.Count, MaxLines);
            w.Byte(n);
            for (int i = 0; i < n; i++) w.String(NameSanitizer.Clean(lines[i], "", MaxLineChars), MaxLineChars * 4);
            return w.ToArray();
        }

        /// <summary>Lines are re-cleaned on arrival, so markup can't be smuggled into the rich-text UI.</summary>
        public static List<string> Decode(byte[] data)
        {
            var r = new WireReader(data, MaxBytes);
            int n = r.Byte(MaxLines);
            var lines = new List<string>(n);
            for (int i = 0; i < n; i++)
            {
                string line = NameSanitizer.Clean(r.String(MaxLineChars * 4), "", MaxLineChars);
                if (line.Length > 0) lines.Add(line);
            }
            r.End();
            return lines;
        }
    }
}
