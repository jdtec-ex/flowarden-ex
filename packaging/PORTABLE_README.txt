Flowarden portable package
=========================

This archive is a full portable bundle:

  - flowarden / flowarden.exe     Rust core + CLI (also launched by the UI)
  - Flowarden.Ui / Flowarden.Ui.exe   Avalonia desktop UI (self-contained .NET)
  - README.txt                    this file

Supported platforms (separate archives per OS):
  Linux x64, macOS Apple Silicon (arm64), Windows x64

Quick start
-----------

1) Windows: install Npcap from https://npcap.com/ (enable WinPcap API mode if asked).
   Linux: install libpcap (e.g. libpcap0.8 / libpcap) if not already present.
   macOS: system libpcap is used; grant capture permission when the OS prompts.

2) Unzip / untar this folder anywhere.

3) Start the desktop UI from this folder:
     Windows:  double-click Flowarden.Ui.exe
     Linux:    ./Flowarden.Ui
     macOS:    ./Flowarden.Ui

   The UI looks for flowarden next to itself (same directory). Keep both files
   in this folder; do not move only the UI binary.

4) Optional CLI examples (from this folder):
     ./flowarden devices
     ./flowarden capture --device <iface> --duration 5
     ./flowarden core --bind 127.0.0.1:39091

Notes
-----

- This is a Public Beta build.
- Live capture needs OS privileges and a working pcap/Npcap stack.
- GeoLite2 country/ASN databases are embedded in the core binary (MaxMind terms apply).
- Project: https://github.com/jdtec-ex/flowarden-ex
- Releases: https://github.com/jdtec-ex/flowarden-ex/releases
