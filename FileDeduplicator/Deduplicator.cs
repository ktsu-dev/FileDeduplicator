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
	/// <para>
	/// Paths that lead to one file (see <see cref="FileIdentity"/>) are collapsed to the one
	/// <see cref="SelectFileToKeep"/> would prefer. They are one copy, not several, so a file the
	/// scan reached through a bind mount or a hardlink is not a duplicate of itself.
	/// </para>
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
			(AbsoluteFilePath File, long Size)[] present = [.. CollapseSameFile(kvp.Value)
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
	/// Keeps one path per file, preferring the path <see cref="SelectFileToKeep"/> would keep.
	/// </summary>
	/// <param name="paths">The paths in one hash group.</param>
	/// <returns>The paths, with every further path to an already-listed file left out.</returns>
	/// <remarks>
	/// A path whose identity cannot be read is kept: it is either gone, which the sizing that follows
	/// drops, or it cannot be inspected, and the deletion guard in <see cref="DeleteDuplicates(IReadOnlyList{DuplicateGroup}, string?, Action{AbsoluteFilePath}?)"/>
	/// still stands between it and the keeper.
	/// </remarks>
	internal static IEnumerable<AbsoluteFilePath> CollapseSameFile(IEnumerable<AbsoluteFilePath> paths)
	{
		HashSet<FileIdentity> seen = [];

		return OrderByPreference(paths)
			.Where(path => !FileIdentity.TryGet(path.WeakString, out FileIdentity identity) || seen.Add(identity));
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
		OrderByPreference(duplicates).First();

	private static IOrderedEnumerable<AbsoluteFilePath> OrderByPreference(IEnumerable<AbsoluteFilePath> files) =>
		files.OrderBy(f => f.FileName.WeakString.Length).ThenBy(f => f.WeakString, StringComparer.Ordinal);

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

	/// <summary>
	/// Resolves the scanned root with every link followed, for <see cref="DeleteDuplicates(IReadOnlyList{DuplicateGroup}, string?, Action{AbsoluteFilePath}?)"/>.
	/// </summary>
	/// <remarks>
	/// Called when the root is scanned, not when copies are deleted. The confirmation prompt between
	/// the two can stay open indefinitely, and a root swapped for a link in that time would otherwise
	/// resolve to the new place, so every path under it would still "lead inside the root" and the
	/// run would delete in a folder that was never scanned or listed (ktsu-dev/FileDeduplicator#206).
	/// </remarks>
	/// <param name="root">The directory being scanned.</param>
	/// <returns>The resolved root, or <see langword="null"/> when it could not be resolved.</returns>
	internal static string? ResolveRoot(AbsoluteDirectoryPath root) =>
		RealPath.TryResolve(root.WeakString, out string resolvedRoot) ? resolvedRoot : null;

	/// <summary>
	/// Deletes every copy in each deletable group except the one <see cref="SelectFileToKeep"/> keeps,
	/// resolving <paramref name="root"/> now.
	/// </summary>
	/// <param name="duplicateGroups">The groups to deduplicate.</param>
	/// <param name="root">The directory that was scanned.</param>
	/// <param name="afterDelete">Called after each copy is deleted.</param>
	/// <returns>What was deleted, skipped and failed.</returns>
	internal static DeduplicationResult DeleteDuplicates(IReadOnlyList<DuplicateGroup> duplicateGroups, AbsoluteDirectoryPath root, Action<AbsoluteFilePath>? afterDelete = null) =>
		DeleteDuplicates(duplicateGroups, ResolveRoot(root), afterDelete);

	/// <summary>
	/// Deletes every copy in each deletable group except the one <see cref="SelectFileToKeep"/> keeps.
	/// </summary>
	/// <param name="duplicateGroups">The groups to deduplicate.</param>
	/// <param name="realRoot">
	/// The directory that was scanned, as <see cref="ResolveRoot"/> resolved it when it was scanned,
	/// or <see langword="null"/> if it could not be. A copy whose path no longer leads inside it is
	/// not deleted, and a group whose kept copy no longer does is left alone.
	/// </param>
	/// <param name="afterDelete">
	/// Called after each copy is deleted. Production code passes nothing; tests use it to change the
	/// filesystem between two deletions in one group.
	/// </param>
	/// <returns>What was deleted, skipped and failed.</returns>
	internal static DeduplicationResult DeleteDuplicates(IReadOnlyList<DuplicateGroup> duplicateGroups, string? realRoot, Action<AbsoluteFilePath>? afterDelete = null)
	{
		int deletedCount = 0;
		long bytesReclaimed = 0;
		List<string> errors = [];
		List<SkippedFile> skipped = [];

		foreach (DuplicateGroup group in duplicateGroups.Where(IsDeletable))
		{
			AbsoluteFilePath keeper = SelectFileToKeep(group.Files);

			// The keeper is re-hashed, identified and snapshotted through its recorded path. If a
			// directory on that path now leads outside the root, all of those read an identical file
			// out there, every check passes, and the scanned folder is left with no copy of the
			// content at all, so the whole group is left alone.
			if (!LeadsInsideRoot(keeper, realRoot, out string? keeperOutsideReason))
			{
				SkipWholeGroup(group, keeper, keeperOutsideReason, skipped);
				continue;
			}

			// The grouping was computed before the confirmation prompt, which is an interactive
			// pause of unbounded length. If the copy being kept no longer holds the group's
			// content, deleting the others would destroy the only remaining copies of it, so the
			// whole group is left alone.
			if (!StillMatchesGroup(keeper, group.Hash, out string? keeperReason))
			{
				SkipWholeGroup(group, keeper, keeperReason, skipped);
				continue;
			}

			bool keeperIdentified = FileIdentity.TryGet(keeper.WeakString, out FileIdentity keeperIdentity);

			// Deleting the other copies can take seconds apiece for large files, and the keeper can
			// be rewritten by a sync client or an editor while that runs. Once it no longer holds the
			// group's content, every copy still to be deleted is the last one left, so the keeper is
			// checked cheaply before each deletion and the rest of the group is kept if it moved.
			KeeperSnapshot? keeperSnapshot = KeeperSnapshot.TryTake(keeper);
			bool deletedFromGroup = false;
			List<AbsoluteFilePath> others = [.. group.Files.Where(f => f != keeper)];

			for (int i = 0; i < others.Count; i++)
			{
				AbsoluteFilePath file = others[i];

				if (keeperSnapshot is null || !keeperSnapshot.StillMatches(keeper))
				{
					foreach (AbsoluteFilePath remaining in others.Skip(i))
					{
						Skip(remaining, $"the copy being kept ({keeper}) changed while the group was being deduplicated, so this may be the last copy of the content", skipped);
					}

					break;
				}

				if (!LeadsInsideRoot(keeper, realRoot, out string? keeperMovedReason))
				{
					foreach (AbsoluteFilePath remaining in others.Skip(i))
					{
						Skip(remaining, $"the copy being kept ({keeper}) {keeperMovedReason}, so this may be the last copy of the content in it", skipped);
					}

					break;
				}

				// Re-hash immediately before deleting: a file that changed since the scan is no
				// longer a duplicate, and deleting it would be irreversible loss of content that
				// exists nowhere else.
				if (!StillMatchesGroup(file, group.Hash, out string? reason))
				{
					Skip(file, reason, skipped);
					continue;
				}

				// A path that leads to the keeper's own file is not another copy. Through a bind
				// mount, deleting it deletes the keeper, and the re-hash above cannot tell, because
				// the keeper is the file being deleted. FindDuplicates already collapses such paths,
				// so this is the last guard, against whatever the grouping was handed.
				if (keeperIdentified && IsSameFile(file, keeperIdentity))
				{
					Skip(file, $"is the same file as the copy being kept ({keeper}), reached through another path", skipped);
					continue;
				}

				// The path was recorded at scan time, when no directory along it under the root was a
				// link (the scan does not follow them). A directory replaced by a link since then --
				// during the confirmation prompt, which can stay open indefinitely -- would send the
				// delete to an identical file outside the tree the user approved. Checked last,
				// immediately before the delete, to keep that window as short as it can be.
				if (!LeadsInsideRoot(file, realRoot, out string? outsideReason))
				{
					Skip(file, outsideReason, skipped);
					continue;
				}

				if (TryDelete(file, errors, out long fileSize))
				{
					deletedCount++;
					bytesReclaimed += fileSize;
					deletedFromGroup = true;
					afterDelete?.Invoke(file);
				}
			}

			// The snapshot cannot see a rewrite that keeps the size and lands within the timestamp's
			// resolution, so the keeper is re-hashed once after the group's last deletion. The
			// deletions cannot be undone by then, but the user is told the content may be gone.
			if (deletedFromGroup && !StillMatchesGroup(keeper, group.Hash, out string? finalReason))
			{
				string error = $"  Error: the copy kept for this group ({keeper}) {finalReason}; the deleted copies held content it no longer has.";
				errors.Add(error);
				Console.WriteLine(error);
			}
		}

		return new DeduplicationResult(deletedCount, bytesReclaimed, errors, skipped);
	}

	/// <summary>
	/// Reports whether a recorded path still leads to a file inside the scanned root.
	/// </summary>
	/// <param name="file">The path recorded at scan time.</param>
	/// <param name="realRoot">The scanned root with every link resolved, or <see langword="null"/> if it could not be.</param>
	/// <param name="reason">When the answer is no, why -- phrased to follow the file's name.</param>
	/// <returns><see langword="true"/> if the file's directory resolves to the root or beneath it.</returns>
	private static bool LeadsInsideRoot(AbsoluteFilePath file, string? realRoot, out string? reason)
	{
		string directory = Path.GetDirectoryName(file.WeakString)!;

		if (realRoot is null || !RealPath.TryResolve(directory, out string realDirectory))
		{
			reason = "could not be resolved to confirm it is still inside the scanned folder";
			return false;
		}

		if (!RealPath.IsWithin(realDirectory, realRoot))
		{
			reason = $"no longer leads inside the scanned folder: its directory now resolves to {realDirectory}";
			return false;
		}

		reason = null;
		return true;
	}

	/// <summary>
	/// Deletes one confirmed duplicate, recording a failure instead of letting it end the run.
	/// </summary>
	/// <param name="file">The copy to delete.</param>
	/// <param name="errors">The list to record a failure on.</param>
	/// <param name="fileSize">The size of the deleted copy, when it was deleted.</param>
	/// <returns><see langword="true"/> if the copy was deleted.</returns>
	private static bool TryDelete(AbsoluteFilePath file, List<string> errors, out long fileSize)
	{
		try
		{
			fileSize = new FileInfo(file.WeakString).Length;
			File.Delete(file.WeakString);
			Console.WriteLine($"  Deleted: {file}");
			return true;
		}
		catch (IOException ex)
		{
			RecordDeleteError(file, ex, errors);
		}
		// A copy the process is not allowed to remove -- read-only on Windows, or in a
		// write-protected directory on Unix -- must cost that one file, not the rest of the
		// run. Letting this escape would abandon every group after it, with no summary and
		// no report of what was already deleted.
		catch (UnauthorizedAccessException ex)
		{
			RecordDeleteError(file, ex, errors);
		}

		fileSize = 0;
		return false;
	}

	private static void RecordDeleteError(AbsoluteFilePath file, Exception ex, List<string> errors)
	{
		string error = $"  Error deleting {file}: {ex.Message}";
		errors.Add(error);
		Console.WriteLine(error);
	}

	/// <summary>
	/// Reports whether a path leads to the file with the given identity.
	/// </summary>
	/// <param name="file">The path to check.</param>
	/// <param name="identity">The identity to compare against.</param>
	/// <returns><see langword="true"/> only when the path's identity was read and matches.</returns>
	private static bool IsSameFile(AbsoluteFilePath file, FileIdentity identity) =>
		FileIdentity.TryGet(file.WeakString, out FileIdentity fileIdentity) && fileIdentity == identity;

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

/// <summary>
/// The size, last write time and identity of the copy being kept, taken once its content has been
/// confirmed, so it can be checked cheaply between deletions without re-hashing it each time.
/// </summary>
internal sealed class KeeperSnapshot
{
	private readonly long length;
	private readonly DateTime lastWriteTimeUtc;
	private readonly FileIdentity? identity;

	private KeeperSnapshot(long length, DateTime lastWriteTimeUtc, FileIdentity? identity)
	{
		this.length = length;
		this.lastWriteTimeUtc = lastWriteTimeUtc;
		this.identity = identity;
	}

	/// <summary>
	/// Records the keeper's current state.
	/// </summary>
	/// <param name="keeper">The copy being kept.</param>
	/// <returns>The snapshot, or <see langword="null"/> if the keeper could not be read.</returns>
	internal static KeeperSnapshot? TryTake(AbsoluteFilePath keeper) =>
		TryRead(keeper, out long length, out DateTime lastWrite, out FileIdentity? identity)
			? new KeeperSnapshot(length, lastWrite, identity)
			: null;

	/// <summary>
	/// Reports whether the keeper still looks exactly as it did when the snapshot was taken.
	/// </summary>
	/// <param name="keeper">The copy being kept.</param>
	/// <returns><see langword="true"/> if its size, last write time and identity are unchanged.</returns>
	internal bool StillMatches(AbsoluteFilePath keeper) =>
		TryRead(keeper, out long currentLength, out DateTime currentLastWrite, out FileIdentity? currentIdentity)
		&& currentLength == length
		&& currentLastWrite == lastWriteTimeUtc
		&& currentIdentity == identity;

	private static bool TryRead(AbsoluteFilePath keeper, out long length, out DateTime lastWriteTimeUtc, out FileIdentity? identity)
	{
		try
		{
			FileInfo info = new(keeper.WeakString);
			length = info.Length;
			lastWriteTimeUtc = info.LastWriteTimeUtc;
			identity = FileIdentity.TryGet(keeper.WeakString, out FileIdentity id) ? id : null;
			return true;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			// A keeper that is gone or can no longer be read does not match the snapshot.
			length = 0;
			lastWriteTimeUtc = default;
			identity = null;
			return false;
		}
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
