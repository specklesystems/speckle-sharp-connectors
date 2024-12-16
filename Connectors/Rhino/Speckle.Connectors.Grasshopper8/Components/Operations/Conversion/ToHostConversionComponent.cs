using Grasshopper.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Rhino;
using Rhino.Geometry;
using Speckle.Connectors.Grasshopper8.Components.BaseComponents;
using Speckle.Connectors.Grasshopper8.Parameters;
using Speckle.Converters.Common;
using Speckle.Converters.Grasshopper;
using Speckle.Sdk;
using Speckle.Sdk.Models;

namespace Speckle.Connectors.Grasshopper8.Components.Operations.Conversion;

public class ToHostConversionComponent()
  : SpeckleScopedTaskCapableComponent<Base, List<GeometryBase>>(
    "To Host Conversion",
    "THC",
    "Converts a speckle object to rhino",
    "Speckle",
    "Dev"
  )
{
  public override Guid ComponentGuid => new("38BAB10C-4D80-4E0C-8235-A87C3E66F55F");

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
    var grasshopperConversionSettingsFactory =
      scope.ServiceProvider.GetRequiredService<IGrasshopperConversionSettingsFactory>();

    scope
      .ServiceProvider.GetRequiredService<IConverterSettingsStore<GrasshopperConversionSettings>>()
      .Initialize(grasshopperConversionSettingsFactory.Create(RhinoDoc.ActiveDoc));

    var rootConverter = scope.ServiceProvider.GetRequiredService<IRootToHostConverter>();

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
}
