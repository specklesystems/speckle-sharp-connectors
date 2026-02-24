using Speckle.Connectors.Common.Operations;
using Speckle.Sdk.Models.Collections;
using Speckle.Sdk.Models.Instances;
using Speckle.Sdk.Pipelines.Progress;

namespace Speckle.Connectors.Common.Instances;

public interface IInstanceBaker<TAppIdMapValueType>
{
  /// <summary>
  /// Will bake a set of instance components (instances and instance definitions) in the host app.
  /// </summary>
  /// <param name="instanceComponents"></param>
  /// <param name="applicationIdMap"></param>
  /// <param name="baseLayerName"></param>
  /// <param name="onOperationProgressed"></param>
  /// <returns></returns>
  BakeResult BakeInstances(
    ICollection<(Collection[] collectionPath, IInstanceComponent obj)> instanceComponents,
    Dictionary<string, TAppIdMapValueType> applicationIdMap,
    string baseLayerName,
    IProgress<CardProgress> onOperationProgressed
  );

  /// <summary>
  /// <para>Cleans up previously baked instances and associated definitions containing the `namePrefix` in their name.</para>
  /// <para>Note: this is based on the convention that all defintions have their name set to a model based prefix.</para>
  /// </summary>
  /// <param name="namePrefix">The name prefix to search and delete by.</param>
  void PurgeInstances(string namePrefix);
}
