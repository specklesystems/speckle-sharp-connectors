namespace Speckle.Connectors.CSiShared;

public interface IForm1 : IDisposable
{
  string Text { get; set; }
  void SetSapModel(ref cSapModel sapModel, ref cPluginCallback pluginCallback);
  void Show();
  void ShowDialog();
  event FormClosedEventHandler FormClosed;
}
