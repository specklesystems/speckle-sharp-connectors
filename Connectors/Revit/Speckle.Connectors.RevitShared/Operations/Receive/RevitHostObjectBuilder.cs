using Autodesk.Revit.DB;
using Speckle.Connectors.Utils.Builders;
using Speckle.Connectors.Utils.Conversion;
using Speckle.Converters.Common;
using Speckle.Core.Logging;
using Speckle.Core.Models;
using Speckle.Core.Models.Collections;
using Speckle.Core.Models.GraphTraversal;

namespace Speckle.Connectors.Revit.Operations.Receive;

/// <summary>
/// Potentially consolidate all application specific IHostObjectBuilders
/// https://spockle.atlassian.net/browse/DUI3-465
/// </summary>
internal sealed class RevitHostObjectBuilder : IHostObjectBuilder
{
  private readonly IRootToHostConverter _converter;
  private readonly GraphTraversal _traverseFunction;
  private readonly ITransactionManager _transactionManager;

  public RevitHostObjectBuilder(
    IRootToHostConverter converter,
    GraphTraversal traverseFunction,
    ITransactionManager transactionManager
  )
  {
    _converter = converter;
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
    var objectsToConvert = _traverseFunction
      .TraverseWithProgress(rootObject, onOperationProgressed, cancellationToken)
      .Where(obj => obj.Current is not Collection);

    using var transactionGroup = _transactionManager.StartTransactionGroup(projectName);
    var conversionResults = BakeObjects(objectsToConvert);
    return conversionResults;
  }

  // POC: Potentially refactor out into an IObjectBaker.
  private HostObjectBuilderResult BakeObjects(IEnumerable<TraversalContext> objectsGraph)
  {
    var conversionResults = new List<ReceiveConversionResult>();
    var bakedObjectIds = new List<string>();

    foreach (TraversalContext tc in objectsGraph)
    {
      try
      {
        using var transaction = _transactionManager.StartTransaction();
        if (_converter.Convert(tc.Current) is Element element)
        {
          bakedObjectIds.Add(element.Id.ToString());
        }
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        conversionResults.Add(new(Status.ERROR, tc.Current, null, null, ex));
      }
    }

    return new(bakedObjectIds, conversionResults);
  }
}
