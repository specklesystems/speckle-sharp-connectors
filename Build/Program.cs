using System.IO.Compression;
using Build;
using GlobExpressions;
using Microsoft.Build.Construction;
using static Bullseye.Targets;
using static SimpleExec.Command;

const string CLEAN = "clean";
const string RESTORE = "restore";
const string BUILD = "build";
const string BUILD_LINUX = "build-linux";
const string TEST = "test";
const string TEST_ONLY = "test-only";
const string FORMAT = "format";
const string ZIP = "zip";
const string VERSION = "version";
const string RESTORE_TOOLS = "restore-tools";
const string BUILD_SERVER_VERSION = "build-server-version";
const string CLEAN_LOCKS = "clean-locks";
const string CHECK_SOLUTIONS = "check-solutions";
const string DEEP_CLEAN = "deep-clean";
const string DEEP_CLEAN_LOCAL = "deep-clean-local";

//need to pass arguments
/*var arguments = new List<string>();
if (args.Length > 1)
{
  arguments = args.ToList();
  args = new[] { arguments.First() };
  //arguments = arguments.Skip(1).ToList();
}*/
void Build(string solution, string configuration)
{
  Console.WriteLine();
  Console.WriteLine();
  Console.WriteLine($"Building solution '{solution}' as '{configuration}'");
  Console.WriteLine();
  Run("dotnet", $"build .\\{solution} --configuration {configuration} --no-restore");
}
void Restore(string solution)
{
  Console.WriteLine();
  Console.WriteLine($"Restoring solution '{solution}'");
  Console.WriteLine();
  Run("dotnet", $"restore .\\{solution} --no-cache");
}
void DeleteFiles(string pattern)
{
  foreach (var f in Glob.Files(".", pattern))
  {
    Console.WriteLine("Found and will delete: " + f);
    File.Delete(f);
  }
}
void DeleteDirectories(string pattern)
{
  foreach (var f in Glob.Directories(".", pattern))
  {
    if (f.StartsWith("Build"))
    {
      continue;
    }
    Console.WriteLine("Found and will delete: " + f);
    Directory.Delete(f, true);
  }
}

void CleanSolution(string solution, string configuration)
{
  Console.WriteLine("Cleaning solution: " + solution);

  DeleteDirectories("**/bin");
  DeleteDirectories("**/obj");
  DeleteFiles("**/*.lock.json");
  Restore(solution);
  Build(solution, configuration);
}

string[] GetInstallerProjects()
{
  var root = Environment.CurrentDirectory;
  var projFile = Path.Combine(root, "affected.proj");
  Console.WriteLine("Affected project file: " + projFile);
  if (File.Exists(projFile))
  {
    Console.WriteLine(Environment.CurrentDirectory);
    var project = ProjectRootElement.Open(projFile);
    var references = project.ItemGroups.SelectMany(x => x.Items).Where(x => x.ItemType == "ProjectReference").ToList();
    var projs = new List<string>();
    foreach (var refe in references)
    {
      var referencePath = refe.Include[(root.Length + 1)..];
      referencePath = Path.GetDirectoryName(referencePath) ?? throw new InvalidOperationException();
      if (Path.DirectorySeparatorChar != '/')
      {
        referencePath = referencePath.Replace(Path.DirectorySeparatorChar, '/');
      }

      foreach (var proj in Consts.InstallerManifests.SelectMany(x => x.Projects))
      {
        if (proj.ProjectPath.Contains(referencePath))
        {
          projs.Add(refe.Include);
        }
      }
    }

    foreach (var proj in projs)
    {
      Console.WriteLine("Affected project being built: " + proj);
    }
    return projs.ToArray();
  }

  return Consts.Solutions;
}

var projects = GetInstallerProjects();

Target(
  CLEAN_LOCKS,
  () =>
  {
    DeleteFiles("**/*.lock.json");
    Restore("Speckle.Connectors.sln");
  }
);

Target(
  DEEP_CLEAN,
  () =>
  {
    CleanSolution("Speckle.Connectors.sln", "debug");
  }
);
Target(
  DEEP_CLEAN_LOCAL,
  () =>
  {
    CleanSolution("Local.sln", "local");
  }
);

Target(
  CLEAN,
  ForEach("**/output"),
  dir =>
  {
    IEnumerable<string> GetDirectories(string d)
    {
      return Glob.Directories(".", d);
    }

    void RemoveDirectory(string d)
    {
      if (Directory.Exists(d))
      {
        Console.WriteLine(d);
        Directory.Delete(d, true);
      }
    }

    foreach (var d in GetDirectories(dir))
    {
      RemoveDirectory(d);
    }
  }
);

Target(
  VERSION,
  async () =>
  {
    var (output, _) = await ReadAsync("dotnet", "minver -v w");
    output = output.Trim();
    Console.WriteLine($"Version: {output}");
    Run("echo", $"\"version={output}\" >> $GITHUB_OUTPUT");
  }
);

Target(
  RESTORE_TOOLS,
  () =>
  {
    Run("dotnet", "tool restore");
  }
);

Target(
  FORMAT,
  DependsOn(RESTORE_TOOLS),
  () =>
  {
    Run("dotnet", "csharpier --check .");
  }
);

Target(
  RESTORE,
  DependsOn(FORMAT),
  projects,
  s =>
  {
    Run("dotnet", $"restore {s} --locked-mode");
  }
);

Target(
  BUILD_SERVER_VERSION,
  DependsOn(RESTORE_TOOLS),
  () =>
  {
    Run("dotnet", "tool run dotnet-gitversion /output json /output buildserver");
  }
);

Target(
  BUILD,
  DependsOn(RESTORE),
  projects,
  s =>
  {
    var version = Environment.GetEnvironmentVariable("GitVersion_FullSemVer") ?? "3.0.0-localBuild";
    var fileVersion = Environment.GetEnvironmentVariable("GitVersion_AssemblySemFileVer") ?? "3.0.0.0";
    Console.WriteLine($"Version: {version} & {fileVersion}");
    Run(
      "dotnet",
      $"build {s} -c Release --no-restore -warnaserror -p:Version={version} -p:FileVersion={fileVersion} -v:m"
    );
  }
);

Target(CHECK_SOLUTIONS, Solutions.CompareConnectorsToLocal);

Target(
  TEST,
  DependsOn(BUILD, CHECK_SOLUTIONS),
  Glob.Files(".", "**/*.Tests.csproj"),
  file =>
  {
    Run("dotnet", $"test {file} -c Release --no-build --no-restore --verbosity=minimal");
  }
);

Target(
  TEST_ONLY,
  DependsOn(FORMAT),
  Glob.Files(".", "**/*.Tests.csproj"),
  file =>
  {
    Run("dotnet", $"build {file} -c Release --no-incremental");
    Run(
      "dotnet",
      $"test {file} -c Release --no-build --verbosity=minimal /p:AltCover=true /p:AltCoverAttributeFilter=ExcludeFromCodeCoverage /p:AltCoverVerbosity=Warning"
    );
  }
);

Target(
  BUILD_LINUX,
  DependsOn(FORMAT),
  Glob.Files(".", "**/Speckle.Importers.Ifc.csproj"),
  file =>
  {
    Run("dotnet", $"restore {file} --locked-mode");
    var version = Environment.GetEnvironmentVariable("GitVersion_FullSemVer") ?? "3.0.0-localBuild";
    var fileVersion = Environment.GetEnvironmentVariable("GitVersion_AssemblySemFileVer") ?? "3.0.0.0";
    Console.WriteLine($"Version: {version} & {fileVersion}");
    Run(
      "dotnet",
      $"build {file} -c Release --no-restore -warnaserror -p:Version={version} -p:FileVersion={fileVersion} -v:m"
    );

    RunAsync(
      "dotnet",
      $"pack {file} -c Release -o output --no-build -p:Version={version} -p:FileVersion={fileVersion} -v:m"
    );
  }
);

Target(
  ZIP,
  DependsOn(TEST),
  Consts.InstallerManifests,
  x =>
  {
    var outputDir = Path.Combine(".", "output");
    var slugDir = Path.Combine(outputDir, x.HostAppSlug);

    Directory.CreateDirectory(outputDir);
    Directory.CreateDirectory(slugDir);

    foreach (var asset in x.Projects)
    {
      var fullPath = Path.Combine(".", asset.ProjectPath, "bin", "Release", asset.TargetName);
      if (!Directory.Exists(fullPath))
      {
        throw new InvalidOperationException("Could not find: " + fullPath);
      }

      var assetName = Path.GetFileName(asset.ProjectPath);
      var connectorDir = Path.Combine(slugDir, assetName);

      Directory.CreateDirectory(connectorDir);
      foreach (var directory in Directory.EnumerateDirectories(fullPath, "*", SearchOption.AllDirectories))
      {
        Directory.CreateDirectory(directory.Replace(fullPath, connectorDir));
      }

      foreach (var file in Directory.EnumerateFiles(fullPath, "*", SearchOption.AllDirectories))
      {
        Console.WriteLine(file);
        File.Copy(file, file.Replace(fullPath, connectorDir), true);
      }
    }

    var outputPath = Path.Combine(outputDir, $"{x.HostAppSlug}.zip");
    File.Delete(outputPath);
    Console.WriteLine($"Zipping: '{slugDir}' to '{outputPath}'");
    ZipFile.CreateFromDirectory(slugDir, outputPath);
    // Directory.Delete(slugDir, true);
  }
);

Target("default", DependsOn(FORMAT, ZIP), () => Console.WriteLine("Done!"));

await RunTargetsAndExitAsync(args).ConfigureAwait(true);
