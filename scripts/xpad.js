#!/usr/bin/env node
"use strict";

const { spawn } = require("child_process");
const fs = require("fs");
const path = require("path");

const repoRoot = path.resolve(__dirname, "..");
const exePath = path.join(repoRoot, "bin", "Release", "net10.0", "xpad.exe");

if (!fs.existsSync(exePath)) {
  console.error(`xpad: missing ${exePath}`);
  console.error("Build it first:  dotnet build -c Release");
  process.exit(1);
}

const child = spawn(exePath, process.argv.slice(2), {
  cwd: process.cwd(),
  stdio: "inherit",
  windowsHide: false,
});

child.on("error", (err) => {
  console.error(`xpad: failed to start ${exePath}`);
  console.error(err.message);
  process.exit(1);
});

child.on("exit", (code, signal) => {
  if (signal) {
    process.kill(process.pid, signal);
    return;
  }
  process.exit(code ?? 1);
});
