using Autodesk.Revit.DB;
using Microsoft.Extensions.Logging;
using Revit.Async;
using Speckle.Connectors.Revit.HostApp;
using Speckle.Connectors.Utils.Builders;
using Speckle.Connectors.Utils.Conversion;
using Speckle.Connectors.Utils.Operations.Receive;
using Speckle.Converters.Common;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Sdk;
using Speckle.Sdk.Logging;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;
using Speckle.Sdk.Models.GraphTraversal;

namespace Speckle.Connectors.Revit.Operations.Receive;

/// <summary>
/// Potentially consolidate all application specific IHostObjectBuilders
/// https://spockle.atlassian.net/browse/DUI3-465
/// </summary>
internal sealed class RevitHostObjectBuilder : IHostObjectBuilder, IDisposable
{
  private readonly IRootToHostConverter _converter;
  private readonly IRevitConversionContextStack _contextStack;
  private readonly GraphTraversal _traverseFunction;
  private readonly ITransactionManager _transactionManager;
  private readonly RevitGroupBaker _groupManager;
  private readonly RevitMaterialBaker _materialBaker;
  private readonly ILogger<RevitHostObjectBuilder> _logger;

  private readonly RootObjectUnpacker _rootObjectUnpacker;

  public RevitHostObjectBuilder(
    IRootToHostConverter converter,
    IRevitConversionContextStack contextStack,
    GraphTraversal traverseFunction,
    ITransactionManager transactionManager,
    RevitGroupBaker groupManager,
    RevitMaterialBaker materialBaker,
    RootObjectUnpacker rootObjectUnpacker,
    ILogger<RevitHostObjectBuilder> logger
  )
  {
    _converter = converter;
    _contextStack = contextStack;
    _traverseFunction = traverseFunction;
    _transactionManager = transactionManager;
    _groupManager = groupManager;
    _materialBaker = materialBaker;
    _rootObjectUnpacker = rootObjectUnpacker;
    _logger = logger;
  }

  public Task<HostObjectBuilderResult> Build(
    Base rootObject,
    string projectName,
    string modelName,
    Action<string, double?>? onOperationProgressed,
    CancellationToken cancellationToken
  ) =>
    RevitTask.RunAsync(() => BuildSync(rootObject, projectName, modelName, onOperationProgressed, cancellationToken));

  private HostObjectBuilderResult BuildSync(
    Base rootObject,
    string projectName,
    string modelName,
    Action<string, double?>? onOperationProgressed,
    CancellationToken cancellationToken
  )
  {
    onOperationProgressed?.Invoke("Converting", null);

    // 1 - Unpack objects and proxies from root commit object
    var unpackedRoot = _rootObjectUnpacker.Unpack(rootObject);

    using var activity = SpeckleActivityFactory.Start("Build");
    IEnumerable<TraversalContext> objectsToConvert;

    using (var _ = SpeckleActivityFactory.Start("Traverse"))
    {
      objectsToConvert = _traverseFunction.Traverse(rootObject).Where(obj => obj.Current is not Collection);
    }

    var elementIds = new List<ElementId>();

    using TransactionGroup transactionGroup = new(_contextStack.Current.Document, $"Received data from {projectName}");
    transactionGroup.Start();
    _transactionManager.StartTransaction();

    if (unpackedRoot.RenderMaterialProxies != null)
    {
      _materialBaker.BakeMaterials(unpackedRoot.RenderMaterialProxies);
      foreach (var item in _materialBaker.ObjectIdAndMaterialIndexMap)
      {
        _contextStack.RenderMaterialProxyCache.ObjectIdAndMaterialIndexMap.Add(item.Key, item.Value); // Massive hack!
      }
    }

    // TODO: RevitMaterialBaker -> it will produce the map.
    // TODO: you need inject this dictionary to your RevitConversionContextStack

    var conversionResults = BakeObjects(objectsToConvert, onOperationProgressed, cancellationToken, out elementIds);

    using (var _ = SpeckleActivityFactory.Start("Commit"))
    {
      _transactionManager.CommitTransaction();
      transactionGroup.Assimilate();
    }

    using TransactionGroup createGroupTransaction = new(_contextStack.Current.Document, "Creating group");
    createGroupTransaction.Start();
    _transactionManager.StartTransaction(true);

    try
    {
      var baseGroupName = $"Project {projectName} - Model {modelName}";
      _groupManager.BakeGroups(baseGroupName);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogError(ex, "Failed to create group after receiving elements in Revit");
    }

    using (var _ = SpeckleActivityFactory.Start("Commit"))
    {
      _transactionManager.CommitTransaction();
      createGroupTransaction.Assimilate();
    }

    _contextStack.RenderMaterialProxyCache.ObjectIdAndMaterialIndexMap.Clear(); // Massive hack!

    return conversionResults;
  }

  private HostObjectBuilderResult BakeObjects(
    IEnumerable<TraversalContext> objectsGraph,
    Action<string, double?>? onOperationProgressed,
    CancellationToken cancellationToken,
    out List<ElementId> elemIds
  )
  {
    using (var _ = SpeckleActivityFactory.Start("BakeObjects"))
    {
      var conversionResults = new List<ReceiveConversionResult>();
      var bakedObjectIds = new List<string>();
      var elementIds = new List<ElementId>();

      // is this a dumb idea?
      var objectList = objectsGraph.ToList();
      int count = 0;

      foreach (TraversalContext tc in objectList)
      {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
          using var activity = SpeckleActivityFactory.Start("BakeObject");
          var result = _converter.Convert(tc.Current);
          onOperationProgressed?.Invoke("Converting", (double)++count / objectList.Count);

          // Note: our current converter always returns a DS for now
          if (result is DirectShape ds)
          {
            /*var newMaterialId = Material.Create(_contextStack.Current.Document, "MyNewMaterial");
            var revitMaterial = (Material)_contextStack.Current.Document.GetElement(newMaterialId);
            revitMaterial.Color = new Color(0, 255, 255);
            revitMaterial.Transparency = 100;
            revitMaterial.Shininess = 75;
            revitMaterial.Smoothness = 25;
            ds.M*/

            bakedObjectIds.Add(ds.UniqueId.ToString());
            _groupManager.AddToGroupMapping(tc, ds);
          }
          else
          {
            throw new SpeckleConversionException($"Failed to cast {result.GetType()} to Direct Shape.");
          }
          conversionResults.Add(new(Status.SUCCESS, tc.Current, ds.UniqueId, "Direct Shape"));
        }
        catch (Exception ex) when (!ex.IsFatal())
        {
          conversionResults.Add(new(Status.ERROR, tc.Current, null, null, ex));
        }
      }

      elemIds = elementIds;
      return new(bakedObjectIds, conversionResults);
    }
  }

  public void Dispose()
  {
    _transactionManager?.Dispose();
  }
}
