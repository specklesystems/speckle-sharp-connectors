using Rhino.DocObjects.Tables;

namespace Speckle.Connectors.Rhino.Operations.Receive;

/// <summary>
/// Helper class to disable <see cref="ViewTable.RedrawEnabled"/> within a scope
/// </summary>
public sealed class DisableRedrawScope : IDisposable
{
  private readonly ViewTable _viewTable;
  private readonly bool _returnToStatus;

  public DisableRedrawScope(ViewTable viewTable, bool returnToStatus = true)
  {
    _viewTable = viewTable;
    _returnToStatus = returnToStatus;

    _viewTable.RedrawEnabled = false;
  }

  public void Dispose()
  {
    _viewTable.RedrawEnabled = _returnToStatus;
    _viewTable.Redraw();
  }
}
