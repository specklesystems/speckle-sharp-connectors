using System.Diagnostics;
using System.Runtime.InteropServices;
using Grasshopper.Kernel;
using GrasshopperAsyncComponent;
using Microsoft.Extensions.DependencyInjection;
using Rhino;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.Grasshopper8.HostApp;
using Speckle.Connectors.Grasshopper8.Parameters;
using Speckle.Converters.Common;
using Speckle.Converters.Rhino;
using Speckle.Sdk;
using Speckle.Sdk.Api;
using Speckle.Sdk.Credentials;
using Speckle.Sdk.Models.Extensions;

namespace Speckle.Connectors.Grasshopper8.Components.Operations.Send;

[Guid("52481972-7867-404F-8D9F-E1481183F355")]
public class SendAsyncComponent : GH_AsyncComponent
{
  public SendAsyncComponent()
    : base(
      "Send Async from Speckle",
      "SA",
      "Send objects async to speckle",
      ComponentCategories.PRIMARY_RIBBON,
      ComponentCategories.OPERATIONS
    )
  {
    BaseWorker = new SendComponentWorker(this);
  }

  public override Guid ComponentGuid => GetType().GUID;

  protected override Bitmap Icon => BitmapBuilder.CreateSquareIconBitmap("SA");

  public string CurrentComponentState { get; set; } = "needs_input";
  public string? Url { get; set; }
  public Client ApiClient { get; set; }
  public HostApp.SpeckleUrlModelResource? UrlModelResource { get; set; }
  public SpeckleCollectionGoo? RootCollection { get; set; }
  public SendOperation<SpeckleCollectionGoo> SendOperation { get; private set; }
  public static IServiceScope? Scope { get; set; }

  protected override void RegisterInputParams(GH_InputParamManager pManager)
  {
    pManager.AddParameter(new SpeckleUrlModelResourceParam());
    pManager.AddParameter(
      new SpeckleCollectionWrapperParam(GH_ParamAccess.item),
      "Model",
      "model",
      "The collection model object to send",
      GH_ParamAccess.item
    );
  }

  protected override void RegisterOutputParams(GH_OutputParamManager pManager)
  {
    pManager.AddParameter(new SpeckleUrlModelResourceParam());
  }

  public override void AppendAdditionalMenuItems(ToolStripDropDown menu)
  {
    static void Open(string url)
    {
      var psi = new ProcessStartInfo { FileName = url, UseShellExecute = true };
      Process.Start(psi);
    }

    base.AppendAdditionalMenuItems(menu);

    Menu_AppendSeparator(menu);
    if (Url != null)
    {
      Menu_AppendSeparator(menu);

      Menu_AppendItem(menu, $"View created version online ↗", (s, e) => Open(Url));
    }

    Menu_AppendSeparator(menu);

    if (CurrentComponentState == "publishing")
    {
      Menu_AppendItem(
        menu,
        "Cancel Publish",
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
    // Dependency Injection
    Scope = PriorityLoader.Container.CreateScope();
    SendOperation = Scope.ServiceProvider.GetRequiredService<SendOperation<SpeckleCollectionGoo>>();
    var rhinoConversionSettingsFactory = Scope.ServiceProvider.GetRequiredService<IRhinoConversionSettingsFactory>();
    Scope
      .ServiceProvider.GetRequiredService<IConverterSettingsStore<RhinoConversionSettings>>()
      .Initialize(rhinoConversionSettingsFactory.Create(RhinoDoc.ActiveDoc));

    var accountManager = Scope.ServiceProvider.GetRequiredService<AccountService>();
    var clientFactory = Scope.ServiceProvider.GetRequiredService<IClientFactory>();

    // We need to call this always in here to be able to react and set events :/
    ParseInput(da, accountManager, clientFactory);

    if (CurrentComponentState == "sending")
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

  public override void DocumentContextChanged(GH_Document document, GH_DocumentContext context)
  {
    switch (context)
    {
      case GH_DocumentContext.Loaded:
        OnDisplayExpired(true);
        break;

      case GH_DocumentContext.Unloaded:
        // Will execute every time a document becomes inactive (in background or closing file.)
        //Correctly dispose of the client when changing documents to prevent subscription handlers being called in background.
        RequestCancellation();
        break;
    }

    base.DocumentContextChanged(document, context);
  }

  private void ParseInput(IGH_DataAccess da, AccountService accountManager, IClientFactory clientFactory)
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
      Account account = accountManager.GetAccountWithServerUrlFallback("", new Uri(dataInput.Server));
      if (account is null)
      {
        throw new SpeckleAccountManagerException($"No default account was found");
      }

      ApiClient?.Dispose();
      ApiClient = clientFactory.Create(account);
    }
    catch (Exception e) when (!e.IsFatal())
    {
      AddRuntimeMessage(GH_RuntimeMessageLevel.Error, e.ToFormattedString());
    }

    SpeckleCollectionGoo rootCollection = new();
    da.GetData(1, ref rootCollection);
    if (rootCollection is null)
    {
      RootCollection = null;
      TriggerAutoSave();
      return;
    }
    RootCollection = rootCollection;
  }
}

public class SendComponentWorker : WorkerInstance
{
  public SendComponentWorker(GH_Component p)
    : base(p) { }

  public SpeckleUrlModelVersionResource? CreatedVersion { get; set; }

  private List<(GH_RuntimeMessageLevel, string)> RuntimeMessages { get; } = new();

  public override WorkerInstance Duplicate()
  {
    return new SendComponentWorker(Parent);
  }

  public override void GetData(IGH_DataAccess dataAcess, GH_ComponentParamServer p) { }

  public override void SetData(IGH_DataAccess dataAccess)
  {
    dataAccess.SetData(0, CreatedVersion);
  }

  public override void DoWork(Action<string, double> reportProgress, Action done)
  {
    var sendComponent = (SendAsyncComponent)Parent;

    try
    {
      SpeckleUrlModelResource? urlModelResource = sendComponent.UrlModelResource;
      if (urlModelResource is null)
      {
        throw new InvalidOperationException("Url Resource was null");
      }

      SpeckleCollectionGoo? rootCollection = sendComponent.RootCollection;
      if (rootCollection is null)
      {
        throw new InvalidOperationException("Root Collection was null");
      }

      var t = Task.Run(async () =>
      {
        // Step 1 - SEND TO SERVER
        var sendInfo = await urlModelResource
          .GetSendInfo(sendComponent.ApiClient, CancellationToken)
          .ConfigureAwait(false);

        var progress = new Progress<CardProgress>(_ =>
        {
          // TODO: Progress here
          // Message = $"{progress.Status}: {progress.Progress}";
        });

        var result = await sendComponent
          .SendOperation.Execute(
            new List<SpeckleCollectionGoo>() { rootCollection },
            sendInfo,
            progress,
            CancellationToken
          )
          .ConfigureAwait(false);

        // TODO: need the created version id here from the send result, not the rootobj id
        CreatedVersion = new(sendInfo.ServerUrl.ToString(), sendInfo.ProjectId, sendInfo.ModelId, result.RootObjId);
        sendComponent.Url = $"{CreatedVersion.Server}projects/{sendInfo.ProjectId}/models/{sendInfo.ModelId}"; // TODO: missing "@VersionId"

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
}
