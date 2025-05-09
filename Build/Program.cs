using System.IO.Compression;
using Build;
using GlobExpressions;
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
const string RESTORE_TOOLS = "restore-tools";
const string CLEAN_LOCKS = "clean-locks";
const string CHECK_SOLUTIONS = "check-solutions";
const string GEN_SOLUTIONS = "generate-solutions";
const string DEEP_CLEAN = "deep-clean";
const string DEEP_CLEAN_LOCAL = "deep-clean-local";
const string DETECT_AFFECTED = "detect-affected";

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
  Run("dotnet", $"build \".\\{solution}\" --configuration {configuration} --no-restore");
}
void Restore(string solution)
{
  Console.WriteLine();
  Console.WriteLine($"Restoring solution '{solution}'");
  Console.WriteLine();
  Run("dotnet", $"restore \".\\{solution}\" --no-cache");
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

Target(
  CLEAN_LOCKS,
  Consts.Solutions,
  s =>
  {
    DeleteFiles("**/*.lock.json");
    Restore(s);
  }
);

Target(
  DEEP_CLEAN,
  Consts.Solutions,
  s =>
  {
    CleanSolution(s, "debug");
  }
);
Target(
  DEEP_CLEAN_LOCAL,
  () =>
  {
    CleanSolution("Local.sln", "Local");
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
  RESTORE_TOOLS,
  () =>
  {
    Run("dotnet", "tool restore");
  }
);

Target(
  DETECT_AFFECTED,
  DependsOn(RESTORE_TOOLS),
  async () =>
  {
    foreach (var group in await Affected.GetAffectedProjectGroups())
    {
      Console.WriteLine("Affected project group being built: " + group.HostAppSlug);
    }
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
  DependsOn(FORMAT, DETECT_AFFECTED),
  Consts.Solutions,
  async s =>
  {
    var version = await Versions.ComputeVersion();
    var fileVersion = await Versions.ComputeFileVersion();
    Console.WriteLine($"Restoring: {s} - Version: {version} & {fileVersion}");
    await RunAsync("dotnet", $"restore \"{s}\" --locked-mode");
  }
);

Target(
  BUILD,
  DependsOn(RESTORE),
  Consts.Solutions,
  async s =>
  {
    var version = await Versions.ComputeVersion();
    var fileVersion = await Versions.ComputeFileVersion();
    Console.WriteLine($"Restoring: {s} - Version: {version} & {fileVersion}");
    await RunAsync(
      "dotnet",
      $"build \"{s}\" -c Release --no-restore -warnaserror -p:Version={version} -p:FileVersion={fileVersion} -v:m"
    );
  }
);

Target(CHECK_SOLUTIONS, Solutions.CompareConnectorsToLocal);
Target(GEN_SOLUTIONS, Solutions.GenerateSolutions);

Target(
  TEST,
  DependsOn(BUILD, CHECK_SOLUTIONS),
  async () =>
  {
    foreach (var s in await Affected.GetTestProjects())
    {
      await RunAsync("dotnet", $"test \"{s}\" -c Release --no-build --no-restore --verbosity=minimal");
    }
  }
);

//all tests on purpose
Target(
  TEST_ONLY,
  DependsOn(FORMAT),
  Glob.Files(".", "**/*.Tests.csproj"),
  file =>
  {
    Run("dotnet", $"build \"{file}\" -c Release --no-incremental");
    Run(
      "dotnet",
      $"test \"{file}\" -c Release --no-build --verbosity=minimal /p:AltCover=true /p:AltCoverAttributeFilter=ExcludeFromCodeCoverage /p:AltCoverVerbosity=Warning"
    );
  }
);

Target(
  BUILD_LINUX,
  DependsOn(FORMAT),
  Glob.Files(".", "**/Speckle.Importers.Ifc.csproj"),
  async file =>
  {
    await RunAsync("dotnet", $"restore \"{file}\" --locked-mode");
    var version = await Versions.ComputeVersion();
    var fileVersion = await Versions.ComputeFileVersion();
    Console.WriteLine($"Version: {version} & {fileVersion}");
    await RunAsync(
      "dotnet",
      $"build \"{file}\" -c Release --no-restore -warnaserror -p:Version={version} -p:FileVersion={fileVersion} -v:m"
    );

    await RunAsync(
      "dotnet",
      $"pack \"{file}\" -c Release -o output --no-build -p:Version={version} -p:FileVersion={fileVersion} -v:m"
    );
  }
);

Target(
  ZIP,
  DependsOn(TEST),
  async () =>
  {
    var version = await Versions.ComputeVersion();
    var fileVersion = await Versions.ComputeFileVersion();
    foreach (var group in await Affected.GetAffectedProjectGroups())
    {
      Console.WriteLine($"Zipping: {group.HostAppSlug} as {version}");
      var outputDir = Path.Combine(".", "output");
      var slugDir = Path.Combine(outputDir, group.HostAppSlug);

      Directory.CreateDirectory(outputDir);
      Directory.CreateDirectory(slugDir);

      foreach (var asset in group.Projects)
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

      var outputPath = Path.Combine(outputDir, $"{group.HostAppSlug}.zip");
      File.Delete(outputPath);
      Console.WriteLine($"Zipping: '{slugDir}' to '{outputPath}'");
      ZipFile.CreateFromDirectory(slugDir, outputPath);
    }

    string githubEnv = Environment.GetEnvironmentVariable("GITHUB_ENV") ?? "Unset";
    Console.WriteLine($"GITHUB_ENV: {githubEnv}");
    File.AppendAllText(githubEnv, $"SEMVER={version}{Environment.NewLine}");
    File.AppendAllText(githubEnv, $"FILE_VERSION={fileVersion}{Environment.NewLine}");
  }
);

Target("default", DependsOn(TEST), () => Console.WriteLine("Done!"));

await RunTargetsAndExitAsync(args).ConfigureAwait(true);
