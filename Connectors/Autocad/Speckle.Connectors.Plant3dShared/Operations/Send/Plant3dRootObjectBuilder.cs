using Autodesk.AutoCAD.DatabaseServices;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.Autocad.HostApp;
using Speckle.Connectors.Autocad.Operations.Send;
using Speckle.Connectors.Common.Caching;
using Speckle.Converters.Autocad;
using Speckle.Converters.Common;
using Speckle.Sdk.Logging;
using Speckle.Sdk.Models.Collections;

namespace Speckle.Connectors.Plant3dShared.Operations.Send;

public sealed class Plant3dRootObjectBuilder : AutocadRootObjectBaseBuilder
{
  private readonly AutocadLayerUnpacker _layerUnpacker;

  public Plant3dRootObjectBuilder(
    AutocadLayerUnpacker layerUnpacker,
    IRootToSpeckleConverter converter,
    IConverterSettingsStore<AutocadConversionSettings> converterSettings,
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
      converterSettings,
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
}
