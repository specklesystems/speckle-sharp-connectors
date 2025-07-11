using Speckle.Newtonsoft.Json;

namespace Speckle.Importers.Rhino;

public class RhinoImportResult
{
  [JsonProperty("success")]
  public bool Success { get; set; }

  [JsonProperty("commitId")]
  public string CommitId { get; set; }

  [JsonProperty("errorMessage")]
  public string ErrorMessage { get; set; }
}
