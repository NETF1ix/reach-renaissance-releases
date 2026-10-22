# OPEN FIRST: Proton test build

**A test build for Linux, Steam Deck and Mac (Wine), not a release.** Windows players: use the [latest release](https://github.com/NETF1ix/reach-renaissance-releases/releases/latest) instead.

**Don't update this build:** if the launcher asks to update, answer **No**, and in the menus press **LATER**.

The steps:

```text
OPEN FIRST: Reach Renaissance Proton test build
Test build, commit 8e2df4f, 22 Oct 2026
===============================================

Thanks for testing! This kit holds the 33 files that turn the official
Reach Renaissance 0.1.19 into a test build for Wine on a Mac and Proton
on Linux and the Steam Deck (draft PR #141). It's built from the main
branch, so it also has changes made since 0.1.19 that aren't released
yet (File Share, the chat show/hide key, Workshop mod warnings and
blocks, LAN and relay changes). It's a test build, not a release, and
it will be taken down once testing is done.

Don't update this build. While 0.1.19 is the newest release, nothing
is offered. If a newer one comes out while you test, the menus show
"Reach Renaissance 0.1.x is available": press LATER, not UPDATE AND
RESTART, and skip "Update to 0.1.x" in the menu. If the launcher asks
"Update and restart now?", answer No. An update replaces this build
with an official one, and official 0.1.19 doesn't run under Wine. If
it happens anyway, delete ~/Games/rr-proton-test and redo step 1.


You need
--------
  - Halo: The Master Chief Collection with Halo: Reach, installed and
    up to date in Steam. On a Mac that's Windows Steam in a Wine
    bottle (CrossOver, Whisky or other). On Linux and the Deck it's
    the normal Steam (MCC runs with Proton).
  - The official ReachRenaissance-v0.1.19.zip, from the release page:
    https://github.com/NETF1ix/reach-renaissance-releases/releases/tag/v0.1.19
  - This kit, ReachRenaissance-v0.1.19-proton-kit-8e2df4f.zip, from
    this page: click it in the file list above, then the download
    button (Download raw file). Or from a terminal:
  cd ~/Downloads && curl -LO https://github.com/NETF1ix/reach-renaissance-releases/raw/proton-test-build/ReachRenaissance-v0.1.19-proton-kit-8e2df4f.zip

Keep both as .zip files in Downloads. On a Mac, Safari unzips
downloads by itself and deletes the .zip, and its way of unzipping
breaks the official zip. Before downloading, turn off Safari >
Settings > General > "Open 'safe' files after downloading", or use
another browser. You can also get the official zip from Terminal:
  cd ~/Downloads
  curl -LO https://github.com/NETF1ix/reach-renaissance-releases/releases/download/v0.1.19/ReachRenaissance-v0.1.19.zip
If Downloads has a folder named after a zip instead of the zip,
delete the folder and download the zip again.


1. Unzip both into a new folder, from a terminal
------------------------------------------------
Use a terminal, not Finder or Ark: the official zip stores its folders
in a Windows format that those apps unpack as flat files. Copy and
paste each line, then press Return or Enter.

Mac (Terminal):
  mkdir -p ~/Games/rr-proton-test && cd ~/Games/rr-proton-test
  unzip -oq ~/Downloads/ReachRenaissance-v0.1.19.zip
  unzip -oq ~/Downloads/ReachRenaissance-v0.1.19-proton-kit-8e2df4f.zip
  shasum -a 256 -c ReachRenaissance-v0.1.19-proton-kit-8e2df4f-SHA256SUMS.txt | grep -v ': OK$'

macOS may ask whether Terminal can access your Downloads folder:
click OK. unzip warns that the official zip "appears to use
backslashes as path separators". That's expected.

Steam Deck: do everything in Desktop Mode (Steam button > Power >
Switch to Desktop). Open Konsole from the app menu at the bottom left
(System > Konsole). Steam + X shows the keyboard. To paste a line in
Konsole, press Ctrl+Shift+V (Ctrl+V doesn't paste there).

Linux and Steam Deck (Konsole or another terminal):
  mkdir -p ~/Games/rr-proton-test && cd ~/Games/rr-proton-test
  bsdtar -xf ~/Downloads/ReachRenaissance-v0.1.19.zip
  bsdtar -xf ~/Downloads/ReachRenaissance-v0.1.19-proton-kit-8e2df4f.zip
  sha256sum --quiet -c ReachRenaissance-v0.1.19-proton-kit-8e2df4f-SHA256SUMS.txt

SteamOS and Arch have bsdtar. If it says "bsdtar: command not found"
(Ubuntu, Debian, Mint, Fedora), use the two unzip lines from the Mac
commands in place of the bsdtar lines.

The last command prints nothing when all 773 files match this build.
If it prints anything, stop: delete ~/Games/rr-proton-test, check that
both zips are in Downloads, and do step 1 again. If it still prints
something, send what it printed.

The game is now in ~/Games/rr-proton-test/ReachRenaissance.


2. Start it
-----------
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
    (Or click Run... in the bottle in Whisky and pick
    "Reach Renaissance.exe". Then there's no rr-launcher.txt.)
  - Plain Wine or Game Porting Toolkit, for a bottle made with that
    same Wine (the toolkit's command is wine64):
      cd ~/Games/rr-proton-test/ReachRenaissance
      WINEPREFIX="<bottle folder>" wine "Reach Renaissance.exe" 2>&1 | tee ~/Desktop/rr-launcher.txt

Linux and Steam Deck: in ~/Games/rr-proton-test/ReachRenaissance,
open README.txt and follow "Linux and Steam Deck" steps 3 to 6 (step 1
here replaces its step 2). In its step 3, pick "Reach Renaissance.exe"
in ~/Games/rr-proton-test/ReachRenaissance, not in
~/Games/ReachRenaissance. Then, in the shortcut's Properties in Steam,
rename it "Reach Renaissance TEST", so you can tell it apart.

If it says "Reach Renaissance doesn't support Linux/Proton yet", you
started the official 0.1.19, not this build. Start it from
rr-proton-test. Its VERSION.txt says "Proton test build".


3. Install WebView2 when asked
------------------------------
When the launcher offers to install Microsoft Edge WebView2 Runtime,
answer Yes. The menus need it. The install takes a few minutes and
needs the internet. Then play as usual.

This changes the bottle (or Proton prefix) for good: WebView2 and Edge
Update are installed, and WebView2 is set to run as Windows 7 there.
On a Mac, if other programs in that bottle use WebView2, test in a
copy of the bottle.


If the menus stay empty
-----------------------
Try each of these, one at a time, then both together:
  WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--no-sandbox
  WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--disable-gpu
  WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS="--no-sandbox --disable-gpu"

On a Mac, put it at the start of the command's last line, for example:
  WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--no-sandbox "$CX" --bottle "<bottle name>" "$PWD/Reach Renaissance.exe" 2>&1 | tee ~/Desktop/rr-launcher.txt

With Proton, set the shortcut's Launch Options in Steam to it followed
by %command%, for example:
  WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--no-sandbox %command%


What's different under Wine
---------------------------
None of this runs on Windows.
  - The launcher detects Wine and offers to install Microsoft Edge
    WebView2 Runtime when it's missing, and runs it as Windows 7. That's
    the known fix for WebView2's empty window under Wine, which lacks
    DirectComposition.
  - Updates work without curl and tar.
  - Linux folders hidden by a dot (like ~/.local/share/Steam) show up
    in the folder picker.
  - No pictures of the game window are taken, because they come out
    black under Wine. The Theme page says there's no preview.
  - Steam Workshop links (for mods a game needs) open in your system's
    own Steam. That's right for Proton. On a Mac they go to the Mac's
    Steam, not the one in the bottle, so tell us what happens when you
    join a game that needs a mod.


What to send back
-----------------
Reply to whoever sent you this kit with:
  - Your setup: Mac (Intel or Apple Silicon) and macOS version, or
    your Linux distribution or SteamOS version. Your Wine (CrossOver,
    Whisky, Game Porting Toolkit, plain Wine or Proton) and its
    version. On a Mac, the graphics backend (D3DMetal, DXMT, DXVK with
    MoltenVK, or WineD3D).
  - The WebView2 install: did it finish, and what did the launcher say?
  - The menus: do they draw? Empty, black, white or working? Do mouse,
    keyboard and controller work in them?
  - The menus' background: is the game's scene (the Spartan, the
    backdrop) visible behind the menus, or is the background solid?
  - The game: does it start? Do sound and input work in a match?
  - Logs, below.

Logs on a Mac: rr-launcher.txt from your Desktop, and the newest
folder in the bottle's
  drive_c/users/<Wine user>/AppData/Local/ReachRenaissance/runs
zipped (right-click > Compress). The Wine user is crossover in
CrossOver, and usually your Mac user name in Whisky and plain Wine.
CrossOver's bottles are in ~/Library/Application Support/CrossOver/
Bottles. In Finder, Go > Go to Folder (Cmd+Shift+G) opens a path.

Logs on Linux and the Deck: set the shortcut's Launch Options to
PROTON_LOG=1 %command% (anything else goes before %command% too), play
once, and send steam-<number>.log from your home folder. Also send the
newest run folder. These lines put it on your Desktop as rr-run.zip:
  RUN=$(ls -td ~/.local/share/Steam/steamapps/compatdata/*/pfx/drive_c/users/steamuser/AppData/Local/ReachRenaissance/runs/*/ | head -1)
  bsdtar -a -cf ~/Desktop/rr-run.zip -C "$RUN.." "$(basename "$RUN")"
```
