
using Grasshopper;
using Rhino;

namespace Speckle.Connectors.GrasshopperShared.Registration;

public static class CurrentDocument
{
  private static RhinoDoc? s_headlessDoc;
  
  public static void DisposeHeadlessDoc()
  {
#if RHINO7_OR_GREATER
    s_headlessDoc?.Dispose();
#endif
    s_headlessDoc = null;
  }

  public static void SetupHeadlessDoc()
  {
#if RHINO7_OR_GREATER
    // var templatePath = Path.Combine(Helpers.UserApplicationDataPath, "Speckle", "Templates",
    //   SpeckleGHSettings.HeadlessTemplateFilename);
    // Console.WriteLine($"Setting up doc. Looking for '{templatePath}'");
    // _headlessDoc = File.Exists(templatePath)
    //   ? RhinoDoc.CreateHeadless(templatePath)
    //   : RhinoDoc.CreateHeadless(null);

    s_headlessDoc = RhinoDoc.CreateHeadless(null);
    Console.WriteLine(
      $"Speckle - Backup headless doc is ready: '{s_headlessDoc.Name ?? "Untitled"}'\n    with template: '{s_headlessDoc.TemplateFileUsed ?? "No template"}'\n    with units: {s_headlessDoc.ModelUnitSystem}"
    );
    Console.WriteLine(
      "Speckle - To modify the units in a headless run, you can override the 'RhinoDoc.ActiveDoc' in the '.gh' file using a c#/python script."
    );
#endif
  }
  /// <summary>
  /// Get the current document for this Grasshopper instance.
  /// This will correspond to the `ActiveDoc` on normal Rhino usage, while in headless mode it will try to load
  /// </summary>
  /// <returns></returns>
  public static RhinoDoc? Document
  {
    get
    {
#if RHINO7_OR_GREATER
      if (Instances.RunningHeadless && RhinoDoc.ActiveDoc == null && s_headlessDoc != null)
      {
        // Running headless, with no ActiveDoc override and _headlessDoc was correctly initialised.
        // Only time the _headlessDoc is not set is upon document opening, where the components will
        // check for this as their normal initialisation routine, but the document will be refreshed on every solution run.
        Console.WriteLine(
          $"Speckle - Fetching headless doc '{s_headlessDoc.Name ?? "Untitled"}'\n    with template: '{s_headlessDoc.TemplateFileUsed ?? "No template"}'"
        );
        Console.WriteLine("    Model units:" + s_headlessDoc.ModelUnitSystem);
        return s_headlessDoc;
      }

      return RhinoDoc.ActiveDoc;
#else
    return RhinoDoc.ActiveDoc;
#endif
    }
  }
}
