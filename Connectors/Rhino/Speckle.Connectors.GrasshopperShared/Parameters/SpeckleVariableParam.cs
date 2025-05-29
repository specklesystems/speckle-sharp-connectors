using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Speckle.Connectors.GrasshopperShared.HostApp;

namespace Speckle.Connectors.GrasshopperShared.Parameters;

/// <summary>
/// An implementation of Param_GenericObject specific for variable input components that support name inheritance
/// </summary>
public class SpeckleVariableParam : Param_GenericObject
{
  public bool CanInheritNames { get; set; } = true;

  private bool _alwaysInheritNames;
  public bool AlwaysInheritNames // why so complicated?? -> if connected then user enables setting, we want auto update. also, setting only worked if enabled before connected.
  {
    get => _alwaysInheritNames;
    set
    {
      if (_alwaysInheritNames != value)
      {
        _alwaysInheritNames = value;
        OnAlwaysInheritNamesChanged();
      }
    }
  }

  public override Guid ComponentGuid => new("A1B2C3D4-E5F6-7890-ABCD-123456789ABC");

  private string _lastInheritedName = string.Empty; // cache last inherited name to avoid unnecessary updates
  private readonly HashSet<IGH_Param> _subscribedSources = new(); // keep track of subscribed sources to avoid duplicate subscriptions

  static SpeckleVariableParam()
  {
    KeyWatcher.Initialize(); // initialize KeyWatcher once for all instances
  }

  private void OnAlwaysInheritNamesChanged()
  {
    if (_alwaysInheritNames)
    {
      // when enabling AlwaysInheritNames, subscribe to all existing sources AND inherit names immediately (#AutomaticRefresh)
      SetupSourceSubscriptions();
      if (MutableNickName && CanInheritNames && Sources.Count > 0)
      {
        InheritNickname();
      }
    }
    else
    {
      // when disabling AlwaysInheritNames, unsubscribe from all sources otherwise people be confused
      CleanupSourceSubscriptions();
    }
  }

  private void SetupSourceSubscriptions()
  {
    foreach (var source in Sources)
    {
      if (!_subscribedSources.Contains(source))
      {
        source.ObjectChanged += OnSourceObjectChanged;
        _subscribedSources.Add(source);
      }
    }
  }

  private void CleanupSourceSubscriptions()
  {
    foreach (var source in _subscribedSources.ToList())
    {
      source.ObjectChanged -= OnSourceObjectChanged;
    }
    _subscribedSources.Clear();
  }

  private void UpdateInheritedName()
  {
    var names = Sources.Select(s => s.NickName).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
    var currentName = string.Join("|", names).Trim();

    // only update if name changed
    if (!string.IsNullOrEmpty(currentName) && currentName != _lastInheritedName)
    {
      _lastInheritedName = currentName;
      Name = currentName;
      NickName = currentName;
    }
  }

  public override void AppendAdditionalMenuItems(ToolStripDropDown menu)
  {
    base.AppendAdditionalMenuItems(menu); // adds normal menu stuff first

    if (CanInheritNames && MutableNickName && Sources.Count > 0) // inherit names only shows up if something is connected
    {
      Menu_AppendSeparator(menu);
      Menu_AppendItem(menu, "Inherit names", (_, _) => InheritNickname(), true);
    }
  }

  private void InheritNickname()
  {
    RecordUndoEvent("Input name change");
    var names = Sources.Select(s => s.NickName).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
    var fullname = string.Join("|", names).Trim();
    var parentComponent = Attributes?.Parent?.DocObject as GH_Component;

    if (string.IsNullOrEmpty(fullname))
    {
      // if no valid names found, show a warning
      parentComponent?.AddRuntimeMessage(
        GH_RuntimeMessageLevel.Warning,
        $"Could not inherit name from parameter '{NickName}': No valid source names found."
      );
      return; // early return - no valid names to inherit
    }

    // valid names, proceed with inheritance
    Name = fullname;
    NickName = fullname;
    _lastInheritedName = fullname;

    // expire the parent component to trigger updates
    if (parentComponent != null)
    {
      parentComponent.ExpireSolution(true);
    }
    else
    {
      // if standalone parameter, just expire preview
      ExpirePreview(true);
    }
  }

  public override void AddSource(IGH_Param source, int index)
  {
    base.AddSource(source, index); // do normal connection stuff

    // subscribe to source's ObjectChanged event for automatic updates if AlwaysInheritNames is enabled
    if (CanInheritNames && AlwaysInheritNames && !_subscribedSources.Contains(source))
    {
      source.ObjectChanged += OnSourceObjectChanged;
      _subscribedSources.Add(source);
    }

    // if Tab was pressed, automatically inherit name
    // or if set to always inherit
    if (MutableNickName && CanInheritNames && (AlwaysInheritNames || KeyWatcher.TabPressed))
    {
      InheritNickname();
    }
  }

  public override void RemoveSource(IGH_Param source)
  {
    // unsubscribe from source's ObjectChanged event
    if (_subscribedSources.Contains(source))
    {
      source.ObjectChanged -= OnSourceObjectChanged;
      _subscribedSources.Remove(source);
    }

    base.RemoveSource(source);

    if (MutableNickName && CanInheritNames && AlwaysInheritNames)
    {
      InheritNickname();
    }
  }

  private void OnSourceObjectChanged(IGH_DocumentObject sender, GH_ObjectChangedEventArgs e)
  {
    // only react to nickname changes if we're inheriting names
    if (!CanInheritNames || !AlwaysInheritNames || !MutableNickName)
    {
      return;
    }

    if (e.Type == GH_ObjectEventType.NickName || e.Type == GH_ObjectEventType.NickNameAccepted)
    {
      // use small delay to ensure the name change is complete
      Rhino.RhinoApp.InvokeOnUiThread(() =>
      {
        if (AlwaysInheritNames) // double-check in case it changed
        {
          UpdateInheritedName();
        }
      });
    }
  }

  public override void RemovedFromDocument(GH_Document document)
  {
    // Clean up all event subscriptions when removed from document
    CleanupSourceSubscriptions();
    document.ObjectsAdded -= OnDocumentObjectsAdded;
    base.RemovedFromDocument(document);
  }

  public override bool Write(GH_IWriter writer)
  {
    var result = base.Write(writer);
    writer.SetBoolean("CanInheritNames", CanInheritNames);
    writer.SetBoolean("AlwaysInheritNames", AlwaysInheritNames);
    writer.SetString("LastInheritedName", _lastInheritedName);

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
      // set backing field directly to avoid triggering logic during document loading
      _alwaysInheritNames = alwaysInherit;
    }

    string lastInheritedName = string.Empty;
    if (reader.TryGetString("LastInheritedName", ref lastInheritedName))
    {
      _lastInheritedName = lastInheritedName;
    }

    return result;
  }

  protected override void OnVolatileDataCollected()
  {
    base.OnVolatileDataCollected();

    // after collecting data, ensure subscriptions are set up if needed
    if (CanInheritNames && AlwaysInheritNames)
    {
      SetupSourceSubscriptions();
    }
  }

  public override void AddedToDocument(GH_Document document)
  {
    base.AddedToDocument(document);
    document.ObjectsAdded += OnDocumentObjectsAdded;
  }

  private void OnDocumentObjectsAdded(object sender, GH_DocObjectEventArgs e)
  {
    // This event fires after all objects are added to the document during loading
    // Check if we need to inherit names now that everything is properly connected
    if (CanInheritNames && AlwaysInheritNames && Sources.Count > 0)
    {
      // use Rhino's UI thread invocation with a small delay to ensure all connections are established
      Rhino.RhinoApp.InvokeOnUiThread(() =>
      {
        if (AlwaysInheritNames) // double-check in case it changed
        {
          UpdateInheritedName();
        }
      });
    }

    // unsubscribe after first use - we only need this during initial document loading
    if (sender is GH_Document doc)
    {
      doc.ObjectsAdded -= OnDocumentObjectsAdded;
    }
  }
}
