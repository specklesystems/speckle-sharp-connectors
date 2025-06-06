using System.Resources;
using System.Runtime.InteropServices;
using Rhino.PlugIns;

// Plug-in Description Attributes - all of these are optional.
// These will show in Rhino's option dialog, in the tab Plug-ins.
[assembly: PlugInDescription(DescriptionType.Address, "")]
[assembly: PlugInDescription(DescriptionType.Country, "")]
[assembly: PlugInDescription(DescriptionType.Email, "hello@speckle.systems")]
[assembly: PlugInDescription(DescriptionType.Phone, "")]
[assembly: PlugInDescription(DescriptionType.Fax, "")]
[assembly: PlugInDescription(DescriptionType.Organization, "Speckle Systems Ltd.")]
[assembly: PlugInDescription(DescriptionType.UpdateUrl, "")]
[assembly: PlugInDescription(DescriptionType.WebSite, "https://speckle.systems")]

// Icons should be Windows .ico files and contain 32-bit images in the following sizes: 16, 24, 32, 48, and 256.
[assembly: PlugInDescription(DescriptionType.Icon, "Speckle.Connectors.Rhino8.Resources.speckle32.ico")]

// The following GUID is for the ID of the typelib if this pro ject is exposed to COM
// This will also be the Guid of the Rhino plug-in
[assembly: Guid("2153799A-0CEC-40DE-BC3A-01E5055222FF")]

[assembly: NeutralResourcesLanguage("en")]
