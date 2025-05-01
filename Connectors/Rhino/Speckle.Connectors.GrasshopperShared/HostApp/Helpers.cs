using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.Geometry;
using Speckle.Connectors.GrasshopperShared.Parameters;
using Speckle.DoubleNumerics;
using Speckle.Sdk;
using Speckle.Sdk.Common;
using Speckle.Sdk.Common.Exceptions;

namespace Speckle.Connectors.GrasshopperShared.HostApp;

public static class GrasshopperHelpers
{
  public static string ToSpeckleString(this UnitSystem unitSystem)
  {
    switch (unitSystem)
    {
      case UnitSystem.None:
        return Units.Meters;
      case UnitSystem.Millimeters:
        return Units.Millimeters;
      case UnitSystem.Centimeters:
        return Units.Centimeters;
      case UnitSystem.Meters:
        return Units.Meters;
      case UnitSystem.Kilometers:
        return Units.Kilometers;
      case UnitSystem.Inches:
        return Units.Inches;
      case UnitSystem.Feet:
        return Units.Feet;
      case UnitSystem.Yards:
        return Units.Yards;
      case UnitSystem.Miles:
        return Units.Miles;
      case UnitSystem.Unset:
        return Units.Meters;
      default:
        throw new UnitNotSupportedException($"The Unit System \"{unitSystem}\" is unsupported.");
    }
  }

  /// <summary>
  /// Retrieves a unique Speckle application id from the rgb value.
  /// </summary>
  /// <remarks> Uses the rgb value since color names are not unique </remarks>
  public static string GetSpeckleApplicationId(this Color color) => $"color_{color}";

  /// <summary>
  /// Creates a unique Speckle application id from the display material properties.
  /// </summary>
  /// <param name="mat"></param>
  /// <returns></returns>
  public static string GetSpeckleApplicationId(this Rhino.Display.DisplayMaterial mat) =>
    $"material_{mat.Transparency}_{mat.Diffuse}_{mat.Emission}_{mat.Shine}_{mat.Specular}";

  public static string GetSpeckleApplicationId(this SpeckleMaterialWrapper matWrapper) =>
    $"material_{matWrapper.Material.opacity}_{matWrapper.Material.diffuse}_{matWrapper.Material.emissive}_{matWrapper.Material.metalness}_{matWrapper.Material.roughness}";

  /// <summary>
  /// Retrieves a unique Speckle application id from the path of the collection
  /// </summary>
  /// <param name="collectionWrapper"></param>
  /// <returns></returns>
  public static string GetSpeckleApplicationId(this SpeckleCollectionWrapper collectionWrapper) =>
    $"{string.Join(Constants.LAYER_PATH_DELIMITER, collectionWrapper.Path)}";

  public static Transform MatrixToTransform(Matrix4x4 matrix, string units)
  {
    var currentDoc = RhinoDoc.ActiveDoc; // POC: too much right now to interface around
    var conversionFactor = Units.GetConversionFactor(units, currentDoc.ModelUnitSystem.ToSpeckleString());

    var t = Transform.Identity;
    t.M00 = matrix.M11;
    t.M01 = matrix.M12;
    t.M02 = matrix.M13;
    t.M03 = matrix.M14 * conversionFactor;

    t.M10 = matrix.M21;
    t.M11 = matrix.M22;
    t.M12 = matrix.M23;
    t.M13 = matrix.M24 * conversionFactor;

    t.M20 = matrix.M31;
    t.M21 = matrix.M32;
    t.M22 = matrix.M33;
    t.M23 = matrix.M34 * conversionFactor;

    t.M30 = matrix.M41;
    t.M31 = matrix.M42;
    t.M32 = matrix.M43;
    t.M33 = matrix.M44;
    return t;
  }

  /// <summary>
  /// Attempts to cast the goo to a geometry base object.
  /// </summary>
  /// <param name="geoGeo"></param>
  /// <returns></returns>
  /// <exception cref="SpeckleException">If it fails to cast</exception>
  public static GeometryBase GeometricGooToGeometryBase(this IGH_GeometricGoo geoGeo)
  {
    var value = geoGeo.GetType().GetProperty("Value")?.GetValue(geoGeo);
    switch (value)
    {
      case GeometryBase gb:
        return gb;
      case Point3d pt:
        return new Rhino.Geometry.Point(pt);
      case Line ln:
        return new LineCurve(ln);
      case Rectangle3d rec:
        return rec.ToNurbsCurve();
      case Circle c:
        return new ArcCurve(c);
      case Arc ac:
        return new ArcCurve(ac);
      case Ellipse el:
        return el.ToNurbsCurve();
      case Sphere sp:
        return sp.ToBrep();
    }

    throw new SpeckleException("Failed to cast IGH_GeometricGoo to geometry base");
  }

  /// <summary>
  /// Creates a tree based of a string that encodes the grasshopper topology.
  /// </summary>
  /// <param name="topology"></param>
  /// <param name="subset"></param>
  /// <returns></returns>
  public static DataTree<object> CreateDataTreeFromTopologyAndItems(string topology, System.Collections.IList subset)
  {
    var tree = new DataTree<object>();
    var treeTopo = topology.Split(' ');
    int subsetCount = 0;
    foreach (var branch in treeTopo)
    {
      if (!string.IsNullOrEmpty(branch))
      {
        var branchTopo = branch.Split('-')[0].Split(';');
        var branchIndexes = new List<int>();
        foreach (var t in branchTopo)
        {
          branchIndexes.Add(Convert.ToInt32(t));
        }

        var elCount = Convert.ToInt32(branch.Split('-')[1]);
        var myPath = new GH_Path(branchIndexes.ToArray());

        for (int i = 0; i < elCount; i++)
        {
          tree.EnsurePath(myPath).Add(new Grasshopper.Kernel.Types.GH_ObjectWrapper(subset[subsetCount + i]));
        }

        subsetCount += elCount;
      }
    }

    return tree;
  }

  /// <summary>
  /// Encodes a tree topology into an exhaustive string which can be used to recreate it using
  /// <see cref="CreateDataTreeFromTopologyAndItems"/>.
  /// </summary>
  /// <param name="param"></param>
  /// <returns></returns>
  public static string GetParamTopology(IGH_Param param)
  {
    string topology = "";
    foreach (GH_Path myPath in param.VolatileData.Paths)
    {
      topology += myPath.ToString(false) + "-" + param.VolatileData.get_Branch(myPath).Count + " ";
    }
    return topology;
  }
}
