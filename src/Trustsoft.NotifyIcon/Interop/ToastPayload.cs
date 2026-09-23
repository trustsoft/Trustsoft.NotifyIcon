using System.Text;

namespace Trustsoft.NotifyIcon.Interop;

/// <summary>
/// The toast payload: the XML the show path loads into a
/// <c>Windows.Data.Xml.Dom.XmlDocument</c> and hands to the shell, rendered from the public
/// <see cref="ToastContent"/> the consumer built.
/// </summary>
/// <remarks>
/// <para>
/// <b>Shaped by the T01 measurement.</b> The probe recorded the exact payload it showed
/// (<c>docs/TOAST-MEASUREMENT.md</c>, Contract 2): a <c>ToastGeneric</c> binding inside a
/// <c>&lt;toast&gt;</c> element whose <c>launch</c> attribute carries the argument string Windows
/// later reports to the activation callback. That shape is reproduced verbatim here - the
/// <c>launch</c> attribute is the only channel an unpackaged process receives on a body click, so
/// the element order and the attribute spelling are part of the contract, not cosmetic.
/// </para>
/// <para>
/// <b>A hand-written writer, deliberately.</b> The measurement recorded that
/// <c>IXmlDocumentIO.LoadXml</c> accepts exactly what this class emits, so the writer is extended
/// per content shape rather than replaced: <c>XDocument</c> would add indentation and self-close
/// elements differently, and any second rendering path would have to be kept byte-identical by
/// hand. There is exactly one escaping path in charge - <see cref="EscapeText"/> and
/// <see cref="EscapeAttribute"/> below - so a payload can only ever be well-formed XML.
/// </para>
/// <para>
/// <b>The rendered shapes, and their pinned order.</b> The <c>&lt;toast&gt;</c> element carries
/// <c>launch</c> and then <c>scenario</c> (both omitted when absent or default); the
/// <c>&lt;visual&gt;&lt;binding template="ToastGeneric"&gt;</c> carries one <c>&lt;text&gt;</c> child
/// per text line followed by an optional <c>&lt;image&gt;</c>; then, after <c>&lt;/visual&gt;</c>, an
/// optional <c>&lt;audio silent="true"/&gt;</c> and an optional <c>&lt;actions&gt;</c> list. The
/// attribute order is this library's pinned choice, not a schema requirement, and the contract
/// tests assert the exact strings so a reordering is a deliberate edit rather than an accident.
/// </para>
/// <para>
/// <b>Three fields produce no XML at all.</b> <see cref="ToastContent.Tag"/>,
/// <see cref="ToastContent.Group"/> and <see cref="ToastContent.Expiry"/> are properties of the
/// notification object the shell is handed, not attributes of the toast document - the schema has
/// no place to put them. They are deliberately absent here: a string-only design that silently
/// dropped them would pass every XML test, which is why the contract asserts their absence
/// explicitly and the show path applies them separately.
/// </para>
/// </remarks>
internal sealed class ToastPayload
{
    /// <summary>
    /// The binding template the measurement used: the modern template whose <c>&lt;text&gt;</c>
    /// children render as the toast's title and body lines.
    /// </summary>
    internal const string ToastGenericTemplate = "ToastGeneric";

    /// <summary>Initializes a payload over the public content model.</summary>
    /// <param name="content">The content to render; the payload keeps the instance it was given.</param>
    /// <exception cref="ArgumentNullException">The content is <see langword="null"/>.</exception>
    internal ToastPayload(ToastContent content)
    {
        Content = content ?? throw new ArgumentNullException(nameof(content));
    }

    /// <summary>
    /// Gets the content this payload was built from. The show path reads the launch argument and
    /// the title from here for its trace line; the payload does not copy the fields.
    /// </summary>
    internal ToastContent Content { get; }

    /// <summary>
    /// Renders the payload as the exact XML handed to <c>IXmlDocumentIO.LoadXml</c>.
    /// </summary>
    /// <returns>The document, with every caller-supplied value escaped.</returns>
    /// <remarks>
    /// The shape is the one the measurement recorded, extended per content shape:
    /// <c>&lt;toast [launch="..."] [scenario="..."]&gt;&lt;visual&gt;&lt;binding template="ToastGeneric"&gt;&lt;text&gt;title&lt;/text&gt;[&lt;text&gt;body&lt;/text&gt;][&lt;image .../&gt;]&lt;/binding&gt;&lt;/visual&gt;[&lt;audio silent="true"/&gt;][&lt;actions&gt;...&lt;/actions&gt;]&lt;/toast&gt;</c>.
    /// An absent or empty value omits its element or attribute rather than emitting one empty; a
    /// <see langword="null"/> title renders as an empty first line, never as a missing element.
    /// </remarks>
    internal string ToXml()
    {
        ToastContent content = Content;
        var xml = new StringBuilder(256);

        xml.Append("<toast");

        if (!string.IsNullOrEmpty(content.Launch))
        {
            xml.Append(" launch=\"");
            xml.Append(EscapeAttribute(content.Launch));
            xml.Append('"');
        }

        if (ScenarioName(content.Severity) is { } scenario)
        {
            xml.Append(" scenario=\"");
            xml.Append(scenario);
            xml.Append('"');
        }

        xml.Append("><visual><binding template=\"");
        xml.Append(ToastGenericTemplate);
        xml.Append("\"><text>");
        xml.Append(EscapeText(content.Title));
        xml.Append("</text>");

        if (!string.IsNullOrEmpty(content.Body))
        {
            xml.Append("<text>");
            xml.Append(EscapeText(content.Body));
            xml.Append("</text>");
        }

        if (content.Image is { } image)
        {
            // src first, then placement (always), then the optional crop hint - the schema's own
            // syntax order for the image element.
            xml.Append("<image src=\"");
            xml.Append(EscapeAttribute(image.Reference));
            xml.Append("\" placement=\"");
            xml.Append(PlacementName(image.Placement));
            xml.Append('"');

            if (image.CircleCrop)
            {
                xml.Append(" hint-crop=\"circle\"");
            }

            xml.Append("/>");
        }

        xml.Append("</binding></visual>");

        if (content.Sound == ToastSound.Silent)
        {
            xml.Append("<audio silent=\"true\"/>");
        }

        if (content.Buttons.Count > 0)
        {
            xml.Append("<actions>");

            foreach (ToastButton button in content.Buttons)
            {
                // No activationType attribute: foreground activation is the schema default and D054
                // scopes v1 to activation in the running application.
                xml.Append("<action content=\"");
                xml.Append(EscapeAttribute(button.Text));
                xml.Append("\" arguments=\"");
                xml.Append(EscapeAttribute(button.Arguments));
                xml.Append("\"/>");
            }

            xml.Append("</actions>");
        }

        xml.Append("</toast>");

        return xml.ToString();
    }

    /// <summary>
    /// Maps a severity onto its lowercase <c>scenario</c> attribute value, or
    /// <see langword="null"/> for the default (which writes no attribute at all: an absent scenario
    /// is how a toast says "default", and the schema has no <c>default</c> value).
    /// </summary>
    /// <param name="severity">The content's severity.</param>
    /// <returns>The attribute value, or <see langword="null"/>.</returns>
    private static string? ScenarioName(ToastSeverity severity) => severity switch
    {
        ToastSeverity.Reminder => "reminder",
        ToastSeverity.Alarm => "alarm",
        ToastSeverity.Urgent => "urgent",
        _ => null,
    };

    /// <summary>Maps an image placement onto its <c>placement</c> attribute value.</summary>
    /// <param name="placement">The image's placement.</param>
    /// <returns>The schema's own spelling for the placement.</returns>
    private static string PlacementName(ToastImagePlacement placement) => placement switch
    {
        ToastImagePlacement.Hero => "hero",
        _ => "appLogoOverride",
    };

    /// <summary>
    /// Escapes a value for use inside an XML text node.
    /// </summary>
    /// <param name="value">The raw value.</param>
    /// <returns>The escaped value, or an empty string for <see langword="null"/>.</returns>
    private static string EscapeText(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);
    }

    /// <summary>
    /// Escapes a value for use inside a double-quoted XML attribute value: text escaping plus the
    /// quote character itself.
    /// </summary>
    /// <param name="value">The raw value.</param>
    /// <returns>The escaped value, or an empty string for <see langword="null"/>.</returns>
    private static string EscapeAttribute(string? value) =>
        EscapeText(value).Replace("\"", "&quot;", StringComparison.Ordinal);
}
