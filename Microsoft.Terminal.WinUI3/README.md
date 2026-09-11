# Microsoft.Terminal.WinUI3 compatibility fork

This directory contains the WinUI 3 terminal-control wrapper from
EasyWindowsTerminalControl 1.0.20-beta.1 and the four supporting interface
files from Windows Terminal. It is built from source against the same Windows
App SDK 2.3.1 line as SFTP Explorer.

The published NuGet package is built against Windows App SDK 1.7. Instantiating
that binary in this Windows App SDK 2.3.1 application terminates the process with
exception `0xc000027b`. Keeping the source local prevents that binary-version
mismatch while retaining the native Microsoft Terminal renderer from
`CI.Microsoft.Terminal.Wpf`.

Upstream licenses and the Windows Terminal notice are included next to the
source files.
