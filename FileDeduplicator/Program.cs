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
		using Parser parser = CreateParser(Console.Error);
		return Run(parser, args);
	}

	/// <summary>
	/// Creates the parser <see cref="Main"/> uses.
	/// </summary>
	/// <remarks>
	/// Verb names match in any case. Otherwise <c>scan -p dir</c> names no verb, falls through to the
	/// default Menu verb, and exits 0 without scanning (ktsu-dev/FileDeduplicator#195).
	/// </remarks>
	/// <param name="helpWriter">Where help and parse errors are written.</param>
	/// <returns>The parser.</returns>
	internal static Parser CreateParser(TextWriter helpWriter) => new(settings =>
	{
		settings.CaseSensitive = false;
		settings.HelpWriter = helpWriter;
	});

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
