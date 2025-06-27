using Grasshopper.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.Common;
using Speckle.Connectors.Common.Analytics;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.Common.Operations.Receive;
using Speckle.Connectors.GrasshopperShared.Components.BaseComponents;
using Speckle.Connectors.GrasshopperShared.HostApp;
using Speckle.Connectors.GrasshopperShared.Operations.Receive;
using Speckle.Connectors.GrasshopperShared.Parameters;
using Speckle.Connectors.GrasshopperShared.Properties;
using Speckle.Connectors.GrasshopperShared.Registration;
using Speckle.Sdk;
using Speckle.Sdk.Api;
using Speckle.Sdk.Credentials;
using Speckle.Sdk.Models.Collections;

namespace Speckle.Connectors.GrasshopperShared.Components.Operations.Receive;

public class ReceiveComponentInput
{
  public SpeckleUrlModelResource Resource { get; }
  public bool Run { get; }

  public ReceiveComponentInput(SpeckleUrlModelResource resource, bool run)
  {
    Resource = resource;
    Run = run;
  }
}

public class ReceiveComponentOutput
{
  public SpeckleCollectionWrapperGoo RootObject { get; set; }
}

public class ReceiveComponent : SpeckleTaskCapableComponent<ReceiveComponentInput, ReceiveComponentOutput>
{
  public ReceiveComponent()
    : base(
      "(Sync) Load",
      "sL",
      "Load a model from Speckle, synchronously",
      ComponentCategories.PRIMARY_RIBBON,
      ComponentCategories.DEVELOPER
    ) { }

  public override Guid ComponentGuid => new("74954F59-B1B7-41FD-97DE-4C6B005F2801");
  protected override Bitmap Icon => Resources.speckle_operations_syncload;

  protected override void RegisterInputParams(GH_InputParamManager pManager)
  {
    pManager.AddParameter(new SpeckleUrlModelResourceParam(GH_ParamAccess.item));
    pManager.AddBooleanParameter("Run", "r", "Run the load operation", GH_ParamAccess.item);
  }

  protected override void RegisterOutputParams(GH_OutputParamManager pManager)
  {
    pManager.AddParameter(
      new SpeckleCollectionParam(GH_ParamAccess.item),
      "Collection",
      "collection",
      "The model collection of the loaded version",
      GH_ParamAccess.item
    );
  }

  protected override ReceiveComponentInput GetInput(IGH_DataAccess da)
  {
    SpeckleUrlModelResource? url = null;
    da.GetData(0, ref url);
    if (url is null)
    {
      throw new SpeckleException("Speckle model resource is null");
    }

    bool run = false;
    da.GetData(1, ref run);

    return new(url, run);
  }

  protected override void SetOutput(IGH_DataAccess da, ReceiveComponentOutput result)
  {
    if (result.RootObject is null)
    {
      Message = "Not Loaded";
    }
    else
    {
      da.SetData(0, result.RootObject);
      Message = "Done";
    }
  }

#pragma warning disable CA1506
  protected override async Task<ReceiveComponentOutput> PerformTask(
#pragma warning restore CA1506
    ReceiveComponentInput input,
    CancellationToken cancellationToken = default
  )
  {
    var multipleResources = Params.Input[0].VolatileData.HasInputCountGreaterThan(1);
    if (multipleResources)
    {
      AddRuntimeMessage(
        GH_RuntimeMessageLevel.Error,
        "Only one model can be loaded at a time. To load to multiple models, please use different load components."
      );
      return new();
    }
    if (!input.Run)
    {
      return new();
    }

    using var scope = PriorityLoader.CreateScopeForActiveDocument();
    var clientFactory = scope.ServiceProvider.GetRequiredService<IClientFactory>();
    var receiveOperation = scope.ServiceProvider.GetRequiredService<GrasshopperReceiveOperation>();

    // Do the thing 👇🏼

    Account? account = input.Resource.Account.GetAccount(scope);
    if (account is null)
    {
      throw new SpeckleAccountManagerException($"No default account was found");
    }

    using var client = clientFactory.Create(account);
    var receiveInfo = await input.Resource.GetReceiveInfo(client, cancellationToken).ConfigureAwait(false);

    var progress = new Progress<CardProgress>(_ =>
    {
      // TODO: Progress only makes sense in non-blocking async receive, which is not supported yet.
      // Message = $"{progress.Status}: {progress.Progress}";
    });

    var root = await receiveOperation
      .ReceiveCommitObject(receiveInfo, progress, cancellationToken)
      .ConfigureAwait(false);

    // TODO: If we have NodeRun events later, better to have `ComponentTracker` to use across components
    var customProperties = new Dictionary<string, object>()
    {
      { "isAsync", false },
      { "sourceHostApp", HostApplications.GetSlugFromHostAppNameAndVersion(receiveInfo.SourceApplication) }
    };
    if (receiveInfo.WorkspaceId != null)
    {
      customProperties.Add("workspace_id", receiveInfo.WorkspaceId);
    }
    if (receiveInfo.SelectedVersionUserId != null)
    {
      customProperties.Add("isMultiplayer", receiveInfo.SelectedVersionUserId != client.Account.userInfo.id);
    }
    var mixpanel = PriorityLoader.Container.GetRequiredService<IMixPanelManager>();
    await mixpanel.TrackEvent(MixPanelEvents.Receive, account, customProperties);

    // We need to rethink these lovely unpackers, there's a bit too many of 'em
    var rootObjectUnpacker = scope.ServiceProvider.GetService<RootObjectUnpacker>();
    var traversalContextUnpacker = new TraversalContextUnpacker();

    var unpackedRoot = rootObjectUnpacker.Unpack(root);

    // split atomic objects from block components before conversion
    var (atomicObjects, blockInstances) = rootObjectUnpacker.SplitAtomicObjectsAndInstances(
      unpackedRoot.ObjectsToConvert
    );

    // Initialize unpackers and collection builder
    var colorUnpacker = new GrasshopperColorUnpacker(unpackedRoot);
    var materialUnpacker = new GrasshopperMaterialUnpacker(unpackedRoot);
    var collectionRebuilder = new GrasshopperCollectionRebuilder(
      (root as Collection) ?? new Collection { name = "unnamed" }
    );

    // convert atomic objects directly
    var mapHandler = new LocalToGlobalMapHandler(
      traversalContextUnpacker,
      collectionRebuilder,
      colorUnpacker,
      materialUnpacker
    );

    // objects used in definitions need to be added to ConvertedObjectsMap but not directly to collection
    // this flattened list allows us to create the bool flag needed by the ConvertAtomicObject method
    var definitionObjectIds = unpackedRoot.DefinitionProxies.GetDefinitionObjectIds();

    foreach (var atomicContext in atomicObjects)
    {
      var objId = atomicContext.Current.applicationId ?? atomicContext.Current.id;
      bool isDefinitionObject = objId != null && definitionObjectIds.Contains(objId);
      mapHandler.ConvertAtomicObject(atomicContext, isDefinitionObject);
    }

    // process block instances using converted atomic objects
    // NOTE: Objects of block definitions appear both as standalone objects and within block definitions when baked.
    // block processing needs converted objects, but object filtering needs block definitions.
    mapHandler.ConvertBlockInstances(blockInstances, unpackedRoot.DefinitionProxies);

    // var x = new SpeckleCollectionGoo { Value = collGen.RootCollection };
    var goo = new SpeckleCollectionWrapperGoo(collectionRebuilder.RootCollectionWrapper);
    return new ReceiveComponentOutput { RootObject = goo };
  }
}
