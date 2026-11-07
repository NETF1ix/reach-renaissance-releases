# Linux and Steam Deck

How to set up and play Reach Renaissance on Linux and on a Steam Deck. This is the full guide. The front page
of the releases repo has a short version
([Linux and Steam Deck](https://github.com/NETF1ix/reach-renaissance-releases#linux-and-steam-deck)), and
`README.txt` in the game's folder has the main steps and the usual fixes.

Reach Renaissance is a Windows program. On Linux it runs through Proton, Steam's Windows compatibility layer,
as a non-Steam game in your Steam library. Linux support is still experimental.

What is known so far:

- **Works:** a test build on a Steam Deck with Proton Experimental reached the menus, the Armory, the server
  list and a Workshop download. It has run on a Linux PC too.
- **Not confirmed yet:** Gaming Mode on the Deck, a whole online match from a Deck, voice chat, and an update
  installed on a Deck. Please report what happens (see [Logs and bug reports](#logs-and-bug-reports)).

Two marks in this guide:

- **(not confirmed yet)**: nobody has checked it on a real Deck or Linux PC yet.
- **(from 0.1.36)**: it came with 0.1.36, the release that brought this Linux work. On an older version the
  older way, given next to it, still applies.

Contents:

1. [What you need](#what-you-need)
2. [Download](#download)
3. [Extract](#extract)
4. [Add it to Steam](#add-it-to-steam)
5. [Force Proton Experimental](#force-proton-experimental)
6. [Steam from Flathub (Flatpak)](#steam-from-flathub-flatpak)
7. [First start](#first-start)
8. [Steam Deck: Desktop Mode, then Gaming Mode](#steam-deck-desktop-mode-then-gaming-mode)
9. [Controls and typing](#controls-and-typing)
10. [MCC on an SD card or another drive](#mcc-on-an-sd-card-or-another-drive)
11. [Hosting and firewalls](#hosting-and-firewalls)
12. [Updating, and keeping your name and armor](#updating-and-keeping-your-name-and-armor)
13. [Logs and bug reports](#logs-and-bug-reports)
14. [Troubleshooting](#troubleshooting)

## What you need

- **Halo: The Master Chief Collection on Steam, with Halo: Reach installed and up to date.** Install it in
  Steam as usual (Steam runs MCC with Proton). Reach Renaissance uses MCC's files and changes nothing in them.
  MCC doesn't need to be open. The Microsoft Store / Game Pass version isn't supported.
- **Proton Experimental.** It's what the Deck test used. Steam installs it when you pick it (step 5). Other
  versions (Proton 10, GE-Proton, proton-cachyos) haven't been tested. If something doesn't work on one of
  them, switch to Proton Experimental first.
- **A Vulkan driver.** Proton turns the game's Direct3D 11 into Vulkan (DXVK). The Steam Deck has one built in.
  On a Linux PC, install your distribution's Vulkan driver: Mesa's for AMD and Intel (`mesa-vulkan-drivers` on
  Ubuntu, Debian and Fedora; `vulkan-radeon` or `vulkan-intel` on Arch), or NVIDIA's own driver. Check it in a
  terminal:
  ```
  vulkaninfo --summary
  ```
  It should list your graphics card, with an `apiVersion` of 1.3 or newer: Proton's DXVK needs Vulkan 1.3.
  (`vulkaninfo` is in the `vulkan-tools` package.) If it lists only `llvmpipe`, that's a slow software
  driver, not your card's: install the driver for your card.
- **The internet** for the first start (WebView2 is downloaded from Microsoft) and for online play.
- **Room in your home folder:** the zip is about 700 MB, and the folder it makes is bigger.
- Everyone you play with on the same Reach Renaissance version.

Steam's Proton is the way that's tested. Lutris, Heroic, Bottles and plain Wine haven't been tested. If you
use one, it needs a runner with DXVK (Proton-GE or Wine-GE): a distribution's plain Wine draws with its own
Direct3D, and the game may not start.

## Download

From the [release page](https://github.com/NETF1ix/reach-renaissance-releases/releases/latest), download
`ReachRenaissance-v<version>.zip` into `~/Downloads`. Your browser may ask where to save it.

Optional: download `SHA256SUMS.txt` too, and check the zip in a terminal (Konsole on the Steam Deck):

```
cd ~/Downloads && sha256sum -c --ignore-missing SHA256SUMS.txt
```

It should say `OK` after the zip's name.

## Extract

Put the game somewhere in your home folder that you own, such as `~/Games`. Not inside MCC's folder, and
never with `sudo`. A Windows (NTFS) drive is a bad place too: Linux mounts it read-only while Windows is
hibernated or has Fast Startup on, and then updates can't install.

**From 0.1.36**, any file manager can extract it. In Dolphin (the Steam Deck's file manager, in
Desktop Mode), right-click the zip > **Extract** > **Extract archive here**. That makes a `ReachRenaissance`
folder. Move it into `~/Games` (make that folder if you don't have it).

**Older zips** (0.1.34 and earlier, and any later zip that gives the names below) store their folders in a
Windows way that Dolphin, Ark and most archive apps can't read. You get files with names like `ReachRenaissance\bin\host-probe.exe` instead of folders, and
then Play does nothing. If you see such names, delete those files and extract from a terminal instead. This
takes the newest Reach Renaissance zip in `~/Downloads`:

```
mkdir -p ~/Games && bsdtar -C ~/Games -xf "$(ls -t ~/Downloads/ReachRenaissance-v*.zip | head -1)"
```

SteamOS and Arch have `bsdtar`. If yours doesn't (some Ubuntu, Linux Mint, Debian or Fedora installs), use
`unzip`, then make the files yours to change (`-o` replaces the old files without asking, for an update over
the same folder):

```
mkdir -p ~/Games && unzip -qo "$(ls -t ~/Downloads/ReachRenaissance-v*.zip | head -1)" -d ~/Games
chmod -R u+rwX ~/Games/ReachRenaissance
```

(`unzip` warns that the zip "appears to use backslashes as path separators". That's expected: it still makes
the right folders.)

Either way, check that `~/Games/ReachRenaissance` holds the folders `bin`, `data` and `themes` and the file
`Reach Renaissance.exe`. Keep the .exe in that folder: it won't start on its own.

If you extracted with `sudo` by mistake, give the folder back to yourself:

```
sudo chown -R "$USER" ~/Games/ReachRenaissance
```

## Add it to Steam

On a Steam Deck, do this in Desktop Mode: Gaming Mode can't add non-Steam games.

1. In Steam: **Games > Add a Non-Steam Game to My Library > Browse**.
2. Go to `~/Games/ReachRenaissance`. If `Reach Renaissance.exe` isn't listed, set the file type at the bottom
   to **All Files**.
3. Pick `Reach Renaissance.exe`, then **Add Selected Programs**.

Add the .exe inside the `ReachRenaissance` folder, not a copy of it.

On SteamOS, Dolphin may also offer right-click > **Add to Steam** on the .exe (not confirmed yet for .exe
files). It adds the same shortcut. You still pick Proton by hand (next step).

## Force Proton Experimental

1. In your Steam library, right-click **Reach Renaissance** > **Properties** > **Compatibility**.
2. Tick **Force the use of a specific Steam Play compatibility tool**.
3. Pick **Proton Experimental**.

Without this, Steam tries to start the .exe as a Linux program, and nothing happens when you press Play.

Use this non-Steam shortcut to play. Don't start Reach Renaissance through MCC's own Steam entry (MCC's Launch
Options): Proton applies MCC's own fixes to that entry, and one of them may stop the menus from drawing (not
confirmed yet).

Leave Proton's Wayland mode off: don't add `PROTON_ENABLE_WAYLAND=1` to the Launch Options (and don't use a
Proton build that turns it on by default). The menus are a second window over the game's, and Wine's Wayland
driver can't keep them there. Don't add `PROTON_USE_WINED3D=1` either: it swaps DXVK for an older, slower way
of drawing that hasn't been tested. It's only a last try for a graphics card that has no Vulkan 1.3 driver at
all.

## Steam from Flathub (Flatpak)

Skip this on a Steam Deck. It's for a Linux PC whose Steam came from Flathub (the Software or Discover app, or
`flatpak install`). To check: `flatpak list | grep com.valvesoftware.Steam` prints a line if yours is.

Flatpak Steam can only see some folders: its own, `/mnt`, `/media` and `/run/media`. It can't see `~/Games`, so
the shortcut can't start the game. Do one of these **before** you add the shortcut:

- **Let Steam see your games folder.** Close Steam, then run:
  ```
  flatpak override --user --filesystem=~/Games com.valvesoftware.Steam
  ```
  If you keep the game somewhere else, put that folder in place of `~/Games`. Give it just that folder, not
  your whole home folder: with your whole home folder Steam may look for its own files in the wrong place.
- **Or put the game where Flatpak Steam already looks**, such as `~/.var/app/com.valvesoftware.Steam/Games`.
  Steam's Browse shows that folder as `~/Games`.

If you added the shortcut before doing this, remove it (right-click > **Manage** > **Remove non-Steam game
from your library**), then add it again and force Proton again. **From 0.1.36**, a shortcut that
Flatpak Steam cut off from its folder says so and gives the command.

With Flatpak Steam, Steam's folder is `~/.var/app/com.valvesoftware.Steam/.local/share/Steam`. Use that in
place of `~/.local/share/Steam` in the paths below. A Steam library on another drive that isn't under `/mnt`,
`/media` or `/run/media` needs its own `flatpak override --user --filesystem=<that folder>
com.valvesoftware.Steam` too.

## First start

Press **Play**. The first start takes a while:

1. Proton sets up the shortcut's own Windows folder (its "prefix"). This takes a minute or so.
2. Reach Renaissance offers to install **Microsoft Edge WebView2 Runtime**, which the menus need. Answer
   **Yes**. It downloads from Microsoft and takes a few minutes. Under Proton it can take up to 15 minutes; the
   bar can sit near the end for a while.
3. The menus open and ask for your name.

On a Steam Deck these boxes need a tap on the screen or the trackpad: the controller can't answer them.

If the WebView2 install fails (no internet, or a network that blocks Microsoft's servers):

- **From 0.1.36:** in your browser, download **Evergreen Standalone Installer, x64** from Microsoft's
  [WebView2 page](https://developer.microsoft.com/microsoft-edge/webview2/), put the file in your
  `ReachRenaissance` folder, and press Play again. It offers to install from that file.
- **Until then:** run that same file inside this shortcut's prefix with Protontricks (on a Deck, install it
  from Discover in Desktop Mode): pick the Reach Renaissance shortcut in Protontricks, then **Run an arbitrary
  executable** and pick the file.

Some things look different on Linux, and that's expected, not a broken install:

- The menus have a plain teal background instead of the live Reach scene behind them.
- The intro video doesn't play.
- In a match, the pause menu, the Tab scoreboard, the chat box and the emote wheel cover the whole screen while
  they're open.

On a Linux desktop, an X11 (Xorg) session is the safer choice if a Wayland session gives trouble with the
keyboard or the menus' window. Cinnamon's Wayland session (Linux Mint) is still experimental.

## Steam Deck: Desktop Mode, then Gaming Mode

1. Do the whole setup in **Desktop Mode** (the STEAM button > Power > Switch to Desktop): extract, add the shortcut,
   force Proton Experimental.
2. Play it once in Desktop Mode, so WebView2 installs and you pick your name. Quit.
3. Switch back to **Gaming Mode**. Reach Renaissance is in your library under **Non-Steam**. Play it with the
   same shortcut. It shouldn't ask about WebView2 again.

Gaming Mode hasn't been confirmed yet: it shows one window at a time for most games, and the menus are a second
window over the game's. If Gaming Mode shows only the game or only the menus, play in Desktop Mode for now,
and please report it with your SteamOS version (Settings > System).

**Screen.** In **SETTINGS > Video**, set **WINDOW MODE** to **Borderless Fullscreen**: the game then fills the
Deck's 1280x800 screen. The default window is 1280x720, with black bars, and in Desktop Mode it runs past the
edge of the screen. **From 0.1.36**, the game starts borderless on a Steam Deck by itself (until you
pick a WINDOW MODE), and on a PC its first window fits the screen.

**Frame rate and battery.** Set **FRAMERATE LIMIT** to 60 FPS, or use Steam's own frame limit in the Quick
Access menu (the ... button, then Performance) for 40 FPS and longer battery. If matches drop frames, set
**GRAPHICS QUALITY** to **Original**. Leave **V-SYNC** off (the default): with it on under Proton, the frame
rate halves to 30 whenever a frame runs long.

The first minute on a new map may stutter while the graphics driver builds its shaders. Non-Steam shortcuts
don't get Steam's shader downloads. It should be smooth after that, and on later loads.

## Controls and typing

The Deck's controls work as an Xbox controller, in the menus and in matches.

- **Menus:** the D-pad or left stick moves, A selects, B goes back, X and Y do what the hints say, LB and RB
  switch tabs, Menu opens the Start menu (the game menu in a match), View opens Friends.
- **In a match:** Menu opens the game menu, View holds the scoreboard, D-pad Down opens the emote wheel.
- Touch and the trackpads work as a mouse in the menus.

**Typing.** The first-run name screen has its own keyboard: A on Name, then A types, B deletes, X adds a space
and Menu is done. For any other text box (a new name, a friend code, Forge map names, search, chat) press
**STEAM + X** to open Steam's keyboard. **From 0.1.36**, A on a text box opens the menus' own keyboard
too, and chat boxes show the STEAM + X hint.

**Steam Input.** Use the **Gamepad** template (or **Gamepad with Mouse Trackpad**). In a match, any mouse
movement (the right trackpad, gyro as a mouse, a touch on the screen) switches the game to keyboard and mouse
until you press a controller button, so keep your thumb off the right trackpad while you move (not confirmed
yet). For gyro aiming, set the gyro to **Joystick Camera**, not to a mouse.

**Other controllers.** The menus read any Xbox-style controller. Matches only read Xbox (XInput) controllers.
A PlayStation or Switch controller needs Steam Input on for this shortcut (Properties > Controller), which
makes it look like an Xbox controller.

**Voice chat.** Push-to-talk is V (your team) and B (everyone) on a keyboard (**SETTINGS > Keyboard** changes
them). A controller has no talk button of its own: in Steam Input, map a back button to V, or set **SETTINGS >
Audio > MICROPHONE MODE** to **Open Mic**. The microphone is the system's default (PipeWire or PulseAudio); pick
another in **SETTINGS > Audio**. Voice hasn't been tested on Linux or the Deck yet.

## MCC on an SD card or another drive

Usually there's nothing to do. Reach Renaissance reads the Steam libraries Steam lists, the SD card's too, and
finds MCC there by itself (not confirmed yet with MCC on a real SD card).

If it says it couldn't find MCC, it asks you to pick MCC's folder. That window shows Windows-style drives:
your Linux folders are under **Z:**. So `~/.local/share/Steam` is `Z:\home\<you>\.local\share\Steam`, and an
SD card is under `Z:\run\media\...`. Hidden folders such as `.local` are shown there. Pick the folder named
`Halo The Master Chief Collection` (it's in `steamapps/common` of the library MCC is in). In Steam, right-click
MCC > **Manage** > **Browse local files** shows where it is. Reach Renaissance remembers the folder you pick,
updates included.

Instead of picking, you can give the folder in the shortcut's **Properties > Launch Options**, with the whole
path in quotes, for example:

```
%command% "/run/media/deck/<card>/steamapps/common/Halo The Master Chief Collection"
```

Only do this when it's needed. If you later move MCC (Steam's **Move install folder**, or a reinstall to
another drive), clear those Launch Options again. **From 0.1.36**, a Launch Options folder that has
no MCC is skipped and MCC is looked for by itself.

**A library on a drive mounted somewhere unusual** (such as `/data` or `/games`): Proton runs the shortcut in
a container that doesn't see every folder, so MCC may not be found even though Steam runs it fine. If the drive
is mounted, add that library's folder to the Launch Options:

```
STEAM_COMPAT_MOUNTS=/data/SteamLibrary %command%
```

Put your library's folder in place of `/data/SteamLibrary` (several folders are joined with `:`). If you
already have `%command% "..."` there, keep one `%command%`: `STEAM_COMPAT_MOUNTS=/data/SteamLibrary %command%
"..."`. **From 0.1.36**, Reach Renaissance says which folder it can't see and gives this line.

## Hosting and firewalls

**Joining** needs no setup in the usual case: pick a game in Matchmaking's server list and join.

**Hosting** needs other players to reach your PC. On Windows, SETTINGS > Network sets up Windows Firewall. On
Linux, your system's own firewall applies, which Reach Renaissance can't check or change.

First, see if a firewall is on:

- **firewalld** (SteamOS, Fedora, openSUSE): `systemctl is-active firewalld` says `active` when it's on.
  SteamOS has firewalld off unless you turned it on, as far as players have reported, but check anyway.
  Fedora Workstation's default firewall setup already lets these ports in.
- **ufw** (Ubuntu, Linux Mint and others): `sudo ufw status` says `Status: active` when it's on. It's off by
  default on Ubuntu.

If one is on, allow TCP and UDP 40011-40013 (hosting) and TCP and UDP 11700-11710 (games on your home
network). On a Steam Deck, `sudo` needs a password first: set one with `passwd` in Konsole.

firewalld:

```
sudo firewall-cmd --permanent --add-port=40011-40013/tcp --add-port=40011-40013/udp --add-port=11700-11710/tcp --add-port=11700-11710/udp && sudo firewall-cmd --reload
```

This uses firewalld's default zone. If `firewall-cmd --get-active-zones` puts your network in another zone,
add `--zone=<that zone>` after `--permanent`.

ufw:

```
sudo ufw allow 40011:40013/tcp && sudo ufw allow 40011:40013/udp && sudo ufw allow 11700:11710/tcp && sudo ufw allow 11700:11710/udp
```

What the ports are for: 40011 is your game and the status page the server list checks (40012 and 40013 are for
a second copy or a dedicated server). 11700-11709 is the server list for your home network, and 11710 is where
its answers come under Proton. **From 0.1.36**, SETTINGS > Network shows these commands, and A copies
them.

**The router.** Reach Renaissance asks your router to forward its port by itself (UPnP). Before 0.1.36,
that fails on a Linux PC or Deck whose firewall is on, even with the ports allowed: forward UDP and
TCP 40011 to your PC on the router by hand, or other players join you through the slower relay. **From
0.1.36**, the router's answer comes in through the ports above (not confirmed yet with a real router).

If a join says "the NATs between you did not open", that's not a Linux problem: it's the most common join
failure everywhere. The relay is the fallback, and it needs no setup.

**Workshop mods** that a game needs download by themselves through Steam. Steam must be running and signed in
to the account that owns MCC.

## Updating, and keeping your name and armor

New versions install themselves: Reach Renaissance offers **Update and restart** when it starts, and the menus
have an **UPDATE** button. This works under Proton (checked under Wine; an update on a real Deck is not
confirmed yet). You can also extract a new zip over the same folder, with the same steps as above.

**Keep the same folder and the same Steam shortcut.** Your name, armor, player id and settings live in that
shortcut's Proton prefix, not in the game's folder:

```
~/.local/share/Steam/steamapps/compatdata/<number>/pfx/drive_c/users/steamuser/AppData/Local/ReachRenaissance
```

`<number>` is the shortcut's own. To find it:

```
ls -d ~/.local/share/Steam/steamapps/compatdata/*/pfx/drive_c/users/steamuser/AppData/Local/ReachRenaissance
```

(In Dolphin, Ctrl+H shows hidden folders such as `.local`.)

A shortcut for another folder, or a new shortcut, gets a new prefix: it asks for your name again, the armor and
settings are back to the defaults, and WebView2 installs again. If you move the folder, change **Target** and
**Start In** in the shortcut's **Properties** instead of adding it again (whether Steam then keeps the same
prefix is not confirmed yet, so back up first).

**Back up** your name, armor and settings (put your shortcut's number in place of `<number>`):

```
cd ~/.local/share/Steam/steamapps/compatdata/<number>/pfx/drive_c/users/steamuser/AppData/Local && tar -czf ~/ReachRenaissance-backup.tar.gz ReachRenaissance
```

To bring them into a new shortcut, play it once, quit, and extract the backup into its prefix:

```
tar -xzf ~/ReachRenaissance-backup.tar.gz -C ~/.local/share/Steam/steamapps/compatdata/<new number>/pfx/drive_c/users/steamuser/AppData/Local
```

Then don't play from the old shortcut and the new one at the same time: they share one player id.

**To remove it:** in Steam, right-click it > **Manage** > **Remove non-Steam game from your library**, delete
`~/Games/ReachRenaissance`, and delete `~/.local/share/Steam/steamapps/compatdata/<number>` for its prefix
(only that number: the others belong to your other games).

## Logs and bug reports

Each run's logs are in the prefix's `runs` folder (the last 10 runs):

```
~/.local/share/Steam/steamapps/compatdata/<number>/pfx/drive_c/users/steamuser/AppData/Local/ReachRenaissance/runs
```

With Steam from Flathub, start with `~/.var/app/com.valvesoftware.Steam/.local/share/Steam` instead; with Snap's
Steam, `~/snap/steam/common/.local/share/Steam`. This shows the newest run's folder:

```
ls -td ~/.local/share/Steam/steamapps/compatdata/*/pfx/drive_c/users/steamuser/AppData/Local/ReachRenaissance/runs/*/ | head -1
```

For a bug report, zip that folder (in Dolphin: right-click > **Compress**) and attach it. Internet addresses in
these logs are masked.

When a message names a Windows path: `%LOCALAPPDATA%` is the prefix's `.../AppData/Local` above, `C:\` is the
prefix's `drive_c`, and `Z:\` is your Linux root `/` (so `Z:\home\deck` is `/home/deck`). **From 0.1.36**, the messages show Linux paths.

**If the game doesn't start at all** (no menus, so maybe no run folder), set the shortcut's Launch Options to:

```
PROTON_LOG=1 %command%
```

Press Play again, and send the `steam-<number>.log` it writes in your home folder (Flatpak Steam: in
`~/.var/app/com.valvesoftware.Steam`). Reach Renaissance's own start-up messages (the WebView2 install, the MCC
search) are in there. This log isn't masked like the run folders, so it can show your Linux user name in folder
names. Take `PROTON_LOG=1` out again afterwards: the log grows each time.

Say in the report:

- Steam Deck (and the SteamOS version), or your Linux distribution and its version.
- On a Deck: Desktop Mode or Gaming Mode. On a PC: X11 or Wayland, and which desktop (KDE Plasma, GNOME...).
- The Proton version, and whether your Steam is from Flathub.
- What you did, what you expected, and what you saw.

## Troubleshooting

| Problem | What to do |
|---|---|
| **Play does nothing**, or Steam shows Running for a moment and stops | 1) Proton isn't forced: see [Force Proton Experimental](#force-proton-experimental). 2) The folder isn't whole: `~/Games/ReachRenaissance` must hold `bin`, `data` and `themes`; files named `ReachRenaissance\...` mean a flat extract, so [extract](#extract) again with the terminal line. 3) Steam from Flathub: see [Flatpak](#steam-from-flathub-flatpak). 4) Still nothing: send `steam-<number>.log` (see [Logs](#logs-and-bug-reports)). |
| **The menus' window is empty, white or black** | Check that WebView2 installed (answer Yes at the first start; see [First start](#first-start) if it failed). Use Proton Experimental and this non-Steam shortcut, not MCC's entry, with no `PROTON_ENABLE_WAYLAND`. Then try Launch Options `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS="--no-sandbox" %command%` (the switch the game's own WebView2 message names: it works around a Wine bug that Proton may still have). If that doesn't help, try `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS="--no-sandbox --disable-gpu" %command%`. A plain teal background behind the menus is normal. |
| **The keyboard does nothing in the menus**, or the game menu won't close | Alt-Tab away and back once, or click once on the menus. If a match's game menu won't close with B, tap it or use the mouse. On a Linux desktop, an X11 (Xorg) session avoids most of this. **From 0.1.36** it should happen less. |
| **The game closes right after its window appears** (no Vulkan) | Install your distribution's Vulkan driver and check it with `vulkaninfo --summary` (see [What you need](#what-you-need)). The Deck has one. **From 0.1.36**, a message names the missing driver. |
| **"Couldn't find Halo: The Master Chief Collection"** | Check that MCC and Halo: Reach are installed in Steam. Then pick MCC's folder (Linux folders are under `Z:`), or see [MCC on an SD card or another drive](#mcc-on-an-sd-card-or-another-drive) for Launch Options and `STEAM_COMPAT_MOUNTS`. |
| **Steam from Flathub**: Browse can't see the folder, or the game can't find its files | See [Steam from Flathub](#steam-from-flathub-flatpak): `flatpak override --user --filesystem=~/Games com.valvesoftware.Steam`, restart Steam, then remove the shortcut and add it again. |
| **Can't host, or nobody can join you** | Allow the ports in your firewall and forward UDP and TCP 40011 on the router: see [Hosting and firewalls](#hosting-and-firewalls). Joining others needs no setup; if a join fails, try again (the relay is the fallback). |
| **Games on your home network don't show** | With a firewall on, allow TCP and UDP 11700-11710 (11710 is where the answers come). |
| **Gaming Mode shows only one window** (only the game, or only the menus) | Play in Desktop Mode for now and report it with your SteamOS version (not confirmed yet how Gaming Mode behaves). |
| **The game is in a small window, or runs past the screen's edge** | SETTINGS > Video > WINDOW MODE: Borderless Fullscreen. |
| **"This MCC update's Halo: Reach isn't supported yet"** | MCC was updated (or isn't up to date). Wait for a new Reach Renaissance release. |
