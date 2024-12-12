using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.Common.Operations.Receive;
using Speckle.Connectors.Grasshopper8.Components.BaseComponents;
using Speckle.Connectors.Grasshopper8.Parameters;
using Speckle.Sdk;
using Speckle.Sdk.Models.Collections;

namespace Speckle.Connectors.Grasshopper8.Components.Collections;

public record UnpackCollectionComponentInput(Collection RootObject) { }

public record UnpackRootObjectComponentOutput(Dictionary<string, List<SpeckleObject?>> Elements) { }

public class UnpackCollectionComponent
  : SpeckleScopedTaskCapableComponent<UnpackCollectionComponentInput, UnpackRootObjectComponentOutput>
{
  public UnpackCollectionComponent()
    : base("Unpack Root Object", "SURO", "Unpacks the root object from a receive operation", "Speckle", "Collections")
  { }

  public override Guid ComponentGuid => new("3C770686-20D5-434C-99E3-BDE735E8267F");

  protected override void RegisterInputParams(GH_InputParamManager pManager)
  {
    pManager.AddParameter(new SpeckleCollectionWrapperParam(GH_ParamAccess.item));
  }

  protected override void RegisterOutputParams(GH_OutputParamManager pManager)
  {
    pManager.AddTextParameter("Element Paths", "EP", "Path to the element in the collection tree", GH_ParamAccess.tree);
    pManager.AddParameter(new SpeckleObjectParam(), "Elements", "E", "Elements", GH_ParamAccess.tree);
  }

  protected override UnpackCollectionComponentInput GetInput(IGH_DataAccess da)
  {
    SpeckleCollectionGoo? collectionGoo = null;
    da.GetData(0, ref collectionGoo);
    if (collectionGoo == null)
    {
      throw new SpeckleException("No base object provided");
    }
    return new UnpackCollectionComponentInput(collectionGoo.Value);
  }

  protected override void SetOutput(IGH_DataAccess da, UnpackRootObjectComponentOutput result)
  {
    GH_Structure<GH_String> paths = new();
    GH_Structure<SpeckleObjectGoo> elements = new();

    int count = 0;
    foreach (var element in result.Elements)
    {
      var ints = da.ParameterTargetPath(0);
      var indexPath = ints.AppendElement(da.ParameterTargetIndex(0));
      var countPath = indexPath.AppendElement(count);
      paths.Append(new GH_String(element.Key), indexPath);
      elements.AppendRange(element.Value.Select(e => new SpeckleObjectGoo(e!)), countPath);
      count++;
    }

    da.SetDataTree(0, paths);
    da.SetDataTree(1, elements);
  }

  protected override Task<UnpackRootObjectComponentOutput> PerformScopedTask(
    UnpackCollectionComponentInput input,
    IServiceScope scope,
    CancellationToken cancellationToken = default
  )
  {
    var rootObjectUnpacker = scope.ServiceProvider.GetRequiredService<RootObjectUnpacker>();
    var contextUnpacker = scope.ServiceProvider.GetRequiredService<TraversalContextUnpacker>();

    var unpackedRoot = rootObjectUnpacker.Unpack(input.RootObject);

    // 2 - Split atomic objects and instance components with their path
    var (atomicObjects, instanceComponents) = rootObjectUnpacker.SplitAtomicObjectsAndInstances(
      unpackedRoot.ObjectsToConvert
    );

    // At this point everything should have been converted to SpeckleGrasshopperObjects (name pending), so no instances should exist.
    var atomicObjectsWithPath = contextUnpacker.GetAtomicObjectsWithPath(atomicObjects);

    var dict = new Dictionary<string, List<SpeckleObject?>>();
    foreach (var atomicObj in atomicObjectsWithPath)
    {
      var names = atomicObj.path.Select(p => p.name);
      string fullPath = string.Join("::", names);
      if (!dict.TryGetValue(fullPath, out List<SpeckleObject?>? value))
      {
        value = new List<SpeckleObject?>();
        dict.Add(fullPath, value);
      }
      value.Add(atomicObj.current as SpeckleObject);
    }

    var output = new UnpackRootObjectComponentOutput(dict);

    return Task.FromResult(output);
  }
}
