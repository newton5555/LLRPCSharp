# LLRP Reader Studio

This is the first WPF application built on the high-level `LlrpSdk` API. It is an
application-layer reference, not another protocol implementation.

Implemented in this baseline:

- manual LLRP reader profiles and multiple simultaneous reader sessions;
- Impinj extension registration for profiles by default;
- aggregated inventory observations across connected readers;
- exact-EPC Gen2 memory read and write;
- device settings query, SDK default settings, draft apply, and explicit inventory start;
- application-side Tags of Interest;
- standard GPO output diagnostics.

The first release deliberately excludes mDNS discovery, RShell, RDD/FDD capture,
IoT-device management, and xArray/xSpan spatial Location/Direction showcases.

Run on Windows:

```powershell
dotnet run --project src/LlrpReaderStudio/LlrpReaderStudio.csproj
```
