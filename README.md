# 🌟 WunderBar: The Premium Windows Performance Suite

WunderBar is a state-of-the-art, high-performance system utility designed to replace and enhance the standard Windows taskbar and system monitoring experience. Built with a focus on speed, aesthetics, and deep system integration, WunderBar provides power users with the tools they need to master their workspace.

![WunderBar Logo](WunderBar.ico)

## ✨ Features

- **🚀 Ultra-Fast Search**: Instant NTFS-level file indexing and search capabilities.
- **📊 Vital Signs Monitoring**: Real-time CPU and RAM telemetry with high-frequency updates and visual charts.
- **📋 Clipboard Intelligence**: Advanced clipboard history management with quick-access snippets.
- **📂 TreeSize Integration**: Visual breakdown of disk usage to identify storage bottlenecks instantly.
- **🛠️ System Tray & Controls**: Centralized management for hidden tray icons and native system shortcuts.
- **💎 Premium Aesthetics**: A stunning glassmorphic UI featuring modern typography (Inter & Outfit) and smooth micro-animations.

## 🛠️ Technology Stack

- **Core**: C# / .NET with WinForms for deep system-level control.
- **Frontend**: Integrated WebView2 layer for a modern, responsive HTML5/CSS3 interface.
- **Services**:
  - **MFT Scanner**: Direct Master File Table access for instantaneous file operations.
  - **Metrics Engine**: Low-overhead hardware monitoring.
  - **Virtual Host Bridge**: Seamless communication between native backend and web frontend.

## 🚀 Getting Started

### Prerequisites

- Windows 10/11
- .NET Desktop Runtime (6.0 or later)
- WebView2 Runtime

### Installation

1. Clone the repository:
   ```bash
   git clone https://github.com/btrahan1/WunderBar.git
   ```
2. Open `WunderBar.sln` in Visual Studio.
3. Build and Run in **Release** mode for optimal performance.

### ⚙️ Startup Configuration (Recommended)

To enable WunderBar to start automatically with Administrative privileges (required for MFT scanning and process management) without a UAC prompt on every boot:

1. Open **PowerShell** as Administrator.
2. Run the following command (update the path to your `WunderBar.exe`):
   ```powershell
   $action = New-ScheduledTaskAction -Execute 'C:\Path\To\WunderBar.exe' -WorkingDirectory 'C:\Path\To\'
   $trigger = New-ScheduledTaskTrigger -AtLogOn
   $principal = New-ScheduledTaskPrincipal -UserId $env:USERNAME -LogonType Interactive -RunLevel Highest
   Register-ScheduledTask -TaskName "WunderBar" -Action $action -Trigger $trigger -Principal $principal -Force
   ```

### 🛡️ Antivirus Exclusions

Because WunderBar performs low-level system operations (Direct MFT access), it may be flagged by heuristic scanners. To ensure uninterrupted operation:

1. Open **Windows Security**.
2. Go to **Virus & threat protection settings** > **Manage settings**.
3. Scroll to **Exclusions** > **Add or remove exclusions**.
4. Add the folder where WunderBar is installed.

## ⚖️ License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

---

*WunderBar — Efficiency, redefined.*
