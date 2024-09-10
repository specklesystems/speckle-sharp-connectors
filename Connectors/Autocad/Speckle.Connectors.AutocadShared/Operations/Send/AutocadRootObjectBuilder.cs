using System.Diagnostics.CodeAnalysis;
using Autodesk.AutoCAD.DatabaseServices;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.Autocad.HostApp;
using Speckle.Connectors.Utils.Builders;
using Speckle.Connectors.Utils.Caching;
using Speckle.Connectors.Utils.Conversion;
using Speckle.Connectors.Utils.Extensions;
using Speckle.Connectors.Utils.Operations;
using Speckle.Converters.Common;
using Speckle.Sdk;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;
using Speckle.Sdk.Models.Instances;

namespace Speckle.Connectors.Autocad.Operations.Send;

public class AutocadRootObjectBuilder : IRootObjectBuilder<AutocadRootObject>
{
  private readonly IRootToSpeckleConverter _converter;
  private readonly string[] _documentPathSeparator = ["\\"];
  private readonly ISendConversionCache _sendConversionCache;
  private readonly AutocadInstanceObjectManager _instanceObjectsManager;
  private readonly AutocadMaterialManager _materialManager;
  private readonly AutocadColorManager _colorManager;
  private readonly AutocadLayerManager _layerManager;
  private readonly AutocadGroupManager _groupManager;
  private readonly ILogger<AutocadRootObjectBuilder> _logger;

  public AutocadRootObjectBuilder(
    IRootToSpeckleConverter converter,
    ISendConversionCache sendConversionCache,
    AutocadInstanceObjectManager instanceObjectManager,
    AutocadMaterialManager materialManager,
    AutocadColorManager colorManager,
    AutocadLayerManager layerManager,
    AutocadGroupManager groupManager,
    ILogger<AutocadRootObjectBuilder> logger
  )
  {
    _converter = converter;
    _sendConversionCache = sendConversionCache;
    _instanceObjectsManager = instanceObjectManager;
    _materialManager = materialManager;
    _colorManager = colorManager;
    _layerManager = layerManager;
    _groupManager = groupManager;
    _logger = logger;
  }

  [SuppressMessage(
    "Maintainability",
    "CA1506:Avoid excessive class coupling",
    Justification = """
      It is already simplified but has many different references since it is a builder. Do not know can we simplify it now.
      Later we might consider to refactor proxies from one proxy manager? but we do not know the shape of it all potential
      proxy classes yet. So I'm supressing this one now!!!
      """
  )]
  public RootObjectBuilderResult Build(
    IReadOnlyList<AutocadRootObject> objects,
    SendInfo sendInfo,
    Action<string, double?>? onOperationProgressed = null,
    CancellationToken ct = default
  )
  {
    // 0 - Init the root
    Collection root =
      new()
      {
        name = Application
          .DocumentManager.CurrentDocument.Name // POC: https://spockle.atlassian.net/browse/CNX-9319
          .Split(_documentPathSeparator, StringSplitOptions.None)
          .Reverse()
          .First()
      };

    // TODO: better handling for document and transactions!!
    Document doc = Application.DocumentManager.CurrentDocument;
    using Transaction tr = doc.Database.TransactionManager.StartTransaction();

    // 1 - Unpack the instances
    var (atomicObjects, instanceProxies, instanceDefinitionProxies) = _instanceObjectsManager.UnpackSelection(objects);
    root[ProxyKeys.INSTANCE_DEFINITION] = instanceDefinitionProxies;

    // 2 - Unpack the groups
    root[ProxyKeys.GROUP] = _groupManager.UnpackGroups(atomicObjects);

    // 3 - Convert atomic objects
    List<LayerTableRecord> usedAcadLayers = new(); // Keeps track of autocad layers used, so we can pass them on later to the material and color unpacker.
    List<SendConversionResult> results = new();
    int count = 0;
    foreach (var (entity, applicationId) in atomicObjects)
    {
      ct.ThrowIfCancellationRequested();

      // Create and add a collection for each layer if not done so already.
      Layer layer = _layerManager.GetOrCreateSpeckleLayer(entity, tr, out LayerTableRecord? autocadLayer);
      if (autocadLayer is not null)
      {
        usedAcadLayers.Add(autocadLayer);
        root.elements.Add(layer);
      }

      var result = ConvertAutocadEntity(entity, applicationId, layer, instanceProxies, sendInfo.ProjectId);
      results.Add(result);

      onOperationProgressed?.Invoke("Converting", (double)++count / atomicObjects.Count);
    }

    if (results.All(x => x.Status == Status.ERROR))
    {
      throw new SpeckleConversionException("Failed to convert all objects."); // fail fast instead creating empty commit! It will appear as model card error with red color.
    }

    // TODO: Check with Dim! I believe this part is not needed anymore since it is fixed on viewer side, but still TBD this is a valid case or not to remove failed objects from definition objects.
    // var conversionFailedAppIds = results
    //   .FindAll(result => result.Status == Status.ERROR)
    //   .Select(result => result.SourceId);
    //
    // // Cleans up objects that failed to convert from definition proxies.
    // // see https://linear.app/speckle/issue/CNX-115/viewer-handle-gracefully-instances-with-elements-that-failed-to
    // foreach (var definitionProxy in instanceDefinitionProxies)
    // {
    //   definitionProxy.objects.RemoveAll(id => conversionFailedAppIds.Contains(id));
    // }

    // 4 - Unpack the render material proxies
    root[ProxyKeys.RENDER_MATERIAL] = _materialManager.UnpackMaterials(atomicObjects, usedAcadLayers);

    // 5 - Unpack the color proxies
    root[ProxyKeys.COLOR] = _colorManager.UnpackColors(atomicObjects, usedAcadLayers);

    return new RootObjectBuilderResult(root, results);
  }

  private SendConversionResult ConvertAutocadEntity(
    Entity entity,
    string applicationId,
    Collection collectionHost,
    IReadOnlyDictionary<string, InstanceProxy> instanceProxies,
    string projectId
  )
  {
    string sourceType = entity.GetType().ToString();
    try
    {
      Base converted;
      if (entity is BlockReference && instanceProxies.TryGetValue(applicationId, out InstanceProxy? instanceProxy))
      {
        converted = instanceProxy;
      }
      else if (_sendConversionCache.TryGetValue(projectId, applicationId, out ObjectReference? value))
      {
        converted = value;
      }
      else
      {
        converted = _converter.Convert(entity);
        converted.applicationId = applicationId;
      }

      collectionHost.elements.Add(converted);
      return new(Status.SUCCESS, applicationId, sourceType, converted);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogSendConversionError(ex, sourceType);
      return new(Status.ERROR, applicationId, sourceType, null, ex);
    }
  }
}
