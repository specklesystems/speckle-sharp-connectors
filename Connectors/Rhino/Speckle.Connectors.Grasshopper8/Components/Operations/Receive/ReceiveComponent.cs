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
using Speckle.Sdk.Models.Extensions;

namespace Speckle.Connectors.Grasshopper8.Components.Operations.Receive;

public class ReceiveComponentOutput
{
  public SpeckleCollectionGoo RootObject { get; set; }
}

public class ReceiveComponent : GH_AsyncComponent<HostApp.SpeckleUrlModelResource, ReceiveComponentOutput>
{
  public ReceiveComponent()
    : base("Receive from Speckle", "RFS", "Receive objects from speckle", "Speckle", "Operations")
  {
    BaseWorker = new ReceiveComponentWorker(this);
  }

  public override Guid ComponentGuid => new("74954F59-B1B7-41FD-97DE-4C6B005F2801");

  protected override Bitmap Icon => BitmapBuilder.CreateSquareIconBitmap("R");

  public HostApp.SpeckleUrlModelResource UrlResource { get; set; }
  public AccountService AccountManager { get; set; }
  public IClientFactory ClientFactory { get; set; }
  public GrasshopperReceiveOperation ReceiveOperation { get; set; }
  public RootObjectUnpacker RootObjectUnpacker { get; set; }
  public CancellationToken CancellationToken { get; set; }

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

  protected override HostApp.SpeckleUrlModelResource GetInput(IGH_DataAccess da)
  {
    HostApp.SpeckleUrlModelResource? url = null;
    da.GetData(0, ref url);
    if (url is null)
    {
      throw new SpeckleException("Speckle url is null");
    }

    UrlResource = url;

    return url;
  }

  protected override void SetOutput(IGH_DataAccess da, ReceiveComponentOutput result)
  {
    da.SetData(0, result.RootObject);
    Message = "Done";
  }

  protected override Task<ReceiveComponentOutput> PerformScopedTask(
    HostApp.SpeckleUrlModelResource input,
    IServiceScope scope,
    CancellationToken cancellationToken = default
  )
  {
    // TODO: Resolving dependencies here may be overkill in most cases. Must re-evaluate.
    AccountManager = scope.ServiceProvider.GetRequiredService<AccountService>();
    ClientFactory = scope.ServiceProvider.GetRequiredService<IClientFactory>();
    ReceiveOperation = scope.ServiceProvider.GetRequiredService<GrasshopperReceiveOperation>();
    RootObjectUnpacker = scope.ServiceProvider.GetService<RootObjectUnpacker>();
    CancellationToken = cancellationToken;

    // Do the thing 👇🏼
    var worker = (ReceiveComponentWorker)BaseWorker;
    return Task.FromResult(new ReceiveComponentOutput { RootObject = worker.Result });
  }
}

public class ReceiveComponentWorker : WorkerInstance
{
  public ReceiveComponentWorker(GH_Component p)
    : base(p) { }

  private AccountService AccountManager { get; set; }
  private IClientFactory ClientFactory { get; set; }
  private GrasshopperReceiveOperation ReceiveOperation { get; set; }
  private RootObjectUnpacker RootObjectUnpacker { get; set; }
  private HostApp.SpeckleUrlModelResource UrlModelResource { get; set; }

  public Base Root { get; set; }

  public SpeckleCollectionGoo Result { get; set; }

  private List<(GH_RuntimeMessageLevel, string)> RuntimeMessages { get; } = new();

  public override WorkerInstance Duplicate()
  {
    return new ReceiveComponentWorker(Parent);
  }

  public override void GetData(IGH_DataAccess dataAcess, GH_ComponentParamServer p)
  {
    HostApp.SpeckleUrlModelResource? url = null;
    dataAcess.GetData(0, ref url);
    if (url is null)
    {
      throw new SpeckleException("Speckle url is null");
    }

    UrlModelResource = url;
  }

  public override void DoWork(Action done)
  {
    using var scope = PriorityLoader.Container.CreateScope();
    var receiveComponent = (ReceiveComponent)Parent;

    AccountManager = scope.ServiceProvider.GetRequiredService<AccountService>();
    ClientFactory = scope.ServiceProvider.GetRequiredService<IClientFactory>();
    ReceiveOperation = scope.ServiceProvider.GetRequiredService<GrasshopperReceiveOperation>();
    RootObjectUnpacker = scope.ServiceProvider.GetService<RootObjectUnpacker>();
    CancellationToken = default;

    try
    {
      // TODO: Get any account for this server, as we don't have a mechanism yet to pass accountIds through
      var account = AccountManager.GetAccountWithServerUrlFallback("", new Uri(UrlModelResource.Server));
      if (account is null)
      {
        throw new SpeckleAccountManagerException($"No default account was found");
      }
      var t = Task.Run(async () =>
      {
        using var client = ClientFactory.Create(account);
        var receiveInfo = await UrlModelResource.GetReceiveInfo(client, CancellationToken).ConfigureAwait(false);

        var progress = new Progress<CardProgress>(_ =>
        {
          // TODO: Progress only makes sense in non-blocking async receive, which is not supported yet.
          // Message = $"{progress.Status}: {progress.Progress}";
        });

        Root = await ReceiveOperation
          .ReceiveCommitObject(receiveInfo, progress, CancellationToken)
          .ConfigureAwait(false);
        done();
      });
      t.Wait();

      // We need to rethink these lovely unpackers, there's a bit too many of 'em
      var localToGlobalUnpacker = new LocalToGlobalUnpacker();
      var traversalContextUnpacker = new TraversalContextUnpacker();

      var unpackedRoot = RootObjectUnpacker.Unpack(Root);

      // "flatten" block instances
      var localToGlobalMaps = localToGlobalUnpacker.Unpack(
        unpackedRoot.DefinitionProxies,
        unpackedRoot.ObjectsToConvert.ToList()
      );

      var collGen = new CollectionRebuilder((Root as Collection) ?? new Collection() { name = "unnamed" });

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

      Result = new SpeckleCollectionGoo(collGen.RootCollection);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      RuntimeMessages.Add((GH_RuntimeMessageLevel.Error, ex.ToFormattedString()));
      done();
    }
  }

  public override void SetData(IGH_DataAccess dataAccess)
  {
    dataAccess.SetData(0, Result);
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
