# Connect Phone: wireless adb for Unity Build And Run

Auto-connects the Samsung S23+ to Unity over Android **Wireless debugging** (TLS-paired), even though its IP and port keep changing.

## Constraints (user decisions, don't undo)
- **No `adb tcpip 5555`.** Some phone apps flag an open legacy adb port as tampering.
- **Don't change Unity's Android SDK path** (compatibility risk). Use Unity's bundled adb.
- mDNS discovery (`adb mdns services`) returns nothing on the user's networks (corporate Wi-Fi filters multicast). It's tried first but can't be relied on.

## Files
| Path | Role |
|---|---|
| `Tools/Connect-Phone.ps1` | Finds and connects the phone and remembers its IP and port. |
| `Assets/Aquatica/Editor/PhoneConnector.cs` | Unity hook that runs the script. |
| `%LOCALAPPDATA%\adb-phone-ips.json` | Cache: `{ "<network name>": { Ip, Port, Updated } }` |
| `Desktop\Connect Phone.lnk` | Runs the script interactively: `powershell -NoProfile -ExecutionPolicy Bypass -Command "& '<script>'; Read-Host ..."` |

## Connect-Phone.ps1
Parameters: `-Ip <addr>` overrides the IP; `-Adb <path>` sets the adb to use; `-NonInteractive` means never prompt.

- **Default adb:** Unity's bundled adb for the version in `ProjectSettings/ProjectVersion.txt`, at `C:\Program Files\Unity\Hub\Editor\<ver>\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe`. Different adb versions kill each other's server and drop the connection. The PATH adb (Android Studio 37.x) is not the same version as Unity's (36.x), so don't mix them.
- **Network name:** the Wi-Fi SSID (`netsh wlan show interfaces`). On Ethernet or without Wi-Fi, it falls back to `Get-NetConnectionProfile` `.Name`.

Order of attempts (exit 0 on success):
1. **An `IP:port` device is already in `adb devices`:** save it and exit. Any other device (USB, mDNS-named): exit without saving.
2. **mDNS** `_adb-tls-connect`: connect and save.
3. **IP:** `-Ip`, else the cached IP for this network, else ask. With `-NonInteractive` and nothing cached, exit 2.
4. **Cached port:** try it. It's still valid unless Wireless debugging was toggled.
5. **Port scan:** parallel TCP scan of ports 30000–65535 (batches of 4000, 700 ms timeout), then `adb connect` on each open port until one becomes `device`. Save it.
6. **Cached IP failed:** ask for a new IP once (interactive only) and scan again.

Exit codes: 1 = not found, 2 = no IP for this network in non-interactive mode, 3 = adb not found.

## PhoneConnector.cs (`#if UNITY_EDITOR_WIN`, namespace `Aquatica.EditorTools`)
- **When it runs:** once per editor session on load (`[InitializeOnLoad]` + `SessionState`, quiet, in the background); from the **Aquatica → Connect Phone** menu; and before an Android build with `AutoRunPlayer`, i.e. Build And Run (`IPreprocessBuildWithReport`, synchronous, 60 s timeout, progress bar).
- **Which adb:** `-Adb` is set from `AndroidExternalToolsSettings.sdkRootPath`, which is Unity's bundled SDK.
- **On success:** it finds `IP:port<tab>device` in the output and sets the internal `EditorUserBuildSettings.androidDeviceSocketAddress` via reflection. That's the IP:port box under Build Profiles → Android → Run Device, which Unity `adb connect`s to when it refreshes the device list. Without this, Unity keeps retrying the stale address. If a Unity upgrade renames the property, the update is skipped silently.
- **Run Device setting:** should be **Default device**. A wireless device's serial includes the port, so selecting the phone by name breaks once the port changes.

## Known issues and fixes so far
- **Empty network name on Ethernet:** the script only read the Wi-Fi SSID, so cache writes failed with null-index errors and Unity found no saved IP. Fixed with the profile-name fallback.
- **Already-connected device not saved:** the script used to exit early without saving. Fixed in step 1 above.
- **Scan finds no open ports but the phone answers ping:** Wireless debugging is off. Android turns it off by itself (Wi-Fi change, timeout). Re-enable it on the phone.
- **On a new network:** run the desktop shortcut once to enter the IP. Unity can't prompt.
- **Unity's console shows no result from the menu:** the `ContinueWith` callback runs on the main thread, and Unity throttles that while its window is unfocused.

## Debugging
```powershell
$adb = "C:\Program Files\Unity\Hub\Editor\6000.3.10f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe"
& $adb devices; & $adb mdns services
powershell -NoProfile -ExecutionPolicy Bypass -File Tools\Connect-Phone.ps1 -NonInteractive -Adb $adb; $LASTEXITCODE
Get-Content "$env:LOCALAPPDATA\adb-phone-ips.json"
```
Future option: ADB Wi-Fi 2.0 (Platform-Tools 37+ and Android 17) reconnects across trusted networks natively and could replace this tool.
