using System;

namespace Microsoft.UI.Xaml;

public interface IXamlServiceProvider
{
    object? GetService(Type serviceType);
}
