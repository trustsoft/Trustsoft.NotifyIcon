// Makes the internal shell seam (IShellApi, shell constants, NOTIFYICONDATAW) reachable
// from the test assembly without widening the shipped public API (D009).
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Trustsoft.NotifyIcon.Tests")]

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
