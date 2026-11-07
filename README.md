# Reach Renaissance

## ⬇️ [Download the latest version](../../releases/latest)

On the release page, click the **Download** link at the top. Already playing? The game updates itself when it starts.

Halo: Reach multiplayer, Firefight and Campaign with the Xbox 360 menus, playable over the internet, without opening MCC.

A free fan project, not affiliated with Microsoft, Halo Studios, 343 Industries or Bungie. No game files are included: you need your own copy of the game.

## What you need

- Windows 10 or 11, 64-bit. Or Linux or a Steam Deck, through Steam's Proton (experimental): see [Linux and Steam Deck](#linux-and-steam-deck).
- Halo: The Master Chief Collection on **Steam**, with Halo: Reach installed and up to date. MCC doesn't need to be open, and nothing in MCC changes (anti-cheat settings included). The Microsoft Store / Game Pass version isn't supported.
- Everyone you play with on the same Reach Renaissance version.

## Download and play

1. From [Releases](../../releases/latest), download `ReachRenaissance-v<version>.zip` and `SHA256SUMS.txt`.
2. Optional: check the download. In PowerShell, `Get-FileHash ReachRenaissance-v<version>.zip` should print the hash listed in `SHA256SUMS.txt`.
3. Unzip the whole folder anywhere (not inside the MCC folder) and double-click `Reach Renaissance.exe` inside it. Keep the .exe in that folder: it won't start if you move it out on its own. For a desktop icon, right-click it and choose **Send to > Desktop (create shortcut)**. Windows may say "Windows protected your PC" because the program isn't signed: **More info**, then **Run anyway**.

New versions are offered when you start the game (**Update and restart**). Only releases signed with the project's key install.

## Linux and Steam Deck

Reach Renaissance runs through Proton, Steam's Windows compatibility layer. This is still experimental: a test build has been played on a Steam Deck, so please report what happens. On a Steam Deck, do the setup in Desktop Mode.

1. Install MCC with Halo: Reach in Steam as usual.
2. From [Releases](../../releases/latest), download `ReachRenaissance-v<version>.zip`. Optional: download `SHA256SUMS.txt` too and check the zip in a terminal: `cd ~/Downloads && sha256sum -c --ignore-missing SHA256SUMS.txt` should say `OK`.
3. Extract it somewhere in your home folder, not with sudo and not inside MCC's folder. In Dolphin (the Steam Deck's file manager), right-click the zip > **Extract** > **Extract archive here**, then move the `ReachRenaissance` folder it makes into `~/Games`. That folder holds `bin`, `data`, `themes` and `Reach Renaissance.exe`.
   If you get files with names like `ReachRenaissance\bin\host-probe.exe` instead of folders (the zips of 0.1.34 and earlier), delete them and extract from a terminal (Konsole on the Steam Deck). This takes the newest zip in Downloads:
   ```
   mkdir -p ~/Games && bsdtar -C ~/Games -xf "$(ls -t ~/Downloads/ReachRenaissance-v*.zip | head -1)"
   ```
   No `bsdtar` (some Ubuntu, Mint, Debian or Fedora installs)? Use `unzip`. It warns that the zip "appears to use backslashes as path separators": that's expected with these zips, and the folders come out right.
   ```
   mkdir -p ~/Games; unzip -qo "$(ls -t ~/Downloads/ReachRenaissance-v*.zip | head -1)" -d ~/Games; chmod -R u+rwX ~/Games/ReachRenaissance
   ```
4. In Steam: **Games > Add a Non-Steam Game to My Library > Browse**, set the file type to **All Files**, pick `Reach Renaissance.exe` in `~/Games/ReachRenaissance`, then **Add Selected Programs**.
5. Right-click it in your Steam library > **Properties > Compatibility**: tick **Force the use of a specific Steam Play compatibility tool** and pick **Proton Experimental**.
6. Press **Play**. The first start takes a while: Proton sets itself up, then the game offers to install Microsoft Edge WebView2 Runtime, which the menus need. Answer **Yes** (it needs the internet and takes a few minutes). On a Steam Deck, tap the screen or use the trackpad for these boxes.
7. On a Steam Deck, play it once in Desktop Mode, so WebView2 is installed, then try Gaming Mode with the same shortcut. Gaming Mode isn't confirmed yet: if it shows only the game or only the menus, play in Desktop Mode, and please tell us. The Deck's controls work as an Xbox controller, and **Steam + X** opens the keyboard for your name and chat.

Keep the folder where it is and keep the same Steam shortcut: your name, armor and settings are kept with that shortcut's Proton files. New versions are offered when the game starts (**Update and restart**); an update installed this way on a Steam Deck isn't confirmed yet, so if one fails, download the new zip and extract it over the old folder. If you move the folder, change **Target** and **Start In** in the shortcut's Properties rather than adding it again. The `README.txt` in the folder has more: MCC on an SD card or another drive, the firewall, Wayland.

### If Play does nothing

- **Proton isn't forced.** Check step 5. Without it, Steam tries to start the .exe as a Linux program, and nothing happens.
- **The folder isn't whole.** `~/Games/ReachRenaissance` must hold `bin`, `data` and `themes` folders. Files named `ReachRenaissance\...` mean the zip was extracted flat: extract it again with the terminal line in step 3.
- **Steam from Flathub (Flatpak)** can only see some folders. Run `flatpak override --user --filesystem=~/Games com.valvesoftware.Steam` (or put the folder somewhere your Steam can already see), restart Steam, then remove the shortcut and add it again (steps 4 and 5).
- **No Vulkan driver** (a Linux PC; the Steam Deck has one). Proton draws the game with Vulkan. Install your distribution's Vulkan driver (Mesa for AMD and Intel, NVIDIA's own driver for NVIDIA) and check it with `vulkaninfo --summary`.
- **Still nothing?** Set the shortcut's Launch Options to `PROTON_LOG=1 %command%`, press Play again, and send the `steam-<number>.log` it writes in your home folder with your bug report (Flatpak Steam may write it in `~/.var/app/com.valvesoftware.Steam` instead).

Logs are in `~/.local/share/Steam/steamapps/compatdata/<number>/pfx/drive_c/users/steamuser/AppData/Local/ReachRenaissance/runs` (Flatpak Steam: the same under `~/.var/app/com.valvesoftware.Steam/.local/share/Steam`). Zip the newest folder for a bug report.

## Playing online

- **Join:** Matchmaking's server list shows public games and games on your home network. No router setup needed.
- **Host:** Custom Games, pick a map and game type, Start Game (Network starts on Host on Server Browser). The first time, use SETTINGS > Network > Allow Reach Renaissance so Windows Firewall lets players in.
- The public server list doesn't show anyone's IP address, but like most PC games with player hosting, people in a game with you can learn your internet address.

## Known limits

- When MCC updates, Reach Renaissance stops starting until a new release supports that update.
- This is an early test build: expect bugs. Logs are in `%LOCALAPPDATA%\ReachRenaissance\runs` ([on Linux](#linux-and-steam-deck), in Steam's folder); zip the newest folder for a bug report. From 0.1.2 on, internet addresses in the logs are masked; with 0.1.1, send logs to the developers privately rather than posting them.
