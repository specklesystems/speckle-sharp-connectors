using Autodesk.Revit.DB;
using Microsoft.Extensions.Logging;
using Revit.Async;
using Speckle.Connectors.Revit.HostApp;
using Speckle.Connectors.Utils.Builders;
using Speckle.Connectors.Utils.Conversion;
using Speckle.Connectors.Utils.Operations;
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
  private readonly ISyncToThread _syncToThread;
  private readonly RevitGroupManager _groupManager;
  private readonly ILogger<RevitHostObjectBuilder> _logger;

  public RevitHostObjectBuilder(
    IRootToHostConverter converter,
    IRevitConversionContextStack contextStack,
    GraphTraversal traverseFunction,
    ITransactionManager transactionManager,
    ISyncToThread syncToThread,
    RevitGroupManager groupManager,
    ILogger<RevitHostObjectBuilder> logger
  )
  {
    _converter = converter;
    _contextStack = contextStack;
    _traverseFunction = traverseFunction;
    _transactionManager = transactionManager;
    _syncToThread = syncToThread;
    _groupManager = groupManager;
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
#pragma warning disable IDE0060
    Action<string, double?>? onOperationProgressed,
    CancellationToken cancellationToken
  )
#pragma warning restore IDE0060
  {
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

    Dictionary<string, List<string>> applicationIdMap = new();

    // TODO: progress reporting
    // TODO: cancellation
    var conversionResults = BakeObjects(objectsToConvert, out elementIds);

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

    return conversionResults;
  }

  private HostObjectBuilderResult BakeObjects(IEnumerable<TraversalContext> objectsGraph, out List<ElementId> elemIds)
  {
    using (var _ = SpeckleActivityFactory.Start("BakeObjects"))
    {
      var conversionResults = new List<ReceiveConversionResult>();
      var bakedObjectIds = new List<string>();
      var elementIds = new List<ElementId>();

      foreach (TraversalContext tc in objectsGraph)
      {
        try
        {
          using var activity = SpeckleActivityFactory.Start("BakeObject");
          var result = _converter.Convert(tc.Current);

          // Note: our current converter always returns a DS for now
          if (result is DirectShape ds)
          {
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
