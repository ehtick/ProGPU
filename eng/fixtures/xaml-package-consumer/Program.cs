using PackageConsumer;

var page = new MainPage();
if (!string.Equals(
        page.ActionContentValue,
        "Packaged generator",
        StringComparison.Ordinal))
    throw new InvalidOperationException("Packaged x:Bind output did not initialize.");
if (!string.Equals(
        page.ResourceTextValue,
        "Packaged resource source",
        StringComparison.Ordinal))
    throw new InvalidOperationException("Packaged resource binding did not initialize.");

page.UpdateResourceTitle("Packaged resource update");
if (!string.Equals(
        page.ResourceTextValue,
        "Packaged resource update",
        StringComparison.Ordinal))
    throw new InvalidOperationException("Packaged resource binding did not update.");

Console.WriteLine("ProGPU packaged XAML consumer succeeded.");
