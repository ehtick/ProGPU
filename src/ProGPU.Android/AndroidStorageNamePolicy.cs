namespace ProGPU.Android;

internal static class AndroidStorageNamePolicy
{
    public static string SanitizeFileName(string? value, string fallback)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fallback);
        string fileName = string.IsNullOrWhiteSpace(value) ? fallback : value;
        foreach (char invalid in Path.GetInvalidFileNameChars()) fileName = fileName.Replace(invalid, '_');
        fileName = fileName.Replace('\0', '_');
        return fileName is "." or ".." ? fallback : fileName;
    }
}
