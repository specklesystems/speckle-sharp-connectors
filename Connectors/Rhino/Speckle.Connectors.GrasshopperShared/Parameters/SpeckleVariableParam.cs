using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Speckle.Connectors.GrasshopperShared.HostApp;

namespace Speckle.Connectors.GrasshopperShared.Parameters;

/// <summary>
/// Simplified parameter with name inheritance support
/// Focuses on core functionality: Tab key inheritance and AlwaysInheritNames
/// </summary>
public class SpeckleVariableParam : Param_GenericObject
{
  private bool _alwaysInheritNames;
  private readonly HashSet<IGH_Param> _sourceSubscriptions = [];
  private bool _isUpdatingName; // Prevent recursive updates

  static SpeckleVariableParam()
  {
    KeyWatcher.Initialize();
  }

  public bool CanInheritNames { get; set; } = true;

  public bool AlwaysInheritNames
  {
    get => _alwaysInheritNames;
    set
    {
      if (_alwaysInheritNames == value)
      {
        return;
      }

      _alwaysInheritNames = value;

      // disable manual renaming when AlwaysInheritNames is enabled
      MutableNickName = !value;

      if (value)
      {
        SubscribeToSources();
        TryInheritName();
      }
      else
      {
        UnsubscribeFromSources();
      }
    }
  }

  public override Guid ComponentGuid => new("A1B2C3D4-E5F6-7890-ABCD-123456789ABC");

  public override void AddSource(IGH_Param source, int index)
  {
    base.AddSource(source, index);

    if (AlwaysInheritNames)
    {
      SubscribeToSource(source);
    }

    // Inherit name on Tab key or when AlwaysInheritNames is enabled
    if (ShouldInheritName())
    {
      TryInheritName();
    }
  }

  public override void RemoveSource(IGH_Param source)
  {
    UnsubscribeFromSource(source);
    base.RemoveSource(source);

    if (AlwaysInheritNames)
    {
      TryInheritName();
    }
  }

  private bool ShouldInheritName() => CanInheritNames && (AlwaysInheritNames || KeyWatcher.TabPressed);

  private void TryInheritName()
  {
    if (!CanInheritNames || Sources.Count == 0 || _isUpdatingName)
    {
      return;
    }

    var names = Sources.Select(s => s.NickName).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
    if (names.Count == 0)
    {
      return;
    }

    var inheritedName = string.Join("|", names);

    // Only update if the name actually changed to avoid unnecessary events
    if (NickName != inheritedName)
    {
      _isUpdatingName = true;
      try
      {
        // Temporarily allow renaming for programmatic update
        MutableNickName = true;

        Name = inheritedName;
        NickName = inheritedName;

        // Restore the correct state
        MutableNickName = !AlwaysInheritNames;

        // Tell the parent component its layout needs to be recalculated
        Attributes.Parent?.ExpireLayout();
      }
      finally
      {
        _isUpdatingName = false;
      }
    }
  }

  private void SubscribeToSources()
  {
    foreach (var source in Sources)
    {
      SubscribeToSource(source);
    }
  }

  private void SubscribeToSource(IGH_Param source)
  {
    if (_sourceSubscriptions.Add(source))
    {
      source.ObjectChanged += OnSourceChanged;
    }
  }

  private void UnsubscribeFromSources()
  {
    foreach (var source in _sourceSubscriptions.ToList())
    {
      UnsubscribeFromSource(source);
    }
  }

  private void UnsubscribeFromSource(IGH_Param source)
  {
    if (_sourceSubscriptions.Remove(source))
    {
      source.ObjectChanged -= OnSourceChanged;
    }
  }

  private void OnSourceChanged(IGH_DocumentObject sender, GH_ObjectChangedEventArgs e)
  {
    if (!AlwaysInheritNames || _isUpdatingName)
    {
      return;
    }

    if (e.Type == GH_ObjectEventType.NickName || e.Type == GH_ObjectEventType.NickNameAccepted)
    {
      // Use immediate UI thread invocation for responsive name inheritance
      Rhino.RhinoApp.InvokeOnUiThread(() =>
      {
        if (AlwaysInheritNames && !_isUpdatingName) // Double-check in case it changed
        {
          TryInheritName();
        }
      });
    }
  }

  public override void RemovedFromDocument(GH_Document document)
  {
    UnsubscribeFromSources();
    base.RemovedFromDocument(document);
  }

  public override bool Write(GH_IWriter writer)
  {
    var result = base.Write(writer);
    writer.SetBoolean("CanInheritNames", CanInheritNames);
    writer.SetBoolean("AlwaysInheritNames", AlwaysInheritNames);
    return result;
  }

  public override bool Read(GH_IReader reader)
  {
    var result = base.Read(reader);
    bool canInherit = true;
    if (reader.TryGetBoolean("CanInheritNames", ref canInherit))
    {
      CanInheritNames = canInherit;
    }

    bool alwaysInherit = false;
    if (reader.TryGetBoolean("AlwaysInheritNames", ref alwaysInherit))
    {
      _alwaysInheritNames = alwaysInherit; // Set backing field directly during deserialization to avoid triggering logic
      MutableNickName = !alwaysInherit; // Set MutableNickName based on the loaded value
    }

    return result;
  }

  protected override void OnVolatileDataCollected()
  {
    base.OnVolatileDataCollected();

    // Ensure subscriptions are properly set up after data collection
    // and after the object is fully deserialized
    if (AlwaysInheritNames)
    {
      SubscribeToSources();
      TryInheritName();
    }
  }
}
