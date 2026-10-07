// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.FileDeduplicator.Verbs;

using CommandLine;

using DustInTheWind.ConsoleTools.Controls.Menus;

using ktsu.Semantics.Paths;
using ktsu.Semantics.Strings;

internal abstract class BaseVerb : ICommand
{
	/// <summary>
	/// Gets or sets the path given with <c>-p</c>, or <see langword="null"/> when none was given and the
	/// verb should ask for one. An explicit <c>-p .</c> means the current directory, so <c>"."</c>
	/// cannot double as the "not supplied" marker.
	/// </summary>
	[Option('p', "path", Required = false, HelpText = "The root path to scan for files.")]
	public string? PathString { get; set; }

	public abstract bool IsActive { get; }

	internal AbsoluteDirectoryPath Path => System.IO.Path.GetFullPath(PathString ?? ".").As<AbsoluteDirectoryPath>();

	/// <summary>
	/// Runs the verb.
	/// </summary>
	/// <returns>The process exit code: 0 on success, 1 when the run failed or the user aborted.</returns>
	public abstract int Run();

	/// <summary>
	/// Gets or sets the exit code of the current run. It starts at 0, and a verb sets it to 1 when the
	/// run fails or the user aborts.
	/// </summary>
	internal int ExitCode { get; set; }

	/// <summary>
	/// Gets whether the verb works on the directory named by <see cref="Path"/>, which then has to
	/// exist before the verb runs.
	/// </summary>
	internal virtual bool ScansADirectory => true;

	internal virtual bool ValidateArgs() => true;

	public void Execute() => _ = Run();
}

internal abstract class BaseVerb<T> : BaseVerb where T : BaseVerb<T>
{
	private bool isActive = true;
	public override bool IsActive => isActive;

	public override int Run()
	{
		// The interactive menu runs the same instance every time its item is chosen, so the path is
		// put back on every exit, early returns and exceptions included. Otherwise the next run skips
		// the prompt and works on the previous directory.
		try
		{
			ExitCode = 0;
			if (!ValidateArgs() || (ScansADirectory && (!IsRepresentablePath() || !FileScanner.IsScannableDirectory(Path))))
			{
				return 1;
			}

			isActive = false;
			Run((T)this);
			return ExitCode;
		}
		finally
		{
			isActive = true;
			PathString = null;
		}
	}

	internal abstract void Run(T options);

	/// <summary>
	/// Checks that the root given by <see cref="BaseVerb.PathString"/> converts to an
	/// <see cref="AbsoluteDirectoryPath"/>, and says why in one line when it does not.
	/// </summary>
	/// <remarks>
	/// The conversion rejects characters and lengths the file system accepts, such as <c>|</c> in a
	/// Unix directory name or a path over 256 characters, and the exception would otherwise escape
	/// <see cref="Run()"/> as a stack trace.
	/// </remarks>
	/// <returns><see langword="false"/> when the root cannot be represented.</returns>
	private bool IsRepresentablePath()
	{
		try
		{
			_ = Path;
			return true;
		}
		catch (ArgumentException ex)
		{
			Console.WriteLine($"Cannot scan {PathString}: {ex.Message}");
			return false;
		}
	}
}
