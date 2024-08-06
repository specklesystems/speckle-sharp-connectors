﻿using System.Reflection;
using Rhino.PlugIns;
using Speckle.Autofac;
using Speckle.Autofac.DependencyInjection;
using Speckle.Connectors.Rhino.DependencyInjection;
using Speckle.Connectors.Rhino.HostApp;
using Speckle.Core.Common;
using Speckle.Core.Kits;
using Speckle.Core.Models.Extensions;

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
  private IRhinoPlugin? _rhinoPlugin;

  private Speckle.Connectors.Utils.Connector? _connector;

  protected override string LocalPlugInName => "Speckle (New UI)";
  public SpeckleContainer Container => _connector.NotNull().Container;

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
      // Register Settings
      var rhinoSettings = new RhinoSettings(HostApplications.Rhino, GetVersion());

      // POC: We must load the Rhino connector module manually because we only search for DLL files when calling `LoadAutofacModules`,
      // but the Rhino connector has `.rhp` as it's extension.
      var builder = SpeckleContainerBuilder
        .CreateInstance()
        .LoadAutofacModules(Assembly.GetExecutingAssembly(), rhinoSettings.Modules)
        .AddSingleton(rhinoSettings);
      _connector = Speckle.Connectors.Utils.Connector.Start<SpeckleConnectorsRhinoPlugin>(
        builder,
        new(HostApplications.Rhino, GetVersionAsString()) //HostAppVersion.v7
      );
      // Resolve root plugin object and initialise.
      _rhinoPlugin = Container.Resolve<IRhinoPlugin>();
      _rhinoPlugin.Initialise();

      return LoadReturnCode.Success;
    }
    catch (Exception e) when (!e.IsFatal())
    {
      errorMessage = e.ToFormattedString();
      return LoadReturnCode.ErrorShowDialog;
    }
  }

  protected override void OnShutdown()
  {
    _rhinoPlugin?.Shutdown();
    _connector?.Dispose();
    base.OnShutdown();
  }

  private static HostAppVersion GetVersion()
  {
#if RHINO7
    return HostAppVersion.v7;
#elif RHINO8
    return HostAppVersion.v8;
#else
    throw new NotImplementedException();
#endif
  }

  private static string GetVersionAsString()
  {
#if RHINO7
    return "7";
#elif RHINO8
    return "8";
#else
    throw new NotImplementedException();
#endif
  }
}
