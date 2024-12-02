namespace Speckle.Connectors.CSiShared.HostApp;

// NOTE: Create a centralized access point for ETABS and SAP APIs across the entire program
// CSi is already giving us the "sapModel" reference through the plugin interface. No need to attach to running instance
// Prevent having to pass the "sapModel" around between classes and this ensures consistent access
public interface ICSiApplicationService
{
  cSapModel SapModel { get; }
  void Initialize(cSapModel sapModel, cPluginCallback pluginCallback);
}

public class CSiApplicationService : ICSiApplicationService
{
  public cSapModel SapModel { get; private set; }
  private cPluginCallback _pluginCallback;

  public CSiApplicationService()
  {
    SapModel = null!;
  }

  public void Initialize(cSapModel sapModel, cPluginCallback pluginCallback)
  {
    SapModel = sapModel;
    _pluginCallback = pluginCallback;
  }
}
