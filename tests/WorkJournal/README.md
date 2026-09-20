# Work journal checks

Compile `Program.cs` together with `../../TTSK Dim Plates/TTSK Dim Plates/WorkJournal.cs`
using the Visual Studio Roslyn C# compiler, referencing System.Core.dll and System.Xml.dll.
Run the executable with an output directory argument. Tests use a unique subdirectory there,
never the user's real journal. The generated `TTSK_GioLam_Mau.txt` is fictional sample data.

24 checks cover repeated restarts, multiple projects across days and model paths, same-session model switching,
latest closing time per project/day, UTF-8 Japanese, corrupt records, hiding incomplete sessions,
daily rollover without fabricated closing times, legacy overnight records, date filtering and atomic record replacement.

Compile `UiSmoke.cs` referencing the built application, System.Core, System.Xml, System.Drawing and
System.Windows.Forms. Copy the application's EXE config alongside the smoke executable. Run with
the output-directory argument, optionally followed by `dark`, to test each theme. The smoke test
checks grouped cards, selectable read-only code/name fields, custom calendar selection, absence of
native date controls/window frame, custom maximize/restore, scrolling, compact layout and real close/cancel behavior.
