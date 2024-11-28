using Grasshopper.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Rhino;
using Rhino.Geometry;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.Grasshopper8.Operations.Receive;
using Speckle.Converters.Common;
using Speckle.Converters.Rhino;
using Speckle.Sdk.Common;
using Speckle.Sdk.Credentials;
using Speckle.Sdk.Models;

namespace Speckle.Connectors.Grasshopper8;

public class SpeckleFirstComponent : GH_TaskCapableComponent<List<object?>>
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

  protected override void SolveInstance(IGH_DataAccess da)
  {
    if (InPreSolve)
    {
      // Collect the data and create the task
      string url = GetInput(da);
      TaskList.Add(PerformReceiveOperation(url, CancelToken));
      Message = "Receiving...";
      return;
    }

    if (!GetSolveResults(da, out List<object?> result))
    {
      // INFO: This will run synchronously. Useful for Rhino.Compute runs, but can also be enabled by user.
      string url = GetInput(da);
      var syncResult = PerformReceiveOperation(url).Result;
      SetOutput(da, syncResult);
    }

    if (result is not null)
    {
      SetOutput(da, result);
    }
  }

  private void SetOutput(IGH_DataAccess da, List<object?> result)
  {
    da.SetDataList(0, result);
    Message = "Done";
  }

  private string GetInput(IGH_DataAccess da)
  {
    string url = "";
    da.GetData(0, ref url);
    return url;
  }

  private async Task<List<object?>> PerformReceiveOperation(string url, CancellationToken cancellationToken = default)
  {
    // TODO: URL Parsing must be done here
    Console.WriteLine($"Receiving from fake url, skipping input: {url}");

    var account = _accountManager.GetDefaultAccount();
    if (account is null)
    {
      throw new SpeckleAccountManagerException($"No default account was found");
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
      // TODO: Progress only makes sense in non-blocking async receive, which is not supported yet.
      // Message = $"{progress.Status}: {progress.Progress}";
    });

    using var scope = PriorityLoader.Container.CreateScope();
    IRhinoConversionSettingsFactory rhinoConversionSettingsFactory =
      scope.ServiceProvider.GetRequiredService<IRhinoConversionSettingsFactory>();

    scope
      .ServiceProvider.GetRequiredService<IConverterSettingsStore<RhinoConversionSettings>>()
      .Initialize(rhinoConversionSettingsFactory.Create(RhinoDoc.ActiveDoc));

    var receiveOperation = scope.ServiceProvider.GetRequiredService<ReceiveOperation>();
    var result = await receiveOperation.Execute(receiveInfo, progress, cancellationToken).ConfigureAwait(false);

    List<object?> results = new();
    // HACK: GrashhopperHostObjectBuilder returns a specific subclass that contains the result object as well.
    foreach (var conversionResult in result.ConversionResults)
    {
      if (conversionResult is not GrasshopperReceiveConversionResult ghConversionResult)
      {
        throw new NotSupportedException($"Unsupported conversion result type: {conversionResult}");
      }

      if (ghConversionResult.Result is GeometryBase geometryBase)
      {
        //var guid = BakeObject(geometryBase, obj, atts);
      }
      else if (ghConversionResult.Result is List<GeometryBase> geometryBases) // one to many raw encoding case
      {
        results.AddRange(geometryBases);
      }
      else if (ghConversionResult.Result is IEnumerable<(object, Base)> fallbackConversionResult) // one to many fallback conversion
      {
        results.AddRange(fallbackConversionResult.Select(o => o.Item1));
      }
      results.Add(ghConversionResult.Result);
    }

    return results;
  }

  public override Guid ComponentGuid => new Guid("c123402d-6b40-4619-bb3b-88eb3fc8bb7a");
}
