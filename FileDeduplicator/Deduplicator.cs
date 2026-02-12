// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace ktsu.FileDeduplicator;

using System.Collections.Generic;
using System.IO;
using System.Linq;

using ktsu.Semantics.Paths;

internal static class Deduplicator
{
	internal static Dictionary<string, List<AbsoluteFilePath>> GroupByHash(Dictionary<AbsoluteFilePath, string> fileHashes)
	{
		Dictionary<string, List<AbsoluteFilePath>> groups = [];

		foreach (KeyValuePair<AbsoluteFilePath, string> kvp in fileHashes)
		{
			if (!groups.TryGetValue(kvp.Value, out List<AbsoluteFilePath>? paths))
			{
				paths = [];
				groups[kvp.Value] = paths;
			}

			paths.Add(kvp.Key);
		}

		return groups;
	}

	internal static IReadOnlyList<DuplicateGroup> FindDuplicates(Dictionary<string, List<AbsoluteFilePath>> hashGroups) =>
		[.. hashGroups
			.Where(kvp => kvp.Value.Count > 1)
			.Select(kvp => new DuplicateGroup(kvp.Key, kvp.Value))];

	internal static AbsoluteFilePath SelectFileToKeep(List<AbsoluteFilePath> duplicates) =>
		duplicates.OrderBy(f => f.FileName.WeakString.Length).ThenBy(f => f.WeakString, StringComparer.Ordinal).First();

	internal static DeduplicationResult DeleteDuplicates(IReadOnlyList<DuplicateGroup> duplicateGroups)
	{
		int deletedCount = 0;
		long bytesReclaimed = 0;
		List<string> errors = [];

		foreach (DuplicateGroup group in duplicateGroups)
		{
			AbsoluteFilePath keeper = SelectFileToKeep(group.Files);

			foreach (AbsoluteFilePath file in group.Files)
			{
				if (file == keeper)
				{
					continue;
				}

				try
				{
					long fileSize = new FileInfo(file.WeakString).Length;
					File.Delete(file.WeakString);
					deletedCount++;
					bytesReclaimed += fileSize;
					Console.WriteLine($"  Deleted: {file}");
				}
				catch (IOException ex)
				{
					string error = $"  Error deleting {file}: {ex.Message}";
					errors.Add(error);
					Console.WriteLine(error);
				}
			}
		}

		return new DeduplicationResult(deletedCount, bytesReclaimed, errors);
	}
}

internal sealed class DuplicateGroup(string hash, List<AbsoluteFilePath> files)
{
	internal string Hash { get; } = hash;
	internal List<AbsoluteFilePath> Files { get; } = files;
	internal long FileSize { get; } = new FileInfo(files[0].WeakString).Length;
}

internal sealed class DeduplicationResult(int deletedCount, long bytesReclaimed, List<string> errors)
{
	internal int DeletedCount { get; } = deletedCount;
	internal long BytesReclaimed { get; } = bytesReclaimed;
	internal IReadOnlyList<string> Errors { get; } = errors;
}
