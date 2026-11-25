namespace smss_api_service_layer.helper
{
    /// <summary>How alike two guardians' names are: the flag the duplicate report shows beside a shared phone number.</summary>
    public static class GuardianNames
    {
        public const string Same = "SAME";
        public const string Similar = "SIMILAR";
        public const string Different = "DIFFERENT";

        /// <summary>Lower case, letters and digits only, one space between words.</summary>
        public static string Normalise(string? name)
        {
            var chars = (name ?? "").ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray();
            return string.Join(' ', new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }

        /// <summary>SAME when the normalised names are equal; SIMILAR when they are a typo apart or one name holds every word of the other (or the same words in another order); otherwise DIFFERENT.</summary>
        public static string Compare(string? a, string? b)
        {
            var x = Normalise(a); var y = Normalise(b);
            if (x.Length == 0 || y.Length == 0) return Different;
            if (x == y) return Same;
            var tx = x.Split(' '); var ty = y.Split(' ');
            if (tx.OrderBy(t => t).SequenceEqual(ty.OrderBy(t => t))) return Similar;
            var (small, big) = tx.Length <= ty.Length ? (tx, ty) : (ty, tx);
            if (small.Length >= 2 && small.All(t => big.Contains(t))) return Similar;
            return Distance(x, y) <= Math.Max(1, Math.Min(x.Length, y.Length) / 8) ? Similar : Different;
        }

        /// <summary>The name match of a whole group: SAME when every pair is the same, SIMILAR when every pair is at least similar, otherwise DIFFERENT.</summary>
        public static string CompareAll(IReadOnlyList<string?> names)
        {
            var worst = Same;
            for (var i = 0; i < names.Count; i++)
                for (var j = i + 1; j < names.Count; j++)
                {
                    var c = Compare(names[i], names[j]);
                    if (c == Different) return Different;
                    if (c == Similar) worst = Similar;
                }
            return worst;
        }

        private static int Distance(string a, string b)
        {
            var d = new int[a.Length + 1, b.Length + 1];
            for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
            for (var j = 0; j <= b.Length; j++) d[0, j] = j;
            for (var i = 1; i <= a.Length; i++)
                for (var j = 1; j <= b.Length; j++)
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            return d[a.Length, b.Length];
        }
    }
}
