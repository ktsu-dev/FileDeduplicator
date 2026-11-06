// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.FileDeduplicator.Test;

using ktsu.Semantics.Paths;
using ktsu.Semantics.Strings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Deletion goes through path strings recorded at scan time. These tests check that a path which
/// no longer leads inside the scanned folder is never deleted through.
/// </summary>
[TestClass]
public sealed class ScannedRootContainmentTests
{
	/// <summary>
	/// A subdirectory replaced by a link to a folder outside the root during the confirmation
	/// prompt must not let the delete reach an identical file out there.
	/// </summary>
	[TestMethod]
	public void ASubdirectorySwappedForALinkOutsideTheRootIsNotDeletedThrough()
	{
		// Arrange -- a.cfg is kept, sub/settings.cfg is up for deletion
		using TempTree tree = new();
		using TempTree outside = new();
		AbsoluteFilePath keeper = tree.Write("a.cfg", "important config v1");
		AbsoluteFilePath copy = tree.Write("sub/settings.cfg", "important config v1");
		AbsoluteFilePath outsideFile = outside.Write("settings.cfg", "important config v1");
		IReadOnlyList<DuplicateGroup> duplicates = Deduplicator.FindDuplicates(Deduplicator.GroupByHash(FileHasher.HashFiles([keeper, copy])));

		// Arrange -- while the prompt is open, sub/ becomes a link to the outside folder
		string sub = Path.Combine(tree.Root.WeakString, "sub");
		Directory.Delete(sub, recursive: true);
		CreateDirectoryLinkOrSkip(sub, outside.Root.WeakString);

		// Act
		DeduplicationResult result = Deduplicator.DeleteDuplicates(duplicates, tree.Root);

		// Assert
		Assert.IsTrue(TempTree.Exists(outsideFile), "A file outside the scanned folder must never be deleted.");
		Assert.IsTrue(TempTree.Exists(keeper), "The keeper must survive.");
		Assert.AreEqual(0, result.DeletedCount);
		Assert.ContainsSingle(result.SkippedFiles);
		Assert.AreEqual(copy, result.SkippedFiles[0].Path);
		Assert.Contains("scanned folder", result.SkippedFiles[0].Reason);
	}

	/// <summary>
	/// The kept copy's directory replaced by a link to a folder outside the root, holding an
	/// identical file, must not let the other copies go: the scanned folder would be left with none
	/// (ktsu-dev/FileDeduplicator#206).
	/// </summary>
	[TestMethod]
	public void AKeeperWhoseDirectoryIsSwappedForALinkOutsideTheRootKeepsTheWholeGroup()
	{
		// Arrange -- sub/a.cfg is kept (shortest name), settings.cfg is up for deletion
		using TempTree tree = new();
		using TempTree outside = new();
		AbsoluteFilePath keeper = tree.Write("sub/a.cfg", "important config v1");
		AbsoluteFilePath copy = tree.Write("settings.cfg", "important config v1");
		AbsoluteFilePath outsideFile = outside.Write("a.cfg", "important config v1");
		IReadOnlyList<DuplicateGroup> duplicates = Deduplicator.FindDuplicates(Deduplicator.GroupByHash(FileHasher.HashFiles([keeper, copy])));
		string? realRoot = Deduplicator.ResolveRoot(tree.Root);

		// Arrange -- while the prompt is open, sub/ becomes a link to the outside folder
		string sub = Path.Combine(tree.Root.WeakString, "sub");
		Directory.Delete(sub, recursive: true);
		CreateDirectoryLinkOrSkip(sub, outside.Root.WeakString);

		// Act
		DeduplicationResult result = Deduplicator.DeleteDuplicates(duplicates, realRoot);

		// Assert
		Assert.IsTrue(TempTree.Exists(copy), "The only copy left inside the scanned folder was deleted.");
		Assert.IsTrue(TempTree.Exists(outsideFile), "A file outside the scanned folder must never be deleted.");
		Assert.AreEqual(0, result.DeletedCount);
		Assert.ContainsSingle(result.SkippedFiles);
		Assert.AreEqual(copy, result.SkippedFiles[0].Path);
		Assert.Contains("copy being kept", result.SkippedFiles[0].Reason);
		Assert.Contains("scanned folder", result.SkippedFiles[0].Reason);
	}

	/// <summary>
	/// The root itself replaced by a link to a sibling folder after the scan must not send the
	/// deletes into that sibling, which was never scanned or listed (ktsu-dev/FileDeduplicator#206).
	/// </summary>
	[TestMethod]
	public void ARootSwappedForALinkAfterTheScanDeletesNothing()
	{
		// Arrange -- parent/photos is scanned; parent/backup holds the same names and content
		using TempTree parent = new();
		AbsoluteFilePath keeper = parent.Write("photos/a.jpg", "pixels");
		AbsoluteFilePath copy = parent.Write("photos/longer.jpg", "pixels");
		AbsoluteFilePath backupKeeper = parent.Write("backup/a.jpg", "pixels");
		AbsoluteFilePath backupCopy = parent.Write("backup/longer.jpg", "pixels");
		string photos = Path.Combine(parent.Root.WeakString, "photos");
		AbsoluteDirectoryPath root = photos.As<AbsoluteDirectoryPath>();
		IReadOnlyList<DuplicateGroup> duplicates = Deduplicator.FindDuplicates(Deduplicator.GroupByHash(FileHasher.HashFiles([keeper, copy])));
		string? realRoot = Deduplicator.ResolveRoot(root);

		// Arrange -- while the prompt is open, photos is moved aside and replaced by a link to backup
		Directory.Move(photos, Path.Combine(parent.Root.WeakString, "photos-moved"));
		CreateDirectoryLinkOrSkip(photos, Path.Combine(parent.Root.WeakString, "backup"));

		// Act
		DeduplicationResult result = Deduplicator.DeleteDuplicates(duplicates, realRoot);

		// Assert
		Assert.IsTrue(TempTree.Exists(backupCopy), "A file in a folder that was never scanned was deleted.");
		Assert.IsTrue(TempTree.Exists(backupKeeper));
		Assert.AreEqual(0, result.DeletedCount);
		Assert.ContainsSingle(result.SkippedFiles);
		Assert.Contains("scanned folder", result.SkippedFiles[0].Reason);
	}

	/// <summary>
	/// The root itself may be reached through a link, as when a user passes a symlinked home
	/// folder. That alone must not stop copies under it from being deleted.
	/// </summary>
	[TestMethod]
	public void ARootReachedThroughALinkStillDeletesItsDuplicates()
	{
		// Arrange
		using TempTree tree = new();
		using TempTree links = new();
		string linkedRoot = Path.Combine(links.Root.WeakString, "linked-root");
		CreateDirectoryLinkOrSkip(linkedRoot, tree.Root.WeakString);
		AbsoluteDirectoryPath root = linkedRoot.As<AbsoluteDirectoryPath>();
		AbsoluteFilePath keeper = Path.Combine(linkedRoot, "a.txt").As<AbsoluteFilePath>();
		AbsoluteFilePath copy = Path.Combine(linkedRoot, "nested", "bb.txt").As<AbsoluteFilePath>();
		_ = tree.Write("a.txt", "shared");
		_ = tree.Write("nested/bb.txt", "shared");
		IReadOnlyList<DuplicateGroup> duplicates = Deduplicator.FindDuplicates(Deduplicator.GroupByHash(FileHasher.HashFiles([keeper, copy])));

		// Act
		DeduplicationResult result = Deduplicator.DeleteDuplicates(duplicates, root);

		// Assert
		Assert.AreEqual(1, result.DeletedCount);
		Assert.IsEmpty(result.SkippedFiles);
		Assert.IsFalse(TempTree.Exists(copy), "A copy inside the root should still be deleted.");
	}

	/// <summary>
	/// Resolution follows a relative link and a link inside a link's target.
	/// </summary>
	[TestMethod]
	public void RealPathFollowsNestedRelativeLinks()
	{
		// Arrange -- first -> second (relative), second -> target (absolute)
		using TempTree tree = new();
		_ = tree.Write("target/file.txt", "x");
		string target = Path.Combine(tree.Root.WeakString, "target");
		string second = Path.Combine(tree.Root.WeakString, "second");
		string first = Path.Combine(tree.Root.WeakString, "first");
		CreateDirectoryLinkOrSkip(second, target);
		CreateDirectoryLinkOrSkip(first, "second");
		Assert.IsTrue(RealPath.TryResolve(tree.Root.WeakString, out string realRoot));

		// Act
		bool resolved = RealPath.TryResolve(Path.Combine(first, "file.txt"), out string real);

		// Assert
		Assert.IsTrue(resolved);
		Assert.AreEqual(Path.Combine(realRoot, "target", "file.txt"), real);
		Assert.IsTrue(RealPath.IsWithin(real, realRoot));
		Assert.IsFalse(RealPath.IsWithin(realRoot + "-sibling", realRoot), "A sibling sharing the root's name as a prefix is not inside it.");
	}

	/// <summary>
	/// A link loop never resolves, so a scanned root reached through one leaves every copy on disk
	/// with a reason, rather than deleting on an unchecked path.
	/// </summary>
	[TestMethod]
	public void AnUnresolvableRootSkipsEveryCopy()
	{
		// Arrange -- loop-a -> loop-b -> loop-a
		using TempTree tree = new();
		AbsoluteFilePath keeper = tree.Write("a.txt", "shared");
		AbsoluteFilePath copy = tree.Write("bb.txt", "shared");
		IReadOnlyList<DuplicateGroup> duplicates = Deduplicator.FindDuplicates(Deduplicator.GroupByHash(FileHasher.HashFiles([keeper, copy])));
		string loopA = Path.Combine(tree.Root.WeakString, "loop-a");
		string loopB = Path.Combine(tree.Root.WeakString, "loop-b");
		CreateDirectoryLinkOrSkip(loopA, loopB);
		CreateDirectoryLinkOrSkip(loopB, loopA);

		// Act
		DeduplicationResult result = Deduplicator.DeleteDuplicates(duplicates, loopA.As<AbsoluteDirectoryPath>());

		// Assert
		Assert.AreEqual(0, result.DeletedCount);
		Assert.IsTrue(TempTree.Exists(copy), "A copy must not be deleted when the root cannot be resolved.");
		Assert.ContainsSingle(result.SkippedFiles);
		Assert.Contains("could not be resolved", result.SkippedFiles[0].Reason);
	}

	/// <summary>
	/// Resolution refuses a relative path, gives up on a link loop, and handles "." and ".."
	/// segments without leaving the real directory tree.
	/// </summary>
	[TestMethod]
	public void RealPathHandlesDotSegmentsLoopsAndRelativeInput()
	{
		// Arrange
		using TempTree tree = new();
		_ = tree.Write("dir/file.txt", "x");
		Assert.IsTrue(RealPath.TryResolve(tree.Root.WeakString, out string realRoot));
		string loopA = Path.Combine(tree.Root.WeakString, "loop-a");
		string loopB = Path.Combine(tree.Root.WeakString, "loop-b");
		CreateDirectoryLinkOrSkip(loopA, loopB);
		CreateDirectoryLinkOrSkip(loopB, loopA);
		string dotted = tree.Root.WeakString + $"{Path.DirectorySeparatorChar}.{Path.DirectorySeparatorChar}dir{Path.DirectorySeparatorChar}..{Path.DirectorySeparatorChar}dir{Path.DirectorySeparatorChar}file.txt";

		// Act + Assert
		Assert.IsFalse(RealPath.TryResolve(Path.Combine("relative", "path"), out _), "A relative path has no single meaning to resolve.");
		Assert.IsFalse(RealPath.TryResolve(Path.Combine(loopA, "file.txt"), out _), "A link loop never resolves.");
		Assert.IsTrue(RealPath.TryResolve(dotted, out string real));
		Assert.AreEqual(Path.Combine(realRoot, "dir", "file.txt"), real);
	}

	/// <summary>
	/// A file-system root contains everything on it, including paths that do not repeat its
	/// trailing separator.
	/// </summary>
	[TestMethod]
	public void EverythingIsWithinAFileSystemRoot()
	{
		// Arrange
		string root = Path.GetPathRoot(Path.GetTempPath())!;

		// Act + Assert
		Assert.IsTrue(RealPath.IsWithin(Path.Combine(root, "any", "where"), root));
		Assert.IsTrue(RealPath.IsWithin(root, root));
	}

	private static void CreateDirectoryLinkOrSkip(string path, string target)
	{
		try
		{
			_ = Directory.CreateSymbolicLink(path, target);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			// Windows without Developer Mode or elevation refuses to create symbolic links.
			Assert.Inconclusive($"Cannot create a symbolic link here: {ex.Message}");
		}
	}
}
