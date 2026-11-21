# Amico's Satchel

A Dalamud plugin for Final Fantasy XIV that bundles small quality-of-life features into one plugin. Type `/satchel` (or use the plugin's button in the plugin installer) to see every feature and turn each one on or off.

## Installing

1. In game, type `/xlsettings` and open **Experimental**.
2. Under **Custom Plugin Repositories**, add this link, tick **Enabled** and press **Save**:

   ```
   https://raw.githubusercontent.com/phrantkjay/AmicosSatchel/main/repo.json
   ```

3. Type `/xlplugins`, search for **Amico's Satchel** and install it.
4. Type `/satchel` and switch on the features you want.

For the live map: switch on **Amico's Second Sight**, open its settings and click **Download Second Sight**. The map downloads from this repository's releases, is checked before use, and keeps itself up to date.

## Features

### Tails of Chance

Shows your odds of finishing Wondrous Tails inside the journal, under the seal prompt:

- The exact chance of finishing with 1+, 2+, and 3+ lines from your current seals
- What a shuffle would give you, with your odds colored green (better than a shuffle) or red (worse) whenever you can shuffle

A finished journal holds 9 seals, and each new seal lands on a random empty square. Every 9-seal board that contains your current seals is equally likely, so the plugin lists all 11,440 possible finished boards once and counts the ones that match yours.

A shuffle rearranges your seals at random, and the remaining seals then land at random too. Together that is the same as drawing a completely random finished board, so the shuffle figure is a single exact value (58.46% / 10.56% / 0.21%) no matter how many seals you have.

### Amico's Second Sight

A live map of your zone to keep on a second screen. The module feeds the Amico's Second Sight desktop app: turn it on in `/satchel`, then start the app (the module's settings have an **Open Second Sight** button once the map has connected once).

- Sends your zone, position and facing, nearby players, NPCs, monsters and objects, party members, FATEs, Critical Engagements (Occult Crescent and Bozja), quest and event markers, the map flag and your deciphered treasure map
- Read-only: it only reads game state and never sends input to the game
- Talks to the app over a local named pipe; nothing leaves your PC
- `/secondsight` prints your zone, position and whether the map is connected

### FC Command Center

A dashboard for Free Company officers and members. Open it with `/fcc` (add a tab name to jump to it, such as `/fcc tasks`).

- **Calendar**: FC events by month, one-off or repeating every week or two, shown in your own time zone, with chat reminders before they start. Everyone can reply Going, Maybe or Can't go, and officers see the headcount by role (tanks, healers, DPS)
- **Announcements**: news for members, with pinning, expiry dates and a "new" marker for what you haven't read
- **Tasks**: officer task assignments with an assignee, due date, priority and status
- **Company actions**: what's running now and how long it has left, plus the planned rotation with stock counts and the next one to activate
- **Chest**: what the FC chest held when it was last opened, added up across pages, with notes per item and a warning when something drops below its minimum. Also logs the FC's gil over time and what went in and out of the chest between readings
- **Workshop**: submersible and airship return times, a log of what each voyage brought back, and workshop projects with every phase's materials filled in from the game, checked against the chest, and a note of who's bringing what
- **Members**: the FC roster with when each member was last seen online, a filter for inactive members, and private officer notes. The roster also fills the name pickers for tasks, hosts and project leads

Chat alerts (each can be turned off in the module's settings, with an optional sound):

- An event is about to start
- A submersible or airship is back
- A company action is about to end, naming the next one in your rotation
- On login: new announcements, tasks assigned to you and overdue tasks

The game data is read-only and only fills in when you open the matching window in game: the FC chest, the workshop's voyage control panel, or the Free Company window's Members tab.

#### Sharing

Everything is saved on your PC, one file per Free Company. To share it, an officer copies a **share code** from the Share tab and posts it in the FC's Discord or a DM. Members paste it into their own Share tab. Each item keeps its own last-change time, so the newer change wins item by item, deletions carry over, and pasting the same code twice is harmless. Member codes leave out the chest contents and roster unless the officer ticks them.

Members send their event replies back the same way: **Copy my reply code** holds only their own replies, for an officer to paste in.

The Share tab can also copy a Discord post with this week's events (as Discord timestamps, so everyone sees their own time zone) and pinned announcements, and save the events as an `.ics` calendar file for Google Calendar, Outlook or Apple Calendar.

Members who only read the board can turn off **Officer tools** in the module's settings.

## License

Copyright (c) 2026 Phrantk Amico. Source available under the [PolyForm Strict License 1.0.0](LICENSE).

You may install and use this plugin for personal, noncommercial purposes. You may not copy, modify, redistribute, or build on this code or its builds without written permission from Phrantk Amico.
