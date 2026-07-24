using Avalonia.Platform;
using System;
using ControlCatalog.Pages;

namespace ControlCatalog.Desktop;

public class EmbedSampleGtk : INativeDemoControl
{
    public IPlatformHandle CreateControl(bool isSecond, IPlatformHandle parent, Func<IPlatformHandle> createDefault)
    {
        return createDefault();
    }
}
