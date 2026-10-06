// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.FileDeduplicator.Test;

using CommandLine;

using ktsu.FileDeduplicator.Verbs;
using ktsu.Semantics.Paths;

using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Tests that an explicit <c>-p .</c> means the current directory rather than "no path given".
/// </summary>
/// <remarks>
/// <c>"."</c> used to be both the option's default and the marker that made a verb prompt, so
/// <c>-p .</c> prompted anyway and read the next stdin line as the path. With input piped in, as in
/// <c>echo y | Deduplicate -p .</c>, the <c>y</c> meant to confirm the deletion became the
/// directory to work on (ktsu-dev/FileDeduplicator#145).
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class ExplicitPathTests
{
	/// <summary>
	/// Parses a command line the way <c>Program.Main</c> does and returns the verb it selects.
	/// </summary>
	/// <param name="args">The command-line arguments.</param>
	/// <returns>The parsed verb.</returns>
	private static BaseVerb Parse(params string[] args)
	{
		BaseVerb? verb = null;
		using Parser parser = new();
		_ = parser.ParseArguments(args, Program.Verbs).WithParsed<BaseVerb>(parsed => verb = parsed);
		Assert.IsNotNull(verb, $"'{string.Join(' ', args)}' did not parse.");
		return verb;
	}

	/// <summary>
	/// Runs a verb parsed from <paramref name="args"/> with <paramref name="directory"/> as the
	/// current directory.
	/// </summary>
	/// <param name="directory">The directory to run in.</param>
	/// <param name="stdin">Lines for the verb's prompts.</param>
	/// <param name="args">The command-line arguments.</param>
	/// <returns>Everything the verb wrote to the console.</returns>
	private static string RunIn(AbsoluteDirectoryPath directory, string stdin, params string[] args)
	{
		string original = Environment.CurrentDirectory;
		try
		{
			Environment.CurrentDirectory = directory.WeakString;
			return ConsoleCapture.Run(Parse(args), stdin);
		}
		finally
		{
			Environment.CurrentDirectory = original;
		}
	}

	[TestMethod]
	public void ScanWithAnExplicitDotScansTheCurrentDirectoryWithoutPrompting()
	{
		using TempTree tree = new();
		_ = tree.Write("a.txt", "alpha");
		_ = tree.Write("aa.txt", "alpha");

		string output = RunIn(tree.Root, "elsewhere\n", "Scan", "-p", ".");

		Assert.DoesNotContain("Enter the path", output, "An explicit -p . was treated as no path.");
		Assert.Contains("Found 1 group(s) of duplicate files", output);
	}

	[TestMethod]
	public void DeduplicateWithAnExplicitDotReadsTheNextLineAsTheConfirmation()
	{
		using TempTree tree = new();
		AbsoluteFilePath keeper = tree.Write("a.txt", "alpha");
		AbsoluteFilePath copy = tree.Write("aa.txt", "alpha");

		string output = RunIn(tree.Root, "y\n", "Deduplicate", "--path", ".");

		Assert.DoesNotContain("Enter the path", output, "An explicit --path . was treated as no path.");
		Assert.IsTrue(TempTree.Exists(keeper), "The kept copy was deleted.");
		Assert.IsFalse(TempTree.Exists(copy), "The 'y' was not read as the confirmation.");
	}

	[TestMethod]
	public void DryRunWithAnExplicitDotDoesNotPrompt()
	{
		using TempTree tree = new();
		_ = tree.Write("a.txt", "alpha");

		string output = RunIn(tree.Root, "elsewhere\n", "DryRun", "-p", ".");

		Assert.DoesNotContain("Enter the path", output);
		Assert.Contains("Found 1 file(s).", output);
	}

	[TestMethod]
	public void StatsWithAnExplicitDotDoesNotPrompt()
	{
		using TempTree tree = new();
		_ = tree.Write("a.txt", "alpha");

		string output = RunIn(tree.Root, "elsewhere\n", "Stats", "-p", ".");

		Assert.DoesNotContain("Enter the path", output);
		Assert.Contains("Total files: 1", output);
	}

	[TestMethod]
	public void LeavingOutThePathStillPrompts()
	{
		using TempTree tree = new();
		_ = tree.Write("a.txt", "alpha");

		string output = RunIn(tree.Root, $"{tree.Root.WeakString}\n", "Scan");

		Assert.Contains("Enter the path to scan:", output);
		Assert.Contains("Found 1 file(s).", output);
	}
}
