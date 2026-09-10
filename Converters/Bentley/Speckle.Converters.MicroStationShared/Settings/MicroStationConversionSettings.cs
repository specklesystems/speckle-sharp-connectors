namespace Speckle.Converters.MicroStation.Settings;

/// <summary>
/// Immutable settings snapshot used by all geometry converters during a single send operation.
/// Created per-operation by <see cref="MicroStationConversionSettingsFactory"/> and pushed into
/// <see cref="Speckle.Converters.Common.IConverterSettingsStore{T}"/>.
/// </summary>
/// <param name="ActiveModel">The active design model — conversion-time services (color map, level
/// cache, material resolution) key off it. Held for the duration of one send operation only.</param>
/// <param name="SpeckleUnits">Target Speckle unit string (e.g. "m", "mm", "ft") — the active model's master unit.</param>
/// <param name="UorPerMaster">
/// Units-of-resolution per master unit of the active model. DgnPlatformNET element geometry getters
/// (CurveVector points, PolyfaceHeader vertices, basis transforms) return UOR coordinates — the
/// native DgnPlatform convention — so every coordinate is scaled by 1/UorPerMaster on the way out.
/// <see cref="Speckle.Converters.MicroStation.Services.GeometryMapper"/> is the single choke point;
/// the `Speckle probe` keyin logs a range-vs-UorPerMaster check to verify against a live model.
/// </param>
/// <param name="GlobalOriginX">Active model global origin (UORs) — subtracted before scaling, matching dgnextract.</param>
/// <param name="IncludeReferenceAttachments">When true, reference attachments are gathered and sent in the master frame.</param>
public record MicroStationConversionSettings(
  DPN.DgnModel ActiveModel,
  string SpeckleUnits,
  double UorPerMaster,
  double GlobalOriginX,
  double GlobalOriginY,
  double GlobalOriginZ,
  bool IncludeReferenceAttachments = true
);
