// Test to verify the extension loading issue
using System;
using Microsoft.EntityFrameworkCore;
using TelegramSearchBot.Model.Data;

public class ExtensionLoadingTest
{
    // This test demonstrates the issue with extension loading in Lucene indexing
    
    public static void Main()
    {
        Console.WriteLine("=== Message Extension Loading Issue Verification ===");
        Console.WriteLine("Problem: MessageService.AddToLucene uses FindAsync which doesn't load MessageExtensions");
        Console.WriteLine("Current code in AddToLucene:");
        Console.WriteLine("  var message = await DataContext.Messages.FindAsync(messageOption.MessageDataId);");
        Console.WriteLine();
        Console.WriteLine("This loads only the basic Message entity without MessageExtensions");
        Console.WriteLine("Result: OCR/ASR extensions are never indexed in Lucene");
        Console.WriteLine();
        Console.WriteLine("Fix needed: Use Include to eagerly load MessageExtensions");
        Console.WriteLine("  var message = await DataContext.Messages");
        Console.WriteLine("      .Include(m => m.MessageExtensions)");
        Console.WriteLine("      .FirstOrDefaultAsync(m => m.Id == messageOption.MessageDataId);");
    }
}