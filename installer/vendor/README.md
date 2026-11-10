# Installer vendor files

The installer bundles the Microsoft Edge WebView2 runtime so the app works offline.

- Runtime: Microsoft Edge WebView2 **Fixed Version 157.0.4341.46, x64**
- Download: https://developer.microsoft.com/en-us/microsoft-edge/webview2/ (choose Fixed Version, after accepting Microsoft's licence)
- Unpack so that `msedgewebview2.exe` sits directly in `installer/vendor/WebView2Runtime/`.

This folder is git-ignored; do not commit the runtime binaries.
