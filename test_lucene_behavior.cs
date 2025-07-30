using System;
using System.IO;
using Lucene.Net.Store;

class TestLuceneBehavior
{
    static void Main()
    {
        string testDir = Path.Combine("/tmp", "lucene_test", "nonexistent_dir");
        Console.WriteLine($"Testing with directory: {testDir}");
        
        try
        {
            // This should fail if directory doesn't exist
            var directory = FSDirectory.Open(testDir);
            Console.WriteLine("✓ FSDirectory.Open succeeded");
            
            // Try to create an index writer
            var analyzer = new Lucene.Net.Analysis.Standard.StandardAnalyzer(Lucene.Net.Util.LuceneVersion.LUCENE_48);
            var config = new Lucene.Net.Index.IndexWriterConfig(Lucene.Net.Util.LuceneVersion.LUCENE_48, analyzer);
            var writer = new Lucene.Net.Index.IndexWriter(directory, config);
            Console.WriteLine("✓ IndexWriter creation succeeded");
            writer.Dispose();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Error: {ex.GetType().Name}: {ex.Message}");
        }
        
        // Now test with explicit directory creation
        Console.WriteLine("\nTesting with explicit directory creation...");
        try
        {
            Directory.CreateDirectory(testDir);
            var directory = FSDirectory.Open(testDir);
            Console.WriteLine("✓ FSDirectory.Open succeeded with explicit directory creation");
            
            var analyzer = new Lucene.Net.Analysis.Standard.StandardAnalyzer(Lucene.Net.Util.LuceneVersion.LUCENE_48);
            var config = new Lucene.Net.Index.IndexWriterConfig(Lucene.Net.Util.LuceneVersion.LUCENE_48, analyzer);
            var writer = new Lucene.Net.Index.IndexWriter(directory, config);
            Console.WriteLine("✓ IndexWriter creation succeeded");
            writer.Dispose();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Error: {ex.GetType().Name}: {ex.Message}");
        }
        
        // Clean up
        try
        {
            if (Directory.Exists(Path.GetDirectoryName(testDir)))
                Directory.Delete(Path.GetDirectoryName(testDir), true);
        }
        catch { }
    }
}