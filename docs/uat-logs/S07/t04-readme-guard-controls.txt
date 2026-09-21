S07/T04 - negative controls for the README guard
date:          2026-09-22 00:37:53
machine:       MinibookX
test:          PackagePurityTests.Readme_documents_the_install_line_usage_and_the_shipped_surface
filter:        --TestCaseFilter:"FullyQualifiedName~Readme_documents_the_install_line_usage_and_the_shipped_surface"
README.md:     sha256 5310d269b1296f61ea10d6af7bab605a34de5ac306bede82c95df42addb2a526

----------------------------------------------------------------
positive control - the README as it stands
----------------------------------------------------------------
Starting test execution, please wait...
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:     1, Skipped:     0, Total:     1, Duration: 6 ms - Trustsoft.NotifyIcon.Tests.dll (net8.0)
  PASS  the guard passes on the delivered README (exit 0)

----------------------------------------------------------------
control 1 - the install line advertises version 9.9.9 (the version-bump trap)
----------------------------------------------------------------
mutated README.md sha256: 2b7a9e9150034b7b449d0671141ee3cb0cd0afe11d8787113b4b7b2cbfd751d5
VSTest version 18.7.0 (x64)

Starting test execution, please wait...
A total of 1 test files matched the specified pattern.
[xUnit.net 00:00:00.33]     Trustsoft.NotifyIcon.Tests.PackagePurityTests.Readme_documents_the_install_line_usage_and_the_shipped_surface [FAIL]
  Failed Trustsoft.NotifyIcon.Tests.PackagePurityTests.Readme_documents_the_install_line_usage_and_the_shipped_surface [6 ms]
  Error Message:
   R010: the README is the package's readme, so it must show the install line a consumer copies: <PackageReference Include="Trustsoft.NotifyIcon" Version="1.0.0" />. It is built from the library project's own PackageId and Version here, so a version bump has to update the document instead of shipping a README that tells a consumer to install a version that does not exist.
  Stack Trace:
     at Trustsoft.NotifyIcon.Tests.PackagePurityTests.Readme_documents_the_install_line_usage_and_the_shipped_surface() in C:\Users\Maxim\Desktop\Trustsoft.NotifyIcon\.gsd-worktrees\M001\tests\Trustsoft.NotifyIcon.Tests\PackagePurityTests.cs:line 541
   at System.RuntimeMethodHandle.InvokeMethod(Object target, Void** arguments, Signature sig, Boolean isConstructor)
   at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)

Failed!  - Failed:     1, Passed:     0, Skipped:     0, Total:     1, Duration: 6 ms - Trustsoft.NotifyIcon.Tests.dll (net8.0)
  PASS  the guard failed (exit 1) and named the broken fact: must show the install line a consumer copies
  PASS  the README is restored byte-for-byte (5310d269b1296f61ea10d6af7bab605a34de5ac306bede82c95df42addb2a526)

----------------------------------------------------------------
control 2 - the consumer markup namespace URI is removed everywhere it appears
----------------------------------------------------------------
mutated README.md sha256: 702701dfc10bbb4c9daad0bb12547a00a2bb7203a170c7be96a25861f7b7889c
VSTest version 18.7.0 (x64)

Starting test execution, please wait...
A total of 1 test files matched the specified pattern.
[xUnit.net 00:00:00.33]     Trustsoft.NotifyIcon.Tests.PackagePurityTests.Readme_documents_the_install_line_usage_and_the_shipped_surface [FAIL]
  Failed Trustsoft.NotifyIcon.Tests.PackagePurityTests.Readme_documents_the_install_line_usage_and_the_shipped_surface [5 ms]
  Error Message:
   R010: the README must document the consumer markup namespace http://schemas.trustsoft.com/notifyicon, which is what a declarative consumer writes instead of a clr-namespace and an assembly name.
  Stack Trace:
     at Trustsoft.NotifyIcon.Tests.PackagePurityTests.Readme_documents_the_install_line_usage_and_the_shipped_surface() in C:\Users\Maxim\Desktop\Trustsoft.NotifyIcon\.gsd-worktrees\M001\tests\Trustsoft.NotifyIcon.Tests\PackagePurityTests.cs:line 549
   at System.RuntimeMethodHandle.InvokeMethod(Object target, Void** arguments, Signature sig, Boolean isConstructor)
   at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)

Failed!  - Failed:     1, Passed:     0, Skipped:     0, Total:     1, Duration: 5 ms - Trustsoft.NotifyIcon.Tests.dll (net8.0)
  PASS  the guard failed (exit 1) and named the broken fact: must document the consumer markup namespace
  PASS  the README is restored byte-for-byte (5310d269b1296f61ea10d6af7bab605a34de5ac306bede82c95df42addb2a526)

----------------------------------------------------------------
control 3 - the "## Windowless shutdown" section is dropped
----------------------------------------------------------------
mutated README.md sha256: 5efa26cb89fa358c27e3337ddb4ec836757146905a2748a4239a1dee0dac5c8a
VSTest version 18.7.0 (x64)

Starting test execution, please wait...
A total of 1 test files matched the specified pattern.
[xUnit.net 00:00:00.33]     Trustsoft.NotifyIcon.Tests.PackagePurityTests.Readme_documents_the_install_line_usage_and_the_shipped_surface [FAIL]
  Failed Trustsoft.NotifyIcon.Tests.PackagePurityTests.Readme_documents_the_install_line_usage_and_the_shipped_surface [5 ms]
  Error Message:
   R010: the README must carry a '## Windowless shutdown' section. A consumer who installs the package reads this file first, and these are the questions they arrive with: how to install it, how to use it from code, how to declare it in markup, what a windowless application must do about shutdown, and how the interaction model behaves.
  Stack Trace:
     at Trustsoft.NotifyIcon.Tests.PackagePurityTests.Readme_documents_the_install_line_usage_and_the_shipped_surface() in C:\Users\Maxim\Desktop\Trustsoft.NotifyIcon\.gsd-worktrees\M001\tests\Trustsoft.NotifyIcon.Tests\PackagePurityTests.cs:line 573
   at System.RuntimeMethodHandle.InvokeMethod(Object target, Void** arguments, Signature sig, Boolean isConstructor)
   at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)

Failed!  - Failed:     1, Passed:     0, Skipped:     0, Total:     1, Duration: 5 ms - Trustsoft.NotifyIcon.Tests.dll (net8.0)
  PASS  the guard failed (exit 1) and named the broken fact: must carry a '## Windowless shutdown' section
  PASS  the README is restored byte-for-byte (5310d269b1296f61ea10d6af7bab605a34de5ac306bede82c95df42addb2a526)

----------------------------------------------------------------
control 4 - "## Repository notes" is moved above the install line
----------------------------------------------------------------
mutated README.md sha256: a672f300c5f275929c61b88924f53faa470805eebbcc8a3f443c8121c211a217
VSTest version 18.7.0 (x64)

Starting test execution, please wait...
A total of 1 test files matched the specified pattern.
[xUnit.net 00:00:00.34]     Trustsoft.NotifyIcon.Tests.PackagePurityTests.Readme_documents_the_install_line_usage_and_the_shipped_surface [FAIL]
  Failed Trustsoft.NotifyIcon.Tests.PackagePurityTests.Readme_documents_the_install_line_usage_and_the_shipped_surface [6 ms]
  Error Message:
   R010: the README's repository-facing content must sit below the consumer content. A '## Repository notes' heading that precedes the install line puts the repository's build instructions in front of a package user.
  Stack Trace:
     at Trustsoft.NotifyIcon.Tests.PackagePurityTests.Readme_documents_the_install_line_usage_and_the_shipped_surface() in C:\Users\Maxim\Desktop\Trustsoft.NotifyIcon\.gsd-worktrees\M001\tests\Trustsoft.NotifyIcon.Tests\PackagePurityTests.cs:line 584
   at System.RuntimeMethodHandle.InvokeMethod(Object target, Void** arguments, Signature sig, Boolean isConstructor)
   at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)

Failed!  - Failed:     1, Passed:     0, Skipped:     0, Total:     1, Duration: 6 ms - Trustsoft.NotifyIcon.Tests.dll (net8.0)
  PASS  the guard failed (exit 1) and named the broken fact: repository-facing content must sit below the consumer content
  PASS  the README is restored byte-for-byte (5310d269b1296f61ea10d6af7bab605a34de5ac306bede82c95df42addb2a526)

----------------------------------------------------------------
control 5 - BalloonTipOptions is no longer named anywhere in the README
----------------------------------------------------------------
mutated README.md sha256: b6c912d11ba67572c72e1cf20e3eaf6a5d4393395d74af1c048e724fb14084f0
VSTest version 18.7.0 (x64)

Starting test execution, please wait...
A total of 1 test files matched the specified pattern.
[xUnit.net 00:00:00.35]     Trustsoft.NotifyIcon.Tests.PackagePurityTests.Readme_documents_the_install_line_usage_and_the_shipped_surface [FAIL]
  Failed Trustsoft.NotifyIcon.Tests.PackagePurityTests.Readme_documents_the_install_line_usage_and_the_shipped_surface [8 ms]
  Error Message:
   R010: the README documents the shipped public surface, but does not name [BalloonTipOptions]. Public_surface_is_only_the_documented_types pins the shipped set; this assertion is what makes the word 'documented' mean the readme the package ships, rather than a list that exists only inside the test suite.
  Stack Trace:
     at Trustsoft.NotifyIcon.Tests.PackagePurityTests.Readme_documents_the_install_line_usage_and_the_shipped_surface() in C:\Users\Maxim\Desktop\Trustsoft.NotifyIcon\.gsd-worktrees\M001\tests\Trustsoft.NotifyIcon.Tests\PackagePurityTests.cs:line 602
   at System.RuntimeMethodHandle.InvokeMethod(Object target, Void** arguments, Signature sig, Boolean isConstructor)
   at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)

Failed!  - Failed:     1, Passed:     0, Skipped:     0, Total:     1, Duration: 8 ms - Trustsoft.NotifyIcon.Tests.dll (net8.0)
  PASS  the guard failed (exit 1) and named the broken fact: does not name [BalloonTipOptions]
  PASS  the README is restored byte-for-byte (5310d269b1296f61ea10d6af7bab605a34de5ac306bede82c95df42addb2a526)

================================================================
SUMMARY  0 control failure(s)
README.md sha256 at the end: 5310d269b1296f61ea10d6af7bab605a34de5ac306bede82c95df42addb2a526
README.md sha256 at the start: 5310d269b1296f61ea10d6af7bab605a34de5ac306bede82c95df42addb2a526
