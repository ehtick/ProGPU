using Microsoft.CodeAnalysis.MSBuild;

internal static class CliMsBuildWorkspace
{
    public static MSBuildWorkspace Create()
    {
        var frameworkDirectory =
            new DirectoryInfo(AppContext.BaseDirectory);
        var configuration =
            frameworkDirectory.Parent?.Name;
        if (!string.Equals(
                configuration,
                "Debug",
                StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(
                configuration,
                "Release",
                StringComparison.OrdinalIgnoreCase))
        {
            return MSBuildWorkspace.Create();
        }

        return MSBuildWorkspace.Create(
            new Dictionary<string, string>
            {
                ["Configuration"] = configuration!
            });
    }
}
