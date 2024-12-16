using Grasshopper.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Rhino;
using Rhino.Geometry;
using Speckle.Connectors.Grasshopper8.Components.BaseComponents;
using Speckle.Connectors.Grasshopper8.Parameters;
using Speckle.Converters.Common;
using Speckle.Converters.Grasshopper;
using Speckle.Sdk.Common;
using Speckle.Sdk.Models;

namespace Speckle.Connectors.Grasshopper8.Components.Operations.Conversion;

public class ToSpeckleConversionComponent()
  : SpeckleScopedTaskCapableComponent<GeometryBase, Base>(
    "To Speckle Conversion",
    "TSC",
    "To Speckle Conversion",
    "Speckle",
    "Dev"
  )
{
  public override Guid ComponentGuid => new("ED3EC26D-681D-4E45-8FD8-DC4846F82B12");

  protected override void RegisterInputParams(GH_InputParamManager pManager)
  {
    pManager.AddGeometryParameter("Geometry", "Geometry", "Geometry to convert.", GH_ParamAccess.item);
  }

  protected override void RegisterOutputParams(GH_OutputParamManager pManager)
  {
    pManager.AddParameter(new SpeckleObjectParam());
  }

  protected override GeometryBase GetInput(IGH_DataAccess da)
  {
    GeometryBase? geom = null;
    da.GetData(0, ref geom);
    return geom.NotNull();
  }

  protected override void SetOutput(IGH_DataAccess da, Base result)
  {
    da.SetData(0, result);
  }

  protected override Task<Base> PerformScopedTask(
    GeometryBase input,
    IServiceScope scope,
    CancellationToken cancellationToken = default
  )
  {
    var grasshopperConversionSettingsFactory =
      scope.ServiceProvider.GetRequiredService<IGrasshopperConversionSettingsFactory>();

    scope
      .ServiceProvider.GetRequiredService<IConverterSettingsStore<GrasshopperConversionSettings>>()
      .Initialize(grasshopperConversionSettingsFactory.Create(RhinoDoc.ActiveDoc));

    var rootConverter = scope.ServiceProvider.GetRequiredService<IRootToSpeckleConverter>();

    return Task.FromResult(rootConverter.Convert(input));
  }
}
