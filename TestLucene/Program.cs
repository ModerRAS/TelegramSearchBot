using System;
using System.IO;
using Lucene.Net.Store;
using Lucene.Net.Util;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Index;

class Program
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
            var analyzer = new StandardAnalyzer(LuceneVersion.LUCENE_48);
            var config = new IndexWriterConfig(LuceneVersion.LUCENE_48, analyzer);
            var writer = new IndexWriter(directory, config);
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
            System.IO.Directory.CreateDirectory(testDir);
            var directory = FSDirectory.Open(testDir);
            Console.WriteLine("✓ FSDirectory.Open succeeded with explicit directory creation");
            
            var analyzer = new StandardAnalyzer(LuceneVersion.LUCENE_48);
            var config = new IndexWriterConfig(LuceneVersion.LUCENE_48, analyzer);
            var writer = new IndexWriter(directory, config);
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
            if (System.IO.Directory.Exists(Path.GetDirectoryName(testDir)))
                System.IO.Directory.Delete(Path.GetDirectoryName(testDir), true);
        }
        catch { }
    }
}