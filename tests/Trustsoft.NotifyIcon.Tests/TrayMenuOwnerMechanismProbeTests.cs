using Trustsoft.NotifyIcon.Interop;
using Xunit;
using Xunit.Abstractions;

namespace Trustsoft.NotifyIcon.Tests;

/// <summary>
/// The S08 measurement probe: the mechanism the library now ships for the menu popup's owner and the
/// foreground relationship, measured against real Windows with the foreground claim refused
/// hermetically through the shell seam.
/// </summary>
/// <remarks>
/// <para>
/// <b>What this class is for now.</b> T01 built this as a variant harness - the shipped construction,
/// the explicit owner, and the attach-thread foreground sequence, each applied to the live popup by
/// the test - and used it to measure which mechanism to ship. T02 moved the chosen mechanism into the
/// library's own open path, so the harness that applies repairs by hand no longer describes the
/// product: what is measurable now is <em>the delivered path's</em> internals, which is what this
/// class asserts. The variant table and its raw lines stay in
/// <c>docs/REMEDIATION-S08-MEASUREMENT.md</c> as the evidence behind D044.
/// </para>
/// <para>
/// <b>Why the internals and not the outcome again.</b> The delivered contract - the anchor owns the
/// popup, an outside click dismisses it, nothing is left behind - is asserted by
/// <c>TrayIconMenuOwnerDeterminismTests</c>. Here the subject is the mechanism itself: that WPF really
/// did build the popup ownerless under the scripted refusal (otherwise the hostile state is no longer
/// reproduced and the repair proves nothing), that the library wrote the anchor into the owner slot
/// and read it back, that a second claim really was made and granted through the attach-thread
/// sequence, and that the popup was resolved from the menu's own presentation source rather than from
/// a size or class heuristic over the process's windows.
/// </para>
/// <para>
/// <b>The menu is opened through the product's own path.</b> A registered <see cref="TrayIcon"/> with
/// an assigned menu receives the shell's version-4 <c>WM_CONTEXTMENU</c> callback by a same-thread
/// <c>SendMessage</c> (the S02 and T04 injection precedent); the anchor, the popup and the click are
/// real, and only the shell seam answers are scripted. The refusal is
/// <see cref="FakeShellApi.SetForegroundWindowResult"/>, which is the recorded hostile state
/// (<c>setForegroundWindow=False</c>, <c>owner=0x0</c>) rather than whichever window happens to hold
/// the foreground on the machine running the suite. It is consumed by the first claim - the one WPF's
/// construction depends on - so the repair's re-claim is a real call whose result is measured (see
/// <see cref="FakeShellApi.SetForegroundWindow"/>).
/// </para>
/// <para>
/// <b>Every test writes its raw line to the test output channel and carries it in every assertion
/// message.</b> The numbers in the remediation document's table were produced by this instrument, and
/// a failing assertion here is diagnosable without a debugger.
/// </para>
/// <para>
/// The class joins <see cref="TrayMenuDismissalCollection"/>: it creates real top-level windows,
/// injects real mouse input and reads the desktop-wide foreground window, so it must not share the
/// process with another test class that does the same.
/// </para>
/// </remarks>
[Collection(TrayMenuDismissalCollection.Name)]
public sealed class TrayMenuOwnerMechanismProbeTests
{
    private readonly ITestOutputHelper _output;

    /// <summary>Initializes the probe and its raw-measurement output channel.</summary>
    /// <param name="output">The test output the raw measurement line is written to.</param>
    public TrayMenuOwnerMechanismProbeTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// The hostile state is reproduced by the scripted refusal, and the library records it: the claim
    /// the library made before the popup existed was refused, and WPF's own construction left the
    /// popup with no owner.
    /// </summary>
    /// <remarks>
    /// This is the control the whole repair rests on. If WPF ever starts owning the popup without the
    /// process holding the foreground, the hostile state is no longer reproduced and the repair's
    /// evidence has to be re-measured rather than assumed.
    /// </remarks>
    [StaFact]
    public void The_refused_claim_is_really_refused_and_wpf_builds_the_popup_ownerless()
    {
        MenuDeterminismResult result = TrayMenuProductScenario.Run(refuseClaim: true);

        _output.WriteLine(result.Describe());

        // The library really made the claim through the seam, and the seam refused it.
        Assert.True(result.ClaimObserved, $"The library must claim the foreground through the seam. {result.Describe()}");
        Assert.False(result.ClaimResults[0], $"This probe scripts the refusal. {result.Describe()}");

        // The popup opened and was namable, so "ownerless" is a reading about a real window.
        Assert.NotEqual(IntPtr.Zero, result.PopupHandle);
        Assert.True(result.IsOpenBeforeClick, $"The menu should have opened. {result.Describe()}");

        // ... and WPF built it without an owner, which is the measured failing shape (F5).
        Assert.Equal(IntPtr.Zero, result.OwnerBeforeRepair);
    }

    /// <summary>
    /// The library writes the anchor into the popup's owner slot and reads the value back, so the
    /// owner stops being WPF's internal decision.
    /// </summary>
    /// <remarks>
    /// The read-back is the load-bearing half: <c>SetWindowLongPtr</c> returns the <em>previous</em>
    /// owner, so a value taken from the write's return would prove nothing. The assertion compares the
    /// library's after-reading with an independent reading of the same window taken by this test.
    /// </remarks>
    [StaFact]
    public void The_delivered_open_writes_the_anchor_into_the_owner_slot_and_reads_it_back()
    {
        MenuDeterminismResult result = TrayMenuProductScenario.Run(refuseClaim: true);

        _output.WriteLine(result.Describe());

        IntPtr anchor = result.AnchorHandleBeforeClick;

        Assert.NotEqual(IntPtr.Zero, anchor);
        Assert.True(result.OwnerRepaired, $"The owner write is what repaired this popup. {result.Describe()}");

        // The library's read-back, the independent reading, and the value neither of them may be.
        Assert.Equal(anchor, result.OwnerAfterRepair);
        Assert.Equal(anchor, result.OwnerMeasured);
        Assert.NotEqual(result.HostHandle, result.OwnerMeasured);
    }

    /// <summary>
    /// The refused claim triggers the attach-thread foreground sequence: a second claim is made and
    /// granted, and the anchor becomes the desktop's foreground window.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The two halves of D044's mechanism are separate facts and are asserted separately: the owner
    /// write (the test above) and the activation relationship, here. The recorded reason the
    /// mechanism carries both is the V2 row of <c>docs/REMEDIATION-S08-MEASUREMENT.md</c> - the owner
    /// write applied to a popup that had already been shown left the menu open - while in the
    /// delivered path the write and the re-claim both happen inside the open.
    /// </para>
    /// <para>
    /// The dismissal that follows is asserted by <c>TrayIconMenuOwnerDeterminismTests</c>; here the
    /// subject is the seam's own evidence: two claims, the second one granted, and the foreground
    /// window while the menu is open being the anchor.
    /// </para>
    /// </remarks>
    [StaFact]
    public void The_delivered_open_reclaims_the_foreground_because_the_plain_claim_was_refused()
    {
        MenuDeterminismResult result = TrayMenuProductScenario.Run(refuseClaim: true);

        _output.WriteLine(result.Describe());

        Assert.True(result.AnchorIsForeground, $"The refused claim must be re-claimed. {result.Describe()}");
        Assert.Equal(2, result.ClaimResults.Count);
        Assert.False(result.ClaimResults[0]);
        Assert.True(result.ClaimResults[1], $"The re-claim must be granted. {result.Describe()}");
        Assert.Equal(result.AnchorHandleBeforeClick, result.ForegroundAfterRepair);
    }

    /// <summary>
    /// The popup window is resolved from the menu's own presentation source, not by a size or class
    /// heuristic over the process's windows.
    /// </summary>
    /// <remarks>
    /// A consumer application has large windows of its own, so a heuristic is not a contract; the
    /// heuristic result is recorded here only to show that both routes agree on this shape, not
    /// because the library uses it.
    /// </remarks>
    [StaFact]
    public void The_popup_is_resolved_from_the_menu_presentation_source_not_a_size_heuristic()
    {
        MenuDeterminismResult result = TrayMenuProductScenario.Run(refuseClaim: true);

        _output.WriteLine(result.Describe());

        Assert.NotEqual(IntPtr.Zero, result.PresentationSourcePopup);
        Assert.Equal(result.PresentationSourcePopup, result.PopupHandle);

        // The measurement agrees with the size discrimination, which is why the recorded numbers of
        // the two routes can be compared at all.
        Assert.Equal(Assert.Single(result.HeuristicPopupWindows), result.PresentationSourcePopup);
    }
}
