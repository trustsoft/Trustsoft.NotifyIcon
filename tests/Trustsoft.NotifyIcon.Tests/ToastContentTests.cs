using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using Xunit;

namespace Trustsoft.NotifyIcon.Tests;

/// <summary>
/// The S02 public content model: the shape a consumer fills in before a toast can be shown.
/// </summary>
/// <remarks>
/// <para>
/// <b>What a content-model test can and cannot assert.</b> These are dumb data types with no
/// behaviour, so there is no call to observe and no shell to record - the facts worth pinning are
/// the ones a later change could quietly move: the documented default of every field, the
/// complete vocabulary of each enum, the guarantee that <see cref="ToastContent.Buttons"/> is
/// never null, and the absence of behaviour on the types themselves. The XML the model produces
/// is a different contract, pinned separately by the payload tests.
/// </para>
/// <para>
/// <b>Why the documentation assertion is here.</b> The build suppresses CS1591 by name
/// (<c>Directory.Build.props</c>) because a compiler error is the wrong enforcement point during
/// development, so an undocumented public member would compile and ship. D010 wants the shipped
/// surface documented; this test makes that a property of the build output instead of a review
/// rule, reading the generated documentation file beside the assembly.
/// </para>
/// </remarks>
public sealed class ToastContentTests
{
    /// <summary>
    /// Every public type this slice adds, used by the documentation guard.
    /// </summary>
    private static readonly Type[] ContentTypes =
    [
        typeof(ToastContent),
        typeof(ToastButton),
        typeof(ToastImage),
        typeof(ToastSeverity),
        typeof(ToastSound),
        typeof(ToastImagePlacement),
    ];

    /// <summary>
    /// A freshly constructed content object carries exactly the documented defaults, so a consumer
    /// who sets only what they care about gets no surprise from the fields they did not set.
    /// </summary>
    /// <remarks>
    /// The defaults are part of the contract, not an implementation detail: an empty title rather
    /// than a null one, a null body rather than an empty line, <see cref="ToastSeverity.Default"/>
    /// rather than a scenario, and no image, launch argument, tag, group or expiry. Each default is
    /// the "say nothing" value, which is what lets the payload be built from a minimal content
    /// object.
    /// </remarks>
    [Fact]
    public void Content_defaults_are_the_documented_ones()
    {
        var content = new ToastContent();

        Assert.Equal(string.Empty, content.Title);
        Assert.Null(content.Body);
        Assert.Equal(ToastSeverity.Default, content.Severity);
        Assert.Null(content.Launch);
        Assert.Null(content.Image);
        Assert.NotNull(content.Buttons);
        Assert.Empty(content.Buttons);
        Assert.Null(content.Tag);
        Assert.Null(content.Group);
        Assert.Null(content.Expiry);
        Assert.Equal(ToastSound.Default, content.Sound);
    }

    /// <summary>
    /// Every field round-trips through its property, including the three non-XML fields, so the
    /// content object is a faithful carrier of what the consumer asked for.
    /// </summary>
    [Fact]
    public void Every_content_field_round_trips()
    {
        var expiry = new DateTimeOffset(2026, 9, 23, 14, 30, 0, TimeSpan.FromHours(3));
        var image = new ToastImage
        {
            Reference = "file:///C:/images/logo.png",
            Placement = ToastImagePlacement.Hero,
            CircleCrop = true,
        };

        var content = new ToastContent
        {
            Title = "Build finished",
            Body = "3 projects built in 12.4s",
            Severity = ToastSeverity.Reminder,
            Launch = "sample-toast-1",
            Image = image,
            Tag = "build",
            Group = "ci",
            Expiry = expiry,
            Sound = ToastSound.Silent,
        };

        content.Buttons.Add(new ToastButton { Text = "Open log", Arguments = "open-log" });

        Assert.Equal("Build finished", content.Title);
        Assert.Equal("3 projects built in 12.4s", content.Body);
        Assert.Equal(ToastSeverity.Reminder, content.Severity);
        Assert.Equal("sample-toast-1", content.Launch);
        Assert.Same(image, content.Image);
        Assert.Equal("file:///C:/images/logo.png", content.Image!.Reference);
        Assert.Equal(ToastImagePlacement.Hero, content.Image.Placement);
        Assert.True(content.Image.CircleCrop);
        Assert.Equal("build", content.Tag);
        Assert.Equal("ci", content.Group);
        Assert.Equal(expiry, content.Expiry);
        Assert.Equal(ToastSound.Silent, content.Sound);

        ToastButton button = Assert.Single(content.Buttons);
        Assert.Equal("Open log", button.Text);
        Assert.Equal("open-log", button.Arguments);
    }

    /// <summary>
    /// The nullable members accept <see langword="null"/> as the documented "not set" value, so a
    /// consumer can clear a field as well as set it.
    /// </summary>
    [Fact]
    public void Nullable_members_accept_null()
    {
        var content = new ToastContent
        {
            Body = "body",
            Launch = "launch",
            Image = new ToastImage(),
            Tag = "tag",
            Group = "group",
            Expiry = DateTimeOffset.UnixEpoch,
        };

        content.Body = null;
        content.Launch = null;
        content.Image = null;
        content.Tag = null;
        content.Group = null;
        content.Expiry = null;

        Assert.Null(content.Body);
        Assert.Null(content.Launch);
        Assert.Null(content.Image);
        Assert.Null(content.Tag);
        Assert.Null(content.Group);
        Assert.Null(content.Expiry);
    }

    /// <summary>
    /// <see cref="ToastContent.Buttons"/> is never null, and it is the same list instance every
    /// time it is read, so a consumer adds to the toast's buttons rather than to a copy.
    /// </summary>
    /// <remarks>
    /// This is what makes "never null" a property of the type: the property is initialized and has
    /// no setter, so no consumer can assign null to it or replace the list out from under a caller
    /// that is already holding it.
    /// </remarks>
    [Fact]
    public void Buttons_is_never_null_and_is_one_stable_list()
    {
        var content = new ToastContent();

        IList<ToastButton> buttons = content.Buttons;

        Assert.NotNull(buttons);
        Assert.Empty(buttons);
        Assert.Same(buttons, content.Buttons);

        buttons.Add(new ToastButton { Text = "Yes", Arguments = "yes" });
        buttons.Add(new ToastButton { Text = "No", Arguments = "no" });

        Assert.Same(buttons, content.Buttons);
        Assert.Equal(2, content.Buttons.Count);
        Assert.Equal(new[] { "Yes", "No" }, content.Buttons.Select(button => button.Text));
    }

    /// <summary>
    /// <see cref="ToastContent.Buttons"/> has no setter, which is the mechanism behind "never
    /// null".
    /// </summary>
    /// <remarks>
    /// Asserted through reflection rather than through a compile-time omission, because the
    /// guarantee is part of the public contract: a setter added later would make
    /// <c>content.Buttons = null</c> compile and every "never null" claim in the documentation
    /// false.
    /// </remarks>
    [Fact]
    public void Buttons_property_has_no_setter()
    {
        PropertyInfo property = typeof(ToastContent).GetProperty(nameof(ToastContent.Buttons))!;

        Assert.NotNull(property);
        Assert.True(property.CanRead);
        Assert.False(property.CanWrite);
        Assert.Equal(typeof(IList<ToastButton>), property.PropertyType);
    }

    /// <summary>
    /// The severity vocabulary is exactly the four documented values, with the ordinals pinned so
    /// a reordering is a deliberate edit.
    /// </summary>
    /// <remarks>
    /// D053 fixes the vocabulary as Default/Reminder/Alarm/Urgent. The numbers are the library's
    /// own (the payload builder renders lower-case names, so there is no numeric wire identity),
    /// but pinning them keeps a future insertion from silently renumbering an existing member -
    /// which would matter to a consumer that persisted an enum value.
    /// </remarks>
    [Fact]
    public void Severity_vocabulary_is_the_four_documented_values()
    {
        Assert.Equal(
            new[] { ToastSeverity.Default, ToastSeverity.Reminder, ToastSeverity.Alarm, ToastSeverity.Urgent },
            Enum.GetValues<ToastSeverity>());

        Assert.Equal(0, (int)ToastSeverity.Default);
        Assert.Equal(1, (int)ToastSeverity.Reminder);
        Assert.Equal(2, (int)ToastSeverity.Alarm);
        Assert.Equal(3, (int)ToastSeverity.Urgent);
    }

    /// <summary>
    /// The sound vocabulary is exactly the two documented values, with the ordinals pinned.
    /// </summary>
    /// <remarks>
    /// Two states rather than a <c>bool</c>, because the enum can grow to named system sounds
    /// additively while a boolean cannot. <see cref="ToastSound.Default"/> means "no audio element
    /// is written", not "play a specific default sound".
    /// </remarks>
    [Fact]
    public void Sound_vocabulary_is_default_and_silent()
    {
        Assert.Equal(new[] { ToastSound.Default, ToastSound.Silent }, Enum.GetValues<ToastSound>());
        Assert.Equal(0, (int)ToastSound.Default);
        Assert.Equal(1, (int)ToastSound.Silent);
    }

    /// <summary>
    /// The image placement vocabulary is exactly the two placements the toast schema defines, with
    /// the ordinals pinned.
    /// </summary>
    /// <remarks>
    /// The names are the schema's own spellings in camel case, so a consumer reading the
    /// documentation and the XML sees the same word. There is no third placement to expose.
    /// </remarks>
    [Fact]
    public void Image_placement_vocabulary_is_app_logo_override_and_hero()
    {
        Assert.Equal(
            new[] { ToastImagePlacement.AppLogoOverride, ToastImagePlacement.Hero },
            Enum.GetValues<ToastImagePlacement>());

        Assert.Equal(0, (int)ToastImagePlacement.AppLogoOverride);
        Assert.Equal(1, (int)ToastImagePlacement.Hero);
    }

    /// <summary>
    /// A freshly constructed image says only what the schema's comment already says: replace the
    /// app logo, do not crop.
    /// </summary>
    [Fact]
    public void Image_defaults_are_the_documented_ones()
    {
        var image = new ToastImage();

        Assert.Equal(string.Empty, image.Reference);
        Assert.Equal(ToastImagePlacement.AppLogoOverride, image.Placement);
        Assert.False(image.CircleCrop);
    }

    /// <summary>
    /// A freshly constructed button has two empty strings rather than nulls, so the payload
    /// builder never has to decide what a null label or argument means.
    /// </summary>
    [Fact]
    public void Button_defaults_are_the_documented_ones()
    {
        var button = new ToastButton();

        Assert.Equal(string.Empty, button.Text);
        Assert.Equal(string.Empty, button.Arguments);
    }

    /// <summary>
    /// The content classes carry no behaviour: they are sealed, their only public constructor is
    /// the parameterless one, and their only public members are the property accessors and the
    /// members every object inherits.
    /// </summary>
    /// <remarks>
    /// This is the mechanical form of "a dumb data shape". A method on one of these types would
    /// mean behaviour that the notifier is supposed to own - validation, normalisation or a shell
    /// call - and it would arrive with a public signature the project would then have to support.
    /// The guard makes such an addition a deliberate edit rather than an accidental one.
    /// </remarks>
    [Theory]
    [InlineData(typeof(ToastContent))]
    [InlineData(typeof(ToastButton))]
    [InlineData(typeof(ToastImage))]
    public void Content_classes_carry_no_behaviour(Type type)
    {
        Assert.True(type.IsSealed, $"{type.Name} must be sealed: the content model is a data shape, not a base class.");

        ConstructorInfo[] constructors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        ConstructorInfo constructor = Assert.Single(constructors);

        Assert.Empty(constructor.GetParameters());

        MethodInfo[] methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        string[] behavioural = [.. methods.Where(method => !method.IsSpecialName).Select(method => method.Name)];

        Assert.True(
            behavioural.Length == 0,
            $"{type.Name} carries public method(s) [{string.Join(", ", behavioural)}]. The content model is data only: "
            + "behaviour belongs to the notifier that shows it. If the method is intended, that is a deliberate API decision.");

        FieldInfo[] fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        Assert.True(fields.Length == 0, $"{type.Name} exposes public field(s) [{string.Join(", ", fields.Select(field => field.Name))}]; the content model is property-based.");

        Assert.Empty(type.GetEvents(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));
    }

    /// <summary>
    /// Every public type and member this slice adds is documented in the generated documentation
    /// file that ships beside the assembly (D010).
    /// </summary>
    /// <remarks>
    /// <para>
    /// CS1591 is suppressed by name in <c>Directory.Build.props</c>, so the compiler does not fail
    /// on an undocumented member; this test is the enforcement point instead. It reads the
    /// <c>.xml</c> the SDK generates beside the library assembly, which is the same file that
    /// travels inside the package, so a member documented only in spirit does not pass.
    /// </para>
    /// <para>
    /// Enum members appear as fields in the documentation, hence the <c>F:</c> prefix for the enum
    /// branch. Types are checked with <c>T:</c> and class members with <c>P:</c>.
    /// </para>
    /// </remarks>
    [Fact]
    public void Every_public_member_of_the_content_model_is_documented()
    {
        string documentationPath = Path.ChangeExtension(typeof(TrayIcon).Assembly.Location, ".xml");

        Assert.True(
            File.Exists(documentationPath),
            $"No generated documentation file was found at '{documentationPath}', so the documented surface cannot be checked. "
            + "Build the solution first: dotnet build Trustsoft.NotifyIcon.sln -c Release.");

        HashSet<string> documented = XDocument.Load(documentationPath)
            .Descendants("member")
            .Select(element => (string?)element.Attribute("name"))
            .Where(name => name is not null)
            .Select(name => name!)
            .ToHashSet(StringComparer.Ordinal);

        List<string> missing = [];

        foreach (Type type in ContentTypes)
        {
            if (!documented.Contains($"T:{type.FullName}"))
            {
                missing.Add($"T:{type.FullName}");
            }

            if (type.IsEnum)
            {
                foreach (string name in Enum.GetNames(type))
                {
                    if (!documented.Contains($"F:{type.FullName}.{name}"))
                    {
                        missing.Add($"F:{type.FullName}.{name}");
                    }
                }

                continue;
            }

            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (!documented.Contains($"P:{type.FullName}.{property.Name}"))
                {
                    missing.Add($"P:{type.FullName}.{property.Name}");
                }
            }
        }

        Assert.True(
            missing.Count == 0,
            $"D010: {missing.Count} public member(s) of the S02 content model carry no XML documentation: [{string.Join(", ", missing)}]. "
            + "CS1591 is suppressed in Directory.Build.props, so nothing else would notice; document the member in English.");
    }

    /// <summary>
    /// The three content classes live in the library's public namespace, so a consumer writes the
    /// same <c>using</c> they already have.
    /// </summary>
    [Fact]
    public void Content_types_live_in_the_library_namespace()
    {
        Assert.All(ContentTypes, type => Assert.Equal("Trustsoft.NotifyIcon", type.Namespace));
    }
}
