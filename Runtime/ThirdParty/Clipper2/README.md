# Clipper2 (vendored)

- Source: https://github.com/AngusJohnson/Clipper2 — tag `Clipper2_2.0.1`, `CSharp/Clipper2Lib/`
- License: Boost Software License 1.0 (`LICENSE.txt`). `HashCode.cs` additionally carries its own
  MIT (.NET Foundation) / BSD-2 (xxHash) notice in its header.
- Local changes:
  - namespace `Clipper2Lib` -> `VMG.ThirdParty.Clipper2Lib` (avoids clashing with a project's own Clipper2 copy)
  - `#nullable enable` added to the files that lacked it (silences CS8632 under Unity)
- Used by `VMG.Core.StrokeMerger` (union + triangulation).
