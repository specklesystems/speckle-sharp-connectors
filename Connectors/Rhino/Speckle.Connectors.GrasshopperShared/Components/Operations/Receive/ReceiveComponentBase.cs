using System.Diagnostics.CodeAnalysis;
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

/// <summary>
/// Shared machinery for the synchronous Load components. Subclasses differ only in which path they prefer
/// (<see cref="PreferArtefacts"/>) and which outputs they expose.
/// </summary>
public abstract class ReceiveComponentBase(
  string name,
  string nickname,
  string description,
  string category,
  string subCategory
)
  : SpeckleTaskCapableComponent<ReceiveComponentInput, ReceiveComponentOutput>(
    name,
    nickname,
    description,
    category,
    subCategory
  )
{
  private IClient? _apiClient;
  private string? _lastVersionId;
  private SpeckleUrlModelResource? _lastResource;

  /// <summary>
  /// True reads the 4.0 bundle when there is one and drops to v3 otherwise; false does the reverse. A migrated
  /// version has both, so this decides what such a version produces.
  /// </summary>
  protected abstract bool PreferArtefacts { get; }

  protected override void RegisterInputParams(GH_InputParamManager pManager)
  {
    pManager.AddParameter(new SpeckleUrlModelResourceParam(GH_ParamAccess.item));
    pManager.AddBooleanParameter("Run", "r", "Run the load operation", GH_ParamAccess.item);
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

  /// <summary>Sets the canvas message once outputs are written. Shared by both variants.</summary>
  protected void SetStatusMessage(bool loaded)
  {
    if (loaded)
    {
      Message = _apiClient != null ? "Loaded" : "Done";
    }
    else
    {
      Message = _apiClient != null ? "Monitoring" : "Not Loaded";
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
      });

      Base root;
      if (PreferArtefacts)
      {
        var artefactResult = await TryLoadFromArtefacts(scope, account, client, receiveInfo, cancellationToken)
          .ConfigureAwait(false);
        if (artefactResult is not null)
        {
          return artefactResult;
        }

        // unmigrated v3 - a Remark, not an error: erroring here would make this component unusable while most
        // versions are still unmigrated, and push people back to the deprecated one
        AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, Constants.LEGACY_FALLBACK_MESSAGE);
        root = await receiveOperation
          .ReceiveCommitObject(receiveInfo, progress, cancellationToken)
          .ConfigureAwait(false);
      }
      else
      {
        // v3 first, on purpose. a migrated version keeps its v1 root, so preferring v3 means migration never changes
        // what an existing script outputs. only a 4.0-native version has no v1 root to read.
        try
        {
          root = await receiveOperation
            .ReceiveCommitObject(receiveInfo, progress, cancellationToken)
            .ConfigureAwait(false);
        }
        catch (Exception ex) when (!ex.IsFatal() && !cancellationToken.IsCancellationRequested)
        {
          var fallback = await TryLoadFromArtefacts(scope, account, client, receiveInfo, cancellationToken)
            .ConfigureAwait(false);

          if (fallback is null)
          {
            throw; // no bundle either - the v3 failure is the real error
          }

          AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, Constants.DEPRECATED_LOAD_FALLBACK_MESSAGE);
          return fallback;
        }
      }

      return await BuildFromLegacyGraph(scope, client, account, receiveInfo, root).ConfigureAwait(false);
    }
    finally
    {
      SpeckleConversionContext.EndCurrent();
    }
  }

  /// <summary>Unpacks and converts a v3 <see cref="Base"/> graph into the canvas wrapper tree.</summary>
  [SuppressMessage("Maintainability", "CA1506:Avoid excessive class coupling")]
  private async Task<ReceiveComponentOutput> BuildFromLegacyGraph(
    IServiceScope scope,
    IClient client,
    Account account,
    GrasshopperReceiveInfo receiveInfo,
    Base root
  )
  {
    // extract model-wide root properties (see cnx-2722)
    SpecklePropertyGroupGoo? rootPropertiesGoo = null;
    if (root is RootCollection rootCollection && rootCollection.properties.Count > 0)
    {
      rootPropertiesGoo = new SpecklePropertyGroupGoo(rootCollection.properties);
    }

    await TrackReceive(client, account, receiveInfo).ConfigureAwait(false);

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

    // no bundle here, so no object index - project/version id still identify the model
    collectionRebuilder.RootCollectionWrapper.SetModelContext(
      new SpeckleModelContext(receiveInfo.ProjectId, receiveInfo.SelectedVersionId)
    );

    return new ReceiveComponentOutput
    {
      RootObject = new SpeckleCollectionWrapperGoo(collectionRebuilder.RootCollectionWrapper),
      RootProperties = rootPropertiesGoo,
      ProxiesGoo = SpeckleCollectionWrapper.BuildProxiesGoo(root),
    };
  }

  private static async Task TrackReceive(IClient client, Account account, GrasshopperReceiveInfo receiveInfo)
  {
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
  }

  /// <summary>
  /// Builds the wrapper tree from the version's 4.0 bundle, or null when it has none. Never throws - an artefact-side
  /// failure is surfaced as a warning and reported as "no bundle" so the caller decides what to do.
  /// </summary>
  /// <remarks>Leaves RootProperties and ProxiesGoo null - neither survives into the bundle.</remarks>
  protected async Task<ReceiveComponentOutput?> TryLoadFromArtefacts(
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
        new GrasshopperArtefactObjectBuilder().Build(
          bundle,
          receiveInfo.ModelName,
          new SpeckleModelContext(receiveInfo.ProjectId, receiveInfo.SelectedVersionId),
          // only the deprecated variant reaches here as a fallback, and its scripts expect DataObjects
          groupAsDataObjects: !PreferArtefacts
        );

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
