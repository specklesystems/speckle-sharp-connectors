using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.Common.Operations.Receive;
using Speckle.Connectors.Grasshopper8.Components.BaseComponents;
using Speckle.Connectors.Grasshopper8.Parameters;
using Speckle.Sdk;
using Speckle.Sdk.Models.Collections;

namespace Speckle.Connectors.Grasshopper8.Components.Collections;

public record UnpackRootObjectComponentInput(Collection RootObject) { }

public record UnpackRootObjectComponentOutput(Dictionary<string, List<SpeckleObject?>> Elements) { }

public class UnpackRootObjectComponent
  : SpeckleScopedTaskCapableComponent<UnpackRootObjectComponentInput, UnpackRootObjectComponentOutput>
{
  public UnpackRootObjectComponent()
    : base("Unpack Root Object", "SURO", "Unpacks the root object from a receive operation", "Speckle", "Collections")
  { }

  public override Guid ComponentGuid => new("3C770686-20D5-434C-99E3-BDE735E8267F");

  protected override void RegisterInputParams(GH_InputParamManager pManager)
  {
    pManager.AddParameter(new SpeckleCollectionWrapperParam(GH_ParamAccess.item));
  }

  protected override void RegisterOutputParams(GH_OutputParamManager pManager)
  {
    pManager.AddTextParameter("Element Paths", "EP", "Path to the element in the collection tree", GH_ParamAccess.list);
    pManager.AddParameter(new SpeckleObjectParam(), "Elements", "E", "Elements", GH_ParamAccess.tree);
  }

  protected override UnpackRootObjectComponentInput GetInput(IGH_DataAccess da)
  {
    SpeckleCollectionGoo? collectionGoo = null;
    da.GetData(0, ref collectionGoo);
    if (collectionGoo == null)
    {
      throw new SpeckleException("No base object provided");
    }
    return new UnpackRootObjectComponentInput(collectionGoo.Value);
  }

  protected override void SetOutput(IGH_DataAccess da, UnpackRootObjectComponentOutput result)
  {
    List<string> paths = new();
    GH_Structure<SpeckleObjectGoo> elements = new();

    int count = 0;
    foreach (var element in result.Elements)
    {
      var ints = da.ParameterTargetPath(0);
      var ghPath = ints.AppendElement(da.ParameterTargetIndex(0)).AppendElement(count);
      paths.Add(element.Key);
      elements.AppendRange(element.Value.Select(e => new SpeckleObjectGoo(e!)), ghPath);
      count++;
    }

    da.SetDataList(0, paths);
    da.SetDataTree(1, elements);
  }

  protected override Task<UnpackRootObjectComponentOutput> PerformScopedTask(
    UnpackRootObjectComponentInput input,
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

    atomicObjectsWithPath.ForEach(atomicObj =>
    {
      var names = atomicObj.path.Select(p => p.name);
      string fullPath = string.Join("::", names);
      if (!dict.TryGetValue(fullPath, out List<SpeckleObject?>? value))
      {
        value = new List<SpeckleObject?>();
        dict.Add(fullPath, value);
      }
      value.Add(atomicObj.current as SpeckleObject);
    });

    var output = new UnpackRootObjectComponentOutput(dict);

    return Task.FromResult(output);
  }
}
