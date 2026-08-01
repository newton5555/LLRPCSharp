# LlrpCli Agent Guide

This file provides development guidelines for agents working inside `src/LlrpCli/`.

## CLI Framework Guidelines

- **Respect Spectre.Console Capabilities**: `LlrpCli` is built on `Spectre.Console` and `Spectre.Console.Cli`. Do not introduce heavy TUI Framework dependencies (such as `Terminal.Gui`); maximize native Spectre.Console capabilities.
