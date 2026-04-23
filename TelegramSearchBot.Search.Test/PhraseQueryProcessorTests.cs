using System.Linq;
using System.Text.RegularExpressions;
using Lucene.Net.Analysis.Cn.Smart;
using Lucene.Net.Index;
using Lucene.Net.Store;
using Lucene.Net.Util;
using TelegramSearchBot.Search.Tool;
using TelegramSearchBot.Tokenizer.Abstractions;

namespace TelegramSearchBot.Search.Test {
    public class PhraseQueryProcessorTests {
        [Fact]
        public void ExtractPhraseQueries_PreservesDuplicateTermsForPhraseQueries() {
            var processor = new PhraseQueryProcessor(new RepeatedTokenizer(), new ExtFieldQueryOptimizer());
            using var directory = new RAMDirectory();
            var config = new IndexWriterConfig(LuceneVersion.LUCENE_48, new SmartChineseAnalyzer(LuceneVersion.LUCENE_48));
            using (var writer = new IndexWriter(directory, config)) {
                writer.Commit();
            }

            using var reader = DirectoryReader.Open(directory);

            var (phraseQueries, _) = processor.ExtractPhraseQueries("\"北京 北京\"", reader, 1);

            var combinedPhraseQuery = Assert.Single(phraseQueries);
            var contentPhrase = Assert.Single(combinedPhraseQuery.Clauses
                .Where(static clause => clause.Query is Lucene.Net.Search.PhraseQuery)
                .Select(static clause => ( Lucene.Net.Search.PhraseQuery ) clause.Query));

            Assert.Equal(2, Regex.Matches(contentPhrase.ToString(), "北京").Count);
        }

        private sealed class RepeatedTokenizer : ITokenizer {
            public TokenizerMetadata Metadata { get; } = new("Repeated", "Test", false);

            public IReadOnlyList<string> Tokenize(string text) {
                return new[] { "北京" };
            }

            public IReadOnlyList<string> SafeTokenize(string text) {
                return new[] { "北京" };
            }

            public IReadOnlyList<TokenWithOffset> TokenizeWithOffsets(string text) {
                return new[] {
                    new TokenWithOffset(0, 2, "北京"),
                    new TokenWithOffset(3, 5, "北京")
                };
            }
        }
    }
}
