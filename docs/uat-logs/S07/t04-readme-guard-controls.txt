S07/T04 - negative controls for the README guard
date:          2026-09-21 16:25:41
machine:       MinibookX
test:          PackagePurityTests.Readme_documents_the_install_line_usage_and_the_shipped_surface
filter:        --TestCaseFilter:"FullyQualifiedName~Readme_documents_the_install_line_usage_and_the_shipped_surface"
README.md:     sha256 e87308ab2af00b805def26e8fa2cae1c6570fff2215a974de6a08bf2cc366820

----------------------------------------------------------------
positive control - the README as it stands
----------------------------------------------------------------
Starting test execution, please wait...
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:     1, Skipped:     0, Total:     1, Duration: 7 ms - Trustsoft.NotifyIcon.Tests.dll (net8.0)
  PASS  the guard passes on the delivered README (exit 0)

----------------------------------------------------------------
control 1 - the install line advertises version 9.9.9 (the version-bump trap)
----------------------------------------------------------------
mutated README.md sha256: d3a7aaa9a7e7e798d21ceccd086b7040e909d2406edf40776229f60ba30b5751
VSTest version 18.7.0 (x64)

Starting test execution, please wait...
A total of 1 test files matched the specified pattern.
[xUnit.net 00:00:00.37]     Trustsoft.NotifyIcon.Tests.PackagePurityTests.Readme_documents_the_install_line_usage_and_the_shipped_surface [FAIL]
  Failed Trustsoft.NotifyIcon.Tests.PackagePurityTests.Readme_documents_the_install_line_usage_and_the_shipped_surface [7 ms]
  Error Message:
   R010: the README is the package's readme, so it must show the install line a consumer copies: <PackageReference Include="Trustsoft.NotifyIcon" Version="1.0.0" />. It is built from the library project's own PackageId and Version here, so a version bump has to update the document instead of shipping a README that tells a consumer to install a version that does not exist.
  Stack Trace:
     at Trustsoft.NotifyIcon.Tests.PackagePurityTests.Readme_documents_the_install_line_usage_and_the_shipped_surface() in C:\Users\Maxim\YandexDisk\Projects AI\Trustsoft.NotifyIcon\.gsd-worktrees\M001\tests\Trustsoft.NotifyIcon.Tests\PackagePurityTests.cs:line 541
   at System.RuntimeMethodHandle.InvokeMethod(Object target, Void** arguments, Signature sig, Boolean isConstructor)
   at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)

Failed!  - Failed:     1, Passed:     0, Skipped:     0, Total:     1, Duration: 7 ms - Trustsoft.NotifyIcon.Tests.dll (net8.0)
  PASS  the guard failed (exit 1) and named the broken fact: must show the install line a consumer copies
  PASS  the README is restored byte-for-byte (e87308ab2af00b805def26e8fa2cae1c6570fff2215a974de6a08bf2cc366820)

----------------------------------------------------------------
control 2 - the consumer markup namespace URI is removed everywhere it appears
----------------------------------------------------------------
mutated README.md sha256: 5202714758984305c6d03d355f6cd06c8a5dec2a50391046abc40bdcc4778c4a
VSTest version 18.7.0 (x64)

Starting test execution, please wait...
A total of 1 test files matched the specified pattern.
[xUnit.net 00:00:00.36]     Trustsoft.NotifyIcon.Tests.PackagePurityTests.Readme_documents_the_install_line_usage_and_the_shipped_surface [FAIL]
  Failed Trustsoft.NotifyIcon.Tests.PackagePurityTests.Readme_documents_the_install_line_usage_and_the_shipped_surface [6 ms]
  Error Message:
   R010: the README must document the consumer markup namespace http://schemas.trustsoft.com/notifyicon, which is what a declarative consumer writes instead of a clr-namespace and an assembly name.
  Stack Trace:
     at Trustsoft.NotifyIcon.Tests.PackagePurityTests.Readme_documents_the_install_line_usage_and_the_shipped_surface() in C:\Users\Maxim\YandexDisk\Projects AI\Trustsoft.NotifyIcon\.gsd-worktrees\M001\tests\Trustsoft.NotifyIcon.Tests\PackagePurityTests.cs:line 549
   at System.RuntimeMethodHandle.InvokeMethod(Object target, Void** arguments, Signature sig, Boolean isConstructor)
   at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)

Failed!  - Failed:     1, Passed:     0, Skipped:     0, Total:     1, Duration: 6 ms - Trustsoft.NotifyIcon.Tests.dll (net8.0)
  PASS  the guard failed (exit 1) and named the broken fact: must document the consumer markup namespace
  PASS  the README is restored byte-for-byte (e87308ab2af00b805def26e8fa2cae1c6570fff2215a974de6a08bf2cc366820)

----------------------------------------------------------------
control 3 - the "## Windowless shutdown" section is dropped
----------------------------------------------------------------
mutated README.md sha256: 4591e981d48259b4ff44a5605b48bae51b0d48738a967cb4d820f5f52ba39085
VSTest version 18.7.0 (x64)

Starting test execution, please wait...
A total of 1 test files matched the specified pattern.
[xUnit.net 00:00:00.38]     Trustsoft.NotifyIcon.Tests.PackagePurityTests.Readme_documents_the_install_line_usage_and_the_shipped_surface [FAIL]
  Failed Trustsoft.NotifyIcon.Tests.PackagePurityTests.Readme_documents_the_install_line_usage_and_the_shipped_surface [8 ms]
  Error Message:
   R010: the README must carry a '## Windowless shutdown' section. A consumer who installs the package reads this file first, and these are the questions they arrive with: how to install it, how to use it from code, how to declare it in markup, what a windowless application must do about shutdown, and how the interaction model behaves.
  Stack Trace:
     at Trustsoft.NotifyIcon.Tests.PackagePurityTests.Readme_documents_the_install_line_usage_and_the_shipped_surface() in C:\Users\Maxim\YandexDisk\Projects AI\Trustsoft.NotifyIcon\.gsd-worktrees\M001\tests\Trustsoft.NotifyIcon.Tests\PackagePurityTests.cs:line 573
   at System.RuntimeMethodHandle.InvokeMethod(Object target, Void** arguments, Signature sig, Boolean isConstructor)
   at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)

Failed!  - Failed:     1, Passed:     0, Skipped:     0, Total:     1, Duration: 8 ms - Trustsoft.NotifyIcon.Tests.dll (net8.0)
  PASS  the guard failed (exit 1) and named the broken fact: must carry a '## Windowless shutdown' section
  PASS  the README is restored byte-for-byte (e87308ab2af00b805def26e8fa2cae1c6570fff2215a974de6a08bf2cc366820)

----------------------------------------------------------------
control 4 - "## Repository notes" is moved above the install line
----------------------------------------------------------------
mutated README.md sha256: 13028af6966c6dffd1929e356ad10a3132a9e488b758265091dcbbef973f264d
VSTest version 18.7.0 (x64)

Starting test execution, please wait...
A total of 1 test files matched the specified pattern.
[xUnit.net 00:00:00.36]     Trustsoft.NotifyIcon.Tests.PackagePurityTests.Readme_documents_the_install_line_usage_and_the_shipped_surface [FAIL]
  Failed Trustsoft.NotifyIcon.Tests.PackagePurityTests.Readme_documents_the_install_line_usage_and_the_shipped_surface [9 ms]
  Error Message:
   R010: the README's repository-facing content must sit below the consumer content. A '## Repository notes' heading that precedes the install line puts the repository's build instructions in front of a package user.
  Stack Trace:
     at Trustsoft.NotifyIcon.Tests.PackagePurityTests.Readme_documents_the_install_line_usage_and_the_shipped_surface() in C:\Users\Maxim\YandexDisk\Projects AI\Trustsoft.NotifyIcon\.gsd-worktrees\M001\tests\Trustsoft.NotifyIcon.Tests\PackagePurityTests.cs:line 584
   at System.RuntimeMethodHandle.InvokeMethod(Object target, Void** arguments, Signature sig, Boolean isConstructor)
   at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)

Failed!  - Failed:     1, Passed:     0, Skipped:     0, Total:     1, Duration: 9 ms - Trustsoft.NotifyIcon.Tests.dll (net8.0)
  PASS  the guard failed (exit 1) and named the broken fact: repository-facing content must sit below the consumer content
  PASS  the README is restored byte-for-byte (e87308ab2af00b805def26e8fa2cae1c6570fff2215a974de6a08bf2cc366820)

----------------------------------------------------------------
control 5 - BalloonTipOptions is no longer named anywhere in the README
----------------------------------------------------------------
mutated README.md sha256: 6fc6361b92b3e4ed4d0a6ec28a7d95a367f19a2b5e60629be97ffc8430d07123
VSTest version 18.7.0 (x64)

Starting test execution, please wait...
A total of 1 test files matched the specified pattern.
[xUnit.net 00:00:00.36]     Trustsoft.NotifyIcon.Tests.PackagePurityTests.Readme_documents_the_install_line_usage_and_the_shipped_surface [FAIL]
  Failed Trustsoft.NotifyIcon.Tests.PackagePurityTests.Readme_documents_the_install_line_usage_and_the_shipped_surface [7 ms]
  Error Message:
   R010: the README documents the shipped public surface, but does not name [BalloonTipOptions]. Public_surface_is_only_the_documented_types pins the shipped set; this assertion is what makes the word 'documented' mean the readme the package ships, rather than a list that exists only inside the test suite.
  Stack Trace:
     at Trustsoft.NotifyIcon.Tests.PackagePurityTests.Readme_documents_the_install_line_usage_and_the_shipped_surface() in C:\Users\Maxim\YandexDisk\Projects AI\Trustsoft.NotifyIcon\.gsd-worktrees\M001\tests\Trustsoft.NotifyIcon.Tests\PackagePurityTests.cs:line 602
   at System.RuntimeMethodHandle.InvokeMethod(Object target, Void** arguments, Signature sig, Boolean isConstructor)
   at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)

Failed!  - Failed:     1, Passed:     0, Skipped:     0, Total:     1, Duration: 7 ms - Trustsoft.NotifyIcon.Tests.dll (net8.0)
  PASS  the guard failed (exit 1) and named the broken fact: does not name [BalloonTipOptions]
  PASS  the README is restored byte-for-byte (e87308ab2af00b805def26e8fa2cae1c6570fff2215a974de6a08bf2cc366820)

================================================================
SUMMARY  0 control failure(s)
README.md sha256 at the end: e87308ab2af00b805def26e8fa2cae1c6570fff2215a974de6a08bf2cc366820
README.md sha256 at the start: e87308ab2af00b805def26e8fa2cae1c6570fff2215a974de6a08bf2cc366820
