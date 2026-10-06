// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.FileDeduplicator;

using System.Reflection;
using System.Text;

using CommandLine;

using ktsu.FileDeduplicator.Verbs;

internal static class Program
{
	internal static Type[] Verbs { get; } = LoadVerbs();

	/// <summary>
	/// Runs the verb named on the command line.
	/// </summary>
	/// <param name="args">The command-line arguments.</param>
	/// <returns>
	/// 0 on success, 1 when the verb failed or the user aborted, and 2 when the arguments could not be
	/// parsed. Asking for help or the version is a success.
	/// </returns>
	private static int Main(string[] args)
	{
		Console.OutputEncoding = Encoding.UTF8;
		return Run(Parser.Default, args);
	}

	/// <summary>
	/// Parses <paramref name="args"/> with <paramref name="parser"/> and runs the verb they name.
	/// </summary>
	/// <param name="parser">The parser to use.</param>
	/// <param name="args">The command-line arguments.</param>
	/// <returns>The process exit code, as <see cref="Main"/> describes it.</returns>
	internal static int Run(Parser parser, string[] args) =>
		parser.ParseArguments(args, Verbs).MapResult(
			(BaseVerb verb) => verb.Run(),
			errors => errors.IsHelp() || errors.IsVersion() ? 0 : 2);

	private static Type[] LoadVerbs() => [.. Assembly.GetExecutingAssembly().GetTypes().Where(t => t.GetCustomAttribute<VerbAttribute>() != null)];
}
