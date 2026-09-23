using System.Reflection;
using Xunit;

namespace Trustsoft.NotifyIcon.Tests;

/// <summary>
/// Pins the public failure contract of R013: the exception type a consumer catches by name, the
/// machine-readable fields it branches on, and the operation constants that must not drift.
/// </summary>
/// <remarks>
/// <para>
/// These tests need no shell, no window and no STA thread: the whole error policy is a data shape,
/// which is exactly why the shape was settled before the lifecycle code that throws it exists.
/// </para>
/// <para>
/// The constants are asserted by reflection rather than by reading
/// <c>TrayIconException.OperationAdd</c> directly, so that a rename of a constant (a source
/// change that a compiled test would happily follow) fails here instead of silently breaking a
/// consumer that branches on the string value.
/// </para>
/// </remarks>
public sealed class TrayIconExceptionTests
{
    /// <summary>
    /// The two machine-readable fields are what a consumer acts on, so they must survive
    /// construction unchanged.
    /// </summary>
    [Fact]
    public void Constructor_round_trips_operation_and_win32_error_code()
    {
        var exception = new TrayIconException(TrayIconException.OperationAdd, 87);

        Assert.Equal("Add", exception.Operation);
        Assert.Equal(87, exception.Win32ErrorCode);
    }

    /// <summary>
    /// The human-readable message must name the operation and the numeric code, because that is
    /// all a log reader has when the exception is printed.
    /// </summary>
    [Fact]
    public void Message_names_the_operation_and_the_numeric_error_code()
    {
        var exception = new TrayIconException(TrayIconException.OperationSetVersion, 5);

        Assert.Contains("SetVersion", exception.Message, StringComparison.Ordinal);
        Assert.Contains("5", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Win32 error 5", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The detail overload exists for failures whose cause the operation and the code cannot
    /// express (an unsupported <c>ImageSource</c>, for example): the detail is appended, and the
    /// machine-readable fields stay exactly what they were.
    /// </summary>
    [Fact]
    public void Detail_overload_appends_the_detail_and_keeps_the_fields_machine_readable()
    {
        const string detail = "The image source could not be converted to a 32-bit bitmap.";

        var exception = new TrayIconException(TrayIconException.OperationConvertIcon, 0, detail);

        Assert.Contains(detail, exception.Message, StringComparison.Ordinal);
        Assert.Equal(TrayIconException.OperationConvertIcon, exception.Operation);

        // A non-Win32 failure reports 0 and must still say "failed": 0 is not a success signal.
        Assert.Equal(0, exception.Win32ErrorCode);
        Assert.Contains("failed", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The eight operation names are the string values a consumer branches on. They are pinned
    /// here so a rename is a deliberate edit, and so a ninth operation cannot appear without
    /// this list being updated.
    /// </summary>
    [Fact]
    public void Operation_constants_are_the_pinned_public_strings()
    {
        var expected = new List<string>
        {
            "OperationAdd=Add",
            "OperationConvertIcon=ConvertIcon",
            "OperationLoadIcon=LoadIcon",
            "OperationModify=Modify",
            "OperationOpenMenu=OpenMenu",
            "OperationRegisterMessage=RegisterMessage",
            "OperationRemove=Remove",
            "OperationSetVersion=SetVersion",
        };

        string[] actual = typeof(TrayIconException)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(field => field.IsLiteral && !field.IsInitOnly && field.FieldType == typeof(string))
            .Select(field => $"{field.Name}={field.GetRawConstantValue()}")
            .OrderBy(entry => entry, StringComparer.Ordinal)
            .ToArray();

        // Comparing the rendered name=value pairs keeps the failure message readable and catches
        // both a changed value and an added or removed constant.
        Assert.Equal(expected.OrderBy(entry => entry, StringComparer.Ordinal), actual);
    }

    /// <summary>
    /// The consumer catch is by type, so the type must be public - and sealed, because D008 makes
    /// it the single public tray exception rather than a hierarchy a caller is expected to extend.
    /// </summary>
    [Fact]
    public void Type_is_public_sealed_and_derives_from_Exception()
    {
        Type type = typeof(TrayIconException);

        Assert.True(type.IsPublic, $"{type.FullName} must be public: a consumer can only catch by type.");
        Assert.True(type.IsSealed, $"{type.FullName} must be sealed: D008 makes it the single tray exception type.");
        Assert.False(type.IsAbstract, $"{type.FullName} must be instantiable, not a static or abstract class.");
        Assert.True(
            typeof(Exception).IsAssignableFrom(type),
            $"{type.FullName} must derive from System.Exception.");
    }

    /// <summary>
    /// A missing operation name would make the consumer's branch impossible, so it is rejected at
    /// construction instead of being stored as null or empty.
    /// </summary>
    [Fact]
    public void Missing_operation_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => new TrayIconException(null!, 87));
        Assert.Throws<ArgumentException>(() => new TrayIconException(string.Empty, 87));

        // The detail overload validates on the same terms as the short one...
        Assert.Throws<ArgumentNullException>(() => new TrayIconException(null!, 87, "detail"));
        Assert.Throws<ArgumentException>(() => new TrayIconException(string.Empty, 87, "detail"));

        // ...while an empty detail is legitimate, not an error: the short overload passes exactly
        // that, and the resulting message must simply omit the detail sentence.
        var withoutDetail = new TrayIconException(TrayIconException.OperationAdd, 87, detail: string.Empty);

        Assert.EndsWith("failed with Win32 error 87.", withoutDetail.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The mechanical guard that internal interop types never leak into the package surface
    /// (D002 / D010), and that the public types stay in the documented namespace.
    /// </summary>
    /// <remarks>
    /// The later purity task tightens this to an exact set including <c>TrayIcon</c>; at this point
    /// the lifecycle type does not exist yet, so the relationship asserted is "subset". M001/S02/T02
    /// adds <c>TrayIconClickEventArgs</c> and <c>TrayMenuActivation</c> to the allow-list
    /// deliberately, M001/S04/T02 adds <c>BalloonTipIcon</c> and <c>BalloonTipOptions</c> the same
    /// way (D031), M002/S02/T01 adds the six toast content-model types
    /// (<c>ToastContent</c>, <c>ToastSeverity</c>, <c>ToastSound</c>, <c>ToastButton</c>,
    /// <c>ToastImage</c>, <c>ToastImagePlacement</c>), M002/S02/T05 adds the toast entry point
    /// and failure type (<c>ToastNotifier</c>, <c>ToastException</c>), and M002/S03/T01 adds the four
    /// activation types (<c>ToastActivatedEventArgs</c>, <c>ToastDismissedEventArgs</c>,
    /// <c>ToastDismissalReason</c>, <c>ToastErrorEventArgs</c>) the notifier's three events carry -
    /// this list is an enumeration, not a pattern, so a new public type has to be named here before
    /// it can ship. It is the third of the three allow-lists the S03 widening touches and the easiest
    /// one to miss, which is why the comment names the slice that grew it.
    /// </remarks>
    [Fact]
    public void Exported_types_are_in_the_library_namespace_and_within_the_documented_surface()
    {
        Type[] exported = typeof(TrayIconException).Assembly.GetExportedTypes();

        string observed = string.Join(
            ", ",
            exported.Select(type => type.FullName ?? type.Name).OrderBy(name => name, StringComparer.Ordinal));

        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "TrayIcon",
            "TrayIconException",
            "TrayErrorEventArgs",
            "TrayIconClickEventArgs",
            "TrayMenuActivation",
            "BalloonTipIcon",
            "BalloonTipOptions",
            "ToastContent",
            "ToastSeverity",
            "ToastSound",
            "ToastButton",
            "ToastImage",
            "ToastImagePlacement",
            "ToastNotifier",
            "ToastException",
            "ToastActivatedEventArgs",
            "ToastDismissedEventArgs",
            "ToastDismissalReason",
            "ToastErrorEventArgs",
        };

        string[] unexpected = exported
            .Where(type => !allowed.Contains(type.Name))
            .Select(type => type.FullName ?? type.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            unexpected.Length == 0,
            $"Unexpected exported types: {string.Join(", ", unexpected)}. Observed exported types: {observed}.");

        Assert.All(exported, type => Assert.Equal("Trustsoft.NotifyIcon", type.Namespace));

        // The surface is non-empty: if the export set ever becomes empty this test would pass
        // vacuously, and every "the public API is only X" assertion after it would too.
        Assert.NotEmpty(exported);
    }
}
