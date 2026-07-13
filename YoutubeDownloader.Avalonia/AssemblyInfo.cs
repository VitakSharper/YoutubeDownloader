using System.Runtime.Versioning;

// This is a Windows-only desktop app (WinExe, app.manifest, ships win-x64; it launches
// explorer.exe and reads the registry). Declaring the whole assembly Windows-only lets the
// platform-compatibility analyzer (CA1416) know the Windows-only APIs are used honestly.
[assembly: SupportedOSPlatform("windows")]
