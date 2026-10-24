# OPEN FIRST: Proton test build

**A test build for Linux, Steam Deck and Mac (Wine), not a release.** Windows players: use the [latest release](https://github.com/NETF1ix/reach-renaissance-releases/releases/latest) instead.

**You need two zips:** the official `ReachRenaissance-v0.1.22.zip` from the [0.1.22 release](https://github.com/NETF1ix/reach-renaissance-releases/releases/tag/v0.1.22) (under Assets), and this test kit, `ReachRenaissance-v0.1.22-proton-kit-3a493906.zip`, from the file list above (click it, then the download button). Unpack them from a terminal as below, not with Ark or a file manager, and **don't use sudo for any of it.**

This build doesn't offer updates; when a new release comes out, wait for the next test build.

The steps (also in the kit as PROTON-TEST-README.txt):

```text
Reach Renaissance Proton test build
Built on 0.1.22, commit 3a493906, 23 Oct 2026
=============================================

Thanks for testing! This kit holds the 11 files that turn the
official Reach Renaissance 0.1.22 into a test build with the changes
for Proton on Linux and the Steam Deck, and Wine on a Mac, that aren't
in a release yet. It's a test build, not a release, so please don't
pass it on.

Fixed since the last test build, from your reports:
  - The keyboard works in the menus (your name, chat, search boxes).
  - The menus draw their own background instead of black.
  - Leaving a match goes back to the menus. Before, you could be stuck
    on the backdrop while the menu sounds still played.
  - B in a match only switches grenades. It also opened the pause
    menu before.
  - The mouse pointer shows over the menus.
  - The controller should work in the menus as soon as they appear.
    Before, it could do nothing for the first half minute.
  - Unpacking works with unzip as well as bsdtar, and nothing needs
    sudo.

Still being worked on:
  - Moving the window: the menus stay put while you drag and catch up
    when you let go.
  - Fullscreen through the desktop. Use the game's own setting
    instead: Settings > Video > WINDOW MODE > Borderless Fullscreen,
    and tell us whether that works for you.
  - The Spartan in the menus under Wine is new in this build. Tell us
    whether you see him in the Armory and on the player card, or
    only the menus' own background.

This build doesn't offer updates under Wine: official releases don't
run under Wine yet, so an update would replace it with one that won't
start. When a new release comes out, wait for the next test build.


You need
--------
  - Halo: The Master Chief Collection with Halo: Reach, installed and
    up to date in Steam. On Linux and the Deck that's the normal
    Steam (MCC runs with Proton). On a Mac it's Windows Steam in a
    Wine bottle (CrossOver, Whisky or other).
  - The official ReachRenaissance-v0.1.22.zip, under Assets on the
    release page:
    https://github.com/NETF1ix/reach-renaissance-releases/releases/tag/v0.1.22
  - This kit, ReachRenaissance-v0.1.22-proton-kit-3a493906.zip,
    from the proton-test-build page: click it in the file list, then
    the download button (Download raw file).

Keep both as .zip files in your Downloads folder. Or download both
from a terminal:
  cd ~/Downloads
  curl -LO https://github.com/NETF1ix/reach-renaissance-releases/releases/download/v0.1.22/ReachRenaissance-v0.1.22.zip
  curl -LO https://github.com/NETF1ix/reach-renaissance-releases/raw/proton-test-build/ReachRenaissance-v0.1.22-proton-kit-3a493906.zip

On a Mac, Safari unzips downloads by itself and deletes the .zip, and
its way of unzipping breaks the official zip. Before downloading,
turn off Safari > Settings > General > "Open 'safe' files after
downloading", or use another browser, or the two curl lines above.

On Linux, play it through Steam with Proton, as below. Plain Wine and
Lutris aren't supported in this test.

Linux Mint: log in to the "Cinnamon" session, not "Cinnamon on
Wayland". Mint's Wayland session is still experimental and crashed a
tester's whole desktop. On other desktops (KDE, GNOME), say which
session you used when you report back.


1. Unpack both into a new folder, from a terminal
-------------------------------------------------
Never use sudo for any of this. Files unpacked with sudo belong to
the system's admin account, and the game can't change them.

Tested an earlier test build? Delete its folder first:
  chmod -R u+rwX ~/Games/rr-proton-test; rm -rf ~/Games/rr-proton-test
If rm still says "Permission denied" (and chmod says "Operation not
permitted"), the earlier build was unpacked with sudo. This once,
remove it with sudo:
  sudo rm -rf ~/Games/rr-proton-test
Your Steam shortcut and WebView2 stay, so in step 2 just start the
shortcut. Clear its Launch Options if you set any for the earlier
build (PROTON_LOG or WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS).

Steam Deck: do everything in Desktop Mode (Steam button > Power >
Switch to Desktop). Open Konsole from the app menu at the bottom left
(System > Konsole). Steam + X shows the keyboard. To paste a line in
Konsole, press Ctrl+Shift+V (Ctrl+V doesn't paste there).

Use a terminal, not your file manager or Ark: the official zip
stores its folders in a Windows format that those apps unpack as flat
files.

Linux and Steam Deck (copy and paste each line, then press Enter):
  mkdir -p ~/Games/rr-proton-test
  unzip -oq ~/Downloads/ReachRenaissance-v0.1.22.zip -d ~/Games/rr-proton-test
  chmod -R u+rwX ~/Games/rr-proton-test
  unzip -oq ~/Downloads/ReachRenaissance-v0.1.22-proton-kit-3a493906.zip -d ~/Games/rr-proton-test
  cd ~/Games/rr-proton-test && sha256sum --quiet -c ReachRenaissance-v0.1.22-proton-kit-3a493906-SHA256SUMS.txt

If the mkdir line says "Permission denied", ~/Games itself was made
with sudo. Run sudo chown "$USER" ~/Games, then start again from the
mkdir line.

unzip warns that the official zip "appears to use backslashes as path
separators". That's expected. The chmod line matters: without it,
unzip stops with "Permission denied".

If unzip says "command not found", use these two lines in place of
the two unzip lines and the chmod line:
  bsdtar -C ~/Games/rr-proton-test -xf ~/Downloads/ReachRenaissance-v0.1.22.zip
  bsdtar -C ~/Games/rr-proton-test -xf ~/Downloads/ReachRenaissance-v0.1.22-proton-kit-3a493906.zip

If unzip or bsdtar says it can't find or open a zip, run
  ls -l ~/Downloads/*.zip
The official zip is 444067001 bytes and the kit about 9666000. A
different name: a second download is often named "... (1).zip";
delete the extra copies and use the right name. A different size: the
download broke, or a web page was saved instead of the zip. Delete it
and download it again, for example with the curl lines above.

The last line prints nothing when all 778 files match this build. If
it prints anything, delete ~/Games/rr-proton-test, download both zips
again and redo this step. If it still prints something, send what it
printed.

Mac (Terminal):
  mkdir -p ~/Games/rr-proton-test
  unzip -oq ~/Downloads/ReachRenaissance-v0.1.22.zip -d ~/Games/rr-proton-test
  chmod -R u+rwX ~/Games/rr-proton-test
  unzip -oq ~/Downloads/ReachRenaissance-v0.1.22-proton-kit-3a493906.zip -d ~/Games/rr-proton-test
  cd ~/Games/rr-proton-test && shasum -a 256 -c ReachRenaissance-v0.1.22-proton-kit-3a493906-SHA256SUMS.txt | grep -v ': OK$'
macOS may ask whether Terminal can access your Downloads folder: click
OK. unzip's warning about backslashes is expected here too.

The game is now in ~/Games/rr-proton-test/ReachRenaissance. Follow
this file, not the Linux and Steam Deck section of the README.txt in
that folder: that one is for a future release.


2. Start it
-----------
Linux and Steam Deck, the first time:
  1. In Steam: Games > Add a Non-Steam Game to My Library > Browse.
     Set the file type to All Files, pick "Reach Renaissance.exe" in
     ~/Games/rr-proton-test/ReachRenaissance, then Add Selected
     Programs.
  2. Right-click it > Properties. Rename it "Reach Renaissance TEST"
     so you can tell it apart. Under Compatibility, tick "Force the
     use of a specific Steam Play compatibility tool" and pick Proton
     Experimental (or Proton 10 or newer).
  3. Play it. On a Steam Deck, play it from Desktop Mode first, then
     try Gaming Mode. The Deck's controls work as an Xbox controller;
     Steam + X opens the keyboard.
If MCC isn't found (MCC on an SD card, for example), pick its folder
when asked: Linux folders are under Z:, SD cards under Z:\run\media.
Steam from Flathub (Flatpak) can't see ~/Games until you allow it
once, before adding the game:
  flatpak override --user --filesystem=~/Games/rr-proton-test com.valvesoftware.Steam
then quit Steam and start it again.

Mac: use the bottle that has Windows Steam with MCC, and the Wine that
came with that bottle. Reach Renaissance finds MCC through that Steam.
Don't run a different Wine (such as Homebrew's) on a CrossOver or
Whisky bottle: it upgrades the bottle and can break it. These commands
also save the launcher's messages to rr-launcher.txt on your Desktop.

  - CrossOver:
      CX=/Applications/CrossOver.app/Contents/SharedSupport/CrossOver/bin/wine
      cd ~/Games/rr-proton-test/ReachRenaissance
      "$CX" --bottle "<bottle name>" "$PWD/Reach Renaissance.exe" 2>&1 | tee ~/Desktop/rr-launcher.txt
  - Whisky: Whisky's Wine is usually
    ~/Library/Application Support/com.isaacmarovitz.Whisky/Libraries/Wine/bin/wine64
    and its bottles are folders in
    ~/Library/Containers/com.isaacmarovitz.Whisky/Bottles.
      W="$HOME/Library/Application Support/com.isaacmarovitz.Whisky/Libraries/Wine/bin/wine64"
      cd ~/Games/rr-proton-test/ReachRenaissance
      WINEPREFIX="<bottle folder>" "$W" "Reach Renaissance.exe" 2>&1 | tee ~/Desktop/rr-launcher.txt
  - Game Porting Toolkit, for a bottle made with it:
      cd ~/Games/rr-proton-test/ReachRenaissance
      WINEPREFIX="<bottle folder>" wine64 "Reach Renaissance.exe" 2>&1 | tee ~/Desktop/rr-launcher.txt

If it says "Reach Renaissance doesn't support Linux/Proton yet", you
started an official release, not this build. Start it from
~/Games/rr-proton-test. Its VERSION.txt says "Proton test build".


3. Install WebView2 when asked
------------------------------
The first time, the launcher offers to install Microsoft Edge WebView2
Runtime: answer Yes. The menus need it. The install takes a few
minutes and needs the internet. Then play as usual.

This changes the Proton prefix (or the Mac bottle) for good: WebView2
and Edge Update are installed, and WebView2 is set to run as Windows
7 there. On a Mac, if other programs in that bottle use WebView2,
test in a copy of the bottle.


What to send back
-----------------
Reply to whoever sent you this build with:
  - Your setup: Linux distribution (or SteamOS version), desktop (KDE,
    GNOME, Cinnamon...) and session (X11 or Wayland), graphics card,
    and the Proton version. On a Mac: Intel or Apple Silicon, macOS
    version, and your Wine (CrossOver, Whisky or the toolkit).
  - The menus: do they draw? Do the mouse, keyboard and controller
    work in them? Can you type your name or a chat message?
  - Leaving a match: do you get back to the menus?
  - The game: does it start? Do sound and input work in a match?
  - Anything that looked or felt wrong, with a screenshot if you can.
  - The logs, below.

Logs on Linux and the Deck: send the run folder of the session that
went wrong. Each start makes one. Right after that session, these two
lines put the newest one on your Desktop as rr-run.tar.gz:
  RUN=$(ls -td ~/.steam/steam/steamapps/compatdata/*/pfx/drive_c/users/steamuser/AppData/Local/ReachRenaissance/runs/*/ ~/.local/share/Steam/steamapps/compatdata/*/pfx/drive_c/users/steamuser/AppData/Local/ReachRenaissance/runs/*/ ~/.var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/compatdata/*/pfx/drive_c/users/steamuser/AppData/Local/ReachRenaissance/runs/*/ 2>/dev/null | head -1)
  tar -czf ~/Desktop/rr-run.tar.gz -C "$RUN.." "$(basename "$RUN")"
If there's no Desktop folder, use ~/rr-run.tar.gz instead.

For a crash, missing menus or a failed WebView2 install, also send
Proton's log: set the shortcut's Launch Options to
  PROTON_LOG=1 %command%
play once more until it goes wrong, and send steam-<number>.log from
your home folder (with Flatpak Steam, from
~/.var/app/com.valvesoftware.Steam instead). Then clear the Launch Options again: the log slows
the game down, so don't judge smoothness with it on.

Logs on a Mac: rr-launcher.txt from your Desktop, and the newest
folder in the bottle's
  drive_c/users/<Wine user>/AppData/Local/ReachRenaissance/runs
zipped (right-click > Compress). The Wine user is crossover in
CrossOver, and usually your Mac user name in Whisky. CrossOver's
bottles are in ~/Library/Application Support/CrossOver/Bottles. In
Finder, Go > Go to Folder (Cmd+Shift+G) opens a path.
```
