using System.Reflection;
using Grasshopper.Kernel.Types;
using Speckle.Sdk;

namespace Speckle.Connectors.Grasshopper8.HostApp;

public static class GrasshopperGooExtensions
{
  public static T UnwrapGoo<T>(this IGH_Goo goo)
  {
    if (goo is GH_Goo<T> specificGoo)
    {
      return specificGoo.Value;
    }

    var valuePropInfo = goo.GetType()
      .GetField("m_value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    if (valuePropInfo != null)
    {
      var tempValue = valuePropInfo.GetValue(goo);
      if (tempValue is T value)
      {
        return value;
      }
    }
    // TODO: Potentially unwrap new rhino objects

    throw new SpeckleException(
      $"Internal value of goo {goo.GetType().Name} was not the provided type {typeof(T).Name}"
    );
  }
}
