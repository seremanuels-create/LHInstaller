LHInstaller 1.1
Put the programs of a freshly formatted PC back in place, in two steps.

(Italiano: vedi LEGGIMI.txt)

--------------------------------------------------------------------------
BEFORE YOU FORMAT
--------------------------------------------------------------------------

1. Open LHInstaller.exe (double-click, nothing to install).

2. Fill the list. Three ways, all in the top bar:
   - "Read from this PC"  two tabs. The first: the programs you have now
                           that winget can reinstall on its own (just tick
                           them). The second: the ones winget CANNOT
                           reinstall, taken from a website or carrying a
                           licence. For each of those you can look for a
                           catalog match, add the installer address, open
                           a web search, or put it on the list as a
                           reminder.
   - "Search the catalog" type a name: the row marked "Recommended" is
                           always the latest stable version.
   - "Add address"        for programs the catalog does not have: paste
                           the link to the installer file.

   Reminders land in the "To complete" group with the status
   "address missing": double-click one and you give it an address. Until
   they have one, the install skips them and says so.

3. Organise, if you like. The "Groups" panel on the left keeps things
   tidy: a group's checkbox turns everything inside it on or off, its
   name filters the list. Only ticked entries get installed.

4. Copy the whole folder to a USB stick.
   Two files are enough: LHInstaller.exe and LHInstaller.json (the list,
   which saves itself next to the executable). No installer is stored:
   everything is downloaded when the time comes.

--------------------------------------------------------------------------
AFTER YOU FORMAT
--------------------------------------------------------------------------

1. Copy the folder from the stick to the PC (the Desktop will do).

2. Double-click LHInstaller.exe and press "Start installs".

It asks for administrator rights once, then downloads and installs one
after the other. The console at the bottom streams winget's real output,
line by line; the "Status" column shows where each program is (running,
installed, failed).

If something fails, the summary lists the failures. Press Start again:
whatever is already installed gets skipped. If an installer fails
quietly, turn on "Show installer windows" and try again.

--------------------------------------------------------------------------
WHY IT ASKS FOR ADMINISTRATOR ONLY WHEN YOU PRESS START
--------------------------------------------------------------------------

Building the list needs no special rights, and being asked at every
launch would be tiresome. Installing software does need them: when you
press Start, LHInstaller restarts as administrator (Windows asks once)
and carries on by itself. The shield on the button is the Windows
convention for that; the bottom bar always shows the current state.

--------------------------------------------------------------------------
WHAT THE FORMATTED PC NEEDS
--------------------------------------------------------------------------

Nothing. Windows 10 and 11 already ship with everything:

  .NET Framework 4.8   runs LHInstaller
  winget               downloads and installs the programs
                       (comes with "App Installer")

If winget is missing, LHInstaller says so at startup, in the bottom bar
and in the console, and does not pretend to work. Install it from the
Microsoft Store by searching for "App Installer".

--------------------------------------------------------------------------
LIMITS, PLAINLY
--------------------------------------------------------------------------

The winget catalog covers programs you download from the web: browsers,
Discord, Spotify, VLC, 7-Zip, Steam, Blender, Git, Python, Node, Visual
Studio Code, OBS and so on.

It does NOT cover programs with a personal licence or an account: audio
plug-ins, Ableton, FL Studio, DaVinci Resolve, Native Access. The
"Not reinstallable" tab of "Read from this PC" lists them all, so you
decide for each one: installer address, reminder, or the maker's portal
as you have always done.

"Look for catalog matches", in that tab, tries to match each program to
the catalog by name: sure matches in green, namesakes to check in orange
("maybe"). It is one winget call per program, so it is slow: that is why
it is a button, and why it can be stopped.

For direct addresses a silent install is not guaranteed: each installer
family takes a different argument. LHInstaller recognises Inno Setup,
NSIS, MSI, WiX and InstallShield by reading the signature inside the
downloaded file. If it cannot tell the family, it opens the installer
window and writes that in the console, instead of pretending it worked.

The "Status" column says "installed" when winget recognises the program
on the PC. A program installed from a website, without going through
winget, may not be recognised: it stays "to install" and, if you run it,
its installer updates it.

--------------------------------------------------------------------------
THE OTHER THINGS IT DOES
--------------------------------------------------------------------------

Check for         Brings the Version column up to date with what the
updates (F5)      catalog offers today, and flags when the PC has an
                  older one. For direct addresses it notices when the
                  file at the other end changed (ETag, date, size).

Filter (Ctrl+F)   Shows only entries whose name, identifier, site or
                  group contains the text. Esc clears it.

Right-click       On an entry: edit, move to a group, "install only
                  this one", copy identifier. On a group: tick or untick
                  everything, rename, delete.

Profile           Load, save as, full backup and restore (replace or
                  merge). "Import from a winget file" and "Export in
                  winget format" exchange the list with anyone using
                  "winget export / import" from a terminal.

Console           Clear, save log, auto-scroll. Every install session
                  leaves a file in the Log folder anyway.

Language          "Language" menu in the top bar: Italiano, English, or
                  Automatic, which follows Windows. The choice is saved
                  in the profile and LHInstaller reopens itself in the
                  new language. Numbers and dates follow it too.

Updates           At startup LHInstaller checks whether a newer version
                  of itself exists and, if so, says it with a strip at
                  the top of the window: you can see what's new,
                  download, or skip that version. The check is switched
                  off from the Help menu ("Check at startup") and can be
                  run by hand from there. With no connection, or while
                  no version has been published, it says nothing.

Shortcuts         Ctrl+K search, Ctrl+F filter, Ctrl+S save, F5 check,
                  Del remove, Enter edit, F1 help.

Command line      LHInstaller.exe --apri cerca | pc | indirizzo | aiuto
                  opens that dialog straight away. --avvia starts the
                  installation (used by the restart as administrator).

--------------------------------------------------------------------------
WHERE THE FILES GO
--------------------------------------------------------------------------

LHInstaller.json   your list, next to the executable
Download\          installers fetched from direct addresses
Log\               the report of each session

If the USB stick is write-protected, LHInstaller switches by itself to
%APPDATA%\LHInstaller and says so at startup.

--------------------------------------------------------------------------
REBUILDING IT
--------------------------------------------------------------------------

    .\build.ps1

It uses the C# compiler already inside Windows, in
C:\Windows\Microsoft.NET. Neither Visual Studio nor the .NET SDK is
needed: the very PC that LHInstaller exists to repopulate can also
rebuild it from scratch. The script generates the executable icon too.
