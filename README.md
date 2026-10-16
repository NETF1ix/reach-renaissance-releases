# Reach Renaissance

Halo: Reach multiplayer, Firefight and Campaign with the Xbox 360 menus, playable over the internet, without opening MCC.

A free fan project, not affiliated with Microsoft, Halo Studios, 343 Industries or Bungie. No game files are included: you need your own copy of the game.

## What you need

- Windows 10 or 11, 64-bit.
- Halo: The Master Chief Collection on **Steam**, with Halo: Reach installed and up to date. MCC doesn't need to be open, and nothing in MCC changes (anti-cheat settings included). The Microsoft Store / Game Pass version isn't supported.
- Everyone you play with on the same Reach Renaissance version.

## Download and play

1. From [Releases](../../releases/latest), download `ReachRenaissance-v<version>.zip` and `SHA256SUMS.txt`.
2. Optional: check the download. In PowerShell, `Get-FileHash ReachRenaissance-v<version>.zip` should print the hash listed in `SHA256SUMS.txt`.
3. Unzip anywhere (not inside the MCC folder) and double-click `Reach Renaissance.exe`. Windows may say "Windows protected your PC" because the program isn't signed: **More info**, then **Run anyway**.

New versions are offered when you start the game (**Update and restart**). Only releases signed with the project's key install.

## Playing online

- **Join:** Matchmaking's server list shows public games and games on your home network. No router setup needed.
- **Host:** SETTINGS > Network > Host Public, then Custom Games, pick a map and game type, Start Game. The first time, use SETTINGS > Network > Allow Reach Renaissance so Windows Firewall lets players in.
- The public server list doesn't show anyone's IP address, but like most PC games with player hosting, people in a game with you can learn your internet address.

## Known limits

- When MCC updates, Reach Renaissance stops starting until a new release supports that update.
- This is an early test build: expect bugs. Logs are in `%LOCALAPPDATA%\ReachRenaissance\runs`; zip the newest folder for a bug report (it contains the addresses of players you played with, so send it to the developers rather than posting it).
