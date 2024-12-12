using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Operations;
using Speckle.Converters.Common;
using Speckle.Sdk;
using Speckle.Sdk.Common;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;

namespace Speckle.Connectors.Grasshopper8.HostApp;

public class GrasshopperRootObjectBuilder(IRootToSpeckleConverter converter)
  : IRootObjectBuilder<IReadOnlyDictionary<string, GH_Structure<IGH_Goo>>>
{
  public Task<RootObjectBuilderResult> Build(
    IReadOnlyDictionary<string, GH_Structure<IGH_Goo>> input,
    SendInfo sendInfo,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken ct = default
  )
  {
    // TODO: Send info is used in other connectors to get the project ID to populate the SendConversionCache

    Console.WriteLine($"Send Info {sendInfo}");

    var elements = new List<Base>();

    foreach (var keypair in input)
    {
      ct.ThrowIfCancellationRequested();

      var progress = new Progress<double>(d =>
      {
        onOperationProgressed.Report(new CardProgress($"Converting {keypair.Key}", d / input.Count));
      });

      elements.Add(ConvertGhStructureToCollection(keypair.Key, keypair.Value, progress, ct));
    }

    var rootModel = new Collection("Grasshopper Model") { elements = elements, ["version"] = 3 };

    // TODO: Not getting any conversion results yet
    var result = new RootObjectBuilderResult(rootModel, []);

    return Task.FromResult(result);
  }

  private Collection ConvertGhStructureToCollection(
    string name,
    GH_Structure<IGH_Goo> tree,
    IProgress<double> onOperationProgressed,
    CancellationToken ct = default
  )
  {
    var totalItems = tree.DataCount;
    var currentIndex = 0;
    var branchCollections = new List<Base>();
    foreach (var path in tree.Paths)
    {
      ct.ThrowIfCancellationRequested();
      var pathElements = tree.get_Branch(path) as IList<IGH_Goo>;
      var convertedElements = new List<Base>();

      pathElements.NotNull();

      foreach (var pathElement in pathElements)
      {
        ct.ThrowIfCancellationRequested();

        try
        {
          var goo = pathElement.UnwrapGoo<GeometryBase>();
          var converted = converter.Convert(goo);
          convertedElements.Add(converted);
        }
        catch (Exception e) when (!e.IsFatal())
        {
          Console.WriteLine(e);
        }
        finally
        {
          onOperationProgressed.Report((double)currentIndex / totalItems);
          currentIndex++;
        }
      }

      var branchCollection = new Collection(path.ToString()) { elements = convertedElements };
      branchCollections.Add(branchCollection);
    }

    return new Collection(name) { elements = branchCollections };
  }
}
