// using Speckle.Converters.Common;
// using Speckle.Converters.Common.Objects;
// using Speckle.Converters.Rhino.ToSpeckle.Encoding;
//
// namespace Speckle.Converters.Rhino.ToSpeckle.Raw;
//
// public class BrepXToSpeckleConverter(IConverterSettingsStore<RhinoConversionSettings> settingsStore)
//   : ITypedConverter<RG.Brep, SO.RawEncoding>
// {
//   public SO.RawEncoding Convert(RG.Brep target) => RawEncodingCreator.Encode(target, settingsStore.Current.Document);
// }
//
// public class ExtrusionXToSpeckleConverter(IConverterSettingsStore<RhinoConversionSettings> settingsStore)
//   : ITypedConverter<RG.Extrusion, SO.RawEncoding>
// {
//   public SO.RawEncoding Convert(RG.Extrusion target) => RawEncodingCreator.Encode(target, settingsStore.Current.Document);
// }
//
// public class SubDXToSpeckleConverter(IConverterSettingsStore<RhinoConversionSettings> settingsStore)
//   : ITypedConverter<RG.SubD, SO.RawEncoding>
// {
//   public SO.RawEncoding Convert(RG.SubD target) => RawEncodingCreator.Encode(target, settingsStore.Current.Document);
// }
//
