using System.Reflection;
using System.Windows;
using PoeShared.Blazor.Scaffolding;

[assembly: ThemeInfo(
    ResourceDictionaryLocation.None, //where theme specific resource dictionaries are located
    //(used if a resource is not found in the page,
    // or application resource dictionaries)
    ResourceDictionaryLocation.SourceAssembly //where the generic resource dictionary is located
    //(used if a resource is not found in the page,
    // app, or any theme specific resource dictionaries)
)]

[assembly: AssemblyHasPoeMetadataReplacements]
[assembly: AssemblyHasPoeConfigConverters]
[assembly: AssemblyHasBlazorViews]

[assembly: AssemblyProduct("CVATAAT")]
[assembly: AssemblyVersion("0.0.0.0")]
[assembly: AssemblyFileVersion("0.0.0.0")]
[assembly: AssemblyMetadata("SquirrelAwareVersion", "0")]