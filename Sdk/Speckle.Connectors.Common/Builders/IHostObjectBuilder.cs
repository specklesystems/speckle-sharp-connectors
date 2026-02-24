using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.Common.Operations;
using Speckle.Sdk.Models;
using Speckle.Sdk.Pipelines.Progress;

namespace Speckle.Connectors.Common.Builders;

// POC: We might consider to put also IRootObjectBuilder interface here in same folder and create concrete classes from it in per connector.
public interface IHostObjectBuilder
{
  /// <summary>
  /// Build host application objects from root commit object.
  /// </summary>
  /// <param name="rootObject">Commit object that received from server.</param>
  /// <param name="projectName">Project of the model.</param>
  /// <param name="modelName">Name of the model.</param>
  /// <param name="onOperationProgressed"> Action to update UI progress bar.</param>
  /// <returns> List of application ids.</returns> // POC: Where we will return these ids will matter later when we target to also cache received application ids.
  /// <remarks>Project and model name are needed for now to construct host app objects into related layers or filters.
  /// POC: we might consider later to have HostObjectBuilderContext? that might hold all possible data we will need.</remarks>
  Task<HostObjectBuilderResult> Build(
    Base rootObject,
    string projectName,
    string modelName,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken
  );
}

public record HostObjectBuilderResult(
  IEnumerable<string> BakedObjectIds,
  IEnumerable<ReceiveConversionResult> ConversionResults
);
