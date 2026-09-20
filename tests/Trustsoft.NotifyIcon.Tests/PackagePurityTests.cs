using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Xunit;

namespace Trustsoft.NotifyIcon.Tests;

/// <summary>
/// The mechanical guard for the product premise: the shipped library has no runtime dependency
/// beyond WPF and exposes nothing beyond the documented type surface.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why these are tests and not a review rule.</b> R011 ("no runtime dependencies beyond WPF;
/// no <c>System.Windows.Forms</c>, no <c>H.NotifyIcon</c>, no WinRT contracts package") is the
/// reason this project exists (D001), and it is exactly the kind of promise that decays silently:
/// one convenient <c>PackageReference</c>, or one transitive helper that looks innocent, re-enters
/// the dependency graph and no ordinary test notices, because everything still compiles and
/// everything still passes. Making it an assertion turns the promise into a property of the build.
/// </para>
/// <para>
/// <b>What is asserted, and from which artifact.</b> Two different sources, on purpose: the
/// <em>declaration</em> (the csproj text on disk, read through the repository root) and the
/// <em>compiled result</em> (the assembly's referenced-assembly table and its exported types).
/// A guard that only read the csproj could be satisfied by a dependency arriving through a
/// <c>Directory.Build.props</c> or a project reference; a guard that only read the assembly would
/// miss a package reference that is not used yet. Both halves are cheap, so both are checked.
/// </para>
/// <para>
/// <b>The purity checks have no external service dependency.</b> They read files in this repository
/// and reflect over a loaded assembly; nothing here reaches the network, the shell or the desktop.
/// The one environmental assumption is that a Release build of the library happened first, which is
/// asserted by a dedicated test with a failure message that names the command to run.
/// </para>
/// </remarks>
public class PackagePurityTests
{
    /// <summary>The solution file that identifies the repository root.</summary>
    private const string SolutionFileName = "Trustsoft.NotifyIcon.sln";

    /// <summary>The library project directory, relative to the repository root.</summary>
    private static readonly string LibraryProjectDirectory = Path.Combine("src", "Trustsoft.NotifyIcon");

    /// <summary>The single assembly this repository builds and ships.</summary>
    private const string LibraryAssemblyName = "Trustsoft.NotifyIcon";

    /// <summary>The three target frameworks the library must build (D006).</summary>
    private static readonly string[] ExpectedTargetFrameworks =
    [
        "net8.0-windows",
        "net9.0-windows",
        "net10.0-windows",
    ];

    /// <summary>
    /// The command that produces the artifacts the three-TFM output check looks for.
    /// </summary>
    private const string ReleaseBuildCommand = "dotnet build Trustsoft.NotifyIcon.sln -c Release";

    /// <summary>
    /// The library csproj must declare no <c>PackageReference</c> at all.
    /// </summary>
    /// <remarks>
    /// The declaration is read from disk rather than inferred from the build output, because an
    /// unused package reference is invisible in the compiled assembly yet still lands in the
    /// dependency graph of every consumer. The source file is a git-tracked repository file, not a
    /// build artifact, so this test does not depend on where the compiler last put its output.
    /// </remarks>
    [Fact]
    public void Library_csproj_has_no_package_reference()
    {
        string projectPath = Path.Combine(RepositoryRoot(), LibraryProjectDirectory, "Trustsoft.NotifyIcon.csproj");

        Assert.True(File.Exists(projectPath), $"The library project file was not found at '{projectPath}'.");

        string projectText = File.ReadAllText(projectPath);

        Assert.False(
            projectText.Contains("<PackageReference", StringComparison.Ordinal),
            $"R011/D001: the shipped library must carry no package reference, but '{projectPath}' declares at least one. "
            + "If a dependency is genuinely required, that is a product decision to raise, not a line to add here.");
    }

    /// <summary>
    /// Every assembly the library was compiled against must be one the platform itself provides.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two independent assertions, because they catch different mistakes. The name check catches
    /// the forbidden dependencies by their identity, including the tray helpers that are the whole
    /// point of the rule (<c>H.NotifyIcon</c> matches the <c>NotifyIcon</c> fragment while
    /// <c>Trustsoft.NotifyIcon</c> is excluded by name). The platform-membership check catches the
    /// dependency whose name looks innocent - a third-party helper pulled in transitively would
    /// still have to appear in this table, and it would not be a shared-framework assembly.
    /// </para>
    /// <para>
    /// Platform membership is decided by <em>presence in the installed shared frameworks</em>
    /// rather than by "is the loaded file under the running framework directory": WPF assemblies
    /// live in the WindowsDesktop framework, a sibling of the runtime framework the test host
    /// itself runs in, so a single-directory check would reject <c>PresentationFramework</c>. The
    /// set is built from the framework directories on this machine, and the failure message names
    /// the offending assembly together with the location it resolves to, so a real regression is
    /// diagnosable from the test output alone.
    /// </para>
    /// </remarks>
    [Fact]
    public void Loaded_library_references_only_framework_assemblies()
    {
        Assembly library = typeof(TrayIcon).Assembly;
        AssemblyName[] referenced = library.GetReferencedAssemblies();

        Assert.NotEmpty(referenced);

        HashSet<string> platformAssemblies = GetPlatformAssemblyNames();

        Assert.False(
            platformAssemblies.Count == 0,
            $"No shared-framework assemblies were found, so this guard cannot judge anything. Runtime directory: '{RuntimeEnvironment.GetRuntimeDirectory()}'.");

        foreach (AssemblyName reference in referenced)
        {
            string name = reference.Name ?? string.Empty;

            Assert.False(
                name.Contains("System.Windows.Forms", StringComparison.Ordinal),
                $"R011/D001: the library references '{name}'. WinForms is the dependency this project exists to remove.");

            Assert.False(
                string.Equals(name, "System.Drawing", StringComparison.Ordinal)
                || name.StartsWith("System.Drawing.", StringComparison.Ordinal),
                $"R011/D001: the library references '{name}'. Icon ownership is implemented over P/Invoke precisely to avoid this package.");

            Assert.False(
                name.Contains("NotifyIcon", StringComparison.Ordinal)
                && !string.Equals(name, LibraryAssemblyName, StringComparison.Ordinal),
                $"R011/D001: the library references '{name}', another notification-area implementation.");

            Assert.True(
                platformAssemblies.Contains(name) || string.Equals(name, LibraryAssemblyName, StringComparison.Ordinal),
                $"R011: the library references '{name}', which is not an assembly of an installed shared framework "
                + $"(resolved location: '{DescribeResolution(name)}'). The shipped library may only depend on the platform.");
        }
    }

    /// <summary>
    /// The exported type set is exactly the three documented public types.
    /// </summary>
    /// <remarks>
    /// The interop types (<c>IShellApi</c>, <c>NOTIFYICONDATAW</c>, the Win32 helpers) are internal
    /// by design (D002/D010) and this is the assertion that keeps them that way: widening one of
    /// them to <c>public</c> compiles, passes every behavioural test and silently enlarges the API
    /// this project will have to support for the rest of its life. The observed list is part of the
    /// message, so a future addition forces a deliberate edit here rather than an accidental pass.
    /// </remarks>
    [Fact]
    public void Public_surface_is_only_TrayIcon_and_its_two_error_types()
    {
        Type[] exported = typeof(TrayIcon).Assembly.GetExportedTypes();

        Type[] expected =
        [
            typeof(TrayIcon),
            typeof(TrayIconException),
            typeof(TrayErrorEventArgs),
        ];

        // Compiler-generated types are filtered explicitly rather than tolerated wholesale: a
        // filter that ignored every nested type would also hide a genuinely public nested class.
        Type[] observed = [.. exported.Where(type => !IsCompilerGenerated(type))];

        string observedNames = string.Join(
            ", ",
            exported.Select(type => type.FullName ?? type.Name).OrderBy(name => name, StringComparer.Ordinal));

        Assert.True(
            observed.Length == expected.Length && expected.All(observed.Contains),
            $"The public surface must be exactly {{{string.Join(", ", expected.Select(type => type.FullName))}}}. "
            + $"Observed exported types: [{observedNames}] (compiler-generated types excluded). "
            + "If a new public type is intended, that is a deliberate API decision: update this test and D002/D010 together.");
    }

    /// <summary>
    /// The library csproj declares the three Windows TFMs of D006 and no .NET Framework target.
    /// </summary>
    /// <remarks>
    /// Read from the <c>TargetFrameworks</c> element rather than from a substring search of the
    /// whole file, so a mention in a comment cannot satisfy the assertion. Set equality is used:
    /// losing a target framework is a packaging decision, and gaining one (for example
    /// <c>net11.0-windows</c>) should be a deliberate edit here too.
    /// </remarks>
    [Fact]
    public void Library_targets_three_windows_tfms()
    {
        string projectPath = Path.Combine(RepositoryRoot(), LibraryProjectDirectory, "Trustsoft.NotifyIcon.csproj");

        Assert.True(File.Exists(projectPath), $"The library project file was not found at '{projectPath}'.");

        string projectText = File.ReadAllText(projectPath);
        Match match = Regex.Match(projectText, @"<TargetFrameworks>(?<tfms>[^<]*)</TargetFrameworks>", RegexOptions.Singleline);

        Assert.True(
            match.Success,
            $"'{projectPath}' has no <TargetFrameworks> element, so the three-TFM contract cannot be verified. "
            + $"Expected: {string.Join(";", ExpectedTargetFrameworks)}.");

        string[] declared =
        [
            .. match.Groups["tfms"].Value
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
        ];

        Assert.Equal(
            ExpectedTargetFrameworks.OrderBy(tfm => tfm, StringComparer.Ordinal),
            declared.OrderBy(tfm => tfm, StringComparer.Ordinal));

        Assert.DoesNotContain(declared, tfm => tfm.StartsWith("net4", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// A Release build of all three target frameworks leaves the library assembly in each output
    /// directory.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the only test in this class that depends on a build having been run, and it fails
    /// loudly when it has not - with the command to run in the message - rather than skipping
    /// silently. A green run therefore always means the three-TFM claim was actually observed;
    /// a skipped run would mean nothing was checked, which is the failure mode this project already
    /// had to fix once in its test harness (see <c>StaFact.cs</c>: a harness that silently did
    /// nothing read as a passing suite).
    /// </para>
    /// <para>
    /// Building the test project alone is not enough to satisfy it, and that is intentional: a
    /// project reference builds only the target framework the referencing project needs, so only
    /// <c>dotnet build Trustsoft.NotifyIcon.sln -c Release</c> (or an explicit multi-TFM build)
    /// produces all three directories.
    /// </para>
    /// </remarks>
    [Fact]
    public void Solution_build_outputs_exist_for_all_three_tfms()
    {
        string root = RepositoryRoot();
        List<string> missing = [];

        foreach (string targetFramework in ExpectedTargetFrameworks)
        {
            string assemblyPath = Path.Combine(
                root,
                LibraryProjectDirectory,
                "bin",
                "Release",
                targetFramework,
                $"{LibraryAssemblyName}.dll");

            if (!File.Exists(assemblyPath))
            {
                missing.Add(assemblyPath);
            }
        }

        Assert.True(
            missing.Count == 0,
            $"No Release build output was found for every target framework. Missing: [{string.Join(", ", missing)}]. "
            + $"Build it first: {ReleaseBuildCommand} (running the test project alone only builds the single target framework it needs).");
    }

    /// <summary>
    /// Finds the repository root by walking up from the test assembly until the solution file is
    /// found.
    /// </summary>
    /// <returns>The absolute path of the repository root.</returns>
    /// <exception cref="InvalidOperationException">
    /// The solution file is not in any ancestor directory of the test assembly. Thrown rather than
    /// silently substituting a guess, because every path-based test in this class would otherwise
    /// be measuring the wrong file - or nothing at all.
    /// </exception>
    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"No directory above '{AppContext.BaseDirectory}' contains '{SolutionFileName}', so the repository root could not be located. "
            + "These tests read repository files by absolute path and must not fall back to a guessed location.");
    }

    /// <summary>
    /// Collects the simple names of every assembly installed in the shared frameworks of the
    /// running .NET installation.
    /// </summary>
    /// <returns>The set of platform assembly names, compared case-insensitively.</returns>
    /// <remarks>
    /// The runtime framework directory and its sibling <c>WindowsDesktop</c> framework are both
    /// scanned. The <c>shared</c> root is recognised by name and simply skipped when the layout
    /// does not match (for example a self-contained deployment), which degrades the membership
    /// check into a name-only check instead of rejecting the whole framework.
    /// </remarks>
    private static HashSet<string> GetPlatformAssemblyNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        string runtimeDirectory = Path.GetFullPath(RuntimeEnvironment.GetRuntimeDirectory());

        AddAssemblyNames(runtimeDirectory, names);

        string? frameworkRoot = Path.GetDirectoryName(Path.GetDirectoryName(runtimeDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));

        if (frameworkRoot is not null && string.Equals(Path.GetFileName(frameworkRoot), "shared", StringComparison.OrdinalIgnoreCase))
        {
            foreach (string frameworkDirectory in Directory.EnumerateDirectories(frameworkRoot))
            {
                foreach (string versionDirectory in Directory.EnumerateDirectories(frameworkDirectory))
                {
                    AddAssemblyNames(versionDirectory, names);
                }
            }
        }

        return names;
    }

    /// <summary>
    /// Adds the simple names of the managed assemblies in one directory to <paramref name="names"/>.
    /// </summary>
    /// <param name="directory">The directory to scan.</param>
    /// <param name="names">The set to add to.</param>
    private static void AddAssemblyNames(string directory, HashSet<string> names)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (string file in Directory.EnumerateFiles(directory, "*.dll", SearchOption.TopDirectoryOnly))
        {
            names.Add(Path.GetFileNameWithoutExtension(file));
        }
    }

    /// <summary>
    /// Resolves an assembly name to a location for the failure message, or explains why it could
    /// not be resolved.
    /// </summary>
    /// <param name="name">The simple assembly name.</param>
    /// <returns>A human-readable description of where the assembly comes from.</returns>
    private static string DescribeResolution(string name)
    {
        try
        {
            Assembly loaded = Assembly.Load(new AssemblyName(name));
            return loaded.IsDynamic ? "dynamic" : loaded.Location;
        }
        catch (Exception ex)
        {
            return $"could not be resolved: {ex.GetType().Name}: {ex.Message}";
        }
    }

    /// <summary>
    /// Determines whether a type was emitted by the compiler rather than written by hand.
    /// </summary>
    /// <param name="type">The type to inspect.</param>
    /// <returns><see langword="true"/> for compiler-generated types.</returns>
    private static bool IsCompilerGenerated(Type type) =>
        type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false)
        || (type.IsNested && type.DeclaringType is not null && IsCompilerGenerated(type.DeclaringType));
}
