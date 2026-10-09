// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.FileDeduplicator.Test;

using ktsu.FileDeduplicator.Verbs;
using ktsu.Semantics.Paths;

using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Tests what the read-only verbs report, and that they stay read-only.
/// </summary>
/// <remarks>
/// Scan, DryRun and Stats exist to be read before anything is deleted, so what they print is their
/// entire contract -- and Deduplicate's listing is now built from the same helper DryRun uses, which
/// makes "DryRun still says what it used to" something worth holding still rather than assuming.
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class VerbOutputTests
{
	/// <summary>
	/// Writes a tree with one group of three copies and one unique file.
	/// </summary>
	/// <param name="tree">The tree to write into.</param>
	/// <param name="keeper">The copy the shortest-name policy will keep.</param>
	/// <param name="doomed">The copies it will not.</param>
	private static void WriteOneGroup(TempTree tree, out AbsoluteFilePath keeper, out List<AbsoluteFilePath> doomed)
	{
		keeper = tree.Write("a.txt", "alpha");
		doomed =
		[
			tree.Write("aa.txt", "alpha"),
			tree.Write("nested/aaa.txt", "alpha"),
		];
		_ = tree.Write("unique.txt", "beta");
	}

	/// <summary>
	/// Every verb given a file instead of a directory says so in one line and deletes nothing.
	/// </summary>
	[TestMethod]
	public void EveryVerbReportsAFilePathAsNotADirectory()
	{
		// Arrange
		using TempTree tree = new();
		AbsoluteFilePath file = tree.Write("a/f.txt", "content");
		string path = file.WeakString;

		// Act
		string[] outputs =
		[
			ConsoleCapture.Run(new Scan { PathString = path }),
			ConsoleCapture.Run(new DryRun { PathString = path }),
			ConsoleCapture.Run(new Stats { PathString = path }),
			ConsoleCapture.Run(new Deduplicate { PathString = path }, "y"),
		];

		// Assert
		foreach (string output in outputs)
		{
			Assert.Contains($"Not a directory: {path}", output);
		}

		Assert.IsTrue(TempTree.Exists(file), "The file named as the root must be left alone.");
	}

	/// <summary>
	/// DryRun names the keeper and every other copy, totals them, and deletes nothing.
	/// </summary>
	[TestMethod]
	public void DryRunListsTheGroupAndLeavesEveryFileOnDisk()
	{
		// Arrange
		using TempTree tree = new();
		WriteOneGroup(tree, out AbsoluteFilePath keeper, out List<AbsoluteFilePath> doomed);

		// Act
		string output = ConsoleCapture.Normalize(ConsoleCapture.Run(new DryRun { PathString = tree.Root.WeakString }));

		// Assert
		Assert.Contains("Found 1 group(s) of duplicate files:", output);
		Assert.Contains($"KEEP: {keeper}", output);
		Assert.Contains("--- Dry Run Summary ---", output);
		Assert.Contains("Duplicate groups: 1", output);
		Assert.Contains("Files to delete: 2", output);
		Assert.Contains("Space to reclaim: 10 B", output);

		foreach (AbsoluteFilePath file in doomed)
		{
			Assert.Contains($"DELETE: {file}", output);
			Assert.IsTrue(TempTree.Exists(file), $"DryRun deleted {file}.");
		}

		Assert.IsTrue(TempTree.Exists(keeper), $"DryRun deleted {keeper}.");
	}

	/// <summary>
	/// Scan marks each copy in a group, and points at the verb that acts on them.
	/// </summary>
	[TestMethod]
	public void ScanMarksEveryCopyKeepOrDelete()
	{
		// Arrange
		using TempTree tree = new();
		WriteOneGroup(tree, out AbsoluteFilePath keeper, out List<AbsoluteFilePath> doomed);

		// Act
		string output = ConsoleCapture.Normalize(ConsoleCapture.Run(new Scan { PathString = tree.Root.WeakString }));

		// Assert
		Assert.Contains($"{keeper} [KEEP]", output);
		Assert.Contains("Total duplicate groups: 1", output);
		Assert.Contains("Total wasted space: 10 B", output);
		Assert.Contains("Run the 'Deduplicate' command to remove duplicates.", output);

		foreach (AbsoluteFilePath file in doomed)
		{
			Assert.Contains($"{file} [DELETE]", output);
			Assert.IsTrue(TempTree.Exists(file), $"Scan deleted {file}.");
		}
	}

	/// <summary>
	/// A tree where no two files are the same size has no duplicates, so nothing in it is read.
	/// </summary>
	[TestMethod]
	public void ScanHashesNothingWhenEveryFileHasItsOwnSize()
	{
		// Arrange
		using TempTree tree = new();
		_ = tree.Write("a.txt", "a");
		_ = tree.Write("b.txt", "bb");
		_ = tree.Write("nested/c.txt", "ccc");

		// Act
		string output = ConsoleCapture.Normalize(ConsoleCapture.Run(new Scan { PathString = tree.Root.WeakString }));

		// Assert
		Assert.DoesNotContain("Hashed:", output);
		Assert.Contains("No duplicate files found.", output);
	}

	/// <summary>
	/// Stats still counts and sizes the files it skipped hashing because their size is unique.
	/// </summary>
	[TestMethod]
	public void StatsCountsFilesWithAUniqueSizeAsUnique()
	{
		// Arrange
		using TempTree tree = new();
		_ = tree.Write("a.txt", "a");
		_ = tree.Write("b.txt", "bb");
		_ = tree.Write("nested/c.txt", "ccc");

		// Act
		string output = ConsoleCapture.Normalize(ConsoleCapture.Run(new Stats { PathString = tree.Root.WeakString }));

		// Assert
		Assert.DoesNotContain("Hashed:", output);
		Assert.Contains("Total files: 3", output);
		Assert.DoesNotContain("Unreadable files:", output);
		Assert.Contains("Total size: 6 B", output);
		Assert.Contains("Unique files: 3", output);
		Assert.Contains("Duplicate files: 0", output);
		Assert.Contains("Duplicate groups: 0", output);
	}

	/// <summary>
	/// Stats counts the tree and breaks the duplicates down without touching anything.
	/// </summary>
	[TestMethod]
	public void StatsReportsTotalsForTheTree()
	{
		// Arrange
		using TempTree tree = new();
		WriteOneGroup(tree, out AbsoluteFilePath keeper, out List<AbsoluteFilePath> doomed);

		// Act
		string output = ConsoleCapture.Normalize(ConsoleCapture.Run(new Stats { PathString = tree.Root.WeakString }));

		// Assert -- three 5-byte copies plus a 4-byte unique file, so 19 B held and 10 B wasted
		Assert.Contains("Total files: 4", output);
		Assert.Contains("Total size: 19 B", output);
		Assert.Contains("Unique files: 2", output);
		Assert.Contains("Duplicate files: 2", output);
		Assert.Contains("Duplicate groups: 1", output);
		Assert.Contains("Wasted space: 10 B", output);
		Assert.Contains("Duplicate files by extension:", output);
		Assert.Contains(".txt: 2 file(s)", output);
		Assert.Contains("Largest duplicate groups (by wasted space):", output);
		Assert.Contains("3 copies, 5 B each, 10 B wasted", output);

		Assert.IsTrue(TempTree.Exists(keeper), $"Stats deleted {keeper}.");
		Assert.IsTrue(doomed.TrueForAll(TempTree.Exists), "Stats deleted a file.");
	}

	/// <summary>
	/// A file Stats cannot hash is reported as unreadable, not counted as a duplicate or in the size.
	/// </summary>
	/// <remarks>
	/// Held open with <see cref="FileShare.None"/> rather than stripped of its permissions, because a
	/// process running as root reads through permissions but .NET enforces the share lock on every
	/// platform. Before ktsu-dev/FileDeduplicator#137 this tree reported one duplicate file and zero
	/// duplicate groups.
	/// </remarks>
	[TestMethod]
	public void StatsReportsAFileItCannotHashAsUnreadable()
	{
		// Arrange
		using TempTree tree = new();
		_ = tree.Write("a.txt", "alpha");
		_ = tree.Write("b.txt", "beta");
		AbsoluteFilePath locked = tree.Write("locked.txt", "gamma");
		using FileStream holder = new(locked.WeakString, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

		// Act
		string output = ConsoleCapture.Normalize(ConsoleCapture.Run(new Stats { PathString = tree.Root.WeakString }));

		// Assert -- the two readable files are 9 B and unique
		Assert.Contains("Total files: 3", output);
		Assert.Contains("Unreadable files: 1", output);
		Assert.Contains("Total size: 9 B", output);
		Assert.Contains("Unique files: 2", output);
		Assert.Contains("Duplicate files: 0", output);
		Assert.Contains("Duplicate groups: 0", output);
	}

	/// <summary>
	/// The extension breakdown counts each redundant copy under its own extension, leaves out the
	/// copy that is kept, and so sums to "Duplicate files" and reads the same on every run.
	/// </summary>
	[TestMethod]
	public void StatsBreaksDuplicatesDownByEachCopysOwnExtension()
	{
		// Arrange -- the keeper is photo.jpg, the shortest name
		using TempTree tree = new();
		_ = tree.Write("photo.jpg", "same picture");
		_ = tree.Write("photo-copy.png", "same picture");
		_ = tree.Write("backup.bak", "same picture");

		// Act
		string[] outputs = [.. Enumerable.Range(0, 3).Select(_ => ConsoleCapture.Normalize(ConsoleCapture.Run(new Stats { PathString = tree.Root.WeakString })))];

		// Assert
		foreach (string output in outputs)
		{
			Assert.Contains("Duplicate files: 2", output);
			Assert.Contains("Duplicate files by extension:\n.bak: 1 file(s)\n.png: 1 file(s)\n", output);
			Assert.DoesNotContain(".jpg:", output);
		}
	}

	/// <summary>
	/// Empty files are never deleted, so Stats does not count them as duplicates or wasted copies and
	/// agrees with DryRun on how many files a run would remove (ktsu-dev/FileDeduplicator#172).
	/// </summary>
	[TestMethod]
	public void StatsLeavesEmptyFilesOutOfTheDuplicates()
	{
		// Arrange -- the tree from the issue: four empty files and one 2-byte pair
		using TempTree tree = new();
		_ = tree.Write("a/__init__.py", string.Empty);
		_ = tree.Write("b/__init__.py", string.Empty);
		_ = tree.Write("c/__init__.py", string.Empty);
		_ = tree.Write(".gitkeep", string.Empty);
		_ = tree.Write("A.JPG", "xy");
		_ = tree.Write("b/copy.jpg", "xy");

		// Act
		string output = ConsoleCapture.Normalize(ConsoleCapture.Run(new Stats { PathString = tree.Root.WeakString }));
		string dryRun = ConsoleCapture.Normalize(ConsoleCapture.Run(new DryRun { PathString = tree.Root.WeakString }));

		// Assert
		Assert.Contains("Files to delete: 1", dryRun);
		Assert.Contains("Duplicate files: 1", output);
		Assert.Contains("Duplicate groups: 1", output);
		Assert.Contains("Empty duplicates (never deleted): 3", output);
		Assert.Contains("Duplicate files by extension:\n.jpg: 1 file(s)\n", output);
		Assert.DoesNotContain(".py:", output);
		Assert.DoesNotContain("0 B each", output, "An empty group was listed among the largest groups.");
	}

	/// <summary>
	/// <c>.jpg</c> and <c>.JPG</c> copies are one kind of file and are counted under one extension.
	/// </summary>
	[TestMethod]
	public void StatsCountsExtensionsThatDifferOnlyInCaseTogether()
	{
		// Arrange -- the keepers are the shorter names a.jpg, b.jpg and c.JPG
		using TempTree tree = new();
		_ = tree.Write("a.jpg", "first");
		_ = tree.Write("a2.JPG", "first");
		_ = tree.Write("b.jpg", "second");
		_ = tree.Write("b2.jpg", "second");
		_ = tree.Write("c.JPG", "third");
		_ = tree.Write("c2.JPG", "third");

		// Act
		string output = ConsoleCapture.Normalize(ConsoleCapture.Run(new Stats { PathString = tree.Root.WeakString }));

		// Assert
		Assert.Contains("Duplicate files by extension:\n.jpg: 3 file(s)\n", output);
		Assert.DoesNotContain(".JPG:", output);
	}

	/// <summary>
	/// A file that cannot be hashed is named by its full path, so the failing copy can be found
	/// among others sharing its file name.
	/// </summary>
	[TestMethod]
	public void AHashingErrorNamesTheFullPath()
	{
		// Arrange -- the same length as its sibling, or it would never be read
		using TempTree tree = new();
		_ = tree.Write("one/IMG_0001.jpg", "alpha");
		AbsoluteFilePath locked = tree.Write("two/IMG_0001.jpg", "gamma");
		using FileStream holder = new(locked.WeakString, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

		// Act
		string output = ConsoleCapture.Run(new Stats { PathString = tree.Root.WeakString });

		// Assert
		Assert.Contains($"Error hashing {locked}:", output);
	}

	/// <summary>
	/// A tree where every file hashes has no unreadable line at all.
	/// </summary>
	[TestMethod]
	public void StatsOmitsTheUnreadableLineWhenEveryFileHashes()
	{
		// Arrange
		using TempTree tree = new();
		WriteOneGroup(tree, out _, out _);

		// Act
		string output = ConsoleCapture.Normalize(ConsoleCapture.Run(new Stats { PathString = tree.Root.WeakString }));

		// Assert
		Assert.DoesNotContain("Unreadable files:", output);
	}

	/// <summary>
	/// A tree with nothing duplicated says so, in every verb that looks for duplicates.
	/// </summary>
	[TestMethod]
	public void NoDuplicatesIsReportedByEveryVerb()
	{
		// Arrange
		using TempTree tree = new();
		_ = tree.Write("a.txt", "alpha");
		_ = tree.Write("b.txt", "beta");

		// Act
		string scan = ConsoleCapture.Run(new Scan { PathString = tree.Root.WeakString });
		string dryRun = ConsoleCapture.Run(new DryRun { PathString = tree.Root.WeakString });
		string deduplicate = ConsoleCapture.Run(new Deduplicate { PathString = tree.Root.WeakString }, "y");

		// Assert
		Assert.Contains("No duplicate files found.", scan);
		Assert.Contains("No duplicate files found.", dryRun);
		Assert.Contains("No duplicate files found.", deduplicate);
		Assert.DoesNotContain("Proceed with deletion?", deduplicate, "Nothing was found, so nothing should have been asked.");
	}

	/// <summary>
	/// An empty directory stops before hashing, rather than reporting on nothing.
	/// </summary>
	[TestMethod]
	public void AnEmptyDirectoryStopsAfterDiscovery()
	{
		// Arrange
		using TempTree tree = new();

		// Act
		string output = ConsoleCapture.Run(new Scan { PathString = tree.Root.WeakString });

		// Assert
		Assert.Contains("Found 0 file(s).", output);
		Assert.DoesNotContain("Hashing files...", output);
	}

	/// <summary>
	/// A file the path type cannot represent is named and skipped, and every verb still reports the
	/// duplicates around it instead of crashing (ktsu-dev/FileDeduplicator#140).
	/// </summary>
	[TestMethod]
	public void EveryVerbSkipsAnUnrepresentableFileAndReportsTheRest()
	{
		// Arrange
		using TempTree tree = new();
		_ = tree.Write("a.txt", "alpha");
		AbsoluteFilePath copy = tree.Write("aa.txt", "alpha");
		FileScannerAndHasherTests.WriteUnrepresentable(tree, "notes <draft>.txt");
		string path = tree.Root.WeakString;

		// Act
		(string Output, int ExitCode)[] runs =
		[
			RunWithExitCode(new Scan { PathString = path }),
			RunWithExitCode(new DryRun { PathString = path }),
			RunWithExitCode(new Stats { PathString = path }),
			RunWithExitCode(new Deduplicate { PathString = path }, "y"),
		];

		// Assert
		foreach ((string output, int exitCode) in runs)
		{
			Assert.AreEqual(0, exitCode, output);
			Assert.Contains("notes <draft>.txt", output, "The skipped file was not named.");
			Assert.Contains("Duplicate", output, StringComparison.OrdinalIgnoreCase);
		}

		Assert.IsFalse(TempTree.Exists(copy), "Deduplicate did not remove the duplicate beside the skipped file.");
	}

	/// <summary>
	/// A root the path type cannot represent fails in one line with exit code 1, not a stack trace.
	/// </summary>
	[TestMethod]
	public void ScanOfAnUnrepresentableRootReportsItInOneLine()
	{
		// Arrange
		using TempTree tree = new();
		FileScannerAndHasherTests.WriteUnrepresentable(tree, "left|right/f.txt");
		string root = Path.Combine(tree.Root.WeakString, "left|right");

		// Act
		(string output, int exitCode) = RunWithExitCode(new Scan { PathString = root });

		// Assert
		Assert.AreEqual(1, exitCode, output);
		Assert.Contains($"Cannot scan {root}", output);
	}

	private static (string Output, int ExitCode) RunWithExitCode(BaseVerb verb, string stdin = "")
	{
		string output = ConsoleCapture.Run(verb, stdin, out int exitCode);
		return (output, exitCode);
	}

	/// <summary>
	/// When every duplicate is empty, and so kept, Scan does not point at a Deduplicate run that
	/// would have nothing to do.
	/// </summary>
	[TestMethod]
	public void ScanDoesNotSuggestDeduplicateWhenOnlyEmptyDuplicatesExist()
	{
		// Arrange
		using TempTree tree = new();
		AbsoluteFilePath first = tree.Write("a/.gitkeep", string.Empty);
		_ = tree.Write("b/.gitkeep", string.Empty);

		// Act
		string output = ConsoleCapture.Normalize(ConsoleCapture.Run(new Scan { PathString = tree.Root.WeakString }));

		// Assert
		Assert.Contains($"{first} [KEEP, empty]", output);
		Assert.DoesNotContain("Run the 'Deduplicate' command", output, $"Scan suggested a Deduplicate run with nothing to delete. Output was:\n{output}");
	}
}
