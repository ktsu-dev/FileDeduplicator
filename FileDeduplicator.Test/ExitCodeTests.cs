// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.FileDeduplicator.Test;

using CommandLine;

using ktsu.FileDeduplicator.Verbs;
using ktsu.Semantics.Paths;

using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Tests that a failed or aborted run exits non-zero, so a script can tell it from a successful one.
/// </summary>
/// <remarks>
/// <c>Main</c> and every verb returned <see langword="void"/>, so the process exited 0 for a missing
/// directory, an abort, failed deletions and unparseable arguments alike, and
/// <c>Deduplicate -p /data &amp;&amp; echo done</c> reported success when nothing was deleted
/// (ktsu-dev/FileDeduplicator#144).
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class ExitCodeTests
{
	private static int ExitCodeOf(BaseVerb verb, string stdin = "")
	{
		_ = ConsoleCapture.Run(verb, stdin, out int exitCode);
		return exitCode;
	}

	private static int ExitCodeOfCommandLine(params string[] args)
	{
		_ = ConsoleCapture.Run(
			captured =>
			{
				using Parser parser = Program.CreateParser(captured);
				return Program.Run(parser, args);
			},
			string.Empty,
			out int exitCode);
		return exitCode;
	}

	private static void AssertEveryVerbExitsOne(string path)
	{
		Assert.AreEqual(1, ExitCodeOf(new Scan { PathString = path }), "Scan");
		Assert.AreEqual(1, ExitCodeOf(new DryRun { PathString = path }), "DryRun");
		Assert.AreEqual(1, ExitCodeOf(new Stats { PathString = path }), "Stats");
		Assert.AreEqual(1, ExitCodeOf(new Deduplicate { PathString = path }, "y"), "Deduplicate");
	}

	[TestMethod]
	public void EveryVerbExitsOneForAMissingDirectory()
	{
		using TempTree tree = new();
		AssertEveryVerbExitsOne(Path.Combine(tree.Root.WeakString, "not-there"));
	}

	[TestMethod]
	public void EveryVerbExitsOneForAFileGivenAsTheRoot()
	{
		using TempTree tree = new();
		AssertEveryVerbExitsOne(tree.Write("f.txt", "content").WeakString);
	}

	[TestMethod]
	public void EveryVerbExitsZeroForADirectoryItCouldScan()
	{
		using TempTree empty = new();
		using TempTree duplicates = new();
		_ = duplicates.Write("a.txt", "alpha");
		_ = duplicates.Write("aa.txt", "alpha");

		Assert.AreEqual(0, ExitCodeOf(new Scan { PathString = empty.Root.WeakString }), "Scan of an empty directory");
		Assert.AreEqual(0, ExitCodeOf(new Scan { PathString = duplicates.Root.WeakString }), "Scan");
		Assert.AreEqual(0, ExitCodeOf(new DryRun { PathString = duplicates.Root.WeakString }), "DryRun");
		Assert.AreEqual(0, ExitCodeOf(new Stats { PathString = duplicates.Root.WeakString }), "Stats");
	}

	[TestMethod]
	public void AbortingAtThePathPromptExitsOne() =>
		Assert.AreEqual(1, ExitCodeOf(new Scan(), "\n"));

	[TestMethod]
	public void DecliningTheDeletionExitsOne()
	{
		using TempTree tree = new();
		_ = tree.Write("a.txt", "alpha");
		_ = tree.Write("aa.txt", "alpha");

		Assert.AreEqual(1, ExitCodeOf(new Deduplicate { PathString = tree.Root.WeakString }, "n"));
	}

	[TestMethod]
	public void DeduplicateExitsZeroWhenEveryDeletionSucceeds()
	{
		using TempTree tree = new();
		_ = tree.Write("a.txt", "alpha");
		AbsoluteFilePath copy = tree.Write("aa.txt", "alpha");

		Assert.AreEqual(0, ExitCodeOf(new Deduplicate { PathString = tree.Root.WeakString }, "y"));
		Assert.IsFalse(TempTree.Exists(copy));
	}

	[TestMethod]
	public void DeduplicateExitsOneWhenADeletionFails()
	{
		using TempTree tree = new();
		_ = tree.Write("a.txt", "alpha");
		AbsoluteFilePath undeletable = tree.Write("locked/aa.txt", "alpha");

		using DeletionBlock block = new(undeletable);
		if (!block.IsEnforced)
		{
			Assert.Inconclusive("This process deletes through a write-protected directory, so the refusal under test cannot be staged. Run the tests as an unprivileged user.");
		}

		Assert.AreEqual(1, ExitCodeOf(new Deduplicate { PathString = tree.Root.WeakString }, "y"));
	}

	[TestMethod]
	public void AnUnknownOptionExitsTwo() =>
		Assert.AreEqual(2, ExitCodeOfCommandLine("Scan", "--nope"));

	[TestMethod]
	public void ALowercaseVerbRunsThatVerb()
	{
		using TempTree tree = new();
		string missing = Path.Combine(tree.Root.WeakString, "not-there");

		// Scan of a missing directory exits 1; the Menu it used to fall through to exits 0 at end of input
		Assert.AreEqual(1, ExitCodeOfCommandLine("scan", "-p", missing));
		Assert.AreEqual(0, ExitCodeOfCommandLine("scan", "-p", tree.Root.WeakString));
	}

	[TestMethod]
	public void AnUnknownVerbExitsTwo()
	{
		using TempTree tree = new();
		Assert.AreEqual(2, ExitCodeOfCommandLine("dedupe", "-p", tree.Root.WeakString));
	}

	[TestMethod]
	public void AStrayPositionalArgumentExitsTwo()
	{
		using TempTree tree = new();
		Assert.AreEqual(2, ExitCodeOfCommandLine("Scan", "-p", tree.Root.WeakString, "extra"));
	}

	[TestMethod]
	public void APathGivenWithoutDashPExitsTwoWithoutPrompting()
	{
		using TempTree tree = new();
		string output = ConsoleCapture.Run(
			captured =>
			{
				using Parser parser = Program.CreateParser(captured);
				return Program.Run(parser, ["Deduplicate", tree.Root.WeakString]);
			},
			"y",
			out int exitCode);

		Assert.AreEqual(2, exitCode);
		Assert.IsFalse(output.Contains("Enter the path", StringComparison.Ordinal), output);
	}

	[TestMethod]
	public void AskingForHelpExitsZero() =>
		Assert.AreEqual(0, ExitCodeOfCommandLine("--help"));

	[TestMethod]
	public void AParsedVerbReturnsItsOwnExitCode()
	{
		using TempTree tree = new();
		string missing = Path.Combine(tree.Root.WeakString, "not-there");

		Assert.AreEqual(1, ExitCodeOfCommandLine("Scan", "-p", missing));
		Assert.AreEqual(0, ExitCodeOfCommandLine("Scan", "-p", tree.Root.WeakString));
	}
}
