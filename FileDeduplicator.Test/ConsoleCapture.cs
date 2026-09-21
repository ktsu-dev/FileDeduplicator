// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.FileDeduplicator.Test;

using ktsu.FileDeduplicator.Verbs;

/// <summary>
/// Runs a verb with the console redirected, and hands back everything it wrote.
/// </summary>
/// <remarks>
/// The verbs have no return value and no output abstraction -- what they do is what they print --
/// so the console is the only surface a test can assert against. Redirecting it is global state,
/// which is why every class using this is marked <c>[DoNotParallelize]</c>.
/// </remarks>
internal static class ConsoleCapture
{
	/// <summary>
	/// Runs a verb, feeding it the given answers on stdin.
	/// </summary>
	/// <param name="verb">The verb to run.</param>
	/// <param name="stdin">Lines the verb's prompts will read, or nothing.</param>
	/// <returns>Everything the verb wrote to the console.</returns>
	internal static string Run(BaseVerb verb, string stdin = "")
	{
		TextWriter originalOut = Console.Out;
		TextReader originalIn = Console.In;

		try
		{
			using StringWriter captured = new();
			using StringReader answers = new(stdin);
			Console.SetOut(captured);
			Console.SetIn(answers);

			verb.Run();

			return captured.ToString();
		}
		finally
		{
			Console.SetOut(originalOut);
			Console.SetIn(originalIn);
		}
	}

	/// <summary>
	/// Collapses runs of whitespace so assertions describe what a line says rather than the column
	/// its markers happen to sit in, and so a CRLF platform reads the same as an LF one.
	/// </summary>
	/// <param name="output">The captured output.</param>
	/// <returns>The output with each line trimmed and its internal whitespace collapsed.</returns>
	internal static string Normalize(string output) =>
		string.Join('\n', output.Split('\n').Select(line => string.Join(' ', line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))));
}
