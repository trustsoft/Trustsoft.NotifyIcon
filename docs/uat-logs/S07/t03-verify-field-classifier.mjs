// S07/T03 - classify the persisted Verify string with the gate's own code.
//
// The host verification gate rejected T03's Verify field with
// discoverySource "task-plan-unsafe": it names commands, but none are
// shell-safe, and the gate deliberately does not fall back to project-wide
// checks in that case (#1922). This script imports the shipped classifier from
// the installed gsd-pi build and runs it over
//   (a) the Verify line exactly as it is persisted for T03, and
//   (b) the proposed newline-separated replacement,
// so the remediation is checked against the gate's own rules rather than
// against a reading of them.
//
// Usage (from the repository root): node docs/uat-logs/S07/t03-verify-field-classifier.mjs

import { readFileSync } from "node:fs";

const GATE = "file:///C:/Users/Maxim/AppData/Roaming/npm/node_modules/@opengsd/gsd-pi/dist/resources/extensions/gsd/verification-gate.js";
const { discoverCommands, validateVerificationCommand, splitUnquotedLines } = await import(GATE);

const PLAN = ".gsd/phases/01-core-tray-icon-for-wpf-without-winforms/01-07-PLAN.md";
const persisted = readFileSync(PLAN, "utf-8")
  .split(/\r?\n/)
  .find((line) => line.includes("Verify:") && line.includes("consumer-proof"))
  .replace(/^\s*-\s+Verify:\s*/, "");

const proposed = [
  "dotnet pack src/Trustsoft.NotifyIcon/Trustsoft.NotifyIcon.csproj -c Release",
  "dotnet build samples/consumer-proof -c Release -f net8.0-windows",
  "dotnet build samples/consumer-proof -c Release -f net9.0-windows",
  "dotnet build samples/consumer-proof -c Release -f net10.0-windows",
  "dotnet run --project scripts/probe-live -c Release --no-build -- samples/consumer-proof/bin/Release/net8.0-windows/ConsumerProof.exe 20",
].join("\n");

function report(label, verify) {
  const text = verify instanceof Array ? verify.join("\n") : verify;
  console.log(`## ${label}`);
  console.log(`verify text (${text.split("\n").length} line(s)):`);
  for (const line of text.split("\n")) console.log(`  | ${line}`);
  const discovered = discoverCommands({ cwd: process.cwd(), taskPlanVerify: text });
  console.log(`  discoverySource: ${discovered.source}`);
  console.log(`  commands the gate would run (${discovered.commands.length}):`);
  for (const cmd of discovered.commands) console.log(`    - ${cmd}`);
  console.log("  per-candidate validation:");
  for (const candidate of splitUnquotedLines(text)) {
    const validation = validateVerificationCommand(candidate.trim());
    console.log(`    ${validation.ok ? "SAFE  " : "UNSAFE"} ${candidate.trim()}`);
    if (!validation.ok) console.log(`           reason: ${validation.reason}`);
  }
  console.log("");
}

report("(a) persisted Verify field for M001/S07/T03", persisted);
report("(b) proposed replacement, newline-separated", proposed);
