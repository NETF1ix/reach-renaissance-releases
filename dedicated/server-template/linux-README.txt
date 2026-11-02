project-renaissance-dedicated on Linux
=====================================

Reach Renaissance itself has no Linux version: its own README says Linux and the
Steam Deck run it through Proton, experimentally. So this host runs natively on
Linux and starts the game's Windows engine through Wine or Proton.

What you need
  - Halo: The Master Chief Collection + Halo: Reach installed in Steam.
  - The Reach Renaissance release unzipped in your home folder (not inside MCC).
  - Wine, or Proton (name it in dedicated.toml as runner, or with a wrapper
    script of your own).
  - Make the program executable once:
        chmod +x project-renaissance-dedicated

Start
  0. You can keep everything in one folder, the way Project Reclaimer does it:
     copy the Reach Renaissance files into game/ next to dedicated.toml (bin,
     data, themes, licenses, VERSION.txt - the launcher .exe isn't needed; see
     game/COPY GAME FILES HERE.txt). That is what game = "game", the default,
     means, and it is what you upload to a VPS. MCC's own files can go in
     game/mcc the same way (set mcc = 'game/mcc').
  1. Or put dedicated.toml next to the program and edit it:
        game        = '/home/you/Games/ReachRenaissance'
        mcc         = '/home/you/.steam/steam/steamapps/common/Halo The Master Chief Collection'
        runner      = leave out to use wine, else e.g. "proton run"
        wine_prefix = leave out for ~/.wine, else your prefix
        client_data = leave out to let it search, else the ReachRenaissance
                      folder inside the prefix (fileshare/ and playlists/ live there)
  2. ./project-renaissance-dedicated check      shows what it found and what is missing
  3. ./project-renaissance-dedicated            starts every server; Ctrl+C stops them
  4. A server's dashboard is at http://127.0.0.1:<game port + 1000>/
     (password in data/<server>/admin/password.txt)

Paths
  Everything the engine reads is written in the form a Windows program uses:
  /home/you becomes Z:\home\you, and C:\... is looked up inside the Wine prefix.
  Playlists the game's menus wrote on Linux, or playlists written on Windows,
  therefore resolve as they are.

  Game types and saved Forge maps can live in the server folder itself, next to
  dedicated.toml: "Game Modes" (or "GameTypes") and "Maps". A playlist may name
  a file by the path another PC had (C:\Users\You\...\MY GAME.bin); this host
  looks it up by file name in those folders first, so a folder copied from a
  Windows PC to a VPS runs without editing its playlists. Stock game types come
  from MCC (set mcc) or can be copied in; check says exactly what it resolved.

Firewall
  Linux firewalls are usually off. If yours is on (ufw, firewalld), allow UDP
  40011-40013 (and whatever ports you set in dedicated.toml). UPnP is asked for
  by the engine itself, and needs it enabled in the router (upnp = true).
  When the router doesn't open the port (the console says so), players off your
  network reach the server through the server list's relay only, which plays
  laggy: forward UDP and TCP for each game port on the router instead.

  ./project-renaissance-dedicated reach  answers that instead of leaving it to
  guesswork: it asks the server list which address it sees for this machine, what
  it knows about each server, and its rendezvous (the UDP request a player's
  client sends), whose answer names the address and port the list would hand a
  player. That is measured from outside, so a forward made by hand is confirmed
  here with no second machine: the port in the answer is the port players are
  told when the forward works, and a NAT-assigned one when it doesn't. It prints
  that port, the one to forward, and what to forward it to.
  RCON stays on this machine (TCP, 127.0.0.1 by default), so no rule is needed
  for it unless you set [rcon] bind to something else on purpose.

RCON
  Set a password in [rcon] and every server answers Source RCON on TCP, on its
  own game port number by default:
        status | players | say <message> | kick | ban | unban | bans |
        endgame | endround | next | restart | reload | help
  The Admin UI panel that speaks this protocol works as it is: point it at
  127.0.0.1 and the port, with the same password.

Updates
  When the game updates under a running host (VERSION.txt or bin/host-probe.exe
  changes), every server is restarted on the new build: players can only join a
  server that runs the same version they do. Set restart_on_update = false to
  keep servers where they are.

Keep it running
  Started from a terminal, servers stop when that terminal closes. To keep them
  up, run the host under tmux or screen:
        tmux new -s renaissance
        ./project-renaissance-dedicated
        # Ctrl+B then D leaves it running; tmux attach -t renaissance returns
  or as a service you write once as root, in /etc/systemd/system/renaissance.service:
        [Unit]
        Description=Project Renaissance dedicated servers
        After=network-online.target
        [Service]
        User=YOUR_USER
        WorkingDirectory=/home/YOUR_USER/renaissance
        ExecStart=/home/YOUR_USER/renaissance/project-renaissance-dedicated
        Restart=on-failure
        [Install]
        WantedBy=multi-user.target
  then: sudo systemctl enable --now renaissance    (its output: journalctl -u renaissance -f)

Everything else - config keys, playlists, ports, and the notes from real runs
(the late-join behaviour, the server-list registration trap, the console) - is
in README.md next to this file.
