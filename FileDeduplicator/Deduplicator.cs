// Copyright (c) 2023-2026 ktsu-dev contributors

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

	/// <summary>
	/// Builds the duplicate groups from the hash groups, sizing each from the copies still on disk.
	/// </summary>
	/// <remarks>
	/// The hash pass can take minutes on a large tree, and the directories this is pointed at are
	/// often live, so a file that hashed may be gone by now. A vanished copy is dropped rather than
	/// allowed to abort the run, and a group it leaves with a single copy is no longer a group.
	/// </remarks>
	/// <param name="hashGroups">The files that hashed, grouped by hash.</param>
	/// <returns>Every group that still has at least two copies.</returns>
	internal static IReadOnlyList<DuplicateGroup> FindDuplicates(Dictionary<string, List<AbsoluteFilePath>> hashGroups)
	{
		List<DuplicateGroup> duplicates = [];

		foreach (KeyValuePair<string, List<AbsoluteFilePath>> kvp in hashGroups.Where(kvp => kvp.Value.Count > 1))
		{
			// Each copy is sized exactly once, so the filter and the size it reports cannot disagree
			// about a file that disappears between two reads.
			(AbsoluteFilePath File, long Size)[] present = [.. kvp.Value
				.Select(file => (File: file, Size: TryGetSize(file, out long size) ? size : (long?)null))
				.Where(copy => copy.Size.HasValue)
				.Select(copy => (copy.File, copy.Size!.Value))];

			if (present.Length > 1)
			{
				duplicates.Add(new DuplicateGroup(kvp.Key, [.. present.Select(copy => copy.File)], present[0].Size));
			}
		}

		return duplicates;
	}

	/// <summary>
	/// Sums the sizes of the given files, counting nothing for one that is no longer there.
	/// </summary>
	/// <param name="files">The files to size.</param>
	/// <returns>The total size in bytes of the files that could still be sized.</returns>
	internal static long TotalSize(IEnumerable<AbsoluteFilePath> files) =>
		files.Sum(file => TryGetSize(file, out long size) ? size : 0);

	/// <summary>
	/// Reads a file's size, tolerating a file that has disappeared or can no longer be reached.
	/// </summary>
	/// <param name="file">The file to size.</param>
	/// <param name="size">The size in bytes, when it could be read.</param>
	/// <returns><see langword="true"/> if the file is still there and its size was read.</returns>
	internal static bool TryGetSize(AbsoluteFilePath file, out long size)
	{
		try
		{
			size = new FileInfo(file.WeakString).Length;
			return true;
		}
		catch (IOException)
		{
			// FileNotFoundException and DirectoryNotFoundException both derive from this.
			size = 0;
			return false;
		}
		catch (UnauthorizedAccessException)
		{
			size = 0;
			return false;
		}
	}

	internal static AbsoluteFilePath SelectFileToKeep(List<AbsoluteFilePath> duplicates) =>
		duplicates.OrderBy(f => f.FileName.WeakString.Length).ThenBy(f => f.WeakString, StringComparer.Ordinal).First();

	/// <summary>
	/// Reports whether deduplication may delete copies from a group.
	/// </summary>
	/// <remarks>
	/// Every zero-length file hashes the same, so an empty <c>__init__.py</c>, a <c>.gitkeep</c> and a
	/// <c>py.typed</c> marker land in one group. Such files matter because they exist, not for what
	/// they hold, and deleting them frees nothing, so the group is still reported but never deleted.
	/// </remarks>
	/// <param name="group">The group to check.</param>
	/// <returns><see langword="true"/> unless the group's files are empty.</returns>
	internal static bool IsDeletable(DuplicateGroup group) => group.FileSize > 0;

	internal static DeduplicationResult DeleteDuplicates(IReadOnlyList<DuplicateGroup> duplicateGroups)
	{
		int deletedCount = 0;
		long bytesReclaimed = 0;
		List<string> errors = [];
		List<SkippedFile> skipped = [];

		foreach (DuplicateGroup group in duplicateGroups.Where(IsDeletable))
		{
			AbsoluteFilePath keeper = SelectFileToKeep(group.Files);

			// The grouping was computed before the confirmation prompt, which is an interactive
			// pause of unbounded length. If the copy being kept no longer holds the group's
			// content, deleting the others would destroy the only remaining copies of it, so the
			// whole group is left alone.
			if (!StillMatchesGroup(keeper, group.Hash, out string? keeperReason))
			{
				SkipWholeGroup(group, keeper, keeperReason, skipped);
				continue;
			}

			foreach (AbsoluteFilePath file in group.Files.Where(f => f != keeper))
			{
				// Re-hash immediately before deleting: a file that changed since the scan is no
				// longer a duplicate, and deleting it would be irreversible loss of content that
				// exists nowhere else.
				if (!StillMatchesGroup(file, group.Hash, out string? reason))
				{
					Skip(file, reason, skipped);
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
				// A copy the process is not allowed to remove -- read-only on Windows, or in a
				// write-protected directory on Unix -- must cost that one file, not the rest of the
				// run. Letting this escape would abandon every group after it, with no summary and
				// no report of what was already deleted.
				catch (UnauthorizedAccessException ex)
				{
					string error = $"  Error deleting {file}: {ex.Message}";
					errors.Add(error);
					Console.WriteLine(error);
				}
			}
		}

		return new DeduplicationResult(deletedCount, bytesReclaimed, errors, skipped);
	}

	/// <summary>
	/// Preserves every copy in a group, because the copy that would have been kept no longer
	/// holds the group's content.
	/// </summary>
	/// <param name="group">The group to leave on disk.</param>
	/// <param name="keeper">The copy that would have been kept.</param>
	/// <param name="keeperReason">What the keeper did, phrased to follow its name.</param>
	/// <param name="skipped">The list to record the preserved files on.</param>
	private static void SkipWholeGroup(DuplicateGroup group, AbsoluteFilePath keeper, string? keeperReason, List<SkippedFile> skipped)
	{
		foreach (AbsoluteFilePath file in group.Files.Where(f => f != keeper))
		{
			Skip(file, $"the copy being kept ({keeper}) {keeperReason}", skipped);
		}
	}

	/// <summary>
	/// Re-reads a file and reports whether it still holds the content its duplicate group was
	/// formed from.
	/// </summary>
	/// <param name="file">The file to re-hash.</param>
	/// <param name="groupHash">The hash the group was formed from.</param>
	/// <param name="reason">When the answer is no, why -- phrased to follow the file's name.</param>
	/// <returns><see langword="true"/> if the file still hashes to <paramref name="groupHash"/>.</returns>
	private static bool StillMatchesGroup(AbsoluteFilePath file, string groupHash, out string? reason)
	{
		try
		{
			if (string.Equals(FileHasher.ComputeHash(file), groupHash, StringComparison.Ordinal))
			{
				reason = null;
				return true;
			}

			reason = "changed since it was scanned, so it is no longer a duplicate";
			return false;
		}
		catch (IOException ex)
		{
			reason = $"could not be re-read to confirm it is still a duplicate: {ex.Message}";
			return false;
		}
		catch (UnauthorizedAccessException ex)
		{
			reason = $"could not be re-read to confirm it is still a duplicate: {ex.Message}";
			return false;
		}
	}

	private static void Skip(AbsoluteFilePath file, string? reason, List<SkippedFile> skipped)
	{
		SkippedFile skip = new(file, reason ?? "could not be confirmed as a duplicate");
		skipped.Add(skip);
		Console.WriteLine($"  Skipped: {file} -- {skip.Reason}");
	}
}

internal sealed class DuplicateGroup(string hash, List<AbsoluteFilePath> files, long fileSize)
{
	internal string Hash { get; } = hash;
	internal List<AbsoluteFilePath> Files { get; } = files;
	internal long FileSize { get; } = fileSize;
}

internal sealed class DeduplicationResult(int deletedCount, long bytesReclaimed, List<string> errors, List<SkippedFile> skippedFiles)
{
	internal int DeletedCount { get; } = deletedCount;
	internal long BytesReclaimed { get; } = bytesReclaimed;
	internal IReadOnlyList<string> Errors { get; } = errors;

	/// <summary>
	/// Gets the files that were proposed for deletion but left on disk because they could no
	/// longer be confirmed as duplicates.
	/// </summary>
	internal IReadOnlyList<SkippedFile> SkippedFiles { get; } = skippedFiles;
}

/// <summary>
/// A file that was preserved instead of deleted, and why.
/// </summary>
internal sealed class SkippedFile(AbsoluteFilePath path, string reason)
{
	internal AbsoluteFilePath Path { get; } = path;
	internal string Reason { get; } = reason;
}
