using Grasshopper.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.Grasshopper8.Components.BaseComponents;
using Speckle.Connectors.Grasshopper8.HostApp;
using Speckle.Connectors.Grasshopper8.Parameters;
using Speckle.Sdk;
using Speckle.Sdk.Api;
using Speckle.Sdk.Credentials;
using Speckle.Sdk.Models;

namespace Speckle.Connectors.Grasshopper8.Components.Operations.Receive;

public class ReceiveComponentOutput
{
  public Base RootObject { get; set; }
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
      new SpeckleObjectParam(GH_ParamAccess.item),
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

    return new ReceiveComponentOutput { RootObject = root };
  }
}
