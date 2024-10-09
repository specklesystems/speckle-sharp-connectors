using Autodesk.AutoCAD.DatabaseServices;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.Autocad.HostApp;
using Speckle.Connectors.Autocad.Operations.Send;
using Speckle.Connectors.Common.Caching;
using Speckle.Converters.Common;
using Speckle.Sdk.Logging;
using Speckle.Sdk.Models.Collections;

namespace Speckle.Connectors.Civil3dShared.Operations.Send;

public sealed class Civil3dRootObjectBuilder : AutocadRootObjectBaseBuilder
{
  private readonly AutocadLayerUnpacker _layerUnpacker;

  public Civil3dRootObjectBuilder(
    AutocadLayerUnpacker layerUnpacker,
    IRootToSpeckleConverter converter,
    ISendConversionCache sendConversionCache,
    AutocadInstanceUnpacker instanceObjectManager,
    AutocadMaterialUnpacker materialUnpacker,
    AutocadColorUnpacker colorUnpacker,
    AutocadGroupUnpacker groupUnpacker,
    ILogger<AutocadRootObjectBuilder> logger,
    ISdkActivityFactory activityFactory
  )
    : base(
      converter,
      sendConversionCache,
      instanceObjectManager,
      materialUnpacker,
      colorUnpacker,
      groupUnpacker,
      logger,
      activityFactory
    )
  {
    _layerUnpacker = layerUnpacker;
  }

  public override (Collection, LayerTableRecord?) CreateObjectCollection(Entity entity, Transaction tr)
  {
    Layer layer = _layerUnpacker.GetOrCreateSpeckleLayer(entity, tr, out LayerTableRecord? autocadLayer);

    return (layer, autocadLayer);
  }

  // POC: probably will need to add Network definition proxies as well
}
