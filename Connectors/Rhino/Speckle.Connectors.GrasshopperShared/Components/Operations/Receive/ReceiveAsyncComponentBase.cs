using GH_IO.Serialization;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using GrasshopperAsyncComponent;
using Microsoft.Extensions.DependencyInjection;
using Rhino;
using Speckle.Connectors.Common;
using Speckle.Connectors.Common.Analytics;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.Common.Operations.Receive;
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
using Speckle.Sdk.Models.Extensions;
using Speckle.Sdk.Pipelines.Progress;

namespace Speckle.Connectors.GrasshopperShared.Components.Operations.Receive;

/// <summary>
/// Shared machinery for the asynchronous Load components. Subclasses differ only in which path they prefer
/// (<see cref="PreferArtefacts"/>) and which outputs they expose (<see cref="WriteOutputs"/>).
/// </summary>
public abstract class ReceiveAsyncComponentBase : GH_AsyncComponent<ReceiveAsyncComponentBase>
{
  protected ReceiveAsyncComponentBase(
    string name,
    string nickname,
    string description,
    string category,
    string subCategory
  )
    : base(name, nickname, description, category, subCategory)
  {
    BaseWorker = new ReceiveComponentWorker(this);
  }

  public override void CreateAttributes() => m_attributes = new ReceiveAsyncComponentAttributes(this);

  /// <summary>
  /// True reads the 4.0 bundle when there is one and drops to v3 otherwise; false does the reverse. A migrated
  /// version has both, so this decides what such a version produces.
  /// </summary>
  public abstract bool PreferArtefacts { get; }

  /// <summary>Writes the worker's result to whichever outputs this variant declares.</summary>
  public abstract void WriteOutputs(IGH_DataAccess da, ReceiveComponentOutput result);

  public string InputType { get; set; }
  public bool AutoReceive { get; set; }
  public string ReceivedVersionId { get; set; }
  public ComponentState CurrentComponentState { get; set; } = ComponentState.NeedsInput;
  public bool JustPastedIn { get; set; }
  public string LastVersionDate { get; set; }
  public string LastInfoMessage { get; set; }
  public SpeckleUrlModelResource? UrlModelResource { get; set; }

  // DI props
  public IClient ApiClient { get; private set; }

  protected override void RegisterInputParams(GH_InputParamManager pManager) =>
    pManager.AddParameter(new SpeckleUrlModelResourceParam(GH_ParamAccess.item));

  protected override void SolveInstance(IGH_DataAccess da)
  {
    MultipleResources = Params.Input[0].VolatileData.HasInputCountGreaterThan(1);
    if (MultipleResources)
    {
      AddRuntimeMessage(
        GH_RuntimeMessageLevel.Error,
        "Only one model can be loaded at a time. To load to multiple models, please use different load components."
      );
      return;
    }

    da.DisableGapLogic();
    // We need to call this always in here to be able to react and set events :/
    ParseInput(da);

    if (
      (
        AutoReceive
        || CurrentComponentState == ComponentState.Ready
        || CurrentComponentState == ComponentState.Receiving
      ) && !JustPastedIn
    )
    {
      CurrentComponentState = ComponentState.Receiving;

      // Delegate control to parent async component.
      base.SolveInstance(da);
      return;
    }

    if (JustPastedIn)
    {
      // This ensures that we actually do a run. The worker will check and determine if it needs to pull an existing object or not.
      OnDisplayExpired(true);
      base.SolveInstance(da);
    }
    else
    {
      CurrentComponentState = ComponentState.Expired;
      Message = "Expired";
      OnDisplayExpired(true);
    }
  }

  public bool MultipleResources { get; set; }

  public override void AppendAdditionalMenuItems(ToolStripDropDown menu)
  {
    base.AppendAdditionalMenuItems(menu);

    Menu_AppendSeparator(menu);
    if (InputType == "Model")
    {
      var autoReceiveMi = Menu_AppendItem(
        menu,
        "Load new versions automatically",
        (s, e) =>
        {
          AutoReceive = !AutoReceive;
          RhinoApp.InvokeOnUiThread(
            (Action)
              delegate
              {
                OnDisplayExpired(true);
              }
          );
        },
        true,
        AutoReceive
      );
      autoReceiveMi.ToolTipText =
        "Toggle automatic loading. If set, any new version will be loaded instantly. This only is applicable when receiving from a model url.";
    }
    else
    {
      var autoReceiveMi = Menu_AppendItem(menu, "Automatic loading is disabled because you have specified a version.");
      autoReceiveMi.ToolTipText = "To enable automatic loading, select a model without selecting a specific version.";
    }

    Menu_AppendSeparator(menu);

    if (CurrentComponentState == ComponentState.Receiving)
    {
      Menu_AppendItem(
        menu,
        "Cancel Load",
        (s, e) =>
        {
          CurrentComponentState = ComponentState.Expired;
          RequestCancellation();
        }
      );
    }
  }

  private void HandleNewCommit()
  {
    Message = "Expired";
    CurrentComponentState = ComponentState.Expired;
    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"There is a newer version available for this {InputType}");
    RhinoApp.InvokeOnUiThread(
      (Action)
        delegate
        {
          if (AutoReceive)
          {
            ExpireSolution(true);
          }
          else
          {
            OnDisplayExpired(true);
          }
        }
    );
  }

  public override void RemovedFromDocument(GH_Document document)
  {
    RequestCancellation();
    base.RemovedFromDocument(document);
  }

  public override void DocumentContextChanged(GH_Document document, GH_DocumentContext context)
  {
    switch (context)
    {
      case GH_DocumentContext.Loaded:
      {
        // Will execute every time a document becomes active (from background or opening file.).
        if (UrlModelResource != null)
        {
          Task.Run(async () =>
          {
            // Ensure fresh instance of client.
            ResetApiClient(UrlModelResource);

            // Get last commit from the branch
            var b = UrlModelResource.GetReceiveInfo(ApiClient);
            await b;

            // Compare version ids. If they don't match, notify user or fetch data if in auto mode
            if (b.Result.SelectedVersionId != ReceivedVersionId)
            {
              HandleNewCommit();
            }

            OnDisplayExpired(true);
          });
        }

        break;
      }
      case GH_DocumentContext.Unloaded:
        // Will execute every time a document becomes inactive (in background or closing file.)
        // Correctly dispose of the client when changing documents to prevent subscription handlers being called in background.
        CurrentComponentState = ComponentState.Expired;
        RequestCancellation();
        ApiClient?.Dispose();
        break;
    }

    base.DocumentContextChanged(document, context);
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

    // set the type of url input
    switch (dataInput)
    {
      case SpeckleUrlModelVersionResource:
        InputType = "Version";
        AutoReceive = false;
        LastInfoMessage = "";
        ResetApiClient(dataInput);
        break;
      case SpeckleUrlModelResource:
        InputType = "Model";
        // handled in do work
        break;
      default:
        InputType = "Invalid";
        break;
    }

    if (UrlModelResource != null && UrlModelResource.Equals(dataInput) && !JustPastedIn)
    {
      return;
    }

    UrlModelResource = dataInput;

    ResetApiClient(UrlModelResource);
  }

  private void ApiClient_OnVersionCreated(object? sender, ProjectVersionsUpdatedMessage e)
  {
    HandleNewCommit();
  }

  public void ResetApiClient(SpeckleUrlModelResource urlResource)
  {
    try
    {
      using var scope = PriorityLoader.CreateScopeForActiveDocument();
      Account? account = urlResource.Account.GetAccount(scope);
      if (account is null)
      {
        throw new SpeckleAccountManagerException("No default account was found");
      }

      ApiClient?.Dispose();
      ApiClient = scope.Get<IClientFactory>().Create(account);
      ApiClient.Subscription.CreateProjectVersionsUpdatedSubscription(urlResource.ProjectId).Listeners +=
        ApiClient_OnVersionCreated;
    }
    catch (Exception e) when (!e.IsFatal())
    {
      AddRuntimeMessage(GH_RuntimeMessageLevel.Error, e.ToFormattedString());
    }
  }

  public override bool Write(GH_IWriter writer)
  {
    // call base implementation first
    var result = base.Write(writer);

    // persist AutoReceive setting
    writer.SetBoolean("AutoReceive", AutoReceive);

    return result;
  }

  public override bool Read(GH_IReader reader)
  {
    // call base implementation first
    var result = base.Read(reader);

    // restore AutoReceive setting
    bool autoReceive = false;
    if (reader.TryGetBoolean("AutoReceive", ref autoReceive))
    {
      AutoReceive = autoReceive;
    }

    return result;
  }
}

public sealed class ReceiveComponentWorker : WorkerInstance<ReceiveAsyncComponentBase>
{
  public ReceiveComponentWorker(
    ReceiveAsyncComponentBase p,
    string id = "baseWorker",
    CancellationToken cancellationToken = default
  )
    : base(p, id, cancellationToken) { }

  public Base Root { get; set; }
  public SpeckleUrlModelResource? UrlModelResource { get; set; }

  /// <summary>What the receive produced. Which parts reach the canvas is up to <see cref="ReceiveAsyncComponentBase.WriteOutputs"/>.</summary>
  public ReceiveComponentOutput? Output { get; private set; }

  private List<(GH_RuntimeMessageLevel, string)> RuntimeMessages { get; } = new();

  public override WorkerInstance<ReceiveAsyncComponentBase> Duplicate(string id, CancellationToken cancellationToken)
  {
    return new ReceiveComponentWorker(Parent, id, cancellationToken);
  }

  public override void GetData(IGH_DataAccess da, GH_ComponentParamServer p)
  {
    UrlModelResource = Parent.UrlModelResource;
  }

  public override void SetData(IGH_DataAccess da)
  {
    if (CancellationToken.IsCancellationRequested)
    {
      return;
    }

    foreach (var (level, message) in RuntimeMessages)
    {
      Parent.AddRuntimeMessage(level, message);
    }

    var parent = Parent;

    parent.CurrentComponentState = ComponentState.UpToDate;

    parent.JustPastedIn = false;

    if (Output?.RootObject == null)
    {
      return;
    }

    parent.WriteOutputs(da, Output);
  }

  public override async Task DoWork(Action<string, double> reportProgress, Action done)
  {
    try
    {
      await Receive(reportProgress);
      done();
    }
    catch (OperationCanceledException) when (CancellationToken.IsCancellationRequested)
    {
      RuntimeMessages.Add((GH_RuntimeMessageLevel.Remark, "Operation cancelled"));
      Parent.CurrentComponentState = ComponentState.Expired;
      //No need to call `done()` - GrasshopperAsyncComponent assumes immediate cancel,
      //thus it has already performed clean-up actions that would normally be done on `done()`
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      RuntimeMessages.Add((GH_RuntimeMessageLevel.Error, ex.ToFormattedString()));
      done();
    }
  }

#pragma warning disable CA1506
  private async Task Receive(Action<string, double> reportProgress)
#pragma warning restore CA1506
  {
    if (UrlModelResource is null)
    {
      throw new InvalidOperationException("Model Resource was null");
    }

    // Means it's a copy paste of an empty non-init component; set the record and exit fast.
    if (Parent.JustPastedIn && !Parent.AutoReceive)
    {
      Parent.JustPastedIn = false;
      return;
    }

    Parent.CurrentComponentState = ComponentState.Receiving;
    RhinoApp.InvokeOnUiThread(
      (Action)
        delegate
        {
          Parent.OnDisplayExpired(true);
        }
    );

    // Step 1 - RECEIVE FROM SERVER
    var receiveInfo = await UrlModelResource.GetReceiveInfo(Parent.ApiClient, CancellationToken).ConfigureAwait(false);

    var progress = new Progress<CardProgress>(p =>
    {
      reportProgress(Id, p.Progress ?? 0);
      //eceiveComponent.Message = $"{p.Status}";
    });

    CancellationToken.ThrowIfCancellationRequested();

    if (receiveInfo == null)
    {
      return;
    }

    using var scope = PriorityLoader.CreateScopeForActiveDocument();

    try
    {
      if (Parent.PreferArtefacts)
      {
        if (await TryLoadFromArtefacts(scope, receiveInfo).ConfigureAwait(false))
        {
          return;
        }

        // unmigrated v3 - a Remark, not an error: erroring here would make this component unusable while most
        // versions are still unmigrated, and push people back to the deprecated one
        RuntimeMessages.Add((GH_RuntimeMessageLevel.Remark, Constants.LEGACY_FALLBACK_MESSAGE));
        Root = await scope
          .Get<GrasshopperReceiveOperation>()
          .ReceiveCommitObject(receiveInfo, progress, CancellationToken)
          .ConfigureAwait(false);
      }
      else
      {
        // v3 first, on purpose. a migrated version keeps its v1 root, so preferring v3 means migration never changes
        // what an existing script outputs. only a 4.0-native version has no v1 root to read.
        try
        {
          Root = await scope
            .Get<GrasshopperReceiveOperation>()
            .ReceiveCommitObject(receiveInfo, progress, CancellationToken)
            .ConfigureAwait(false);
        }
        catch (Exception ex) when (!ex.IsFatal() && !CancellationToken.IsCancellationRequested)
        {
          if (!await TryLoadFromArtefacts(scope, receiveInfo).ConfigureAwait(false))
          {
            throw; // no bundle either - the v3 failure is the real error
          }

          RuntimeMessages.Add((GH_RuntimeMessageLevel.Warning, Constants.DEPRECATED_LOAD_FALLBACK_MESSAGE));
          return;
        }
      }

      CancellationToken.ThrowIfCancellationRequested();

      SpecklePropertyGroupGoo? rootPropertiesGoo = null;
      if (Root is RootCollection rootCollection && rootCollection.properties.Count > 0)
      {
        rootPropertiesGoo = new SpecklePropertyGroupGoo(rootCollection.properties);
      }

      // Step 2 - CONVERT
      //receiveComponent.Message = $"Unpacking...";
      SpeckleConversionContext.SetupCurrent(scope);

      var unpackedRoot = scope.Get<RootObjectUnpacker>().Unpack(Root);

      // separate atomic objects from block instances
      var (atomicObjects, blockInstances) = scope
        .Get<RootObjectUnpacker>()
        .SplitAtomicObjectsAndInstances(unpackedRoot.ObjectsToConvert);

      // initialize unpackers and collection builder (data holders - created with new)
      var colorUnpacker = new GrasshopperColorUnpacker(unpackedRoot);
      var materialUnpacker = new GrasshopperMaterialUnpacker(unpackedRoot);
      var collectionRebuilder = new GrasshopperCollectionRebuilder(
        (Root as Collection) ?? new Collection { name = "unnamed" }
      );

      // get handler from DI and initialize with per-operation data
      var mapHandler = scope
        .Get<LocalToGlobalMapHandler>()
        .Initialize(
          scope.Get<TraversalContextUnpacker>(),
          colorUnpacker,
          materialUnpacker,
          collectionRebuilder,
          unpackedRoot.DefinitionProxies
        );

      // handler deals with two-pass conversion: normal objects first, then DataObjects with InstanceProxies
      mapHandler.ConvertAtomicObjects(atomicObjects);

      // process block instances using converted atomic objects
      // internally filters out InstanceProxies that belong to registered DataObjects
      // block processing needs converted objects, but object filtering needs block definitions.
      mapHandler.ConvertBlockInstances(blockInstances);

      // no bundle here, so no object index - project/version id still identify the model
      collectionRebuilder.RootCollectionWrapper.SetModelContext(
        new SpeckleModelContext(receiveInfo.ProjectId, receiveInfo.SelectedVersionId)
      );

      Output = new ReceiveComponentOutput
      {
        RootObject = new SpeckleCollectionWrapperGoo(collectionRebuilder.RootCollectionWrapper),
        RootProperties = rootPropertiesGoo,
        ProxiesGoo = SpeckleCollectionWrapper.BuildProxiesGoo(Root),
      };

      // TODO: If we have NodeRun events later, better to have `ComponentTracker` to use across components
      var customProperties = new Dictionary<string, object>()
      {
        { "isAsync", true },
        { "sourceHostApp", HostApplications.GetSlugFromHostAppNameAndVersion(receiveInfo.SourceApplication) },
        { "auto", Parent.AutoReceive },
      };
      if (receiveInfo.WorkspaceId != null)
      {
        customProperties.Add("workspace_id", receiveInfo.WorkspaceId);
      }

      if (receiveInfo.SelectedVersionUserId != null)
      {
        customProperties.Add(
          "isMultiplayer",
          receiveInfo.SelectedVersionUserId != Parent.ApiClient.Account.userInfo.id
        );
      }

      await scope
        .Get<IMixPanelManager>()
        .TrackEvent(MixPanelEvents.Receive, Parent.ApiClient.Account, customProperties);
    }
    finally
    {
      SpeckleConversionContext.EndCurrent();
    }
  }

  /// <summary>
  /// Builds the wrapper tree from the version's 4.0 bundle into <see cref="Output"/>, or returns false when it has
  /// none. Never throws.
  /// </summary>
  /// <remarks>Leaves RootProperties and ProxiesGoo null - neither survives into the bundle.</remarks>
  private async Task<bool> TryLoadFromArtefacts(IServiceScope scope, ReceiveInfo receiveInfo)
  {
    try
    {
      var artifactReceiver = scope.ServiceProvider.GetService<IArtifactReceiver>();
      if (artifactReceiver is null)
      {
        return false;
      }

      var bundle = await artifactReceiver
        .TryGetBundleAsync(
          Parent.ApiClient.Account,
          receiveInfo,
          new Progress<CardProgress>(_ => { }),
          CancellationToken
        )
        .ConfigureAwait(false);
      if (bundle is null)
      {
        return false;
      }

      // Receive's finally disposes the conversion context
      SpeckleConversionContext.SetupCurrent(scope);
      (SpeckleCollectionWrapper rootWrapper, IReadOnlyList<string> buildWarnings) =
        new GrasshopperArtefactObjectBuilder().Build(
          bundle,
          receiveInfo.ModelName,
          new SpeckleModelContext(receiveInfo.ProjectId, receiveInfo.SelectedVersionId),
          // only the deprecated variant reaches here as a fallback, and its scripts expect DataObjects
          groupAsDataObjects: !Parent.PreferArtefacts
        );

      foreach (var warning in buildWarnings)
      {
        RuntimeMessages.Add((GH_RuntimeMessageLevel.Warning, warning));
      }

      // best-effort: this endpoint 404s on some servers, don't lose an already-built receive over it
      try
      {
        await Parent
          .ApiClient.Version.Received(
            new(receiveInfo.SelectedVersionId, receiveInfo.ProjectId, receiveInfo.ReceivingApplicationSlug),
            CancellationToken
          )
          .ConfigureAwait(false);
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        RuntimeMessages.Add(
          (GH_RuntimeMessageLevel.Warning, $"Could not mark the version as received ({ex.Message}).")
        );
      }

      Output = new ReceiveComponentOutput
      {
        RootObject = new SpeckleCollectionWrapperGoo(rootWrapper),
      };
      return true;
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      RuntimeMessages.Add((GH_RuntimeMessageLevel.Warning, $"4.0 artefact load also failed ({ex.Message})."));
      return false;
    }
  }
}

public class ReceiveAsyncComponentAttributes : GH_ComponentAttributes
{
  public ReceiveAsyncComponentAttributes(GH_Component owner)
    : base(owner) { }

  private Rectangle ButtonBounds { get; set; }

  protected override void Layout()
  {
    base.Layout();

    var baseRec = GH_Convert.ToRectangle(Bounds);
    baseRec.Height += 26;

    var btnRec = baseRec;
    btnRec.Y = btnRec.Bottom - 26;
    btnRec.Height = 26;
    btnRec.Inflate(-2, -2);

    Bounds = baseRec;
    ButtonBounds = btnRec;
  }

  protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
  {
    base.Render(canvas, graphics, channel);

    var state = ((ReceiveAsyncComponentBase)Owner).CurrentComponentState;

    if (channel == GH_CanvasChannel.Objects)
    {
      if (((ReceiveAsyncComponentBase)Owner).AutoReceive)
      {
        var autoReceiveButton = GH_Capsule.CreateTextCapsule(
          ButtonBounds,
          ButtonBounds,
          GH_Palette.Blue,
          "Auto Load",
          2,
          0
        );

        autoReceiveButton.Render(graphics, Selected, Owner.Locked, false);
        autoReceiveButton.Dispose();
      }
      else
      {
        var palette =
          state == ComponentState.Expired || state == ComponentState.UpToDate
            ? GH_Palette.Black
            : GH_Palette.Transparent;
        var text = state != ComponentState.Receiving ? "Load" : "Loading...";

        var button = GH_Capsule.CreateTextCapsule(
          ButtonBounds,
          ButtonBounds,
          palette,
          text,
          2,
          state == ComponentState.Expired ? 10 : 0
        );
        button.Render(graphics, Selected, Owner.Locked, false);
        button.Dispose();
      }
    }
  }

  public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
  {
    if (e.Button != MouseButtons.Left)
    {
      return base.RespondToMouseDown(sender, e);
    }

    if (!((RectangleF)ButtonBounds).Contains(e.CanvasLocation))
    {
      return base.RespondToMouseDown(sender, e);
    }

    if (((ReceiveAsyncComponentBase)Owner).CurrentComponentState == ComponentState.Receiving)
    {
      return GH_ObjectResponse.Handled;
    }

    // NOTE: why do we kill auto receive when clicking on the button?
    // It's enabled via the context menu, I expect it to be disabled from the same place
    // if (((ReceiveAsyncComponentBase)Owner).AutoReceive)
    // {
    //   ((ReceiveAsyncComponentBase)Owner).AutoReceive = false;
    //   Owner.OnDisplayExpired(true);
    //   return GH_ObjectResponse.Handled;
    // }

    // TODO: check if owner has null account/client, and call the reset thing SYNC
    if ((Owner as ReceiveAsyncComponentBase)?.MultipleResources == true)
    {
      return GH_ObjectResponse.Handled;
    }

    ((ReceiveAsyncComponentBase)Owner).CurrentComponentState = ComponentState.Ready;
    Owner.ExpireSolution(true);
    return GH_ObjectResponse.Handled;
  }
}
