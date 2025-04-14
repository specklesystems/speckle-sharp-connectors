using System.Reflection;
using Grasshopper.Kernel;
using Speckle.Connectors.GrasshopperShared.Components;

namespace Speckle.Connectors.GrasshopperShared.Parameters;

public class SpeckleUrlModelResourceParam : GH_Param<SpeckleUrlModelResourceGoo>
{
  public SpeckleUrlModelResourceParam()
    : this(GH_ParamAccess.item) { }

  public SpeckleUrlModelResourceParam(IGH_InstanceDescription tag)
    : base(tag) { }

  public SpeckleUrlModelResourceParam(IGH_InstanceDescription tag, GH_ParamAccess access)
    : base(tag, access) { }

  public SpeckleUrlModelResourceParam(GH_ParamAccess access)
    : base(
      "Speckle Model Resource",
      "SMR",
      "A Speckle model resource",
      ComponentCategories.PRIMARY_RIBBON,
      ComponentCategories.PARAMETERS,
      access
    ) { }

  public override Guid ComponentGuid => new Guid("E5421FC2-F10D-447F-BF23-5C934ABDB2D3");

  protected override Bitmap? Icon
  {
    get
    {
      Assembly assembly = GetType().Assembly;
      var stream = assembly.GetManifestResourceStream(
        assembly.GetName().Name + "." + "Resources" + ".speckle_param_model.png"
      );
      return stream != null ? new Bitmap(stream) : null;
    }
  }
}
