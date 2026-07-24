using System;

using Avalonia.Platform;
using ControlCatalog.Pages;

namespace ControlCatalog.Desktop;

public class EmbedSampleMac : INativeDemoControl
{
    public IPlatformHandle CreateControl(bool isSecond, IPlatformHandle parent, Func<IPlatformHandle> createDefault)
    {
        return createDefault();
    }
}
