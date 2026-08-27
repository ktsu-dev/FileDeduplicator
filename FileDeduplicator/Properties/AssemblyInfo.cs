// Copyright (c) 2023-2026 ktsu-dev contributors

// NOTE: no "ktsu." prefix. This repository has no AUTHORS.md, so ktsu.Sdk resolves an empty
// AuthorsNamespace and the assembly is named FileDeduplicator rather than ktsu.FileDeduplicator
// the way every other repository in the organization is. Tracked separately; if that is corrected
// this literal has to move with it or the test project silently loses access to internals.
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("FileDeduplicator.Test")]
[assembly: CLSCompliant(false)]
[assembly: System.Runtime.InteropServices.ComVisible(false)]
