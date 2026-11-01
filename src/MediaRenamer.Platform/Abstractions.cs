namespace MediaRenamer.Platform;

/// <summary>Stores API keys and credentials. Windows: DPAPI/Credential Manager; Linux: libsecret.</summary>
public interface ISecretStore
{
    string? Get(string key);
    void Set(string key, string value);
    void Remove(string key);
}

/// <summary>OS-specific file name rules used by the rename safety checks.</summary>
public interface IPathRules
{
    bool IsCaseSensitive { get; }
    int MaxPathLength { get; }
    bool IsValidFileName(string name, out string? reason);
    string Sanitize(string name);
}
