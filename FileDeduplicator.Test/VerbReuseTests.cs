// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.FileDeduplicator.Test;

using ktsu.FileDeduplicator.Verbs;
using ktsu.Semantics.Paths;

using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Tests that a verb instance asks for a new path each time it runs, however its last run ended.
/// </summary>
/// <remarks>
/// The interactive menu creates one instance of each verb and runs it every time its item is chosen.
/// Each verb reset its path only on the last line of its run, so an early return (nothing found, no
/// duplicates, a declined confirmation) kept the old path. The next run then skipped the prompt,
/// and the path the user typed was read as the answer to the next question instead
/// (ktsu-dev/FileDeduplicator#138).
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class VerbReuseTests
{
	/// <summary>
	/// Runs a verb against a tree with no duplicates, which returns early, then runs it again.
	/// </summary>
	/// <param name="verb">The verb instance, reused as the menu reuses it.</param>
	/// <param name="prompt">The prompt the verb shows when it has no path.</param>
	private static void AssertPromptsAgainAfterAnEarlyReturn(BaseVerb verb, string prompt)
	{
		using TempTree first = new();
		_ = first.Write("a.txt", "alpha");
		_ = first.Write("b.txt", "beta");

		using TempTree second = new();
		_ = second.Write("c.txt", "gamma");

		string firstOutput = ConsoleCapture.Run(verb, $"{first.Root.WeakString}\n");
		string secondOutput = ConsoleCapture.Run(verb, $"{second.Root.WeakString}\n");

		Assert.Contains(prompt, firstOutput);
		Assert.Contains(prompt, secondOutput, $"{verb.GetType().Name} reused the previous path without asking.");
		Assert.DoesNotContain(first.Root.WeakString, secondOutput, $"{verb.GetType().Name} ran against the previous path.");
	}

	[TestMethod]
	public void DeduplicatePromptsAgainAfterFindingNoDuplicates() =>
		AssertPromptsAgainAfterAnEarlyReturn(new Deduplicate(), "Enter the path to deduplicate:");

	[TestMethod]
	public void DryRunPromptsAgainAfterFindingNoDuplicates() =>
		AssertPromptsAgainAfterAnEarlyReturn(new DryRun(), "Enter the path to scan:");

	[TestMethod]
	public void ScanPromptsAgainAfterFindingNoDuplicates() =>
		AssertPromptsAgainAfterAnEarlyReturn(new Scan(), "Enter the path to scan:");

	[TestMethod]
	public void StatsPromptsAgainAfterFindingNoFiles()
	{
		Stats stats = new();
		using TempTree empty = new();
		using TempTree second = new();
		_ = second.Write("c.txt", "gamma");

		_ = ConsoleCapture.Run(stats, $"{empty.Root.WeakString}\n");
		string secondOutput = ConsoleCapture.Run(stats, $"{second.Root.WeakString}\n");

		Assert.Contains("Enter the path to analyze:", secondOutput, "Stats reused the previous path without asking.");
	}

	/// <summary>
	/// Declining the deletion is the case the issue describes: the next path typed must not be read
	/// as a confirmation against the previous directory.
	/// </summary>
	[TestMethod]
	public void DeduplicatePromptsAgainAfterTheUserDeclines()
	{
		Deduplicate deduplicate = new();
		using TempTree first = new();
		AbsoluteFilePath keeper = first.Write("a.txt", "alpha");
		AbsoluteFilePath copy = first.Write("aa.txt", "alpha");

		using TempTree second = new();
		_ = second.Write("c.txt", "gamma");

		_ = ConsoleCapture.Run(deduplicate, $"{first.Root.WeakString}\nn\n");
		string secondOutput = ConsoleCapture.Run(deduplicate, $"{second.Root.WeakString}\n");

		Assert.Contains("Enter the path to deduplicate:", secondOutput, "Deduplicate reused the previous path without asking.");
		Assert.IsTrue(TempTree.Exists(keeper) && TempTree.Exists(copy), "The path typed second was read as a confirmation against the first directory.");
	}
}
