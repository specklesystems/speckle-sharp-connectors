using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Speckle.Connectors.DUI.Models;

public class SuspendingObservableCollection<T> : ObservableCollection<T>
{
  private class ObservableCollectionExSuspension : IDisposable
  {
    private readonly SuspendingObservableCollection<T> _collection;

    public ObservableCollectionExSuspension(SuspendingObservableCollection<T> collection)
    {
      _collection = collection;
      collection.IsNotifying = true;
    }

    public void Dispose() => _collection.IsNotifying = false;
  }

  public SuspendingObservableCollection()
  {
    IsNotifying = true;
  }

  public SuspendingObservableCollection(IEnumerable<T> collection)
    : base(collection)
  {
    IsNotifying = true;
  }

  /// <summary>
  /// Enables/Disables property change notification.
  /// </summary>
  public bool IsNotifying { get; private set; }

  public IDisposable SuspendNotifications() => new ObservableCollectionExSuspension(this);

  /// <summary>
  /// Notifies subscribers of the property change.
  /// </summary>
  /// <param name = "propertyName">Name of the property.</param>
  public virtual void NotifyOfPropertyChange(string propertyName)
  {
    if (IsNotifying)
    {
      OnPropertyChanged(new PropertyChangedEventArgs(propertyName));
    }
  }

  /// <summary>
  /// Raises a change notification indicating that all bindings should be refreshed.
  /// </summary>
  public void Refresh()
  {
    OnPropertyChanged(new PropertyChangedEventArgs("Count"));
    OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
    OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
  }

  protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
  {
    if (IsNotifying)
    {
      base.OnCollectionChanged(e);
    }
  }

  /// <summary>
  /// Raises the PropertyChanged event with the provided arguments.
  /// </summary>
  /// <param name = "e">The event data to report in the event.</param>
  protected override void OnPropertyChanged(PropertyChangedEventArgs e)
  {
    if (IsNotifying)
    {
      base.OnPropertyChanged(e);
    }
  }

  /// <summary>
  /// Adds the range.
  /// </summary>
  /// <param name = "items">The items.</param>
  public void AddRange(IEnumerable<T> items)
  {
    var previousNotificationSetting = IsNotifying;
    IsNotifying = false;
    var index = Count;
    foreach (var item in items)
    {
      InsertItem(index, item);
      index++;
    }
    IsNotifying = previousNotificationSetting;
    Refresh();
  }

  /// <summary>
  /// Removes the range.
  /// </summary>
  /// <param name = "items">The items.</param>
  public void RemoveRange(IEnumerable<T> items)
  {
    var previousNotificationSetting = IsNotifying;
    IsNotifying = false;
    foreach (var item in items)
    {
      var index = IndexOf(item);
      if (index >= 0)
      {
        RemoveItem(index);
      }
    }
    IsNotifying = previousNotificationSetting;
    Refresh();
  }
}
