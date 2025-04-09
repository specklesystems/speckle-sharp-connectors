using System.Runtime.InteropServices;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using GrasshopperAsyncComponent;
using Microsoft.Extensions.DependencyInjection;
using Rhino;
using Speckle.Connectors.Common.Instances;
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

namespace Speckle.Connectors.GrasshopperShared.Components.Operations.Receive;

[Guid("1587DF34-83E5-4AFE-B42E-F7C5C37ECD68")]
public class ReceiveAsyncComponent : GH_AsyncComponent
{
  public ReceiveAsyncComponent()
    : base("Load", "L", "Load a model from Speckle", ComponentCategories.PRIMARY_RIBBON, ComponentCategories.OPERATIONS)
  {
    BaseWorker = new ReceiveComponentWorker(this);
    Attributes = new ReceiveAsyncComponentAttributes(this);
  }

  public override Guid ComponentGuid => GetType().GUID;

  protected override Bitmap Icon => BitmapBuilder.CreateSquareIconBitmap("L");

  public string InputType { get; set; }
  public bool AutoReceive { get; set; }
  public bool ReceiveOnOpen { get; set; }
  public string ReceivedVersionId { get; set; }
  public ComponentState CurrentComponentState { get; set; } = ComponentState.NeedsInput;
  public bool JustPastedIn { get; set; }
  public string LastVersionDate { get; set; }
  public string LastInfoMessage { get; set; }
  public HostApp.SpeckleUrlModelResource? UrlModelResource { get; set; }

  // DI props
  public Client ApiClient { get; private set; }
  public GrasshopperReceiveOperation ReceiveOperation { get; private set; }
  public RootObjectUnpacker RootObjectUnpacker { get; private set; }
  public static IServiceScope? Scope { get; private set; }
  public AccountService AccountManager { get; private set; }
  public IClientFactory ClientFactory { get; private set; }

  protected override void RegisterInputParams(GH_InputParamManager pManager)
  {
    pManager.AddParameter(new SpeckleUrlModelResourceParam(GH_ParamAccess.item));
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

  protected override void SolveInstance(IGH_DataAccess da)
  {
    da.DisableGapLogic();

    // Dependency Injection
    Scope = PriorityLoader.Container.CreateScope();
    ReceiveOperation = Scope.ServiceProvider.GetRequiredService<GrasshopperReceiveOperation>();
    RootObjectUnpacker = Scope.ServiceProvider.GetService<RootObjectUnpacker>();
    AccountManager = Scope.ServiceProvider.GetRequiredService<AccountService>();
    ClientFactory = Scope.ServiceProvider.GetRequiredService<IClientFactory>();

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

  public override void AppendAdditionalMenuItems(ToolStripDropDown menu)
  {
    base.AppendAdditionalMenuItems(menu);

    Menu_AppendSeparator(menu);
    if (InputType == "Model")
    {
      var autoReceiveMi = Menu_AppendItem(
        menu,
        "Load automatically",
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

    var receivOnOpenMi = Menu_AppendItem(
      menu,
      "Load when Document opened",
      (sender, args) =>
      {
        ReceiveOnOpen = !ReceiveOnOpen;
        RhinoApp.InvokeOnUiThread(
          (Action)
            delegate
            {
              OnDisplayExpired(true);
            }
        );
      },
      !AutoReceive,
      AutoReceive || ReceiveOnOpen
    );
    receivOnOpenMi.ToolTipText =
      "The node will automatically perform a load operation as soon as the document is open, or the node is copy/pasted into a new document.";

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
    Scope?.Dispose();
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
        return;
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

  private void ApiClient_OnVersionCreated(object sender, ProjectVersionsUpdatedMessage e)
  {
    HandleNewCommit();
  }

  public void ResetApiClient(SpeckleUrlModelResource urlResource)
  {
    try
    {
      // TODO: Get any account for this server, as we don't have a mechanism yet to pass accountIds through
      Account account = AccountManager.GetAccountWithServerUrlFallback("", new Uri(urlResource.Server));
      if (account is null)
      {
        throw new SpeckleAccountManagerException($"No default account was found");
      }

      ApiClient?.Dispose();
      ApiClient = ClientFactory.Create(account);
      ApiClient.Subscription.CreateProjectVersionsUpdatedSubscription(urlResource.ProjectId).Listeners +=
        ApiClient_OnVersionCreated;
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

  public Base Root { get; set; }
  public SpeckleUrlModelResource? UrlModelResource { get; set; }
  public SpeckleCollectionWrapperGoo Result { get; set; }
  private List<(GH_RuntimeMessageLevel, string)> RuntimeMessages { get; } = new();

  public override WorkerInstance Duplicate()
  {
    return new ReceiveComponentWorker(Parent);
  }

  public override void GetData(IGH_DataAccess da, GH_ComponentParamServer p)
  {
    UrlModelResource = ((ReceiveAsyncComponent)Parent).UrlModelResource;
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

    var parent = (ReceiveAsyncComponent)Parent;

    parent.CurrentComponentState = ComponentState.UpToDate;

    parent.JustPastedIn = false;

    if (Result == null)
    {
      return;
    }

    da.SetData(0, Result);
  }

#pragma warning disable CA1506
  public override void DoWork(Action<string, double> reportProgress, Action done)
  {
    var receiveComponent = (ReceiveAsyncComponent)Parent;

    try
    {
      if (UrlModelResource is null)
      {
        throw new InvalidOperationException("Model Resource was null");
      }

      // Means it's a copy paste of an empty non-init component; set the record and exit fast unless ReceiveOnOpen is true.
      if (receiveComponent.JustPastedIn && !receiveComponent.AutoReceive)
      {
        receiveComponent.JustPastedIn = false;
        if (!receiveComponent.ReceiveOnOpen)
        {
          return;
        }

        receiveComponent.CurrentComponentState = ComponentState.Receiving;
        RhinoApp.InvokeOnUiThread(
          (Action)
            delegate
            {
              receiveComponent.OnDisplayExpired(true);
            }
        );
      }

      var t = Task.Run(async () =>
      {
        // Step 1 - RECEIVE FROM SERVER
        var receiveInfo = await UrlModelResource
          .GetReceiveInfo(receiveComponent.ApiClient, CancellationToken)
          .ConfigureAwait(false);

        var progress = new Progress<CardProgress>(p =>
        {
          reportProgress(Id, p.Progress ?? 0);
          //eceiveComponent.Message = $"{p.Status}";
        });

        if (CancellationToken.IsCancellationRequested)
        {
          return;
        }

        if (receiveInfo == null)
        {
          done();
          return;
        }

        Root = await receiveComponent
          .ReceiveOperation.ReceiveCommitObject(receiveInfo, progress, CancellationToken)
          .ConfigureAwait(false);

        if (CancellationToken.IsCancellationRequested)
        {
          return;
        }

        // Step 2 - CONVERT
        //receiveComponent.Message = $"Unpacking...";
        LocalToGlobalUnpacker localToGlobalUnpacker = new();
        TraversalContextUnpacker traversalContextUnpacker = new();
        var unpackedRoot = receiveComponent.RootObjectUnpacker.Unpack(Root);

        // "flatten" block instances
        var localToGlobalMaps = localToGlobalUnpacker.Unpack(
          unpackedRoot.DefinitionProxies,
          unpackedRoot.ObjectsToConvert.ToList()
        );

        // TODO: unpack colors and render materials
        GrasshopperColorBaker colorBaker = new(unpackedRoot);

        GrasshopperCollectionRebuilder collectionRebuilder =
          new((Root as Collection) ?? new Collection() { name = "unnamed" });

        LocalToGlobalMapHandler mapHandler = new(traversalContextUnpacker, collectionRebuilder, colorBaker);

        int count = 0;
        int total = localToGlobalMaps.Count;

        foreach (var map in localToGlobalMaps)
        {
          mapHandler.CreateGrasshopperObjectFromMap(map);
          count++;
        }

        Result = new SpeckleCollectionWrapperGoo(collectionRebuilder.RootCollectionWrapper);

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
}

public class ReceiveAsyncComponentAttributes : GH_ComponentAttributes
{
  private bool _selected;

  public ReceiveAsyncComponentAttributes(GH_Component owner)
    : base(owner) { }

  private Rectangle ButtonBounds { get; set; }

  public override bool Selected
  {
    get => _selected;
    set => _selected = value;
  }

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

    var state = ((ReceiveAsyncComponent)Owner).CurrentComponentState;

    if (channel == GH_CanvasChannel.Objects)
    {
      if (((ReceiveAsyncComponent)Owner).AutoReceive)
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
          state == ComponentState.Expired || state == ComponentState.UpToDate || state == ComponentState.Cancelled
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

    if (((ReceiveAsyncComponent)Owner).CurrentComponentState == ComponentState.Receiving)
    {
      return GH_ObjectResponse.Handled;
    }

    if (((ReceiveAsyncComponent)Owner).AutoReceive)
    {
      ((ReceiveAsyncComponent)Owner).AutoReceive = false;
      Owner.OnDisplayExpired(true);
      return GH_ObjectResponse.Handled;
    }

    // TODO: check if owner has null account/client, and call the reset thing SYNC
    ((ReceiveAsyncComponent)Owner).CurrentComponentState = ComponentState.Ready;
    Owner.ExpireSolution(true);
    return GH_ObjectResponse.Handled;
  }
}
