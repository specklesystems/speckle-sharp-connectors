using Autodesk.AutoCAD.DatabaseServices;
using Speckle.Connectors.Autocad.HostApp;
using Speckle.Connectors.Autocad.Operations.Receive;
using Speckle.Connectors.Civil3dShared.HostApp;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.Common.Operations.Receive;
using Speckle.Converters.Common;
using Speckle.Sdk.Models;

namespace Speckle.Connectors.Civil3dShared.Operations.Receive;

/// <summary>
/// <para>Civil3D-specific host object builder with property set support. Expects to be a scoped dependency per receive operation.</para>
/// </summary>
public sealed class Civil3dHostObjectBuilder : AutocadHostObjectBaseBuilder
{
  private readonly PropertySetBaker _propertySetBaker;

  public Civil3dHostObjectBuilder(
    IRootToHostConverter converter,
    AutocadLayerBaker layerBaker,
    AutocadGroupBaker groupBaker,
    AutocadInstanceBaker instanceBaker,
    IAutocadMaterialBaker materialBaker,
    IAutocadColorBaker colorBaker,
    AutocadContext autocadContext,
    RootObjectUnpacker rootObjectUnpacker,
    IReceiveConversionHandler conversionHandler,
    PropertySetBaker propertySetBaker
  )
    : base(
      converter,
      layerBaker,
      groupBaker,
      instanceBaker,
      materialBaker,
      colorBaker,
      autocadContext,
      rootObjectUnpacker,
      conversionHandler
    )
  {
    _propertySetBaker = propertySetBaker;
  }

  protected override void PostBakeEntity(Entity entity, Base originalObject, Transaction tr)
  {
    _propertySetBaker.TryBakePropertySets(entity, originalObject, tr);
  }
}
