using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Xml;
using System.Xml.Linq;
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
    /// The package version M001 ships, pinned here so that changing it is a deliberate edit.
    /// </summary>
    /// <remarks>
    /// The number itself is a recorded decision (D037): 1.0.0, stable rather than a prerelease,
    /// because M001 delivers the v1 surface and D010 makes breaking changes major-only. The
    /// assertion is a set-equality style pin for the same reason <see cref="ExpectedTargetFrameworks"/>
    /// is one: a version bump is a release act, not a side effect of an unrelated edit, and a test
    /// that merely checked "some version exists" would let <c>1.0.0-preview.1</c> ship by accident.
    /// </remarks>
    private const string ExpectedPackageVersion = "1.0.0";

    /// <summary>
    /// The directory the nupkg is written to, relative to the repository root.
    /// </summary>
    private const string PackageOutputDirectoryName = "artifacts";

    /// <summary>
    /// The package ids the build may pull for the test graph, and for nothing else.
    /// </summary>
    /// <remarks>
    /// The shipped library carries no <c>PackageReference</c> at all (asserted separately); this
    /// list is the complete allowlist for the rest of the repository. It is deliberately short:
    /// adding to it means naming a package this project will restore on every clean machine, which
    /// is the kind of change R011/D001 exists to make visible.
    /// </remarks>
    private static readonly string[] TestFrameworkPackageIds =
    [
        "Microsoft.NET.Test.Sdk",
        "xunit",
        "xunit.runner.visualstudio",
        "Xunit.StaFact",
    ];

    /// <summary>
    /// The one package id a non-packable consumer demonstration may reference.
    /// </summary>
    /// <remarks>
    /// S07's consumer proof (and any consumer-shaped sample) must reference the *packed* library,
    /// because a project reference would measure something else. The reference is only permitted in
    /// a project that declares <c>IsPackable=false</c>, so a consumer instrument can never become a
    /// published artifact of its own.
    /// </remarks>
    private const string PackedLibraryPackageId = "Trustsoft.NotifyIcon";

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
    /// The exported type set is exactly the documented public types: the element, its two error
    /// types, the two click types the event surface needs and the two balloon types the balloon
    /// surface needs.
    /// </summary>
    /// <remarks>
    /// The interop types (<c>IShellApi</c>, <c>NOTIFYICONDATAW</c>, the Win32 helpers) are internal
    /// by design (D002/D010) and this is the assertion that keeps them that way: widening one of
    /// them to <c>public</c> compiles, passes every behavioural test and silently enlarges the API
    /// this project will have to support for the rest of its life. The observed list is part of the
    /// message, so a future addition forces a deliberate edit here rather than an accidental pass.
    /// S02 added <see cref="TrayIconClickEventArgs"/> and <see cref="TrayMenuActivation"/> - the
    /// args a click handler receives and the value that says whether a right click opens the menu -
    /// and S04 added <see cref="BalloonTipIcon"/> and <see cref="BalloonTipOptions"/> (D031) - the
    /// severity and option vocabulary of <c>ShowBalloonTip</c> - which is exactly the kind of
    /// widening this test exists to make deliberate.
    /// </remarks>
    [Fact]
    public void Public_surface_is_only_the_documented_types()
    {
        Type[] exported = typeof(TrayIcon).Assembly.GetExportedTypes();

        Type[] expected =
        [
            typeof(TrayIcon),
            typeof(TrayIconException),
            typeof(TrayErrorEventArgs),
            typeof(TrayIconClickEventArgs),
            typeof(TrayMenuActivation),
            typeof(BalloonTipIcon),
            typeof(BalloonTipOptions),
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
    /// <see cref="TrayIcon"/> exposes a real <c>ContextMenu</c> dependency property and the
    /// deliberate decision behind it - the exported type set did not grow for it - is recorded rather
    /// than assumed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the S03-to-S06 half of the surface contract, and it is asserted here rather than only
    /// in the boundary tests because of <em>what was not added</em>: the natural way to give an
    /// element a menu is to introduce a menu type of one's own, and that type would become public API
    /// this project has to support for the rest of its life (D002/D010/D015). WPF's own
    /// <see cref="System.Windows.Controls.ContextMenu"/> is used directly instead, so the context
    /// menu contributes no type of its own - the exported set has since grown to the seven
    /// documented types with S04's balloon enums (D031), and none of them is for the menu.
    /// </para>
    /// <para>
    /// The property is asserted to be owned by <see cref="TrayIcon"/> with a <see langword="null"/>
    /// default and a resolvable <see cref="DependencyPropertyDescriptor"/>, because those three are
    /// what makes it usable from C# <em>and</em> from a resource dictionary - the shape S06 resolves
    /// a <c>StaticResource</c> holding a menu through.
    /// </para>
    /// </remarks>
    [Fact]
    public void TrayIcon_exposes_the_context_menu_property_without_adding_a_public_type()
    {
        DependencyProperty property = TrayIcon.ContextMenuProperty;

        Assert.Equal("ContextMenu", property.Name);
        Assert.Equal(typeof(System.Windows.Controls.ContextMenu), property.PropertyType);
        Assert.Equal(typeof(TrayIcon), property.OwnerType);
        Assert.False(property.ReadOnly);
        Assert.Null(property.DefaultMetadata.DefaultValue);
        Assert.Null(property.GetMetadata(typeof(TrayIcon)).DefaultValue);

        // What a markup consumer looks through, exactly as the S01 property contract test does.
        DependencyPropertyDescriptor? descriptor = DependencyPropertyDescriptor.FromProperty(property, typeof(TrayIcon));

        Assert.NotNull(descriptor);
        Assert.Equal("ContextMenu", descriptor!.Name);
        Assert.Equal(property, descriptor.DependencyProperty);

        // The property is a member of TrayIcon, and no type was added to carry it: the observed set
        // still has to be exactly the documented types - seven since S04 (D031) added the two
        // balloon enums, none of them for the menu.
        Type[] exported = [.. typeof(TrayIcon).Assembly.GetExportedTypes().Where(type => !IsCompilerGenerated(type))];

        Assert.Equal(
            new[]
            {
                typeof(TrayIcon),
                typeof(TrayIconException),
                typeof(TrayErrorEventArgs),
                typeof(TrayIconClickEventArgs),
                typeof(TrayMenuActivation),
                typeof(BalloonTipIcon),
                typeof(BalloonTipOptions),
            }.OrderBy(type => type.FullName, StringComparer.Ordinal),
            exported.OrderBy(type => type.FullName, StringComparer.Ordinal));
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
    /// The library csproj declares the package metadata a consumer sees on the package page (R010).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Read from the XML rather than from the file text, so a property named in a comment (this
    /// project's comments are long and do name these properties on purpose) cannot satisfy the
    /// assertion, and a property that was commented out cannot either.
    /// </para>
    /// <para>
    /// Two of the assertions here are <em>absence</em> checks: <c>RepositoryUrl</c> and
    /// <c>PackageProjectUrl</c> must not be declared, because this repository has no remote and a
    /// fabricated URL in package metadata is worse than an absent one - it cannot be corrected after
    /// publication. Pinning the absence means adding one is a deliberate edit against a URL that
    /// actually resolves.
    /// </para>
    /// </remarks>
    [Fact]
    public void Library_csproj_declares_the_package_metadata_a_consumer_sees()
    {
        string root = RepositoryRoot();
        string projectPath = Path.Combine(root, LibraryProjectDirectory, "Trustsoft.NotifyIcon.csproj");

        Assert.True(File.Exists(projectPath), $"The library project file was not found at '{projectPath}'.");

        Dictionary<string, string> properties = ReadProperties(projectPath);

        Assert.Equal(LibraryAssemblyName, RequiredProperty(properties, "PackageId", projectPath));
        Assert.Equal(ExpectedPackageVersion, RequiredProperty(properties, "Version", projectPath));
        Assert.Equal("true", RequiredProperty(properties, "IsPackable", projectPath));

        string authors = RequiredProperty(properties, "Authors", projectPath);
        Assert.Equal("Trustsoft", authors);

        string description = RequiredProperty(properties, "Description", projectPath);

        Assert.True(
            description.Length >= 40,
            $"R010: the library declares a description of {description.Length} characters. A one-line placeholder is not the metadata a "
            + "consumer reads on the package page; describe what the package does, in English, in the library csproj.");

        // A proxy for the English requirement (D010), not a language detector: it catches a
        // non-ASCII description, which is the realistic way a Russian-language description would
        // arrive here. It deliberately says nothing about the other fields' vocabulary.
        Assert.True(
            description.All(char.IsAscii),
            "R010/D010: the package description must be English, but it contains non-ASCII characters. "
            + "If this is a typographic character rather than another language, widen this assertion deliberately.");

        string[] tags =
        [
            .. RequiredProperty(properties, "PackageTags", projectPath)
                .Split([';', ',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
        ];

        Assert.True(
            tags.Length >= 3 && tags.Any(tag => string.Equals(tag, "wpf", StringComparison.OrdinalIgnoreCase)),
            $"R010: package tags must be a real, searchable set (at least three, including 'wpf'). Declared: [{string.Join(", ", tags)}].");

        Assert.Equal("MIT", RequiredProperty(properties, "PackageLicenseExpression", projectPath));

        string licenseText = File.ReadAllText(Path.Combine(root, "LICENSE"));
        Assert.True(
            licenseText.StartsWith("MIT License", StringComparison.Ordinal),
            "R010/D010: PackageLicenseExpression says MIT, so the LICENSE file at the repository root must actually be the MIT text.");

        string readmeFileName = RequiredProperty(properties, "PackageReadmeFile", projectPath);
        Assert.Equal("README.md", readmeFileName);

        Assert.True(
            File.Exists(Path.Combine(root, readmeFileName)),
            $"R010: PackageReadmeFile names '{readmeFileName}', which does not exist at the repository root. "
            + "The README travels inside the package, so it has to be the repository's own README.");

        // Naming the readme is only half of it: the file has to be an actual package entry at the
        // package root, or the nupkg carries a <readme> element pointing at nothing.
        List<(string Include, string PackagePath)> packedAtRoot = [.. ReadPackedRootItems(projectPath)];

        foreach (string fileName in new[] { readmeFileName, "LICENSE" })
        {
            bool packed = packedAtRoot.Any(item => string.Equals(
                ResolveDeclaredPath(projectPath, item.Include),
                Path.Combine(root, fileName),
                StringComparison.OrdinalIgnoreCase));

            Assert.True(
                packed,
                $"R010: '{fileName}' is not packed into the nupkg at the package root. Add it as "
                + $"<None Include=\"...\" Pack=\"true\" PackagePath=\"\\\" /> in '{LibraryProjectDirectory}'. Entries found: "
                + $"[{string.Join(", ", packedAtRoot.Select(item => $"{item.Include} -> '{item.PackagePath}'"))}].");
        }

        string outputPath = ResolveDeclaredPath(
            projectPath,
            RequiredProperty(properties, "PackageOutputPath", projectPath));

        Assert.Equal(
            TrimSeparators(Path.Combine(root, PackageOutputDirectoryName)),
            TrimSeparators(outputPath));

        Assert.False(
            properties.ContainsKey("RepositoryUrl"),
            "This repository has no remote, so RepositoryUrl must stay out of the package metadata. "
            + "Add it deliberately once there is a URL that resolves; do not fill it in to silence a warning.");

        Assert.False(
            properties.ContainsKey("PackageProjectUrl"),
            "There is no project page for this package yet, so PackageProjectUrl must stay out rather than pointing somewhere invented.");
    }

    /// <summary>
    /// The compiled assembly carries the version the package metadata declares.
    /// </summary>
    /// <remarks>
    /// The metadata test above reads the declaration; this one reads the artifact, so a nupkg whose
    /// nuspec says 1.0.0 while the assembly inside it reports something else cannot pass. Only the
    /// assembly version's first three parts are compared, because the fourth is the revision; the
    /// informational version is compared with <c>StartsWith</c> because the SDK appends the source
    /// revision (<c>1.0.0+&lt;commit&gt;</c>) to it.
    /// </remarks>
    [Fact]
    public void Loaded_library_carries_the_declared_package_version()
    {
        Version expected = Version.Parse(ExpectedPackageVersion);
        Version? observed = typeof(TrayIcon).Assembly.GetName().Version;

        Assert.True(
            observed is not null,
            "The library assembly reports no version at all, so the number in its nuspec cannot be checked against the artifact.");

        Assert.Equal(expected.Major, observed!.Major);
        Assert.Equal(expected.Minor, observed!.Minor);
        Assert.Equal(expected.Build, observed!.Build);

        string? informational = typeof(TrayIcon).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        Assert.True(
            informational is not null && informational.StartsWith(ExpectedPackageVersion, StringComparison.Ordinal),
            $"The assembly's informational version is '{informational ?? "(absent)"}', which does not start with the declared package version '{ExpectedPackageVersion}'. "
            + "The version of the shipped artifact and the version in the nuspec have to be the same number; if the package version moved, move it in the csproj ``<Version>`` only.");
    }

    /// <summary>
    /// Nothing that exists to demonstrate or measure the library can be packed, and the instruments
    /// are invisible to a solution-level pack.
    /// </summary>
    /// <remarks>
    /// The sample and the test project are packable by default in the SDK, which is how a repository
    /// accidentally publishes its demo. The probe is a further case: it declares
    /// <c>IsPackable=false</c> for defence in depth, and it is absent from
    /// <c>Trustsoft.NotifyIcon.sln</c> so that <c>dotnet pack Trustsoft.NotifyIcon.sln</c> cannot even
    /// enumerate it. The consumer proof added by S07/T03 has to stay out of the solution for the same
    /// reason: it must not inherit the repository's build settings, or it would stop being a consumer.
    /// </remarks>
    [Fact]
    public void Non_shipping_projects_are_not_packable_and_the_instruments_stay_out_of_the_solution()
    {
        string root = RepositoryRoot();

        string[] nonShippingProjects =
        [
            Path.Combine("samples", "Trustsoft.NotifyIcon.Sample", "Trustsoft.NotifyIcon.Sample.csproj"),
            Path.Combine("tests", "Trustsoft.NotifyIcon.Tests", "Trustsoft.NotifyIcon.Tests.csproj"),
            Path.Combine("scripts", "probe-live", "probe-live.csproj"),
        ];

        foreach (string relativePath in nonShippingProjects)
        {
            string projectPath = Path.Combine(root, relativePath);

            Assert.True(File.Exists(projectPath), $"'{relativePath}' does not exist, so its packaging state cannot be checked.");

            Dictionary<string, string> properties = ReadProperties(projectPath);

            Assert.True(
                properties.TryGetValue("IsPackable", out string? isPackable)
                && string.Equals(isPackable, "false", StringComparison.OrdinalIgnoreCase),
                $"'{relativePath}' must declare <IsPackable>false</IsPackable>. Without it the SDK packs it by default and "
                + "`dotnet pack` produces a second nupkg next to the library's - a demonstration or an instrument would be published.");
        }

        string solutionText = File.ReadAllText(Path.Combine(root, SolutionFileName));

        Assert.False(
            solutionText.Contains("probe-live", StringComparison.OrdinalIgnoreCase),
            $"'{SolutionFileName}' lists probe-live. The probe is a verification instrument: it must stay out of the solution so that "
            + "a solution-level pack cannot reach it, and it takes no project reference to the library so its oracle stays independent.");

        Assert.False(
            solutionText.Contains("consumer-proof", StringComparison.OrdinalIgnoreCase),
            $"'{SolutionFileName}' lists a consumer proof. That project exists to consume the *packed* library with no inheritance from this "
            + "repository's build settings; putting it in the solution would give it both a project reference and those settings.");
    }

    /// <summary>
    /// No project or props file anywhere in the repository declares a package reference beyond the
    /// test framework and the packed library itself (R011).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The sweep covers <c>*.csproj</c>, <c>*.props</c> and <c>*.targets</c>, because a dependency
    /// can arrive through an imported file just as easily as through a project file, and a guard that
    /// only read the library csproj would not see it.
    /// </para>
    /// <para>
    /// The rules encoded here are: the four test-framework packages may only appear under a
    /// <c>tests</c> directory; the packed library may be referenced only by a project that declares
    /// <c>IsPackable=false</c> (a consumer-shaped demonstration, which must never become a published
    /// artifact of its own); the known forbidden dependencies are named explicitly so the failure
    /// message says which rule was broken; every reference must carry a pinned version rather than a
    /// floating one; and anything outside the allowlist fails, so a new dependency is a deliberate
    /// edit to this file rather than an accidental line in a csproj.
    /// </para>
    /// </remarks>
    [Fact]
    public void No_project_declares_a_package_reference_outside_the_test_framework_and_the_packed_library()
    {
        string root = RepositoryRoot();
        List<string> offenders = [];
        int filesInspected = 0;
        int referencesInspected = 0;

        foreach (string file in EnumerateBuildFiles(root))
        {
            filesInspected++;

            XDocument document;

            try
            {
                document = XDocument.Load(file);
            }
            catch (XmlException ex)
            {
                offenders.Add($"{RelativePath(root, file)} is not parseable as XML ({ex.Message}), so its dependency graph is unknown");
                continue;
            }

            foreach (XElement element in document.Descendants().Where(element => element.Name.LocalName == "PackageReference"))
            {
                referencesInspected++;

                string where = RelativePath(root, file);
                string id = ((string?)element.Attribute("Include") ?? string.Empty).Trim();
                string version = ((string?)element.Attribute("Version") ?? string.Empty).Trim();

                if (id.Length == 0)
                {
                    offenders.Add($"{where} declares a PackageReference with no Include, so what it pulls in cannot be judged");
                    continue;
                }

                bool isTestFramework = TestFrameworkPackageIds.Contains(id, StringComparer.OrdinalIgnoreCase);
                bool isPackedLibrary = string.Equals(id, PackedLibraryPackageId, StringComparison.OrdinalIgnoreCase);

                if (id.Contains("Windows.Forms", StringComparison.OrdinalIgnoreCase)
                    || id.Contains("System.Drawing", StringComparison.OrdinalIgnoreCase)
                    || id.Contains("Windows.SDK.Contracts", StringComparison.OrdinalIgnoreCase)
                    || (id.Contains("NotifyIcon", StringComparison.OrdinalIgnoreCase) && !isPackedLibrary))
                {
                    offenders.Add($"{where} declares '{id}', which R011/D001 forbids by name (WinForms, System.Drawing, a WinRT contracts package, or another tray implementation)");
                }
                else if (isTestFramework && !where.Contains("tests", StringComparison.OrdinalIgnoreCase))
                {
                    offenders.Add($"{where} declares test-framework package '{id}' outside the test project, so a shipping project would build against it");
                }
                else if (isPackedLibrary && !IsExplicitlyUnpackable(file))
                {
                    offenders.Add($"{where} references the packed library '{id}' without declaring <IsPackable>false</IsPackable>, so a consumer demonstration could be packed and published itself");
                }
                else if (!isTestFramework && !isPackedLibrary)
                {
                    offenders.Add(
                        $"{where} declares PackageReference '{id}', which is neither a test-framework package ("
                        + $"{string.Join(", ", TestFrameworkPackageIds)}) nor the packed library ({PackedLibraryPackageId}). "
                        + "R011: the shipped library may depend on the platform only - if this dependency is genuinely required, that is a product decision to raise");
                }

                if (version.Length == 0 || version.Contains('*', StringComparison.Ordinal))
                {
                    offenders.Add($"{where} declares '{id}' with version '{version}': every reference in this repository carries a pinned version so a restore is reproducible");
                }
            }
        }

        Assert.True(filesInspected > 0, $"No project or props file was found under '{root}', so the dependency sweep inspected nothing.");

        Assert.True(
            offenders.Count == 0,
            $"R011: {offenders.Count} package-reference violation(s) found among {referencesInspected} reference(s) in {filesInspected} file(s):\n"
            + string.Join("\n", offenders.Select(offender => " - " + offender)));
    }

    /// <summary>
    /// A Release build leaves the XML documentation next to the library assembly for every target
    /// framework, and the documentation is generated because the repository turns it on.
    /// </summary>
    /// <remarks>
    /// R010 has the XML documentation shipping inside the package, and the SDK packs the generated
    /// <c>.xml</c> beside the assembly automatically - which is only true while
    /// <c>GenerateDocumentationFile</c> stays on in <c>Directory.Build.props</c>. Both halves are
    /// asserted here (the repository-wide setting and the produced file, per framework) because
    /// switching the setting off would silently remove the file from the package with nothing in the
    /// nuspec to show for it. The check reads the comment for <c>TrayIcon</c> out of each file, so an
    /// empty or truncated documentation file does not pass.
    /// </remarks>
    [Fact]
    public void Release_build_emits_xml_documentation_beside_every_target_framework_assembly()
    {
        string root = RepositoryRoot();
        string propsPath = Path.Combine(root, "Directory.Build.props");

        Assert.True(File.Exists(propsPath), $"'{propsPath}' does not exist.");

        Dictionary<string, string> properties = ReadProperties(propsPath);

        Assert.True(
            properties.TryGetValue("GenerateDocumentationFile", out string? generate) && string.Equals(generate, "true", StringComparison.OrdinalIgnoreCase),
            "R010: Directory.Build.props must keep <GenerateDocumentationFile>true</GenerateDocumentationFile>. It is what puts "
            + "Trustsoft.NotifyIcon.xml next to each framework's assembly inside the nupkg; nothing in the nuspec would show its absence.");

        List<string> missing = [];

        foreach (string targetFramework in ExpectedTargetFrameworks)
        {
            string documentationPath = Path.Combine(
                root,
                LibraryProjectDirectory,
                "bin",
                "Release",
                targetFramework,
                $"{LibraryAssemblyName}.xml");

            if (!File.Exists(documentationPath))
            {
                missing.Add(documentationPath);
                continue;
            }

            string documentation = File.ReadAllText(documentationPath);

            Assert.True(
                documentation.Contains($"T:{LibraryAssemblyName}.TrayIcon", StringComparison.Ordinal),
                $"'{documentationPath}' contains no documentation entry for {LibraryAssemblyName}.TrayIcon, so the documentation is empty or truncated.");
        }

        Assert.True(
            missing.Count == 0,
            $"No XML documentation file was found for every target framework. Missing: [{string.Join(", ", missing)}]. "
            + $"Build it first: {ReleaseBuildCommand}.");
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

    /// <summary>
    /// Directory names the dependency sweep never descends into: build output, package output and
    /// tool state, none of which is a source of this repository's dependency graph.
    /// </summary>
    private static readonly string[] SkippedDirectoryNames =
    [
        ".git",
        ".gsd",
        ".gsd-worktrees",
        ".vs",
        "artifacts",
        "bin",
        "node_modules",
        "obj",
    ];

    /// <summary>
    /// Reads every property declared in the <c>PropertyGroup</c> elements of a build file.
    /// </summary>
    /// <param name="buildFilePath">The csproj, props or targets file to read.</param>
    /// <returns>The property values, keyed by property name, with later declarations winning.</returns>
    /// <remarks>
    /// XML rather than text, so a property named in a comment - and this repository's comments do
    /// name these properties on purpose - cannot satisfy an assertion, and a property that was
    /// commented out cannot either. Later groups win because that is MSBuild's own last-writer-wins
    /// semantics for an unconditional property.
    /// </remarks>
    private static Dictionary<string, string> ReadProperties(string buildFilePath)
    {
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        XDocument document = XDocument.Load(buildFilePath);

        foreach (XElement group in document.Descendants().Where(element => element.Name.LocalName == "PropertyGroup"))
        {
            foreach (XElement property in group.Elements())
            {
                properties[property.Name.LocalName] = property.Value.Trim();
            }
        }

        return properties;
    }

    /// <summary>
    /// Reads a property that must be declared, failing with the file's path when it is not.
    /// </summary>
    /// <param name="properties">The properties read from the build file.</param>
    /// <param name="name">The property name to read.</param>
    /// <param name="buildFilePath">The file the properties came from, named in the failure message.</param>
    /// <returns>The declared value.</returns>
    private static string RequiredProperty(Dictionary<string, string> properties, string name, string buildFilePath)
    {
        Assert.True(
            properties.TryGetValue(name, out string? value) && !string.IsNullOrWhiteSpace(value),
            $"'{buildFilePath}' does not declare a non-empty <{name}> element, so the packaging contract cannot be verified from it.");

        return value!;
    }

    /// <summary>
    /// Returns the items that are packed to the root of the nupkg.
    /// </summary>
    /// <param name="buildFilePath">The project file to read.</param>
    /// <returns>The declared include path and package path of every root-packed item.</returns>
    /// <remarks>
    /// A root package path is what <c>PackageReadmeFile</c> resolves against, so an item packed into
    /// a subfolder would satisfy a naive "the README is packed" check while leaving the nuspec's
    /// <c>&lt;readme&gt;</c> pointing at nothing.
    /// </remarks>
    private static IEnumerable<(string Include, string PackagePath)> ReadPackedRootItems(string buildFilePath)
    {
        XDocument document = XDocument.Load(buildFilePath);

        return document.Descendants()
            .Where(element => element.Name.LocalName == "None"
                && string.Equals((string?)element.Attribute("Pack"), "true", StringComparison.OrdinalIgnoreCase))
            .Select(element => (
                Include: (string?)element.Attribute("Include") ?? string.Empty,
                PackagePath: (string?)element.Attribute("PackagePath") ?? string.Empty))
            .Where(item => item.PackagePath.Trim() is "" or "/" or "\\");
    }

    /// <summary>
    /// Resolves a path declared in a build file the way MSBuild would, for the properties this
    /// repository actually uses.
    /// </summary>
    /// <param name="buildFilePath">The build file the path was declared in.</param>
    /// <param name="declaredPath">The declared value, possibly containing <c>$(MSBuildThisFileDirectory)</c>.</param>
    /// <returns>The absolute, normalized path.</returns>
    /// <remarks>
    /// Any other unresolvable property is a hard failure rather than a guess: silently treating
    /// <c>$(SolutionDir)</c> as literal text would make the assertion measure a nonexistent path and
    /// report a false failure (or worse, a false pass against a relative path that happens to exist).
    /// </remarks>
    private static string ResolveDeclaredPath(string buildFilePath, string declaredPath)
    {
        string projectDirectory =
            Path.GetDirectoryName(Path.GetFullPath(buildFilePath))! + Path.DirectorySeparatorChar;

        string substituted = declaredPath.Replace(
            "$(MSBuildThisFileDirectory)",
            projectDirectory,
            StringComparison.OrdinalIgnoreCase);

        Assert.False(
            substituted.Contains("$(", StringComparison.Ordinal),
            $"'{declaredPath}' in '{buildFilePath}' uses an MSBuild property this test cannot resolve, so the path it produces is unknown.");

        return Path.GetFullPath(substituted);
    }

    /// <summary>
    /// Removes the trailing directory separators so two paths can be compared textually.
    /// </summary>
    /// <param name="path">The path to trim.</param>
    /// <returns>The path without a trailing separator.</returns>
    private static string TrimSeparators(string path) =>
        path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    /// <summary>
    /// Enumerates every csproj, props and targets file that belongs to this repository's sources.
    /// </summary>
    /// <param name="root">The repository root to walk.</param>
    /// <returns>The build files, in no particular order.</returns>
    /// <remarks>
    /// Build output and tool state are skipped by name because a dependency guard that read a
    /// generated file (a restored package's props, for instance) would report dependencies this
    /// repository does not declare.
    /// </remarks>
    private static IEnumerable<string> EnumerateBuildFiles(string root)
    {
        var skipped = new HashSet<string>(SkippedDirectoryNames, StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<string>();

        pending.Push(root);

        while (pending.Count > 0)
        {
            string directory = pending.Pop();

            foreach (string child in Directory.EnumerateDirectories(directory))
            {
                string name = Path.GetFileName(child);

                if (name.StartsWith('.') || skipped.Contains(name))
                {
                    continue;
                }

                pending.Push(child);
            }

            foreach (string pattern in new[] { "*.csproj", "*.props", "*.targets" })
            {
                foreach (string file in Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly))
                {
                    yield return file;
                }
            }
        }
    }

    /// <summary>
    /// Formats a file path relative to the repository root for a failure message.
    /// </summary>
    /// <param name="root">The repository root.</param>
    /// <param name="file">The file to describe.</param>
    /// <returns>The relative path, with forward slashes on every platform.</returns>
    private static string RelativePath(string root, string file) =>
        Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');

    /// <summary>
    /// Determines whether a build file opts itself out of packing explicitly.
    /// </summary>
    /// <param name="buildFilePath">The build file to read.</param>
    /// <returns><see langword="true"/> when <c>IsPackable</c> is declared false.</returns>
    private static bool IsExplicitlyUnpackable(string buildFilePath) =>
        ReadProperties(buildFilePath).TryGetValue("IsPackable", out string? isPackable)
        && string.Equals(isPackable, "false", StringComparison.OrdinalIgnoreCase);
}
