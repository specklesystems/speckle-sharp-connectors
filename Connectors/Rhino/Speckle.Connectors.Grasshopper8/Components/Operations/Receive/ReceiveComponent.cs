using Grasshopper.Kernel;
using GrasshopperAsyncComponent;
using Microsoft.Extensions.DependencyInjection;
using Rhino.Geometry;
using Speckle.Connectors.Common.Instances;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.Common.Operations.Receive;
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

public class ReceiveComponent : GH_AsyncComponent
{
  public ReceiveComponent()
    : base("Receive from Speckle", "RFS", "Receive objects from speckle", "Speckle", "Operations")
  {
    BaseWorker = new ReceiveComponentWorker(this);
  }

  public override Guid ComponentGuid => new("74954F59-B1B7-41FD-97DE-4C6B005F2801");

  protected override Bitmap Icon => BitmapBuilder.CreateSquareIconBitmap("R");

  public string CurrentComponentState { get; set; } = "needs_input";
  public Client ApiClient { get; set; }
  public HostApp.SpeckleUrlModelResource? UrlModelResource { get; set; }
  public GrasshopperReceiveOperation ReceiveOperation { get; private set; }
  public RootObjectUnpacker RootObjectUnpacker { get; private set; }

  public static IServiceScope? Scope { get; set; }
  public static AccountService AccountManager { get; private set; }
  public static IClientFactory ClientFactory { get; private set; }

  public static CancellationToken CancellationToken { get; private set; }

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

  public override void AppendAdditionalMenuItems(ToolStripDropDown menu)
  {
    base.AppendAdditionalMenuItems(menu);

    Menu_AppendSeparator(menu);

    if (CurrentComponentState == "loading")
    {
      Menu_AppendItem(
        menu,
        "Cancel Load",
        (s, e) =>
        {
          CurrentComponentState = "expired";
          RequestCancellation();
        }
      );
    }
  }

  protected override void SolveInstance(IGH_DataAccess da)
  {
    Scope = PriorityLoader.Container.CreateScope();
    AccountManager = Scope.ServiceProvider.GetRequiredService<AccountService>();
    ClientFactory = Scope.ServiceProvider.GetRequiredService<IClientFactory>();
    CancellationToken = default;

    // We need to call this always in here to be able to react and set events :/
    ParseInput(da);

    ReceiveOperation = Scope.ServiceProvider.GetRequiredService<GrasshopperReceiveOperation>();
    RootObjectUnpacker = Scope.ServiceProvider.GetService<RootObjectUnpacker>();

    if (CurrentComponentState == "receiving")
    {
      base.SolveInstance(da);
      return;
    }

    // This ensures that we actually do a run. The worker will check and determine if it needs to pull an existing object or not.
    OnDisplayExpired(true);

    base.SolveInstance(da);
    return;
  }

  public override void RemovedFromDocument(GH_Document document)
  {
    RequestCancellation();
    Scope?.Dispose();
    base.RemovedFromDocument(document);
  }

  private void ParseInput(IGH_DataAccess da)
  {
    HostApp.SpeckleUrlModelResource? dataInput = null;
    da.GetData(0, ref dataInput);
    if (dataInput is null)
    {
      UrlModelResource = null;
      TriggerAutoSave();
      return;
    }

    UrlModelResource = dataInput;
    try
    {
      // TODO: Get any account for this server, as we don't have a mechanism yet to pass accountIds through
      Account account = AccountManager.GetAccountWithServerUrlFallback("", new Uri(dataInput.Server));
      if (account is null)
      {
        throw new SpeckleAccountManagerException($"No default account was found");
      }

      ApiClient?.Dispose();
      ApiClient = ClientFactory.Create(account);
    }
    catch (Exception e) when (!e.IsFatal())
    {
      AddRuntimeMessage(GH_RuntimeMessageLevel.Error, e.ToFormattedString());
    }
  }
}

public class ReceiveComponentWorker : WorkerInstance
{
  public ReceiveComponentWorker(GH_Component p)
    : base(p) { }

  private HostApp.SpeckleUrlModelResource? UrlModelResource { get; set; }

  public Base Root { get; set; }

  public SpeckleCollectionGoo Result { get; set; }

  private List<(GH_RuntimeMessageLevel, string)> RuntimeMessages { get; } = new();

  public override WorkerInstance Duplicate()
  {
    return new ReceiveComponentWorker(Parent);
  }

  public override void GetData(IGH_DataAccess dataAcess, GH_ComponentParamServer p)
  {
    UrlModelResource = ((ReceiveComponent)Parent).UrlModelResource;
  }

#pragma warning disable CA1506

  public override void DoWork(Action<string, double> reportProgress, Action done)
  {
    var receiveComponent = (ReceiveComponent)Parent;

    try
    {
      if (UrlModelResource is null)
      {
        throw new InvalidOperationException("Url Resource was null");
      }

      var t = Task.Run(async () =>
      {
        // Step 1 - RECIEVE FROM SERVER
        var receiveInfo = await UrlModelResource
          .GetReceiveInfo(receiveComponent.ApiClient, CancellationToken)
          .ConfigureAwait(false);

        var progress = new Progress<CardProgress>(_ =>
        {
          // TODO: Progress here
          // Message = $"{progress.Status}: {progress.Progress}";
        });

        Root = await receiveComponent
          .ReceiveOperation.ReceiveCommitObject(receiveInfo, progress, CancellationToken)
          .ConfigureAwait(false);

        // Step 2 - CONVERT

        var localToGlobalUnpacker = new LocalToGlobalUnpacker();
        var traversalContextUnpacker = new TraversalContextUnpacker();
        var unpackedRoot = receiveComponent.RootObjectUnpacker.Unpack(Root);

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

        // DONE
        done();
      });
      t.Wait();
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      RuntimeMessages.Add((GH_RuntimeMessageLevel.Error, ex.ToFormattedString()));
      done();
    }
  }
#pragma warning restore CA1506


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
    if (result is IEnumerable<(GeometryBase, Base)> fallbackConversionResult)
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
