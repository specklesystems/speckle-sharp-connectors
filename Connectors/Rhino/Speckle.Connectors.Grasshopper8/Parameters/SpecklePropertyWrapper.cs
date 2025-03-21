using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Speckle.Connectors.Grasshopper8.HostApp;

namespace Speckle.Connectors.Grasshopper8.Parameters;

public class SpecklePropertyGoo : GH_Goo<KeyValuePair<string, object?>>, ISpeckleGoo
{
  public override IGH_Goo Duplicate() => throw new NotImplementedException();

  public override string ToString() => $"{Value.Key}:{Value.Value}";

  public override bool IsValid => true;
  public override string TypeName => "Speckle property wrapper";
  public override string TypeDescription => "Speckle property wrapper";

  public SpecklePropertyGoo() { }

  public SpecklePropertyGoo(KeyValuePair<string, object?> value)
  {
    Value = value;
  }
}

public class SpecklePropertyParam : GH_Param<SpecklePropertyGoo>
{
  /// <summary>
  /// Gets the unique ID for this component. Do not change this ID after release.
  /// </summary>
  public override Guid ComponentGuid => new Guid("B3101D12-DA73-45DF-B617-16E1C65BB37C");

  protected override Bitmap Icon => BitmapBuilder.CreateHexagonalBitmap("P");

  public SpecklePropertyParam()
    : this(GH_ParamAccess.item) { }

  public SpecklePropertyParam(IGH_InstanceDescription tag)
    : base(tag) { }

  public SpecklePropertyParam(IGH_InstanceDescription tag, GH_ParamAccess access)
    : base(tag, access) { }

  public SpecklePropertyParam(GH_ParamAccess access)
    : base("Speckle Property Wrapper", "SPO", "Represents a Speckle Property", "Speckle", "Params", access) { }
}
