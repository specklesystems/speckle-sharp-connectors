using Grasshopper.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Rhino;
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
using Speckle.Sdk.Api.GraphQL.Models;
using Speckle.Sdk.Credentials;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;
using Speckle.Sdk.Pipelines.Progress;

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
  /// <remarks>
  /// Made nullable as output can be null when Run = false or on error
  /// </remarks>
  public SpeckleCollectionWrapperGoo? RootObject { get; set; }
  public SpecklePropertyGroupGoo? RootProperties { get; set; }
  public SpecklePropertyGroupGoo? ProxiesGoo { get; set; }
}

public class ReceiveComponent : SpeckleTaskCapableComponent<ReceiveComponentInput, ReceiveComponentOutput>
{
  private IClient? _apiClient;
  private string? _lastVersionId;
  private SpeckleUrlModelResource? _lastResource;
  public override Guid ComponentGuid => new("74954F59-B1B7-41FD-97DE-4C6B005F2801");
  protected override Bitmap Icon => Resources.speckle_operations_syncload;
  public override GH_Exposure Exposure => GH_Exposure.hidden;

  /// <remarks>
  /// Marks this component as obsolete in the Grasshopper UI (hides it from the ribbon, adds the
  /// "obsolete" overlay icon on canvas).
  /// </remarks>
  public override bool Obsolete => true;

  public ReceiveComponent()
    : base(
      "(Sync) Load",
      "sL",
      "Load a model from Speckle, synchronously",
      ComponentCategories.PRIMARY_RIBBON,
      ComponentCategories.DEVELOPER
    ) { }

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

    pManager.AddParameter(
      new SpecklePropertyGroupParam(),
      "Properties",
      "properties",
      "Model-wide properties from the root collection",
      GH_ParamAccess.item
    );

    pManager.AddParameter(
      new SpecklePropertyGroupParam(),
      "Proxies",
      "proxies",
      "Proxy objects from the root collection, keyed by type (e.g. levelProxies, analysisResults). Use Deconstruct to access individual lists.",
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

    if (run)
    {
      SetupSubscription(url);
    }
    else
    {
      CleanupSubscription();
    }

    return new ReceiveComponentInput(url, run);
  }

  protected override void SetOutput(IGH_DataAccess da, ReceiveComponentOutput result)
  {
    if (result.RootObject is null)
    {
      Message = _apiClient != null ? "Monitoring" : "Not Loaded";
    }
    else
    {
      da.SetData(0, result.RootObject);
      da.SetData(1, result.RootProperties);
      da.SetData(2, result.ProxiesGoo);

      Message = _apiClient != null ? "Loaded" : "Done";
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
      return new ReceiveComponentOutput();
    }
    if (!input.Run)
    {
      return new ReceiveComponentOutput();
    }

    using var scope = PriorityLoader.CreateScopeForActiveDocument();

    try
    {
      var clientFactory = scope.ServiceProvider.GetRequiredService<IClientFactory>();
      var receiveOperation = scope.ServiceProvider.GetRequiredService<GrasshopperReceiveOperation>();

      // Do the thing 👇🏼

      Account? account = input.Resource.Account.GetAccount(scope);
      if (account is null)
      {
        throw new SpeckleAccountManagerException("No default account was found");
      }

      using var client = clientFactory.Create(account);
      var receiveInfo = await input.Resource.GetReceiveInfo(client, cancellationToken).ConfigureAwait(false);

      // store version id for tracking
      _lastVersionId = receiveInfo.SelectedVersionId;

      var progress = new Progress<CardProgress>(_ =>
      {
        // TODO: Progress only makes sense in non-blocking async receive, which is not supported yet.
        // Message = $"{progress.Status}: {progress.Progress}";
      });

      // deprecated component: v3 first, on purpose. a migrated version keeps its v1 root, so preferring v3 means
      // migration never changes what an existing script outputs. only a 4.0-native version has no v1 root to read.
      Base root;
      try
      {
        root = await receiveOperation
          .ReceiveCommitObject(receiveInfo, progress, cancellationToken)
          .ConfigureAwait(false);
      }
      catch (Exception ex) when (!ex.IsFatal() && !cancellationToken.IsCancellationRequested)
      {
        ReceiveComponentOutput? fallback = await TryLoadFromArtefacts(
            scope,
            account,
            client,
            receiveInfo,
            cancellationToken
          )
          .ConfigureAwait(false);

        if (fallback is null)
        {
          throw; // no bundle either - the v3 failure is the real error
        }

        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, Constants.DEPRECATED_LOAD_FALLBACK_MESSAGE);
        return fallback;
      }

      // extract model-wide root properties (see cnx-2722)
      SpecklePropertyGroupGoo? rootPropertiesGoo = null;
      if (root is RootCollection rootCollection && rootCollection.properties.Count > 0)
      {
        rootPropertiesGoo = new SpecklePropertyGroupGoo(rootCollection.properties);
      }

      // TODO: If we have NodeRun events later, better to have `ComponentTracker` to use across components
      var customProperties = new Dictionary<string, object>
      {
        { "isAsync", false },
        { "sourceHostApp", HostApplications.GetSlugFromHostAppNameAndVersion(receiveInfo.SourceApplication) },
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

      // Setup conversion context BEFORE unpacking (which triggers DataObjectConverter)
      SpeckleConversionContext.SetupCurrent(scope);

      var rootObjectUnpacker = scope.ServiceProvider.GetService<RootObjectUnpacker>();
      var unpackedRoot = rootObjectUnpacker.Unpack(root);

      // split atomic objects from block components before conversion
      var (atomicObjects, blockInstances) = rootObjectUnpacker.SplitAtomicObjectsAndInstances(
        unpackedRoot.ObjectsToConvert
      );

      // Initialize unpackers and collection builder (data holders - created with new)
      var colorUnpacker = new GrasshopperColorUnpacker(unpackedRoot);
      var materialUnpacker = new GrasshopperMaterialUnpacker(unpackedRoot);
      var collectionRebuilder = new GrasshopperCollectionRebuilder(
        (root as Collection) ?? new Collection { name = "unnamed" }
      );

      // get handler from DI and initialize with per-operation data
      var mapHandler = scope
        .ServiceProvider.GetRequiredService<LocalToGlobalMapHandler>()
        .Initialize(
          scope.ServiceProvider.GetRequiredService<TraversalContextUnpacker>(),
          colorUnpacker,
          materialUnpacker,
          collectionRebuilder,
          unpackedRoot.DefinitionProxies
        );

      // two-pass conversion: normal objects first, then DataObjects with InstanceProxies
      mapHandler.ConvertAtomicObjects(atomicObjects);

      // process block instances (internally filters InstanceProxies belonging to registered DataObjects)
      mapHandler.ConvertBlockInstances(blockInstances);

      var goo = new SpeckleCollectionWrapperGoo(collectionRebuilder.RootCollectionWrapper);
      return new ReceiveComponentOutput
      {
        RootObject = goo,
        RootProperties = rootPropertiesGoo,
        ProxiesGoo = SpeckleCollectionWrapper.BuildProxiesGoo(root),
      };
    }
    finally
    {
      SpeckleConversionContext.EndCurrent();
    }
  }

  /// <summary>
  /// Loads a 4.0-native version (no v1 root) from its artefact bundle. Returns null when there's no bundle either, so
  /// the caller can rethrow the v3 failure. Never throws.
  /// </summary>
  /// <remarks>Leaves RootProperties and ProxiesGoo null - neither survives into the bundle. Caller warns.</remarks>
  private async Task<ReceiveComponentOutput?> TryLoadFromArtefacts(
    IServiceScope scope,
    Account account,
    IClient client,
    ReceiveInfo receiveInfo,
    CancellationToken cancellationToken
  )
  {
    try
    {
      var artifactReceiver = scope.ServiceProvider.GetService<IArtifactReceiver>();
      if (artifactReceiver is null)
      {
        return null;
      }

      var bundle = await artifactReceiver
        .TryGetBundleAsync(account, receiveInfo, new Progress<CardProgress>(_ => { }), cancellationToken)
        .ConfigureAwait(false);
      if (bundle is null)
      {
        return null;
      }

      // PerformTask's finally disposes the conversion context
      SpeckleConversionContext.SetupCurrent(scope);
      (SpeckleCollectionWrapper rootWrapper, IReadOnlyList<string> buildWarnings) =
        new GrasshopperArtefactObjectBuilder().Build(bundle, receiveInfo.ModelName);

      foreach (var warning in buildWarnings)
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, warning);
      }

      // best-effort: this endpoint 404s on some servers, don't lose an already-built receive over it
      try
      {
        await client
          .Version.Received(
            new(receiveInfo.SelectedVersionId, receiveInfo.ProjectId, receiveInfo.ReceivingApplicationSlug),
            cancellationToken
          )
          .ConfigureAwait(false);
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Could not mark the version as received ({ex.Message}).");
      }

      return new ReceiveComponentOutput { RootObject = new SpeckleCollectionWrapperGoo(rootWrapper) };
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"4.0 artefact load also failed ({ex.Message}).");
      return null;
    }
  }

  private void SetupSubscription(SpeckleUrlModelResource resource)
  {
    // skip if already subscribed to this resource
    if (_apiClient != null && _lastResource != null && _lastResource.Equals(resource))
    {
      return;
    }

    // only subscribe for Model URLs (not specific versions)
    if (resource is SpeckleUrlModelVersionResource)
    {
      CleanupSubscription();
      _lastResource = resource;
      return;
    }

    try
    {
      CleanupSubscription(); // clean up old subscription first

      using var scope = PriorityLoader.CreateScopeForActiveDocument();
      var account = resource.Account.GetAccount(scope);
      if (account == null)
      {
        return;
      }

      _apiClient = scope.Get<IClientFactory>().Create(account);
      _apiClient.Subscription.CreateProjectVersionsUpdatedSubscription(resource.ProjectId).Listeners +=
        OnVersionCreated;

      _lastResource = resource;
      Message = "Monitoring";
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Could not setup monitoring: {ex.Message}");
    }
  }

  private void OnVersionCreated(object? sender, ProjectVersionsUpdatedMessage e) =>
    // new version detected - trigger reload
    RhinoApp.InvokeOnUiThread(
      (Action)
        delegate
        {
          ExpireSolution(true);
        }
    );

  private void CleanupSubscription()
  {
    if (_apiClient != null && _lastResource != null)
    {
      try
      {
        _apiClient.Subscription.CreateProjectVersionsUpdatedSubscription(_lastResource.ProjectId).Listeners -=
          OnVersionCreated;
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        // ignore cleanup errors
      }

      _apiClient.Dispose();
      _apiClient = null;
    }
  }

  // Cleanup on removal
  public override void RemovedFromDocument(GH_Document document)
  {
    CleanupSubscription();
    base.RemovedFromDocument(document);
  }

  // Handle document context changes
  public override void DocumentContextChanged(GH_Document document, GH_DocumentContext context)
  {
    if (context == GH_DocumentContext.Unloaded)
    {
      CleanupSubscription();
    }
    else if (context == GH_DocumentContext.Loaded && _lastResource != null && _apiClient != null)
    {
      // Check for version changes when document reopens
      Task.Run(async () =>
      {
        try
        {
          var receiveInfo = await _lastResource.GetReceiveInfo(_apiClient);
          if (receiveInfo.SelectedVersionId != _lastVersionId)
          {
            RhinoApp.InvokeOnUiThread(
              (Action)
                delegate
                {
                  ExpireSolution(true);
                }
            );
          }
        }
        catch (Exception ex) when (!ex.IsFatal())
        {
          // ignore errors during background check
        }
      });
    }

    base.DocumentContextChanged(document, context);
  }
}
