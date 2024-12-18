
using Speckle.Converters.Common.Registration;
using Speckle.InterfaceGenerator;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Common;

[GenerateAutoInterface]
public class SpeckleConverter(IConverterManager converterManager) :  ConverterBase(converterManager),ISpeckleConverter
{
  public virtual Base Convert(object target) => (Base)Convert(target, (manager, sourceType) => manager.GetSpeckleConverter(sourceType));
}
