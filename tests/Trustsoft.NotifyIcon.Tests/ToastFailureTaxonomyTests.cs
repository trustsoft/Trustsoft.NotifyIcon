using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Trustsoft.NotifyIcon.Interop;
using Xunit;

namespace Trustsoft.NotifyIcon.Tests;

/// <summary>
/// Contract tests for the internal delivery-failure taxonomy M002/S04/T04 adds: the
/// <c>WPN_E_*</c> codes it carries, the stable name each one maps to, and the one Verbose trace line
/// that names a failing code beside S03's unchanged Error-level line.
/// </summary>
/// <remarks>
/// <para>
/// The values are asserted against the literals from the local SDK header
/// (<c>winerror.h</c>, the notification-platform block, lines 56680-57020) rather than against the
/// constants themselves, so a typo in the table is a failure rather than a tautology.
/// </para>
/// <para>
/// The trace test drives the shell's asynchronous failure through
/// <see cref="FakeToastApi.RaiseFailed"/>, which is the measured way a notifications-disabled
/// delivery failure actually arrives. The class joins <c>TraceChannelCollection</c> because it raises
/// the process-wide source level to read the Verbose line.
/// </para>
/// </remarks>
[Collection(TraceChannelCollection.Name)]
public sealed class ToastFailureTaxonomyTests
{
    /// <summary>The override id these tests register with, chosen so it cannot be the derived default.</summary>
    private const string OverrideId = "Vendor.Taxonomy.App";

    /// <summary>
    /// Every code carries the header's own value: the table is a quotation of
    /// <c>winerror.h</c>, and a transcription slip would silently misname a real failure.
    /// </summary>
    [Fact]
    public void The_constants_carry_the_winerror_h_values()
    {
        Assert.Equal(unchecked((int)0x803E0102), ToastFailureTaxonomy.InvalidApp);
        Assert.Equal(unchecked((int)0x803E0105), ToastFailureTaxonomy.PlatformUnavailable);
        Assert.Equal(unchecked((int)0x803E0111), ToastFailureTaxonomy.NotificationsDisabled);
        Assert.Equal(unchecked((int)0x803E0112), ToastFailureTaxonomy.DeviceIncapable);
        Assert.Equal(unchecked((int)0x803E0114), ToastFailureTaxonomy.TypeDisabled);
        Assert.Equal(unchecked((int)0x803E0115), ToastFailureTaxonomy.PayloadTooLarge);
        Assert.Equal(unchecked((int)0x803E0116), ToastFailureTaxonomy.TagTooLong);
        Assert.Equal(unchecked((int)0x803E0201), ToastFailureTaxonomy.PowerSave);
        Assert.Equal(unchecked((int)0x803E0202), ToastFailureTaxonomy.ImageMissing);
        Assert.Equal(unchecked((int)0x803E0207), ToastFailureTaxonomy.Dropped);
        Assert.Equal(unchecked((int)0x803E0209), ToastFailureTaxonomy.GroupTooLong);
        Assert.Equal(unchecked((int)0x803E020A), ToastFailureTaxonomy.GroupNotAlphanumeric);
    }

    /// <summary>
    /// Each listed code maps to exactly its stable name - the whole table, so a name cannot drift
    /// away from the constant it belongs to.
    /// </summary>
    /// <param name="code">The <c>HRESULT</c> the platform reported.</param>
    /// <param name="expected">The name the taxonomy must report.</param>
    [Theory]
    [InlineData(unchecked((int)0x803E0102), "InvalidApp")]
    [InlineData(unchecked((int)0x803E0105), "PlatformUnavailable")]
    [InlineData(unchecked((int)0x803E0111), "NotificationsDisabled")]
    [InlineData(unchecked((int)0x803E0112), "DeviceIncapable")]
    [InlineData(unchecked((int)0x803E0114), "TypeDisabled")]
    [InlineData(unchecked((int)0x803E0115), "PayloadTooLarge")]
    [InlineData(unchecked((int)0x803E0116), "TagTooLong")]
    [InlineData(unchecked((int)0x803E0201), "PowerSave")]
    [InlineData(unchecked((int)0x803E0202), "ImageMissing")]
    [InlineData(unchecked((int)0x803E0207), "Dropped")]
    [InlineData(unchecked((int)0x803E0209), "GroupTooLong")]
    [InlineData(unchecked((int)0x803E020A), "GroupNotAlphanumeric")]
    public void Every_listed_code_maps_to_its_stable_name(int code, string expected) =>
        Assert.Equal(expected, ToastFailureTaxonomy.NameOf(code));

    /// <summary>
    /// Everything the table does not carry is reported as <see cref="ToastFailureTaxonomy.Unknown"/>
    /// rather than guessed at: <c>0</c>, the code this library's own show path produces for a
    /// value-level failure, the benign first-use <c>E_NOT_FOUND</c>, and the <c>WPN_E_*</c> codes a
    /// toast show cannot plausibly produce.
    /// </summary>
    /// <param name="code">The unmapped code under test.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(unchecked((int)0x80004005))]
    [InlineData(unchecked((int)0x80070057))]
    [InlineData(unchecked((int)0x8000FFFF))]
    [InlineData(unchecked((int)0x80070490))]
    [InlineData(unchecked((int)0x803E0100))]
    [InlineData(unchecked((int)0x803E0117))]
    [InlineData(unchecked((int)0x803E020B))]
    [InlineData(-1)]
    public void An_unlisted_code_maps_to_Unknown(int code) =>
        Assert.Equal(ToastFailureTaxonomy.Unknown, ToastFailureTaxonomy.NameOf(code));

    /// <summary>
    /// The Unknown name itself is the pinned fallback string, so a reader of the trace line reads the
    /// same word every time.
    /// </summary>
    [Fact]
    public void The_unknown_name_is_pinned() => Assert.Equal("Unknown", ToastFailureTaxonomy.Unknown);

    /// <summary>
    /// One failing code produces one Verbose taxonomy line naming it, and the Error-level line keeps
    /// exactly the shape S03 pinned: same severity, same event id, same wording, with the name only on
    /// the Verbose line.
    /// </summary>
    /// <remarks>
    /// The failure is delivered the way the platform really delivers it - through the notification's
    /// <c>Failed</c> callback after <c>Show</c> returned <c>S_OK</c> - and the code is the measured
    /// notifications-disabled one, so the line under test is the one a real disabled-delivery run
    /// produces.
    /// </remarks>
    [Fact]
    public void A_failure_names_its_code_verbosely_beside_the_unchanged_error_line()
    {
        var fake = new FakeToastApi { AppUserModelIdToReadBack = OverrideId };
        var notifier = new ToastNotifier(fake) { AppUserModelId = OverrideId };

        notifier.Show(Content());

        int code = ToastFailureTaxonomy.NotificationsDisabled;

        var recorder = new TraceRecorder();
        TraceSource source = NotifyIconTrace.Source;
        SourceLevels previousLevel = source.Switch.Level;

        try
        {
            source.Listeners.Add(recorder);
            source.Switch.Level = SourceLevels.Verbose;

            // The shell's asynchronous delivery failure for an already-accepted toast.
            Assert.True(fake.RaiseFailed(code));
            source.Flush();
        }
        finally
        {
            source.Switch.Level = previousLevel;
            source.Listeners.Remove(recorder);
        }

        // The taxonomy reading, at Verbose - the level a diagnosis deliberately raises.
        Assert.Contains(
            recorder.Events,
            entry => entry.EventType == TraceEventType.Verbose
                && entry.EventId == NotifyIconTrace.VerboseEventId
                && entry.Message is not null
                && entry.Message.Contains(
                    $"toast failure taxonomy: code=0x{code:X8} name='NotificationsDisabled'",
                    StringComparison.Ordinal));

        // The Error line is unmoved: one line, the pinned event id, the pinned wording. The name is
        // deliberately NOT appended to it.
        (TraceEventType eventType, int eventId, string? message) = Assert.Single(
            recorder.Events.Where(entry => entry.EventType == TraceEventType.Error));

        Assert.Equal(NotifyIconTrace.ToastErrorEventId, eventId);
        Assert.NotNull(message);
        Assert.Contains(
            $"Toast {ToastException.OperationNotificationFailed} failed (code {code}, 0x{code:X8}).",
            message,
            StringComparison.Ordinal);
        Assert.DoesNotContain("taxonomy", message, StringComparison.Ordinal);

        notifier.Dispose();
    }

    /// <summary>
    /// A representative content: the minimal shape the other notifier tests use.
    /// </summary>
    /// <returns>The content.</returns>
    private static ToastContent Content() => new()
    {
        Title = "Build finished",
        Body = "3 projects built in 12.4s",
        Severity = ToastSeverity.Reminder,
        Launch = "sample-toast-1",
    };

    /// <summary>
    /// Captures the structured trace events the shared source emits, so severity, event id and
    /// message can be asserted instead of inferred from formatted text - the same shape
    /// <c>ToastEventTests</c> uses, kept local because that one is private to its class.
    /// </summary>
    private sealed class TraceRecorder : TraceListener
    {
        /// <summary>Gets the sequence of trace events this listener has received.</summary>
        internal List<(TraceEventType EventType, int EventId, string? Message)> Events { get; } = [];

        /// <inheritdoc />
        public override void Write(string? message)
        {
        }

        /// <inheritdoc />
        public override void WriteLine(string? message)
        {
        }

        /// <inheritdoc />
        public override void TraceEvent(
            TraceEventCache? eventCache,
            string source,
            TraceEventType eventType,
            int id,
            string? message)
        {
            Events.Add((eventType, id, message));
        }
    }
}
