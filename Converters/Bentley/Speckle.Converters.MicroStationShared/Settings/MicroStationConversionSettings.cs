namespace Speckle.Converter.MicroStation.Settings;

/// <summary>
/// Immutable settings snapshot used by all geometry converters during a single send operation.
/// Created per-operation by <see cref="MicroStationConversionSettingsFactory"/> and pushed into
/// <see cref="Speckle.Converters.Common.IConverterSettingsStore{T}"/>.
/// </summary>
/// <param name="SpeckleUnits">Target Speckle unit string (e.g. "m", "mm", "ft").</param>
/// <param name="IncludeInvisibleElements">
/// When true, invisible elements are included in the send operation.
/// </param>
public record MicroStationConversionSettings(string SpeckleUnits, bool IncludeInvisibleElements = false);
