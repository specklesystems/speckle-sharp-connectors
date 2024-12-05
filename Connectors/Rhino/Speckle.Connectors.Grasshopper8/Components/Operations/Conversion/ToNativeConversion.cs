using Grasshopper.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Rhino;
using Rhino.Geometry;
using Speckle.Connectors.Grasshopper8.Components.BaseComponents;
using Speckle.Connectors.Grasshopper8.Parameters;
using Speckle.Converters.Common;
using Speckle.Converters.Rhino;
using Speckle.DoubleNumerics;
using Speckle.Sdk;
using Speckle.Sdk.Common;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Instances;

namespace Speckle.Connectors.Grasshopper8.Components.Operations.Conversion;

public static class RhinoUnitsExtension
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
}

public class ToNativeConversion()
  : SpeckleScopedTaskCapableComponent<Base, List<GeometryBase>>(
    "ToNativeConversion",
    "STN",
    "Converts a speckle object to rhino",
    "Speckle",
    "Conversion"
  )
{
  public override Guid ComponentGuid => new Guid("38BAB10C-4D80-4E0C-8235-A87C3E66F55F");

  protected override void RegisterInputParams(GH_InputParamManager pManager)
  {
    pManager.AddParameter(new SpeckleObjectParam(GH_ParamAccess.item));
  }

  protected override void RegisterOutputParams(GH_OutputParamManager pManager)
  {
    pManager.AddGeometryParameter("Geometry", "Geometry", "Geometry", GH_ParamAccess.list);
  }

  protected override Base GetInput(IGH_DataAccess da)
  {
    Base? input = null;
    if (!da.GetData(0, ref input) || input is null)
    {
      throw new SpeckleException("Input is not valid");
    }

    return input;
  }

  protected override void SetOutput(IGH_DataAccess da, List<GeometryBase> result)
  {
    da.SetDataList(0, result);
  }

  protected override Task<List<GeometryBase>> PerformScopedTask(
    Base input,
    IServiceScope scope,
    CancellationToken cancellationToken = default
  )
  {
    var rhinoConversionSettingsFactory = scope.ServiceProvider.GetRequiredService<IRhinoConversionSettingsFactory>();

    scope
      .ServiceProvider.GetRequiredService<IConverterSettingsStore<RhinoConversionSettings>>()
      .Initialize(rhinoConversionSettingsFactory.Create(RhinoDoc.ActiveDoc));

    var rootConverter = scope.ServiceProvider.GetRequiredService<IRootToHostConverter>();

    if (input is InstanceProxy proxy)
    {
      var geometries = proxy["__geometry"] as List<Base>;
      var converted = geometries.SelectMany(g => Convert(g, rootConverter)).ToList();
      var transform = MatrixToTransform(proxy.transform, proxy.units);
      converted.ForEach(c => c.Transform(transform));
      return Task.FromResult(converted);
    }

    return Task.FromResult(Convert(input, rootConverter));
  }

  private List<GeometryBase> Convert(Base input, IRootToHostConverter rootConverter)
  {
    var result = rootConverter.Convert(input);

    if (result is GeometryBase geometry)
    {
      return new List<GeometryBase> { geometry };
    }
    else if (result is List<GeometryBase> geometryList)
    {
      return geometryList;
    }

    throw new SpeckleException("Failed to convert input to rhino");
  }

  private Transform MatrixToTransform(Matrix4x4 matrix, string units)
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
}
