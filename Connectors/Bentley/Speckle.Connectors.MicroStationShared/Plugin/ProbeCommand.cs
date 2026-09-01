using System.IO;
using System.Text;
using Bentley.MstnPlatformNET;
using Microsoft.Extensions.Logging.Abstractions;
using Speckle.Converters.Common;
using Speckle.Converters.MicroStation.Services;
using Speckle.Converters.MicroStation.Settings;
using Speckle.Converters.MicroStation.ToSpeckle;
using Speckle.Converters.MicroStation.ToSpeckle.Appearance;
using Speckle.Converters.MicroStation.ToSpeckle.MeshExtraction;
using Speckle.Converters.MicroStation.ToSpeckle.Properties;
using Speckle.Converters.MicroStation.ToSpeckle.Raw;

namespace Speckle.Connectors.MicroStation.Plugin;

/// <summary>
/// `Speckle probe` — offline-development verification keyin. Walks the active model with the real
/// conversion pipeline (no DUI, no server) and writes a diagnostic report to
/// <c>%TEMP%\speckle-msprobe.log</c>:
/// units sanity (UOR scale vs element ranges), per-element-type extraction outcome (typed curves /
/// meshes / text / fallbacks), appearance resolution, EC property counts, attachment walk summary.
/// This is what verifies the API assumptions the build-only dev cycle could not test live.
/// </summary>
internal static class ProbeCommand
{
  public static void Run(string? unparsed = null)
  {
    string logPath = Path.Combine(Path.GetTempPath(), "speckle-msprobe.log");
    var log = new StringBuilder();
    try
    {
      var assembly = typeof(ProbeCommand).Assembly;
      log.AppendLine($"build: {assembly.Location}");
      log.AppendLine($"build time: {File.GetLastWriteTime(assembly.Location):yyyy-MM-dd HH:mm:ss}");
      Probe(log, unparsed);
    }
    catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
    {
      log.AppendLine($"!! probe crashed: {ex}");
    }
    File.WriteAllText(logPath, log.ToString(), Encoding.UTF8);
  }

  private static void Probe(StringBuilder log, string? unparsed)
  {
    DPN.DgnModel? model = Session.Instance?.GetActiveDgnModel();
    if (model == null)
    {
      log.AppendLine("no active model");
      return;
    }

    // Assemble the pipeline by hand — the probe runs without the DUI container.
    var settingsStore = new ProbeSettingsStore();
    var unitConverter = new MicroStationToSpeckleUnitConverter();
    var settingsFactory = new MicroStationConversionSettingsFactory(unitConverter);
    settingsStore.Initialize(settingsFactory.Create(model));

    MicroStationConversionSettings s = settingsStore.Current;
    log.AppendLine($"model: {model.ModelName}  units: {s.SpeckleUnits}  uorPerMaster: {s.UorPerMaster}");
    log.AppendLine($"globalOrigin (uor): {s.GlobalOriginX}, {s.GlobalOriginY}, {s.GlobalOriginZ}");
    if (model.GetRange(out BG.DRange3d modelRange).Equals(DPN.StatusInt.Success))
    {
      log.AppendLine(
        $"model range RAW: [{modelRange.Low.X:G6}..{modelRange.High.X:G6}] — divide by uorPerMaster and "
          + "compare to the readout: if the divided number matches what MicroStation shows, geometry is UOR "
          + "(current assumption); if the RAW number matches, flip the scaling in GeometryMapper."
      );
    }

    var mapper = new GeometryMapper(settingsStore);
    var appearance = new AppearanceResolver(settingsStore);
    var polyface = new PolyfaceConverter(mapper);
    var primitives = new CurvePrimitiveConverter(mapper);
    var capture = new GraphicsCaptureExtractor(mapper, polyface, primitives);
    var text = new TextConverter(mapper);
    var instanceSink = new SharedCellInstanceSink { Enabled = true };
    var extractor = new DisplayValueExtractor(
      mapper,
      capture,
      polyface,
      primitives,
      appearance,
      text,
      instanceSink,
      NullLogger<DisplayValueExtractor>.Instance
    );
    var properties = new PropertiesExtractor(NullLogger<PropertiesExtractor>.Instance);

    var perType =
      new Dictionary<
        string,
        (int elements, int geoms, int matGeoms, int instances, int empty, int errors, int ecProps)
      >();
    int scanned = 0;
    foreach (MgdElement? element in model.GetGraphicElements())
    {
      if (element == null)
      {
        continue;
      }
      scanned++;
      if (scanned > 5000)
      {
        log.AppendLine("... element cap (5000) hit, stopping scan");
        break;
      }
      string type = element.TypeName ?? element.ElementType.ToString();
      var entry = perType.TryGetValue(type, out var e) ? e : default;
      entry.elements++;
      try
      {
        var extraction = extractor.Extract(element);
        var extracted = extraction.DisplayValue;
        entry.geoms += extracted.Count;
        entry.matGeoms += extracted.Count(g => g.Material != null);
        entry.instances += extraction.Instances.Count;
        if (extracted.Count == 0 && extraction.Instances.Count == 0)
        {
          entry.empty++;
        }
        entry.ecProps += properties.Extract(element).Properties.Count;
      }
      catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
      {
        entry.errors++;
        if (entry.errors == 1)
        {
          log.AppendLine($"[{type}] first error: {ex.Message}");
        }
      }
      perType[type] = entry;
    }

    log.AppendLine($"scanned: {scanned}  nestedDefinitions: {instanceSink.Definitions.Count}");
    log.AppendLine("type | elements | geoms | matGeoms | instances | empty | errors | ecPropKeys");
    foreach (var kv in perType.OrderByDescending(kv => kv.Value.elements))
    {
      var v = kv.Value;
      log.AppendLine(
        $"{kv.Key} | {v.elements} | {v.geoms} | {v.matGeoms} | {v.instances} | {v.empty} | {v.errors} | {v.ecProps}"
      );
    }

    DumpLevelDiagnostics(log, model);
    DumpSharedCellAndMaterialDiagnostics(log, model, appearance);

    // ── Per-id detail (keyin: `Speckle probe 544761,78994,...`) ───────────────────────────
    if (!string.IsNullOrWhiteSpace(unparsed))
    {
      var wanted = new HashSet<string>(
        unparsed!.Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries),
        StringComparer.Ordinal
      );
      foreach (MgdElement? element in model.GetGraphicElements())
      {
        if (element == null || !wanted.Contains(((ulong)element.ElementId).ToString()))
        {
          continue;
        }
        DumpElementDetail(log, model, element);
      }
    }

    // Attachment summary
    try
    {
      DPN.DgnAttachmentCollection? attachments = model.GetDgnAttachments();
      int count = 0;
      if (attachments != null)
      {
        foreach (DPN.DgnAttachment? att in attachments)
        {
          if (att == null)
          {
            continue;
          }
          count++;
          att.GetTransformToParent(out BG.DTransform3d t, true);
          log.AppendLine(
            $"attachment: {att.AttachModelName} displayed={att.IsDisplayed} missing={att.IsMissingFile} "
              + $"translation=({t.Translation.X:G6},{t.Translation.Y:G6},{t.Translation.Z:G6})"
          );
        }
      }
      log.AppendLine($"attachments: {count}");
    }
    catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
    {
      log.AppendLine($"attachment walk failed: {ex.Message}");
    }
  }

  private static void DumpLevelDiagnostics(StringBuilder log, DPN.DgnModel model)
  {
    // ── Level diagnostics: own-level resolution + global/per-view display ─────────────────
    try
    {
      DPN.LevelCache? levelCache = model.GetLevelCache();
      DPN.ViewInformation? viewInfo = Session.GetActiveViewport()?.GetViewInformation();
      int validOwn = 0,
        invalidOwn = 0;
      var invalidSampleTypes = new Dictionary<string, int>();
      foreach (MgdElement? element in model.GetGraphicElements())
      {
        if (element == null)
        {
          continue;
        }
        DPN.LevelHandle? handle = levelCache?.GetLevel(element.LevelId, true);
        if (handle is { IsValid: true })
        {
          validOwn++;
        }
        else
        {
          invalidOwn++;
          string t = element.TypeName ?? element.ElementType.ToString();
          invalidSampleTypes[t] = invalidSampleTypes.TryGetValue(t, out int n) ? n + 1 : 1;
        }
      }
      log.AppendLine($"own-level resolution: valid={validOwn} invalid={invalidOwn}");
      foreach (var kv in invalidSampleTypes.OrderByDescending(k => k.Value).Take(8))
      {
        log.AppendLine($"  invalid-own-level type: {kv.Key} x{kv.Value}");
      }

      if (levelCache != null)
      {
        log.AppendLine("levels: name | code | globalDisplay | frozen | effectiveInActiveView");
        foreach (DPN.LevelHandle? handle in levelCache.GetHandles())
        {
          if (handle is not { IsValid: true })
          {
            continue;
          }
          string effective = "?";
          if (viewInfo != null)
          {
            try
            {
              viewInfo.GetEffectiveLevelDisplay(out bool shown, model, handle.LevelId);
              effective = shown.ToString();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
              effective = "err";
            }
          }
          log.AppendLine($"  {handle.Name} | {handle.LevelCode} | {handle.Display} | {handle.Frozen} | {effective}");
        }
      }
    }
    catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
    {
      log.AppendLine($"level diagnostics failed: {ex.Message}");
    }
  }

  /// <summary>Explains instancing coverage (why a shared cell would bake instead of proxy) and
  /// how many elements resolve a real render material in this session.</summary>
  private static void DumpSharedCellAndMaterialDiagnostics(
    StringBuilder log,
    DPN.DgnModel model,
    AppearanceResolver appearance
  )
  {
    try
    {
      int sharedTotal = 0,
        notClrSharedCell = 0,
        defNull = 0,
        placementFail = 0,
        ok = 0;
      var clrOther = new Dictionary<string, int>();
      int materialHits = 0;
      var materialNames = new HashSet<string>(StringComparer.Ordinal);
      int scanned = 0;

      foreach (MgdElement? element in model.GetGraphicElements())
      {
        if (element == null)
        {
          continue;
        }
        if (++scanned > 8000)
        {
          log.AppendLine("... shared-cell scan cap hit");
          break;
        }

        if (appearance.ResolveMaterial(element) is { } material)
        {
          materialHits++;
          materialNames.Add(material.Name);
        }

        if (element.ElementType != DPN.MSElementType.SharedCellInstance)
        {
          continue;
        }
        sharedTotal++;
        if (element is not MgdElements.SharedCellElement sharedCell)
        {
          notClrSharedCell++;
          string t = element.GetType().Name;
          clrOther[t] = clrOther.TryGetValue(t, out int n) ? n + 1 : 1;
          continue;
        }
        MgdElement? definition = Speckle.Converters.MicroStation.Services.SharedCellPlacement.FindDefinition(
          sharedCell
        );
        if (definition == null)
        {
          defNull++;
          continue;
        }
        if (
          !Speckle.Converters.MicroStation.Services.SharedCellPlacement.TryCompute(
            sharedCell,
            definition,
            out BG.DTransform3d _
          )
        )
        {
          placementFail++;
          continue;
        }
        ok++;
      }

      log.AppendLine(
        $"sharedCells: total={sharedTotal} instanceable={ok} notClrSharedCell={notClrSharedCell} "
          + $"defNull={defNull} placementFail={placementFail}"
      );
      foreach (var kv in clrOther.OrderByDescending(k => k.Value).Take(5))
      {
        log.AppendLine($"  shared-cell clr type: {kv.Key} x{kv.Value}");
      }
      log.AppendLine(
        $"materials: elementsWithMaterial={materialHits} distinct={materialNames.Count} "
          + $"names=[{string.Join(", ", materialNames.Take(12))}]"
      );
    }
    catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
    {
      log.AppendLine($"shared-cell/material diagnostics failed: {ex.Message}");
    }
  }

  private static void DumpElementDetail(StringBuilder log, DPN.DgnModel model, MgdElement element)
  {
    string id = ((ulong)element.ElementId).ToString();
    log.AppendLine($"── element {id} ──");
    log.AppendLine($"  type: {element.TypeName} ({element.ElementType}) clrType: {element.GetType().Name}");
    try
    {
      DPN.LevelHandle? own = model.GetLevelCache()?.GetLevel(element.LevelId, true);
      log.AppendLine(
        own is { IsValid: true }
          ? $"  own level: {own.Name} code={own.LevelCode} display={own.Display} frozen={own.Frozen}"
          : "  own level: INVALID"
      );
      var (effName, effNum) = Speckle.Converters.MicroStation.ToSpeckle.Properties.PropertiesExtractor.GetLevelInfo(
        element
      );
      log.AppendLine($"  effective level: {effName} ({effNum})");
    }
    catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
    {
      log.AppendLine($"  level read failed: {ex.Message}");
    }
    if (element is MgdElements.DisplayableElement displayable)
    {
      if (displayable.CalcElementRange(out BG.DRange3d range).Equals(DPN.StatusInt.Success))
      {
        log.AppendLine(
          $"  range RAW: [{range.Low.X:G6},{range.Low.Y:G6},{range.Low.Z:G6}] .. [{range.High.X:G6},{range.High.Y:G6},{range.High.Z:G6}]"
        );
      }
      log.AppendLine($"  invisible: {element.IsInvisible}");
      if (displayable.GetBasisTransform(out BG.DTransform3d basis))
      {
        log.AppendLine(
          $"  basisTransform: rows=({basis.RowX.X:G4},{basis.RowX.Y:G4},{basis.RowX.Z:G4})({basis.RowY.X:G4},{basis.RowY.Y:G4},{basis.RowY.Z:G4})({basis.RowZ.X:G4},{basis.RowZ.Y:G4},{basis.RowZ.Z:G4}) t=({basis.Translation.X:G6},{basis.Translation.Y:G6},{basis.Translation.Z:G6})"
        );
      }
    }
    if (element is MgdElements.SharedCellElement sharedCell)
    {
      log.AppendLine(
        $"  sharedCell: name={sharedCell.CellName} scale=({sharedCell.Scale.X:G4},{sharedCell.Scale.Y:G4},{sharedCell.Scale.Z:G4})"
      );
      sharedCell.GetOrientation(out BG.DMatrix3d rot);
      log.AppendLine(
        $"  orientation rows: ({rot.RowX.X:G4},{rot.RowX.Y:G4},{rot.RowX.Z:G4})({rot.RowY.X:G4},{rot.RowY.Y:G4},{rot.RowY.Z:G4})({rot.RowZ.X:G4},{rot.RowZ.Y:G4},{rot.RowZ.Z:G4})"
      );
      sharedCell.GetTransformOrigin(out BG.DPoint3d origin);
      log.AppendLine($"  transformOrigin RAW: ({origin.X:G6},{origin.Y:G6},{origin.Z:G6})");
      DPN.DgnFile? file = sharedCell.DgnModel?.GetDgnFile();
      MgdElement? definition = file != null ? sharedCell.GetDefinition(file) : null;
      if (definition is MgdElements.DisplayableElement displayableDefinition)
      {
        displayableDefinition.GetTransformOrigin(out BG.DPoint3d defOrigin);
        log.AppendLine($"  defOrigin RAW: ({defOrigin.X:G6},{defOrigin.Y:G6},{defOrigin.Z:G6})");
        if (
          Speckle.Converters.MicroStation.Services.SharedCellPlacement.TryCompute(
            sharedCell,
            definition,
            out BG.DTransform3d placement
          )
        )
        {
          log.AppendLine(
            $"  computed placement: rows=({placement.RowX.X:G4},{placement.RowX.Y:G4},{placement.RowX.Z:G4})({placement.RowY.X:G4},{placement.RowY.Y:G4},{placement.RowY.Z:G4})({placement.RowZ.X:G4},{placement.RowZ.Y:G4},{placement.RowZ.Z:G4}) t=({placement.Translation.X:G6},{placement.Translation.Y:G6},{placement.Translation.Z:G6})"
          );
        }
      }
    }
  }

  /// <summary>Minimal settings store for the container-less probe path.</summary>
  private sealed class ProbeSettingsStore : IConverterSettingsStore<MicroStationConversionSettings>
  {
    private MicroStationConversionSettings? _current;

    public MicroStationConversionSettings Current =>
      _current ?? throw new InvalidOperationException("Probe settings not initialized.");

    public IDisposable Push(Func<MicroStationConversionSettings, MicroStationConversionSettings> nextContext)
    {
      MicroStationConversionSettings? previous = _current;
      _current = nextContext(Current);
      return new PopScope(this, previous);
    }

    public void Initialize(MicroStationConversionSettings context) => _current = context;

    private sealed class PopScope(ProbeSettingsStore owner, MicroStationConversionSettings? previous) : IDisposable
    {
      public void Dispose() => owner._current = previous;
    }
  }
}
