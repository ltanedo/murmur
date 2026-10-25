# Murmur for Windows (tray app)

Scaffold created on macOS from `docs/SPEC-windows-tray-app.md`; it has **not** been
compiled yet. First task on a Windows machine:

```powershell
cd windows
dotnet build Murmur.sln -c Release
```

then fix anything the compiler finds, run the app, and work through the acceptance
list in the spec (§4). Every file notes what is implemented and what is a TODO.
