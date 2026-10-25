# winget manifest

After the first public Windows release, generate the three manifest files with
`wingetcreate new https://github.com/ltanedo/murmur/releases/download/v<version>/MurmurSetup-<version>.exe`
(identifier `ltanedo.Murmur`, per-user Inno installer, silent switches `/VERYSILENT /NORESTART`),
validate with `winget validate`, and open the PR to `microsoft/winget-pkgs`. Until that
PR is merged, the README documents the installer, Scoop and PowerShell routes only.
