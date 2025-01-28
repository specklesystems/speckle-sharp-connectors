using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.Rhino;
using Speckle.Converters.Rhino.ToSpeckle.Raw;
using Speckle.Testing;
using Xunit;

namespace Speckle.Converters.Rhino7.Tests;

public class EllipseToSpeckleConverterTests : MoqTest
{
  [Fact]
  public void Convert_Test()
  {
    var mockContextStack = Create<IConverterSettingsStore<RhinoConversionSettings>>();
    var planeConverter = Create<ITypedConverter<RG.Plane, SOG.Plane>>();
    var boxConverter = Create<ITypedConverter<RG.Box, SOG.Box>>();

    _ = new EllipseToSpeckleConverter(planeConverter.Object, boxConverter.Object, mockContextStack.Object);
  }
}
