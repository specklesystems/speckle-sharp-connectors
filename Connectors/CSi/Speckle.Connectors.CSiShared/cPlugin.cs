namespace Speckle.Connectors.CSiShared;

public interface ICSiPlugin : IDisposable
{
  void Main(ref cSapModel sapModel, ref cPluginCallback pluginCallback);
  int Info(ref string text);
}
