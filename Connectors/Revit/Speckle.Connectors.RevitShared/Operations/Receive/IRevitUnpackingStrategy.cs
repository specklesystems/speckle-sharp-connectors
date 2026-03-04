using Speckle.Connectors.Common.Instances;
using Speckle.Connectors.Common.Operations.Receive;
using Speckle.Objects.Data;
using Speckle.Sdk.Models.Collections;
using Speckle.Sdk.Models.Instances;

namespace Speckle.Connectors.Revit.Operations.Receive;

public record UnpackStrategyResult(
  IReadOnlyCollection<LocalToGlobalMap> LocalToGlobalMaps,
  List<(Collection[] path, IInstanceComponent component)>? InstanceComponents,
  Dictionary<string, DataObject> ParentDataObjectMap
);

/// <summary>
/// Defines the strategy for unpacking a commit into bakeable Revit objects.
/// </summary>
/// <remarks>
/// Depending on the setting, we either blindly flatten everything into DirectShapes, or we carefully
/// split out instance components to bake as native Revit Families.
/// </remarks>
public interface IRevitUnpackStrategy
{
  UnpackStrategyResult Unpack(RootObjectUnpackerResult unpackedRoot);
}
