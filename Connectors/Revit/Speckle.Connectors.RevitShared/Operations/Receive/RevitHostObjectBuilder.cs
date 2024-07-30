using Autodesk.Revit.DB;
using Speckle.Connectors.Utils.Builders;
using Speckle.Connectors.Utils.Conversion;
using Speckle.Converters.Common;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Core.Logging;
using Speckle.Core.Models;
using Speckle.Core.Models.Collections;
using Speckle.Core.Models.GraphTraversal;
using Speckle.Logging;

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

  public RevitHostObjectBuilder(
    IRootToHostConverter converter,
    IRevitConversionContextStack contextStack,
    GraphTraversal traverseFunction,
    ITransactionManager transactionManager
  )
  {
    _converter = converter;
    _contextStack = contextStack;
    _traverseFunction = traverseFunction;
    _transactionManager = transactionManager;
  }

  public HostObjectBuilderResult Build(
    Base rootObject,
    string projectName,
    string modelName,
    Action<string, double?>? onOperationProgressed,
    CancellationToken cancellationToken
  )
  {
    using (var activity = SpeckleActivityFactory.Start("Build"))
    {
      IEnumerable<TraversalContext> objectsToConvert;

      using (var traverse = SpeckleActivityFactory.Start("Traverse"))
      {
        objectsToConvert = _traverseFunction
          .TraverseWithProgress(rootObject, onOperationProgressed, cancellationToken)
          .Where(obj => obj.Current is not Collection);
      }

      using TransactionGroup transactionGroup =
        new(_contextStack.Current.Document, $"Received data from {projectName}");
      transactionGroup.Start();
      _transactionManager.StartTransaction();

      var conversionResults = BakeObjects(objectsToConvert);
      using (var commit = SpeckleActivityFactory.Start("Commit"))
      {
        _transactionManager.CommitTransaction();
        transactionGroup.Assimilate();
      }

      return conversionResults;
    }
  }

  // POC: Potentially refactor out into an IObjectBaker.
  private HostObjectBuilderResult BakeObjects(IEnumerable<TraversalContext> objectsGraph)
  {
    using (var bakeObjects = SpeckleActivityFactory.Start("BakeObjects"))
    {
      var conversionResults = new List<ReceiveConversionResult>();
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
