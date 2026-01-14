namespace Speckle.Importers.JobProcessor;

public static class SupportedFileTypes
{
  /// <summary>
  /// This is the full list of file extensions that this job processor will look for jobs with that extension
  /// This also happens to be the full list of file types that Rhino 8 supports.,
  /// </summary>
  private static readonly string[] s_fileTypes =
  [
    "3dm", // Rhino 3D Model (except ver 1.x save)
    "3dmbak", // Rhino 3D Model Backup
    "rws", // Rhino Worksession
    "3mf", // 3MF
    "3ds", // 3D Studio
    "amf", // AMF
    "ai", // Adobe Illustrator
    "dwg", // AutoCAD Drawing
    "dxf", // AutoCAD Drawing Exchange
    "x", // DirectX
    "e57", // E57
    "dst", // Embroidery
    "exp",
    "dst", // Encapsulated PostScript
    "exp",
    "off", // Geomview OFF
    "gf", // GHS Geometry
    "gft",
    "gltf", // GL Transmission Format
    "glb",
    "gts", // GTS (GNU Triangulated Surface)
    "igs", // IGES
    "iges",
    "lwo", // Lightwave
    "dgn", // Microstation
    "fbx", // MotionBuilder
    "scn", // NextEngine Scan
    "obj", // OBJ (Wavefront)
    "pdf", // PDF
    "ply", // PLY
    "asc", // Points
    "csv",
    "xyz",
    "pts",
    "cgo_ascii", // Points
    "cgo_asci",
    "txt", // Points
    "raw", // Raw Triangles
    "m", // Recon M
    "svg", // Scalable Vector Graphics
    "skp", // SketchUp
    "slc", // Slice
    "sldprt", // SOLIDWORKS
    "sldasm",
    "stp", // STEP
    "step",
    "stl", // STL (Stereolithography)
    "vda", // VDA
    "wrl", // VRML/Open Inventor
    "vrml",
    "iv",
    "gdf", // WAMIT
    "zpr", // Zcorp (3D Systems)
  ];

  // This is very dumb workaround to the fact our server has been enqueuing
  // mixed type file extensions since we implemented model ingestion API
  // I'd really rather the server filter this server side,
  // but for now as a workaround we'll process both lower and upper cases.
  // https://linear.app/speckle/issue/CXPLA-367
  public static readonly string[] FileTypes = s_fileTypes
    .Concat(s_fileTypes.Select(x => x.ToUpperInvariant()))
    .ToArray();
}
