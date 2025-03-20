using System.Diagnostics;
using Grasshopper.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Rhino;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.Grasshopper8.Components.BaseComponents;
using Speckle.Connectors.Grasshopper8.HostApp;
using Speckle.Connectors.Grasshopper8.Parameters;
using Speckle.Converters.Common;
using Speckle.Converters.Rhino;
using Speckle.Sdk;
using Speckle.Sdk.Api;
using Speckle.Sdk.Common;
using Speckle.Sdk.Credentials;

namespace Speckle.Connectors.Grasshopper8.Components.Operations.Send;

public class SendComponentInput
{
  public SpeckleUrlModelResource Resource { get; }
  public SpeckleCollectionGoo Input { get; }

  public SendComponentInput(SpeckleUrlModelResource resource, SpeckleCollectionGoo input)
  {
    Resource = resource;
    Input = input;
  }
}

public class SendComponentOutput(SpeckleUrlModelResource resource)
{
  public SpeckleUrlModelResource Resource { get; } = resource;
}

public class SendComponent : SpeckleScopedTaskCapableComponent<SendComponentInput, SendComponentOutput>
{
  public SendComponent()
    : base("Send from Speckle", "SFS", "Send objects to speckle", "Speckle", "Operations") { }

  public override Guid ComponentGuid => new("0CF0D173-BDF0-4AC2-9157-02822B90E9FB");

  public string? Url { get; private set; }

  protected override Bitmap Icon => BitmapBuilder.CreateSquareIconBitmap("S");

  protected override void RegisterInputParams(GH_InputParamManager pManager)
  {
    pManager.AddParameter(new SpeckleUrlModelResourceParam());
    pManager.AddParameter(
      new SpeckleCollectionParam(GH_ParamAccess.item),
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

  protected override SendComponentInput GetInput(IGH_DataAccess da)
  {
    if (da.Iteration != 0)
    {
      throw new SpeckleException("No more than 1 resource allowed");
    }

    SpeckleUrlModelResource? resource = null;
    if (!da.GetData(0, ref resource))
    {
      throw new SpeckleException("Failed to get resource");
    }

    SpeckleCollectionGoo rootCollection = new();
    da.GetData(1, ref rootCollection);

    return new SendComponentInput(resource.NotNull(), rootCollection);
  }

  protected override void SetOutput(IGH_DataAccess da, SendComponentOutput result)
  {
    da.SetData(0, result.Resource);
  }

  public override void AppendAdditionalMenuItems(ToolStripDropDown menu)
  {
    base.AppendAdditionalMenuItems(menu);

    Menu_AppendSeparator(menu);
    if (Url != null)
    {
      Menu_AppendSeparator(menu);

      Menu_AppendItem(menu, $"View created version online ↗", (s, e) => Open(Url));
    }

    static void Open(string url)
    {
      var psi = new ProcessStartInfo { FileName = url, UseShellExecute = true };
      Process.Start(psi);
    }
  }

  protected override async Task<SendComponentOutput> PerformScopedTask(
    SendComponentInput input,
    IServiceScope scope,
    CancellationToken cancellationToken = default
  )
  {
    var rhinoConversionSettingsFactory = scope.ServiceProvider.GetRequiredService<IRhinoConversionSettingsFactory>();
    scope
      .ServiceProvider.GetRequiredService<IConverterSettingsStore<RhinoConversionSettings>>()
      .Initialize(rhinoConversionSettingsFactory.Create(RhinoDoc.ActiveDoc));

    var accountManager = scope.ServiceProvider.GetRequiredService<AccountService>();
    var clientFactory = scope.ServiceProvider.GetRequiredService<IClientFactory>();
    var sendOperation = scope.ServiceProvider.GetRequiredService<SendOperation<SpeckleCollectionGoo>>();

    // TODO: Get any account for this server, as we don't have a mechanism yet to pass accountIds through
    var account = accountManager.GetAccountWithServerUrlFallback("", new Uri(input.Resource.Server));

    if (account is null)
    {
      throw new SpeckleAccountManagerException($"No default account was found");
    }

    var progress = new Progress<CardProgress>(_ =>
    {
      // TODO: Progress only makes sense in non-blocking async receive, which is not supported yet.
      // Message = $"{progress.Status}: {progress.Progress}";
    });

    using var client = clientFactory.Create(account);
    var sendInfo = await input.Resource.GetSendInfo(client, cancellationToken).ConfigureAwait(false);
    var result = await sendOperation
      .Execute(new List<SpeckleCollectionGoo>() { input.Input }, sendInfo, progress, cancellationToken)
      .ConfigureAwait(false);

    // TODO: need the created version id here from the send result, not the rootobj id
    SpeckleUrlModelVersionResource createdVersionResource =
      new(sendInfo.ServerUrl.ToString(), sendInfo.ProjectId, sendInfo.ModelId, result.RootObjId);
    Url = $"{createdVersionResource.Server}projects/{sendInfo.ProjectId}/models/{sendInfo.ModelId}"; // TODO: missing "@VersionId"
    return new SendComponentOutput(createdVersionResource);
  }
}
