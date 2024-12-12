using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
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
  public Dictionary<string, GH_Structure<IGH_Goo>> Inputs { get; }

  public SendComponentInput(SpeckleUrlModelResource resource, Dictionary<string, GH_Structure<IGH_Goo>> inputs)
  {
    Resource = resource;
    Inputs = inputs;
  }
}

public class SendComponentOutput(SpeckleUrlModelObjectResource resource)
{
  public SpeckleUrlModelObjectResource Resource { get; } = resource;
}

public class SendComponent()
  : SpeckleScopedTaskCapableComponent<SendComponentInput, SendComponentOutput>(
    "Send",
    "SS",
    "Speckle Send",
    "Speckle",
    "Operations"
  )
{
  public override Guid ComponentGuid => new("0CF0D173-BDF0-4AC2-9157-02822B90E9FB");

  protected override void RegisterInputParams(GH_InputParamManager pManager)
  {
    pManager.AddParameter(new SpeckleUrlModelResourceParam());
    pManager.AddGenericParameter("A", "A", "A", GH_ParamAccess.tree);
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

    var name = Params.Input[1].Name;
    da.GetDataTree(1, out GH_Structure<IGH_Goo> tree);

    var inputDict = new Dictionary<string, GH_Structure<IGH_Goo>> { { name, tree } };

    return new SendComponentInput(resource.NotNull(), inputDict);
  }

  protected override void SetOutput(IGH_DataAccess da, SendComponentOutput result)
  {
    da.SetData(0, result.Resource);
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
    var sendOperation = scope.ServiceProvider.GetRequiredService<
      SendOperation<IReadOnlyDictionary<string, GH_Structure<IGH_Goo>>>
    >();

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
    var receiveInfo = await input.Resource.GetSendInfo(client, cancellationToken).ConfigureAwait(false);

    var result = await sendOperation
      .Execute(input.Inputs, receiveInfo, progress, cancellationToken)
      .ConfigureAwait(false);

    return new SendComponentOutput(
      new SpeckleUrlModelObjectResource(receiveInfo.ServerUrl.ToString(), receiveInfo.ProjectId, result.RootObjId)
    );
  }
}
