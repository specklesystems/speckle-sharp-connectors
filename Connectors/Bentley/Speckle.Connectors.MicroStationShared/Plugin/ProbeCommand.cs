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
  public static void Run()
  {
    string logPath = Path.Combine(Path.GetTempPath(), "speckle-msprobe.log");
    var log = new StringBuilder();
    try
    {
      Probe(log);
    }
    catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
    {
      log.AppendLine($"!! probe crashed: {ex}");
    }
    File.WriteAllText(logPath, log.ToString(), Encoding.UTF8);
  }

  private static void Probe(StringBuilder log)
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
    var extractor = new DisplayValueExtractor(
      mapper,
      capture,
      polyface,
      primitives,
      appearance,
      text,
      NullLogger<DisplayValueExtractor>.Instance
    );
    var properties = new PropertiesExtractor(NullLogger<PropertiesExtractor>.Instance);

    var perType = new Dictionary<string, (int elements, int geoms, int empty, int errors, int ecProps)>();
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
        var extracted = extractor.Extract(element);
        entry.geoms += extracted.Count;
        if (extracted.Count == 0)
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

    log.AppendLine($"scanned: {scanned}");
    log.AppendLine("type | elements | geoms | empty | errors | ecPropKeys");
    foreach (var kv in perType.OrderByDescending(kv => kv.Value.elements))
    {
      var v = kv.Value;
      log.AppendLine($"{kv.Key} | {v.elements} | {v.geoms} | {v.empty} | {v.errors} | {v.ecProps}");
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
