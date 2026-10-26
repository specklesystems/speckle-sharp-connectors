namespace Speckle.Converters.TeklaShared.ToSpeckle.Helpers;

public class ClassPropertyExtractor
{
  public ClassPropertyExtractor() { }

  public Dictionary<string, object?> GetProperties(TSM.ModelObject modelObject)
  {
    Dictionary<string, object?> properties = new();

    // the api class is coarser than the type we publish (a column and a pad footing are both Beam), so we
    // keep it here rather than lose it
    properties["apiClass"] = modelObject.GetType().Name;

    switch (modelObject)
    {
      // includes beams and contour plates
      case TSM.Part part:
        AddPartProperties(part, properties);
        break;
      case TSM.BoltArray boltArray:
        AddBoltArrayProperties(boltArray, properties);
        break;
      case TSM.SingleRebar singleRebar:
        AddSingleRebarProperties(singleRebar, properties);
        break;
      case TSM.RebarMesh rebarMesh:
        AddRebarMeshProperties(rebarMesh, properties);
        break;
      case TSM.RebarGroup rebarGroup:
        AddRebarGroupProperties(rebarGroup, properties);
        break;
      case TSM.Assembly assembly:
        AddAssemblyProperties(assembly, properties);
        break;
      case TSM.PourObject pourObject:
        AddPourObjectProperties(pourObject, properties);
        break;
      case TSM.BaseComponent component:
        AddComponentProperties(component, properties);
        break;
      case TSM.RebarSplice splice:
        AddRebarSpliceProperties(splice, properties);
        break;
      case TSM.BaseWeld weld:
        AddWeldProperties(weld, properties);
        break;
    }

    return properties;
  }

  private void AddWeldProperties(TSM.BaseWeld weld, Dictionary<string, object?> properties)
  {
    properties["typeAbove"] = weld.TypeAbove.ToString();
    properties["sizeAbove"] = weld.SizeAbove;
    properties["typeBelow"] = weld.TypeBelow.ToString();
    properties["sizeBelow"] = weld.SizeBelow;
    properties["shopWeld"] = weld.ShopWeld;
    properties["aroundWeld"] = weld.AroundWeld;
    properties["weldNumber"] = weld.WeldNumber;
    properties["weldNumberPrefix"] = weld.WeldNumberPrefix;
  }

  private void AddComponentProperties(TSM.BaseComponent component, Dictionary<string, object?> properties)
  {
    properties["number"] = component.Number;
    switch (component)
    {
      case TSM.Connection connection:
        properties["code"] = connection.Code;
        properties["class"] = connection.Class;
        properties["status"] = connection.Status.ToString();
        break;
      case TSM.Detail detail:
        properties["code"] = detail.Code;
        properties["class"] = detail.Class;
        properties["status"] = detail.Status.ToString();
        properties["detailType"] = detail.DetailType.ToString();
        break;
      case TSM.Seam seam:
        properties["code"] = seam.Code;
        properties["class"] = seam.Class;
        properties["status"] = seam.Status.ToString();
        break;
    }
  }

  private void AddRebarSpliceProperties(TSM.RebarSplice splice, Dictionary<string, object?> properties)
  {
    properties["type"] = splice.Type.ToString();
    properties["lapLength"] = splice.LapLength;
    properties["offset"] = splice.Offset;
    properties["clearance"] = splice.Clearance;
    properties["barPositions"] = splice.BarPositions.ToString();
  }

  private void AddAssemblyProperties(TSM.Assembly assembly, Dictionary<string, object?> properties)
  {
    properties["assemblyType"] = assembly.GetAssemblyType().ToString();
    properties["assemblyPrefix"] = assembly.AssemblyNumber.Prefix;
    properties["assemblyStartNumber"] = assembly.AssemblyNumber.StartNumber;
  }

  private void AddPourObjectProperties(TSM.PourObject pourObject, Dictionary<string, object?> properties)
  {
    properties["class"] = pourObject.Class;
    properties["pourNumber"] = pourObject.PourNumber;
    properties["pourType"] = pourObject.PourType;
    properties["concreteMixture"] = pourObject.ConcreteMixture;
  }

  private void AddPartProperties(TSM.Part part, Dictionary<string, object?> properties)
  {
    properties["profile"] = part.Profile.ProfileString;
    properties["material"] = part.Material.MaterialString;
  }

  private void AddBoltArrayProperties(TSM.BoltArray boltArray, Dictionary<string, object?> properties)
  {
    properties["boltSize"] = boltArray.BoltSize.ToString();
    properties["boltCount"] = boltArray.BoltPositions.Count.ToString();
    properties["boltStandard"] = boltArray.BoltStandard;
  }

  private void AddSingleRebarProperties(TSM.SingleRebar singleRebar, Dictionary<string, object?> properties)
  {
    properties["grade"] = singleRebar.Grade;
    properties["size"] = singleRebar.Size;
  }

  private void AddRebarMeshProperties(TSM.RebarMesh rebarMesh, Dictionary<string, object?> properties)
  {
    properties["grade"] = rebarMesh.Grade;
  }

  private void AddRebarGroupProperties(TSM.RebarGroup rebarGroup, Dictionary<string, object?> properties)
  {
    properties["grade"] = rebarGroup.Grade;
    properties["size"] = rebarGroup.Size;
  }
}
