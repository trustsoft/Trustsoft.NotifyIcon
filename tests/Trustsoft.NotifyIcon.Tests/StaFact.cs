using System.Reflection;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Trustsoft.NotifyIcon.Tests;

/// <summary>
/// Marks a test as requiring a single-threaded-apartment (<c>STA</c>) thread.
/// </summary>
/// <remarks>
/// A plain <c>[Fact]</c> that touches WPF types throws
/// <c>InvalidOperationException: The calling thread must be STA</c>. Test bodies marked with
/// this attribute run on a dedicated <see cref="Thread"/> configured with
/// <see cref="ApartmentState.STA"/>. The <c>Xunit.StaFact</c> package is intentionally not
/// referenced: the seam is small, and rolling it by hand keeps the xunit version in the test
/// graph exactly pinned to the explicitly referenced packages.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class StaFactAttribute : FactAttribute
{
    internal static bool IsSupported =>
        System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
            System.Runtime.InteropServices.OSPlatform.Windows);

    /// <inheritdoc />
    public override string? Skip
    {
        get => base.Skip ?? (IsSupported ? null : "STA tests require Windows.");
        set => base.Skip = value;
    }
}

/// <summary>
/// The <see cref="StaFactAttribute"/> equivalent for theories whose data rows also require an
/// STA thread.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class StaTheoryAttribute : TheoryAttribute
{
    /// <inheritdoc />
    public override string? Skip
    {
        get => base.Skip ?? (StaFactAttribute.IsSupported ? null : "STA tests require Windows.");
        set => base.Skip = value;
    }
}

/// <summary>
/// Runs the test method body on a dedicated STA thread while the xunit runner stays on the
/// original thread.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Xunit.Sdk.XunitTestInvoker"/> has no virtual <c>RunTestAsync</c>; the supported
/// derivation point is <see cref="InvokeTestMethodAsync(object)"/>, which receives the already
/// constructed test class instance. Overriding it lets the whole method body (including
/// <c>BeforeAfterTest</c> hooks, which execute inside that call) run on the STA thread.
/// </para>
/// <para>
/// The body is started on the STA thread and awaited through a
/// <see cref="TaskCompletionSource{TResult}"/> so xunit's async continuation does not depend on
/// the STA thread's message pump, which is never pumped here.
/// </para>
/// </remarks>
internal sealed class StaTestInvoker : XunitTestInvoker
{
    internal StaTestInvoker(
        ITest test,
        IMessageBus messageBus,
        Type testClass,
        object?[] constructorArguments,
        MethodInfo testMethod,
        object?[]? testMethodArguments,
        IReadOnlyList<BeforeAfterTestAttribute> beforeAfterAttributes,
        ExceptionAggregator aggregator,
        CancellationTokenSource cancellationTokenSource)
        : base(
            test,
            messageBus,
            testClass,
            constructorArguments,
            testMethod,
            testMethodArguments,
            beforeAfterAttributes,
            aggregator,
            cancellationTokenSource)
    {
    }

    /// <inheritdoc />
    protected override Task<decimal> InvokeTestMethodAsync(object testClassInstance)
    {
        var completionSource = new TaskCompletionSource<decimal>(TaskCreationOptions.RunContinuationsAsynchronously);

        var staThread = new Thread(() =>
        {
            try
            {
                // Runs the real method body (and its before/after hooks) on the STA thread.
                decimal result = base.InvokeTestMethodAsync(testClassInstance).GetAwaiter().GetResult();
                completionSource.TrySetResult(result);
            }
            catch (Exception ex)
            {
                completionSource.TrySetException(ex);
            }
        })
        {
            IsBackground = true,
            Name = $"stafact:{Test.DisplayName}",
        };

        staThread.SetApartmentState(ApartmentState.STA);
        staThread.Start();

        return completionSource.Task;
    }
}

/// <summary>
/// Selects <see cref="StaTestInvoker"/> for test methods annotated with
/// <see cref="StaFactAttribute"/> or <see cref="StaTheoryAttribute"/>.
/// </summary>
public sealed class StaTestFramework : XunitTestFramework
{
    /// <summary>
    /// Initializes a new instance of the <see cref="StaTestFramework"/> class.
    /// </summary>
    /// <param name="messageSink">The message sink the framework reports to.</param>
    public StaTestFramework(IMessageSink messageSink)
        : base(messageSink)
    {
    }
}

/// <summary>
/// Test case that routes STA-marked methods through <see cref="StaTestInvoker"/>.
/// </summary>
public sealed class StaTestCase : XunitTestCase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="StaTestCase"/> class for deserialization.
    /// </summary>
    [Obsolete("Called by the de-serializer only.", error: false)]
    public StaTestCase()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="StaTestCase"/> class.
    /// </summary>
    /// <param name="diagnosticMessageSink">The diagnostic message sink.</param>
    /// <param name="defaultTestMethodDisplayName">The default display name for the test method.</param>
    /// <param name="testMethodDisplayOptions">Flags controlling how the test method name is displayed.</param>
    /// <param name="testMethod">The test method.</param>
    /// <param name="testMethodArguments">Arguments for the test method.</param>
    public StaTestCase(
        IMessageSink diagnosticMessageSink,
        TestMethodDisplay defaultTestMethodDisplayName,
        TestMethodDisplayOptions testMethodDisplayOptions,
        ITestMethod testMethod,
        object?[]? testMethodArguments)
        : base(diagnosticMessageSink, defaultTestMethodDisplayName, testMethodDisplayOptions, testMethod, testMethodArguments)
    {
    }

    /// <inheritdoc />
    public override Task<RunSummary> RunAsync(
        IMessageSink diagnosticMessageSink,
        IMessageBus messageBus,
        object?[] constructorArguments,
        ExceptionAggregator aggregator,
        CancellationTokenSource cancellationTokenSource)
        => new StaTestCaseRunner(
            this,
            DisplayName,
            messageBus,
            constructorArguments,
            aggregator,
            cancellationTokenSource).RunAsync();
}

/// <summary>
/// Runner that forwards STA test cases to <see cref="StaTestInvoker"/>.
/// </summary>
internal sealed class StaTestCaseRunner : XunitTestCaseRunner
{
    internal StaTestCaseRunner(
        IXunitTestCase testCase,
        string displayName,
        IMessageBus messageBus,
        object?[] constructorArguments,
        ExceptionAggregator aggregator,
        CancellationTokenSource cancellationTokenSource)
        : base(
            testCase,
            displayName,
            skipReason: null,
            constructorArguments,
            testCase.TestMethodArguments,
            messageBus,
            aggregator,
            cancellationTokenSource)
    {
    }

    /// <inheritdoc />
    protected override XunitTestRunner CreateTestRunner(
        ITest test,
        IMessageBus messageBus,
        Type testClass,
        object?[] constructorArguments,
        MethodInfo testMethod,
        object?[]? testMethodArguments,
        string? skipReason,
        IReadOnlyList<BeforeAfterTestAttribute> beforeAfterAttributes,
        ExceptionAggregator aggregator,
        CancellationTokenSource cancellationTokenSource)
        => new StaTestRunner(
            test,
            messageBus,
            testClass,
            constructorArguments,
            testMethod,
            testMethodArguments,
            skipReason,
            beforeAfterAttributes,
            aggregator,
            cancellationTokenSource);
}

/// <summary>
/// Runner whose invoker is <see cref="StaTestInvoker"/>.
/// </summary>
internal sealed class StaTestRunner : XunitTestRunner
{
    internal StaTestRunner(
        ITest test,
        IMessageBus messageBus,
        Type testClass,
        object?[] constructorArguments,
        MethodInfo testMethod,
        object?[]? testMethodArguments,
        string? skipReason,
        IReadOnlyList<BeforeAfterTestAttribute> beforeAfterAttributes,
        ExceptionAggregator aggregator,
        CancellationTokenSource cancellationTokenSource)
        : base(
            test,
            messageBus,
            testClass,
            constructorArguments,
            testMethod,
            testMethodArguments,
            skipReason,
            beforeAfterAttributes,
            aggregator,
            cancellationTokenSource)
    {
    }

    /// <inheritdoc />
    protected override Task<decimal> InvokeTestMethodAsync(ExceptionAggregator aggregator)
        => new StaTestInvoker(
            Test,
            MessageBus,
            TestClass,
            ConstructorArguments,
            TestMethod,
            TestMethodArguments,
            BeforeAfterAttributes,
            aggregator,
            CancellationTokenSource).RunAsync();
}
