// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.FileDeduplicator.Test;

using ktsu.FileDeduplicator.Verbs;
using ktsu.Semantics.Paths;

using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Tests that version-control metadata under the root is never scanned, reported or deleted.
/// </summary>
/// <remarks>
/// Every branch ref in <c>.git/refs/heads</c> that points at the same commit has the same 41 bytes,
/// so a deduplicator that walks into <c>.git</c> deletes branches (ktsu-dev/FileDeduplicator#192).
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class VersionControlMetadataTests
{
	private const string BranchTip = "0123456789abcdef0123456789abcdef01234567\n";

	[TestMethod]
	[DataRow(".git")]
	[DataRow(".svn")]
	[DataRow(".hg")]
	[DataRow(".bzr")]
	[DataRow(".GIT")]
	public void TheScanSkipsVersionControlMetadataDirectories(string metadata)
	{
		// Arrange
		using TempTree tree = new();
		AbsoluteFilePath outside = tree.Write("proj/readme.txt", "content");
		_ = tree.Write($"proj/{metadata}/refs/heads/a", BranchTip);
		_ = tree.Write($"proj/{metadata}/refs/heads/b", BranchTip);

		// Act
		IReadOnlyList<AbsoluteFilePath> files = FileScanner.ScanForFiles(tree.Root);

		// Assert
		CollectionAssert.AreEquivalent(new[] { outside }, files.ToArray());
	}

	[TestMethod]
	public void TheScanStillReadsFilesWhoseNamesOnlyResembleMetadata()
	{
		// Arrange -- a worktree's ".git" is a file, and ".github" or "x.git" are ordinary directories
		using TempTree tree = new();
		AbsoluteFilePath[] expected =
		[
			tree.Write("worktree/.git", "gitdir: /elsewhere/.git/worktrees/w\n"),
			tree.Write(".github/workflows/ci.yml", "on: push\n"),
			tree.Write("mirror.git/HEAD", "ref: refs/heads/main\n"),
			tree.Write(".gitignore", "bin/\n"),
		];

		// Act
		IReadOnlyList<AbsoluteFilePath> files = FileScanner.ScanForFiles(tree.Root);

		// Assert
		CollectionAssert.AreEquivalent(expected, files.ToArray());
	}

	[TestMethod]
	public void DeduplicateDeletesOnlyTheDuplicatesOutsideGitMetadata()
	{
		// Arrange
		using TempTree tree = new();
		AbsoluteFilePath[] refs =
		[
			tree.Write("proj/.git/refs/heads/main", BranchTip),
			tree.Write("proj/.git/refs/heads/feature", BranchTip),
			tree.Write("proj/.git/refs/heads/release", BranchTip),
			tree.Write("proj/.git/logs/HEAD", "reflog line\n"),
			tree.Write("proj/.git/logs/refs/heads/main", "reflog line\n"),
		];
		AbsoluteFilePath keeper = tree.Write("proj/a.txt", "a real duplicate");
		AbsoluteFilePath copy = tree.Write("proj/copy-of-a.txt", "a real duplicate");

		// Act
		string output = ConsoleCapture.Normalize(ConsoleCapture.Run(new Deduplicate { PathString = tree.Root.WeakString }, "y"));

		// Assert
		Assert.IsTrue(TempTree.Exists(keeper), $"The kept copy was deleted. Output was:\n{output}");
		Assert.IsFalse(TempTree.Exists(copy), $"The real duplicate outside .git was not deleted. Output was:\n{output}");
		foreach (AbsoluteFilePath file in refs)
		{
			Assert.IsTrue(TempTree.Exists(file), $"{file} was deleted. Output was:\n{output}");
		}

		Assert.DoesNotContain(".git", output, $"The run reported a path inside .git. Output was:\n{output}");
	}

	[TestMethod]
	public void ScanAndDryRunDoNotReportGitMetadata()
	{
		// Arrange
		using TempTree tree = new();
		_ = tree.Write("proj/.git/refs/heads/main", BranchTip);
		_ = tree.Write("proj/.git/refs/heads/feature", BranchTip);
		AbsoluteFilePath copy = tree.Write("proj/copy-of-a.txt", "a real duplicate");
		_ = tree.Write("proj/a.txt", "a real duplicate");

		// Act
		string scan = ConsoleCapture.Normalize(ConsoleCapture.Run(new Scan { PathString = tree.Root.WeakString }, string.Empty));
		string dryRun = ConsoleCapture.Normalize(ConsoleCapture.Run(new DryRun { PathString = tree.Root.WeakString }, string.Empty));

		// Assert
		Assert.Contains(copy.WeakString, scan, $"Scan missed the real duplicate. Output was:\n{scan}");
		Assert.Contains(copy.WeakString, dryRun, $"DryRun missed the real duplicate. Output was:\n{dryRun}");
		Assert.DoesNotContain(".git", scan, $"Scan reported a path inside .git. Output was:\n{scan}");
		Assert.DoesNotContain(".git", dryRun, $"DryRun reported a path inside .git. Output was:\n{dryRun}");
	}
}
