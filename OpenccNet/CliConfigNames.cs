namespace OpenccNet;

internal static class CliConfigNames
{
    internal static readonly HashSet<string> All =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "s2t",
            "t2s",
            "s2tw",
            "tw2s",
            "s2twp",
            "tw2sp",
            "s2hkp",
            "hk2sp",
            "s2hk",
            "hk2s",
            "t2tw",
            "tw2t",
            "t2twp",
            "tw2tp",
            "t2hk",
            "hk2t",
            "t2jp",
            "jp2t"
        };

    internal static bool IsValid(string value)
    {
        return All.Contains(value);
    }
}