using Grasshopper.Documentation;
using Grasshopper.Kernel.Types;
using Speckle.Connectors.GrasshopperShared.HostApp;
using Speckle.Sdk;
using Speckle.Sdk.Models;

namespace Speckle.Connectors.GrasshopperShared.Parameters;

public class SpecklePropertyGoo : GH_Goo<object>, ISpecklePropertyGoo
{
  public override IGH_Goo Duplicate() => throw new NotImplementedException();

  public override string ToString() => $"{Value}";

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
      // todo: throw
    }
  }

  public override bool CastFrom(object? source)
  {
    switch (source)
    {
      case List<object?> list:
        List<object?> castedItems = new();
        foreach (var item in list)
        {
          SpecklePropertyGoo itemGoo = new();
          if (itemGoo.CastFrom(item))
          {
            castedItems.Add(itemGoo.Value);
          }
          else
          {
            return false;
          }
        }
        Value = castedItems;
        return true;
      case SpecklePropertyGoo speckleProperty:
        Value = speckleProperty.Value;
        return true;
      case Base @base: // this would capture cases of planes, vectors, and intervals from GH
        try
        {
          Value = SpeckleConversionContext.Current.ConvertToHost(@base!).First().Item1;
          return true;
        }
        catch (SpeckleException)
        {
          return false;
        }
      case GH_Plane plane:
        Value = plane.Value;
        return true;
      case GH_Vector vector:
        Value = vector.Value;
        return true;
      case GH_Interval interval:
        Value = interval.Value;
        return true;
      case double d:
        Value = d;
        return true;
      case int i:
        Value = i;
        return true;
      case long l:
        Value = l;
        return true;
      case string s:
        Value = s;
        return true;
      case bool b:
        Value = b;
        return true;
      case KeyValuePair<string, object?> kvp:
        Value = kvp.Value ?? "";
        return true;
      case KeyValuePair<string, string> kvp:
        Value = kvp.Value;
        return true;
      case GH_String ghS:
        Value = ghS.Value;
        return true;
      case GH_Text t:
        Value = t.Text;
        return true;
      case GH_Number n:
        Value = n.Value;
        return true;
      case GH_Integer ghI:
        Value = ghI.Value;
        return true;
      case GH_Boolean ghB:
        Value = ghB.Value;
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

    if (type.IsAssignableFrom(typeof(GH_Plane)))
    {
      object ptr = new GH_Plane((Rhino.Geometry.Plane)Value);
      target = (T)ptr;
      return true;
    }

    if (type.IsAssignableFrom(typeof(GH_Vector)))
    {
      object ptr = new GH_Vector((Rhino.Geometry.Vector3d)Value);
      target = (T)ptr;
      return true;
    }

    if (type.IsAssignableFrom(typeof(GH_Interval)))
    {
      object ptr = new GH_Interval((Rhino.Geometry.Interval)Value);
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

  public bool Equals(ISpecklePropertyGoo other)
  {
    if (other is not SpecklePropertyGoo prop)
    {
      return false;
    }

    switch (Value)
    {
      case Rhino.Geometry.Plane plane:
        return prop.Value is Rhino.Geometry.Plane otherPlane && plane.Equals(otherPlane);
      case Rhino.Geometry.Vector3d vector:
        return prop.Value is Rhino.Geometry.Vector3d otherVector && vector.Equals(otherVector);
      case Rhino.Geometry.Interval interval:
        return prop.Value is Rhino.Geometry.Interval otherInterval && interval.Equals(otherInterval);
      case string s:
        return s == prop.Value.ToString();
      case bool b:
        return prop.Value is bool otherBool
          ? b == otherBool
          : bool.TryParse(prop.Value.ToString(), out bool parsedBool) && b == parsedBool;
      case double d:
        return prop.Value is double otherDouble
          ? d == otherDouble
          : double.TryParse(prop.Value.ToString(), out double parsedDouble) && d == parsedDouble;
      case float f:
        return prop.Value is float otherFloat
          ? f == otherFloat
          : float.TryParse(prop.Value.ToString(), out float parsedFloat) && f == parsedFloat;
      case int i:
        return prop.Value is int otherInt
          ? i == otherInt
          : int.TryParse(prop.Value.ToString(), out int parsedInt) && i == parsedInt;
      default:
        return Value == prop.Value;
    }
  }
}
