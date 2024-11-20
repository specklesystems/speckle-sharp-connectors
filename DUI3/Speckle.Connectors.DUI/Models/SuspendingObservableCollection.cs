using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Speckle.Connectors.DUI.Models;

public interface INotifyCollection<T> : ICollection<T>, INotifyCollectionChanged;

//want readonly-ness because ObservableCollection isn't thread-safe and ReadOnlyObservableCollection doesn't expose events for whatever reason
public interface IReadOnlyNotifyCollection<out T> : IReadOnlyCollection<T>, INotifyCollectionChanged;

public class NotifyCollection<T> : ObservableCollection<T>, INotifyCollection<T>, IReadOnlyNotifyCollection<T>;

//needed suspension for AddRange/RemoveRange
public class SuspendingNotifyCollection<T> : NotifyCollection<T>
{
  private class SuspendingNotifyCollectionSuspension : IDisposable
  {
    private readonly SuspendingNotifyCollection<T> _collection;
    private readonly bool _previousNotificationSetting;

    public SuspendingNotifyCollectionSuspension(SuspendingNotifyCollection<T> collection)
    {
      _collection = collection;
      _previousNotificationSetting = collection.IsNotifying;
      collection.IsNotifying = false;
    }

    public void Dispose() => _collection.IsNotifying = _previousNotificationSetting;
  }

  /// <summary>
  /// Enables/Disables property change notification.
  /// </summary>
  public bool IsNotifying { get; private set; } = true;

  public IDisposable SuspendNotifications() => new SuspendingNotifyCollectionSuspension(this);

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
