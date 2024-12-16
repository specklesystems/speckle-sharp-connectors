using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using Speckle.Connectors.Common.Operations;
using Speckle.Converters.Common;
using Speckle.Sdk;
using Speckle.Sdk.Common;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;

namespace Speckle.Connectors.Grasshopper8.HostApp;

public class GrasshopperSendOperation
{
  private readonly IRootObjectSender _baseObjectSender;
  private readonly IRootToSpeckleConverter _converter;

  public GrasshopperSendOperation(IRootObjectSender baseObjectSender, IRootToSpeckleConverter converter)
  {
    _baseObjectSender = baseObjectSender;
    _converter = converter;
  }

  public async Task<GrashopperSendOperationResult> Execute(
    IReadOnlyDictionary<string, GH_Structure<IGH_Goo>> objects,
    SendInfo sendInfo,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken ct = default
  )
  {
    var elements = new List<Base>();
    foreach (var keypair in objects)
    {
      elements.Add(ConvertGhStructureToCollection(keypair.Key, keypair.Value));
    }

    var buildResult = new Collection("Grasshopper Model") { elements = elements, ["version"] = 3 };

    var result = await _baseObjectSender.Send(buildResult, sendInfo, onOperationProgressed, ct).ConfigureAwait(false);

    return new(result.RootId);
  }

  private Collection ConvertGhStructureToCollection(string name, GH_Structure<IGH_Goo> tree)
  {
    var result = tree.Paths.Select(path =>
    {
      var pathElements = tree.get_Branch(path) as IList<IGH_Goo>;
      var convertedElements = new List<Base>();

      pathElements.NotNull();

      foreach (var pathElement in pathElements)
      {
        try
        {
          var goo = pathElement.UnwrapGoo<GeometryBase>();
          var converted = _converter.Convert(goo);
          convertedElements.Add(converted);
        }
        catch (Exception e) when (!e.IsFatal())
        {
          Console.WriteLine(e);
        }
      }

      return new Collection(path.ToString()) { elements = convertedElements };
    });

    return new Collection(name) { elements = result.Cast<Base>().ToList() };
  }
}

public record GrashopperSendOperationResult(string RootId);
