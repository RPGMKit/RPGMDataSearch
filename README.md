# RPGMDataSearch

A small WPF program that searches the data of an RPG Maker project (VX Ace `.rvdata2` or MV `.json`) and shows where things are used: which events call a common event, show a picture, set or check a switch,
a variable, an item and so on. A found page can be opened as an html dump in the browser, with the found lines marked.

The project is read with [RPGMDataKit](https://github.com/RPGMKit/RPGMDataKit) (a submodule). The program does not change the project or the library.

## Build and run

Requires the .NET 10 SDK and Windows (WPF, `net10.0-windows`).

```bash
git clone --recurse-submodules <this repository>
dotnet run
```

If the repository is cloned already: `git submodule update --init --recursive` (the library has a submodule of its own, `RubyMarshalCS`; `--recursive` takes it).

## Using it

1. Choose the project: type the folder or use **Browse**. MV: the folder with `data`, or the one with `www`. VX Ace: the folder with `Data\System.rvdata2`. The field is a list of the last 10 projects that were opened.
2. Choose a tab, fill in what to find, press **Search** (or Enter in a field).
3. Look through the rows, filter them, double click a row to open its page in the browser.

Only the open tab is searched. The result is common for the tabs: a new search replaces it.

## Search

Each kind of search has its own tab with only the fields it needs.

| Tab | Finds | Fields |
|---|---|---|
| Common event calls | `Call Common Event` of the given common events | ids, name |
| Show Picture | `Show Picture` by the name of the picture | text, regular expression, ignore case |
| Switches | `Control Switches` (a range counts), `If Switch`, the conditions of a page, the switch of a parallel/autorun common event or of a troop page | ids, name, set / check |
| Variables | `Control Variables` (a range counts), `If Variable`, the condition of a page; a variable that is read as the value of another command (the operand of `Control Variables` or `If Variable`, the variable amount of `Change Items`, `Change Weapons`, `Change Armors`) | ids, name, set / check |
| Self switches | `Control Self Switch`, `If Self Switch`, the condition of a page | letters, set / check |
| Items, Weapons, Armors | `Change Items/Weapons/Armors` (set), `If` on them (check), `Control Variables` that reads their number from the game (check) | ids, name, set / check |
| Actors | `Change Party Member` (set), `If Actor` (check), `Control Variables` that reads a parameter of an actor (check), the actor condition of a page | ids, name, set / check |
| Transfer map | `Transfer Player` to the given maps | ids, name |
| Command text | the name and the description of any command, as the dump writes them | text, regular expression, ignore case |

- **Ids** are `1, 5-10, 40`. For a range of switches or variables in a command, any id of the range is enough. An empty field is any.
- **Name** is a part of the name as the editor shows it (of the common event, switch, variable, item, map, ...), or a regular expression with the options next to the field. The ids and the name both have to fit.
- **Set / check.** *Set* is a command that changes the thing; *check* is a condition or a read of it; *both* is the default. A call of a common event, a picture and a transfer have neither.
- **Look in** (under the tabs) chooses the common events, the map events and the troops.
- A transfer by variables has no map to tell (the variables hold it when the game runs), so it is found only when the ids and the name are empty.

## The result

A row for each command that was found. A condition of a page (not a command) is a row with the line `-1`.

| Column | |
|---|---|
| Source | CE, Map or Troop |
| Location | `M3 Name › E5 Name (x,y) › P2`, `CE12 Name`, `T4 Name › P1` |
| Trigger | how the page starts (a common event: Call only, Autorun, Parallel) |
| Page conditions | the conditions of the page |
| Line | the number of the command in the page, as the dump numbers it |
| Command, Details | the command and its description |
| Matched | what matched, with its name |
| Access | set or check |
| Hits on page | how many rows the page has |

Sort a column by clicking its header.

### Filter

- **Filter the result** keeps the rows that have all the typed words (in any column).
- Every column has a button ▾ in its header: it opens the values of the column (with the number of rows, a search field and "Select all") and keeps the rows with the checked values, as a table processor does.
  The values are those of the rows that the other filters let in. The button of a filtered column is blue.
- The text filter and the filters of the columns work together (all of them have to fit).
- **Clear all filters** (right of the text filter) clears all of them. A new search clears the filters of the columns; they are not kept in `settings.json`.

## Dumps

A double click on a row, **Open the page dump** or the context menu writes the html dump of the page and opens it in the browser. The dump is the library's own html dump (`DumpOptions`).

- **Open dumps of all pages of the event** does it for every page of a map event (it asks first when there are more than 6).
- **Open dumps of all results** writes and opens one dump for each page of the rows that are shown (the filter of the result counts); it asks first when there are more than 10 pages.
- **Expand common events** puts the called common events under their calls; **Extended format** is the extended names.
- **Dump to** is the folder of the dumps, `temp` next to the program by default. A project has a folder of its own in it, named by the hash of the path of the project: `<Dump to>/<hash>/CE|ME|TE/`.

The lines of the hits on the page are marked in the dump and the page is opened at the first of them: the line of the chosen row, or the first of the shown rows for "all results". A condition of a page has no line and marks nothing.
The dump is opened by the address `page.html#lines=12,40`; the shell takes a path to open (a `#` would be a part of the name), so a little page `*.marked.html` next to the dump leads there.

## Settings

Everything is kept in `settings.json` next to the program. It is read at the start and written when the window is closed and after a search; a broken file is ignored.

- the project and the last 10 projects that were opened (a folder that is gone is dropped);
- the open tab and what is typed on each tab, "Look in" and the text filter of the result;
- the options and the folder of the dump;
- the position and the size of the window and the widths of the columns.

## Layout

| | |
|---|---|
| `MainWindow.xaml(.cs)` | the window: the tabs, the result and its filters, the dump |
| `SearchEngine.cs` | walks the common events, the pages of the map events and of the troops; what a command refers to |
| `SearchModel.cs`, `IdFilter.cs` | the request, the hit, the ids and ranges |
| `DumpService.cs` | the html dump of a page with the library, the folder of a project, opening it with the marked lines |
| `ProjectLoader.cs` | MV or VX Ace by the folder |
| `AppSettings.cs` | `settings.json` |
| `external/RPGMDataKit` | the library (submodule) |
