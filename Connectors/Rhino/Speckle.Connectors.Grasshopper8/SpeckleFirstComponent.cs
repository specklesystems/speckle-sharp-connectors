using Grasshopper.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Rhino;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Operations;
using Speckle.Converters.Common;
using Speckle.Converters.Rhino;
using Speckle.Sdk.Common;
using Speckle.Sdk.Credentials;

namespace Speckle.Connectors.Grasshopper8;

public class SpeckleFirstComponent : GH_TaskCapableComponent<HostObjectBuilderResult>
{
  private readonly AccountManager _accountManager;

  /// <summary>
  /// Each implementation of GH_Component must provide a public
  /// constructor without any arguments.
  /// Category represents the Tab in which the component will appear,
  /// Subcategory the panel. If you use non-existing tab or panel names,
  /// new tabs/panels will automatically be created.
  /// </summary>
  public SpeckleFirstComponent()
    : base("Send to Speckle", "STP", "Sends objects to speckle", "Speckle", "Operations")
  {
    _accountManager = PriorityLoader.Container.NotNull().GetRequiredService<AccountManager>();
  }

  protected override void RegisterInputParams(GH_InputParamManager pManager)
  {
    pManager.AddTextParameter("Model/Version URL", "url", "The model or version url to receive", GH_ParamAccess.item);
  }

  protected override void RegisterOutputParams(GH_OutputParamManager pManager)
  {
    pManager.AddGenericParameter("Result", "R", "Result", GH_ParamAccess.item);
  }

  protected override void BeforeSolveInstance()
  {
    base.BeforeSolveInstance();
  }

  protected override void AfterSolveInstance()
  {
    base.AfterSolveInstance();
  }

  protected override void SolveInstance(IGH_DataAccess da)
  {
    if (InPreSolve)
    {
      // Collect the data and create the task
      string url = "";
      da.GetData(0, ref url);

      var account = _accountManager.GetDefaultAccount();
      if (account is null)
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No default account was found");
        return;
      }

      var receiveInfo = new ReceiveInfo(
        account.id,
        new Uri(account.serverInfo.url),
        "2295cb26a0",
        "",
        "bd1fd98086",
        "",
        "832e036b91",
        ""
      );

      var progress = new Progress<CardProgress>(progress =>
      {
        Message = $"{progress.Status}: {progress.Progress}";
      });

      TaskList.Add(PerformReceiveOperation(receiveInfo, progress));
      return;
    }

    if (!GetSolveResults(da, out HostObjectBuilderResult result))
    {
      // Compute syncronously! Not supported for now.
      throw new NotSupportedException("Sync receive not supported yet");
    }

    if (result is not null)
    {
      Message = "Done";
      da.SetData(0, result);
    }
  }

  private async Task<HostObjectBuilderResult> PerformReceiveOperation(
    ReceiveInfo receiveInfo,
    Progress<CardProgress> progress
  )
  {
    using var scope = PriorityLoader.Container.CreateScope();
    IRhinoConversionSettingsFactory rhinoConversionSettingsFactory =
      scope.ServiceProvider.GetRequiredService<IRhinoConversionSettingsFactory>();

    scope
      .ServiceProvider.GetRequiredService<IConverterSettingsStore<RhinoConversionSettings>>()
      .Initialize(rhinoConversionSettingsFactory.Create(RhinoDoc.ActiveDoc));

    var receiveOperation = scope.ServiceProvider.GetRequiredService<ReceiveOperation>();
    return await receiveOperation.Execute(receiveInfo, progress, CancelToken).ConfigureAwait(false);
  }

  public override Guid ComponentGuid => new Guid("c123402d-6b40-4619-bb3b-88eb3fc8bb7a");
}
