using System.Diagnostics.CodeAnalysis;
using Autodesk.AutoCAD.DatabaseServices;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.Autocad.HostApp;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Caching;
using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.Common.Extensions;
using Speckle.Connectors.Common.Operations;
using Speckle.Converters.Common;
using Speckle.Sdk;
using Speckle.Sdk.Logging;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;
using Speckle.Sdk.Models.Instances;

namespace Speckle.Connectors.Autocad.Operations.Send;

public class AutocadRootObjectBuilder : IRootObjectBuilder<AutocadRootObject>
{
  private readonly IRootToSpeckleConverter _converter;
  private readonly string[] _documentPathSeparator = ["\\"];
  private readonly ISendConversionCache _sendConversionCache;
  private readonly AutocadInstanceUnpacker _instanceUnpacker;
  private readonly AutocadMaterialUnpacker _materialUnpacker;
  private readonly AutocadColorUnpacker _colorUnpacker;
  private readonly AutocadLayerUnpacker _layerUnpacker;
  private readonly AutocadGroupUnpacker _groupUnpacker;
  private readonly ILogger<AutocadRootObjectBuilder> _logger;
  private readonly ISdkActivityFactory _activityFactory;

  public AutocadRootObjectBuilder(
    IRootToSpeckleConverter converter,
    ISendConversionCache sendConversionCache,
    AutocadInstanceUnpacker instanceObjectManager,
    AutocadMaterialUnpacker materialUnpacker,
    AutocadColorUnpacker colorUnpacker,
    AutocadLayerUnpacker layerUnpacker,
    AutocadGroupUnpacker groupUnpacker,
    ILogger<AutocadRootObjectBuilder> logger,
    ISdkActivityFactory activityFactory
  )
  {
    _converter = converter;
    _sendConversionCache = sendConversionCache;
    _instanceUnpacker = instanceObjectManager;
    _materialUnpacker = materialUnpacker;
    _colorUnpacker = colorUnpacker;
    _layerUnpacker = layerUnpacker;
    _groupUnpacker = groupUnpacker;
    _logger = logger;
    _activityFactory = activityFactory;
  }

  public Task<RootObjectBuilderResult> Build(
    IReadOnlyList<AutocadRootObject> objects,
    SendInfo sendInfo,
    Action<string, double?>? onOperationProgressed = null,
    CancellationToken ct = default
  ) => Task.FromResult(BuildSync(objects, sendInfo, onOperationProgressed, ct));

  [SuppressMessage(
    "Maintainability",
    "CA1506:Avoid excessive class coupling",
    Justification = """
      It is already simplified but has many different references since it is a builder. Do not know can we simplify it now.
      Later we might consider to refactor proxies from one proxy manager? but we do not know the shape of it all potential
      proxy classes yet. So I'm supressing this one now!!!
      """
  )]
  private RootObjectBuilderResult BuildSync(
    IReadOnlyList<AutocadRootObject> objects,
    SendInfo sendInfo,
    Action<string, double?>? onOperationProgressed,
    CancellationToken ct
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
    var (atomicObjects, instanceProxies, instanceDefinitionProxies) = _instanceUnpacker.UnpackSelection(objects);
    root[ProxyKeys.INSTANCE_DEFINITION] = instanceDefinitionProxies;

    // 2 - Unpack the groups
    root[ProxyKeys.GROUP] = _groupUnpacker.UnpackGroups(atomicObjects);
    using (var _ = _activityFactory.Start("Converting objects"))
    {
      // 3 - Convert atomic objects
      List<LayerTableRecord> usedAcadLayers = new(); // Keeps track of autocad layers used, so we can pass them on later to the material and color unpacker.
      List<SendConversionResult> results = new();
      int count = 0;
      foreach (var (entity, applicationId) in atomicObjects)
      {
        ct.ThrowIfCancellationRequested();
        using (var convertActivity = _activityFactory.Start("Converting object"))
        {
          // Create and add a collection for each layer if not done so already.
          Layer layer = _layerUnpacker.GetOrCreateSpeckleLayer(entity, tr, out LayerTableRecord? autocadLayer);
          if (autocadLayer is not null)
          {
            usedAcadLayers.Add(autocadLayer);
            root.elements.Add(layer);
          }

          var result = ConvertAutocadEntity(entity, applicationId, layer, instanceProxies, sendInfo.ProjectId);
          results.Add(result);

          onOperationProgressed?.Invoke("Converting", (double)++count / atomicObjects.Count);
        }
      }

      if (results.All(x => x.Status == Status.ERROR))
      {
        throw new SpeckleConversionException("Failed to convert all objects."); // fail fast instead creating empty commit! It will appear as model card error with red color.
      }

      // 4 - Unpack the render material proxies
      root[ProxyKeys.RENDER_MATERIAL] = _materialUnpacker.UnpackMaterials(atomicObjects, usedAcadLayers);

      // 5 - Unpack the color proxies
      root[ProxyKeys.COLOR] = _colorUnpacker.UnpackColors(atomicObjects, usedAcadLayers);

      return new RootObjectBuilderResult(root, results);
    }
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
