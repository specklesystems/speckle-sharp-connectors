// TODO: We should scope 2d elements properly in revit. It is outside of reference geometry workflows right now.

// using System.Collections;
// using Speckle.Converters.Common.Objects;
// using Speckle.Objects.Data;
// using Speckle.Sdk.Common.Exceptions;
// using Speckle.Sdk.Models;
// using Speckle.Sdk.Models.Extensions;
//
// namespace Speckle.Converters.RevitShared.ToSpeckle;
//
// public class PlanViewToHostGeometryObjectConverter : ITypedConverter<Base, List<string>>
// {
//   private readonly ITypedConverter<SOG.Region, string> _regionToFilledRegionConverter;
//
//   public PlanViewToHostGeometryObjectConverter(ITypedConverter<SOG.Region, string> regionToFilledRegionConverter)
//   {
//     _regionToFilledRegionConverter = regionToFilledRegionConverter;
//   }
//
//   public List<string> Convert(Base target)
//   {
//     switch (target)
//     {
//       case SOG.Region region:
//         return new List<string>() { _regionToFilledRegionConverter.Convert(region) };
//
//       case DataObject dataObj:
//         List<string> results = new();
//
//         var displayValue = target.TryGetDisplayValue<Base>();
//         if ((displayValue is IList && !displayValue.Any()) || displayValue is null)
//         {
//           throw new ValidationException($"No display value found for {target.speckle_type}");
//         }
//         dataObj.displayValue.ForEach(x => results.AddRange(Convert(x)));
//
//         if (results.Count == 0)
//         {
//           throw new ConversionException($"No objects could be converted for {target.speckle_type}.");
//         }
//
//         return results;
//
//       default:
//         throw new ConversionException($"Objects of type {target.speckle_type} cannot be converted in 2d view.");
//     }
//   }
// }
