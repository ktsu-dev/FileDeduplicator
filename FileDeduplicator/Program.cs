// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace ktsu.FileDeduplicator;

using System.Reflection;
using System.Text;

using CommandLine;

using ktsu.FileDeduplicator.Verbs;

internal static class Program
{
	internal static Type[] Verbs { get; } = LoadVerbs();

	private static void Main(string[] args)
	{
		Console.OutputEncoding = Encoding.UTF8;

		_ = Parser.Default.ParseArguments(args, Verbs)
			.WithParsed<BaseVerb>(task => task.Run());
	}

	private static Type[] LoadVerbs() => [.. Assembly.GetExecutingAssembly().GetTypes().Where(t => t.GetCustomAttribute<VerbAttribute>() != null)];
}
