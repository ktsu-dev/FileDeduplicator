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
	[DataRow("CVS")]
	[DataRow("_darcs")]
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
	[DataRow("cvs")]
	[DataRow("Cvs")]
	[DataRow("cVS")]
	[DataRow("_DARCS")]
	[DataRow("_Darcs")]
	public void TheScanReadsAUserFolderNamedLikeCvsOrDarcsInAnotherCase(string folder)
	{
		// Arrange -- CVS only ever writes "CVS" and darcs only "_darcs" (ktsu-dev/FileDeduplicator#211)
		using TempTree tree = new();
		AbsoluteFilePath[] expected =
		[
			tree.Write($"receipts/{folder}/r1.pdf", "receipt"),
			tree.Write($"receipts/{folder}/r1-copy.pdf", "receipt"),
			tree.Write("receipts/Walgreens/x.pdf", "receipt"),
		];

		// Act
		IReadOnlyList<AbsoluteFilePath> files = FileScanner.ScanForFiles(tree.Root);

		// Assert
		CollectionAssert.AreEquivalent(expected, files.ToArray());
	}

	[TestMethod]
	public void ScanReportsTheCopiesInALowercaseCvsFolder()
	{
		// Arrange -- the reproduction from ktsu-dev/FileDeduplicator#211
		using TempTree tree = new();
		AbsoluteFilePath first = tree.Write("receipts/cvs/r1.pdf", "receipt");
		AbsoluteFilePath second = tree.Write("receipts/cvs/r1-copy.pdf", "receipt");
		AbsoluteFilePath third = tree.Write("receipts/Walgreens/x.pdf", "receipt");

		// Act
		string output = ConsoleCapture.Normalize(ConsoleCapture.Run(new Scan { PathString = tree.Root.WeakString }, string.Empty));

		// Assert
		Assert.Contains("Found 3 file(s).", output, $"Scan did not find every file. Output was:\n{output}");
		Assert.Contains(first.WeakString, output, $"Scan missed a copy in cvs/. Output was:\n{output}");
		Assert.Contains(second.WeakString, output, $"Scan missed a copy in cvs/. Output was:\n{output}");
		Assert.Contains(third.WeakString, output, $"Scan missed the copy outside cvs/. Output was:\n{output}");
	}

	[TestMethod]
	[DataRow("srv/project.git")]
	[DataRow("mirror")]
	[DataRow(".cfg")]
	public void TheScanSkipsABareGitRepositoryWhateverItsName(string bare)
	{
		// Arrange -- a bare repository keeps HEAD, objects/ and refs/ directly in a folder of any name
		using TempTree tree = new();
		AbsoluteFilePath outside = tree.Write("readme.txt", "content");
		_ = tree.Write($"{bare}/HEAD", "ref: refs/heads/main\n");
		_ = tree.Write($"{bare}/objects/info/packs", "\n");
		_ = tree.Write($"{bare}/refs/heads/main", BranchTip);
		_ = tree.Write($"{bare}/refs/heads/dev", BranchTip);

		// Act
		IReadOnlyList<AbsoluteFilePath> files = FileScanner.ScanForFiles(tree.Root);

		// Assert
		CollectionAssert.AreEquivalent(new[] { outside }, files.ToArray());
	}

	[TestMethod]
	public void TheScanStillReadsAFolderThatHasOnlyPartOfAGitDirectoryLayout()
	{
		// Arrange -- HEAD and refs/ without objects/ is not a gitdir, whatever the folder is called
		using TempTree tree = new();
		AbsoluteFilePath[] expected =
		[
			tree.Write("notes.git/HEAD", "ref: refs/heads/main\n"),
			tree.Write("notes.git/refs/heads/a", BranchTip),
			tree.Write("notes.git/refs/heads/b", BranchTip),
		];

		// Act
		IReadOnlyList<AbsoluteFilePath> files = FileScanner.ScanForFiles(tree.Root);

		// Assert
		CollectionAssert.AreEquivalent(expected, files.ToArray());
	}

	[TestMethod]
	public void DeduplicateLeavesTheBranchesOfABareRepositoryAlone()
	{
		// Arrange -- the reproduction from ktsu-dev/FileDeduplicator#198
		using TempTree tree = new();
		_ = tree.Write("srv/project.git/HEAD", "ref: refs/heads/main\n");
		_ = tree.Write("srv/project.git/objects/info/packs", "\n");
		AbsoluteFilePath main = tree.Write("srv/project.git/refs/heads/main", BranchTip);
		AbsoluteFilePath dev = tree.Write("srv/project.git/refs/heads/dev", BranchTip);

		// Act
		string output = ConsoleCapture.Normalize(ConsoleCapture.Run(new Deduplicate { PathString = tree.Root.WeakString }, "y"));

		// Assert
		Assert.IsTrue(TempTree.Exists(main), $"refs/heads/main was deleted. Output was:\n{output}");
		Assert.IsTrue(TempTree.Exists(dev), $"refs/heads/dev was deleted. Output was:\n{output}");
		Assert.DoesNotContain("project.git", output, $"The run reported a path inside the bare repository. Output was:\n{output}");
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
