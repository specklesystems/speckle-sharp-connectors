namespace Speckle.Converters.RevitShared.Helpers;

public sealed record RevitConversionContext(DB.Document Document, string SpeckleUnits, double Tolerance = 0.01);
