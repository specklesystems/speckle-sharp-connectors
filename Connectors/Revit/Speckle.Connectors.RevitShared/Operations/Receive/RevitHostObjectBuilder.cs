using Autodesk.Revit.DB;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.Common.Operations;
using Speckle.Converters.Common;
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
  private readonly IConverterSettingsStore<RevitConversionSettings> _converterSettings;
  private readonly GraphTraversal _traverseFunction;
  private readonly ITransactionManager _transactionManager;
  private readonly ISyncToThread _syncToThread;
  private readonly ISdkActivityFactory _activityFactory;

  public RevitHostObjectBuilder(
    IRootToHostConverter converter,
    IConverterSettingsStore<RevitConversionSettings> converterSettings,
    GraphTraversal traverseFunction,
    ITransactionManager transactionManager,
    ISyncToThread syncToThread,
    ISdkActivityFactory activityFactory
  )
  {
    _converter = converter;
    _converterSettings = converterSettings;
    _traverseFunction = traverseFunction;
    _transactionManager = transactionManager;
    _syncToThread = syncToThread;
    _activityFactory = activityFactory;
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
      using var activity = _activityFactory.Start("Build");
      IEnumerable<TraversalContext> objectsToConvert;
      using (var _ = _activityFactory.Start("Traverse"))
      {
        objectsToConvert = _traverseFunction.Traverse(rootObject).Where(obj => obj.Current is not Collection);
      }

      using TransactionGroup transactionGroup =
        new(_converterSettings.Current.Document, $"Received data from {projectName}");
      transactionGroup.Start();
      _transactionManager.StartTransaction();

      var conversionResults = BakeObjects(objectsToConvert);

      using (var _ = _activityFactory.Start("Commit"))
      {
        _transactionManager.CommitTransaction();
        transactionGroup.Assimilate();
      }
      return conversionResults;
    });

  // POC: Potentially refactor out into an IObjectBaker.
  private HostObjectBuilderResult BakeObjects(IEnumerable<TraversalContext> objectsGraph)
  {
    using (var _ = _activityFactory.Start("BakeObjects"))
    {
      var conversionResults = new List<ReceiveConversionResult>();

      // NOTE!!!! Add 'UniqueId' of the elements once we have receiving in place, otherwise highlight logic will fail.
      var bakedObjectIds = new List<string>();

      foreach (TraversalContext tc in objectsGraph)
      {
        using var activity = _activityFactory.Start("BakeObject");
        try
        {
          var result = _converter.Convert(tc.Current);
          activity?.SetStatus(SdkActivityStatusCode.Ok);
        }
        catch (Exception ex) when (!ex.IsFatal())
        {
          conversionResults.Add(new(Status.ERROR, tc.Current, null, null, ex));
          activity?.RecordException(ex);
          activity?.SetStatus(SdkActivityStatusCode.Error);
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
