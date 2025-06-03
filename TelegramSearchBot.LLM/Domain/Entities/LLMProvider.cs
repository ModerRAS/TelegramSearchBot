namespace TelegramSearchBot.LLM.Domain.Entities;

/// <summary>
/// LLM提供商枚举
/// </summary>
public enum LLMProvider
{
    OpenAI,
    Ollama,
    Gemini
}

/// <summary>
/// LLM提供商聚合根
/// </summary>
public class LLMProviderEntity
{
    public LLMProvider Provider { get; private set; }
    public string Name { get; private set; }
    public bool IsActive { get; private set; }
    public HashSet<string> SupportedCapabilities { get; private set; }

    private LLMProviderEntity() 
    {
        SupportedCapabilities = new HashSet<string>();
    }

    public LLMProviderEntity(LLMProvider provider, string name) : this()
    {
        Provider = provider;
        Name = name ?? throw new ArgumentNullException(nameof(name));
        IsActive = true;
    }

    public void AddCapability(string capability)
    {
        if (string.IsNullOrWhiteSpace(capability))
            throw new ArgumentException("Capability cannot be null or empty", nameof(capability));
        
        SupportedCapabilities.Add(capability);
    }

    public void RemoveCapability(string capability)
    {
        SupportedCapabilities.Remove(capability);
    }

    public bool HasCapability(string capability)
    {
        return SupportedCapabilities.Contains(capability);
    }

    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;
} 