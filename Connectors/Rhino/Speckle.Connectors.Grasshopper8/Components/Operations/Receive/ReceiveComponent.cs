using Grasshopper.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Rhino.Geometry;
using Speckle.Connectors.Common.Instances;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.Common.Operations.Receive;
using Speckle.Connectors.Grasshopper8.Components.BaseComponents;
using Speckle.Connectors.Grasshopper8.HostApp;
using Speckle.Connectors.Grasshopper8.Parameters;
using Speckle.Sdk;
using Speckle.Sdk.Api;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Credentials;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;

namespace Speckle.Connectors.Grasshopper8.Components.Operations.Receive;

public class ReceiveComponentOutput
{
  public SpeckleCollectionGoo RootObject { get; set; }
}

public class ReceiveComponent : SpeckleScopedTaskCapableComponent<SpeckleUrlModelResource, ReceiveComponentOutput>
{
  public ReceiveComponent()
    : base("Receive from Speckle", "RFS", "Receive objects from speckle", "Speckle", "Operations") { }

  public override Guid ComponentGuid => new("74954F59-B1B7-41FD-97DE-4C6B005F2801");
  protected override Bitmap Icon => BitmapBuilder.CreateSquareIconBitmap("R");

  protected override void RegisterInputParams(GH_InputParamManager pManager)
  {
    pManager.AddParameter(new SpeckleUrlModelResourceParam(GH_ParamAccess.item));
  }

  protected override void RegisterOutputParams(GH_OutputParamManager pManager)
  {
    pManager.AddParameter(
      new SpeckleCollectionWrapperParam(GH_ParamAccess.item),
      "Model",
      "model",
      "The model object for the received version",
      GH_ParamAccess.item
    );
  }

  protected override SpeckleUrlModelResource GetInput(IGH_DataAccess da)
  {
    SpeckleUrlModelResource? url = null;
    da.GetData(0, ref url);
    if (url is null)
    {
      throw new SpeckleException("Speckle url is null");
    }

    return url;
  }

  protected override void SetOutput(IGH_DataAccess da, ReceiveComponentOutput result)
  {
    da.SetData(0, result.RootObject);
    Message = "Done";
  }

  protected override async Task<ReceiveComponentOutput> PerformScopedTask(
    SpeckleUrlModelResource input,
    IServiceScope scope,
    CancellationToken cancellationToken = default
  )
  {
    // TODO: Resolving dependencies here may be overkill in most cases. Must re-evaluate.
    var accountManager = scope.ServiceProvider.GetRequiredService<AccountService>();
    var clientFactory = scope.ServiceProvider.GetRequiredService<IClientFactory>();
    var receiveOperation = scope.ServiceProvider.GetRequiredService<GrasshopperReceiveOperation>();

    // Do the thing 👇🏼

    // TODO: Get any account for this server, as we don't have a mechanism yet to pass accountIds through
    var account = accountManager.GetAccountWithServerUrlFallback("", new Uri(input.Server));

    if (account is null)
    {
      throw new SpeckleAccountManagerException($"No default account was found");
    }

    using var client = clientFactory.Create(account);
    var receiveInfo = await input.GetReceiveInfo(client, cancellationToken).ConfigureAwait(false);

    var progress = new Progress<CardProgress>(_ =>
    {
      // TODO: Progress only makes sense in non-blocking async receive, which is not supported yet.
      // Message = $"{progress.Status}: {progress.Progress}";
    });

    var root = await receiveOperation
      .ReceiveCommitObject(receiveInfo, progress, cancellationToken)
      .ConfigureAwait(false);

    // We need to rethink these lovely unpackers, there's a bit too many of 'em
    var rootObjectUnpacker = scope.ServiceProvider.GetService<RootObjectUnpacker>();
    var localToGlobalUnpacker = new LocalToGlobalUnpacker();
    var traversalContextUnpacker = new TraversalContextUnpacker();

    var unpackedRoot = rootObjectUnpacker.Unpack(root);

    // "flatten" block instances
    var localToGlobalMaps = localToGlobalUnpacker.Unpack(
      unpackedRoot.DefinitionProxies,
      unpackedRoot.ObjectsToConvert.ToList()
    );

    var collGen = new CollectionRebuilder((root as Collection) ?? new Collection() { name = "unnamed" });

    foreach (var map in localToGlobalMaps)
    {
      try
      {
        var converted = Convert(map.AtomicObject);
        var path = traversalContextUnpacker.GetCollectionPath(map.TraversalContext).ToList();

        foreach (var matrix in map.Matrix)
        {
          var mat = GrasshopperHelpers.MatrixToTransform(matrix, "meters");
          converted.ForEach(res => res.Transform(mat));
        }

        // note one to many not handled too nice here
        foreach (var geometryBase in converted)
        {
          var gh = new SpeckleObject()
          {
            Base = map.AtomicObject,
            Path = path,
            GeometryBase = geometryBase
          };
          collGen.AppendSpeckleGrasshopperObject(gh);
        }
      }
      catch (ConversionException)
      {
        // TODO
      }
    }

    // var x = new SpeckleCollectionGoo { Value = collGen.RootCollection };
    var goo = new SpeckleCollectionGoo(collGen.RootCollection);
    return new ReceiveComponentOutput { RootObject = goo };
  }

  private List<GeometryBase> Convert(Base input)
  {
    var result = ToSpeckleConversionContext.ToHostConverter.Convert(input);

    if (result is GeometryBase geometry)
    {
      return [geometry];
    }
    if (result is List<GeometryBase> geometryList)
    {
      return geometryList;
    }
    if (result is IEnumerable<(object, Base)> fallbackConversionResult)
    {
      // note special handling for proxying render materials OR we don't care about revit
      return fallbackConversionResult.Select(t => t.Item1).Cast<GeometryBase>().ToList();
    }

    throw new SpeckleException("Failed to convert input to rhino");
  }
}

// NOTE: We will need GrasshopperCollections (with an extra path element)
// these will need to be handled now
internal sealed class CollectionRebuilder
{
  public Collection RootCollection { get; }

  private readonly Dictionary<string, Collection> _cache = new();

  public CollectionRebuilder(Collection baseCollection)
  {
    RootCollection = new Collection() { name = baseCollection.name, applicationId = baseCollection.applicationId };
  }

  public void AppendSpeckleGrasshopperObject(SpeckleObject speckleGrasshopperObject)
  {
    var collection = GetOrCreateCollectionFromPath(speckleGrasshopperObject.Path);
    collection.elements.Add(speckleGrasshopperObject);
  }

  private Collection GetOrCreateCollectionFromPath(IEnumerable<Collection> path)
  {
    // TODO - this flows but it can be optimised (ie, concat path first, check cache, iterate only if not in cache)
    var currentLayerName = "";
    Collection previousCollection = RootCollection;
    foreach (var collection in path)
    {
      currentLayerName += collection.name;
      if (_cache.TryGetValue(currentLayerName, out Collection col))
      {
        previousCollection = col;
        continue;
      }

      var newCollection = new Collection() { name = collection.name, applicationId = collection.applicationId };
      if (collection["path"] != null)
      {
        newCollection["path"] = collection["path"];
      }
      _cache[currentLayerName] = newCollection;
      previousCollection.elements.Add(newCollection);

      previousCollection = newCollection;
    }

    return previousCollection;
  }
}
