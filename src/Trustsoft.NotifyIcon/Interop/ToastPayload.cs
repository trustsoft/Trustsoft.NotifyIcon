using System.Text;

namespace Trustsoft.NotifyIcon.Interop;

/// <summary>
/// The minimal toast payload: the XML the show path loads into a
/// <c>Windows.Data.Xml.Dom.XmlDocument</c> and hands to the shell, generated from a title, an
/// optional body and an optional launch argument.
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
/// <b>One element per line of text.</b> The shell renders each <c>&lt;text&gt;</c> child of a
/// <c>ToastGeneric</c> binding as one line: the first is the title, the second the body. A payload
/// with no body therefore emits one <c>&lt;text&gt;</c> child, never an empty second one - an empty
/// element would render as a blank line rather than as an absent one.
/// </para>
/// <para>
/// <b>A string is XML-escaped, never trusted.</b> The title, the body and the launch argument all
/// come from the caller, and a raw <c>&amp;</c> or <c>&lt;</c> in any of them would make the
/// document malformed, which <c>IXmlDocumentIO.LoadXml</c> reports as a parse failure far away
/// from the value that caused it. Text and attribute values are escaped here so a payload can
/// only ever be well-formed XML.
/// </para>
/// <para>
/// <b>Not the content model.</b> S02 owns the public content model (severity, images, actions,
/// tag, group, expiry, sound) and its exact-XML contract. This type is the minimal payload T04's
/// show path needs, kept deliberately small so the measured call sequence is what the contract
/// tests assert against.
/// </para>
/// </remarks>
internal sealed class ToastPayload
{
    /// <summary>
    /// The binding template the measurement used: the modern template whose <c>&lt;text&gt;</c>
    /// children render as the toast's title and body lines.
    /// </summary>
    internal const string ToastGenericTemplate = "ToastGeneric";

    /// <summary>
    /// Initializes a payload.
    /// </summary>
    /// <param name="title">The toast's first text line; a <see langword="null"/> value is treated as empty.</param>
    /// <param name="body">The toast's optional second text line; omitted from the XML when null or empty.</param>
    /// <param name="launch">
    /// The optional <c>launch</c> argument: the string the shell reports back on a body click. Omitted
    /// from the XML when null or empty, which is the case where Windows reports no arguments at all.
    /// </param>
    internal ToastPayload(string title, string? body = null, string? launch = null)
    {
        Title = title ?? string.Empty;
        Body = body;
        Launch = launch;
    }

    /// <summary>Gets the first text line of the toast.</summary>
    internal string Title { get; }

    /// <summary>Gets the second text line, or <see langword="null"/> when the toast has no body.</summary>
    internal string? Body { get; }

    /// <summary>Gets the launch argument the shell reports on activation, or <see langword="null"/>.</summary>
    internal string? Launch { get; }

    /// <summary>
    /// Renders the payload as the exact XML handed to <c>IXmlDocumentIO.LoadXml</c>.
    /// </summary>
    /// <returns>The document, with every caller-supplied value escaped.</returns>
    /// <remarks>
    /// The shape is the measured one:
    /// <c>&lt;toast launch="..."&gt;&lt;visual&gt;&lt;binding template="ToastGeneric"&gt;&lt;text&gt;title&lt;/text&gt;[&lt;text&gt;body&lt;/text&gt;]&lt;/binding&gt;&lt;/visual&gt;&lt;/toast&gt;</c>.
    /// The <c>launch</c> attribute is emitted only when a launch argument was supplied.
    /// </remarks>
    internal string ToXml()
    {
        var xml = new StringBuilder(256);

        xml.Append("<toast");

        if (!string.IsNullOrEmpty(Launch))
        {
            xml.Append(" launch=\"");
            xml.Append(EscapeAttribute(Launch));
            xml.Append('"');
        }

        xml.Append("><visual><binding template=\"");
        xml.Append(ToastGenericTemplate);
        xml.Append("\"><text>");
        xml.Append(EscapeText(Title));
        xml.Append("</text>");

        if (!string.IsNullOrEmpty(Body))
        {
            xml.Append("<text>");
            xml.Append(EscapeText(Body));
            xml.Append("</text>");
        }

        xml.Append("</binding></visual></toast>");

        return xml.ToString();
    }

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
    /// <returns>The escaped value.</returns>
    private static string EscapeAttribute(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return EscapeText(value).Replace("\"", "&quot;", StringComparison.Ordinal);
    }
}
