using System.Diagnostics;
using System.Globalization;
using System.IO;
using Xunit;

namespace Trustsoft.NotifyIcon.Tests;

/// <summary>
/// Pins the trace channel that R013 / D008 gives a windowless app as its second failure report:
/// a named <see cref="TraceSource"/>, one grep-able line per surfaced failure, and the guarantee
/// that the channel itself can never escalate the failure it is reporting.
/// </summary>
/// <remarks>
/// <para>
/// The library deliberately exposes no public logging API (D002 / D010), so the only way a test
/// can observe the channel is the way a consumer does: by attaching a listener to the source.
/// </para>
/// <para>
/// Every test attaches and detaches its listener in a <c>try</c>/<c>finally</c>. The source is
/// process-wide, so a leaked listener would swallow output from other tests and pollute the test
/// run's own output.
/// </para>
/// <para>
/// The class joins <c>TraceChannelCollection</c> for the same process-wide reason: a test that
/// asserts an exact line count or the default level cannot overlap another class' writer on the
/// same source.
/// </para>
/// </remarks>
[Collection(TraceChannelCollection.Name)]
public sealed class NotifyIconTraceTests
{
    /// <summary>
    /// The failure line must contain the operation, the code and the retry flag in a stable shape,
    /// plus enough exception detail to know what happened without a debugger.
    /// </summary>
    [Fact]
    public void Error_writes_the_pinned_line_with_operation_code_retry_flag_and_exception()
    {
        var writer = new StringWriter(CultureInfo.InvariantCulture);
        var listener = new TextWriterTraceListener(writer);
        TraceSource source = NotifyIconTrace.Source;

        string captured;

        try
        {
            source.Listeners.Add(listener);

            NotifyIconTrace.Error("Add", 87, new InvalidOperationException("boom"), retried: false);
            source.Flush();

            captured = writer.ToString();
        }
        finally
        {
            source.Listeners.Remove(listener);
            listener.Dispose();
        }

        // The exact prefix is the contract: a support log is searched by this shape.
        Assert.Contains(
            "TrayIcon Add failed (Win32 error 87); retried=False.",
            captured,
            StringComparison.Ordinal);
        Assert.Contains("InvalidOperationException", captured, StringComparison.Ordinal);
        Assert.Contains("boom", captured, StringComparison.Ordinal);
    }

    /// <summary>
    /// A retried failure must be distinguishable from a first-attempt one by the line alone.
    /// </summary>
    [Fact]
    public void Error_marks_a_retried_failure()
    {
        string captured = CaptureLine("Modify", 5, new InvalidOperationException("boom"), retried: true);

        Assert.Contains("retried=True", captured, StringComparison.Ordinal);
        Assert.Contains("TrayIcon Modify failed (Win32 error 5); retried=True.", captured, StringComparison.Ordinal);
    }

    /// <summary>
    /// A multi-line exception message must not break the one-line-per-failure rule: a log
    /// processor reading line by line would otherwise attribute the stack trace to a new entry.
    /// </summary>
    [Fact]
    public void Error_collapses_a_multi_line_exception_onto_one_line()
    {
        string captured = CaptureLine(
            "Modify",
            87,
            new InvalidOperationException("first line\r\nsecond line\nthird line"),
            retried: true);

        string[] lines = captured
            .Split('\n')
            .Select(line => line.Trim('\r', ' '))
            .Where(line => line.Length > 0)
            .ToArray();

        // One failure must produce exactly one non-empty line: the listener adds a trailing
        // newline, and the collapsed message must not add any of its own.
        string single = Assert.Single(lines);

        Assert.Contains("TrayIcon Modify failed (Win32 error 87); retried=True.", single, StringComparison.Ordinal);

        // Each fragment of the original message survives; the replacement is one space per line
        // break, so the whitespace is normalised before comparing the reassembled text.
        string normalised = string.Join(' ', single.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        Assert.Contains("first line second line third line", normalised, StringComparison.Ordinal);
    }

    /// <summary>
    /// The line is written as an <see cref="TraceEventType.Error"/> with the pinned event id, so a
    /// listener can filter on severity and id instead of on message text.
    /// </summary>
    [Fact]
    public void Error_traces_at_error_severity_with_the_pinned_event_id()
    {
        var recorder = new RecordingTraceListener();
        TraceSource source = NotifyIconTrace.Source;

        try
        {
            source.Listeners.Add(recorder);

            NotifyIconTrace.Error("Remove", 5, new InvalidOperationException("boom"), retried: true);
        }
        finally
        {
            source.Listeners.Remove(recorder);
        }

        (TraceEventType eventType, int eventId, string? message) = Assert.Single(recorder.Events);

        Assert.Equal(TraceEventType.Error, eventType);
        Assert.Equal(NotifyIconTrace.ErrorEventId, eventId);
        Assert.NotNull(message);
        Assert.Contains("Remove", message, StringComparison.Ordinal);
        Assert.Contains("5", message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The source name is what a consumer writes into their configuration to subscribe, so it is
    /// part of the documented contract rather than an implementation detail.
    /// </summary>
    [Fact]
    public void Source_is_named_and_defaults_to_warning_level()
    {
        Assert.Equal("Trustsoft.NotifyIcon", NotifyIconTrace.SourceName);
        Assert.Equal("Trustsoft.NotifyIcon", NotifyIconTrace.Source.Name);

        // Warning is the level that emits the error line with no configuration at all.
        Assert.Equal(SourceLevels.Warning, NotifyIconTrace.Source.Switch.Level);
    }

    /// <summary>
    /// The channel is the last step of a failure path, so malformed input must degrade to a
    /// readable line rather than to a new exception.
    /// </summary>
    [Fact]
    public void Error_degrades_a_missing_operation_to_a_readable_placeholder()
    {
        string captured = CaptureLine(string.Empty, 87, new InvalidOperationException("boom"), retried: false);

        Assert.Contains(
            "TrayIcon Unknown failed (Win32 error 87); retried=False.",
            captured,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A listener that throws (a closed writer, a file listener on a full disk) must not turn
    /// "the icon could not be updated" into an unhandled exception in a windowless host.
    /// </summary>
    [Fact]
    public void Error_never_escalates_a_listener_failure()
    {
        var throwing = new ThrowingTraceListener();
        TraceSource source = NotifyIconTrace.Source;

        try
        {
            source.Listeners.Add(throwing);

            // No Assert.Throws: the assertion is that control returns here at all.
            NotifyIconTrace.Error("Add", 87, new InvalidOperationException("boom"), retried: false);
        }
        finally
        {
            source.Listeners.Remove(throwing);
        }

        Assert.True(throwing.WasCalled, "The throwing listener must actually have been invoked.");
    }

    /// <summary>
    /// The toast failure line must carry the operation, the code in decimal and hexadecimal, and the
    /// exception detail, in the stable shape a support log is searched for.
    /// </summary>
    [Fact]
    public void ToastError_writes_the_pinned_line_with_the_operation_the_code_and_the_exception()
    {
        string captured = CaptureToastLine("Add", 5, new InvalidOperationException("boom"));

        Assert.Contains("Toast Add failed (code 5, 0x00000005).", captured, StringComparison.Ordinal);
        Assert.Contains("InvalidOperationException", captured, StringComparison.Ordinal);
        Assert.Contains("boom", captured, StringComparison.Ordinal);
    }

    /// <summary>
    /// A toast failure is almost always an <c>HRESULT</c>, and an <c>HRESULT</c> is read in
    /// hexadecimal - so the line carries both forms, and a negative code keeps its bits rather than
    /// gaining a misleading sign in the hex form.
    /// </summary>
    [Fact]
    public void ToastError_renders_a_negative_hresult_in_decimal_and_hexadecimal()
    {
        int hresult = unchecked((int)0x80004005);

        string captured = CaptureToastLine("Show", hresult, new InvalidOperationException("boom"));

        Assert.Contains($"Toast Show failed (code {hresult}, 0x80004005).", captured, StringComparison.Ordinal);
    }

    /// <summary>
    /// A multi-line exception message must not break the one-line-per-failure rule, exactly as for
    /// the tray writer: a log processor reading line by line would otherwise attribute the message
    /// fragments to new entries.
    /// </summary>
    [Fact]
    public void ToastError_collapses_a_multi_line_exception_onto_one_line()
    {
        string captured = CaptureToastLine(
            "LoadXml",
            5,
            new InvalidOperationException("first line\r\nsecond line\nthird line"));

        string[] lines = captured
            .Split('\n')
            .Select(line => line.Trim('\r', ' '))
            .Where(line => line.Length > 0)
            .ToArray();

        string single = Assert.Single(lines);

        Assert.Contains("Toast LoadXml failed (code 5, 0x00000005).", single, StringComparison.Ordinal);

        // Each fragment of the original message survives; the replacement is one space per line
        // break, so the whitespace is normalised before comparing the reassembled text.
        string normalised = string.Join(' ', single.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        Assert.Contains("first line second line third line", normalised, StringComparison.Ordinal);
    }

    /// <summary>
    /// The channel is the last step of a failure path, so malformed input must degrade to a
    /// readable line rather than to a new exception - and the placeholder is the same one the tray
    /// writer uses, because a missing operation is the same defect either way.
    /// </summary>
    [Fact]
    public void ToastError_degrades_a_missing_operation_to_the_same_placeholder_as_Error()
    {
        string captured = CaptureToastLine(string.Empty, 5, new InvalidOperationException("boom"));

        Assert.Contains("Toast Unknown failed (code 5, 0x00000005).", captured, StringComparison.Ordinal);
    }

    /// <summary>
    /// The shell's asynchronous failure and the show path's own failure both report a bare code, so a
    /// null exception must render as a readable placeholder rather than as a blank tail or a crash.
    /// </summary>
    [Fact]
    public void ToastError_renders_a_null_exception_with_the_placeholder()
    {
        string captured = CaptureToastLine("NotificationFailed", 5, exception: null);

        Assert.Contains("Toast NotificationFailed failed (code 5, 0x00000005).", captured, StringComparison.Ordinal);
        Assert.Contains("No exception detail was supplied.", captured, StringComparison.Ordinal);
    }

    /// <summary>
    /// The line is written as an <see cref="TraceEventType.Error"/> with its own pinned event id, so
    /// a listener can filter a toast failure apart from a tray failure and from Verbose traffic.
    /// </summary>
    [Fact]
    public void ToastError_traces_at_error_severity_with_its_own_pinned_event_id()
    {
        var recorder = new RecordingTraceListener();
        TraceSource source = NotifyIconTrace.Source;

        try
        {
            source.Listeners.Add(recorder);

            NotifyIconTrace.ToastError("Add", 5, new InvalidOperationException("boom"));
        }
        finally
        {
            source.Listeners.Remove(recorder);
        }

        (TraceEventType eventType, int eventId, string? message) = Assert.Single(recorder.Events);

        Assert.Equal(TraceEventType.Error, eventType);
        Assert.Equal(NotifyIconTrace.ToastErrorEventId, eventId);

        // The event ids are distinct on purpose: a listener filtering on the id can tell a tray
        // failure, a toast failure and a Verbose step apart without parsing the message.
        Assert.NotEqual(NotifyIconTrace.ErrorEventId, NotifyIconTrace.ToastErrorEventId);
        Assert.NotEqual(NotifyIconTrace.VerboseEventId, NotifyIconTrace.ToastErrorEventId);

        Assert.NotNull(message);
        Assert.Contains("Add", message, StringComparison.Ordinal);
        Assert.Contains("5", message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A listener that throws (a closed writer, a file listener on a full disk) must not turn "the
    /// toast could not be delivered" into an unhandled exception in a windowless host - the same
    /// guarantee the tray writer gives.
    /// </summary>
    [Fact]
    public void ToastError_never_escalates_a_listener_failure()
    {
        var throwing = new ThrowingTraceListener();
        TraceSource source = NotifyIconTrace.Source;

        try
        {
            source.Listeners.Add(throwing);

            // No Assert.Throws: the assertion is that control returns here at all.
            NotifyIconTrace.ToastError("Add", 5, new InvalidOperationException("boom"));
        }
        finally
        {
            source.Listeners.Remove(throwing);
        }

        Assert.True(throwing.WasCalled, "The throwing listener must actually have been invoked.");
    }

    /// <summary>
    /// Runs the error writer with a text listener attached and returns what was captured.
    /// </summary>
    /// <param name="operation">The operation to report.</param>
    /// <param name="win32ErrorCode">The Win32 error code to report.</param>
    /// <param name="exception">The exception to report.</param>
    /// <param name="retried">Whether the failure happened after the retry.</param>
    /// <returns>The captured trace text.</returns>
    private static string CaptureLine(string operation, int win32ErrorCode, Exception exception, bool retried)
    {
        var writer = new StringWriter(CultureInfo.InvariantCulture);
        var listener = new TextWriterTraceListener(writer);
        TraceSource source = NotifyIconTrace.Source;

        try
        {
            source.Listeners.Add(listener);

            NotifyIconTrace.Error(operation, win32ErrorCode, exception, retried);
            source.Flush();

            return writer.ToString();
        }
        finally
        {
            source.Listeners.Remove(listener);
            listener.Dispose();
        }
    }

    /// <summary>
    /// Runs the toast failure writer with a text listener attached and returns what was captured.
    /// </summary>
    /// <param name="operation">The operation to report.</param>
    /// <param name="errorCode">The <c>HRESULT</c> or Win32 code to report.</param>
    /// <param name="exception">The exception to report, or <see langword="null"/> when there is none.</param>
    /// <returns>The captured trace text.</returns>
    private static string CaptureToastLine(string operation, int errorCode, Exception? exception)
    {
        var writer = new StringWriter(CultureInfo.InvariantCulture);
        var listener = new TextWriterTraceListener(writer);
        TraceSource source = NotifyIconTrace.Source;

        try
        {
            source.Listeners.Add(listener);

            NotifyIconTrace.ToastError(operation, errorCode, exception);
            source.Flush();

            return writer.ToString();
        }
        finally
        {
            source.Listeners.Remove(listener);
            listener.Dispose();
        }
    }

    /// <summary>
    /// Captures the structured trace event, so severity and event id can be asserted rather than
    /// inferred from formatted text.
    /// </summary>
    private sealed class RecordingTraceListener : TraceListener
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

    /// <summary>
    /// A listener that fails the way a broken sink does, used to prove the channel cannot escalate.
    /// </summary>
    private sealed class ThrowingTraceListener : TraceListener
    {
        /// <summary>Gets a value indicating whether the listener was invoked.</summary>
        internal bool WasCalled { get; private set; }

        /// <inheritdoc />
        public override void Write(string? message) => WasCalled = true;

        /// <inheritdoc />
        public override void WriteLine(string? message) => WasCalled = true;

        /// <inheritdoc />
        public override void TraceEvent(
            TraceEventCache? eventCache,
            string source,
            TraceEventType eventType,
            int id,
            string? message)
        {
            WasCalled = true;

            throw new IOException("the trace sink is unavailable");
        }
    }
}
