namespace Speckle.Connectors.CSiShared;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE1006:Naming Styles", Justification = "<Pending>")]
public abstract class CSiPluginBase : cPluginContract, IDisposable
{
  private const string s_modality = "Non-Modal";
  private SpeckleFormBase? _panel;
  private bool _disposed;

  public void Main(ref cSapModel sapModel, ref cPluginCallback pluginCallback)
  {
    _panel = CreateForm();
    _panel.Initialize(ref sapModel, ref pluginCallback);
    _panel.FormClosed += (_, _) => Dispose();

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
    text = "Hey Speckler! This is our next-gen CSi Connector.";
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

  ~CSiPluginBase()
  {
    Dispose(false);
  }
}
