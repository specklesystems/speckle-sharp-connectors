using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Grasshopper.Rhinoceros;
using Speckle.Connectors.Grasshopper8.Components;
using Speckle.Connectors.Grasshopper8.HostApp;

namespace Speckle.Connectors.Grasshopper8.Parameters;

public class SpecklePropertyGroupGoo : GH_Goo<Dictionary<string, SpecklePropertyGoo>>, ISpeckleGoo
{
  public override IGH_Goo Duplicate() => throw new NotImplementedException();

  public override string ToString() => $"PropertyGroup ({Value.Count})";

  public override bool IsValid => true;
  public override string TypeName => "Speckle property group wrapper";
  public override string TypeDescription => "Speckle property group wrapper";

  public SpecklePropertyGroupGoo() { }

  public SpecklePropertyGroupGoo(Dictionary<string, SpecklePropertyGoo> value)
  {
    Value = value;
  }

  public override bool CastFrom(object source)
  {
    switch (source)
    {
      case SpecklePropertyGroupGoo specklePropertyGroup:
        Value = specklePropertyGroup.Value;
        return true;
      case ModelUserText userText:
        Dictionary<string, SpecklePropertyGoo> dictionary = userText.ToDictionary(
          kvp => kvp.Key,
          kvp => new SpecklePropertyGoo(kvp.Value, new() { kvp.Key })
        );
        Value = dictionary;
        return true;
    }

    return false;
  }
}

public class SpecklePropertyGroupParam : GH_Param<SpecklePropertyGroupGoo>
{
  public override Guid ComponentGuid => new("AF4757C3-BA33-4ACD-A92B-C80356043129");
  protected override Bitmap Icon => BitmapBuilder.CreateHexagonalBitmap("PG");

  public SpecklePropertyGroupParam()
    : this(GH_ParamAccess.item) { }

  public SpecklePropertyGroupParam(IGH_InstanceDescription tag)
    : base(tag) { }

  public SpecklePropertyGroupParam(IGH_InstanceDescription tag, GH_ParamAccess access)
    : base(tag, access) { }

  public SpecklePropertyGroupParam(GH_ParamAccess access)
    : base(
      "Speckle Property Group",
      "SPGO",
      "Represents a Dictionary property group",
      ComponentCategories.PRIMARY_RIBBON,
      ComponentCategories.PARAMETERS,
      access
    ) { }
}
