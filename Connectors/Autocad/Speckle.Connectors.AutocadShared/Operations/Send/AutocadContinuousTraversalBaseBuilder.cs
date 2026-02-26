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
using Speckle.Sdk.Pipelines.Progress;
using Speckle.Sdk.Pipelines.Send;

namespace Speckle.Connectors.Autocad.Operations.Send;

/// <summary>
/// Abstract base class for AutoCAD continuous traversal builders that stream objects through a
/// <see cref="SendPipeline"/> for packfile-based uploads. Same conversion logic as
/// <see cref="AutocadRootObjectBaseBuilder"/>, but processes elements through the pipeline.
/// </summary>
public abstract class AutocadContinuousTraversalBaseBuilder : IRootContinuousTraversalBuilder<AutocadRootObject>
{
  private readonly IRootToSpeckleConverter _converter;
  private readonly string[] _documentPathSeparator = ["\\"];
  private readonly ISendConversionCache _sendConversionCache;
  private readonly AutocadInstanceUnpacker _instanceUnpacker;
  private readonly AutocadMaterialUnpacker _materialUnpacker;
  private readonly AutocadColorUnpacker _colorUnpacker;
  private readonly AutocadGroupUnpacker _groupUnpacker;
  private readonly ILogger<AutocadRootObjectBuilder> _logger;
  private readonly ISdkActivityFactory _activityFactory;

  protected AutocadContinuousTraversalBaseBuilder(
    IRootToSpeckleConverter converter,
    ISendConversionCache sendConversionCache,
    AutocadInstanceUnpacker instanceObjectManager,
    AutocadMaterialUnpacker materialUnpacker,
    AutocadColorUnpacker colorUnpacker,
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
    _groupUnpacker = groupUnpacker;
    _logger = logger;
    _activityFactory = activityFactory;
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
  public async Task<RootObjectBuilderResult> Build(
    IReadOnlyList<AutocadRootObject> objects,
    string projectId,
    SendPipeline sendPipeline,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken
  )
  {
    // 0 - Init the root
    Collection root =
      new()
      {
        name = Application
          .DocumentManager.CurrentDocument.Name.Split(_documentPathSeparator, StringSplitOptions.None)
          .Reverse()
          .First()
      };

    Document doc = Application.DocumentManager.CurrentDocument;
    using Transaction tr = doc.Database.TransactionManager.StartTransaction();

    // 1 - Unpack the instances
    var (atomicObjects, instanceProxies, instanceDefinitionProxies) = _instanceUnpacker.UnpackSelection(objects);
    root[ProxyKeys.INSTANCE_DEFINITION] = instanceDefinitionProxies;

    // 2 - Unpack the groups
    root[ProxyKeys.GROUP] = _groupUnpacker.UnpackGroups(atomicObjects);
    using (var _ = _activityFactory.Start("Converting objects"))
    {
      // 3 - Convert atomic objects and process through pipeline
      List<LayerTableRecord> usedAcadLayers = new();
      List<SendConversionResult> results = new();
      int count = 0;
      foreach (var (entity, applicationId) in atomicObjects)
      {
        cancellationToken.ThrowIfCancellationRequested();
        (Collection objectCollection, LayerTableRecord? autocadLayer) = CreateObjectCollection(entity, tr);

        if (autocadLayer is not null)
        {
          usedAcadLayers.Add(autocadLayer);
          root.elements.Add(objectCollection);
        }

        var result = await ConvertAutocadEntity(
          entity,
          applicationId,
          objectCollection,
          instanceProxies,
          projectId,
          sendPipeline
        );
        results.Add(result);

        onOperationProgressed.Report(new("Converting", (double)++count / atomicObjects.Count));
      }

      if (results.All(x => x.Status == Status.ERROR))
      {
        throw new SpeckleException("Failed to convert all objects.");
      }

      // 4 - Unpack the render material proxies
      root[ProxyKeys.RENDER_MATERIAL] = _materialUnpacker.UnpackMaterials(atomicObjects, usedAcadLayers);

      // 5 - Unpack the color proxies
      root[ProxyKeys.COLOR] = _colorUnpacker.UnpackColors(atomicObjects, usedAcadLayers);

      // add any additional properties (most likely from verticals)
      AddAdditionalProxiesToRoot(root);

      // Process root collection and wait for all uploads
      await sendPipeline.Process(root);
      await sendPipeline.WaitForUpload();

      return new RootObjectBuilderResult(root, results);
    }
  }

  public virtual (Collection, LayerTableRecord?) CreateObjectCollection(Entity entity, Transaction tr)
  {
    return (new(), null);
  }

  public virtual void AddAdditionalProxiesToRoot(Collection rootCollection)
  {
    return;
  }

  private async Task<SendConversionResult> ConvertAutocadEntity(
    Entity entity,
    string applicationId,
    Collection collectionHost,
    IReadOnlyDictionary<string, InstanceProxy> instanceProxies,
    string projectId,
    SendPipeline sendPipeline
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

      var reference = await sendPipeline.Process(converted).ConfigureAwait(false);
      collectionHost.elements.Add(reference);
      return new(Status.SUCCESS, applicationId, sourceType, reference);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogSendConversionError(ex, sourceType);
      return new(Status.ERROR, applicationId, sourceType, null, ex);
    }
  }
}
