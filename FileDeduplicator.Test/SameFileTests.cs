// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.FileDeduplicator.Test;

using System.Diagnostics;

using ktsu.FileDeduplicator.Verbs;
using ktsu.Semantics.Paths;
using ktsu.Semantics.Strings;

using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Tests that one file reached at two paths -- a hardlink or a bind mount -- is never treated as a
/// duplicate of itself, and above all is never deleted as one.
/// </summary>
/// <remarks>
/// Through a bind mount, the "other copy" is the keeper's own directory entry, so deleting it
/// deletes the only copy of the data, and the keeper re-hash cannot catch it. Through a hardlink,
/// deleting it removes a name from a snapshot while reclaiming nothing.
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class SameFileTests
{
	/// <summary>
	/// Two hardlinks to one file share an identity, and two files with the same content do not.
	/// </summary>
	[TestMethod]
	public void HardlinksShareAnIdentityAndCopiesDoNot()
	{
		// Arrange
		using TempTree tree = new();
		AbsoluteFilePath original = tree.Write("original.txt", "content");
		AbsoluteFilePath copy = tree.Write("copy.txt", "content");
		AbsoluteFilePath link = MakeHardLink(tree, original, "link.txt");

		// Act
		Assert.IsTrue(FileIdentity.TryGet(original.WeakString, out FileIdentity originalIdentity));
		Assert.IsTrue(FileIdentity.TryGet(copy.WeakString, out FileIdentity copyIdentity));
		Assert.IsTrue(FileIdentity.TryGet(link.WeakString, out FileIdentity linkIdentity));

		// Assert
		Assert.AreEqual(originalIdentity, linkIdentity);
		Assert.AreNotEqual(originalIdentity, copyIdentity);
	}

	/// <summary>
	/// The index read on Unix is the inode number <c>ls -i</c> reports, which pins the offset the
	/// identity is read from in the runtime's file status structure.
	/// </summary>
	[TestMethod]
	[OSCondition(OperatingSystems.Linux | OperatingSystems.OSX)]
	public void TheUnixIndexIsTheInodeNumber()
	{
		// Arrange
		using TempTree tree = new();
		AbsoluteFilePath file = tree.Write("file.txt", "content");

		// Act
		Assert.IsTrue(FileIdentity.TryGet(file.WeakString, out FileIdentity identity));
		string listing = RunTool("ls", "-i", file.WeakString);

		// Assert
		Assert.AreEqual(ulong.Parse(listing.Trim().Split(' ')[0], System.Globalization.CultureInfo.InvariantCulture), identity.Index);
	}

	/// <summary>
	/// A missing file has no identity, rather than throwing.
	/// </summary>
	[TestMethod]
	public void AMissingFileHasNoIdentity()
	{
		using TempTree tree = new();

		Assert.IsFalse(FileIdentity.TryGet(Path.Combine(tree.Root.WeakString, "absent.txt"), out _));
	}

	/// <summary>
	/// A file and its hardlink are one file, so they do not form a duplicate group.
	/// </summary>
	[TestMethod]
	public void AHardlinkIsNotADuplicateOfItsOwnFile()
	{
		// Arrange
		using TempTree tree = new();
		AbsoluteFilePath original = tree.Write("original.txt", "content");
		AbsoluteFilePath link = MakeHardLink(tree, original, "nested/link.txt");

		// Act
		IReadOnlyList<DuplicateGroup> duplicates = Deduplicator.FindDuplicates(
			Deduplicator.GroupByHash(FileHasher.HashFiles([original, link])));

		// Assert
		Assert.IsEmpty(duplicates);
	}

	/// <summary>
	/// A hardlink in a group with a genuine copy is collapsed into its file, so the group holds the
	/// file once and the genuine copy once.
	/// </summary>
	[TestMethod]
	public void AHardlinkIsCollapsedIntoItsFileAlongsideAGenuineCopy()
	{
		// Arrange
		using TempTree tree = new();
		AbsoluteFilePath original = tree.Write("a.txt", "content");
		AbsoluteFilePath link = MakeHardLink(tree, original, "linked.txt");
		AbsoluteFilePath copy = tree.Write("copy.txt", "content");

		// Act
		IReadOnlyList<DuplicateGroup> duplicates = Deduplicator.FindDuplicates(
			Deduplicator.GroupByHash(FileHasher.HashFiles([original, link, copy])));

		// Assert
		Assert.HasCount(1, duplicates);
		CollectionAssert.AreEquivalent(new[] { original, copy }, duplicates[0].Files);
	}

	/// <summary>
	/// The deletion loop refuses a path that leads to the keeper's own file, whatever grouping it is
	/// handed, and reports it instead.
	/// </summary>
	[TestMethod]
	public void DeletionSkipsAPathThatLeadsToTheKeeper()
	{
		// Arrange -- a group built by hand, bypassing the collapse in FindDuplicates
		using TempTree tree = new();
		AbsoluteFilePath keeper = tree.Write("a.txt", "content");
		AbsoluteFilePath link = MakeHardLink(tree, keeper, "longer-name.txt");
		string hash = FileHasher.HashFiles([keeper])[keeper];
		DuplicateGroup group = new(hash, [keeper, link], "content".Length);

		// Act
		DeduplicationResult result = Deduplicator.DeleteDuplicates([group]);

		// Assert
		Assert.AreEqual(0, result.DeletedCount);
		Assert.AreEqual(0, result.BytesReclaimed);
		Assert.IsTrue(TempTree.Exists(keeper));
		Assert.IsTrue(TempTree.Exists(link));
		Assert.HasCount(1, result.SkippedFiles);
		Assert.AreEqual(link, result.SkippedFiles[0].Path);
		Assert.Contains("same file as the copy being kept", result.SkippedFiles[0].Reason);
	}

	/// <summary>
	/// The issue's reproduction: one directory visible at two paths through a bind mount. The run
	/// ends with the file still present, nothing deleted and nothing reported as reclaimed.
	/// </summary>
	[TestMethod]
	public void DeduplicateLeavesAFileReachedThroughABindMountAlone()
	{
		// Arrange
		using TempTree tree = new();
		AbsoluteFilePath important = tree.Write("real/important.txt", "only copy of important data");
		string view = Path.Combine(tree.Root.WeakString, "view");
		_ = Directory.CreateDirectory(view);
		BindMount(Path.Combine(tree.Root.WeakString, "real"), view);

		try
		{
			// Act
			string output = ConsoleCapture.Normalize(ConsoleCapture.Run(new Deduplicate { PathString = tree.Root.WeakString }, "y"));
			string dryRun = ConsoleCapture.Normalize(ConsoleCapture.Run(new DryRun { PathString = tree.Root.WeakString }));

			// Assert
			Assert.IsTrue(TempTree.Exists(important), $"The only copy was deleted. Output was:\n{output}");
			Assert.AreEqual("only copy of important data", File.ReadAllText(important.WeakString));
			Assert.DoesNotContain("Deleted:", output);
			Assert.Contains("No duplicate files found.", output);
			Assert.Contains("No duplicate files found.", dryRun);
		}
		finally
		{
			_ = RunTool("umount", view);
		}
	}

	private static AbsoluteFilePath MakeHardLink(TempTree tree, AbsoluteFilePath target, string relativePath)
	{
		string link = Path.Combine(tree.Root.WeakString, relativePath.Replace('/', Path.DirectorySeparatorChar));
		_ = Directory.CreateDirectory(Path.GetDirectoryName(link)!);

		_ = OperatingSystem.IsWindows()
			? RunTool("cmd", "/c", "mklink", "/H", link, target.WeakString)
			: RunTool("ln", target.WeakString, link);

		Assert.IsTrue(File.Exists(link), $"Could not create a hardlink at {link}.");
		return link.As<AbsoluteFilePath>();
	}

	private static void BindMount(string source, string target)
	{
		if (!OperatingSystem.IsLinux())
		{
			Assert.Inconclusive("Bind mounts are a Linux feature.");
		}

		using Process mount = Process.Start(new ProcessStartInfo("mount", ["--bind", source, target])
		{
			RedirectStandardError = true,
			RedirectStandardOutput = true,
		})!;
		mount.WaitForExit();

		if (mount.ExitCode != 0)
		{
			Assert.Inconclusive($"Could not bind-mount {source} (mount needs root): {mount.StandardError.ReadToEnd()}");
		}
	}

	private static string RunTool(string fileName, params string[] arguments)
	{
		using Process process = Process.Start(new ProcessStartInfo(fileName, arguments)
		{
			RedirectStandardOutput = true,
			RedirectStandardError = true,
		})!;
		string output = process.StandardOutput.ReadToEnd();
		process.WaitForExit();
		return output;
	}
}
