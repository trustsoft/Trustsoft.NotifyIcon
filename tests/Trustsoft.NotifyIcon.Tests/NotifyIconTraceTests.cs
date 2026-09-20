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
/// </remarks>
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
