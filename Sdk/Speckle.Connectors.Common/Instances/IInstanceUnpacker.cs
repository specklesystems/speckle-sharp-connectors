namespace Speckle.Connectors.Common.Instances;

public interface IInstanceUnpacker<THostObjectType>
{
  /// <summary>
  /// Given a list of host application objects, it will unpack them into atomic objects, instance proxies and instance proxy definitions.
  /// </summary>
  /// <param name="objects">Raw selection from the host application.</param>
  UnpackResult<THostObjectType> UnpackSelection(IEnumerable<THostObjectType> objects);
}
