using Speckle.Connectors.CSiShared;

// NOTE: Plugin entry point must match the assembly name, otherwise hits you with a "Not found" error when loading plugin
// TODO: Move ETABS implementation to csproj as part of CNX-835 and/or CNX-828
namespace Speckle.Connectors.ETABS21;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE1006:Naming Styles", Justification = "<Pending>")]
public class cPlugin : cPluginContract, ICSiPlugin
{
  private static string s_modality = "Non-Modal";
  private IForm1? _panel;
  private bool _disposed;

  public void Main(ref cSapModel sapModel, ref cPluginCallback pluginCallback)
  {
    _panel = new Form1();
    _panel.SetSapModel(ref sapModel, ref pluginCallback);
    _panel.FormClosed += (s, e) => Dispose();

    if (string.Equals(s_modality, "Non-Modal", StringComparison.OrdinalIgnoreCase))
    {
      _panel.Show();
    }
    else
    {
      _panel.ShowDialog();
    }
  }

  public int Info(ref string text)
  {
    text = "Hey Speckler! This is our next-gen ETABS Connector.";
    return 0;
  }

  protected virtual void Dispose(bool disposing)
  {
    if (!_disposed)
    {
      if (disposing)
      {
        if (_panel != null)
        {
          _panel.Dispose();
          _panel = null;
        }
      }
      _disposed = true;
    }
  }

  public void Dispose()
  {
    Dispose(true);
    GC.SuppressFinalize(this);
  }

  ~cPlugin()
  {
    Dispose(false);
  }
}
