using System.Linq;
using System.Text.RegularExpressions;

namespace GMentor.Core
{
    public static class ResultParser
    {
        private static readonly Regex RxYouTube =
            new(@"(?im)^\s*YOUTUBE_QUERY:\s*""(?<q>.+?)""\s*$");

        private static readonly string[] HedgeTokens =
        {
            // English
            "not sure","uncertain","maybe","might","unknown","i think","could be",
            // Turkish
            "emin değilim","emin degilim","tam emin değil","tam emin degil",
            "kesin değil","kesin degil","belki","olabilir","net değil","net degil"
        };

        public static string? TryExtractYouTubeQuery(string text)
        {
            var m = RxYouTube.Match(text);
            return m.Success ? m.Groups["q"].Value.Trim() : null;
        }

        public static string SynthesizeYouTubeQueryFrom(string text)
        {
            var line = text.Split('\n').FirstOrDefault() ?? "";
            return string.IsNullOrWhiteSpace(line) ? "game quest fastest route" : line;
        }

        public static string NormalizeBullets(string text)
        {
            var lines = text.Split('\n')
                .Where(l => !l.TrimStart().StartsWith("YOUTUBE_QUERY:"))
                .Select(l => l.Replace("•", "-").TrimEnd());
            return string.Join('\n', lines);
        }

        public static bool IsLowConfidence(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || text.Trim().Length < 40) return true;
            return HedgeTokens.Any(h => text.IndexOf(h, System.StringComparison.OrdinalIgnoreCase) >= 0);
        }
    }
}
