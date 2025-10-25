using System.Diagnostics.CodeAnalysis;

namespace Speckle.Connectors.GrasshopperShared.Parameters;

public class SpeckleRootCollectionWrapper : SpeckleCollectionWrapper
{
  public Dictionary<string, object?>? RootProperties { get; set; }

  public SpeckleRootCollectionWrapper() { }
  
  [SetsRequiredMembers]
  public SpeckleRootCollectionWrapper(SpeckleCollectionWrapper wrapper, Dictionary<string, object?>? rootProperties = null)
  {
    Base = wrapper.Base;
    Color = wrapper.Color;
    Material = wrapper.Material;
    ApplicationId = wrapper.ApplicationId;
    Name = wrapper.Name;
    Path = wrapper.Path;
    Topology = wrapper.Topology;
    Elements = wrapper.Elements;
    RootProperties = rootProperties;
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
      RootProperties = RootProperties != null ? new Dictionary<string, object?>(RootProperties) : null
    };
  }
}
