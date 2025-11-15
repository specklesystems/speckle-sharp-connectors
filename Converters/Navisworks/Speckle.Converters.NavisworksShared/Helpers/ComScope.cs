using System.Runtime.InteropServices;

namespace Speckle.Converter.Navisworks.Helpers;

/// <summary>
/// ComScope - Because Navisworks COM objects are like vampires that never die unless you tell them to.
///
/// This is a RAII (Resource Acquisition Is Initialization) wrapper for COM objects.
/// Think of it as a babysitter that makes sure COM objects get cleaned up properly
/// when you're done with them, preventing memory leaks that would otherwise
/// slowly consume your machine's RAM like a digital Pac-Man.
///
/// Why do we need this?
/// - Navisworks COM API creates objects that live forever unless explicitly released
/// - Forgetting to call Marshal.ReleaseComObject() = memory leak city
/// - Using statements + IDisposable = automatic cleanup when scope ends
/// - One less thing to remember = fewer bugs = happier developers
///
/// Usage: Wrap it in a 'using' statement and let C# handle the cleanup:
///   using var comThing = new ComScope&lt;SomeComType&gt;(myComObject);
///   // Do stuff with comThing.Value
///   // Automatic cleanup happens here when using block ends
///
/// Pro tip: This prevents the "why is Navisworks eating all my RAM?" conversations
/// that happen way too often with COM interop code.
/// </summary>
/// <typeparam name="T">The COM object type we're babysitting</typeparam>
public readonly struct ComScope<T>(T comObject, bool shouldRelease = true) : IDisposable
  where T : class
{
  public ComScope(T comObject)
    : this(comObject, false) { }

  private T ComObject { get; } = comObject;
  private bool ShouldRelease { get; } = shouldRelease;

  public T Value => ComObject;

  /// <summary>
  /// The magic cleanup method. This gets called automatically when the 'using' block ends.
  /// It tells the COM object "your services are no longer required" in a polite way
  /// that doesn't crash the application.
  /// </summary>
  public void Dispose()
  {
    // Only release if we're supposed to AND the object actually exists
    if (ShouldRelease && ComObject != null)
    {
      try
      {
        // This is the important bit - tells COM runtime to decrement reference count
        Marshal.ReleaseComObject(ComObject);
      }
      catch (InvalidComObjectException)
      {
        // Sometimes the object is already gone (maybe someonereleased, ignore
      }
    }
  }
}
