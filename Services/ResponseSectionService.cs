using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace GMentor.Services
{
    public sealed record ParsedSection(string Header, string Body);

    public interface IResponseSectionService
    {
        IReadOnlyList<ParsedSection> SplitIntoSections(string raw);
        FlowDocument BuildMarkdownDocument(string text);
    }

    public sealed class ResponseSectionService : IResponseSectionService
    {
        private static readonly Regex SectionHeaderRegex =
            new(@"^\*\*(?<name>[^*]+)\*\*[:]?\s*", RegexOptions.Compiled);

        public IReadOnlyList<ParsedSection> SplitIntoSections(string raw)
        {
            var sections = new List<ParsedSection>();

            string? currentHeader = null;
            var buffer = new List<string>();

            void Flush()
            {
                if (currentHeader != null && buffer.Count > 0)
                {
                    var body = string.Join("\n", buffer).Trim();
                    if (!string.IsNullOrWhiteSpace(body))
                        sections.Add(new ParsedSection(currentHeader, body));
                }
                buffer.Clear();
            }

            foreach (var originalLine in raw.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
            {
                var line = originalLine ?? string.Empty;
                var trimmed = line.TrimStart();

                // skip bullet headings, only non-bullet bold lines are section headers
                if (!trimmed.StartsWith("-"))
                {
                    var m = SectionHeaderRegex.Match(trimmed);
                    if (m.Success)
                    {
                        Flush();

                        var header = m.Groups["name"].Value.Trim();
                        if (header.EndsWith(":"))
                            header = header.TrimEnd(':');

                        currentHeader = header;

                        var remainder = trimmed[m.Value.Length..].Trim();
                        if (!string.IsNullOrEmpty(remainder))
                            buffer.Add(remainder);

                        continue;
                    }
                }

                buffer.Add(line);
            }

            Flush();

            if (sections.Count == 0 && !string.IsNullOrWhiteSpace(raw))
                sections.Add(new ParsedSection("Details", raw));

            return sections;
        }

        public FlowDocument BuildMarkdownDocument(string text)
        {
            var doc = new FlowDocument
            {
                Background = Brushes.Transparent,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 13
            };

            var para = NewParagraph();

            foreach (var line in text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    doc.Blocks.Add(para);
                    para = NewParagraph();
                    continue;
                }

                var boldMatches = Regex.Split(line, @"(\*\*[^\*]+\*\*)");
                foreach (var part in boldMatches)
                {
                    if (part.StartsWith("**") && part.EndsWith("**") && part.Length > 4)
                    {
                        para.Inlines.Add(new Bold(new Run(part.Trim('*'))));
                    }
                    else
                    {
                        para.Inlines.Add(new Run(part));
                    }
                }

                para.Inlines.Add(new LineBreak());
            }

            doc.Blocks.Add(para);
            return doc;
        }

        private static Paragraph NewParagraph() => new()
        {
            Margin = new Thickness(0, 0, 0, 6),
            LineHeight = 18
        };
    }
}
