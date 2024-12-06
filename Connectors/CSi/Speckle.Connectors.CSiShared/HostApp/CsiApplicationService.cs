namespace Speckle.Connectors.CSiShared.HostApp;

/// <summary>
/// Create a centralized access point for ETABS and SAP APIs across the entire program.
/// </summary>
/// <remarks>
/// All API methods are based on the objectType and objectName, not the GUID.
/// CSi is already giving us the "sapModel" reference through the plugin interface. No need to attach to running instance.
/// Since objectType is a single int (1, 2 ... 7) we know first index will always be the objectType.
/// Prevent having to pass the "sapModel" around between classes and this ensures consistent access.
/// Name "sapModel" is misleading since it doesn't only apply to SAP2000, but this is the convention in the API, so we keep it.
/// </remarks>
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
