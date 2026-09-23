// Makes the internal shell seam (IShellApi, shell constants, NOTIFYICONDATAW) reachable
// from the test assembly without widening the shipped public API (D009).
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Trustsoft.NotifyIcon.Tests")]

// The windowless sample, for the M002/S01 live end-to-end run.
//
// The toast seam (IToastApi, ToastApi, ToastIdentity, ToastShow, ToastPayload) is internal and S01 has
// no public toast surface yet: the public ToastNotifier/ToastContent is S02/S03 work (D052, D056).
// The slice's exit condition is nevertheless a live run from the windowless sample - register, show,
// subscribe, print the activation - so the sample has to reach the seam that exists now. This is a
// grant to a non-shipping project in this repository only (the sample declares IsPackable=false and
// is not a package), it widens no public API, and it is temporary in spirit: once S02/S03 ship the
// public toast surface the sample's demonstration moves onto it, and this line can go. The sample's
// tray code stays free of library internals (it reaches the library's hidden host window only through
// public window enumeration), so nothing about the shipped consumer surface changes.
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Trustsoft.NotifyIcon.Sample")]

// The consumer namespace for markup (S06).
//
// Without this, a declarative consumer has to write the CLR namespace and the assembly name by hand -
// clr-namespace:Trustsoft.NotifyIcon;assembly=Trustsoft.NotifyIcon - and the assembly part is not
// optional: the XAML parser has no "local assembly" context to fall back on, so omitting it fails the
// parse outright. Mapping a stable URI to the library namespace lets markup say
// xmlns:tni="http://schemas.trustsoft.com/notifyicon" and nothing else.
//
// The URI is an identifier, not a location: nothing ever fetches it, and XML namespace URIs are not
// required to resolve. It is deliberately stable and versionless so a consumer's markup does not have
// to change when the package version does. S07 owns the package metadata and will confirm this URI
// alongside it; it is declared here because the sample is already the reference consumer.
[assembly: System.Windows.Markup.XmlnsDefinition(
    "http://schemas.trustsoft.com/notifyicon",
    "Trustsoft.NotifyIcon")]

// The prefix WPF's designers and tooling suggest for that namespace, so generated markup agrees with
// hand-written markup (the sample uses the same one).
[assembly: System.Windows.Markup.XmlnsPrefix(
    "http://schemas.trustsoft.com/notifyicon",
    "tni")]
