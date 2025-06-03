using Microsoft.Extensions.DependencyInjection;
using Rhino.PlugIns;
using Speckle.Connectors.Common;
using Speckle.Connectors.DUI;
using Speckle.Connectors.Rhino.DependencyInjection;
using Speckle.Converters.Rhino;
using Speckle.Sdk;
using Speckle.Sdk.Models.Extensions;

namespace Speckle.Connectors.Rhino.Plugin;

///<summary>
/// <para>Every RhinoCommon .rhp assembly must have one and only one PlugIn-derived
/// class. DO NOT create instances of this class yourself. It is the
/// responsibility of Rhino to create an instance of this class.</para>
/// <para>To complete plug-in information, please also see all PlugInDescription
/// attributes in AssemblyInfo.cs (you might need to click "Project" ->
/// "Show All Files" to see it in the "Solution Explorer" window).</para>
///</summary>
public class SpeckleConnectorsRhinoPlugin : PlugIn
{
  private IDisposable? _disposableLogger;

  protected override string LocalPlugInName => "Speckle";
  public ServiceProvider? Container { get; private set; }

  public SpeckleConnectorsRhinoPlugin()
  {
    Instance = this;
  }

  ///<summary>Gets the only instance of the Speckle_Connectors_Rhino7Plugin plug-in.</summary>
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
  public static SpeckleConnectorsRhinoPlugin Instance { get; private set; }
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

  // You can override methods here to change the plug-in behavior on
  // loading and shut down, add options pages to the Rhino _Option command
  // and maintain plug-in wide options in a document.

  protected override LoadReturnCode OnLoad(ref string errorMessage)
  {
    try
    {
      AppDomain.CurrentDomain.AssemblyResolve += AssemblyResolver.OnAssemblyResolve<SpeckleConnectorsRhinoPlugin>;
      var services = new ServiceCollection();
      _disposableLogger = services.Initialize(HostApplications.Rhino, GetVersion());
      services.AddRhino();
      services.AddRhinoConverters();

      // but the Rhino connector has `.rhp` as it is extension.
      Container = services.BuildServiceProvider();
      Container.UseDUI();

      return LoadReturnCode.Success;
    }
    catch (Exception e) when (!e.IsFatal())
    {
      errorMessage = e.ToFormattedString();
      return LoadReturnCode.ErrorShowDialog;
    }
  }

  private HostAppVersion GetVersion()
  {
#if RHINO7
    return HostAppVersion.v7;
#elif RHINO8
    return HostAppVersion.v8;
#else
    throw new NotImplementedException();
#endif
  }

  protected override void OnShutdown()
  {
    _disposableLogger?.Dispose();
    Container?.Dispose();
    base.OnShutdown();
  }
}
