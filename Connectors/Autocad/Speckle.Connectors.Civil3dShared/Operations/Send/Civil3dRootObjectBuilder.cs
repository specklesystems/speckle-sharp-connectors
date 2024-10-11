using Autodesk.AutoCAD.DatabaseServices;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.Autocad.HostApp;
using Speckle.Connectors.Autocad.Operations.Send;
using Speckle.Connectors.Common.Caching;
using Speckle.Connectors.Common.Operations;
using Speckle.Converters.Civil3dShared.Helpers;
using Speckle.Converters.Civil3dShared.ToSpeckle;
using Speckle.Converters.Common;
using Speckle.Sdk.Logging;
using Speckle.Sdk.Models.Collections;

namespace Speckle.Connectors.Civil3dShared.Operations.Send;

public sealed class Civil3dRootObjectBuilder : AutocadRootObjectBaseBuilder
{
  private readonly AutocadLayerUnpacker _layerUnpacker;
  private readonly PropertySetDefinitionHandler _propertySetDefinitionHandler;
  private readonly CatchmentGroupHandler _catchmentGroupHandler;
  private readonly PipeNetworkHandler _pipeNetworkHandler;

  public Civil3dRootObjectBuilder(
    AutocadLayerUnpacker layerUnpacker,
    PropertySetDefinitionHandler propertySetDefinitionHandler,
    CatchmentGroupHandler catchmentGroupHandler,
    PipeNetworkHandler pipeNetworkHandler,
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
    _propertySetDefinitionHandler = propertySetDefinitionHandler;
    _catchmentGroupHandler = catchmentGroupHandler;
    _pipeNetworkHandler = pipeNetworkHandler;
  }

  public override (Collection, LayerTableRecord?) CreateObjectCollection(Entity entity, Transaction tr)
  {
    Layer layer = _layerUnpacker.GetOrCreateSpeckleLayer(entity, tr, out LayerTableRecord? autocadLayer);

    return (layer, autocadLayer);
  }

  // POC: probably will need to add Network proxies as well
  public override void AddAdditionalProxiesToRoot(Collection rootObject)
  {
    rootObject[ProxyKeys.PROPERTYSET_DEFINITIONS] = _propertySetDefinitionHandler.Definitions;
    rootObject["catchmentGroupProxies"] = _catchmentGroupHandler.CatchmentGroupProxies.Values.ToList();
    rootObject["pipeNetworkProxies"] = _pipeNetworkHandler.PipeNetworkProxies.Values.ToList();
  }
}
