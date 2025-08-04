using Grasshopper.Kernel;

namespace Speckle.Connectors.GrasshopperShared;

public class Speckle_Connectors_GrasshopperSharedInfo : GH_AssemblyInfo
{
  public override string Name => "Speckle.Connector.Grasshopper";

  // Return a 24x24 pixel bitmap to represent this GHA library.
  // public override Bitmap Icon => null;

  // Return a short string describing the purpose of this GHA library.
  public override string Description => "x";

  public override Guid Id => new Guid("d711dd2a-9c17-483c-a92d-45c1fc736c46");

  // Return a string identifying you or your company.
  public override string AuthorName => "Speckle";

  // Return a string representing your preferred contact details.
  public override string AuthorContact => "info@speckle.systems";

  // Return a string representing the version.  This returns the same version as the assembly.
  public override string? AssemblyVersion => GetType().Assembly.GetName().Version?.ToString();
}
