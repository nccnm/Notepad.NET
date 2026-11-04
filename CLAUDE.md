# Agent instructions

## End-to-end tests

The full end-to-end suite takes several minutes and needs the desktop left alone. Run it only when the user asks.
Otherwise run only the end-to-end tests of the feature just implemented or changed:

```
dotnet test --project tests/NotepadNet.E2E.Tests -c Release --no-build --filter-class "NotepadNet.E2E.Tests.<ClassName>"
```

Build first, since `--no-build` runs the existing binaries. In the end-of-task report, name the end-to-end classes
that ran and say that the full suite did not.
