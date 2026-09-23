using System;
using System.Collections.Generic;

namespace Trustsoft.NotifyIcon;

/// <summary>
/// Everything a consumer says about one toast: the text it shows, how emphatically it is shown,
/// the image and buttons it carries, and the identity, grouping and lifetime the shell attaches to
/// it.
/// </summary>
/// <remarks>
/// <para>
/// <b>A dumb mutable data shape.</b> Every member is a settable property with a documented
/// default, there is no behaviour and no validation, and constructing or filling one of these
/// touches nothing outside this process. Validation belongs to the notifier that shows the content
/// (a missing title, for example, is the notifier's error to raise), so a content object can be
/// built, shared, logged and reused without side effects.
/// </para>
/// <para>
/// <b>Two halves of the payload, and the split is the headline fact of this slice.</b> Most of
/// these fields are rendered into the toast XML - the title and body lines, the scenario, the
/// image, the buttons and the sound element. Three are not XML at all:
/// <see cref="Tag"/>, <see cref="Group"/> and <see cref="Expiry"/> are properties of the
/// notification object the shell is handed, because the toast schema has no tag, group or expiry
/// attribute to put them in. A design that only rendered a string would pass every XML test and
/// silently drop three of the nine fields described here, which is why the contract covers them
/// separately.
/// </para>
/// <para>
/// <b>This is the surface later slices attach to.</b> Actions and events hang off this content
/// model, and image sources are resolved into <see cref="ToastImage.Reference"/> by a later slice
/// (D059), so the field list here is the stable part and the behaviour is added around it.
/// </para>
/// </remarks>
public sealed class ToastContent
{
    /// <summary>
    /// Gets or sets the toast's first line of text, its title. Defaults to
    /// <see cref="string.Empty"/>.
    /// </summary>
    /// <remarks>
    /// The shell renders the first text element of a <c>ToastGeneric</c> toast as the title and
    /// takes its typography from there, so this is the line that reads as the subject. A missing
    /// title is not repaired here; the notifier decides whether to refuse the show.
    /// </remarks>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the toast's optional second line of text, its body. Defaults to
    /// <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// A null or empty body omits the second text element entirely rather than emitting an empty
    /// one, because an empty element renders as a blank line rather than as an absent one.
    /// </remarks>
    public string? Body { get; set; }

    /// <summary>
    /// Gets or sets how emphatically the toast is shown. Defaults to
    /// <see cref="ToastSeverity.Default"/>, which writes no scenario attribute at all.
    /// </summary>
    /// <remarks>
    /// See <see cref="ToastSeverity"/> for what the platform does with each value, and for why a
    /// non-default severity is a request the shell may decline rather than a guaranteed visible
    /// difference.
    /// </remarks>
    public ToastSeverity Severity { get; set; } = ToastSeverity.Default;

    /// <summary>
    /// Gets or sets the toast's launch argument: the string Windows reports back when the toast's
    /// body is activated. Defaults to <see langword="null"/>, which means no launch attribute is
    /// written and the shell reports no argument.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the identifier, not a command line.</b> The launch argument is the deterministic
    /// channel an unpackaged process receives on a body click, so it is how a consumer knows which
    /// toast was activated. S01's measurement recorded exactly this and warned that per-click
    /// attribution is otherwise unavailable, which is why the argument is part of the content
    /// model at all.
    /// </para>
    /// <para>
    /// A null or empty value omits the attribute entirely rather than writing an empty one.
    /// </para>
    /// </remarks>
    public string? Launch { get; set; }

    /// <summary>
    /// Gets or sets the image the toast shows, or <see langword="null"/> for a toast with no
    /// image. Defaults to <see langword="null"/>.
    /// </summary>
    public ToastImage? Image { get; set; }

    /// <summary>
    /// Gets the toast's action buttons, in the order they are rendered. Never
    /// <see langword="null"/>; empty by default.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The list is pre-initialized and the property has no setter precisely so that "never null"
    /// is a property of the type rather than a rule a consumer has to remember: adding a button
    /// mutates the list a consumer already has instead of replacing it. Removing or reordering
    /// entries changes the rendered button order and the order of the arguments the shell can
    /// report.
    /// </para>
    /// <para>
    /// An empty collection means the toast carries no <c>actions</c> element, not an empty one.
    /// </para>
    /// </remarks>
    public IList<ToastButton> Buttons { get; } = new List<ToastButton>();

    /// <summary>
    /// Gets or sets the tag that identifies the toast within its group, or
    /// <see langword="null"/> for no tag. Defaults to <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// A tag is one of the three fields that are <em>not</em> toast XML: it is applied to the
    /// notification object the shell is handed, and a tag without a group identifies the toast in
    /// the shell's default group. The shell uses the tag to replace or update a toast it is
    /// already showing with the same tag and group.
    /// </remarks>
    public string? Tag { get; set; }

    /// <summary>
    /// Gets or sets the group the toast belongs to, or <see langword="null"/> for the default
    /// group. Defaults to <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// Like <see cref="Tag"/>, a group is applied to the notification object rather than written
    /// into the XML. The pair (group, tag) is the toast's identity in the shell's history; both
    /// empty means the toast has no identity to replace.
    /// </remarks>
    public string? Group { get; set; }

    /// <summary>
    /// Gets or sets the time after which Windows may remove the toast from the notification
    /// centre, or <see langword="null"/> to let the shell apply its own lifetime. Defaults to
    /// <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// The third non-XML field: expiry is set on the notification object, and the value is
    /// converted to the platform's own instant representation (100-nanosecond ticks since
    /// 1601-01-01) before it is boxed and handed to the shell. A <see cref="DateTimeOffset"/> is
    /// used rather than a <see cref="DateTime"/> so the instant is unambiguous regardless of the
    /// machine's time zone.
    /// </remarks>
    public DateTimeOffset? Expiry { get; set; }

    /// <summary>
    /// Gets or sets whether the toast plays its notification sound. Defaults to
    /// <see cref="ToastSound.Default"/>, which writes no audio element and leaves the sound to the
    /// shell.
    /// </summary>
    public ToastSound Sound { get; set; } = ToastSound.Default;
}
