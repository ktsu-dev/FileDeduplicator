// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace ktsu.FileDeduplicator;

using System.Collections.Generic;
using System.IO;

using ktsu.Semantics.Paths;
using ktsu.Semantics.Strings;

internal static class FileScanner
{
	internal static IReadOnlyList<AbsoluteFilePath> ScanForFiles(AbsoluteDirectoryPath path)
	{
		if (!path.Exists)
		{
			Console.WriteLine($"Directory not found: {path}");
			return [];
		}

		List<AbsoluteFilePath> files = [];
		foreach (string file in Directory.EnumerateFiles(path.WeakString, "*", SearchOption.AllDirectories))
		{
			files.Add(file.As<AbsoluteFilePath>());
		}

		return files;
	}
}
