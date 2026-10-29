const { spawn } = require("node:child_process");

const child = spawn(
  "GMDiffTool.exe",
  ["modded/data.win", "vanilla/data.win", "out"],
  { stdio: "inherit" },
);

child.on("close", (code) => process.exit(code ?? 1));