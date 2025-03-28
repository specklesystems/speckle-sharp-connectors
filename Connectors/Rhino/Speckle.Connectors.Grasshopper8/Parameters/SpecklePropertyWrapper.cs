using System.Runtime.InteropServices;
using Grasshopper.Documentation;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Speckle.Connectors.Grasshopper8.Components;
using Speckle.Connectors.Grasshopper8.HostApp;

namespace Speckle.Connectors.Grasshopper8.Parameters;

public class SpecklePropertyGoo : GH_Goo<object>, ISpeckleGoo
{
  public override IGH_Goo Duplicate() => throw new NotImplementedException();

  public override string ToString() => $"{Path} - {Value}";

  public string Path { get; set; }

  public override bool IsValid => true;
  public override string TypeName => "Speckle property wrapper";
  public override string TypeDescription => "Speckle property wrapper";

  public SpecklePropertyGoo() { }

  public SpecklePropertyGoo(object value)
  {
    SpecklePropertyGoo goo = new();
    if (goo.CastFrom(value))
    {
      Value = goo;
    }
    else
    {
      //TODO: throw
    }
  }

  public override bool CastFrom(object source)
  {
    switch (source)
    {
      case SpecklePropertyGoo speckleProperty:
        Value = speckleProperty.Value;
        Path = speckleProperty.Path;
        return true;
      case double d:
        Value = new SpecklePropertyGoo() { Value = d, Path = string.Empty };
        return true;
      case int i:
        Value = new SpecklePropertyGoo() { Value = i, Path = string.Empty };
        return true;
      case string s:
        Value = new SpecklePropertyGoo() { Value = s, Path = string.Empty };
        return true;
      case bool b:
        Value = new SpecklePropertyGoo() { Value = b, Path = string.Empty };
        return true;
      case KeyValuePair<string, object?> kvp:
        Value = new SpecklePropertyGoo() { Value = kvp.Value ?? "", Path = kvp.Key };
        return true;
      case KeyValuePair<string, string> kvp:
        Value = new SpecklePropertyGoo() { Value = kvp.Value, Path = kvp.Key };
        return true;
      case GH_String ghS:
        Value = new SpecklePropertyGoo() { Value = ghS.Value, Path = string.Empty };
        return true;
      case GH_Text t:
        Value = new SpecklePropertyGoo() { Value = t.Text, Path = string.Empty };
        return true;
      case GH_Number n:
        Value = new SpecklePropertyGoo() { Value = n.Value, Path = string.Empty };
        return true;
      case GH_Integer ghI:
        Value = new SpecklePropertyGoo() { Value = ghI.Value, Path = string.Empty };
        return true;
      case GH_Boolean ghB:
        Value = new SpecklePropertyGoo() { Value = ghB.Value, Path = string.Empty };
        return true;
    }

    return false;
  }

  public override bool CastTo<T>(ref T target)
  {
    var type = typeof(T);

    if (
      type.IsAssignableFrom(typeof(int))
      || type.IsAssignableFrom(typeof(double))
      || type.IsAssignableFrom(typeof(bool))
      || type.IsAssignableFrom(typeof(string))
    )
    {
      object? ptr = Value;
      target = (T)ptr;
      return true;
    }

    if (type.IsAssignableFrom(typeof(GH_Integer)))
    {
      object ptr = new GH_Integer((int)Value);
      target = (T)ptr;
      return true;
    }

    if (type.IsAssignableFrom(typeof(GH_Number)))
    {
      object ptr = new GH_Number((double)Value);
      target = (T)ptr;
      return true;
    }

    if (type.IsAssignableFrom(typeof(GH_Boolean)))
    {
      object ptr = new GH_Boolean((bool)Value);
      target = (T)ptr;
      return true;
    }

    if (type.IsAssignableFrom(typeof(GH_String)))
    {
      object ptr = new GH_String((string)Value);
      target = (T)ptr;
      return true;
    }

    return false;
  }
}

[Guid("B3101D12-DA73-45DF-B617-16E1C65BB37C")]
public class SpecklePropertyParam : GH_Param<SpecklePropertyGoo>
{
  public SpecklePropertyParam(GH_ParamAccess access)
    : base(
      "Speckle Property",
      "SPO",
      "Represents a Speckle Property",
      ComponentCategories.PRIMARY_RIBBON,
      ComponentCategories.PARAMETERS,
      access
    ) { }

  public override Guid ComponentGuid => GetType().GUID;

  protected override Bitmap Icon => BitmapBuilder.CreateHexagonalBitmap("P");

  public SpecklePropertyParam()
    : this(GH_ParamAccess.item) { }

  public SpecklePropertyParam(IGH_InstanceDescription tag)
    : base(tag) { }

  public SpecklePropertyParam(IGH_InstanceDescription tag, GH_ParamAccess access)
    : base(tag, access) { }
}
