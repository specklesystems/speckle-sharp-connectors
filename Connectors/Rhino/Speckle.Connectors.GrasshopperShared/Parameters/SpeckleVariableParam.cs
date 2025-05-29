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

  private bool ShouldInheritName() =>
    MutableNickName && CanInheritNames && (AlwaysInheritNames || KeyWatcher.TabPressed);

  private void TryInheritName()
  {
    if (!MutableNickName || !CanInheritNames || Sources.Count == 0)
    {
      return;
    }

    var names = Sources.Select(s => s.NickName).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();

    if (names.Count == 0)
    {
      return;
    }

    var inheritedName = string.Join("|", names);
    Name = inheritedName;
    NickName = inheritedName;

    // Trigger update
    (Attributes?.Parent?.DocObject as GH_Component)?.ExpireSolution(true);
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
    if (!AlwaysInheritNames || !MutableNickName)
    {
      return;
    }

    if (e.Type == GH_ObjectEventType.NickName || e.Type == GH_ObjectEventType.NickNameAccepted)
    {
      // Use UI thread to ensure proper timing
      Rhino.RhinoApp.InvokeOnUiThread(() =>
      {
        if (AlwaysInheritNames) // Double-check in case it changed
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
    bool canInherit = default;
    if (reader.TryGetBoolean("CanInheritNames", ref canInherit))
    {
      CanInheritNames = canInherit;
    }

    bool alwaysInherit = default;
    if (reader.TryGetBoolean("AlwaysInheritNames", ref alwaysInherit))
    {
      _alwaysInheritNames = alwaysInherit; // Set backing field to avoid triggering logic during load
    }

    return result;
  }

  protected override void OnVolatileDataCollected()
  {
    base.OnVolatileDataCollected();

    // Ensure subscriptions are properly set up after data collection
    if (AlwaysInheritNames)
    {
      SubscribeToSources();
    }
  }
}
