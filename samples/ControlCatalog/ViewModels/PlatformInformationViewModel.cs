using System;
using System.Runtime.InteropServices;
using MiniMvvm;

namespace ControlCatalog.ViewModels;

public class PlatformInformationViewModel : ViewModelBase
{
    public PlatformInformationViewModel()
    {
        /*  NOTE:
        *   ------------
        *   The below API is not meant to be used in production Apps. 
        *   If you need to consume this info, please use:
        *      - OperatingSystem ( https://learn.microsoft.com/en-us/dotnet/api/system.operatingsystem | if .NET 5 or greater)
        *      - or RuntimeInformation ( https://learn.microsoft.com/en-us/dotnet/api/system.runtime.interopservices.runtimeinformation )
        */
        
        var environment = RuntimeInformation.IsOSPlatform(OSPlatform.Create("BROWSER"))
            ? "browser"
            : "native";
        var formFactor = OperatingSystem.IsAndroid() || OperatingSystem.IsIOS()
            ? "Mobile"
            : "Desktop";
        PlatformInfo = $"Platform: {formFactor} ({environment})";
    }
    
    public string? PlatformInfo { get; }
}
