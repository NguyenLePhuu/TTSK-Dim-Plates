# Test project boundaries

The production application does not reference this directory. The main solution contains only `TTSK Dim Plates.csproj`.

Test projects may reference the production project through `ProjectReference`, but they must not compile production `.cs` files directly and must not depend on a copied executable under `.codex-artifacts`.

Deleting `tests` does not prevent the main application or portable package from building or running. It only removes regression checks and makes test-only commands such as `scripts/Test-Updater.ps1` unavailable. Keep tracked tests when possible so accidental deletion can be restored with Git.
