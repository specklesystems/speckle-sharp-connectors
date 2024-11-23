using CSiAPIv1;

namespace Speckle.Connector.ETABS22;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE1006:Naming Styles", Justification = "<Pending>")]
public class cPlugin : cPluginContract, IDisposable
{
  private static string s_modality = "Non-Modal";
  private Form1? _panel;
  private bool _disposed;

  public void Main(ref cSapModel sapModel, ref cPluginCallback pluginCallback)
  {
    _panel = new Form1();
    _panel.SetSapModel(ref sapModel, ref pluginCallback);

    // Subscribe to form closed event to handle disposal
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
        // Dispose managed resources
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
