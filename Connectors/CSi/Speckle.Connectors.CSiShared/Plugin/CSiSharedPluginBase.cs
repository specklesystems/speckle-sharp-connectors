namespace Speckle.Connectors.CSiShared;

public abstract class CSiSharedPluginBase : cPluginContract, IDisposable
{
  private static string s_modality = "Non-Modal";
  private SpeckleFormBase? _panel;
  private bool _disposed;

  public void Main(ref cSapModel sapModel, ref cPluginCallback pluginCallback)
  {
    _panel = CreateForm();
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

  protected abstract SpeckleFormBase CreateForm();

  public virtual int Info(ref string text)
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
        _panel?.Dispose();
        _panel = null;
      }
      _disposed = true;
    }
  }

  public void Dispose()
  {
    Dispose(true);
    GC.SuppressFinalize(this);
  }

  ~CSiSharedPluginBase()
  {
    Dispose(false);
  }
}
