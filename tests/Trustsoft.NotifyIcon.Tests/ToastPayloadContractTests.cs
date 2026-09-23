using System;
using Trustsoft.NotifyIcon.Interop;
using Xunit;

namespace Trustsoft.NotifyIcon.Tests;

/// <summary>
/// The exact-XML contract of the payload builder: one test per content shape, pinning the string
/// <c>IXmlDocumentIO.LoadXml</c> receives, plus escaping, the null-title rule and the explicit
/// assertion that the three non-XML fields produce nothing.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why exact strings rather than a parsed document.</b> The measurement recorded that the shell
/// accepts exactly what the hand-written writer emits (no indentation, no self-closing differences
/// from <c>XDocument</c>), so the string is the contract. Parsing the output and asserting on a
/// tree would accept a re-serialization the shell never sees; comparing the literal string makes a
/// reordering or an added attribute a deliberate edit to the test rather than a silent drift.
/// </para>
/// <para>
/// <b>Attribute order is a pinned choice.</b> XML attribute order carries no meaning, so the order
/// here is the library's own: <c>launch</c> then <c>scenario</c> on <c>&lt;toast&gt;</c>, and
/// <c>src</c>, <c>placement</c>, <c>hint-crop</c> on <c>&lt;image&gt;</c> (the schema's own syntax
/// order for the image element). The tests pin what the builder produces.
/// </para>
/// <para>
/// <b>The trap this suite exists to close.</b> Tag, group and expiry are notification-object
/// properties, not toast XML, so a string-only design can pass every XML assertion while silently
/// dropping three of the content model's fields. The combined-shape test pins their absence from
/// the document, and the show path applies them separately.
/// </para>
/// </remarks>
public sealed class ToastPayloadContractTests
{
    /// <summary>The minimal document: one title, no body, no attributes, no extra elements.</summary>
    private const string TitleOnlyXml =
        "<toast><visual><binding template=\"ToastGeneric\"><text>Title</text></binding></visual></toast>";

    /// <summary>Title only renders the minimal generic toast, exactly.</summary>
    [Fact]
    public void A_title_only_content_renders_the_minimal_generic_toast()
    {
        var content = new ToastContent { Title = "Title" };

        Assert.Equal(TitleOnlyXml, new ToastPayload(content).ToXml());
    }

    /// <summary>
    /// A body is exactly one more <c>&lt;text&gt;</c> child: the first line is the title, the
    /// second the body.
    /// </summary>
    [Fact]
    public void A_body_becomes_exactly_one_more_text_line()
    {
        var content = new ToastContent { Title = "Title", Body = "Body" };

        Assert.Equal(
            "<toast><visual><binding template=\"ToastGeneric\"><text>Title</text><text>Body</text></binding></visual></toast>",
            new ToastPayload(content).ToXml());
    }

    /// <summary>
    /// An absent or empty body omits the second text element entirely rather than emitting an empty
    /// one, which the shell would render as a blank line.
    /// </summary>
    /// <param name="body">The body to set, or <see langword="null"/>.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void An_absent_or_empty_body_omits_the_second_text_line(string? body)
    {
        var content = new ToastContent { Title = "Title", Body = body };

        Assert.Equal(TitleOnlyXml, new ToastPayload(content).ToXml());
    }

    /// <summary>The launch argument is an attribute on the <c>&lt;toast&gt;</c> element.</summary>
    [Fact]
    public void A_launch_argument_is_an_attribute_on_the_toast_element()
    {
        var content = new ToastContent { Title = "Title", Launch = "sample-toast-1" };

        Assert.Equal(
            "<toast launch=\"sample-toast-1\"><visual><binding template=\"ToastGeneric\"><text>Title</text></binding></visual></toast>",
            new ToastPayload(content).ToXml());
    }

    /// <summary>An absent or empty launch argument writes no attribute at all.</summary>
    /// <param name="launch">The launch argument to set, or <see langword="null"/>.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void An_absent_or_empty_launch_argument_writes_no_attribute(string? launch)
    {
        var content = new ToastContent { Title = "Title", Launch = launch };

        Assert.Equal(TitleOnlyXml, new ToastPayload(content).ToXml());
    }

    /// <summary>
    /// A non-default severity is the schema's lowercase <c>scenario</c> name, never the enum
    /// identifier and never a number (D053: severity is the native <c>ToastScenario</c>).
    /// </summary>
    /// <param name="severity">The severity to render.</param>
    /// <param name="scenario">The expected attribute value.</param>
    [Theory]
    [InlineData(ToastSeverity.Reminder, "reminder")]
    [InlineData(ToastSeverity.Alarm, "alarm")]
    [InlineData(ToastSeverity.Urgent, "urgent")]
    public void A_non_default_severity_writes_a_lowercase_scenario_attribute(ToastSeverity severity, string scenario)
    {
        var content = new ToastContent { Title = "Title", Severity = severity };

        Assert.Equal(
            $"<toast scenario=\"{scenario}\"><visual><binding template=\"ToastGeneric\"><text>Title</text></binding></visual></toast>",
            new ToastPayload(content).ToXml());
    }

    /// <summary>
    /// The default severity writes no <c>scenario</c> attribute: an absent attribute is how a toast
    /// says "default", and the schema has no <c>default</c> value.
    /// </summary>
    [Fact]
    public void The_default_severity_writes_no_scenario_attribute()
    {
        var content = new ToastContent { Title = "Title", Severity = ToastSeverity.Default };

        string xml = new ToastPayload(content).ToXml();

        Assert.Equal(TitleOnlyXml, xml);
        Assert.DoesNotContain("scenario", xml, StringComparison.Ordinal);
    }

    /// <summary>
    /// An image is rendered inside the binding, after the text children, with <c>src</c> before
    /// <c>placement</c>.
    /// </summary>
    [Fact]
    public void An_image_is_rendered_after_the_text_lines_with_src_before_placement()
    {
        var content = new ToastContent
        {
            Title = "Title",
            Image = new ToastImage { Reference = "file:///C:/images/logo.png" },
        };

        Assert.Equal(
            "<toast><visual><binding template=\"ToastGeneric\"><text>Title</text><image src=\"file:///C:/images/logo.png\" placement=\"appLogoOverride\"/></binding></visual></toast>",
            new ToastPayload(content).ToXml());
    }

    /// <summary>
    /// The placement is always written - <c>appLogoOverride</c> or <c>hero</c> - and the crop hint
    /// appears only when the content asked for it.
    /// </summary>
    /// <param name="placement">The image placement.</param>
    /// <param name="circleCrop">Whether the crop hint was requested.</param>
    /// <param name="imageElement">The expected image element.</param>
    [Theory]
    [InlineData(ToastImagePlacement.AppLogoOverride, false, "<image src=\"file:///img.png\" placement=\"appLogoOverride\"/>")]
    [InlineData(ToastImagePlacement.AppLogoOverride, true, "<image src=\"file:///img.png\" placement=\"appLogoOverride\" hint-crop=\"circle\"/>")]
    [InlineData(ToastImagePlacement.Hero, false, "<image src=\"file:///img.png\" placement=\"hero\"/>")]
    [InlineData(ToastImagePlacement.Hero, true, "<image src=\"file:///img.png\" placement=\"hero\" hint-crop=\"circle\"/>")]
    public void An_image_placement_is_always_written_and_the_crop_hint_only_when_requested(
        ToastImagePlacement placement,
        bool circleCrop,
        string imageElement)
    {
        var content = new ToastContent
        {
            Title = "Title",
            Image = new ToastImage { Reference = "file:///img.png", Placement = placement, CircleCrop = circleCrop },
        };

        Assert.Equal(
            $"<toast><visual><binding template=\"ToastGeneric\"><text>Title</text>{imageElement}</binding></visual></toast>",
            new ToastPayload(content).ToXml());
    }

    /// <summary>
    /// The image reference is XML-escaped so the document stays well-formed, but it is never
    /// URL-encoded or rewritten: the builder is not the layer that decides where the bytes live
    /// (a later slice owns producing the reference).
    /// </summary>
    [Fact]
    public void An_image_reference_is_xml_escaped_but_never_url_encoded()
    {
        var content = new ToastContent
        {
            Title = "Title",
            Image = new ToastImage { Reference = "https://example.com/a?x=1&y=2" },
        };

        string xml = new ToastPayload(content).ToXml();

        Assert.Equal(
            "<toast><visual><binding template=\"ToastGeneric\"><text>Title</text><image src=\"https://example.com/a?x=1&amp;y=2\" placement=\"appLogoOverride\"/></binding></visual></toast>",
            xml);

        // The query's own separators survive untouched: no percent-encoding was applied.
        Assert.Contains("?x=1&amp;y=2", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("%3F", xml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("%26", xml, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A silent toast writes the audio element as a child of <c>&lt;toast&gt;</c>, after
    /// <c>&lt;/visual&gt;</c>.
    /// </summary>
    [Fact]
    public void Silent_sound_writes_the_audio_element_after_the_visual()
    {
        var content = new ToastContent { Title = "Title", Sound = ToastSound.Silent };

        Assert.Equal(
            "<toast><visual><binding template=\"ToastGeneric\"><text>Title</text></binding></visual><audio silent=\"true\"/></toast>",
            new ToastPayload(content).ToXml());
    }

    /// <summary>
    /// The default sound writes no <c>&lt;audio&gt;</c> element: "default" is the absence of a
    /// request, not a request for a specific sound.
    /// </summary>
    [Fact]
    public void The_default_sound_writes_no_audio_element()
    {
        var content = new ToastContent { Title = "Title", Sound = ToastSound.Default };

        string xml = new ToastPayload(content).ToXml();

        Assert.Equal(TitleOnlyXml, xml);
        Assert.DoesNotContain("audio", xml, StringComparison.Ordinal);
    }

    /// <summary>
    /// Buttons become an <c>&lt;actions&gt;</c> list in list order, one
    /// <c>&lt;action content="..." arguments="..."/&gt;</c> each, and no activation type attribute
    /// (foreground activation is the schema default and D054 scopes v1 to the running application).
    /// </summary>
    [Fact]
    public void Buttons_become_actions_in_list_order_without_an_activation_type()
    {
        var content = new ToastContent { Title = "Title" };
        content.Buttons.Add(new ToastButton { Text = "Yes", Arguments = "yes" });
        content.Buttons.Add(new ToastButton { Text = "No", Arguments = "no" });

        string xml = new ToastPayload(content).ToXml();

        Assert.Equal(
            "<toast><visual><binding template=\"ToastGeneric\"><text>Title</text></binding></visual><actions><action content=\"Yes\" arguments=\"yes\"/><action content=\"No\" arguments=\"no\"/></actions></toast>",
            xml);
        Assert.DoesNotContain("activationType", xml, StringComparison.Ordinal);
    }

    /// <summary>An empty button list writes no <c>&lt;actions&gt;</c> element rather than an empty one.</summary>
    [Fact]
    public void An_empty_button_list_writes_no_actions_element()
    {
        var content = new ToastContent { Title = "Title" };

        string xml = new ToastPayload(content).ToXml();

        Assert.Equal(TitleOnlyXml, xml);
        Assert.DoesNotContain("actions", xml, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every shape at once, in the pinned order: <c>toast</c> attributes, the visual binding's text
    /// lines and image, then the audio element and the action list.
    /// </summary>
    [Fact]
    public void All_shapes_combined_render_in_the_pinned_order()
    {
        var content = new ToastContent
        {
            Title = "Title",
            Body = "Body",
            Launch = "launch-arg",
            Severity = ToastSeverity.Urgent,
            Image = new ToastImage
            {
                Reference = "file:///img.png",
                Placement = ToastImagePlacement.Hero,
                CircleCrop = true,
            },
            Sound = ToastSound.Silent,
            Tag = "tag",
            Group = "group",
            Expiry = DateTimeOffset.UnixEpoch,
        };
        content.Buttons.Add(new ToastButton { Text = "Ok", Arguments = "ok" });

        Assert.Equal(
            "<toast launch=\"launch-arg\" scenario=\"urgent\"><visual><binding template=\"ToastGeneric\"><text>Title</text><text>Body</text><image src=\"file:///img.png\" placement=\"hero\" hint-crop=\"circle\"/></binding></visual><audio silent=\"true\"/><actions><action content=\"Ok\" arguments=\"ok\"/></actions></toast>",
            new ToastPayload(content).ToXml());
    }

    /// <summary>
    /// Tag, group and expiry produce no XML at all: they are notification-object properties, so the
    /// document for content carrying them is byte-identical to the document for content carrying
    /// none. This is the assertion that stops a string-only design from silently dropping them.
    /// </summary>
    [Fact]
    public void Tag_group_and_expiry_produce_no_xml_at_all()
    {
        var withoutIdentity = new ToastContent { Title = "Title" };
        var withIdentity = new ToastContent
        {
            Title = "Title",
            Tag = "orange-tag-value",
            Group = "orange-group-value",
            Expiry = DateTimeOffset.UnixEpoch,
        };

        string xml = new ToastPayload(withIdentity).ToXml();

        Assert.Equal(new ToastPayload(withoutIdentity).ToXml(), xml);
        Assert.Equal(TitleOnlyXml, xml);

        // Not one of the three values, and no placeholder for any of them, reached the document.
        Assert.DoesNotContain("orange", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("expir", xml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("group", xml, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Caller text is escaped in both text nodes and attribute values, so a payload can never
    /// produce malformed XML (which the shell would report far away from the offending value).
    /// </summary>
    [Fact]
    public void Caller_text_is_escaped_in_text_nodes_and_attribute_values()
    {
        var content = new ToastContent
        {
            Title = "A & B",
            Body = "<body>",
            Launch = "launch \"quoted\" & <tag>",
            Image = new ToastImage { Reference = "https://example.com/a?x=1&y=2" },
        };
        content.Buttons.Add(new ToastButton { Text = "A & B", Arguments = "a&b" });

        string xml = new ToastPayload(content).ToXml();

        Assert.Equal(
            "<toast launch=\"launch &quot;quoted&quot; &amp; &lt;tag&gt;\"><visual><binding template=\"ToastGeneric\"><text>A &amp; B</text><text>&lt;body&gt;</text><image src=\"https://example.com/a?x=1&amp;y=2\" placement=\"appLogoOverride\"/></binding></visual><actions><action content=\"A &amp; B\" arguments=\"a&amp;b\"/></actions></toast>",
            xml);

        // The raw values never appear unescaped.
        Assert.DoesNotContain("A & B", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("<body>", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("\"quoted\"", xml, StringComparison.Ordinal);
    }

    /// <summary>
    /// A null title renders as an empty first line rather than a missing element: the shell still
    /// receives the text child it expects, and the document stays parseable.
    /// </summary>
    [Fact]
    public void A_null_title_renders_as_an_empty_first_line()
    {
        var content = new ToastContent { Title = null! };

        Assert.Equal(
            "<toast><visual><binding template=\"ToastGeneric\"><text></text></binding></visual></toast>",
            new ToastPayload(content).ToXml());
    }

    /// <summary>
    /// The builder refuses a null content rather than deferring the failure to the first field
    /// access.
    /// </summary>
    [Fact]
    public void The_payload_refuses_a_null_content()
    {
        Assert.Throws<ArgumentNullException>(() => new ToastPayload(null!));
    }

    /// <summary>The payload exposes the content it was built from, so the show path can trace its values.</summary>
    [Fact]
    public void The_payload_exposes_the_content_it_was_built_from()
    {
        var content = new ToastContent { Title = "Title", Launch = "launch" };

        Assert.Same(content, new ToastPayload(content).Content);
    }
}
