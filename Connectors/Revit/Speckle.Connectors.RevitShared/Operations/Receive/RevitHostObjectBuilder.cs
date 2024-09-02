using Autodesk.Revit.DB;
using Speckle.Connectors.Utils.Builders;
using Speckle.Connectors.Utils.Conversion;
using Speckle.Connectors.Utils.Operations;
using Speckle.Converters.Common;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Converters.RevitShared.Settings;
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
  private readonly ISettingsStore<RevitConversionSettings> _settings;
  private readonly GraphTraversal _traverseFunction;
  private readonly ITransactionManager _transactionManager;
  private readonly ISyncToThread _syncToThread;

  public RevitHostObjectBuilder(
    IRootToHostConverter converter,
    ISettingsStore<RevitConversionSettings> settings,
    GraphTraversal traverseFunction,
    ITransactionManager transactionManager,
    ISyncToThread syncToThread
  )
  {
    _converter = converter;
    _settings = settings;
    _traverseFunction = traverseFunction;
    _transactionManager = transactionManager;
    _syncToThread = syncToThread;
  }

  public Task<HostObjectBuilderResult> Build(
    Base rootObject,
    string projectName,
    string modelName,
    Action<string, double?>? onOperationProgressed,
    CancellationToken cancellationToken
  ) =>
    _syncToThread.RunOnThread(() =>
    {
      using var activity = SpeckleActivityFactory.Start("Build");
      IEnumerable<TraversalContext> objectsToConvert;
      using (var _ = SpeckleActivityFactory.Start("Traverse"))
      {
        objectsToConvert = _traverseFunction.Traverse(rootObject).Where(obj => obj.Current is not Collection);
      }

      using TransactionGroup transactionGroup = new(_settings.Current.Document, $"Received data from {projectName}");
      transactionGroup.Start();
      _transactionManager.StartTransaction();

      var conversionResults = BakeObjects(objectsToConvert);

      using (var _ = SpeckleActivityFactory.Start("Commit"))
      {
        _transactionManager.CommitTransaction();
        transactionGroup.Assimilate();
      }
      return conversionResults;
    });

  // POC: Potentially refactor out into an IObjectBaker.
  private HostObjectBuilderResult BakeObjects(IEnumerable<TraversalContext> objectsGraph)
  {
    using (var _ = SpeckleActivityFactory.Start("BakeObjects"))
    {
      var conversionResults = new List<ReceiveConversionResult>();

      // NOTE!!!! Add 'UniqueId' of the elements once we have receiving in place, otherwise highlight logic will fail.
      var bakedObjectIds = new List<string>();

      foreach (TraversalContext tc in objectsGraph)
      {
        try
        {
          using var activity = SpeckleActivityFactory.Start("BakeObject");
          var result = _converter.Convert(tc.Current);
        }
        catch (Exception ex) when (!ex.IsFatal())
        {
          conversionResults.Add(new(Status.ERROR, tc.Current, null, null, ex));
        }
      }

      return new(bakedObjectIds, conversionResults);
    }
  }

  public void Dispose()
  {
    _transactionManager?.Dispose();
  }
}
