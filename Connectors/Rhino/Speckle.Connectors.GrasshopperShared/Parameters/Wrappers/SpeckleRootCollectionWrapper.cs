using System.Diagnostics.CodeAnalysis;

namespace Speckle.Connectors.GrasshopperShared.Parameters;

public class SpeckleRootCollectionWrapper : SpeckleCollectionWrapper
{
  public Dictionary<string, object?>? Properties { get; set; }

  /// <summary>
  /// Relations authored on the canvas, peeled off the Publish input [ENG-9475]. Model-scoped like <see cref="Properties"/>:
  /// they are edges between objects anywhere in the tree, not members of it, and the artefact builder resolves them by
  /// application id after the walk.
  /// </summary>
  public List<SpeckleRelation>? Relations { get; set; }

  public SpeckleRootCollectionWrapper() { }

  [SetsRequiredMembers]
  public SpeckleRootCollectionWrapper(
    SpeckleCollectionWrapper wrapper,
    Dictionary<string, object?>? properties = null,
    IReadOnlyList<SpeckleRelation>? relations = null
  )
  {
    Base = wrapper.Base;
    Color = wrapper.Color;
    Material = wrapper.Material;
    ApplicationId = wrapper.ApplicationId;
    Name = wrapper.Name;
    Path = wrapper.Path;
    Topology = wrapper.Topology;
    Elements = wrapper.Elements;
    ModelContext = wrapper.ModelContext;
    Properties = properties;
    Relations = relations is { Count: > 0 } ? relations.ToList() : null;
  }

  public new SpeckleRootCollectionWrapper DeepCopy()
  {
    // delegate most to SpeckleCollectionWrapper and we just copy result
    SpeckleCollectionWrapper baseCopy = base.DeepCopy();
    return new SpeckleRootCollectionWrapper
    {
      Base = baseCopy.Base,
      Color = baseCopy.Color,
      Material = baseCopy.Material,
      ApplicationId = baseCopy.ApplicationId,
      Name = baseCopy.Name,
      Path = baseCopy.Path,
      Topology = baseCopy.Topology,
      Elements = baseCopy.Elements,
      ModelContext = baseCopy.ModelContext,
      Properties = Properties != null ? new Dictionary<string, object?>(Properties) : null,
      Relations = Relations != null ? new List<SpeckleRelation>(Relations) : null, // relations are immutable
    };
  }
}
