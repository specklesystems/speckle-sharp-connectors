namespace Speckle.Connectors.GrasshopperShared.Parameters;

public interface ISpecklePropertyGoo
{
  bool Equals(ISpecklePropertyGoo other);
}

[System.Diagnostics.CodeAnalysis.SuppressMessage(
  "Design",
  "CA1040:Avoid empty interfaces",
  Justification = "Needed to identify acceptable values of objects in collections"
)]
public interface ISpeckleCollectionObject { }
