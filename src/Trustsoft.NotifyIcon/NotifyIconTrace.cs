using System.Diagnostics;
using System.Globalization;

namespace Trustsoft.NotifyIcon;

/// <summary>
/// The library's trace channel: one named <see cref="TraceSource"/> plus the two writers that use
/// it - the error writer of the runtime failure policy and the Verbose click-stream writer.
/// </summary>
/// <remarks>
/// <para>
/// D008 requires a windowless app to be able to find out why its icon is missing, and such an app
/// has no window and often no other logging channel of its own. <see cref="TraceSource"/> is the
/// framework-provided channel that costs no dependency, which is what keeps the zero-dependency
/// promise intact (R011 / D001). <c>Microsoft.Extensions.Logging</c> would add a package reference
/// to the shipping library and is therefore not an option.
/// </para>
/// <para>
/// The source is named "Trustsoft.NotifyIcon" and defaults to <see cref="SourceLevels.Warning"/>,
/// so the error line the failure policy writes is emitted without any configuration and a
/// consumer can subscribe the usual way - by adding a listener to that name (app.config, or
/// <c>new TraceSource(NotifyIconTrace.SourceName)</c> from their side) - and can raise the level
/// to see more when diagnosing.
/// </para>
/// <para>
/// The two writers sit at different levels on purpose and must never swap places (MEM026):
/// <see cref="Error"/> is reserved for failures and is the only writer at
/// <see cref="TraceEventType.Error"/>, while <see cref="Verbose"/> carries the click stream - the
/// raw callback payloads this library understands and the ones it does not - and is filtered out by
/// the default level entirely. A normal run therefore emits nothing on this channel, and an
/// unmapped event code is never reported as a failure: it is normal traffic, not a defect.
/// </para>
/// <para>
/// Nothing here is public: the library exposes no logging API of its own, so the shipped surface
/// stays the tray contract (D002 / D010) and the trace channel is an implementation detail the
/// consumer observes through the framework, not through us.
/// </para>
/// </remarks>
internal static class NotifyIconTrace
{
    /// <summary>
    /// The name a consumer subscribes to in order to receive the library's trace output.
    /// </summary>
    internal const string SourceName = "Trustsoft.NotifyIcon";

    /// <summary>
    /// The trace event id of the surfaced-failure line. Stable so that a listener can filter on it
    /// rather than on the message text.
    /// </summary>
    internal const int ErrorEventId = 1;

    /// <summary>
    /// The trace event id of the Verbose click-stream lines. Stable for the same reason as
    /// <see cref="ErrorEventId"/>, and distinct from it so a listener can tell traffic from failure.
    /// </summary>
    internal const int VerboseEventId = 2;

    /// <summary>
    /// The single source behind <see cref="Source"/>. Created once so that every failure line of
    /// the process lands in the same source and listener list.
    /// </summary>
    private static readonly TraceSource TraceSourceInstance = new(SourceName, SourceLevels.Warning);

    /// <summary>
    /// Gets the library's trace source, named <see cref="SourceName"/>.
    /// </summary>
    /// <value>The process-wide source; internal so only tests and the library can attach listeners.</value>
    internal static TraceSource Source => TraceSourceInstance;

    /// <summary>
    /// Writes one error line for a notification-area failure that has been surfaced to the caller
    /// (a thrown <see cref="TrayIconException"/> or a raised <c>TrayError</c> event).
    /// </summary>
    /// <param name="operation">The failing operation; usually one of the <c>Operation*</c> constants.</param>
    /// <param name="win32ErrorCode">The Win32 error code, or <c>0</c> when no Win32 call failed.</param>
    /// <param name="exception">The exception the failure produced, if any.</param>
    /// <param name="retried">Whether the failure happened after the single retry.</param>
    /// <remarks>
    /// <para>
    /// The line has a stable, grep-able shape so that a support log can be searched for it:
    /// <c>TrayIcon {operation} failed (Win32 error {code}); retried={retried}.</c> followed by the
    /// exception type and message. Everything is collapsed onto one line on purpose: a multi-line
    /// entry breaks line-oriented log parsing, and the full <see cref="Exception"/> object is
    /// available on <see cref="TrayErrorEventArgs.Exception"/> for a consumer that needs the stack.
    /// </para>
    /// <para>
    /// This method never throws. It is the last step of a failure path, so a listener that fails
    /// (a file listener on a full disk, a closed writer) must not promote "the icon could not be
    /// updated" into an unhandled exception that terminates a windowless host. The caller's own
    /// reporting - the exception or the routed event - is unaffected either way.
    /// </para>
    /// </remarks>
    internal static void Error(string operation, int win32ErrorCode, Exception exception, bool retried)
    {
        string line = string.Format(
            CultureInfo.InvariantCulture,
            "TrayIcon {0} failed (Win32 error {1}); retried={2}. {3}",
            SingleLine(operation, fallback: "Unknown"),
            win32ErrorCode,
            retried,
            DescribeException(exception));

        try
        {
            TraceSourceInstance.TraceEvent(TraceEventType.Error, ErrorEventId, line);
        }
        catch (Exception)
        {
            // Deliberately swallowed: reporting a failure must not create a new one. See the
            // remarks above - the failure itself still reaches the caller through the exception
            // or the routed event, which are the channels that carry the full detail.
        }
    }

    /// <summary>
    /// Writes one Verbose line of diagnostic detail about the notification-area traffic the host
    /// window received.
    /// </summary>
    /// <param name="message">The detail to write; collapsed onto a single line before it is traced.</param>
    /// <remarks>
    /// <para>
    /// This is the instrument for a question the documentation does not answer: which event codes
    /// the shell actually sends for a given interaction. It is written at
    /// <see cref="TraceEventType.Verbose"/> so a consumer sees the stream only after deliberately
    /// raising the level, which keeps it out of every normal log and keeps this channel's default
    /// output failure-only (MEM026).
    /// </para>
    /// <para>
    /// Like <see cref="Error"/> it never throws: a broken listener must not be able to turn ordinary
    /// traffic into an exception on the window-procedure path that produced the line.
    /// </para>
    /// </remarks>
    internal static void Verbose(string? message)
    {
        string line = SingleLine(message, fallback: "(no detail)");

        try
        {
            TraceSourceInstance.TraceEvent(TraceEventType.Verbose, VerboseEventId, line);
        }
        catch (Exception)
        {
            // Deliberately swallowed, exactly as in Error: writing a diagnostic line must not
            // create a failure. See the remarks above.
        }
    }

    /// <summary>
    /// Renders an exception as a single line: the full type name and the message.
    /// </summary>
    /// <param name="exception">The exception to describe, if any.</param>
    /// <returns>A one-line description, or a placeholder when no exception was supplied.</returns>
    private static string DescribeException(Exception exception)
    {
        if (exception is null)
        {
            return "No exception detail was supplied.";
        }

        Type type = exception.GetType();

        return string.Concat(
            SingleLine(type.FullName, fallback: type.Name),
            ": ",
            SingleLine(exception.Message, fallback: "(no message)"));
    }

    /// <summary>
    /// Collapses a value onto a single non-empty line, because one failure must produce exactly one
    /// log line - WPF exception messages routinely contain line breaks.
    /// </summary>
    /// <param name="value">The value to render.</param>
    /// <param name="fallback">The text to use when the value is null or empty.</param>
    /// <returns>A single-line, non-empty rendering of <paramref name="value"/>.</returns>
    private static string SingleLine(string? value, string fallback)
    {
        if (string.IsNullOrEmpty(value))
        {
            return fallback;
        }

        return value.Replace('\r', ' ').Replace('\n', ' ');
    }
}
